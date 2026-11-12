namespace GrowLog.Api.Data;

/// <summary>
/// Lineage only: one plant raised together. Where plants are now comes from containers and events.
/// Details can be corrected, and every correction is kept as a <see cref="CatalogueChange"/>.
/// </summary>
internal sealed class Batch
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid PlantId { get; set; }

    public required string TraceabilityCode { get; init; }

    public required BatchSource Source { get; set; }

    /// <summary>The seed lot or supplier's reference.</summary>
    public string? SourceReference { get; set; }

    public Guid? SupplierId { get; set; }

    /// <summary>The purchase line these plants arrived on, if there is one.</summary>
    public Guid? PurchaseLineId { get; init; }

    public Guid? ParentBatchId { get; init; }

    public string? MotherPlant { get; set; }

    /// <summary>The sow-by or use-by date printed on a seed packet: the last day of the month it gives.</summary>
    public DateOnly? SowBy { get; set; }

    /// <summary>Original country of origin as a two-letter ISO code.</summary>
    public required string OriginCountry { get; set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
