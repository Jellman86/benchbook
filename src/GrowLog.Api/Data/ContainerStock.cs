namespace GrowLog.Api.Data;

/// <param name="NotCounted">Sown without a count, and not counted since: the quantity is 0 because nobody knows it yet.</param>
internal sealed record ContainerStock(Guid ContainerId, Guid BatchId, int Quantity, bool NotCounted = false);
