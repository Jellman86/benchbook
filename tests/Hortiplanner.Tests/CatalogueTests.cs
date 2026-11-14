using Hortiplanner.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Hortiplanner.Tests;

// Tests share one database, so each uses names of its own.
public sealed class CatalogueTests(PostgresFixture database)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static string Unique(string name) => $"{name} {Guid.NewGuid():N}";

    private async Task SaveAsync(params object[] entities)
    {
        await using var db = database.CreateContext();
        db.AddRange(entities);
        await db.SaveChangesAsync(Cancellation);
    }

    private async Task<PostgresException> AssertRejectedAsync(params object[] entities)
    {
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(entities));
        return Assert.IsType<PostgresException>(exception.InnerException);
    }

    private static Location Site() => new() { Name = Unique("Croft"), Kind = LocationKind.Site };

    private static Location Greenhouse(Location site, string? name = null) =>
        new() { Name = name ?? Unique("Greenhouse"), Kind = LocationKind.Greenhouse, ParentId = site.Id };

    [Fact]
    public async Task A_plant_is_stored_with_its_price_for_each_pot_size()
    {
        var potSize = new PotSize { Name = Unique("9cm") };
        var plant = new Plant { CommonName = Unique("Lavender"), Variety = "Hidcote", XylellaHost = true };
        plant.Prices.Add(new PlantPrice { PotSizeId = potSize.Id, Price = 4.50m });

        await SaveAsync(potSize, plant);

        await using var db = database.CreateContext();
        var stored = await db.Plants.Include(p => p.Prices).SingleAsync(p => p.Id == plant.Id, Cancellation);
        Assert.True(stored.XylellaHost);
        Assert.Equal(4.50m, Assert.Single(stored.Prices).Price);
    }

    [Fact]
    public async Task A_plant_cannot_be_added_twice_even_with_different_capitals()
    {
        var name = Unique("lavender");
        await SaveAsync(new Plant { CommonName = name, Variety = "Hidcote" });

        var exception = await AssertRejectedAsync(new Plant { CommonName = name.ToUpperInvariant(), Variety = "HIDCOTE" });

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    }

    [Fact]
    public async Task A_plant_without_a_variety_cannot_be_added_twice()
    {
        var name = Unique("Rosemary");
        await SaveAsync(new Plant { CommonName = name });

        var exception = await AssertRejectedAsync(new Plant { CommonName = name });

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    }

    [Fact]
    public async Task Different_varieties_of_a_plant_can_be_added()
    {
        var name = Unique("Lavender");

        await SaveAsync(new Plant { CommonName = name, Variety = "Hidcote" }, new Plant { CommonName = name, Variety = "Munstead" });

        await using var db = database.CreateContext();
        Assert.Equal(2, await db.Plants.CountAsync(p => p.CommonName == name, Cancellation));
    }

    [Fact]
    public async Task A_negative_price_is_refused()
    {
        var potSize = new PotSize { Name = Unique("1L") };
        var plant = new Plant { CommonName = Unique("Salvia") };
        plant.Prices.Add(new PlantPrice { PotSizeId = potSize.Id, Price = -1m });

        var exception = await AssertRejectedAsync(potSize, plant);

        Assert.Equal("ck_plant_prices_price", exception.ConstraintName);
    }

    [Fact]
    public async Task A_pot_size_in_use_cannot_be_deleted()
    {
        var potSize = new PotSize { Name = Unique("2L") };
        var plant = new Plant { CommonName = Unique("Geum") };
        plant.Prices.Add(new PlantPrice { PotSizeId = potSize.Id, Price = 6m });
        await SaveAsync(potSize, plant);
        await using var db = database.CreateContext();

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            db.PotSizes.Where(p => p.Id == potSize.Id).ExecuteDeleteAsync(Cancellation));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, exception.SqlState);
    }

    [Fact]
    public async Task A_supplier_cannot_be_added_twice()
    {
        var name = Unique("Hillside Plants");
        await SaveAsync(new Supplier { Name = name, TypicalLeadTimeDays = 7 });

        var exception = await AssertRejectedAsync(new Supplier { Name = name.ToUpperInvariant() });

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    }

    [Fact]
    public async Task The_same_bench_name_can_be_used_in_different_greenhouses()
    {
        var site = Site();
        var first = Greenhouse(site);
        var second = Greenhouse(site);

        await SaveAsync(
            site,
            first,
            second,
            new Location { Name = "Bench 1", Kind = LocationKind.Bench, ParentId = first.Id },
            new Location { Name = "Bench 1", Kind = LocationKind.Bench, ParentId = second.Id });

        await using var db = database.CreateContext();
        Assert.Equal(2, await db.Locations.CountAsync(l => l.ParentId == first.Id || l.ParentId == second.Id, Cancellation));
    }

    [Fact]
    public async Task A_greenhouse_name_cannot_be_repeated_on_a_site()
    {
        var site = Site();
        var name = Unique("Greenhouse");
        await SaveAsync(site, Greenhouse(site, name));

        var exception = await AssertRejectedAsync(Greenhouse(site, name));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    }

    [Fact]
    public async Task Only_a_site_can_have_no_parent()
    {
        var exception = await AssertRejectedAsync(new Location { Name = Unique("Bench"), Kind = LocationKind.Bench });

        Assert.Equal("ck_locations_parent", exception.ConstraintName);
    }

    [Fact]
    public async Task A_site_cannot_have_a_parent()
    {
        var site = Site();
        await SaveAsync(site);

        var exception = await AssertRejectedAsync(new Location { Name = Unique("Annex"), Kind = LocationKind.Site, ParentId = site.Id });

        Assert.Equal("ck_locations_parent", exception.ConstraintName);
    }

    [Fact]
    public async Task A_location_with_children_cannot_be_deleted()
    {
        var site = Site();
        await SaveAsync(site, Greenhouse(site));
        await using var db = database.CreateContext();

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Locations.Where(l => l.Id == site.Id).ExecuteDeleteAsync(Cancellation));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, exception.SqlState);
    }

    [Fact]
    public async Task An_archived_plant_stays_in_the_catalogue()
    {
        var plant = new Plant { CommonName = Unique("Aster"), ArchivedAt = DateTimeOffset.UtcNow };

        await SaveAsync(plant);

        await using var db = database.CreateContext();
        Assert.NotNull((await db.Plants.SingleAsync(p => p.Id == plant.Id, Cancellation)).ArchivedAt);
    }
}
