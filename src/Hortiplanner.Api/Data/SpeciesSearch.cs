namespace Hortiplanner.Api.Data;

/// <summary>Remembers what has been searched for online, and when, so it isn't asked again.</summary>
internal sealed class SpeciesSearch
{
    public required string Query { get; init; }

    public DateTimeOffset SearchedAt { get; set; }
}
