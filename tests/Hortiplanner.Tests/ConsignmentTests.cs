using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Hortiplanner.Api.Data;
using Hortiplanner.Api.Passports;
using Xunit;
using static Hortiplanner.Tests.AccountHarness;

namespace Hortiplanner.Tests;

public sealed class ConsignmentTests(PostgresFixture database)
{
    [Theory]
    [InlineData("TradeSale", true)]
    [InlineData("DistanceSale", true)]
    [InlineData("InPerson", false)]
    [InlineData("OwnSite", false)]
    public void Trade_and_distance_sales_need_a_passport(string kind, bool needed) =>
        Assert.Equal(needed, ConsignmentEndpoints.PassportNeeded(Enum.Parse<ConsignmentKind>(kind)).Needed);

    private static async Task<Guid> AddAsync(HttpClient client, string path, object body) =>
        Json(await PostForTextAsync(client, path, body)).GetProperty("id").GetGuid();

    private static async Task<(Guid Batch, Guid Tray)> SowAsync(TwoPersonNursery nursery, string name, string botanical)
    {
        var plant = await AddAsync(nursery.Owner, "/api/plants", new { commonName = name, botanicalName = botanical, xylellaHost = false, protectedVariety = false });
        var started = Json(await PostForTextAsync(nursery.Staff, "/api/batches", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow, plantId = plant, quantity = 40, source = "SeedLot", originCountry = "GB", containerKind = "Tray" }));
        return (started.GetProperty("batchId").GetGuid(), started.GetProperty("containerId").GetGuid());
    }

    private static object Line(Guid tray, Guid batch, int quantity) => new { containerId = tray, batchId = batch, quantity };

    [Fact]
    public async Task Each_package_gets_one_passport_covering_every_batch_in_it()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        using var settings = await nursery.Owner.PutAsJsonAsync(Route("/api/settings/nursery"), new { name = "Croft View", registrationNumber = "123456", hasLogo = false, vatRegistered = false, paymentTermsDays = 30 }, Cancellation);
        settings.EnsureSuccessStatusCode();
        var (lavender, lavenderTray) = await SowAsync(nursery, "Lavender", "Lavandula angustifolia");
        var (sage, sageTray) = await SowAsync(nursery, "Sage", "Salvia officinalis");

        using var tooMany = await PostAsync(nursery.Owner, "/api/consignments", new { kind = "TradeSale", sentTo = "Hilltop", packages = new[] { new { lines = new[] { Line(lavenderTray, lavender, 41) } } } });
        Assert.Equal(HttpStatusCode.Conflict, tooMany.StatusCode);

        var created = Json(await PostForTextAsync(nursery.Owner, "/api/consignments", new
        {
            kind = "TradeSale",
            sentTo = "Hilltop Garden Centre",
            packages = new[] { new { lines = new[] { Line(lavenderTray, lavender, 10), Line(sageTray, sage, 5) } }, new { lines = new[] { Line(sageTray, sage, 5) } } },
        }));
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal(1, created.GetProperty("number").GetInt32());

        var details = await nursery.Owner.GetFromJsonAsync<JsonElement>(Route($"/api/consignments/{id}"), Cancellation);
        Assert.True(details.GetProperty("passportNeeded").GetBoolean());
        var package = details.GetProperty("packages")[0].GetProperty("id").GetGuid();

        using var unexamined = await PostAsync(nursery.Owner, $"/api/consignments/{id}/packages/{package}/passport", new { examined = false });
        Assert.Equal(HttpStatusCode.Conflict, unexamined.StatusCode);
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(nursery.Owner, $"/api/consignments/{id}/packages/{package}/passport", new { examined = true }));
        Assert.Equal(HttpStatusCode.OK, await PostStatusAsync(nursery.Owner, $"/api/consignments/{id}/packages/{package}/passport", new { examined = true }));

        var passport = (await nursery.Owner.GetFromJsonAsync<JsonElement>(Route($"/api/consignments/{id}"), Cancellation)).GetProperty("packages")[0].GetProperty("passport");
        // Each batch's name, code and origin stay together.
        var lines = passport.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal("Lavandula angustifolia", lines[0].GetProperty("partA").GetString());
        Assert.Equal("Salvia officinalis", lines[1].GetProperty("partA").GetString());
        Assert.All(lines, line => Assert.Equal("GB", line.GetProperty("partD").GetString()));
        Assert.Equal(10, lines[0].GetProperty("quantity").GetInt32());
        Assert.Equal("123456", passport.GetProperty("partB").GetString());

        using var label = await nursery.Owner.GetAsync(Route($"/api/labels/passports/{package}"), Cancellation);
        Assert.Equal(HttpStatusCode.OK, label.StatusCode);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(await label.Content.ReadAsByteArrayAsync(Cancellation), 0, 4), StringComparison.Ordinal);

        var trace = await nursery.Owner.GetFromJsonAsync<JsonElement>(Route($"/api/batches/{lavender}/trace"), Cancellation);
        Assert.Equal("Hilltop Garden Centre", Assert.Single(trace.GetProperty("issuedPassports").EnumerateArray()).GetProperty("issuedTo").GetString());

        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync(nursery.Staff, "/api/consignments"));
    }

    [Fact]
    public async Task Plants_already_invoiced_can_still_go_in_a_consignment_with_a_passport()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        using var settings = await nursery.Owner.PutAsJsonAsync(Route("/api/settings/nursery"), new { name = "Croft View", registrationNumber = "123456", hasLogo = false, vatRegistered = false, paymentTermsDays = 30 }, Cancellation);
        settings.EnsureSuccessStatusCode();
        var (batch, tray) = await SowAsync(nursery, "Lavender", "Lavandula angustifolia");
        var customer = await AddAsync(nursery.Owner, "/api/customers", new { name = "Hilltop Garden Centre" });
        using var invoiced = await PostAsync(nursery.Owner, "/api/invoices", new { customerId = customer, notes = "", lines = new[] { new { description = "Lavender", quantity = 12, unitPrice = 3m, vatRate = 0m, containerId = tray, batchId = batch } } });
        Assert.Equal(HttpStatusCode.Created, invoiced.StatusCode);

        var waiting = Assert.Single((await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/consignments/invoiced"), Cancellation)).EnumerateArray());
        Assert.Equal(12, waiting.GetProperty("left").GetInt32());
        var invoiceLine = waiting.GetProperty("invoiceLineId").GetGuid();

        // The tray and batch come from the invoice, whatever was sent.
        object Line(int quantity) => new { containerId = Guid.NewGuid(), batchId = Guid.NewGuid(), quantity, invoiceLineId = invoiceLine };
        using var tooMany = await PostAsync(nursery.Owner, "/api/consignments", new { kind = "TradeSale", customerId = customer, packages = new[] { new { lines = new[] { Line(13) } } } });
        Assert.Equal(HttpStatusCode.Conflict, tooMany.StatusCode);
        var id = Json(await PostForTextAsync(nursery.Owner, "/api/consignments", new { kind = "TradeSale", customerId = customer, packages = new[] { new { lines = new[] { Line(12) } } } })).GetProperty("id").GetGuid();
        Assert.Equal(0, (await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/consignments/invoiced"), Cancellation)).GetArrayLength());

        var package = (await nursery.Owner.GetFromJsonAsync<JsonElement>(Route($"/api/consignments/{id}"), Cancellation)).GetProperty("packages")[0];
        Assert.Equal(1, package.GetProperty("lines")[0].GetProperty("invoiceNumber").GetInt32());
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(nursery.Owner, $"/api/consignments/{id}/packages/{package.GetProperty("id").GetGuid()}/passport", new { examined = true }));
    }

    [Fact]
    public async Task Package_quantities_are_grouped_again_when_the_passport_is_issued()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        using var settings = await nursery.Owner.PutAsJsonAsync(Route("/api/settings/nursery"), new { registrationNumber = "123456" }, Cancellation);
        var (batch, tray) = await SowAsync(nursery, "Lavender", "Lavandula angustifolia");
        var id = await AddAsync(nursery.Owner, "/api/consignments", new { kind = "TradeSale", sentTo = "Hilltop", packages = new[] { new { lines = new[] { Line(tray, batch, 15), Line(tray, batch, 15) } } } });
        await PostForTextAsync(nursery.Staff, $"/api/containers/{tray}/count", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow, batchId = batch, counted = 20 });
        var package = (await nursery.Owner.GetFromJsonAsync<JsonElement>(Route($"/api/consignments/{id}"), Cancellation)).GetProperty("packages")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, await PostStatusAsync(nursery.Owner, $"/api/consignments/{id}/packages/{package}/passport", new { examined = true }));
    }

    [Fact]
    public async Task A_cancelled_invoice_cannot_back_a_package_passport()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        using var settings = await nursery.Owner.PutAsJsonAsync(Route("/api/settings/nursery"), new { registrationNumber = "123456" }, Cancellation);
        var (batch, tray) = await SowAsync(nursery, "Lavender", "Lavandula angustifolia");
        var customer = await AddAsync(nursery.Owner, "/api/customers", new { name = "Hilltop" });
        var invoice = Json(await PostForTextAsync(nursery.Owner, "/api/invoices", new { customerId = customer, lines = new[] { new { description = "Lavender", quantity = 10, unitPrice = 3m, vatRate = 0m, containerId = tray, batchId = batch } } })).GetProperty("id").GetGuid();
        var waiting = (await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/consignments/invoiced"), Cancellation))[0];
        var id = await AddAsync(nursery.Owner, "/api/consignments", new { kind = "TradeSale", customerId = customer, packages = new[] { new { lines = new[] { new { containerId = tray, batchId = batch, quantity = 10, invoiceLineId = waiting.GetProperty("invoiceLineId").GetGuid() } } } } });
        await PostForTextAsync(nursery.Owner, $"/api/invoices/{invoice}/credit");
        var package = (await nursery.Owner.GetFromJsonAsync<JsonElement>(Route($"/api/consignments/{id}"), Cancellation)).GetProperty("packages")[0].GetProperty("id").GetGuid();
        using var reply = await PostAsync(nursery.Owner, $"/api/consignments/{id}/packages/{package}/passport", new { examined = true });
        Assert.Equal(HttpStatusCode.Conflict, reply.StatusCode);
        Assert.Contains("InvoiceCancelled", await reply.Content.ReadAsStringAsync(Cancellation), StringComparison.Ordinal);
    }


    [Fact]
    public async Task Consignment_and_package_retries_refuse_changed_content()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        using var settings = await nursery.Owner.PutAsJsonAsync(Route("/api/settings/nursery"), new { registrationNumber = "123456" }, Cancellation);
        var (batch, tray) = await SowAsync(nursery, "Lavender", "Lavandula angustifolia");
        var id = Guid.NewGuid();
        var body = new { id, kind = "TradeSale", sentTo = "Hilltop", packages = new[] { new { lines = new[] { Line(tray, batch, 10) } } } };
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(nursery.Owner, "/api/consignments", body));
        Assert.Equal(HttpStatusCode.OK, await PostStatusAsync(nursery.Owner, "/api/consignments", body));
        using var changed = await PostAsync(nursery.Owner, "/api/consignments", new { id, kind = "TradeSale", sentTo = "Hilltop", packages = new[] { new { lines = new[] { Line(tray, batch, 11) } } } });
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        var package = (await nursery.Owner.GetFromJsonAsync<JsonElement>(Route($"/api/consignments/{id}"), Cancellation)).GetProperty("packages")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(nursery.Owner, $"/api/consignments/{id}/packages/{package}/passport", new { examined = true, findings = "Healthy" }));
        Assert.Equal(HttpStatusCode.Conflict, await PostStatusAsync(nursery.Owner, $"/api/consignments/{id}/packages/{package}/passport", new { examined = true, findings = "Different" }));
    }
}
