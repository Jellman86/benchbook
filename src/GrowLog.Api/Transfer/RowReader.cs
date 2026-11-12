using System.Globalization;

namespace GrowLog.Api.Transfer;

/// <summary>One spreadsheet row's values, read by field, noting each problem in words as it goes.</summary>
internal sealed class RowReader(IReadOnlyDictionary<string, string?> values, ImportKind kind)
{
    private static readonly string[] Yes = ["yes", "y", "true", "1", "x"];

    private static readonly string[] No = ["no", "n", "false", "0"];

    public List<string> Problems { get; } = [];

    public string? Text(string key)
    {
        var field = ImportFields.Find(kind, key);
        var value = values.GetValueOrDefault(key)?.Trim();

        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (value.Length > field.Longest)
        {
            Problems.Add($"{field.Label} is longer than {field.Longest} letters.");
            return null;
        }

        return value;
    }

    public string? Required(string key)
    {
        var value = Text(key);

        if (value is null && Problems.Count == 0)
        {
            Problems.Add($"{ImportFields.Find(kind, key).Label} is empty.");
        }

        return value;
    }

    public bool? YesNo(string key)
    {
        var value = values.GetValueOrDefault(key)?.Trim();

        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (Yes.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (No.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        Problems.Add($"{ImportFields.Find(kind, key).Label} should be yes or no, not \"{value}\".");
        return null;
    }

    public int? Number(string key, int least, int most)
    {
        var value = values.GetValueOrDefault(key)?.Trim();

        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number >= least && number <= most)
        {
            return number;
        }

        Problems.Add($"{ImportFields.Find(kind, key).Label} should be a whole number from {least} to {most}, not \"{value}\".");
        return null;
    }
}
