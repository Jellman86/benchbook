namespace GrowLog.Api.Data;

/// <summary>A record of buying from a supplier. A mistake is voided and entered again, never edited.</summary>
internal sealed class Purchase : IAudited
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid SupplierId { get; init; }

    public required DateOnly PurchasedOn { get; init; }

    /// <summary>The supplier's invoice or order reference.</summary>
    public string? Reference { get; init; }

    public string? Notes { get; init; }

    /// <summary>Null while it is still owed.</summary>
    public DateOnly? PaidOn { get; set; }

    public DateTimeOffset? VoidedAt { get; set; }

    public required Guid RecordedBy { get; init; }

    public string? RequestFingerprint { get; init; }

    public DateTimeOffset CreatedAt { get; private set; }

    public ICollection<PurchaseLine> Lines { get; } = new List<PurchaseLine>();
}
