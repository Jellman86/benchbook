using System.Globalization;
using System.Text;

namespace GrowLog.Api.Transfer;

/// <summary>
/// Comma-separated text a spreadsheet opens as it is. Text that a spreadsheet would run as a formula gets an
/// apostrophe in front, so a name typed as "=HYPERLINK(...)" stays text.
/// </summary>
internal static class Csv
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

    private static string Defused(string text) =>
        text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? $"'{text}" : text;

    private static string Quote(string text) =>
        text.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : text;
}
