namespace Hortiplanner.Api.Data;

internal sealed class PotSize : IAudited, IArchivable
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    /// <summary>How many of these empty pots the nursery has, as last counted by a person.</summary>
    public int? QuantityInStock { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }
}
