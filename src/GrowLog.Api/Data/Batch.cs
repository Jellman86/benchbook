namespace GrowLog.Api.Data;

/// <summary>Lineage only: one plant raised together. Where plants are now comes from containers and events.</summary>
internal sealed class Batch
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid PlantId { get; init; }

    public required string TraceabilityCode { get; init; }

    public required BatchSource Source { get; init; }

    /// <summary>The seed lot or supplier's reference.</summary>
    public string? SourceReference { get; init; }

    public Guid? SupplierId { get; init; }

    public Guid? ParentBatchId { get; init; }

    public string? MotherPlant { get; init; }

    /// <summary>Original country of origin as a two-letter ISO code.</summary>
    public required string OriginCountry { get; init; }

    public DateTimeOffset CreatedAt { get; private set; }
}
