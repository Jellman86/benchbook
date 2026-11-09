namespace GrowLog.Api.Data;

/// <summary>Something the nursery has learned about growing a plant. Notes are only ever added to.</summary>
internal sealed class PlantNote
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid PlantId { get; init; }

    public required string Text { get; init; }

    public required Guid AddedBy { get; init; }

    public DateTimeOffset CreatedAt { get; private set; }
}
