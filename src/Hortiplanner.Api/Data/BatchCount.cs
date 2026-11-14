namespace Hortiplanner.Api.Data;

internal sealed record BatchCount(Guid BatchId, int Quantity, bool NotCounted = false);
