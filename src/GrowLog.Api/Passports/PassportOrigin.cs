using GrowLog.Api.Data;

namespace GrowLog.Api.Passports;

/// <summary>
/// The country of origin a passport gives (Part D), worked out when it is issued. Plants count as grown on here,
/// and so take "GB", once they are potted on, pricked out or split here, or after two weeks under cover or four
/// weeks outside. Xylella hosts take "GB" only after a year here; a plant nobody has checked is treated the same
/// way, rather than assumed not to be a host. Plants raised here from seed or cuttings are GB from the start.
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
            return new(GreatBritain, "Raised here from seed or cuttings, so grown in Great Britain.");
        }

        if (string.Equals(facts.OriginCountry, GreatBritain, StringComparison.Ordinal))
        {
            return new(GreatBritain, "Bought in from Great Britain.");
        }

        var here = facts.Now - facts.ArrivedAt;

        // A plant nobody has checked is treated as a host, not assumed safe.
        var host = facts.XylellaHost switch
        {
            true => "A Xylella host",
            null => "Not yet checked as a Xylella host, so treated as one: it",
            false => null,
        };

        if (host is { } what)
        {
            return here >= HostWait
                ? new(GreatBritain, $"{what} has been here over a year, so it is now grown in Great Britain.")
                : new(facts.OriginCountry, $"{what} takes GB only after a year here, so it keeps {facts.OriginCountry} until then.");
        }

        if (facts.ChangedSince)
        {
            return new(GreatBritain, "Potted on, pricked out or split here, so grown on in Great Britain.");
        }

        if (here >= Outside)
        {
            return new(GreatBritain, "Here four weeks or more, so grown on in Great Britain.");
        }

        if (facts.UnderCover && here >= UnderCover)
        {
            return new(GreatBritain, "Under cover here two weeks or more, so grown on in Great Britain.");
        }

        return new(facts.OriginCountry, $"Not grown on here yet (two weeks under cover, four outside, or potting on), so it keeps {facts.OriginCountry}.");
    }

    /// <param name="ArrivedAt">When the plants arrived, or were sown or taken as cuttings.</param>
    /// <param name="ChangedSince">Whether they have been potted on, pricked out or split here since.</param>
    /// <param name="UnderCover">Whether they are in a greenhouse, polytunnel or cold frame now.</param>
    internal sealed record Facts(
        BatchSource Source,
        string OriginCountry,
        DateTimeOffset ArrivedAt,
        DateTimeOffset Now,
        bool ChangedSince,
        bool UnderCover,
        bool? XylellaHost);

    internal sealed record Decision(string Country, string Rule);
}
