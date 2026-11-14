using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hortiplanner.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using static Hortiplanner.Tests.AccountHarness;

namespace Hortiplanner.Tests;

public sealed class MoneyTests(PostgresFixture database)
{
    private static readonly DateOnly Day = new(2026, 3, 14);

    private static string Month(DateOnly date) => date.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static Task<HttpStatusCode> AddTakingAsync(HttpClient owner, DateOnly takenOn, decimal amount) =>
        PostStatusAsync(owner, "/api/money/takings", new { takenOn, amount, notes = "Busy Saturday" });

    private static Task<JsonElement> GetAsync(HttpClient client, string path) =>
        client.GetFromJsonAsync<JsonElement>(Route(path), Cancellation);

    private static async Task<Guid> AddSupplierAsync(HttpClient owner, string name = "Hillside Plants") =>
        Json(await PostForTextAsync(owner, "/api/suppliers", new { name })).GetProperty("id").GetGuid();

    private static async Task<Guid> AddPurchaseAsync(HttpClient owner, Guid supplierId, DateOnly purchasedOn, decimal netCost, decimal vat, string reference = "INV-1") =>
        Json(await PostForTextAsync(owner, "/api/purchases", new { supplierId, purchasedOn, reference, lines = new[] { new { description = "Compost", netCost, vat } } })).GetProperty("id").GetGuid();

    [Fact]
    public async Task A_days_takings_are_recorded_and_listed()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);

        Assert.Equal(HttpStatusCode.Created, await AddTakingAsync(nursery.Owner, Day, 312.40m));

        var taking = (await GetAsync(nursery.Owner, "/api/money/takings")).EnumerateArray().Single();
        Assert.Equal(312.40m, taking.GetProperty("amount").GetDecimal());
        Assert.Equal("Busy Saturday", taking.GetProperty("notes").GetString());
    }

    [Fact]
    public async Task A_day_can_only_be_entered_once_until_it_is_voided()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        await AddTakingAsync(nursery.Owner, Day, 100m);

        Assert.Equal(HttpStatusCode.Conflict, await AddTakingAsync(nursery.Owner, Day, 200m));

        var id = (await GetAsync(nursery.Owner, "/api/money/takings"))[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(nursery.Owner, $"/api/money/takings/{id}/void"));

        Assert.Equal(HttpStatusCode.Created, await AddTakingAsync(nursery.Owner, Day, 200m));
        Assert.Equal(200m, (await GetAsync(nursery.Owner, "/api/money/takings")).EnumerateArray().Single().GetProperty("amount").GetDecimal());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1.005)]
    public async Task Invalid_amounts_are_refused(double amount)
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);

        Assert.Equal(HttpStatusCode.BadRequest, await AddTakingAsync(nursery.Owner, Day, (decimal)amount));
    }

    [Fact]
    public async Task A_future_date_is_refused()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);

        Assert.Equal(HttpStatusCode.BadRequest, await AddTakingAsync(nursery.Owner, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)), 10m));
    }

    [Fact]
    public async Task The_summary_shows_income_spending_and_what_is_owed_by_month()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var supplier = await AddSupplierAsync(nursery.Owner);
        var april = new DateOnly(2026, 4, 2);
        await AddTakingAsync(nursery.Owner, Day, 100m);
        await AddTakingAsync(nursery.Owner, Day.AddDays(1), 50.50m);
        await AddTakingAsync(nursery.Owner, april, 75m);
        var paid = await AddPurchaseAsync(nursery.Owner, supplier, Day, 40m, 8m);
        await AddPurchaseAsync(nursery.Owner, supplier, april, 10m, 2m);
        await PostStatusAsync(nursery.Owner, $"/api/purchases/{paid}/paid", new { paidOn = Day });

        var summary = await GetAsync(nursery.Owner, "/api/money/summary");

        Assert.Equal(12m, summary.GetProperty("owed").GetDecimal());
        var months = summary.GetProperty("months");
        Assert.Equal(Month(april), months[0].GetProperty("month").GetString());
        Assert.Equal(75m, months[0].GetProperty("income").GetDecimal());
        Assert.Equal(12m, months[0].GetProperty("spending").GetDecimal());
        Assert.Equal(150.50m, months[1].GetProperty("income").GetDecimal());
        Assert.Equal(48m, months[1].GetProperty("spending").GetDecimal());
    }

    [Fact]
    public async Task Voided_records_are_left_out_of_the_summary()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var purchase = await AddPurchaseAsync(nursery.Owner, await AddSupplierAsync(nursery.Owner), Day, 40m, 0m);
        await PostStatusAsync(nursery.Owner, $"/api/purchases/{purchase}/void");

        var summary = await GetAsync(nursery.Owner, "/api/money/summary");

        Assert.Equal(0m, summary.GetProperty("owed").GetDecimal());
        Assert.Equal(0, summary.GetProperty("months").GetArrayLength());
    }

    private static async Task<string> ExportAsync(HttpClient owner)
    {
        using var response = await owner.GetAsync(Route("/api/money/export.csv"), Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadAsStringAsync(Cancellation);
    }

    [Fact]
    public async Task The_export_lists_money_in_and_out_for_an_accountant()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        await AddTakingAsync(nursery.Owner, Day, 100m);
        await AddPurchaseAsync(nursery.Owner, await AddSupplierAsync(nursery.Owner), Day.AddDays(1), 40m, 8m);

        var lines = (await ExportAsync(nursery.Owner)).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("Date,Type,Who", lines[0], StringComparison.Ordinal);
        Assert.Equal("2026-03-14,Income,\"Till takings\",,\"Busy Saturday\",100.00,0.00,100.00,", lines[1]);
        Assert.Equal("2026-03-15,Spending,\"Hillside Plants\",\"INV-1\",\"Compost\",40.00,8.00,48.00,", lines[2]);
    }

    [Fact]
    public async Task Text_cannot_become_a_spreadsheet_formula_or_break_the_columns()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        await AddPurchaseAsync(nursery.Owner, await AddSupplierAsync(nursery.Owner, "=HYPERLINK(\"http://x\")"), Day, 1m, 0m, "a,b");

        var csv = await ExportAsync(nursery.Owner);

        Assert.Contains("\"'=HYPERLINK(\"\"http://x\"\")\"", csv, StringComparison.Ordinal);
        Assert.Contains("\"a,b\"", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_database_refuses_to_alter_a_recorded_total()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        await AddTakingAsync(nursery.Owner, Day, 100m);
        await using var scope = nursery.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HortiplannerDbContext>();

        var update = await Assert.ThrowsAsync<PostgresException>(() =>
            db.TillTakings.ExecuteUpdateAsync(set => set.SetProperty(t => t.Amount, 1m), Cancellation));
        var delete = await Assert.ThrowsAsync<PostgresException>(() => db.TillTakings.ExecuteDeleteAsync(Cancellation));

        Assert.Contains("append-only", update.MessageText, StringComparison.Ordinal);
        Assert.Contains("append-only", delete.MessageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Money_is_for_the_owner()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        using var stranger = Client(nursery.App);

        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync(nursery.Staff, "/api/money/summary"));
        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync(nursery.Staff, "/api/money/export.csv"));
        Assert.Equal(HttpStatusCode.Forbidden, await AddTakingAsync(nursery.Staff, Day, 1m));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(stranger, "/api/money/takings"));
    }
}
