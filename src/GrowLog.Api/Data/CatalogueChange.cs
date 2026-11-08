namespace GrowLog.Api.Data;

/// <summary>A permanent record of one change to the catalogue.</summary>
internal sealed class CatalogueChange
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Entity { get; init; }

    public required string EntityKey { get; init; }

    public required string Action { get; init; }

    public required Guid ChangedBy { get; init; }

    public DateTimeOffset ChangedAt { get; private set; }

    /// <summary>Before and after values, as JSON. Personal data is noted but its values are not kept.</summary>
    public required string Changes { get; init; }
}
