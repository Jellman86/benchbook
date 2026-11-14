using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Data;

/// <summary>A replacement stands for the original kind of work at its original time.</summary>
internal static class EventHistory
{
    public static async Task<(EventType Type, DateTimeOffset At)> ResolveAsync(Event work, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        var seen = new HashSet<Guid>();
        while (work.Type == EventType.Corrected && work.CorrectsEventId is { } original && seen.Add(original))
        {
            work = await db.Events.AsNoTracking().SingleAsync(e => e.Id == original, cancellationToken);
        }
        return (work.Type, work.OccurredAt);
    }
}
