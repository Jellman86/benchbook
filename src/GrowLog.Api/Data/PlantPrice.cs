namespace GrowLog.Api.Data;

/// <summary>A plant's default price in one pot size.</summary>
internal sealed class PlantPrice
{
    public Guid PlantId { get; init; }

    public required Guid PotSizeId { get; init; }

    public required decimal Price { get; set; }
}
