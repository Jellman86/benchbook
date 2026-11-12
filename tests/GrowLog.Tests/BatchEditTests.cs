using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using static GrowLog.Tests.AccountHarness;

namespace GrowLog.Tests;

public sealed class BatchEditTests(PostgresFixture database)
{
    private static readonly DateTimeOffset Sown = DateTimeOffset.UtcNow.AddHours(-2);

    private static async Task<Guid> AddAsync(HttpClient client, string path, object body) =>
        Json(await PostForTextAsync(client, path, body)).GetProperty("id").GetGuid();

    private static async Task<(Guid Plant, Guid Batch, Guid Tray, string Label)> SowAsync(TwoPersonNursery nursery)
    {
        var plant = await AddAsync(nursery.Owner, "/api/plants", new { commonName = "Lavender", xylellaHost = false, protectedVariety = false });
        var started = Json(await PostForTextAsync(nursery.Staff, "/api/batches", new { eventId = Guid.NewGuid(), occurredAt = Sown, plantId = plant, quantity = 40, source = "SeedLot", sourceReference = "Lot 7", originCountry = "GB", containerKind = "Tray" }));
        return (plant, started.GetProperty("batchId").GetGuid(), started.GetProperty("containerId").GetGuid(), started.GetProperty("labelCode").GetString()!);
    }

    private static Task<JsonElement> DetailsAsync(HttpClient client, Guid batch) =>
        client.GetFromJsonAsync<JsonElement>(Route($"/api/batches/{batch}"), Cancellation);

    private static Task<JsonElement> TraceAsync(HttpClient client, Guid batch) =>
        client.GetFromJsonAsync<JsonElement>(Route($"/api/batches/{batch}/trace"), Cancellation);

    private static async Task<HttpResponseMessage> EditAsync(HttpClient client, Guid batch, object body) =>
        await client.PutAsJsonAsync(Route($"/api/batches/{batch}"), body, Cancellation);

    private static object Body(
        Guid plant,
        string source = "SeedLot",
        string? reference = "Lot 7",
        Guid? supplier = null,
        string country = "GB",
        DateTimeOffset? startedAt = null,
        int? quantity = null) =>
        new { plantId = plant, source, sourceReference = reference, supplierId = supplier, motherPlant = (string?)null, originCountry = country, startedAt, quantity };

    private static async Task<int> QuantityAsync(HttpClient client, string label) =>
        (await client.GetFromJsonAsync<JsonElement>(Route($"/api/containers/by-label/{label}"), Cancellation)).GetProperty("contents")[0].GetProperty("quantity").GetInt32();

    [Fact]
    public async Task Details_are_corrected_and_every_correction_is_kept_with_who_made_it()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (_, batch, _, _) = await SowAsync(nursery);
        var salvia = await AddAsync(nursery.Owner, "/api/plants", new { commonName = "Salvia", variety = "Hot Lips", xylellaHost = true, protectedVariety = false });
        var supplier = await AddAsync(nursery.Owner, "/api/suppliers", new { name = "Hillside Plants" });

        using var edited = await EditAsync(nursery.Staff, batch, Body(salvia, source: "BoughtIn", reference: "INV-9", supplier: supplier, country: "nl"));
        Assert.Equal(HttpStatusCode.NoContent, edited.StatusCode);

        var details = await DetailsAsync(nursery.Staff, batch);
        Assert.Equal(salvia, details.GetProperty("plantId").GetGuid());
        Assert.Equal("NL", details.GetProperty("originCountry").GetString());
        Assert.Equal(supplier, details.GetProperty("supplierId").GetGuid());

        var corrections = (await TraceAsync(nursery.Owner, batch)).GetProperty("corrections").EnumerateArray().ToList();
        JsonElement Of(string field) => corrections.Single(change => string.Equals(change.GetProperty("field").GetString(), field, StringComparison.Ordinal));
        var plantChange = Of("Plant");
        Assert.Equal("Lavender", plantChange.GetProperty("before").GetString());
        Assert.Equal("Salvia 'Hot Lips'", plantChange.GetProperty("after").GetString());
        Assert.Equal("Hillside Plants", Of("Supplier").GetProperty("after").GetString());
        Assert.All(corrections, change => Assert.False(string.IsNullOrEmpty(change.GetProperty("changedBy").GetString())));

        // Saving the same again records nothing more.
        using var again = await EditAsync(nursery.Staff, batch, Body(salvia, source: "BoughtIn", reference: "INV-9", supplier: supplier, country: "NL"));
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal(corrections.Count, (await TraceAsync(nursery.Owner, batch)).GetProperty("corrections").GetArrayLength());
    }

    [Fact]
    public async Task The_day_and_number_are_corrected_while_nothing_else_is_recorded()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (plant, batch, _, label) = await SowAsync(nursery);
        var earlier = Sown.AddDays(-5);

        Assert.True((await DetailsAsync(nursery.Staff, batch)).GetProperty("startCanChange").GetBoolean());
        using var edited = await EditAsync(nursery.Staff, batch, Body(plant, startedAt: earlier, quantity: 36));
        Assert.Equal(HttpStatusCode.NoContent, edited.StatusCode);

        Assert.Equal(36, await QuantityAsync(nursery.Staff, label));
        var details = await DetailsAsync(nursery.Staff, batch);
        Assert.Equal(36, details.GetProperty("quantity").GetInt32());
        Assert.Equal(earlier.ToUnixTimeSeconds(), details.GetProperty("startedAt").GetDateTimeOffset().ToUnixTimeSeconds());

        var trace = await TraceAsync(nursery.Owner, batch);
        var sown = Assert.Single(trace.GetProperty("steps").EnumerateArray());
        Assert.Equal(36, sown.GetProperty("quantity").GetInt32());
        Assert.Equal(
            ["Started", "How many"],
            trace.GetProperty("corrections").EnumerateArray().Select(change => change.GetProperty("field").GetString()!).Order(StringComparer.Ordinal).Reverse().ToArray());
    }

    [Fact]
    public async Task Once_other_work_is_recorded_the_start_stays_and_a_count_corrects_the_number()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (plant, batch, tray, _) = await SowAsync(nursery);
        await PostForTextAsync(nursery.Staff, $"/api/containers/{tray}/treatments", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow, product = "Neem oil", dose = "5ml/L", area = "Tray" });

        Assert.False((await DetailsAsync(nursery.Staff, batch)).GetProperty("startCanChange").GetBoolean());
        using var refused = await EditAsync(nursery.Staff, batch, Body(plant, quantity: 30));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("StartLocked", Json(await refused.Content.ReadAsStringAsync(Cancellation)).GetProperty("problem").GetString());

        // Details can still be corrected.
        using var details = await EditAsync(nursery.Staff, batch, Body(plant, reference: "Lot 8"));
        Assert.Equal(HttpStatusCode.NoContent, details.StatusCode);
    }

    [Fact]
    public async Task Changing_how_a_batch_started_records_the_start_again_as_that_kind_of_work()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (plant, batch, _, _) = await SowAsync(nursery);
        var supplier = await AddAsync(nursery.Owner, "/api/suppliers", new { name = "Hillside Plants" });

        using var edited = await EditAsync(nursery.Staff, batch, Body(plant, source: "BoughtIn", supplier: supplier));
        Assert.Equal(HttpStatusCode.NoContent, edited.StatusCode);
        Assert.Equal("Received", Assert.Single((await TraceAsync(nursery.Owner, batch)).GetProperty("steps").EnumerateArray()).GetProperty("type").GetString());

        // Cuttings have a mother plant rather than a lot or supplier.
        using var cuttings = await EditAsync(nursery.Staff, batch, new { plantId = plant, source = "Cuttings", sourceReference = "Lot 7", supplierId = supplier, motherPlant = "By the door", originCountry = "GB" });
        Assert.Equal(HttpStatusCode.NoContent, cuttings.StatusCode);
        var details = await DetailsAsync(nursery.Staff, batch);
        Assert.Equal(JsonValueKind.Null, details.GetProperty("sourceReference").ValueKind);
        Assert.Equal(JsonValueKind.Null, details.GetProperty("supplierId").ValueKind);
        Assert.Equal("By the door", details.GetProperty("motherPlant").GetString());
        Assert.Equal("CuttingsTaken", Assert.Single((await TraceAsync(nursery.Owner, batch)).GetProperty("steps").EnumerateArray()).GetProperty("type").GetString());
    }

    [Fact]
    public async Task The_search_list_shows_the_corrected_start_day()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (plant, batch, _, _) = await SowAsync(nursery);
        var later = Sown.AddMinutes(30);

        using var edited = await EditAsync(nursery.Staff, batch, Body(plant, startedAt: later));
        Assert.Equal(HttpStatusCode.NoContent, edited.StatusCode);

        var match = Assert.Single((await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/trace"), Cancellation)).EnumerateArray());
        Assert.Equal(later.ToUnixTimeSeconds(), match.GetProperty("startedAt").GetDateTimeOffset().ToUnixTimeSeconds());
    }

    [Fact]
    public async Task A_locked_start_refuses_a_change_of_source()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (plant, batch, tray, _) = await SowAsync(nursery);
        await PostForTextAsync(nursery.Staff, $"/api/containers/{tray}/germinated", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow });

        using var refused = await EditAsync(nursery.Staff, batch, Body(plant, source: "Cuttings"));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("SeedLot", (await DetailsAsync(nursery.Staff, batch)).GetProperty("source").GetString());
    }

    [Theory]
    [InlineData("BoughtIn", "GB", 0)]
    [InlineData("SeedLot", "G", 0)]
    [InlineData("SeedLot", "GB", 2)]
    public async Task Bad_corrections_are_refused(string source, string country, int daysAhead)
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (plant, batch, _, _) = await SowAsync(nursery);

        using var response = await EditAsync(nursery.Staff, batch, Body(plant, source: source, country: country, startedAt: daysAhead == 0 ? null : DateTimeOffset.UtcNow.AddDays(daysAhead)));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(nursery.Staff, $"/api/batches/{Guid.NewGuid()}"));
    }
}
