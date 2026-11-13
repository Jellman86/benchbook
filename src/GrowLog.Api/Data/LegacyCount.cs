namespace GrowLog.Api.Data;

/// <summary>The figure an early stock count recorded before count payloads were introduced.</summary>
internal sealed class LegacyCount
{
    public Guid EventId { get; init; }

    public Guid ContainerId { get; init; }

    public Guid BatchId { get; init; }

    public int Counted { get; init; }
}
