using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GrowLog.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GrowLog.Tests.AccountHarness;

namespace GrowLog.Tests;

public sealed class InvoiceTests(PostgresFixture database)
{
    private static async Task<Guid> AddAsync(HttpClient client, string path, object body) =>
        Json(await PostForTextAsync(client, path, body)).GetProperty("id").GetGuid();

    private static Task<Guid> AddCustomerAsync(HttpClient owner, string name = "Hilltop Garden Centre") =>
        AddAsync(owner, "/api/customers", new { name, address = "1 High Street\nBakewell", email = "buyer@example.com", phone = "", vatNumber = "" });

    private static async Task SetVatAsync(HttpClient owner, bool registered)
    {
        using var response = await owner.PutAsJsonAsync(Route("/api/settings/nursery"), new { name = "Croft View", address = "Croft Lane", phone = "", email = "", website = "", registrationNumber = "", vatNumber = registered ? "GB123456789" : null, hasLogo = false, vatRegistered = registered, paymentTermsDays = 14 }, Cancellation);
        response.EnsureSuccessStatusCode();
    }

    private static object Line(string description, int quantity, decimal unitPrice, decimal vatRate = 0, Guid? containerId = null, Guid? batchId = null) =>
        new { description, quantity, unitPrice, vatRate, containerId, batchId };

    private static Task<HttpResponseMessage> IssueAsync(HttpClient owner, Guid customerId, params object[] lines) =>
        PostAsync(owner, "/api/invoices", new { customerId, notes = "", lines });

    private static async Task<JsonElement> IssuedAsync(HttpClient owner, Guid customerId, params object[] lines)
    {
        using var response = await IssueAsync(owner, customerId, lines);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Json(await response.Content.ReadAsStringAsync(Cancellation));
    }

    private static async Task<JsonElement> InvoiceAsync(HttpClient owner, Guid id) =>
        (await owner.GetFromJsonAsync<JsonElement>(Route("/api/invoices"), Cancellation)).EnumerateArray().Single(invoice => invoice.GetProperty("id").GetGuid() == id);

    private static async Task<(Guid Container, Guid Batch, string Label)> TrayAsync(TwoPersonNursery nursery, int quantity)
    {
        var plant = await AddAsync(nursery.Owner, "/api/plants", new { commonName = "Lavender", xylellaHost = false, protectedVariety = false });
        var started = Json(await PostForTextAsync(nursery.Staff, "/api/batches", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow, plantId = plant, quantity, source = "SeedLot", originCountry = "GB", containerKind = "Tray" }));
        return (started.GetProperty("containerId").GetGuid(), started.GetProperty("batchId").GetGuid(), started.GetProperty("labelCode").GetString()!);
    }

    private static async Task<int> QuantityAsync(HttpClient client, string label)
    {
        var contents = (await client.GetFromJsonAsync<JsonElement>(Route($"/api/containers/by-label/{label}"), Cancellation)).GetProperty("contents");
        return contents.GetArrayLength() == 0 ? 0 : contents[0].GetProperty("quantity").GetInt32();
    }

    [Fact]
    public async Task Invoices_are_numbered_without_gaps_and_print_as_pdfs()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var customer = await AddCustomerAsync(nursery.Owner);

        var first = await IssuedAsync(nursery.Owner, customer, Line("Lavender 9cm", 24, 3.5m));
        using var refused = await IssueAsync(nursery.Owner, customer, Line("Wrong", 1, 1m, vatRate: 20));
        var second = await IssuedAsync(nursery.Owner, customer, Line("Salvia 2L", 10, 7.25m), Line("Delivery", 1, 15m));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(1, first.GetProperty("number").GetInt32());
        Assert.Equal(2, second.GetProperty("number").GetInt32());
        Assert.Equal(87.5m, (await InvoiceAsync(nursery.Owner, second.GetProperty("id").GetGuid())).GetProperty("total").GetDecimal());

        using var pdf = await nursery.Owner.GetAsync(Route($"/api/invoices/{first.GetProperty("id").GetGuid()}/pdf"), Cancellation);
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync(Cancellation), 0, 4), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_vat_registered_nursery_charges_vat_at_each_lines_rate()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        await SetVatAsync(nursery.Owner, registered: true);
        var customer = await AddCustomerAsync(nursery.Owner);

        var issued = await IssuedAsync(nursery.Owner, customer, Line("Lavender 9cm", 10, 3m, vatRate: 20), Line("Tomato plants", 10, 2m, vatRate: 0));
        using var odd = await IssueAsync(nursery.Owner, customer, Line("Odd rate", 1, 1m, vatRate: 17.5m));

        Assert.Equal(56m, (await InvoiceAsync(nursery.Owner, issued.GetProperty("id").GetGuid())).GetProperty("total").GetDecimal());
        Assert.Equal(HttpStatusCode.BadRequest, odd.StatusCode);
    }

    [Fact]
    public async Task Plants_taken_from_a_tray_leave_its_stock_and_a_credit_note_puts_them_back()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var customer = await AddCustomerAsync(nursery.Owner);
        var (container, batch, label) = await TrayAsync(nursery, 40);

        using var tooMany = await IssueAsync(nursery.Owner, customer, Line("Lavender", 41, 3m, containerId: container, batchId: batch));
        Assert.Equal(HttpStatusCode.Conflict, tooMany.StatusCode);

        var invoice = await IssuedAsync(nursery.Owner, customer, Line("Lavender", 30, 3m, containerId: container, batchId: batch));
        Assert.Equal(10, await QuantityAsync(nursery.Staff, label));

        var id = invoice.GetProperty("id").GetGuid();
        using var credit = await PostAsync(nursery.Owner, $"/api/invoices/{id}/credit");
        Assert.Equal(HttpStatusCode.Created, credit.StatusCode);
        Assert.Equal(2, Json(await credit.Content.ReadAsStringAsync(Cancellation)).GetProperty("number").GetInt32());
        Assert.Equal(40, await QuantityAsync(nursery.Staff, label));
        Assert.True((await InvoiceAsync(nursery.Owner, id)).GetProperty("credited").GetBoolean());

        using var again = await PostAsync(nursery.Owner, $"/api/invoices/{id}/credit");
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Unpaid_invoices_are_owed_to_the_nursery_until_paid_or_credited()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var customer = await AddCustomerAsync(nursery.Owner);
        var paid = (await IssuedAsync(nursery.Owner, customer, Line("Lavender", 10, 5m))).GetProperty("id").GetGuid();
        var credited = (await IssuedAsync(nursery.Owner, customer, Line("Salvia", 4, 5m))).GetProperty("id").GetGuid();
        await IssuedAsync(nursery.Owner, customer, Line("Geranium", 2, 5m));

        using var marked = await PostAsync(nursery.Owner, $"/api/invoices/{paid}/paid", new { paidOn = DateOnly.FromDateTime(DateTime.Today) });
        using var cancelled = await PostAsync(nursery.Owner, $"/api/invoices/{credited}/credit");

        var summary = await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/money/summary"), Cancellation);
        Assert.Equal(10m, summary.GetProperty("owedToUs").GetDecimal());
        Assert.Equal(60m, summary.GetProperty("months")[0].GetProperty("income").GetDecimal());
    }

    [Fact]
    public async Task An_issued_invoice_cannot_be_changed_only_marked_paid()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);
        var customer = await AddCustomerAsync(owner);
        var id = (await IssuedAsync(owner, customer, Line("Lavender", 10, 5m))).GetProperty("id").GetGuid();

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GrowLogDbContext>();
        var refused = await Assert.ThrowsAnyAsync<Exception>(() => db.Invoices.Where(invoice => invoice.Id == id).ExecuteUpdateAsync(set => set.SetProperty(invoice => invoice.CustomerName, "Someone else"), Cancellation));
        Assert.Contains("append-only", refused.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await db.Invoices.Where(invoice => invoice.Id == id).ExecuteUpdateAsync(set => set.SetProperty(invoice => invoice.PaidOn, DateOnly.FromDateTime(DateTime.Today)), Cancellation));
    }

    [Fact]
    public async Task Only_the_owner_keeps_customers_and_their_contact_details_can_be_forgotten()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var customer = await AddCustomerAsync(nursery.Owner);

        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync(nursery.Staff, "/api/customers"));
        Assert.Equal(HttpStatusCode.Conflict, await PostStatusAsync(nursery.Owner, "/api/customers", new { name = "hilltop garden centre" }));
        var invoice = (await IssuedAsync(nursery.Owner, customer, Line("Lavender", 1, 5m))).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(nursery.Owner, $"/api/customers/{customer}/forget"));

        var kept = (await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/customers"), Cancellation)).EnumerateArray().Single();
        Assert.Equal(JsonValueKind.Null, kept.GetProperty("email").ValueKind);
        Assert.Equal(JsonValueKind.Null, kept.GetProperty("address").ValueKind);
        using var pdf = await nursery.Owner.GetAsync(Route($"/api/invoices/{invoice}/pdf"), Cancellation);
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
    }

    [Fact]
    public async Task A_pdf_invoice_is_read_into_suggested_purchase_lines()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var customer = await AddCustomerAsync(nursery.Owner);
        var invoice = (await IssuedAsync(nursery.Owner, customer, Line("Lavender 9cm", 24, 2.1m), Line("Compost 50L", 10, 6.5m))).GetProperty("id").GetGuid();
        using var pdf = await nursery.Owner.GetAsync(Route($"/api/invoices/{invoice}/pdf"), Cancellation);
        var bytes = await pdf.Content.ReadAsByteArrayAsync(Cancellation);

        using var body = new ByteArrayContent(bytes);
        using var read = await nursery.Owner.PostAsync(Route("/api/purchases/read-invoice"), body, Cancellation);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var draft = Json(await read.Content.ReadAsStringAsync(Cancellation));

        Assert.Equal("0001", draft.GetProperty("reference").GetString());
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), draft.GetProperty("date").GetString());
        var lines = draft.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal("Lavender 9cm", lines[0].GetProperty("description").GetString());
        Assert.Equal(24, lines[0].GetProperty("quantity").GetInt32());
        Assert.Equal(50.40m, lines[0].GetProperty("netCost").GetDecimal());
        Assert.Equal(65.00m, lines[1].GetProperty("netCost").GetDecimal());

        using var notPdf = new ByteArrayContent("hello"u8.ToArray());
        using var refused = await nursery.Owner.PostAsync(Route("/api/purchases/read-invoice"), notPdf, Cancellation);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }
}
