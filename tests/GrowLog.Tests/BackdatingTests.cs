using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using static GrowLog.Tests.AccountHarness;

namespace GrowLog.Tests;

public sealed class BackdatingTests(PostgresFixture database)
{
    [Fact]
    public async Task Work_written_up_later_keeps_its_day_but_never_comes_before_the_tray_started()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = Json(await PostForTextAsync(nursery.Owner, "/api/plants", new { commonName = "Cosmos", xylellaHost = false, protectedVariety = false })).GetProperty("id").GetGuid();
        var sown = DateTimeOffset.UtcNow.AddDays(-20);
        var started = Json(await PostForTextAsync(nursery.Staff, "/api/batches", new { eventId = Guid.NewGuid(), occurredAt = sown, plantId = plant, quantity = 40, source = "SeedLot", originCountry = "GB", containerKind = "Tray" }));
        var tray = started.GetProperty("containerId").GetGuid();
        var batch = started.GetProperty("batchId").GetGuid();

        // Germinated a week after sowing, and a loss two days later, both recorded today.
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(nursery.Staff, $"/api/containers/{tray}/germinated", new { eventId = Guid.NewGuid(), occurredAt = sown.AddDays(7) }));
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(nursery.Staff, $"/api/containers/{tray}/losses", new { eventId = Guid.NewGuid(), occurredAt = sown.AddDays(9), batchId = batch, quantity = 4, reason = "DampingOff" }));

        var history = (await nursery.Staff.GetFromJsonAsync<JsonElement>(Route($"/api/containers/{tray}/history"), Cancellation)).EnumerateArray().ToList();
        var germinated = history.Single(entry => string.Equals(entry.GetProperty("type").GetString(), "Germinated", StringComparison.Ordinal));
        Assert.Equal(sown.AddDays(7).ToUnixTimeSeconds(), germinated.GetProperty("occurredAt").GetDateTimeOffset().ToUnixTimeSeconds());

        // A day before the sowing is a mistake.
        using var early = await PostAsync(nursery.Staff, $"/api/containers/{tray}/losses", new { eventId = Guid.NewGuid(), occurredAt = sown.AddDays(-2), batchId = batch, quantity = 1, reason = "Pest" });
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Equal("BeforeStart", Json(await early.Content.ReadAsStringAsync(Cancellation)).GetProperty("problem").GetString());
        Assert.Equal(HttpStatusCode.Conflict, await PostStatusAsync(nursery.Staff, $"/api/containers/{tray}/notes", new { eventId = Guid.NewGuid(), occurredAt = sown.AddDays(-1), note = "Too early" }));
    }
}
