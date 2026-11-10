using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrowLog.Api.Data;

/// <summary>
/// Stores events and works out stock from them. Stock is the sum of every change still in effect, each taking
/// effect at a time. A stock count says what was there at its time, so work from before a count that arrives
/// late, or is undone, is absorbed by that count rather than changing what the count found.
/// </summary>
internal sealed class EventStore(GrowLogDbContext db, TimeProvider clock)
{
    /// <summary>How far ahead of the server a device's clock may run before its time is not believed.</summary>
    private static readonly TimeSpan ClockAllowance = TimeSpan.FromMinutes(5);

    public Task<AppendResult> AppendAsync(Event newEvent, CancellationToken cancellationToken) =>
        AppendCoreAsync(newEvent, cancellationToken);

    /// <summary>Stores a stock count of one batch in one container, as it was when the count was made.</summary>
    public async Task<AppendResult> AppendStockCountAsync(Event stockCount, Guid batchId, int counted, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(counted);

        if (stockCount.Type != EventType.StockCounted || stockCount.ContainerId is not { } containerId)
        {
            throw new ArgumentException("A stock count must be a StockCounted event on a container.", nameof(stockCount));
        }

        // A count can only say how many of a batch are in a container the batch has been in.
        if (!await db.EventLines.AnyAsync(line => line.ContainerId == containerId && line.BatchId == batchId, cancellationToken))
        {
            return await db.Events.AnyAsync(e => e.Id == stockCount.Id, cancellationToken)
                ? AppendResult.AlreadyStored
                : AppendResult.NotInContainer;
        }

        stockCount.Payload = JsonSerializer.Serialize(new { batchId, counted });
        return await AppendCoreAsync(stockCount, cancellationToken);
    }

    public async Task<ContainerState> GetContainerStateAsync(Guid containerId, CancellationToken cancellationToken)
    {
        var contents = (await SumStockAsync(containerId, cancellationToken))
            .Select(row => new BatchCount(row.BatchId, row.Quantity))
            .ToList();

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

    /// <summary>Every batch in every container that still has plants.</summary>
    public Task<List<ContainerStock>> GetStockAsync(CancellationToken cancellationToken) =>
        SumStockAsync(containerId: null, cancellationToken);

    /// <summary>Where each container is and what pot size it's in, as its events have left it.</summary>
    public async Task<Dictionary<Guid, ContainerPlacement>> GetPlacementsAsync(List<Guid> containerIds, CancellationToken cancellationToken)
    {
        var events = await db.Events
            .Where(e => e.ContainerId != null
                && containerIds.Contains(e.ContainerId.Value)
                && (e.LocationId != null || e.PotSizeId != null)
                && !db.Events.Any(c => c.CorrectsEventId == e.Id))
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.RecordedAt)
            .Select(e => new { ContainerId = e.ContainerId!.Value, e.LocationId, e.PotSizeId })
            .ToListAsync(cancellationToken);

        return events
            .GroupBy(e => e.ContainerId)
            .ToDictionary(
                group => group.Key,
                group => new ContainerPlacement(
                    group.Select(e => e.LocationId).FirstOrDefault(id => id != null),
                    group.Select(e => e.PotSizeId).FirstOrDefault(id => id != null)));
    }

    /// <summary>What a stock count recorded, or null for counts made before counts kept their figure.</summary>
    internal static CountDetails? CountOf(string payload)
    {
        using var details = JsonDocument.Parse(payload);
        var root = details.RootElement;

        return root.TryGetProperty("batchId", out var batch) && root.TryGetProperty("counted", out var counted)
            ? new CountDetails(batch.GetGuid(), counted.GetInt32())
            : null;
    }

    private async Task<AppendResult> AppendCoreAsync(Event newEvent, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        if (newEvent.OccurredAt > now + ClockAllowance)
        {
            newEvent.OccurredAt = now;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var reversed = await ReversedKeysAsync(newEvent.CorrectsEventId, cancellationToken);
        var keys = newEvent.Lines.Select(line => new StockKey(line.ContainerId, line.BatchId))
            .Concat(reversed)
            .Concat(newEvent.ContainerId is { } containerId && CountOf(newEvent.Payload) is { } count && newEvent.Type == EventType.StockCounted
                ? [new StockKey(containerId, count.BatchId)]
                : [])
            .Distinct()
            .ToList();

        await LockContainersAsync(keys, newEvent.ContainerId, cancellationToken);

        // Checked first: a resend is not a new change, whatever has happened to the stock since.
        if (await AlreadyStoredAsync(newEvent, cancellationToken) is { } earlier)
        {
            return earlier;
        }

        foreach (var key in keys)
        {
            if (await SettleAsync(newEvent, key, cancellationToken) < 0)
            {
                return AppendResult.BelowZero;
            }
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
            return await AlreadyStoredAsync(newEvent, cancellationToken) ?? AppendResult.AlreadyStored;
        }
    }

    /// <summary>A resend of the same work, or a clash with different work under the same identifier.</summary>
    private async Task<AppendResult?> AlreadyStoredAsync(Event newEvent, CancellationToken cancellationToken)
    {
        var stored = await db.Events
            .AsNoTracking()
            .Where(e => e.Id == newEvent.Id)
            .Select(e => new { e.Type, e.RecordedBy, e.CorrectsEventId })
            .FirstOrDefaultAsync(cancellationToken);

        if (stored is null)
        {
            return null;
        }

        // The container isn't compared: work that makes a container makes a new one on each try.
        var same = stored.Type == newEvent.Type
            && stored.RecordedBy == newEvent.RecordedBy
            && stored.CorrectsEventId == newEvent.CorrectsEventId;

        return same ? AppendResult.AlreadyStored : AppendResult.IdInUse;
    }

    /// <summary>The stock an undo or correction takes away from, including what a reversed count absorbed.</summary>
    private async Task<List<StockKey>> ReversedKeysAsync(Guid? correctedId, CancellationToken cancellationToken)
    {
        if (correctedId is null)
        {
            return [];
        }

        var lines = await db.EventLines
            .Where(line => line.EventId == correctedId)
            .Select(line => new StockKey(line.ContainerId, line.BatchId))
            .ToListAsync(cancellationToken);

        var adjustments = await db.CountAdjustments
            .Where(adjustment => adjustment.EventId == correctedId || adjustment.CountEventId == correctedId)
            .Select(adjustment => new StockKey(adjustment.ContainerId, adjustment.BatchId))
            .ToListAsync(cancellationToken);

        var counted = await db.Events
            .Where(e => e.Id == correctedId && e.Type == EventType.StockCounted && e.ContainerId != null)
            .Select(e => new { ContainerId = e.ContainerId!.Value, e.Payload })
            .FirstOrDefaultAsync(cancellationToken);

        return [.. lines, .. adjustments, .. counted is not null && CountOf(counted.Payload) is { } count ? [new StockKey(counted.ContainerId, count.BatchId)] : Array.Empty<StockKey>()];
    }

    /// <summary>
    /// Makes every stock count of one batch in one container still hold with the new event in place, adding
    /// the difference to the new event as adjustments, and returns the stock that results.
    /// </summary>
    private async Task<int> SettleAsync(Event newEvent, StockKey key, CancellationToken cancellationToken)
    {
        var ignoring = newEvent.CorrectsEventId;
        var changes = await ChangesAsync(key, ignoring, cancellationToken);

        changes.AddRange(newEvent.Lines
            .Where(line => line.ContainerId == key.ContainerId && line.BatchId == key.BatchId)
            .Select(line => new TimedChange(line.EffectiveAt ?? newEvent.OccurredAt, line.QuantityChange)));

        var counts = await CountsAsync(key, ignoring, cancellationToken);

        if (newEvent.Type == EventType.StockCounted && CountOf(newEvent.Payload) is { } own && own.BatchId == key.BatchId && newEvent.ContainerId == key.ContainerId)
        {
            counts.Add(new TimedCount(newEvent.Id, newEvent.OccurredAt, own.Counted));
        }

        foreach (var count in counts.OrderBy(count => count.At))
        {
            var difference = count.Counted - changes.Where(change => change.At <= count.At).Sum(change => change.Quantity);

            if (difference == 0)
            {
                continue;
            }

            if (count.EventId == newEvent.Id)
            {
                newEvent.Lines.Add(new EventLine { ContainerId = key.ContainerId, BatchId = key.BatchId, QuantityChange = difference, EffectiveAt = count.At });
            }
            else
            {
                newEvent.Adjustments.Add(new CountAdjustment { ContainerId = key.ContainerId, BatchId = key.BatchId, CountEventId = count.EventId, QuantityChange = difference });
            }

            changes.Add(new TimedChange(count.At, difference));
        }

        return changes.Sum(change => change.Quantity);
    }

    /// <summary>Every change to one batch in one container still in effect, with when it takes effect.</summary>
    private async Task<List<TimedChange>> ChangesAsync(StockKey key, Guid? ignoring, CancellationToken cancellationToken)
    {
        var lines = await ActiveLines(ignoring)
            .Where(line => line.ContainerId == key.ContainerId && line.BatchId == key.BatchId)
            .Join(db.Events, line => line.EventId, e => e.Id, (line, e) => new TimedChange(line.EffectiveAt ?? e.OccurredAt, line.QuantityChange))
            .ToListAsync(cancellationToken);

        var adjustments = await ActiveAdjustments(ignoring)
            .Where(adjustment => adjustment.ContainerId == key.ContainerId && adjustment.BatchId == key.BatchId)
            .Join(db.Events, adjustment => adjustment.CountEventId, e => e.Id, (adjustment, e) => new TimedChange(e.OccurredAt, adjustment.QuantityChange))
            .ToListAsync(cancellationToken);

        return [.. lines, .. adjustments];
    }

    /// <summary>The stock counts of one batch in one container still in effect.</summary>
    private async Task<List<TimedCount>> CountsAsync(StockKey key, Guid? ignoring, CancellationToken cancellationToken)
    {
        var counts = await db.Events
            .AsNoTracking()
            .Where(e => e.Type == EventType.StockCounted
                && e.ContainerId == key.ContainerId
                && e.Id != ignoring
                && !db.Events.Any(c => c.CorrectsEventId == e.Id))
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.RecordedAt)
            .Select(e => new { e.Id, e.OccurredAt, e.Payload })
            .ToListAsync(cancellationToken);

        return counts
            .Select(e => (e.Id, e.OccurredAt, Count: CountOf(e.Payload)))
            .Where(e => e.Count is { } count && count.BatchId == key.BatchId)
            .Select(e => new TimedCount(e.Id, e.OccurredAt, e.Count!.Counted))
            .ToList();
    }

    private async Task<List<ContainerStock>> SumStockAsync(Guid? containerId, CancellationToken cancellationToken)
    {
        var lines = await ActiveLines(ignoring: null)
            .Where(line => containerId == null || line.ContainerId == containerId)
            .GroupBy(line => new { line.ContainerId, line.BatchId })
            .Select(group => new ContainerStock(group.Key.ContainerId, group.Key.BatchId, group.Sum(line => line.QuantityChange)))
            .ToListAsync(cancellationToken);

        var adjustments = await ActiveAdjustments(ignoring: null)
            .Where(adjustment => containerId == null || adjustment.ContainerId == containerId)
            .GroupBy(adjustment => new { adjustment.ContainerId, adjustment.BatchId })
            .Select(group => new ContainerStock(group.Key.ContainerId, group.Key.BatchId, group.Sum(adjustment => adjustment.QuantityChange)))
            .ToListAsync(cancellationToken);

        return lines.Concat(adjustments)
            .GroupBy(row => new StockKey(row.ContainerId, row.BatchId))
            .Select(group => new ContainerStock(group.Key.ContainerId, group.Key.BatchId, group.Sum(row => row.Quantity)))
            .Where(row => row.Quantity > 0)
            .ToList();
    }

    /// <summary>Lines still in effect: those of events that have not been reversed.</summary>
    private IQueryable<EventLine> ActiveLines(Guid? ignoring) =>
        db.EventLines.Where(line => line.EventId != ignoring && !db.Events.Any(c => c.CorrectsEventId == line.EventId));

    /// <summary>Adjustments still in effect: neither the event that caused them nor their count has been reversed.</summary>
    private IQueryable<CountAdjustment> ActiveAdjustments(Guid? ignoring) =>
        db.CountAdjustments.Where(adjustment => adjustment.EventId != ignoring
            && adjustment.CountEventId != ignoring
            && !db.Events.Any(c => c.CorrectsEventId == adjustment.EventId || c.CorrectsEventId == adjustment.CountEventId));

    /// <summary>Makes concurrent changes to the same container wait, so two people cannot both spend the same stock.</summary>
    private async Task LockContainersAsync(List<StockKey> keys, Guid? containerId, CancellationToken cancellationToken)
    {
        var containerIds = keys.Select(key => key.ContainerId)
            .Concat(containerId is { } id ? [id] : [])
            .Distinct()
            .ToArray();

        await db.Database.ExecuteSqlRawAsync(
            "SELECT 1 FROM containers WHERE id = ANY({0}) ORDER BY id FOR UPDATE",
            [containerIds],
            cancellationToken);
    }

    internal sealed record CountDetails(Guid BatchId, int Counted);

    private sealed record StockKey(Guid ContainerId, Guid BatchId);

    private sealed record TimedChange(DateTimeOffset At, int Quantity);

    private sealed record TimedCount(Guid EventId, DateTimeOffset At, int Counted);
}
