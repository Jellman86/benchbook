using System.Globalization;
using System.Security.Claims;
using System.Text;
using GrowLog.Api.Accounts;
using GrowLog.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrowLog.Api.Selling;

/// <summary>Simple single-entry records of money in and out. For the owner.</summary>
internal static class MoneyEndpoints
{
    private const int TakingsShown = 60;

    public static void MapMoneyEndpoints(this IEndpointRouteBuilder app)
    {
        var money = app.MapGroup("/api/money").RequireAuthorization(AccountServices.OwnerPolicy);
        money.MapGet("/takings", ListTakingsAsync);
        money.MapPost("/takings", AddTakingAsync).Produces<TakingCreated>(StatusCodes.Status201Created);
        money.MapPost("/takings/{id:guid}/void", VoidTakingAsync);
        money.MapGet("/summary", GetSummaryAsync);
        money.MapGet("/export.csv", ExportAsync).Produces(StatusCodes.Status200OK, contentType: "text/csv");
    }

    private static string Month(DateOnly date) => date.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static async Task<List<TakingResponse>> ListTakingsAsync(GrowLogDbContext db, CancellationToken cancellationToken) =>
        await db.TillTakings
            .AsNoTracking()
            .Where(taking => taking.VoidedAt == null)
            .OrderByDescending(taking => taking.TakenOn)
            .Take(TakingsShown)
            .Select(taking => new TakingResponse(taking.Id, taking.TakenOn, taking.Amount, taking.Notes))
            .ToListAsync(cancellationToken);

    private static async Task<IResult> AddTakingAsync(TakingRequest request, ClaimsPrincipal user, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (request.Amount < 0 || request.Amount != decimal.Round(request.Amount, 2) || request.TakenOn > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
        {
            return TypedResults.BadRequest();
        }

        var taking = new TillTaking
        {
            TakenOn = request.TakenOn,
            Amount = request.Amount,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            RecordedBy = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
        };

        db.TillTakings.Add(taking);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.Created($"/api/money/takings/{taking.Id}", new TakingCreated(taking.Id));
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return TypedResults.Conflict();
        }
    }

    private static async Task<IResult> VoidTakingAsync(Guid id, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (await db.TillTakings.FindAsync([id], cancellationToken) is not { } taking)
        {
            return TypedResults.NotFound();
        }

        taking.VoidedAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<List<Purchase>> LivePurchasesAsync(GrowLogDbContext db, CancellationToken cancellationToken) =>
        await db.Purchases.AsNoTracking().Include(purchase => purchase.Lines).Where(purchase => purchase.VoidedAt == null).ToListAsync(cancellationToken);

    private static decimal Gross(Purchase purchase) => purchase.Lines.Sum(line => line.NetCost + line.Vat);

    private static async Task<MoneySummary> GetSummaryAsync(GrowLogDbContext db, CancellationToken cancellationToken)
    {
        var takings = await db.TillTakings.AsNoTracking().Where(taking => taking.VoidedAt == null).ToListAsync(cancellationToken);
        var purchases = await LivePurchasesAsync(db, cancellationToken);

        var invoices = await db.Invoices.AsNoTracking().Include(invoice => invoice.Lines).ToListAsync(cancellationToken);
        var credited = invoices.Where(invoice => invoice.CreditsInvoiceId is not null).Select(invoice => invoice.CreditsInvoiceId!.Value).ToHashSet();

        // Trade invoices count as income when issued; a credit note takes its invoice back out.
        var invoiced = invoices.Select(invoice => (Month: Month(invoice.IssuedOn), Amount: (invoice.Kind == InvoiceKind.CreditNote ? -1 : 1) * InvoiceTotals.Of(invoice.Lines).Total));
        var income = takings.Select(taking => (Month: Month(taking.TakenOn), taking.Amount))
            .Concat(invoiced)
            .GroupBy(entry => entry.Month, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Amount), StringComparer.Ordinal);
        var owedToUs = invoices
            .Where(invoice => invoice.Kind == InvoiceKind.Invoice && invoice.PaidOn is null && !credited.Contains(invoice.Id))
            .Sum(invoice => InvoiceTotals.Of(invoice.Lines).Total);
        var spending = purchases.GroupBy(purchase => Month(purchase.PurchasedOn), StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Sum(Gross), StringComparer.Ordinal);

        var months = income.Keys.Union(spending.Keys, StringComparer.Ordinal)
            .OrderDescending(StringComparer.Ordinal)
            .Select(month => new MonthFigures(month, income.GetValueOrDefault(month), spending.GetValueOrDefault(month)))
            .ToList();

        return new MoneySummary(months, purchases.Where(purchase => purchase.PaidOn is null).Sum(Gross), owedToUs);
    }

    /// <summary>Quotes a value for CSV, and stops a spreadsheet treating text as a formula.</summary>
    private static string Cell(string? value)
    {
        var text = value ?? string.Empty;

        if (text.Length > 0 && "=+-@\t\r".Contains(text[0], StringComparison.Ordinal))
        {
            text = "'" + text;
        }

        return $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Day(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>One row per invoice, with credit notes as negative income.</summary>
    private static string InvoiceRow(Invoice invoice)
    {
        var totals = InvoiceTotals.Of(invoice.Lines);
        var sign = invoice.Kind == InvoiceKind.CreditNote ? -1 : 1;
        var label = invoice.Kind == InvoiceKind.CreditNote ? "Credit note" : "Invoice";
        var vat = totals.Vat.Sum(rate => rate.Vat);

        return $"{Day(invoice.IssuedOn)},Income,{Cell(invoice.CustomerName)},{Cell($"{label} {invoice.Number:D4}")},{Cell(label)},{Amount(sign * totals.Net)},{Amount(sign * vat)},{Amount(sign * totals.Total)},{Day(invoice.PaidOn)}";
    }

    private static async Task<IResult> ExportAsync(GrowLogDbContext db, CancellationToken cancellationToken)
    {
        var takings = await db.TillTakings.AsNoTracking().Where(taking => taking.VoidedAt == null).ToListAsync(cancellationToken);
        var purchases = await LivePurchasesAsync(db, cancellationToken);
        var suppliers = await db.Suppliers.AsNoTracking().ToDictionaryAsync(supplier => supplier.Id, supplier => supplier.Name, cancellationToken);
        var invoices = await db.Invoices.AsNoTracking().Include(invoice => invoice.Lines).ToListAsync(cancellationToken);

        var rows = takings
            .Select(taking => (Date: taking.TakenOn, Line: $"{Day(taking.TakenOn)},Income,{Cell("Till takings")},,{Cell(taking.Notes)},{Amount(taking.Amount)},0.00,{Amount(taking.Amount)},"))
            .Concat(purchases.SelectMany(purchase => purchase.Lines.Select(line => (
                Date: purchase.PurchasedOn,
                Line: $"{Day(purchase.PurchasedOn)},Spending,{Cell(suppliers[purchase.SupplierId])},{Cell(purchase.Reference)},{Cell(line.Description)},{Amount(line.NetCost)},{Amount(line.Vat)},{Amount(line.NetCost + line.Vat)},{Day(purchase.PaidOn)}"))))
            .Concat(invoices.Select(invoice => (Date: invoice.IssuedOn, Line: InvoiceRow(invoice))))
            .OrderBy(row => row.Date)
            .Select(row => row.Line);

        var csv = new StringBuilder("Date,Type,Who,Reference,Description,Net,VAT,Total,Paid on\n");

        foreach (var row in rows)
        {
            csv.Append(row).Append('\n');
        }

        // The byte order mark tells Excel the file is UTF-8.
        return TypedResults.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv", "growlog-money.csv");
    }

    internal sealed record TakingRequest(DateOnly TakenOn, decimal Amount, string? Notes);

    internal sealed record TakingCreated(Guid Id);

    internal sealed record TakingResponse(Guid Id, DateOnly TakenOn, decimal Amount, string? Notes);

    internal sealed record MonthFigures(string Month, decimal Income, decimal Spending);

    /// <param name="Owed">Still owed to suppliers.</param>
    /// <param name="OwedToUs">Still owed by customers on trade invoices.</param>
    internal sealed record MoneySummary(IReadOnlyList<MonthFigures> Months, decimal Owed, decimal OwedToUs);
}
