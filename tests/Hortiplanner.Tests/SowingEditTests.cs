using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using static Hortiplanner.Tests.AccountHarness;

namespace Hortiplanner.Tests;

/// <summary>Seed recorded with a number can be recorded as weighed or a packet instead, and back, while nothing else is recorded.</summary>
public sealed class SowingEditTests(PostgresFixture database)
{
    private static readonly DateTimeOffset Sown = DateTimeOffset.UtcNow.AddHours(-2);

    private static async Task<(Guid Plant, Guid Batch, Guid Tray, string Label)> SowCountedAsync(TwoPersonNursery nursery)
    {
        var plant = Json(await PostForTextAsync(nursery.Owner, "/api/plants", new { commonName = "Lavender", xylellaHost = false, protectedVariety = false })).GetProperty("id").GetGuid();
        var started = Json(await PostForTextAsync(nursery.Staff, "/api/batches", new { eventId = Guid.NewGuid(), occurredAt = Sown, plantId = plant, quantity = 26, source = "SeedLot", originCountry = "GB", containerKind = "Tray" }));
        return (plant, started.GetProperty("batchId").GetGuid(), started.GetProperty("containerId").GetGuid(), started.GetProperty("labelCode").GetString()!);
    }

    private static async Task<HttpResponseMessage> EditAsync(HttpClient client, Guid batch, Guid plant, string sownAs, decimal? sownAmount = null, int? quantity = null) =>
        await client.PutAsJsonAsync(
            Route($"/api/batches/{batch}"),
            new { plantId = plant, source = "SeedLot", originCountry = "GB", sownAs, sownAmount, quantity },
            Cancellation);

    private static async Task<JsonElement> LineAsync(HttpClient client, string label) =>
        (await client.GetFromJsonAsync<JsonElement>(Route($"/api/containers/by-label/{label}"), Cancellation)).GetProperty("contents")[0];

    [Fact]
    public async Task Counted_seed_can_be_recorded_as_weighed_instead_and_back_while_nothing_else_is_recorded()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (plant, batch, _, label) = await SowCountedAsync(nursery);

        using (var weighed = await EditAsync(nursery.Staff, batch, plant, "Grams", sownAmount: 2.5m))
        {
            Assert.Equal(HttpStatusCode.NoContent, weighed.StatusCode);
        }

        var line = await LineAsync(nursery.Staff, label);
        Assert.Equal((0, true), (line.GetProperty("quantity").GetInt32(), line.GetProperty("notCounted").GetBoolean()));
        var details = await nursery.Staff.GetFromJsonAsync<JsonElement>(Route($"/api/batches/{batch}"), Cancellation);
        Assert.Equal("Grams", details.GetProperty("sownAs").GetString());
        Assert.Equal(2.5m, details.GetProperty("sownAmount").GetDecimal());

        // Back to counted needs the number.
        using (var noNumber = await EditAsync(nursery.Staff, batch, plant, "Counted"))
        {
            Assert.Equal(HttpStatusCode.Conflict, noNumber.StatusCode);
        }

        using (var counted = await EditAsync(nursery.Staff, batch, plant, "Counted", quantity: 30))
        {
            Assert.Equal(HttpStatusCode.NoContent, counted.StatusCode);
        }

        line = await LineAsync(nursery.Staff, label);
        Assert.Equal((30, false), (line.GetProperty("quantity").GetInt32(), line.GetProperty("notCounted").GetBoolean()));
    }

    [Fact]
    public async Task A_tray_moved_after_sowing_can_still_be_recorded_as_about_so_many_and_stays_where_it_went()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (plant, batch, tray, label) = await SowCountedAsync(nursery);
        var site = Json(await PostForTextAsync(nursery.Owner, "/api/locations", new { name = "Nursery", kind = "Site" })).GetProperty("id").GetGuid();
        var house = Json(await PostForTextAsync(nursery.Owner, "/api/locations", new { name = "Greenhouse 2", kind = "Greenhouse", parentId = site })).GetProperty("id").GetGuid();
        await PostForTextAsync(nursery.Staff, $"/api/containers/{tray}/move", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow.AddMinutes(-1), locationId = house });
        await PostForTextAsync(nursery.Staff, $"/api/containers/{tray}/notes", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow, note = "Watered in" });

        Assert.True((await nursery.Staff.GetFromJsonAsync<JsonElement>(Route($"/api/batches/{batch}"), Cancellation)).GetProperty("startCanChange").GetBoolean());

        using (var about = await EditAsync(nursery.Staff, batch, plant, "About", sownAmount: 1250))
        {
            Assert.Equal(HttpStatusCode.NoContent, about.StatusCode);
        }

        var after = await nursery.Staff.GetFromJsonAsync<JsonElement>(Route($"/api/containers/by-label/{label}"), Cancellation);
        Assert.True(after.GetProperty("contents")[0].GetProperty("notCounted").GetBoolean());
        Assert.Equal("Greenhouse 2", after.GetProperty("locationName").GetString());

        // Moving the day later would put the sowing after the move, so that still needs nothing recorded since.
        using var later = await nursery.Staff.PutAsJsonAsync(
            Route($"/api/batches/{batch}"),
            new { plantId = plant, source = "SeedLot", originCountry = "GB", sownAs = "About", sownAmount = 1250, startedAt = DateTimeOffset.UtcNow },
            Cancellation);
        Assert.Equal(HttpStatusCode.Conflict, later.StatusCode);
    }

    private static async Task<(Guid Plant, Guid Batch, Guid Tray, string Label, Guid First, Guid Second, Guid Third)> SowAtAPlaceAsync(TwoPersonNursery nursery)
    {
        var site = Json(await PostForTextAsync(nursery.Owner, "/api/locations", new { name = "Nursery", kind = "Site" })).GetProperty("id").GetGuid();
        var places = new List<Guid>();

        foreach (var name in new[] { "Greenhouse 1", "Greenhouse 2", "Greenhouse 3" })
        {
            places.Add(Json(await PostForTextAsync(nursery.Owner, "/api/locations", new { name, kind = "Greenhouse", parentId = site })).GetProperty("id").GetGuid());
        }

        var plant = Json(await PostForTextAsync(nursery.Owner, "/api/plants", new { commonName = "Lavender", xylellaHost = false, protectedVariety = false })).GetProperty("id").GetGuid();
        var started = Json(await PostForTextAsync(nursery.Staff, "/api/batches", new { eventId = Guid.NewGuid(), occurredAt = Sown, plantId = plant, quantity = 26, source = "SeedLot", originCountry = "GB", containerKind = "Tray", locationId = places[0] }));
        return (plant, started.GetProperty("batchId").GetGuid(), started.GetProperty("containerId").GetGuid(), started.GetProperty("labelCode").GetString()!, places[0], places[1], places[2]);
    }

    private static async Task<string?> WhereAsync(HttpClient client, string label) =>
        (await client.GetFromJsonAsync<JsonElement>(Route($"/api/containers/by-label/{label}"), Cancellation)).GetProperty("locationName").GetString();

    [Fact]
    public async Task A_move_at_the_very_moment_of_sowing_still_locks_the_start_so_the_tray_stays_put()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (plant, batch, tray, label, _, second, _) = await SowAtAPlaceAsync(nursery);
        await PostForTextAsync(nursery.Staff, $"/api/containers/{tray}/move", new { eventId = Guid.NewGuid(), occurredAt = Sown, locationId = second });

        using var refused = await EditAsync(nursery.Staff, batch, plant, "About", sownAmount: 500);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("Greenhouse 2", await WhereAsync(nursery.Staff, label));
    }

    [Fact]
    public async Task A_corrected_move_after_sowing_leaves_the_start_open_and_the_tray_where_the_correction_put_it()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (plant, batch, tray, label, _, second, third) = await SowAtAPlaceAsync(nursery);
        var move = Guid.NewGuid();
        await PostForTextAsync(nursery.Staff, $"/api/containers/{tray}/move", new { eventId = move, occurredAt = Sown.AddMinutes(30), locationId = second });
        await PostForTextAsync(nursery.Staff, $"/api/events/{move}/correct", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow, locationId = third });
        Assert.Equal("Greenhouse 3", await WhereAsync(nursery.Staff, label));

        using (var counted = await EditAsync(nursery.Staff, batch, plant, "Counted", quantity: 30))
        {
            Assert.Equal(HttpStatusCode.NoContent, counted.StatusCode);
        }

        using (var about = await EditAsync(nursery.Staff, batch, plant, "About", sownAmount: 500))
        {
            Assert.Equal(HttpStatusCode.NoContent, about.StatusCode);
        }

        Assert.Equal("Greenhouse 3", await WhereAsync(nursery.Staff, label));
    }

    [Fact]
    public async Task Once_other_work_is_recorded_counted_seed_stays_counted()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (plant, batch, tray, label) = await SowCountedAsync(nursery);
        await PostForTextAsync(nursery.Staff, $"/api/containers/{tray}/treatments", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow, product = "Neem oil", dose = "5ml/L", area = "Tray" });

        using var refused = await EditAsync(nursery.Staff, batch, plant, "Unknown");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("CountInstead", Json(await refused.Content.ReadAsStringAsync(Cancellation)).GetProperty("problem").GetString());
        Assert.Equal(26, (await LineAsync(nursery.Staff, label)).GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task Editing_older_seed_batches_records_no_change_to_how_they_were_sown()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (plant, batch, _, _) = await SowCountedAsync(nursery);

        using var edited = await nursery.Staff.PutAsJsonAsync(
            Route($"/api/batches/{batch}"),
            new { plantId = plant, source = "SeedLot", originCountry = "GB", sourceReference = "Lot 9", sownAs = "Counted" },
            Cancellation);
        Assert.Equal(HttpStatusCode.NoContent, edited.StatusCode);

        var trace = await nursery.Owner.GetFromJsonAsync<JsonElement>(Route($"/api/batches/{batch}/trace"), Cancellation);
        var fields = trace.GetProperty("corrections").EnumerateArray().Select(change => change.GetProperty("field").GetString()).ToList();
        Assert.DoesNotContain("Sown", fields, StringComparer.Ordinal);
        Assert.Contains("Lot or reference", fields, StringComparer.Ordinal);
    }
}
