using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using static GrowLog.Tests.AccountHarness;

namespace GrowLog.Tests;

public sealed class TraceTests(PostgresFixture database)
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UtcNow.AddHours(-12);

    private static DateTimeOffset At(int hours) => Start.AddHours(hours);

    private static async Task<Guid> AddAsync(HttpClient client, string path, object body) =>
        Json(await PostForTextAsync(client, path, body)).GetProperty("id").GetGuid();

    private static async Task<(Guid Batch, Guid Tray, string TrayLabel, string BatchCode)> SowAsync(TwoPersonNursery nursery)
    {
        var plant = await AddAsync(nursery.Owner, "/api/plants", new { commonName = "Lavender", variety = "Hidcote", xylellaHost = true, protectedVariety = false });
        var started = Json(await PostForTextAsync(nursery.Staff, "/api/batches", new { eventId = Guid.NewGuid(), occurredAt = At(0), plantId = plant, quantity = 40, source = "SeedLot", sourceReference = "Lot 7", originCountry = "GB", containerKind = "Tray" }));
        var batch = started.GetProperty("batchId").GetGuid();
        var trace = await TraceAsync(nursery.Owner, batch);

        return (batch, started.GetProperty("containerId").GetGuid(), started.GetProperty("labelCode").GetString()!, trace.GetProperty("traceabilityCode").GetString()!);
    }

    private static Task<string> TreatAsync(TwoPersonNursery nursery, Guid container, DateTimeOffset at, string product) =>
        PostForTextAsync(nursery.Staff, $"/api/containers/{container}/treatments", new { eventId = Guid.NewGuid(), occurredAt = at, product, dose = "5ml/L", area = "Whole tray" });

    private static async Task<Guid> LoseAsync(TwoPersonNursery nursery, Guid container, Guid batch, DateTimeOffset at, int quantity)
    {
        var eventId = Guid.NewGuid();
        await PostForTextAsync(nursery.Staff, $"/api/containers/{container}/losses", new { eventId, occurredAt = at, batchId = batch, quantity, reason = "Frost" });
        return eventId;
    }

    private static async Task<(Guid Id, string Label)> PrickOutAsync(TwoPersonNursery nursery, Guid tray, Guid batch, DateTimeOffset at, int quantity)
    {
        var moved = Json(await PostForTextAsync(nursery.Staff, $"/api/containers/{tray}/transfer", new { eventId = Guid.NewGuid(), occurredAt = at, type = "PrickedOut", batchId = batch, quantity, containerKind = "Tray" }));
        return (moved.GetProperty("containerId").GetGuid(), moved.GetProperty("labelCode").GetString()!);
    }

    private static async Task<Guid> SellAsync(TwoPersonNursery nursery, Guid container, Guid batch, int quantity)
    {
        var customer = await AddAsync(nursery.Owner, "/api/customers", new { name = "Hilltop Garden Centre", address = "1 High Street", email = "", phone = "", vatNumber = "" });
        var lines = new[] { new { description = "Lavender", quantity, unitPrice = 3m, vatRate = 0m, containerId = container, batchId = batch } };
        return Json(await PostForTextAsync(nursery.Owner, "/api/invoices", new { customerId = customer, notes = "", lines })).GetProperty("id").GetGuid();
    }

    private static Task<JsonElement> TraceAsync(HttpClient client, Guid batch) =>
        client.GetFromJsonAsync<JsonElement>(Route($"/api/batches/{batch}/trace"), Cancellation);

    private static async Task<List<Guid>> FindAsync(HttpClient client, string q) =>
        (await client.GetFromJsonAsync<JsonElement>(Route($"/api/trace?q={Uri.EscapeDataString(q)}"), Cancellation))
            .EnumerateArray()
            .Select(match => match.GetProperty("batchId").GetGuid())
            .ToList();

    [Fact]
    public async Task A_batch_traces_back_to_its_source_and_forward_to_who_bought_it()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (batch, tray, trayLabel, _) = await SowAsync(nursery);

        await TreatAsync(nursery, tray, At(1), "Neem oil");
        var (pots, potsLabel) = await PrickOutAsync(nursery, tray, batch, At(2), 30);
        await LoseAsync(nursery, pots, batch, At(3), 4);
        var undone = await LoseAsync(nursery, pots, batch, At(4), 2);
        await PostForTextAsync(nursery.Staff, $"/api/events/{undone}/correct", new { eventId = Guid.NewGuid(), occurredAt = At(5) });
        await LoseAsync(nursery, tray, batch, At(6), 10);
        await TreatAsync(nursery, tray, At(7), "After they left");
        await SellAsync(nursery, pots, batch, 20);

        var trace = await TraceAsync(nursery.Owner, batch);

        Assert.Equal("Lavender", trace.GetProperty("plant").GetProperty("commonName").GetString());
        Assert.True(trace.GetProperty("plant").GetProperty("xylellaHost").GetBoolean());
        Assert.Equal("Lot 7", trace.GetProperty("sourceReference").GetString());
        Assert.Equal(
            ["Sown", "Treated", "PrickedOut", "Lost", "Lost", "Sold"],
            trace.GetProperty("steps").EnumerateArray().Select(step => step.GetProperty("type").GetString()!).ToArray());

        var pricked = trace.GetProperty("steps")[2];
        Assert.Equal(30, pricked.GetProperty("quantity").GetInt32());
        Assert.Equal(trayLabel, pricked.GetProperty("fromLabelCode").GetString());
        Assert.Equal(potsLabel, pricked.GetProperty("labelCode").GetString());
        Assert.Equal(-20, trace.GetProperty("steps")[5].GetProperty("quantity").GetInt32());

        var totals = trace.GetProperty("totals");
        Assert.Equal((14, 20, 6), (totals.GetProperty("lost").GetInt32(), totals.GetProperty("sold").GetInt32(), totals.GetProperty("now").GetInt32()));
        Assert.Equal(2, trace.GetProperty("containers").GetArrayLength());

        var sale = Assert.Single(trace.GetProperty("sales").EnumerateArray());
        Assert.Equal("Hilltop Garden Centre", sale.GetProperty("customerName").GetString());
        Assert.Equal(20, sale.GetProperty("quantity").GetInt32());
        Assert.False(sale.GetProperty("cancelled").GetBoolean());
    }

    [Fact]
    public async Task A_credit_note_shows_the_sale_as_cancelled_and_the_plants_as_back()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (batch, tray, _, _) = await SowAsync(nursery);
        var invoice = await SellAsync(nursery, tray, batch, 15);
        await PostForTextAsync(nursery.Owner, $"/api/invoices/{invoice}/credit");

        var trace = await TraceAsync(nursery.Owner, batch);

        Assert.True(Assert.Single(trace.GetProperty("sales").EnumerateArray()).GetProperty("cancelled").GetBoolean());
        Assert.Equal(0, trace.GetProperty("totals").GetProperty("sold").GetInt32());
        Assert.Equal(40, trace.GetProperty("totals").GetProperty("now").GetInt32());
    }

    [Fact]
    public async Task A_batch_is_found_by_its_code_a_tray_it_was_in_or_its_plant()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (batch, tray, trayLabel, code) = await SowAsync(nursery);
        var (_, potsLabel) = await PrickOutAsync(nursery, tray, batch, At(1), 10);

        Assert.Equal([batch], await FindAsync(nursery.Owner, code.ToLowerInvariant()));
        Assert.Equal([batch], await FindAsync(nursery.Owner, trayLabel));
        Assert.Equal([batch], await FindAsync(nursery.Owner, potsLabel));
        Assert.Equal([batch], await FindAsync(nursery.Owner, "hidc"));
        Assert.Equal([batch], await FindAsync(nursery.Owner, ""));
        Assert.Empty(await FindAsync(nursery.Owner, "Salvia"));
    }

    [Fact]
    public async Task Only_the_owner_can_trace_and_an_unknown_batch_is_not_found()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var (batch, _, _, _) = await SowAsync(nursery);

        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync(nursery.Staff, $"/api/batches/{batch}/trace"));
        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync(nursery.Staff, "/api/trace"));
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(nursery.Owner, $"/api/batches/{Guid.NewGuid()}/trace"));
    }
}
