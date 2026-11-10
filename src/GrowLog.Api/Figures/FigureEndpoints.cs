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
                db.Suppliers.Where(supplier => supplier.Id == batch.SupplierId).Select(supplier => supplier.Name).FirstOrDefault()))
            .ToDictionaryAsync(batch => batch.Id, cancellationToken);

        var lines = (await db.EventLines.AsNoTracking().ToListAsync(cancellationToken))
            .Where(line => !reversed.Contains(line.EventId))
            .Select(line => new Row(line, events[line.EventId], WorkDoneBy(events[line.EventId], events)))
            .ToList();

        // Changes stock counts absorbed when earlier work arrived late or was undone: part of each count's adjustment.
        var absorbed = (await db.CountAdjustments.AsNoTracking().ToListAsync(cancellationToken))
            .Where(adjustment => !reversed.Contains(adjustment.EventId) && !reversed.Contains(adjustment.CountEventId))
            .GroupBy(adjustment => batches[adjustment.BatchId].PlantId)
            .ToDictionary(group => group.Key, group => group.Sum(adjustment => adjustment.QuantityChange));

        var germinatedContainers = events.Values
            .Where(e => e.Type == EventType.Germinated && !reversed.Contains(e.Id) && e.ContainerId is not null)
            .Select(e => e.ContainerId!.Value)
            .ToHashSet();

        // A seed batch only counts towards germination once someone has recorded that it germinated.
        var germinatedBatches = lines
            .Where(row => row.Type == EventType.Sown && germinatedContainers.Contains(row.Line.ContainerId))
            .Select(row => row.Line.BatchId)
            .ToHashSet();

        var perPlant = lines
            .GroupBy(row => batches[row.Line.BatchId].PlantId)
            .Select(group => ForPlant(batches[group.First().Line.BatchId], group.ToList(), germinatedBatches, absorbed.GetValueOrDefault(group.Key)))
            .OrderBy(figures => figures.CommonName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(figures => figures.Variety, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var losses = lines
            .Where(row => row.Type == EventType.Lost)
            .GroupBy(row => LossReasonOf(row.Event.Payload) ?? nameof(LossReason.Other), StringComparer.Ordinal)
            .Select(group => new LossFigures(group.Key, -group.Sum(row => row.Line.QuantityChange)))
            .OrderByDescending(figures => figures.Quantity)
            .ToList();

        var (bySupplier, byPlace, bySeason) = await BreakdownsAsync(lines, batches, germinatedBatches, db, store, cancellationToken);
        return new Figures(perPlant, losses, bySupplier, byPlace, bySeason);
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
                return new GroupFigures(group.Key, measured.Started, measured.Lost, measured.GerminationRate, measured.SurvivalRate);
            })
            .Where(group => group.Started > 0)
            .OrderByDescending(group => group.Started)
            .ToList();

    private static (int Started, int Lost, double? GerminationRate, double? SurvivalRate) Measure(List<Row> rows, HashSet<Guid> germinatedBatches)
    {
        var started = rows.Where(row => StartingTypes.Contains(row.Type)).Sum(row => row.Line.QuantityChange);
        var lost = -rows.Where(row => row.Type == EventType.Lost).Sum(row => row.Line.QuantityChange);
        var watched = rows.Where(row => germinatedBatches.Contains(row.Line.BatchId)).ToList();
        var sown = watched.Where(row => row.Type == EventType.Sown).Sum(row => row.Line.QuantityChange);
        var failed = -watched
            .Where(row => row.Type == EventType.Lost && string.Equals(LossReasonOf(row.Event.Payload), nameof(LossReason.FailedToGerminate), StringComparison.Ordinal))
            .Sum(row => row.Line.QuantityChange);

        return (
            started,
            lost,
            sown > 0 ? (sown - failed) / (double)sown : null,
            started > 0 ? (started - lost) / (double)started : null);
    }

    private static PlantFigures ForPlant(BatchFacts batch, List<Row> rows, HashSet<Guid> germinatedBatches, int absorbed)
    {
        var measured = Measure(rows, germinatedBatches);
        var adjusted = rows.Where(row => row.Type == EventType.StockCounted).Sum(row => row.Line.QuantityChange) + absorbed;

        return new PlantFigures(
            batch.PlantId,
            batch.CommonName,
            batch.Variety,
            measured.Started,
            measured.Lost,
            adjusted,
            measured.Started - measured.Lost + adjusted,
            measured.GerminationRate,
            measured.SurvivalRate);
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

    private sealed record BatchFacts(Guid Id, Guid PlantId, string CommonName, string? Variety, string? SupplierName);

    private sealed record Row(EventLine Line, EventFacts Event, EventType Type);

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
        double? SurvivalRate);

    internal sealed record LossFigures(string Reason, int Quantity);

    /// <param name="Name">A supplier, a place such as "Greenhouse 2", or a month such as "2026-11".</param>
    internal sealed record GroupFigures(string Name, int Started, int Lost, double? GerminationRate, double? SurvivalRate);

    internal sealed record Figures(
        IReadOnlyList<PlantFigures> Plants,
        IReadOnlyList<LossFigures> Losses,
        IReadOnlyList<GroupFigures> BySupplier,
        IReadOnlyList<GroupFigures> ByPlace,
        IReadOnlyList<GroupFigures> BySeason);
}
