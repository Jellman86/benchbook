using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrowLog.Api.Data;

internal sealed class EventStore(GrowLogDbContext db)
{
    public Task<AppendResult> AppendAsync(Event newEvent, CancellationToken cancellationToken) =>
        AppendCoreAsync(newEvent, prepare: null, cancellationToken);

    /// <summary>Stores a stock count, adding a line for the difference between the count and the derived quantity.</summary>
    public Task<AppendResult> AppendStockCountAsync(Event stockCount, Guid batchId, int counted, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(counted);

        if (stockCount.Type != EventType.StockCounted || stockCount.ContainerId is not { } containerId)
        {
            throw new ArgumentException("A stock count must be a StockCounted event on a container.", nameof(stockCount));
        }

        return AppendCoreAsync(
            stockCount,
            async () =>
            {
                var current = await ActiveLines(ignoring: null)
                    .Where(line => line.ContainerId == containerId && line.BatchId == batchId)
                    .SumAsync(line => line.QuantityChange, cancellationToken);

                stockCount.Lines.Add(new EventLine
                {
                    ContainerId = containerId,
                    BatchId = batchId,
                    QuantityChange = counted - current,
                });
            },
            cancellationToken);
    }

    public async Task<ContainerState> GetContainerStateAsync(Guid containerId, CancellationToken cancellationToken)
    {
        var contents = await ActiveLines(ignoring: null)
            .Where(line => line.ContainerId == containerId)
            .GroupBy(line => line.BatchId)
            .Where(group => group.Sum(line => line.QuantityChange) > 0)
            .Select(group => new BatchCount(group.Key, group.Sum(line => line.QuantityChange)))
            .ToListAsync(cancellationToken);

        // Ordered by when the work happened, so events arriving late from an offline device fall into place.
        var events = db.Events
            .Where(e => e.ContainerId == containerId && !db.Events.Any(c => c.CorrectsEventId == e.Id))
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.RecordedAt);

        var locationId = await events
            .Where(e => e.LocationId != null)
            .Select(e => e.LocationId)
            .FirstOrDefaultAsync(cancellationToken);

        var potSizeId = await events
            .Where(e => e.PotSizeId != null)
            .Select(e => e.PotSizeId)
            .FirstOrDefaultAsync(cancellationToken);

        return new ContainerState(contents, locationId, potSizeId);
    }

    private async Task<AppendResult> AppendCoreAsync(Event newEvent, Func<Task>? prepare, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var reversedLines = newEvent.CorrectsEventId is { } correctedId
            ? await db.Set<EventLine>().AsNoTracking().Where(line => line.EventId == correctedId).ToListAsync(cancellationToken)
            : [];

        await LockContainersAsync(newEvent, reversedLines, cancellationToken);

        // Checked first: a resend is not a new change, whatever has happened to the count since.
        if (await db.Events.AnyAsync(e => e.Id == newEvent.Id, cancellationToken))
        {
            return AppendResult.AlreadyStored;
        }

        if (prepare is not null)
        {
            await prepare();
        }

        if (await WouldGoBelowZeroAsync(newEvent, reversedLines, cancellationToken))
        {
            return AppendResult.BelowZero;
        }

        db.Events.Add(newEvent);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AppendResult.Stored;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "pk_events",
        })
        {
            db.ChangeTracker.Clear();
            return AppendResult.AlreadyStored;
        }
    }

    /// <summary>Lines still in effect: those of events that have not been reversed.</summary>
    private IQueryable<EventLine> ActiveLines(Guid? ignoring) =>
        db.Set<EventLine>().Where(line => line.EventId != ignoring && !db.Events.Any(c => c.CorrectsEventId == line.EventId));

    /// <summary>Makes concurrent changes to the same container wait, so two people cannot both spend the same stock.</summary>
    private async Task LockContainersAsync(Event newEvent, List<EventLine> reversedLines, CancellationToken cancellationToken)
    {
        var containerIds = newEvent.Lines.Select(line => line.ContainerId)
            .Concat(reversedLines.Select(line => line.ContainerId))
            .Concat(newEvent.ContainerId is { } containerId ? [containerId] : [])
            .Distinct()
            .ToArray();

        await db.Database.ExecuteSqlAsync(
            $"SELECT 1 FROM containers WHERE id = ANY({containerIds}) ORDER BY id FOR UPDATE",
            cancellationToken);
    }

    private async Task<bool> WouldGoBelowZeroAsync(Event newEvent, List<EventLine> reversedLines, CancellationToken cancellationToken)
    {
        var affected = newEvent.Lines.Concat(reversedLines)
            .Select(line => (line.ContainerId, line.BatchId))
            .Distinct()
            .ToList();

        var containerIds = affected.Select(key => key.ContainerId).Distinct().ToList();

        var current = await ActiveLines(newEvent.CorrectsEventId)
            .Where(line => containerIds.Contains(line.ContainerId))
            .GroupBy(line => new { line.ContainerId, line.BatchId })
            .Select(group => new { group.Key.ContainerId, group.Key.BatchId, Quantity = group.Sum(line => line.QuantityChange) })
            .ToListAsync(cancellationToken);

        return affected.Exists(key =>
            current.Where(c => c.ContainerId == key.ContainerId && c.BatchId == key.BatchId).Sum(c => c.Quantity)
            + newEvent.Lines.Where(l => l.ContainerId == key.ContainerId && l.BatchId == key.BatchId).Sum(l => l.QuantityChange) < 0);
    }
}
