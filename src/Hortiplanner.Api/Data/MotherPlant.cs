namespace Hortiplanner.Api.Data;

/// <summary>
/// A plant kept for taking cuttings from. Each batch of cuttings taken from it is numbered in turn, so every
/// cutting leads back to its mother and the mother lists every batch taken.
/// </summary>
internal sealed class MotherPlant : IAudited, IArchivable
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid PlantId { get; set; }

    /// <summary>What it's called at the nursery, such as "Hidcote by the door".</summary>
    public required string Name { get; set; }

    public Guid? LocationId { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
