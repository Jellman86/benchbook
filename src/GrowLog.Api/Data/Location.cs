namespace GrowLog.Api.Data;

/// <summary>A site, a greenhouse or polytunnel within it, or a bench or bay within that.</summary>
internal sealed class Location : IAudited, IArchivable
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    public required LocationKind Kind { get; init; }

    public Guid? ParentId { get; init; }

    public DateTimeOffset? ArchivedAt { get; set; }
}
