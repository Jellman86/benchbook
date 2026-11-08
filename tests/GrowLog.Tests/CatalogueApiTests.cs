using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrowLog.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using static GrowLog.Tests.AccountHarness;

namespace GrowLog.Tests;

public sealed class CatalogueApiTests(PostgresFixture database)
{
    private sealed class Nursery : IAsyncDisposable
    {
        private readonly TestAuthenticator ownerKey = new();
        private readonly TestAuthenticator staffKey = new();

        private Nursery(TestApp app)
        {
            App = app;
            Owner = Client(app);
            Staff = Client(app);
        }

        public TestApp App { get; }

        public HttpClient Owner { get; }

        public HttpClient Staff { get; }

        public static async Task<Nursery> CreateAsync(PostgresFixture database)
        {
            var nursery = new Nursery(await CreateAppAsync(database));
            await SetUpOwnerAsync(nursery.Owner, nursery.ownerKey);

            var userId = Json(await PostForTextAsync(nursery.Owner, "/api/staff", new { name = "Alex Moss", pin = "4821" })).GetProperty("id").GetGuid();
            var token = Json(await PostForTextAsync(nursery.Owner, $"/api/staff/{userId}/invite")).GetProperty("token").GetString();
            var options = await PostForTextAsync(nursery.Staff, "/api/invites/passkey-options", new { userId, token });
            Assert.Equal(
                HttpStatusCode.NoContent,
                await PostStatusAsync(nursery.Staff, "/api/invites/complete", new { userId, token, credential = Json(nursery.staffKey.Register(options)) }));

            return nursery;
        }

        public async Task<Guid> OwnerIdAsync()
        {
            var staff = await Owner.GetFromJsonAsync<JsonElement>(Route("/api/staff"), Cancellation);
            return staff.EnumerateArray().Single(s => string.Equals(s.GetProperty("name").GetString(), "Sam Fielding", StringComparison.Ordinal)).GetProperty("id").GetGuid();
        }

        public async ValueTask DisposeAsync()
        {
            Owner.Dispose();
            Staff.Dispose();
            ownerKey.Dispose();
            staffKey.Dispose();
            await App.DisposeAsync();
        }
    }

    private static object PlantBody(string commonName, object[]? prices = null) => new
    {
        commonName,
        variety = "Hidcote",
        xylellaHost = true,
        protectedVariety = false,
        prices = prices ?? [],
    };

    private static async Task<Guid> AddAsync(HttpClient client, string path, object body) =>
        Json(await PostForTextAsync(client, path, body)).GetProperty("id").GetGuid();

    private static Task<JsonElement> ListAsync(HttpClient client, string path) =>
        client.GetFromJsonAsync<JsonElement>(Route(path), Cancellation);

    private static async Task<HttpStatusCode> PutStatusAsync(HttpClient client, string path, object body)
    {
        using var response = await client.PutAsJsonAsync(Route(path), body, Cancellation);
        return response.StatusCode;
    }

    private static List<Guid> Ids(JsonElement list) =>
        [.. list.EnumerateArray().Select(entry => entry.GetProperty("id").GetGuid())];

    [Fact]
    public async Task A_plant_is_added_with_its_prices_and_listed()
    {
        await using var nursery = await Nursery.CreateAsync(database);
        var potSize = await AddAsync(nursery.Owner, "/api/pot-sizes", new { name = "9cm" });

        await AddAsync(nursery.Owner, "/api/plants", PlantBody("Lavender", [new { potSizeId = potSize, price = 4.5m }]));

        var plant = (await ListAsync(nursery.Owner, "/api/plants")).EnumerateArray().Single();
        Assert.Equal("Lavender", plant.GetProperty("commonName").GetString());
        Assert.True(plant.GetProperty("xylellaHost").GetBoolean());
        Assert.Equal(4.5m, plant.GetProperty("prices")[0].GetProperty("price").GetDecimal());
    }

    [Fact]
    public async Task Someone_not_signed_in_cannot_see_or_change_the_catalogue()
    {
        await using var nursery = await Nursery.CreateAsync(database);
        using var stranger = Client(nursery.App);

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(stranger, "/api/plants"));
        Assert.Equal(HttpStatusCode.Unauthorized, await PostStatusAsync(stranger, "/api/plants", PlantBody("Lavender")));
    }

    [Fact]
    public async Task Staff_can_add_entries_but_only_the_owner_can_change_or_archive_them()
    {
        await using var nursery = await Nursery.CreateAsync(database);

        var id = await AddAsync(nursery.Staff, "/api/plants", PlantBody("Lavender"));

        Assert.Equal(HttpStatusCode.Forbidden, await PutStatusAsync(nursery.Staff, $"/api/plants/{id}", PlantBody("Renamed")));
        Assert.Equal(HttpStatusCode.Forbidden, await PostStatusAsync(nursery.Staff, $"/api/plants/{id}/archive"));
        Assert.Equal(HttpStatusCode.NoContent, await PutStatusAsync(nursery.Owner, $"/api/plants/{id}", PlantBody("Renamed")));
    }

    [Fact]
    public async Task Adding_a_duplicate_answers_with_a_conflict()
    {
        await using var nursery = await Nursery.CreateAsync(database);
        await AddAsync(nursery.Owner, "/api/plants", PlantBody("Lavender"));

        Assert.Equal(HttpStatusCode.Conflict, await PostStatusAsync(nursery.Owner, "/api/plants", PlantBody("LAVENDER")));
    }

    [Fact]
    public async Task Invalid_entries_are_refused()
    {
        await using var nursery = await Nursery.CreateAsync(database);
        var potSize = await AddAsync(nursery.Owner, "/api/pot-sizes", new { name = "9cm" });

        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(nursery.Owner, "/api/plants", PlantBody("   ")));
        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(nursery.Owner, "/api/plants", PlantBody("Geum", [new { potSizeId = potSize, price = -1m }])));
        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(nursery.Owner, "/api/plants", PlantBody("Geum", [new { potSizeId = Guid.NewGuid(), price = 1m }])));
        Assert.Equal(
            HttpStatusCode.BadRequest,
            await PostStatusAsync(nursery.Owner, "/api/plants", PlantBody("Geum", [new { potSizeId = potSize, price = 1m }, new { potSizeId = potSize, price = 2m }])));
    }

    [Fact]
    public async Task Updating_a_plant_replaces_its_prices()
    {
        await using var nursery = await Nursery.CreateAsync(database);
        var small = await AddAsync(nursery.Owner, "/api/pot-sizes", new { name = "9cm" });
        var large = await AddAsync(nursery.Owner, "/api/pot-sizes", new { name = "2L" });
        var id = await AddAsync(nursery.Owner, "/api/plants", PlantBody("Lavender", [new { potSizeId = small, price = 4m }]));

        Assert.Equal(HttpStatusCode.NoContent, await PutStatusAsync(nursery.Owner, $"/api/plants/{id}", PlantBody("Lavender", [new { potSizeId = large, price = 9m }])));

        var price = (await ListAsync(nursery.Owner, "/api/plants")).EnumerateArray().Single().GetProperty("prices").EnumerateArray().Single();
        Assert.Equal(large, price.GetProperty("potSizeId").GetGuid());
        Assert.Equal(9m, price.GetProperty("price").GetDecimal());
    }

    [Fact]
    public async Task An_archived_entry_leaves_the_list_and_can_be_restored()
    {
        await using var nursery = await Nursery.CreateAsync(database);
        var id = await AddAsync(nursery.Owner, "/api/suppliers", new { name = "Hillside Plants" });

        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(nursery.Owner, $"/api/suppliers/{id}/archive"));
        Assert.Empty(Ids(await ListAsync(nursery.Owner, "/api/suppliers")));
        Assert.Equal([id], Ids(await ListAsync(nursery.Owner, "/api/suppliers?includeArchived=true")));

        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(nursery.Owner, $"/api/suppliers/{id}/restore"));
        Assert.Equal([id], Ids(await ListAsync(nursery.Owner, "/api/suppliers")));
    }

    [Fact]
    public async Task Lists_show_what_was_used_most_recently_first_then_by_name()
    {
        await using var nursery = await Nursery.CreateAsync(database);
        var aster = await AddAsync(nursery.Owner, "/api/plants", PlantBody("Aster"));
        var geum = await AddAsync(nursery.Owner, "/api/plants", PlantBody("Geum"));
        var salvia = await AddAsync(nursery.Owner, "/api/plants", PlantBody("Salvia"));
        var small = await AddAsync(nursery.Owner, "/api/pot-sizes", new { name = "9cm" });
        var large = await AddAsync(nursery.Owner, "/api/pot-sizes", new { name = "2L" });
        var owner = await nursery.OwnerIdAsync();

        await using var scope = nursery.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GrowLogDbContext>();
        var batch = new Batch { PlantId = salvia, TraceabilityCode = "B-1", Source = BatchSource.SeedLot, OriginCountry = "GB" };
        var container = new Container { LabelCode = "C-1", Kind = ContainerKind.Tray };
        db.AddRange(batch, container);
        await db.SaveChangesAsync(Cancellation);

        var sown = new Event { Id = Guid.NewGuid(), Type = EventType.Sown, OccurredAt = DateTimeOffset.UtcNow, RecordedBy = owner, ContainerId = container.Id, PotSizeId = small };
        sown.Lines.Add(new EventLine { ContainerId = container.Id, BatchId = batch.Id, QuantityChange = 5 });
        Assert.Equal(AppendResult.Stored, await scope.ServiceProvider.GetRequiredService<EventStore>().AppendAsync(sown, Cancellation));

        Assert.Equal([salvia, aster, geum], Ids(await ListAsync(nursery.Staff, "/api/plants")));
        Assert.Equal([small, large], Ids(await ListAsync(nursery.Staff, "/api/pot-sizes")));
    }

    [Theory]
    [InlineData("Greenhouse", false)]
    [InlineData("Bench", false)]
    [InlineData("Site", true)]
    public async Task A_location_must_sit_at_the_right_level(string kind, bool underSite)
    {
        await using var nursery = await Nursery.CreateAsync(database);
        var site = await AddAsync(nursery.Owner, "/api/locations", new { name = "Croft", kind = "Site" });

        var status = await PostStatusAsync(nursery.Owner, "/api/locations", new { name = "Wrong", kind, parentId = underSite ? site : (Guid?)null });

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task A_bench_cannot_sit_directly_on_a_site()
    {
        await using var nursery = await Nursery.CreateAsync(database);
        var site = await AddAsync(nursery.Owner, "/api/locations", new { name = "Croft", kind = "Site" });

        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(nursery.Owner, "/api/locations", new { name = "Bench 1", kind = "Bench", parentId = site }));
    }

    [Fact]
    public async Task Locations_build_from_site_to_greenhouse_to_bench()
    {
        await using var nursery = await Nursery.CreateAsync(database);
        var site = await AddAsync(nursery.Owner, "/api/locations", new { name = "Croft", kind = "Site" });
        var greenhouse = await AddAsync(nursery.Owner, "/api/locations", new { name = "Greenhouse 2", kind = "Greenhouse", parentId = site });
        var bench = await AddAsync(nursery.Owner, "/api/locations", new { name = "Bench 1", kind = "Bench", parentId = greenhouse });

        var listed = (await ListAsync(nursery.Staff, "/api/locations")).EnumerateArray().Single(l => l.GetProperty("id").GetGuid() == bench);

        Assert.Equal("Bench", listed.GetProperty("kind").GetString());
        Assert.Equal(greenhouse, listed.GetProperty("parentId").GetGuid());
    }

    [Fact]
    public async Task Every_change_records_who_made_it_and_what_changed()
    {
        await using var nursery = await Nursery.CreateAsync(database);
        var id = await AddAsync(nursery.Owner, "/api/plants", PlantBody("Lavender"));
        await PutStatusAsync(nursery.Owner, $"/api/plants/{id}", PlantBody("English Lavender"));
        await PostStatusAsync(nursery.Owner, $"/api/plants/{id}/archive");
        var owner = await nursery.OwnerIdAsync();

        await using var scope = nursery.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GrowLogDbContext>();
        var changes = await db.CatalogueChanges.Where(c => c.EntityKey == id.ToString()).OrderBy(c => c.Id).ToListAsync(Cancellation);

        Assert.Equal("Added,Modified,Modified", string.Join(',', changes.Select(c => c.Action)));
        Assert.All(changes, c => Assert.Equal(owner, c.ChangedBy));

        var rename = Json(changes[1].Changes).GetProperty("CommonName");
        Assert.Equal("Lavender", rename.GetProperty("before").GetString());
        Assert.Equal("English Lavender", rename.GetProperty("after").GetString());
        Assert.Single(Json(changes[1].Changes).EnumerateObject());
    }

    [Fact]
    public async Task A_suppliers_contact_details_are_not_kept_in_the_change_history()
    {
        await using var nursery = await Nursery.CreateAsync(database);
        var id = await AddAsync(nursery.Owner, "/api/suppliers", new { name = "Hillside Plants", contactName = "Jo Private", email = "jo@private.example", phone = "07700900123", address = "1 Private Lane" });

        await using var scope = nursery.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GrowLogDbContext>();
        var change = await db.CatalogueChanges.SingleAsync(c => c.EntityKey == id.ToString(), Cancellation);

        Assert.Contains("Hillside Plants", change.Changes, StringComparison.Ordinal);
        Assert.DoesNotContain("Private", change.Changes, StringComparison.Ordinal);
        Assert.DoesNotContain("07700900123", change.Changes, StringComparison.Ordinal);
        Assert.DoesNotContain("private.example", change.Changes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_change_history_cannot_be_altered()
    {
        await using var nursery = await Nursery.CreateAsync(database);
        await AddAsync(nursery.Owner, "/api/plants", PlantBody("Lavender"));

        await using var scope = nursery.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GrowLogDbContext>();

        var update = await Assert.ThrowsAsync<PostgresException>(() =>
            db.CatalogueChanges.ExecuteUpdateAsync(set => set.SetProperty(c => c.Action, "Forged"), Cancellation));
        var delete = await Assert.ThrowsAsync<PostgresException>(() => db.CatalogueChanges.ExecuteDeleteAsync(Cancellation));
        var truncate = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("TRUNCATE catalogue_changes", Cancellation));

        Assert.Contains("append-only", update.MessageText, StringComparison.Ordinal);
        Assert.Contains("append-only", delete.MessageText, StringComparison.Ordinal);
        Assert.Contains("append-only", truncate.MessageText, StringComparison.Ordinal);
    }
}
