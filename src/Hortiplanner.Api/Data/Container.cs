namespace Hortiplanner.Api.Data;

/// <summary>A labelled physical unit. Its location, pot size and contents are derived from events.</summary>
internal sealed class Container
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string LabelCode { get; init; }

    public required ContainerKind Kind { get; init; }

    public DateTimeOffset CreatedAt { get; private set; }
}
