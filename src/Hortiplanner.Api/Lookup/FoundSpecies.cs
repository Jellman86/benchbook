namespace Hortiplanner.Api.Lookup;

internal sealed record FoundSpecies(
    string ScientificName,
    string? CommonName,
    string Rank,
    string? MatchedTerm,
    string Source,
    string? Licence,
    long? InaturalistId,
    long? GbifKey,
    FoundPhoto? Photo,
    int Popularity);
