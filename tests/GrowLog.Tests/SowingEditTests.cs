using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using static GrowLog.Tests.AccountHarness;

namespace GrowLog.Tests;

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
