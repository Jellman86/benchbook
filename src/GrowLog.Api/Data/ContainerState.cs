namespace GrowLog.Api.Data;

internal sealed record ContainerState(IReadOnlyList<BatchCount> Contents, Guid? LocationId, Guid? PotSizeId);
