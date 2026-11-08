namespace GrowLog.Api.Data;

/// <summary>One day's total from the shop till. Square remains the record of the card payments.</summary>
internal sealed class TillTaking : IAudited
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required DateOnly TakenOn { get; init; }

    public required decimal Amount { get; init; }

    public string? Notes { get; init; }

    public DateTimeOffset? VoidedAt { get; set; }

    public required Guid RecordedBy { get; init; }

    public DateTimeOffset CreatedAt { get; private set; }
}
