namespace GrowLog.Api.Data;

/// <summary>How an event changed the number of plants of one batch in one container.</summary>
internal sealed class EventLine
{
    public Guid EventId { get; init; }

    public required Guid ContainerId { get; init; }

    public required Guid BatchId { get; init; }

    public required int QuantityChange { get; init; }
}
