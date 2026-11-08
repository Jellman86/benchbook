using System.Text.Json;
using GrowLog.Api.Accounts;
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

    private static async Task<Figures> GetFiguresAsync(GrowLogDbContext db, CancellationToken cancellationToken)
    {
        var events = await db.Events
            .AsNoTracking()
            .Select(e => new EventFacts(e.Id, e.Type, e.CorrectsEventId, e.ContainerId, e.Payload))
            .ToDictionaryAsync(e => e.Id, cancellationToken);

        var reversed = events.Values.Where(e => e.CorrectsEventId is not null).Select(e => e.CorrectsEventId!.Value).ToHashSet();

        var batches = await db.Batches
            .AsNoTracking()
            .Join(db.Plants, batch => batch.PlantId, plant => plant.Id, (batch, plant) => new BatchFacts(batch.Id, batch.PlantId, plant.CommonName, plant.Variety))
            .ToDictionaryAsync(batch => batch.Id, cancellationToken);

        var lines = (await db.EventLines.AsNoTracking().ToListAsync(cancellationToken))
            .Where(line => !reversed.Contains(line.EventId))
            .Select(line => new Row(line, events[line.EventId], WorkDoneBy(events[line.EventId], events)))
            .ToList();

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
            .Select(group => ForPlant(batches[group.First().Line.BatchId], group.ToList(), germinatedBatches))
            .OrderBy(figures => figures.CommonName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(figures => figures.Variety, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var losses = lines
            .Where(row => row.Type == EventType.Lost)
            .GroupBy(row => LossReasonOf(row.Event.Payload) ?? nameof(LossReason.Other), StringComparer.Ordinal)
            .Select(group => new LossFigures(group.Key, -group.Sum(row => row.Line.QuantityChange)))
            .OrderByDescending(figures => figures.Quantity)
            .ToList();

        return new Figures(perPlant, losses);
    }

    private static PlantFigures ForPlant(BatchFacts batch, List<Row> rows, HashSet<Guid> germinatedBatches)
    {
        var started = rows.Where(row => StartingTypes.Contains(row.Type)).Sum(row => row.Line.QuantityChange);
        var lost = -rows.Where(row => row.Type == EventType.Lost).Sum(row => row.Line.QuantityChange);
        var adjusted = rows.Where(row => row.Type == EventType.StockCounted).Sum(row => row.Line.QuantityChange);
        var watched = rows.Where(row => germinatedBatches.Contains(row.Line.BatchId)).ToList();
        var sown = watched.Where(row => row.Type == EventType.Sown).Sum(row => row.Line.QuantityChange);
        var failed = -watched
            .Where(row => row.Type == EventType.Lost && string.Equals(LossReasonOf(row.Event.Payload), nameof(LossReason.FailedToGerminate), StringComparison.Ordinal))
            .Sum(row => row.Line.QuantityChange);

        return new PlantFigures(
            batch.PlantId,
            batch.CommonName,
            batch.Variety,
            started,
            lost,
            adjusted,
            started - lost + adjusted,
            sown > 0 ? (sown - failed) / (double)sown : null,
            started > 0 ? (started - lost) / (double)started : null);
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

    private sealed record BatchFacts(Guid Id, Guid PlantId, string CommonName, string? Variety);

    private sealed record Row(EventLine Line, EventFacts Event, EventType Type);

    private sealed record EventFacts(Guid Id, EventType Type, Guid? CorrectsEventId, Guid? ContainerId, string Payload);

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

    internal sealed record Figures(IReadOnlyList<PlantFigures> Plants, IReadOnlyList<LossFigures> Losses);
}
