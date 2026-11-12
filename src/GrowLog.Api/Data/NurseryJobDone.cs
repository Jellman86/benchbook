namespace GrowLog.Api.Data;

/// <summary>A job done, by whom and on which day: the record of the rota.</summary>
internal sealed class NurseryJobDone
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid JobId { get; init; }

    public required DateOnly DoneOn { get; init; }

    public required Guid DoneBy { get; init; }

    public DateTimeOffset CreatedAt { get; private set; }
}
