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

    [Fact]
    public async Task Work_written_up_for_the_day_a_tray_was_started_goes_just_after_the_start()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = Json(await PostForTextAsync(nursery.Owner, "/api/plants", new { commonName = "Cosmos", xylellaHost = false, protectedVariety = false })).GetProperty("id").GetGuid();
        var day = DateTimeOffset.UtcNow.UtcDateTime.Date.AddDays(-3);
        var sown = new DateTimeOffset(day.AddHours(15), TimeSpan.Zero);
        var started = Json(await PostForTextAsync(nursery.Staff, "/api/batches", new { eventId = Guid.NewGuid(), occurredAt = sown, plantId = plant, quantity = 40, source = "SeedLot", originCountry = "GB", containerKind = "Tray" }));
        var tray = started.GetProperty("containerId").GetGuid();
        var batch = started.GetProperty("batchId").GetGuid();

        // Written up the next morning for "yesterday", which the phone times at this morning's hour.
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(nursery.Staff, $"/api/containers/{tray}/losses", new { eventId = Guid.NewGuid(), occurredAt = new DateTimeOffset(day.AddHours(9), TimeSpan.Zero), batchId = batch, quantity = 2, reason = "Pest" }));

        var history = (await nursery.Staff.GetFromJsonAsync<JsonElement>(Route($"/api/containers/{tray}/history"), Cancellation)).EnumerateArray().ToList();
        var lost = history.Single(entry => string.Equals(entry.GetProperty("type").GetString(), "Lost", StringComparison.Ordinal));
        Assert.Equal(sown.AddTicks(10), lost.GetProperty("occurredAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task A_loss_dated_before_a_later_count_says_the_count_still_stands()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = Json(await PostForTextAsync(nursery.Owner, "/api/plants", new { commonName = "Cosmos", xylellaHost = false, protectedVariety = false })).GetProperty("id").GetGuid();
        var sown = DateTimeOffset.UtcNow.AddDays(-5);
        var started = Json(await PostForTextAsync(nursery.Staff, "/api/batches", new { eventId = Guid.NewGuid(), occurredAt = sown, plantId = plant, quantity = 40, source = "SeedLot", originCountry = "GB", containerKind = "Tray" }));
        var tray = started.GetProperty("containerId").GetGuid();
        var batch = started.GetProperty("batchId").GetGuid();
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(nursery.Staff, $"/api/containers/{tray}/count", new { eventId = Guid.NewGuid(), occurredAt = sown.AddDays(3), batchId = batch, counted = 30, rough = false }));

        var before = Json(await PostForTextAsync(nursery.Staff, $"/api/containers/{tray}/losses", new { eventId = Guid.NewGuid(), occurredAt = sown.AddDays(2), batchId = batch, quantity = 4, reason = "Pest" }));
        Assert.True(before.GetProperty("countedSince").GetBoolean());
        var after = Json(await PostForTextAsync(nursery.Staff, $"/api/containers/{tray}/losses", new { eventId = Guid.NewGuid(), occurredAt = sown.AddDays(4), batchId = batch, quantity = 2, reason = "Pest" }));
        Assert.False(after.GetProperty("countedSince").GetBoolean());

        // Pricking out written up for before the count says so too.
        var pricked = Json(await PostForTextAsync(nursery.Staff, $"/api/containers/{tray}/transfer", new { eventId = Guid.NewGuid(), occurredAt = sown.AddDays(2.5), type = "PrickedOut", batchId = batch, quantity = 10, containerKind = "Tray" }));
        Assert.True(pricked.GetProperty("countedSince").GetBoolean());
    }

    [Fact]
    public async Task Nudged_work_stays_before_a_nearby_count_and_on_its_chosen_day()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = Json(await PostForTextAsync(nursery.Owner, "/api/plants", new { commonName = "Cosmos" })).GetProperty("id").GetGuid();
        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-3).AddHours(23).AddMinutes(59).AddSeconds(50), TimeSpan.Zero);
        var started = Json(await PostForTextAsync(nursery.Staff, "/api/batches", new { eventId = Guid.NewGuid(), occurredAt = start, plantId = plant, quantity = 40, source = "SeedLot", originCountry = "GB", containerKind = "Tray" }));
        var tray = started.GetProperty("containerId").GetGuid();
        var batch = started.GetProperty("batchId").GetGuid();
        await PostForTextAsync(nursery.Staff, $"/api/containers/{tray}/count", new { eventId = Guid.NewGuid(), occurredAt = start.AddSeconds(5), batchId = batch, counted = 30 });
        var loss = Json(await PostForTextAsync(nursery.Staff, $"/api/containers/{tray}/losses", new { eventId = Guid.NewGuid(), occurredAt = start.AddHours(-12), batchId = batch, quantity = 2, reason = "Pest" }));
        Assert.True(loss.GetProperty("countedSince").GetBoolean());
        var history = (await nursery.Staff.GetFromJsonAsync<JsonElement>(Route($"/api/containers/{tray}/history"), Cancellation)).EnumerateArray();
        var lost = history.Single(entry => string.Equals(entry.GetProperty("type").GetString(), "Lost", StringComparison.Ordinal)).GetProperty("occurredAt").GetDateTimeOffset();
        Assert.True(lost > start && lost < start.AddSeconds(5));
        Assert.Equal(start.Date, lost.Date);
    }

}
