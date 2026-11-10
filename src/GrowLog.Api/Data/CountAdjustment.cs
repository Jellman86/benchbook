namespace GrowLog.Api.Data;

/// <summary>
/// A change to stock that a stock count absorbs. When work from before a count reaches the server after it,
/// or is undone, the count still says what was there at the time, so the difference is put down to that count.
/// </summary>
internal sealed class CountAdjustment
{
    /// <summary>The event whose arrival made this adjustment necessary.</summary>
    public Guid EventId { get; init; }

    public required Guid ContainerId { get; init; }

    public required Guid BatchId { get; init; }

    /// <summary>The stock count this adjustment belongs to. It takes effect at that count's time.</summary>
    public required Guid CountEventId { get; init; }

    public required int QuantityChange { get; init; }
}
