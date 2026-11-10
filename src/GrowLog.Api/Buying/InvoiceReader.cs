using System.Globalization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace GrowLog.Api.Buying;

/// <summary>
/// Suggests a purchase from a supplier's PDF invoice: its reference, date and item lines. Only the
/// text in the PDF is read, nothing is sent anywhere, and a person checks everything before saving.
/// </summary>
internal static partial class InvoiceReader
{
    private const int MatchTimeout = 500;
    private const int MostLines = 60;
    private const int MostPages = 10;
    private const double SameLine = 2.5;

    public static InvoiceDraft Read(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        var lines = document.GetPages().Take(MostPages).SelectMany(TextLines).ToList();
        var text = string.Join('\n', lines);

        return new InvoiceDraft(
            ReferencePattern().Match(text) is { Success: true } reference ? reference.Groups["reference"].Value : null,
            FindDate(text),
            lines.Select(ItemOf).OfType<DraftLine>().Take(MostLines).ToList());
    }

    /// <summary>Words gathered into lines by where they sit on the page, top to bottom.</summary>
    private static IEnumerable<string> TextLines(Page page)
    {
        var rows = new List<(double Y, List<Word> Words)>();

        foreach (var word in page.GetWords().OrderByDescending(word => word.BoundingBox.Bottom))
        {
            var row = rows.FirstOrDefault(entry => Math.Abs(entry.Y - word.BoundingBox.Bottom) < SameLine);

            if (row.Words is null)
            {
                rows.Add((word.BoundingBox.Bottom, [word]));
            }
            else
            {
                row.Words.Add(word);
            }
        }

        return rows.Select(row => string.Join(' ', row.Words.OrderBy(word => word.BoundingBox.Left).Select(word => word.Text)));
    }

    /// <summary>A line with words, a quantity and a final amount, that isn't a total.</summary>
    private static DraftLine? ItemOf(string line)
    {
        if (NotAnItemPattern().IsMatch(line) || AmountAtEndPattern().Match(line) is not { Success: true } amount)
        {
            return null;
        }

        var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var description = string.Join(' ', tokens.Where(token => token.Any(char.IsLetter)));
        var quantity = tokens.Select(token => int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : (int?)null).FirstOrDefault(number => number is > 0 and < 100_000);

        if (description.Length < 2 || quantity is null)
        {
            return null;
        }

        var net = decimal.Parse(amount.Groups["amount"].Value.Replace(",", string.Empty, StringComparison.Ordinal), NumberStyles.Number, CultureInfo.InvariantCulture);
        return new DraftLine(description, quantity, net);
    }

    private static DateOnly? FindDate(string text)
    {
        foreach (Match match in DatePattern().Matches(text))
        {
            if (DateOnly.TryParseExact(match.Value, ["dd/MM/yyyy", "d/M/yyyy", "d MMMM yyyy", "d MMM yyyy", "dd.MM.yyyy", "yyyy-MM-dd"], CultureInfo.GetCultureInfo("en-GB"), DateTimeStyles.None, out var date))
            {
                return date;
            }
        }

        return null;
    }

    [GeneratedRegex(@"(?:invoice|inv)\s*(?:no\.?|number|#|ref)?\s*[:#]?\s*(?<reference>[A-Z0-9][A-Z0-9/\-]{2,})", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, MatchTimeout)]
    private static partial Regex ReferencePattern();

    [GeneratedRegex(@"\b(?:\d{1,2}/\d{1,2}/\d{4}|\d{1,2}\.\d{1,2}\.\d{4}|\d{4}-\d{2}-\d{2}|\d{1,2} [A-Z][a-z]+ \d{4})\b", RegexOptions.None, MatchTimeout)]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"£?\s*(?<amount>\d{1,3}(?:,\d{3})*\.\d{2})\s*$", RegexOptions.ExplicitCapture, MatchTimeout)]
    private static partial Regex AmountAtEndPattern();

    [GeneratedRegex(@"\b(?:sub-?total|total|vat|balance|amount due|paid|payment|discount)\b", RegexOptions.IgnoreCase, MatchTimeout)]
    private static partial Regex NotAnItemPattern();
}
