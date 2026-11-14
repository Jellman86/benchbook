using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Hortiplanner.Api.Transfer;

/// <summary>
/// Comma-separated text a spreadsheet opens as it is. Text that a spreadsheet would run as a formula gets an
/// apostrophe in front, so a name typed as "=HYPERLINK(...)" stays text.
/// </summary>
internal static partial class Csv
{
    public static string Write(IReadOnlyList<string> header, IEnumerable<object?[]> rows)
    {
        var text = new StringBuilder();
        text.AppendJoin(',', header.Select(Quote)).Append("\r\n");

        foreach (var row in rows)
        {
            text.AppendJoin(',', row.Select(Cell)).Append("\r\n");
        }

        return text.ToString();
    }

    internal static string Cell(object? value) => value switch
    {
        null => "",
        string text => Quote(Defused(text)),
        bool yes => yes ? "Yes" : "No",
        DateOnly day => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset moment => moment.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => Quote(Defused(value.ToString() ?? "")),
    };

    /// <summary>
    /// A cell a spreadsheet would run as a formula gets an apostrophe. A phone number or a plain number with a sign,
    /// such as "+44 1629 123456", can't run, so it's left as it is.
    /// </summary>
    private static string Defused(string text) =>
        text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' && !PlainNumber().IsMatch(text) ? $"'{text}" : text;

    [GeneratedRegex(@"^[+\-][0-9 ().]+$", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex PlainNumber();

    private static string Quote(string text) =>
        text.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : text;
}
