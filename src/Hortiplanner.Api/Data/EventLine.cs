namespace Hortiplanner.Api.Data;

/// <summary>How an event changed the number of plants of one batch in one container.</summary>
internal sealed class EventLine
{
    public Guid EventId { get; init; }

    public required Guid ContainerId { get; init; }

    public required Guid BatchId { get; init; }

    public required int QuantityChange { get; init; }

    /// <summary>When the change takes effect, if not when its event happened: a correction's replacement takes the original's time.</summary>
    public DateTimeOffset? EffectiveAt
    {
        get;
        init => field = value?.ToUniversalTime();
    }
}
