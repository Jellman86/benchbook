using System.Text.RegularExpressions;

namespace Hortiplanner.Api.Lookup;

/// <summary>
/// Pulls what a grower can use out of an article's plain text. The suggestions are simple
/// pattern matches, so a person always sees and confirms them before they reach a plant.
/// </summary>
internal static partial class ArticleText
{
    private const int MatchTimeout = 500;
    private const int MostDescription = 900;
    private const int MostGrowing = 2500;
    private const int OpeningLength = 800;

    /// <summary>Checks the opening of an article, where its subject is stated.</summary>
    public static bool IsAboutAPlant(string text) =>
        PlantWordsPattern().IsMatch(text.Length > OpeningLength ? text[..OpeningLength] : text);

    public static string? Description(string text)
    {
        var heading = HeadingPattern().Match(text);
        return Clip(WithoutCitations(heading.Success ? text[..heading.Index] : text), MostDescription);
    }

    public static string? Growing(string text)
    {
        var section = GrowingHeadingPattern().Match(text);

        if (!section.Success)
        {
            return null;
        }

        var body = text[(section.Index + section.Length)..];
        var next = HeadingPattern().Match(body);
        return Clip(WithoutCitations(next.Success ? body[..next.Index] : body), MostGrowing);
    }

    public static string? Position(string text)
    {
        var found = new List<string>(3);

        if (FullSunPattern().IsMatch(text))
        {
            found.Add("Full sun");
        }

        if (PartShadePattern().IsMatch(text))
        {
            found.Add("Part shade");
        }

        if (FullShadePattern().IsMatch(text))
        {
            found.Add("Full shade");
        }

        return found.Count == 0 ? null : string.Join(" or ", found);
    }

    public static string? Watering(string text)
    {
        if (DroughtPattern().IsMatch(text))
        {
            return "Drought tolerant once established";
        }

        var moist = MoistPattern().IsMatch(text);
        var drained = DrainedPattern().IsMatch(text);

        return (moist, drained) switch
        {
            (true, true) => "Moist, well-drained soil",
            (false, true) => "Well-drained soil",
            (true, false) => "Moist soil",
            _ => null,
        };
    }

    public static string? Hardiness(string text)
    {
        if (RhsPattern().Match(text) is { Success: true } rhs)
        {
            return $"RHS {rhs.Value}";
        }

        if (UsdaPattern().Match(text) is { Success: true } usda)
        {
            return $"USDA zone {usda.Groups["zone"].Value.Trim()}";
        }

        return DegreesPattern().Match(text) is { Success: true } degrees
            ? $"Hardy to −{degrees.Groups["degrees"].Value} °C"
            : null;
    }

    /// <summary>
    /// Wikipedia's plain text can leave a reference's words running on after a sentence,
    /// and empty brackets where a pronunciation was.
    /// </summary>
    private static string WithoutCitations(string text) =>
        EmptyBracketsPattern().Replace(CitationPattern().Replace(text, string.Empty), string.Empty).Trim();

    /// <summary>Stops at the last full sentence that fits.</summary>
    private static string? Clip(string text, int most)
    {
        if (text.Length == 0)
        {
            return null;
        }

        if (text.Length <= most)
        {
            return text;
        }

        var cut = text.LastIndexOf(". ", most, StringComparison.Ordinal);
        return cut > 0 ? text[..(cut + 1)] : text[..most];
    }

    [GeneratedRegex(@"^==+[^=\n]+==+\s*$", RegexOptions.Multiline, MatchTimeout)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^==+\s*(In cultivation|Cultivation(?: and [^=\n]+)?|Horticulture|Growing|Gardening|Garden use|Ornamental plants?|Ornamental use)\s*==+\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, MatchTimeout)]
    private static partial Regex GrowingHeadingPattern();

    [GeneratedRegex(@"\s*""[^""\n]{1,200}""\.[^\n]{0,200}?\bRetrieved [^.\n]{1,40}\.", RegexOptions.None, MatchTimeout)]
    private static partial Regex CitationPattern();

    [GeneratedRegex(@"\s*\(\s*[;,]?\s*\)", RegexOptions.None, MatchTimeout)]
    private static partial Regex EmptyBracketsPattern();

    [GeneratedRegex(@"\b(?:plants?|genus|species|shrubs?|trees?|flowering|herbs?|herbaceous|perennials?|annuals?|cultivars?|ferns?|grass(?:es)?|climbers?|succulents?|wildflowers?)\b", RegexOptions.IgnoreCase, MatchTimeout)]
    private static partial Regex PlantWordsPattern();

    [GeneratedRegex(@"\bfull sun\b", RegexOptions.IgnoreCase, MatchTimeout)]
    private static partial Regex FullSunPattern();

    [GeneratedRegex(@"\b(partial|part|dappled|light)[- ]shade\b", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, MatchTimeout)]
    private static partial Regex PartShadePattern();

    [GeneratedRegex(@"\b(full|deep) shade\b", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, MatchTimeout)]
    private static partial Regex FullShadePattern();

    [GeneratedRegex(@"\bdrought[- ]toleran", RegexOptions.IgnoreCase, MatchTimeout)]
    private static partial Regex DroughtPattern();

    [GeneratedRegex(@"\bmoist\b", RegexOptions.IgnoreCase, MatchTimeout)]
    private static partial Regex MoistPattern();

    [GeneratedRegex(@"\bwell[- ]drain", RegexOptions.IgnoreCase, MatchTimeout)]
    private static partial Regex DrainedPattern();

    [GeneratedRegex(@"\bH[1-7]\b", RegexOptions.None, MatchTimeout)]
    private static partial Regex RhsPattern();

    [GeneratedRegex(@"USDA (?:hardiness )?zones? (?<zone>\d+[ab]?(?:\s*(?:to|-|–)\s*\d+[ab]?)?)", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, MatchTimeout)]
    private static partial Regex UsdaPattern();

    [GeneratedRegex(@"hardy (?:down )?to\s*[−–-]\s*(?<degrees>\d+)\s*°\s*C", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, MatchTimeout)]
    private static partial Regex DegreesPattern();
}
