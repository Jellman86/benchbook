using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrowLog.Api.Data;

/// <summary>
/// Stores events and works out stock from them. Stock is the sum of every change still in effect, each taking
/// effect at a time. A stock count says what was there at its time, so work from before a count that arrives
/// late, or is undone, is absorbed by that count rather than changing what the count found.
/// </summary>
internal sealed class EventStore(GrowLogDbContext db, TimeProvider clock, DeviceClock? device = null)
{
    /// <summary>How far ahead of the server a device's clock may run before its time is not believed.</summary>
    private static readonly TimeSpan ClockAllowance = TimeSpan.FromMinutes(5);

    public Task<AppendResult> AppendAsync(Event newEvent, CancellationToken cancellationToken) =>
        AppendCoreAsync(newEvent, cancellationToken);

    /// <summary>Stores a stock count of one batch in one container, as it was when the count was made.</summary>
    public async Task<AppendResult> AppendStockCountAsync(Event stockCount, Guid batchId, int counted, CancellationToken cancellationToken, bool rough = false)
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

        stockCount.Payload = rough
            ? JsonSerializer.Serialize(new { batchId, counted, rough })
            : JsonSerializer.Serialize(new { batchId, counted });
        return await AppendCoreAsync(stockCount, cancellationToken);
    }

    /// <summary>
    /// Batches in containers whose latest count was only a rough guess, as when seedlings are estimated as they
    /// come up. Pricking out from them still asks how many were taken and how many are left.
    /// </summary>
    public async Task<HashSet<(Guid ContainerId, Guid BatchId)>> RoughlyCountedAsync(IReadOnlyCollection<Guid> containerIds, CancellationToken cancellationToken)
    {
        var counts = await db.Events
            .AsNoTracking()
            .Where(e => e.Type == EventType.StockCounted
                && e.ContainerId != null
                && containerIds.Contains(e.ContainerId.Value)
                && !db.Events.Any(c => c.CorrectsEventId == e.Id))
            .Select(e => new { ContainerId = e.ContainerId!.Value, e.OccurredAt, e.RecordedAt, e.Payload })
            .ToListAsync(cancellationToken);

        return [.. counts
            .Select(e => (e.ContainerId, e.OccurredAt, e.RecordedAt, Count: CountOf(e.Payload), Rough: IsRough(e.Payload)))
            .Where(e => e.Count is not null)
            .GroupBy(e => (e.ContainerId, e.Count!.BatchId))
            .Where(group => group.OrderBy(e => e.OccurredAt).ThenBy(e => e.RecordedAt).Last().Rough)
            .Select(group => group.Key)];
    }

    internal static bool IsRough(string payload)
    {
        using var details = JsonDocument.Parse(payload);
        return details.RootElement.TryGetProperty("rough", out var rough) && rough.ValueKind == JsonValueKind.True;
    }

    public async Task<ContainerState> GetContainerStateAsync(Guid containerId, CancellationToken cancellationToken)
    {
        var contents = (await SumStockAsync(containerId, cancellationToken))
            .Select(row => new BatchCount(row.BatchId, row.Quantity, row.NotCounted))
            .ToList();

        // Ordered by when the work happened, so events arriving late from an offline device fall into place.
        // A correction stands where the work it replaces stood.
        var events = db.Events
            .Where(e => e.ContainerId == containerId && !db.Events.Any(c => c.CorrectsEventId == e.Id))
            .OrderByDescending(e => e.CorrectsEventId == null ? e.OccurredAt : db.Events.Where(o => o.Id == e.CorrectsEventId).Select(o => o.OccurredAt).First())
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

    /// <summary>Whether a batch in a container was sown without a count and hasn't been counted since.</summary>
    public async Task<bool> IsNotCountedAsync(Guid containerId, Guid batchId, CancellationToken cancellationToken) =>
        (await NotCountedAsync(containerId, cancellationToken)).Contains(new StockKey(containerId, batchId));

    /// <summary>
    /// Records a count of a batch in a container just before other work on it, as when seedlings are counted while
    /// they are pricked out of a tray nobody had counted. The count is placed strictly before the work, on the same
    /// clock, so it never absorbs the work it was made for. Both are kept or neither.
    /// </summary>
    public async Task<AppendResult> AppendCountThenAsync(Event stockCount, Guid batchId, int counted, Event then, CancellationToken cancellationToken)
    {
        PutOnServerClock(then);
        stockCount.OccurredAt = then.OccurredAt.AddSeconds(-1);
        stockCount.Payload = JsonSerializer.Serialize(new { batchId, counted });

        var outer = db.Database.CurrentTransaction is not null;
        await using var transaction = outer ? null : await db.Database.BeginTransactionAsync(cancellationToken);

        var first = await AppendCoreAsync(stockCount, cancellationToken, alreadyOnServerClock: true);

        if (first is not (AppendResult.Stored or AppendResult.AlreadyStored))
        {
            return first;
        }

        var second = await AppendCoreAsync(then, cancellationToken, alreadyOnServerClock: true);

        if (transaction is not null && second is AppendResult.Stored or AppendResult.AlreadyStored)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return second;
    }

    /// <summary>Every batch in every container that still has plants, or was sown and hasn't been counted yet.</summary>
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
            .OrderByDescending(e => e.CorrectsEventId == null ? e.OccurredAt : db.Events.Where(o => o.Id == e.CorrectsEventId).Select(o => o.OccurredAt).First())
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

    private async Task<AppendResult> AppendCoreAsync(Event newEvent, CancellationToken cancellationToken, bool alreadyOnServerClock = false)
    {
        if (!alreadyOnServerClock)
        {
            PutOnServerClock(newEvent);
        }

        // Joins a transaction already open, as when an invoice and the stock it takes are saved together.
        var outer = db.Database.CurrentTransaction is not null;
        await using var transaction = outer ? null : await db.Database.BeginTransactionAsync(cancellationToken);

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
        if (await RefusedAsync(newEvent, keys, cancellationToken) is { } refused)
        {
            return refused;
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

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

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

    /// <summary>A resend, an identifier clash, or work dated before its tray was started; null if the work can go in.</summary>
    private async Task<AppendResult?> RefusedAsync(Event newEvent, List<StockKey> keys, CancellationToken cancellationToken)
    {
        if (await AlreadyStoredAsync(newEvent, cancellationToken) is { } earlier)
        {
            return earlier;
        }

        return await BeforeStartAsync(newEvent, keys, cancellationToken) ? AppendResult.BeforeStart : null;
    }

    private static readonly EventType[] Starts = [EventType.Sown, EventType.Received, EventType.CuttingsTaken, EventType.Corrected];

    /// <summary>
    /// Whether work is dated before a container it touches was started, as when someone picks the wrong day while
    /// writing up earlier work. Starting work, and corrections, which stand in for earlier work, are never refused.
    /// </summary>
    private async Task<bool> BeforeStartAsync(Event newEvent, List<StockKey> keys, CancellationToken cancellationToken)
    {
        if (Starts.Contains(newEvent.Type))
        {
            return false;
        }

        var containers = keys.Select(key => key.ContainerId)
            .Concat(newEvent.ContainerId is { } id ? [id] : [])
            .Distinct()
            .ToList();

        var started = await db.Events
            .Where(e => !db.Events.Any(c => c.CorrectsEventId == e.Id)
                && e.CorrectsEventId == null
                && ((e.ContainerId != null && containers.Contains(e.ContainerId.Value)) || e.Lines.Any(line => containers.Contains(line.ContainerId))))
            .MinAsync(e => (DateTimeOffset?)e.OccurredAt, cancellationToken);

        return started is { } first && newEvent.OccurredAt < first;
    }

    /// <summary>Work timed by a device whose clock is out is put back on the server's clock, and never in the future.</summary>
    private void PutOnServerClock(Event newEvent)
    {
        var now = clock.GetUtcNow();

        if (device?.Skew is { } skew && skew.Duration() > DeviceClock.Tolerance)
        {
            newEvent.OccurredAt += skew;
        }

        if (newEvent.OccurredAt > now + ClockAllowance)
        {
            newEvent.OccurredAt = now;
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

        // "All of them lost" says the tray held none afterwards, as a count of nought would. So a count timed before it,
        // arriving late from a device that was offline, can't bring the plants back.
        var lostAll = (await db.Events
            .AsNoTracking()
            .Where(e => e.Type == EventType.Lost
                && e.ContainerId == key.ContainerId
                && e.Id != ignoring
                && e.Lines.Any(line => line.BatchId == key.BatchId)
                && !db.Events.Any(c => c.CorrectsEventId == e.Id))
            .Select(e => new { e.Id, e.OccurredAt, e.Payload })
            .ToListAsync(cancellationToken))
            .Where(e => LostAll(e.Payload))
            .Select(e => new TimedCount(e.Id, e.OccurredAt, 0));

        return [.. counts
            .Select(e => (e.Id, e.OccurredAt, Count: CountOf(e.Payload)))
            .Where(e => e.Count is { } count && count.BatchId == key.BatchId)
            .Select(e => new TimedCount(e.Id, e.OccurredAt, e.Count!.Counted))
            .Concat(lostAll)];
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

        var counted = lines.Concat(adjustments)
            .GroupBy(row => new StockKey(row.ContainerId, row.BatchId))
            .Select(group => new ContainerStock(group.Key.ContainerId, group.Key.BatchId, group.Sum(row => row.Quantity)))
            .Where(row => row.Quantity > 0)
            .ToList();

        // Sown without a count: there, but how many nobody knows yet.
        var notCounted = (await NotCountedAsync(containerId, cancellationToken))
            .Where(key => !counted.Any(row => row.ContainerId == key.ContainerId && row.BatchId == key.BatchId))
            .Select(key => new ContainerStock(key.ContainerId, key.BatchId, 0, NotCounted: true));

        return [.. counted, .. notCounted];
    }

    /// <summary>
    /// Batches sown into a container without a count (a sowing line of nought) that nobody has counted there since,
    /// and that weren't all lost. Worked out each time, so undoing the first count makes them uncounted again.
    /// </summary>
    private async Task<HashSet<StockKey>> NotCountedAsync(Guid? containerId, CancellationToken cancellationToken)
    {
        var sown = await ActiveLines(ignoring: null)
            .Where(line => line.QuantityChange == 0 && (containerId == null || line.ContainerId == containerId))
            .Join(db.Events.Where(e => e.Type == EventType.Sown || e.Type == EventType.Corrected), line => line.EventId, e => e.Id, (line, e) => new { line.ContainerId, line.BatchId, e.Type, e.CorrectsEventId })
            .ToListAsync(cancellationToken);

        if (sown.Count == 0)
        {
            return [];
        }

        var correctedSowings = await db.Events
            .Where(e => e.Type == EventType.Sown && sown.Select(row => row.CorrectsEventId).Contains(e.Id))
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        var keys = sown
            .Where(row => row.Type == EventType.Sown || (row.CorrectsEventId is { } original && correctedSowings.Contains(original)))
            .Select(row => new StockKey(row.ContainerId, row.BatchId))
            .ToHashSet();

        var containers = keys.Select(key => key.ContainerId).Distinct().ToList();
        var settled = await db.Events
            .AsNoTracking()
            .Where(e => (e.Type == EventType.StockCounted || e.Type == EventType.Lost)
                && e.ContainerId != null
                && containers.Contains(e.ContainerId.Value)
                && !db.Events.Any(c => c.CorrectsEventId == e.Id))
            .Select(e => new { e.Type, ContainerId = e.ContainerId!.Value, e.Payload, Batches = e.Lines.Select(line => line.BatchId).ToList() })
            .ToListAsync(cancellationToken);

        foreach (var work in settled)
        {
            if (work.Type == EventType.StockCounted && CountOf(work.Payload) is { } count)
            {
                keys.Remove(new StockKey(work.ContainerId, count.BatchId));
            }
            else if (work.Type == EventType.Lost && LostAll(work.Payload))
            {
                foreach (var batchId in work.Batches)
                {
                    keys.Remove(new StockKey(work.ContainerId, batchId));
                }
            }
        }

        return keys;
    }

    /// <summary>Whether a loss from an uncounted tray was all of them, which ends it.</summary>
    internal static bool LostAll(string payload)
    {
        using var details = JsonDocument.Parse(payload);
        return details.RootElement.TryGetProperty("all", out var all) && all.ValueKind == JsonValueKind.True;
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
