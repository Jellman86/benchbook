namespace Hortiplanner.Api.Data;

/// <summary>A plant passport as printed on a supplier's label. Only its status can change.</summary>
internal sealed class ReceivedPassport : IAudited
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid BatchId { get; init; }

    /// <summary>Who supplied it, which operators must record.</summary>
    public required Guid SupplierId { get; init; }

    public required DateOnly ReceivedOn { get; init; }

    public required string PartA { get; init; }

    public required string PartB { get; init; }

    /// <summary>Can be absent on plants ready for final sale.</summary>
    public string? PartC { get; init; }

    public required string PartD { get; init; }

    public string? PartE { get; init; }

    public ReceivedPassportStatus Status { get; set; } = ReceivedPassportStatus.Held;

    public required Guid RecordedBy { get; init; }

    public DateTimeOffset CreatedAt { get; private set; }
}
