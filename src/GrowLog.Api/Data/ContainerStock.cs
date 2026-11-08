namespace GrowLog.Api.Data;

internal sealed record ContainerStock(Guid ContainerId, Guid BatchId, int Quantity);
