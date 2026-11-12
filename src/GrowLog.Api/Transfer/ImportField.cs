namespace GrowLog.Api.Transfer;

/// <summary>A column GrowLog can take in, with other names a spreadsheet might give it, for matching.</summary>
internal sealed record ImportField(string Key, string Label, FieldType Type, bool Required, IReadOnlyList<string> Aliases, int Longest = 200);
