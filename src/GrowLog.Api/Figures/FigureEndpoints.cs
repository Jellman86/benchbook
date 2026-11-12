using System.Globalization;
using System.Text.Json;
using GrowLog.Api.Accounts;
using GrowLog.Api.Catalogue;
using GrowLog.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Figures;

/// <summary>Germination and survival, worked out from events each time, so corrections are always reflected.</summary>
internal static class FigureEndpoints
{
    private static readonly EventType[] StartingTypes = [EventType.Sown, EventType.Received, EventType.CuttingsTaken];

    public static void MapFigureEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/figures", GetFiguresAsync).RequireAuthorization(AccountServices.OwnerPolicy);

    private static string? LossReasonOf(string payload)
    {
        using var details = JsonDocument.Parse(payload);
        return details.RootElement.TryGetProperty("reason", out var reason) ? reason.GetString() : null;
    }

    private static async Task<Figures> GetFiguresAsync(GrowLogDbContext db, EventStore store, CancellationToken cancellationToken)
    {
        var events = await db.Events
            .AsNoTracking()
            .Select(e => new EventFacts(e.Id, e.Type, e.CorrectsEventId, e.ContainerId, e.Payload, e.OccurredAt))
            .ToDictionaryAsync(e => e.Id, cancellationToken);

        var reversed = events.Values.Where(e => e.CorrectsEventId is not null).Select(e => e.CorrectsEventId!.Value).ToHashSet();

        var batches = await db.Batches
            .AsNoTracking()
            .Join(db.Plants, batch => batch.PlantId, plant => plant.Id, (batch, plant) => new BatchFacts(
                batch.Id,
                batch.PlantId,
                plant.CommonName,
                plant.Variety,
                db.Suppliers.Where(supplier => supplier.Id == batch.SupplierId).Select(supplier => supplier.Name).FirstOrDefault(),
                batch.SownAs,
                batch.SownAmount))
            .ToDictionaryAsync(batch => batch.Id, cancellationToken);

        var lines = FirstCountsAsStarts(
            (await db.EventLines.AsNoTracking().ToListAsync(cancellationToken))
                .Where(line => !reversed.Contains(line.EventId))
                .Select(line => new Row(line, events[line.EventId], WorkDoneBy(events[line.EventId], events)))
                .ToList(),
            batches);

        // Changes stock counts absorbed when earlier work arrived late or was undone: part of each count's adjustment.
        var absorbed = (await db.CountAdjustments.AsNoTracking().ToListAsync(cancellationToken))
            .Where(adjustment => !reversed.Contains(adjustment.EventId) && !reversed.Contains(adjustment.CountEventId))
            .GroupBy(adjustment => batches[adjustment.BatchId].PlantId)
            .ToDictionary(group => group.Key, group => group.Sum(adjustment => adjustment.QuantityChange));

        var germinatedBatches = GerminatedBatches(events, reversed, lines, batches);

        var perPlant = lines
            .GroupBy(row => batches[row.Line.BatchId].PlantId)
            .Select(group => ForPlant(batches[group.First().Line.BatchId], group.ToList(), germinatedBatches, absorbed.GetValueOrDefault(group.Key)))
            .OrderBy(figures => figures.CommonName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(figures => figures.Variety, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var losses = lines
            .Where(row => row.Type == EventType.Lost)
            .GroupBy(row => LossReasonOf(row.Event.Payload) ?? nameof(LossReason.Other), StringComparer.Ordinal)
            // A tray lost before anyone counted it has no number to add, so it's counted as a tray.
            .Select(group => new LossFigures(group.Key, -group.Sum(row => row.Line.QuantityChange), group.Count(row => row.Line.QuantityChange == 0)))
            .OrderByDescending(figures => figures.Quantity)
            .ThenByDescending(figures => figures.UncountedTrays)
            .ToList();

        var (bySupplier, byPlace, bySeason) = await BreakdownsAsync(lines, batches, germinatedBatches, db, store, cancellationToken);
        return new Figures(perPlant, losses, bySupplier, byPlace, bySeason);
    }

    /// <summary>
    /// Seed batches that count towards germination: only once someone has recorded that they germinated. Seed sown by
    /// weight, by the packet or not known has no number of seeds to measure against, so it gives no rate.
    /// </summary>
    private static HashSet<Guid> GerminatedBatches(Dictionary<Guid, EventFacts> events, HashSet<Guid> reversed, List<Row> lines, Dictionary<Guid, BatchFacts> batches)
    {
        var germinatedContainers = events.Values
            .Where(e => e.Type == EventType.Germinated && !reversed.Contains(e.Id) && e.ContainerId is not null)
            .Select(e => e.ContainerId!.Value)
            .ToHashSet();

        // A tray where none came up, recorded as all lost for that reason, is a real nought.
        var noneCameUp = lines
            .Where(row => row.Type == EventType.Lost
                && EventStore.LostAll(row.Event.Payload)
                && string.Equals(LossReasonOf(row.Event.Payload), nameof(LossReason.FailedToGerminate), StringComparison.Ordinal))
            .Select(row => row.Line.BatchId);

        // "About so many" can only be measured once someone has counted what came up, or none did.
        var settled = events.Values
            .Where(e => e.Type == EventType.StockCounted && !reversed.Contains(e.Id) && EventStore.CountOf(e.Payload) is not null)
            .Select(e => EventStore.CountOf(e.Payload)!.BatchId)
            .Concat(noneCameUp)
            .ToHashSet();

        return lines
            .Where(row => row.Type == EventType.Sown && germinatedContainers.Contains(row.Line.ContainerId))
            .Select(row => row.Line.BatchId)
            .Concat(noneCameUp)
            .Where(id => batches[id].SownAs switch
            {
                null or SownMeasure.Counted => true,
                SownMeasure.About => settled.Contains(id),
                _ => false,
            })
            .ToHashSet();
    }

    /// <summary>Survival and germination by supplier, by place, and by the month batches were started.</summary>
    private static async Task<(List<GroupFigures>, List<GroupFigures>, List<GroupFigures>)> BreakdownsAsync(
        List<Row> lines,
        Dictionary<Guid, BatchFacts> batches,
        HashSet<Guid> germinatedBatches,
        GrowLogDbContext db,
        EventStore store,
        CancellationToken cancellationToken)
    {
        var places = await TopPlacesAsync(lines, db, store, cancellationToken);
        var started = lines
            .Where(row => StartingTypes.Contains(row.Type))
            .GroupBy(row => row.Line.BatchId)
            .ToDictionary(group => group.Key, group => group.Min(row => row.Event.OccurredAt));

        return (
            Group(lines, row => batches[row.Line.BatchId].SupplierName ?? "Own seed and cuttings", germinatedBatches),
            Group(lines, row => places.GetValueOrDefault(row.Line.ContainerId) ?? "No place recorded", germinatedBatches),
            [.. Group(lines, row => started.TryGetValue(row.Line.BatchId, out var at) ? at.ToString("yyyy-MM", CultureInfo.InvariantCulture) : null, germinatedBatches)
                .OrderBy(group => group.Name, StringComparer.Ordinal)]);
    }

    /// <summary>The greenhouse, tunnel or bed each container is in: the first place below the site.</summary>
    private static async Task<Dictionary<Guid, string>> TopPlacesAsync(List<Row> lines, GrowLogDbContext db, EventStore store, CancellationToken cancellationToken)
    {
        var paths = await PlacePaths.LoadAsync(db, cancellationToken);
        var placements = await store.GetPlacementsAsync([.. lines.Select(row => row.Line.ContainerId).Distinct()], cancellationToken);

        return placements
            .Where(entry => entry.Value.LocationId is { } id && paths.ContainsKey(id))
            .ToDictionary(entry => entry.Key, entry => paths[entry.Value.LocationId!.Value].Split(", ")[0]);
    }

    /// <summary>Survival and germination for each group of rows, largest first.</summary>
    private static List<GroupFigures> Group(List<Row> rows, Func<Row, string?> key, HashSet<Guid> germinatedBatches) =>
        rows
            .Select(row => (Row: row, Key: key(row)))
            .Where(item => item.Key is not null)
            .GroupBy(item => item.Key!, StringComparer.Ordinal)
            .Select(group =>
            {
                var measured = Measure([.. group.Select(item => item.Row)], germinatedBatches);
                return new GroupFigures(group.Key, measured.Started, measured.Lost, measured.GerminationRate, measured.SurvivalRate, measured.Approximate);
            })
            .Where(group => group.Started > 0)
            .OrderByDescending(group => group.Started)
            .ToList();

    private static Measured Measure(List<Row> rows, HashSet<Guid> germinatedBatches)
    {
        var started = rows.Where(row => StartingTypes.Contains(row.Type)).Sum(row => row.Line.QuantityChange) + rows.Where(row => row.FirstCount).Sum(row => row.StartCount);
        var lost = -rows.Where(row => row.Type == EventType.Lost).Sum(row => row.Line.QuantityChange);
        var watched = rows.Where(row => germinatedBatches.Contains(row.Line.BatchId)).ToList();

        // Counted sowings: seeds sown, less those that failed to germinate.
        var sown = watched.Where(row => row.Type == EventType.Sown && row.Line.QuantityChange > 0).Sum(row => row.Line.QuantityChange);
        var failed = -watched
            .Where(row => row.Type == EventType.Lost && string.Equals(LossReasonOf(row.Event.Payload), nameof(LossReason.FailedToGerminate), StringComparison.Ordinal))
            .Sum(row => row.Line.QuantityChange);

        // "About so many" sowings: seedlings at the first count against the seeds thought sown, so approximate.
        var about = watched
            .Where(row => row.SownAbout is not null)
            .Select(row => (Seeds: row.SownAbout!.Value, Up: watched.Where(other => other.FirstCount && other.Line.BatchId == row.Line.BatchId).Sum(other => other.StartCount)))
            .ToList();
        var aboutSeeds = about.Sum(item => item.Seeds);
        var seeds = sown + aboutSeeds;
        // Each batch is capped at its own estimate, so one that beat it can't hide failures elsewhere.
        var up = sown - failed + about.Sum(item => Math.Min(item.Up, item.Seeds));

        return new(
            started,
            lost,
            seeds > 0 ? Math.Min(1, (double)up / (double)seeds) : null,
            // A first count that was only a rough guess makes the rate a guess too.
            aboutSeeds > 0 || watched.Any(row => row.FirstCount && row.Type == EventType.StockCounted && EventStore.IsRough(row.Event.Payload)),
            started > 0 ? (started - lost) / (double)started : null);
    }

    /// <summary>
    /// Seed sown without a count is first counted later, so that count is where it starts for the figures, not an
    /// adjustment. Rows for "about so many" sowings carry the seeds thought sown.
    /// </summary>
    private static List<Row> FirstCountsAsStarts(List<Row> rows, Dictionary<Guid, BatchFacts> batches)
    {
        var firsts = rows
            .Where(row => row.Type == EventType.StockCounted && batches[row.Line.BatchId].SownAs is not (null or SownMeasure.Counted))
            .GroupBy(row => row.Line.BatchId)
            .Select(group => group.OrderBy(row => row.Event.OccurredAt).First())
            .ToHashSet();

        // Only the sowing carries the seeds, so a tray pricked out elsewhere doesn't count them again.
        return rows.ConvertAll(row => row with
        {
            FirstCount = firsts.Contains(row),
            StartCount = firsts.Contains(row) ? EventStore.CountOf(row.Event.Payload)?.Counted ?? row.Line.QuantityChange : 0,
            SownAbout = row.Type == EventType.Sown && batches[row.Line.BatchId] is { SownAs: SownMeasure.About, SownAmount: { } amount } ? (int)amount : null,
        });
    }

    private static PlantFigures ForPlant(BatchFacts batch, List<Row> rows, HashSet<Guid> germinatedBatches, int absorbed)
    {
        var measured = Measure(rows, germinatedBatches);
        // A first count's own line can differ from what it counted, if an earlier count was undone: the rest is adjustment.
        var adjusted = rows.Where(row => row.Type == EventType.StockCounted).Sum(row => row.FirstCount ? row.Line.QuantityChange - row.StartCount : row.Line.QuantityChange) + absorbed;

        return new PlantFigures(
            batch.PlantId,
            batch.CommonName,
            batch.Variety,
            measured.Started,
            measured.Lost,
            adjusted,
            measured.Started - measured.Lost + adjusted,
            measured.GerminationRate,
            measured.SurvivalRate,
            measured.Approximate);
    }

    /// <summary>A correction stands in for the event it reversed, so it counts as that kind of work.</summary>
    private static EventType WorkDoneBy(EventFacts work, Dictionary<Guid, EventFacts> events)
    {
        while (work.Type == EventType.Corrected)
        {
            work = events[work.CorrectsEventId!.Value];
        }

        return work.Type;
    }

    private sealed record BatchFacts(Guid Id, Guid PlantId, string CommonName, string? Variety, string? SupplierName, SownMeasure? SownAs, decimal? SownAmount);

    /// <param name="FirstCount">The first count of seed sown without one: where it starts, for the figures.</param>
    /// <param name="SownAbout">For "about so many" sowings, the seeds thought sown.</param>
    /// <param name="StartCount">For a first count, the number it counted: where the batch starts.</param>
    private sealed record Row(EventLine Line, EventFacts Event, EventType Type, bool FirstCount = false, int? SownAbout = null, int StartCount = 0);

    private sealed record Measured(int Started, int Lost, double? GerminationRate, bool Approximate, double? SurvivalRate);

    private sealed record EventFacts(Guid Id, EventType Type, Guid? CorrectsEventId, Guid? ContainerId, string Payload, DateTimeOffset OccurredAt);

    internal sealed record PlantFigures(
        Guid PlantId,
        string CommonName,
        string? Variety,
        int Started,
        int Lost,
        int Adjusted,
        int Alive,
        double? GerminationRate,
        double? SurvivalRate,
        bool GerminationApproximate = false);

    /// <param name="UncountedTrays">Trays with losses for this reason before anyone counted them, so not in Quantity.</param>
    internal sealed record LossFigures(string Reason, int Quantity, int UncountedTrays);

    /// <param name="Name">A supplier, a place such as "Greenhouse 2", or a month such as "2026-11".</param>
    /// <param name="GerminationApproximate">Whether the rate rests on seed sown "about so many", or on a rough first count.</param>
    internal sealed record GroupFigures(string Name, int Started, int Lost, double? GerminationRate, double? SurvivalRate, bool GerminationApproximate = false);

    internal sealed record Figures(
        IReadOnlyList<PlantFigures> Plants,
        IReadOnlyList<LossFigures> Losses,
        IReadOnlyList<GroupFigures> BySupplier,
        IReadOnlyList<GroupFigures> ByPlace,
        IReadOnlyList<GroupFigures> BySeason);
}
