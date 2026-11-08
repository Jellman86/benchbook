namespace GrowLog.Api.Data;

internal sealed class PotSize
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }
}
