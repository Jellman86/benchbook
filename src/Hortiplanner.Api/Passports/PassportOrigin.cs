using Hortiplanner.Api.Data;

namespace Hortiplanner.Api.Passports;

/// <summary>
/// The country of origin a passport gives (Part D), worked out when it is issued. Plants count as grown on here,
/// and so take "GB", once they are potted on, pricked out or split here, or after two weeks under cover or four
/// weeks outside. Xylella hosts and Ceratocystis platani hosts (plane trees) take "GB" only after a year here; a
/// plant nobody has checked for Xylella is treated as a host rather than assumed not to be one. The two and four
/// weeks count only in active growth, which the person must confirm at issue. Plants raised here from seed
/// or cuttings are GB from the start.
/// </summary>
internal static class PassportOrigin
{
    public const string GreatBritain = "GB";

    private static readonly TimeSpan UnderCover = TimeSpan.FromDays(14);
    private static readonly TimeSpan Outside = TimeSpan.FromDays(28);
    private static readonly TimeSpan HostWait = TimeSpan.FromDays(365);

    public static Decision Decide(Facts facts)
    {
        if (facts.Source != BatchSource.BoughtIn)
        {
            return new(GreatBritain, "They were raised here from seed or cuttings, so they were grown in Great Britain.");
        }

        if (string.Equals(facts.OriginCountry, GreatBritain, StringComparison.Ordinal))
        {
            return new(GreatBritain, "They were bought in from Great Britain.");
        }

        var here = facts.Now - facts.ArrivedAt;

        if (HostWording(facts) is { } what)
        {
            return here >= HostWait
                ? new(GreatBritain, $"{what} has been here over a year, so it is now grown in Great Britain.")
                : new(facts.OriginCountry, $"{what} takes GB only after a year here, so it keeps {facts.OriginCountry} until then.");
        }

        if (facts.ChangedSince)
        {
            return new(GreatBritain, "They were potted on, pricked out or split here, so they were grown on in Great Britain.");
        }

        var underCover = facts.UnderCoverSince is { } since && facts.Now - since >= UnderCover;
        var outside = facts.OutsideSince is { } outdoors && facts.Now - outdoors >= Outside;
        if (underCover || outside)
        {
            var time = underCover ? "at least two weeks under cover without a break" : "at least four weeks outside without a break";
            return new(facts.ActiveGrowthConfirmed ? GreatBritain : facts.OriginCountry,
                facts.ActiveGrowthConfirmed ? $"They were confirmed in active growth for {time}, so they were grown on in Great Britain."
                    : $"Confirm active growth for {time} to use GB. Otherwise they keep {facts.OriginCountry}.", true);
        }
        return new(facts.OriginCountry, $"They haven't been grown on here yet, so they keep {facts.OriginCountry}.");
    }

    /// <summary>How a plant that waits a year is described, or null if it doesn't. One nobody has checked is treated as a host.</summary>
    private static string? HostWording(Facts facts)
    {
        if (!facts.XylellaHost.HasValue)
        {
            return "This plant hasn't been checked as a Xylella host yet, so it is treated as one. It";
        }

        if (facts.XylellaHost.Value)
        {
            return "A Xylella host";
        }

        return facts.CeratocystisHost ? "A plane tree, a Ceratocystis platani host," : null;
    }

    /// <param name="ArrivedAt">When the plants arrived, or were sown or taken as cuttings.</param>
    /// <param name="ChangedSince">Whether they have been potted on, pricked out or split here since.</param>
    /// <param name="UnderCoverSince">Since when the tray has been in a greenhouse, polytunnel or cold frame without a break, or null if it isn't now.</param>
    /// <param name="CeratocystisHost">Whether the plant is a plane tree, which hosts Ceratocystis platani.</param>
    internal sealed record Facts(
        BatchSource Source,
        string OriginCountry,
        DateTimeOffset ArrivedAt,
        DateTimeOffset Now,
        bool ChangedSince,
        DateTimeOffset? UnderCoverSince,
        bool? XylellaHost,
        bool CeratocystisHost = false,
        bool ActiveGrowthConfirmed = false,
        DateTimeOffset? OutsideSince = null);

    internal sealed record Decision(string Country, string Rule, bool NeedsActiveGrowthConfirmation = false);
}
