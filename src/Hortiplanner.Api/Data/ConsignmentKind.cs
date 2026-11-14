namespace Hortiplanner.Api.Data;

/// <summary>Where plants are going, which decides whether they need a plant passport.</summary>
internal enum ConsignmentKind
{
    /// <summary>To another business: a passport is needed.</summary>
    TradeSale,

    /// <summary>Sold at a distance, by post, courier or online, to anyone: a passport is needed.</summary>
    DistanceSale,

    /// <summary>Sold in person to someone who will grow them themselves: no passport.</summary>
    InPerson,

    /// <summary>To another of the nursery's own sites.</summary>
    OwnSite,
}
