namespace Hortiplanner.Api.Data;

/// <summary>What sort of place it is. Only a site stands on its own; anything else can sit inside any other place.</summary>
internal enum LocationKind
{
    Site,
    Greenhouse,
    Polytunnel,
    Bench,
    Bay,
    StandingBed,
    ColdFrame,
    SalesArea,
    Area,
}
