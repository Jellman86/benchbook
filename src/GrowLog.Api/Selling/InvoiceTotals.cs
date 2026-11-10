using GrowLog.Api.Data;

namespace GrowLog.Api.Selling;

/// <summary>An invoice's sums: net, VAT for each rate (rounded per rate, as invoices show it), and the total.</summary>
internal sealed record InvoiceTotals(decimal Net, IReadOnlyList<VatAtRate> Vat, decimal Total)
{
    public static decimal LineNet(InvoiceLine line) => line.Quantity * line.UnitPrice;

    public static InvoiceTotals Of(IEnumerable<InvoiceLine> lines)
    {
        var list = lines.ToList();
        var vat = list
            .Where(line => line.VatRate > 0)
            .GroupBy(line => line.VatRate)
            .OrderByDescending(group => group.Key)
            .Select(group =>
            {
                var net = group.Sum(LineNet);
                return new VatAtRate(group.Key, net, Math.Round(net * group.Key / 100m, 2, MidpointRounding.AwayFromZero));
            })
            .ToList();

        var totalNet = list.Sum(LineNet);
        return new InvoiceTotals(totalNet, vat, totalNet + vat.Sum(rate => rate.Vat));
    }
}
