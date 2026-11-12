namespace GrowLog.Api.Data;

/// <summary>
/// A job that comes round again, such as watering a house, feeding or spraying. Doing it moves the next date on
/// by its interval, never into the past.
/// </summary>
internal sealed class NurseryJob : IArchivable
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Title { get; set; }

    public string? Notes { get; set; }

    /// <summary>How many days until it comes round again.</summary>
    public required int EveryDays { get; set; }

    public required DateOnly NextDue { get; set; }

    /// <summary>Where the job is done, if it belongs to one place.</summary>
    public Guid? LocationId { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
