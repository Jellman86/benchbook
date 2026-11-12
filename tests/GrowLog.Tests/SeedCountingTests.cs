using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using static GrowLog.Tests.AccountHarness;

namespace GrowLog.Tests;

public sealed class SeedCountingTests(PostgresFixture database)
{
    private static readonly DateTimeOffset Sown = DateTimeOffset.UtcNow.AddDays(-10);

    private static async Task<Guid> AddAsync(HttpClient client, string path, object body) =>
        Json(await PostForTextAsync(client, path, body)).GetProperty("id").GetGuid();

    private static async Task<(Guid Plant, Guid Batch, Guid Tray, string Label)> SowAsync(TwoPersonNursery nursery, string sownAs = "About", decimal? amount = 200)
    {
        var plant = await AddAsync(nursery.Owner, "/api/plants", new { commonName = $"Primula {Guid.NewGuid():N}", xylellaHost = false, protectedVariety = false });
        var started = Json(await PostForTextAsync(nursery.Staff, "/api/batches", new { eventId = Guid.NewGuid(), occurredAt = Sown, plantId = plant, quantity = 0, source = "SeedLot", originCountry = "GB", containerKind = "Tray", sownAs, sownAmount = amount }));
        return (plant, started.GetProperty("batchId").GetGuid(), started.GetProperty("containerId").GetGuid(), started.GetProperty("labelCode").GetString()!);
    }

    private static async Task<JsonElement> LineAsync(HttpClient client, string label) =>
        (await client.GetFromJsonAsync<JsonElement>(Route($"/api/containers/by-label/{label}"), Cancellation)).GetProperty("contents")[0];

    private static async Task<int> ContentsAsync(HttpClient client, string label) =>
        (await client.GetFromJsonAsync<JsonElement>(Route($"/api/containers/by-label/{label}"), Cancellation)).GetProperty("contents").GetArrayLength();

    private static Task<HttpStatusCode> CountAsync(HttpClient client, Guid tray, Guid batch, int counted, DateTimeOffset? at = null) =>
        PostStatusAsync(client, $"/api/containers/{tray}/count", new { eventId = Guid.NewGuid(), occurredAt = at ?? DateTimeOffset.UtcNow, batchId = batch, counted });

    private static async Task<JsonElement> PlantFiguresAsync(HttpClient owner, Guid plant) =>
        (await owner.GetFromJsonAsync<JsonElement>(Route("/api/figures"), Cancellation)).GetProperty("plants").EnumerateArray().Single(row => row.GetProperty("plantId").GetGuid() == plant);

    [Fact]
    public async Task Seed_sown_without_a_count_is_there_but_not_counted_until_someone_counts_it()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (plant, batch, tray, label) = await SowAsync(nursery);

        var line = await LineAsync(nursery.Staff, label);
        Assert.True(line.GetProperty("notCounted").GetBoolean());
        Assert.Equal(0, line.GetProperty("quantity").GetInt32());

        var active = await nursery.Staff.GetFromJsonAsync<JsonElement>(Route("/api/containers/active"), Cancellation);
        Assert.Contains(active.EnumerateArray(), container => container.GetProperty("id").GetGuid() == tray);
        var production = await nursery.Staff.GetFromJsonAsync<JsonElement>(Route("/api/production"), Cancellation);
        Assert.True(production.EnumerateArray().Single(row => row.GetProperty("containerId").GetGuid() == tray).GetProperty("notCounted").GetBoolean());

        // Germinated, then roughly how many are up.
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(nursery.Staff, $"/api/containers/{tray}/germinated", new { eventId = Guid.NewGuid(), occurredAt = Sown.AddDays(5) }));
        Assert.Equal(HttpStatusCode.Created, await CountAsync(nursery.Staff, tray, batch, 150, Sown.AddDays(5).AddMinutes(1)));

        line = await LineAsync(nursery.Staff, label);
        Assert.False(line.GetProperty("notCounted").GetBoolean());
        Assert.Equal(150, line.GetProperty("quantity").GetInt32());

        // The first count is where it starts, and germination is about 150 of about 200.
        var figures = await PlantFiguresAsync(nursery.Owner, plant);
        Assert.Equal(150, figures.GetProperty("started").GetInt32());
        Assert.Equal(0, figures.GetProperty("adjusted").GetInt32());
        Assert.Equal(0.75, figures.GetProperty("germinationRate").GetDouble(), 3);
        Assert.True(figures.GetProperty("germinationApproximate").GetBoolean());
    }

    [Fact]
    public async Task Pricking_out_of_an_uncounted_tray_counts_whats_taken_and_whats_left()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (_, batch, tray, label) = await SowAsync(nursery, "Grams", 0.5m);

        object Body(int? left) => new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow, type = "PrickedOut", batchId = batch, quantity = 60, containerKind = "Tray", leftInTray = left, countEventId = Guid.NewGuid() };

        using var unanswered = await PostAsync(nursery.Staff, $"/api/containers/{tray}/transfer", Body(null));
        Assert.Equal(HttpStatusCode.Conflict, unanswered.StatusCode);

        var moved = Json(await PostForTextAsync(nursery.Staff, $"/api/containers/{tray}/transfer", Body(20)));
        var newLabel = moved.GetProperty("labelCode").GetString()!;
        Assert.Equal(20, (await LineAsync(nursery.Staff, label)).GetProperty("quantity").GetInt32());
        Assert.Equal(60, (await LineAsync(nursery.Staff, newLabel)).GetProperty("quantity").GetInt32());

        // Undoing the move leaves the count: everything is back in the tray, counted.
        using var undo = await PostAsync(nursery.Staff, $"/api/events/{moved.GetProperty("eventId").GetGuid()}/correct", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow });
        Assert.Equal(HttpStatusCode.Created, undo.StatusCode);
        var line = await LineAsync(nursery.Staff, label);
        Assert.Equal((80, false), (line.GetProperty("quantity").GetInt32(), line.GetProperty("notCounted").GetBoolean()));
    }

    [Fact]
    public async Task Losses_before_any_count_are_all_of_them_or_some_and_all_ends_the_tray()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (plant, batch, tray, label) = await SowAsync(nursery);

        object Loss(bool? all) => new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow, batchId = batch, quantity = 0, reason = "FailedToGerminate", all };

        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(nursery.Staff, $"/api/containers/{tray}/losses", Loss(null)));
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(nursery.Staff, $"/api/containers/{tray}/losses", Loss(false)));
        Assert.True((await LineAsync(nursery.Staff, label)).GetProperty("notCounted").GetBoolean());

        var all = Json(await PostForTextAsync(nursery.Staff, $"/api/containers/{tray}/losses", Loss(true)));
        Assert.Equal(0, await ContentsAsync(nursery.Staff, label));

        // None came up: a real nought for germination.
        Assert.Equal(0, (await PlantFiguresAsync(nursery.Owner, plant)).GetProperty("germinationRate").GetDouble());

        using var undo = await PostAsync(nursery.Staff, $"/api/events/{all.GetProperty("eventId").GetGuid()}/correct", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow });
        Assert.Equal(HttpStatusCode.Created, undo.StatusCode);
        Assert.True((await LineAsync(nursery.Staff, label)).GetProperty("notCounted").GetBoolean());
    }

    [Fact]
    public async Task Seed_sown_by_weight_gives_no_germination_rate_and_a_sowing_has_no_number_to_correct()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (plant, batch, tray, _) = await SowAsync(nursery, "Grams", 0.5m);
        await PostStatusAsync(nursery.Staff, $"/api/containers/{tray}/germinated", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow.AddMinutes(-2) });
        Assert.Equal(HttpStatusCode.Created, await CountAsync(nursery.Staff, tray, batch, 300));

        var figures = await PlantFiguresAsync(nursery.Owner, plant);
        Assert.Equal(300, figures.GetProperty("started").GetInt32());
        Assert.Equal(JsonValueKind.Null, figures.GetProperty("germinationRate").ValueKind);

        var history = await nursery.Staff.GetFromJsonAsync<JsonElement>(Route($"/api/containers/{tray}/history"), Cancellation);
        var sowing = history.EnumerateArray().Single(entry => string.Equals(entry.GetProperty("type").GetString(), "Sown", StringComparison.Ordinal));
        using var refused = await PostAsync(nursery.Staff, $"/api/events/{sowing.GetProperty("eventId").GetGuid()}/correct", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow, quantity = 40 });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task Edit_batch_corrects_how_seed_was_sown_but_never_makes_up_a_number()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (plant, batch, _, _) = await SowAsync(nursery);

        object Edit(string sownAs, decimal? amount, int? quantity = null) =>
            new { plantId = plant, source = "SeedLot", originCountry = "GB", sownAs, sownAmount = amount, quantity };

        using var counted = await nursery.Staff.PutAsJsonAsync(Route($"/api/batches/{batch}"), Edit("Counted", null), Cancellation);
        Assert.Equal(HttpStatusCode.Conflict, counted.StatusCode);
        using var number = await nursery.Staff.PutAsJsonAsync(Route($"/api/batches/{batch}"), Edit("About", 200, quantity: 50), Cancellation);
        Assert.Equal(HttpStatusCode.Conflict, number.StatusCode);

        using var grams = await nursery.Staff.PutAsJsonAsync(Route($"/api/batches/{batch}"), Edit("Grams", 1.5m), Cancellation);
        Assert.Equal(HttpStatusCode.NoContent, grams.StatusCode);

        var details = await nursery.Staff.GetFromJsonAsync<JsonElement>(Route($"/api/batches/{batch}"), Cancellation);
        Assert.Equal(("Grams", 1.5m), (details.GetProperty("sownAs").GetString(), details.GetProperty("sownAmount").GetDecimal()));
        var change = Assert.Single((await nursery.Owner.GetFromJsonAsync<JsonElement>(Route($"/api/batches/{batch}/trace"), Cancellation)).GetProperty("corrections").EnumerateArray());
        Assert.Equal(("Sown", "About 200 seeds", "1.5 g"), (change.GetProperty("field").GetString(), change.GetProperty("before").GetString(), change.GetProperty("after").GetString()));
    }

    [Fact]
    public async Task Germinated_with_no_count_gives_no_rate_and_undoing_the_first_count_makes_it_uncounted_again()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (plant, batch, tray, label) = await SowAsync(nursery);
        await PostStatusAsync(nursery.Staff, $"/api/containers/{tray}/germinated", new { eventId = Guid.NewGuid(), occurredAt = Sown.AddDays(4) });

        Assert.Equal(JsonValueKind.Null, (await PlantFiguresAsync(nursery.Owner, plant)).GetProperty("germinationRate").ValueKind);

        var countId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(nursery.Staff, $"/api/containers/{tray}/count", new { eventId = countId, occurredAt = Sown.AddDays(5), batchId = batch, counted = 120 }));
        Assert.False((await LineAsync(nursery.Staff, label)).GetProperty("notCounted").GetBoolean());

        using var undo = await PostAsync(nursery.Staff, $"/api/events/{countId}/correct", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow });
        Assert.Equal(HttpStatusCode.Created, undo.StatusCode);
        Assert.True((await LineAsync(nursery.Staff, label)).GetProperty("notCounted").GetBoolean());
    }

    [Fact]
    public async Task A_resent_prick_out_is_recognised_and_late_losses_follow_the_count()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (_, batch, tray, label) = await SowAsync(nursery);
        var body = new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow, type = "PrickedOut", batchId = batch, quantity = 30, containerKind = "Tray", leftInTray = 12, countEventId = Guid.NewGuid() };

        using var first = await PostAsync(nursery.Staff, $"/api/containers/{tray}/transfer", body);
        using var again = await PostAsync(nursery.Staff, $"/api/containers/{tray}/transfer", body);
        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.OK), (first.StatusCode, again.StatusCode));
        Assert.Equal(12, (await LineAsync(nursery.Staff, label)).GetProperty("quantity").GetInt32());

        // "Some" queued before the count arrived can't be a number now; "all" takes what's there.
        using var some = await PostAsync(nursery.Staff, $"/api/containers/{tray}/losses", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow, batchId = batch, quantity = 0, reason = "Pest", all = false });
        Assert.Equal("NowCounted", Json(await some.Content.ReadAsStringAsync(Cancellation)).GetProperty("problem").GetString());
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(nursery.Staff, $"/api/containers/{tray}/losses", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow, batchId = batch, quantity = 0, reason = "Pest", all = true }));
        Assert.Equal(0, await ContentsAsync(nursery.Staff, label));
    }

    [Theory]
    [InlineData("About", "0")]
    [InlineData("About", "12.5")]
    [InlineData("Grams", "-1")]
    public async Task Edit_batch_refuses_a_sowing_that_makes_no_sense(string sownAs, string amount)
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (plant, batch, _, _) = await SowAsync(nursery);

        using var response = await nursery.Staff.PutAsJsonAsync(Route($"/api/batches/{batch}"), new { plantId = plant, source = "SeedLot", originCountry = "GB", sownAs, sownAmount = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture) }, Cancellation);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
