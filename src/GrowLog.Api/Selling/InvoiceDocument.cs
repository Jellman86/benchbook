using System.Globalization;
using GrowLog.Api.Data;

namespace GrowLog.Api.Selling;

/// <summary>What an invoice or credit note shows, as data for its template.</summary>
internal static class InvoiceDocument
{
    private static readonly CultureInfo British = CultureInfo.GetCultureInfo("en-GB");

    private static string Day(DateOnly? day) => day?.ToString("d MMMM yyyy", British) ?? string.Empty;

    private static string Rate(decimal rate) => string.Create(CultureInfo.InvariantCulture, $"{rate:0.##}%");

    private static string[] LinesOf(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    public static object Describe(Invoice invoice, NurserySettings nursery, int? creditedNumber)
    {
        var totals = InvoiceTotals.Of(invoice.Lines);
        var isCredit = invoice.Kind == InvoiceKind.CreditNote;

        return new
        {
            title = isCredit ? "Credit note" : "Invoice",
            number = invoice.Number.ToString("D4", CultureInfo.InvariantCulture),
            credits = creditedNumber is { } number ? $"Cancels invoice {number:D4}" : string.Empty,
            issued = Day(invoice.IssuedOn),
            due = isCredit ? string.Empty : Day(invoice.DueOn),
            paid = invoice.PaidOn is null ? string.Empty : $"Paid {Day(invoice.PaidOn)}",
            vatCharged = invoice.VatCharged,
            seller = new
            {
                name = invoice.SellerName ?? nursery.NurseryName ?? string.Empty,
                address = LinesOf(invoice.SellerAddress ?? nursery.Address),
                contact = invoice.SellerContact ?? string.Join(" · ", new[] { nursery.Phone, nursery.Email, nursery.Website }.Where(part => !string.IsNullOrWhiteSpace(part))),
                vat = invoice.VatCharged && !string.IsNullOrWhiteSpace(invoice.SellerVatNumber ?? nursery.VatNumber) ? $"VAT number {invoice.SellerVatNumber ?? nursery.VatNumber}" : string.Empty,
            },
            buyer = new
            {
                name = invoice.CustomerName,
                address = LinesOf(invoice.CustomerAddress),
                vat = string.IsNullOrWhiteSpace(invoice.CustomerVatNumber) ? string.Empty : $"VAT number {invoice.CustomerVatNumber}",
            },
            lines = invoice.Lines.OrderBy(line => line.Position).Select(line => new
            {
                description = line.Description,
                quantity = line.Quantity.ToString(CultureInfo.InvariantCulture),
                unit = InvoiceEndpoints.Format(line.UnitPrice),
                rate = Rate(line.VatRate),
                net = InvoiceEndpoints.Format(InvoiceTotals.LineNet(line)),
            }).ToList(),
            net = InvoiceEndpoints.Format(totals.Net),
            vat = totals.Vat.Select(rate => new { rate = Rate(rate.Rate), vat = InvoiceEndpoints.Format(rate.Vat) }).ToList(),
            total = InvoiceEndpoints.Format(totals.Total),
            notes = invoice.Notes ?? string.Empty,
        };
    }
}
