namespace GrowLog.Api.Data;

/// <summary>A saved copy of a species picture, so it is only downloaded once.</summary>
internal sealed class SpeciesPhoto
{
    public required Guid SpeciesId { get; init; }

    public required string ContentType { get; init; }

    public required byte[] Content { get; init; }
}
