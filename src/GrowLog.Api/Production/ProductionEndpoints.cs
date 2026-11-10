using GrowLog.Api.Catalogue;
using GrowLog.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Production;

/// <summary>Everything growing, and the stage each container has reached, worked out from what was recorded.</summary>
internal static class ProductionEndpoints
{
    private static readonly Dictionary<EventType, ProductionStage> Stages = new()
    {
        [EventType.Sown] = ProductionStage.Sown,
        [EventType.CuttingsTaken] = ProductionStage.Cuttings,
        [EventType.Received] = ProductionStage.BoughtIn,
        [EventType.Germinated] = ProductionStage.Germinated,
        [EventType.PrickedOut] = ProductionStage.PrickedOut,
        [EventType.PottedOn] = ProductionStage.PottedOn,
        [EventType.Ready] = ProductionStage.Ready,
    };

    public static void MapProductionEndpoints(this IEndpointRouteBuilder app) => app.MapGet("/api/production", ListAsync);

    private static async Task<List<ProductionRow>> ListAsync(GrowLogDbContext db, EventStore store, CancellationToken cancellationToken)
    {
        var stock = await store.GetStockAsync(cancellationToken);
        var containerIds = stock.Select(row => row.ContainerId).Distinct().ToList();
        var batchIds = stock.Select(row => row.BatchId).Distinct().ToList();

        var labels = await db.Containers
            .Where(container => containerIds.Contains(container.Id))
            .ToDictionaryAsync(container => container.Id, container => container.LabelCode, cancellationToken);
        var batches = await FindBatchesAsync(batchIds, db, cancellationToken);
        var stages = await FindStagesAsync(containerIds, db, cancellationToken);
        var places = await store.GetPlacementsAsync(containerIds, cancellationToken);
        var locationNames = await PlacePaths.LoadAsync(db, cancellationToken);
        var potSizeNames = await db.PotSizes.ToDictionaryAsync(size => size.Id, size => size.Name, cancellationToken);

        return [.. stock.Select(row =>
        {
            var batch = batches[row.BatchId];
            var place = places.GetValueOrDefault(row.ContainerId);
            var reached = stages.GetValueOrDefault(row.ContainerId);

            return new ProductionRow(
                row.ContainerId,
                labels[row.ContainerId],
                row.BatchId,
                batch.TraceabilityCode,
                batch.PlantId,
                batch.CommonName,
                batch.Variety,
                batch.BotanicalName,
                row.Quantity,
                reached?.Stage,
                reached?.Since,
                place?.LocationId is { } locationId ? locationNames[locationId] : null,
                place?.PotSizeId is { } potSizeId ? potSizeNames[potSizeId] : null,
                batch.SupplierName,
                batch.HasPhoto);
        })];
    }

    private static async Task<Dictionary<Guid, BatchFacts>> FindBatchesAsync(List<Guid> batchIds, GrowLogDbContext db, CancellationToken cancellationToken) =>
        await db.Batches
            .Where(batch => batchIds.Contains(batch.Id))
            .Join(db.Plants, batch => batch.PlantId, plant => plant.Id, (batch, plant) => new BatchFacts(
                batch.Id,
                batch.TraceabilityCode,
                plant.Id,
                plant.CommonName,
                plant.Variety,
                plant.BotanicalName,
                db.Suppliers.Where(supplier => supplier.Id == batch.SupplierId).Select(supplier => supplier.Name).FirstOrDefault(),
                db.PlantPhotos.Any(photo => photo.PlantId == plant.Id) || db.Species.Any(species => species.Id == plant.SpeciesId && species.PhotoUrl != null)))
            .ToDictionaryAsync(batch => batch.Id, cancellationToken);

    /// <summary>The latest stage for each container that hasn't been undone. A correction that replaces work stands in for it.</summary>
    internal static async Task<Dictionary<Guid, StageReached>> FindStagesAsync(List<Guid> containerIds, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        var events = await db.Events
            .AsNoTracking()
            .Where(e => e.ContainerId != null && containerIds.Contains(e.ContainerId.Value))
            .Select(e => new EventFacts(e.Id, e.Type, e.ContainerId!.Value, e.CorrectsEventId, e.OccurredAt, e.RecordedAt, e.LocationId != null || e.Lines.Any()))
            .ToListAsync(cancellationToken);

        var byId = events.ToDictionary(e => e.Id);
        var reversed = events.Where(e => e.CorrectsEventId is not null).Select(e => e.CorrectsEventId!.Value).ToHashSet();

        return events
            .Where(e => !reversed.Contains(e.Id))
            .Select(e => (Event: e, Stage: StageOf(e, byId)))
            .Where(row => row.Stage is not null)
            .GroupBy(row => row.Event.ContainerId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var latest = group.OrderBy(row => row.Event.OccurredAt).ThenBy(row => row.Event.RecordedAt).Last();
                    return new StageReached(latest.Stage!.Value, latest.Event.OccurredAt);
                });
    }

    private static ProductionStage? StageOf(EventFacts work, Dictionary<Guid, EventFacts> byId)
    {
        var type = work.Type;

        if (type == EventType.Corrected)
        {
            // With nothing in its place, a correction is an undo.
            if (!work.Replaces || !byId.TryGetValue(work.CorrectsEventId!.Value, out var original))
            {
                return null;
            }

            type = original.Type;
        }

        return Stages.TryGetValue(type, out var stage) ? stage : null;
    }

    private sealed record EventFacts(Guid Id, EventType Type, Guid ContainerId, Guid? CorrectsEventId, DateTimeOffset OccurredAt, DateTimeOffset RecordedAt, bool Replaces);

    internal sealed record StageReached(ProductionStage Stage, DateTimeOffset Since);

    private sealed record BatchFacts(Guid Id, string TraceabilityCode, Guid PlantId, string CommonName, string? Variety, string? BotanicalName, string? SupplierName, bool HasPhoto);

    internal sealed record ProductionRow(
        Guid ContainerId,
        string LabelCode,
        Guid BatchId,
        string TraceabilityCode,
        Guid PlantId,
        string CommonName,
        string? Variety,
        string? BotanicalName,
        int Quantity,
        ProductionStage? Stage,
        DateTimeOffset? StageSince,
        string? LocationName,
        string? PotSizeName,
        string? SupplierName,
        bool HasPhoto);
}
