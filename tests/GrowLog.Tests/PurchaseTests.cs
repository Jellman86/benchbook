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
}
