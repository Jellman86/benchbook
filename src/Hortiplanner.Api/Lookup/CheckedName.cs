namespace Hortiplanner.Api.Lookup;

internal sealed record CheckedName(string AcceptedName, long GbifKey, string? Family);
