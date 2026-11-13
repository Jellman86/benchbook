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

public sealed class PurchaseTests(PostgresFixture database)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<Guid> AddSupplierAsync(HttpClient owner) =>
        Json(await PostForTextAsync(owner, "/api/suppliers", new { name = "Hillside Plants" })).GetProperty("id").GetGuid();

    private static object Body(Guid supplierId, object[]? lines = null, DateOnly? purchasedOn = null) => new
    {
        supplierId,
        purchasedOn = purchasedOn ?? Today,
        reference = "INV-204",
        notes = "Spring order",
        lines = lines ?? [new { description = "Compost", quantity = 10, netCost = 45.50m, vat = 9.10m }, new { description = "Seed", netCost = 12m, vat = 0m }],
    };

    private static async Task<Guid> AddPurchaseAsync(HttpClient owner, Guid supplierId) =>
        Json(await PostForTextAsync(owner, "/api/purchases", Body(supplierId))).GetProperty("id").GetGuid();

    private static Task<JsonElement> ListAsync(HttpClient owner, string query = "") =>
        owner.GetFromJsonAsync<JsonElement>(Route($"/api/purchases{query}"), Cancellation);

    [Fact]
    public async Task A_purchase_is_recorded_with_its_lines_and_totals_and_starts_unpaid()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var supplier = await AddSupplierAsync(nursery.Owner);

        await AddPurchaseAsync(nursery.Owner, supplier);

        var purchase = (await ListAsync(nursery.Owner)).EnumerateArray().Single();
        Assert.Equal("Hillside Plants", purchase.GetProperty("supplierName").GetString());
        Assert.Equal("INV-204", purchase.GetProperty("reference").GetString());
        Assert.Equal(57.50m, purchase.GetProperty("netTotal").GetDecimal());
        Assert.Equal(9.10m, purchase.GetProperty("vatTotal").GetDecimal());
        Assert.Equal(2, purchase.GetProperty("lines").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, purchase.GetProperty("paidOn").ValueKind);
    }

    [Fact]
    public async Task A_purchase_can_be_marked_paid_and_back_to_unpaid()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var id = await AddPurchaseAsync(nursery.Owner, await AddSupplierAsync(nursery.Owner));

        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(nursery.Owner, $"/api/purchases/{id}/paid", new { paidOn = Today }));
        Assert.Equal(Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), (await ListAsync(nursery.Owner))[0].GetProperty("paidOn").GetString());

        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(nursery.Owner, $"/api/purchases/{id}/paid", new { paidOn = default(DateOnly?) }));
        Assert.Equal(JsonValueKind.Null, (await ListAsync(nursery.Owner))[0].GetProperty("paidOn").ValueKind);
    }

    [Fact]
    public async Task A_voided_purchase_leaves_the_list_but_is_kept()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var id = await AddPurchaseAsync(nursery.Owner, await AddSupplierAsync(nursery.Owner));

        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(nursery.Owner, $"/api/purchases/{id}/void"));

        Assert.Equal(0, (await ListAsync(nursery.Owner)).GetArrayLength());
        Assert.True((await ListAsync(nursery.Owner, "?includeVoided=true"))[0].GetProperty("voided").GetBoolean());
    }

    [Fact]
    public async Task A_voided_purchase_cannot_be_marked_paid()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var id = await AddPurchaseAsync(nursery.Owner, await AddSupplierAsync(nursery.Owner));
        await PostStatusAsync(nursery.Owner, $"/api/purchases/{id}/void");

        Assert.Equal(HttpStatusCode.NotFound, await PostStatusAsync(nursery.Owner, $"/api/purchases/{id}/paid", new { paidOn = Today }));
    }

    public static TheoryData<decimal, decimal, string> BadLines => new()
    {
        { -1m, 0m, "Compost" },
        { 1m, -1m, "Compost" },
        { 1.005m, 0m, "Compost" },
        { 1m, 0m, "  " },
    };

    [Theory]
    [MemberData(nameof(BadLines))]
    public async Task Invalid_lines_are_refused(decimal netCost, decimal vat, string description)
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var supplier = await AddSupplierAsync(nursery.Owner);

        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(nursery.Owner, "/api/purchases", Body(supplier, [new { description, netCost, vat }])));
    }

    [Fact]
    public async Task A_purchase_needs_a_line_a_known_supplier_and_a_real_date()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var supplier = await AddSupplierAsync(nursery.Owner);

        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(nursery.Owner, "/api/purchases", Body(supplier, [])));
        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(nursery.Owner, "/api/purchases", Body(Guid.NewGuid())));
        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(nursery.Owner, "/api/purchases", Body(supplier, purchasedOn: Today.AddDays(5))));
    }

    [Fact]
    public async Task Every_change_is_kept_in_the_history()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var id = await AddPurchaseAsync(nursery.Owner, await AddSupplierAsync(nursery.Owner));
        await PostStatusAsync(nursery.Owner, $"/api/purchases/{id}/paid", new { paidOn = Today });

        await using var scope = nursery.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GrowLogDbContext>();
        var changes = await db.CatalogueChanges.Where(c => c.Entity == nameof(Purchase) && c.EntityKey == id.ToString()).ToListAsync(Cancellation);

        Assert.Equal(2, changes.Count);
        Assert.All(changes, c => Assert.Equal(changes[0].ChangedBy, c.ChangedBy));
    }

    [Fact]
    public async Task The_database_refuses_to_alter_what_was_recorded()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var id = await AddPurchaseAsync(nursery.Owner, await AddSupplierAsync(nursery.Owner));
        await using var scope = nursery.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GrowLogDbContext>();

        var line = await Assert.ThrowsAsync<PostgresException>(() =>
            db.PurchaseLines.Where(l => l.PurchaseId == id).ExecuteUpdateAsync(set => set.SetProperty(l => l.NetCost, 1m), Cancellation));
        var header = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Purchases.Where(p => p.Id == id).ExecuteUpdateAsync(set => set.SetProperty(p => p.Reference, "changed"), Cancellation));
        var delete = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Purchases.Where(p => p.Id == id).ExecuteDeleteAsync(Cancellation));

        Assert.Contains("append-only", line.MessageText, StringComparison.Ordinal);
        Assert.Contains("append-only", header.MessageText, StringComparison.Ordinal);
        Assert.Contains("append-only", delete.MessageText, StringComparison.Ordinal);
    }

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid purchase, byte[] content, string contentType = "image/png")
    {
        using var body = new ByteArrayContent(content);
        body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        return await client.PostAsync(Route($"/api/purchases/{purchase}/receipts"), body, Cancellation);
    }

    [Fact]
    public async Task A_receipt_can_be_added_and_viewed()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var id = await AddPurchaseAsync(nursery.Owner, await AddSupplierAsync(nursery.Owner));

        using var uploaded = await UploadAsync(nursery.Owner, id, Png);
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);

        var receipt = (await ListAsync(nursery.Owner))[0].GetProperty("receiptIds").EnumerateArray().Single().GetGuid();
        using var response = await nursery.Owner.GetAsync(Route($"/api/purchases/receipts/{receipt}"), Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await response.Content.ReadAsByteArrayAsync(Cancellation));
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task A_file_is_judged_by_its_contents_not_its_label()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var id = await AddPurchaseAsync(nursery.Owner, await AddSupplierAsync(nursery.Owner));

        using var html = await UploadAsync(nursery.Owner, id, "<html><script>"u8.ToArray());

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, html.StatusCode);
    }

    [Fact]
    public async Task An_oversized_receipt_is_refused()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var id = await AddPurchaseAsync(nursery.Owner, await AddSupplierAsync(nursery.Owner));
        var tooBig = new byte[(10 * 1024 * 1024) + 1];
        Png.CopyTo(tooBig, 0);

        using var response = await UploadAsync(nursery.Owner, id, tooBig);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task A_receipt_cannot_be_added_to_a_voided_purchase()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var id = await AddPurchaseAsync(nursery.Owner, await AddSupplierAsync(nursery.Owner));
        await PostStatusAsync(nursery.Owner, $"/api/purchases/{id}/void");

        using var response = await UploadAsync(nursery.Owner, id, Png);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Staff_cannot_add_or_view_receipts()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var id = await AddPurchaseAsync(nursery.Owner, await AddSupplierAsync(nursery.Owner));
        using var uploaded = await UploadAsync(nursery.Owner, id, Png);
        var receipt = (await ListAsync(nursery.Owner))[0].GetProperty("receiptIds")[0].GetGuid();

        using var staffUpload = await UploadAsync(nursery.Staff, id, Png);

        Assert.Equal(HttpStatusCode.Forbidden, staffUpload.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync(nursery.Staff, $"/api/purchases/receipts/{receipt}"));
    }

    [Fact]
    public async Task The_database_refuses_to_remove_a_receipt()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var id = await AddPurchaseAsync(nursery.Owner, await AddSupplierAsync(nursery.Owner));
        using var uploaded = await UploadAsync(nursery.Owner, id, Png);
        await using var scope = nursery.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GrowLogDbContext>();

        var delete = await Assert.ThrowsAsync<PostgresException>(() => db.PurchaseReceipts.ExecuteDeleteAsync(Cancellation));

        Assert.Contains("append-only", delete.MessageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_batch_started_from_a_purchase_line_is_counted_against_it()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var supplier = await AddSupplierAsync(nursery.Owner);
        await AddPurchaseAsync(nursery.Owner, supplier);
        var line = (await ListAsync(nursery.Owner))[0].GetProperty("lines")[0];
        Assert.Equal(0, line.GetProperty("batchCount").GetInt32());
        var plant = Json(await PostForTextAsync(nursery.Owner, "/api/plants", new { commonName = "Lavender", xylellaHost = false, protectedVariety = false })).GetProperty("id").GetGuid();

        var status = await PostStatusAsync(nursery.Staff, "/api/batches", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow, plantId = plant, quantity = 10, source = "BoughtIn", supplierId = supplier, originCountry = "GB", containerKind = "Tray", purchaseLineId = line.GetProperty("id").GetGuid() });

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal(1, (await ListAsync(nursery.Owner))[0].GetProperty("lines")[0].GetProperty("batchCount").GetInt32());
    }

    [Fact]
    public async Task Purchases_are_for_the_owner()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var supplier = await AddSupplierAsync(nursery.Owner);
        using var stranger = Client(nursery.App);

        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync(nursery.Staff, "/api/purchases"));
        Assert.Equal(HttpStatusCode.Forbidden, await PostStatusAsync(nursery.Staff, "/api/purchases", Body(supplier)));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(stranger, "/api/purchases"));
    }

    [Fact]
    public async Task Purchase_retries_are_once_only_and_changed_lines_are_refused()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var supplier = await AddSupplierAsync(nursery.Owner);
        var id = Guid.NewGuid();
        var body = new { id, supplierId = supplier, purchasedOn = Today, lines = new[] { new { description = "Compost", netCost = 15m, vat = 3m } } };
        var replies = await Task.WhenAll(PostAsync(nursery.Owner, "/api/purchases", body), PostAsync(nursery.Owner, "/api/purchases", body));
        foreach (var reply in replies)
        {
            Assert.True(reply.IsSuccessStatusCode);
            reply.Dispose();
        }
        using var changed = await PostAsync(nursery.Owner, "/api/purchases", new { id, supplierId = supplier, purchasedOn = Today, lines = new[] { new { description = "Compost", netCost = 16m, vat = 3m } } });
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Single((await ListAsync(nursery.Owner)).EnumerateArray());
    }

}
