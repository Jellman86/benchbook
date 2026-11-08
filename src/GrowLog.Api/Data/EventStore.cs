using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrowLog.Api.Data;

internal sealed class EventStore(GrowLogDbContext db)
{
    /// <summary>Stores an event.</summary>
    /// <returns>False if an event with this identifier is already stored, so an offline client can safely resend.</returns>
    public async Task<bool> AppendAsync(Event newEvent, CancellationToken cancellationToken)
    {
        db.Events.Add(newEvent);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "pk_events",
        })
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
