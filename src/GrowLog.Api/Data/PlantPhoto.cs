namespace GrowLog.Api.Data;

/// <summary>The nursery's own picture of a plant, shown in place of any species picture.</summary>
internal sealed class PlantPhoto
{
    public required Guid PlantId { get; init; }

    public required string ContentType { get; set; }

    public required byte[] Content { get; set; }
}
