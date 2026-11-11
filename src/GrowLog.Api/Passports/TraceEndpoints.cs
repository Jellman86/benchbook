using System.Text.Json;
using GrowLog.Api.Accounts;
using GrowLog.Api.Catalogue;
using GrowLog.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Passports;

/// <summary>
/// Trace-back and trace-forward for one batch, as an inspector or APHA would ask for it: where the plants came from,
/// every passport received with them, what was done to them and where, and who they went to.
/// Work that was undone is left out; corrected work shows as corrected.
/// </summary>
internal static class TraceEndpoints
{
    private const int MostMatches = 30;
    private const string Escape = "\\";

    /// <summary>Work done to a whole container, which applies to every batch in it at the time.</summary>
    private static readonly EventType[] ContainerWork =
        [EventType.Treated, EventType.Examined, EventType.Moved, EventType.Germinated, EventType.Ready, EventType.Note, EventType.StockCounted];

    public static void MapTraceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/trace", FindAsync).RequireAuthorization(AccountServices.OwnerPolicy);
        app.MapGet("/api/batches/{id:guid}/trace", TraceAsync).RequireAuthorization(AccountServices.OwnerPolicy).Produces<BatchTrace>();
    }

    private static string EscapeLike(string value) =>
        value.Replace(Escape, Escape + Escape, StringComparison.Ordinal)
            .Replace("%", Escape + "%", StringComparison.Ordinal)
            .Replace("_", Escape + "_", StringComparison.Ordinal);

    /// <summary>Batches by traceability code, by a tray they have been in, or by plant name. Newest first.</summary>
    private static async Task<List<TraceMatch>> FindAsync(GrowLogDbContext db, string? q = null, CancellationToken cancellationToken = default)
    {
        var text = q?.Trim() ?? "";
        var batches = db.Batches.AsNoTracking();

        if (text.Length > 0)
        {
            var exact = EscapeLike(text);
            var contains = $"%{exact}%";
            var inContainer = db.EventLines
                .Where(line => db.Containers.Any(container => container.Id == line.ContainerId && EF.Functions.ILike(container.LabelCode, exact, Escape)))
                .Select(line => line.BatchId);

            batches = batches.Where(batch => EF.Functions.ILike(batch.TraceabilityCode, exact, Escape)
                || inContainer.Contains(batch.Id)
                || db.Plants.Any(plant => plant.Id == batch.PlantId
                    && (EF.Functions.ILike(plant.CommonName, contains, Escape)
                        || EF.Functions.ILike(plant.Variety!, contains, Escape)
                        || EF.Functions.ILike(plant.BotanicalName!, contains, Escape))));
        }

        return await batches
            .OrderByDescending(batch => batch.CreatedAt)
            .ThenByDescending(batch => batch.Id)
            .Take(MostMatches)
            .Join(db.Plants, batch => batch.PlantId, plant => plant.Id, (batch, plant) => new TraceMatch(
                batch.Id,
                batch.TraceabilityCode,
                plant.CommonName,
                plant.Variety,
                batch.Source,
                db.Events.Where(e => e.Lines.Any(line => line.BatchId == batch.Id)).Min(e => (DateTimeOffset?)e.OccurredAt) ?? batch.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    private static async Task<IResult> TraceAsync(Guid id, GrowLogDbContext db, EventStore store, CancellationToken cancellationToken)
    {
        if (await LoadOriginAsync(id, db, cancellationToken) is not { } origin)
        {
            return TypedResults.NotFound();
        }

        var paths = await PlacePaths.LoadAsync(db, cancellationToken);
        var (steps, containers) = await LoadWorkAsync(id, db, store, paths, cancellationToken);
        var sales = await LoadSalesAsync(id, db, cancellationToken);

        var totals = new TraceTotals(
            Lost: -steps.Where(step => step.Type == EventType.Lost).Sum(step => step.Quantity ?? 0),
            Sold: -steps.Where(step => step.Type == EventType.Sold).Sum(step => step.Quantity ?? 0),
            Now: containers.Sum(container => container.Now));

        return TypedResults.Ok(origin with
        {
            // When the plants were sown or arrived, which can be before the batch reached the server.
            StartedAt = steps.Count > 0 ? steps.Min(step => step.OccurredAt) : origin.StartedAt,
            ReceivedPassports = await LoadPassportsAsync(id, db, cancellationToken),
            Containers = containers,
            Steps = steps,
            Sales = sales,
            Totals = totals,
        });
    }

    /// <summary>Where the batch came from: its plant, source, supplier and purchase, and the batches either side of it.</summary>
    private static async Task<BatchTrace?> LoadOriginAsync(Guid id, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        var origin = await db.Batches
            .AsNoTracking()
            .Where(batch => batch.Id == id)
            .Join(db.Plants, batch => batch.PlantId, plant => plant.Id, (batch, plant) => new
            {
                Batch = batch,
                Plant = plant,
                Supplier = db.Suppliers
                    .Where(supplier => supplier.Id == batch.SupplierId)
                    .Select(supplier => new TraceSupplier(supplier.Id, supplier.Name, supplier.PassportRegistrationNumber))
                    .FirstOrDefault(),
                Purchase = db.PurchaseLines
                    .Where(line => line.Id == batch.PurchaseLineId)
                    .Join(db.Purchases, line => line.PurchaseId, purchase => purchase.Id, (line, purchase) => new TracePurchase(purchase.Id, purchase.PurchasedOn, purchase.Reference))
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (origin is null)
        {
            return null;
        }

        var related = await db.Batches
            .AsNoTracking()
            .Where(batch => batch.Id == origin.Batch.ParentBatchId || batch.ParentBatchId == id)
            .OrderBy(batch => batch.CreatedAt)
            .Join(db.Plants, batch => batch.PlantId, plant => plant.Id, (batch, plant) => new { Link = new TraceBatchLink(batch.Id, batch.TraceabilityCode, plant.CommonName, plant.Variety), batch.ParentBatchId })
            .ToListAsync(cancellationToken);

        return new BatchTrace(
            origin.Batch.Id,
            origin.Batch.TraceabilityCode,
            new TracePlant(origin.Plant.Id, origin.Plant.CommonName, origin.Plant.Variety, origin.Plant.BotanicalName, origin.Plant.XylellaHost),
            origin.Batch.Source,
            origin.Batch.SourceReference,
            origin.Batch.MotherPlant,
            origin.Batch.OriginCountry,
            origin.Batch.CreatedAt,
            origin.Supplier,
            origin.Purchase,
            related.Find(batch => batch.Link.Id == origin.Batch.ParentBatchId)?.Link,
            related.Where(batch => batch.ParentBatchId == id).Select(batch => batch.Link).ToList(),
            [],
            [],
            [],
            [],
            new TraceTotals(0, 0, 0));
    }

    private static Task<List<TracePassport>> LoadPassportsAsync(Guid id, GrowLogDbContext db, CancellationToken cancellationToken) =>
        db.Set<ReceivedPassport>()
            .AsNoTracking()
            .Where(passport => passport.BatchId == id)
            .OrderBy(passport => passport.ReceivedOn)
            .Join(db.Suppliers, passport => passport.SupplierId, supplier => supplier.Id, (passport, supplier) => new TracePassport(
                passport.ReceivedOn,
                supplier.Name,
                passport.PartA,
                passport.PartB,
                passport.PartC,
                passport.PartD,
                passport.PartE,
                passport.Status))
            .ToListAsync(cancellationToken);

    /// <summary>Invoices that took plants of this batch, with whether a credit note later cancelled them.</summary>
    private static async Task<List<TraceSale>> LoadSalesAsync(Guid id, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.InvoiceLines
            .AsNoTracking()
            .Where(line => line.BatchId == id)
            .Join(db.Invoices.Where(invoice => invoice.Kind == InvoiceKind.Invoice), line => line.InvoiceId, invoice => invoice.Id, (line, invoice) => new
            {
                invoice.Id,
                invoice.Number,
                invoice.IssuedOn,
                invoice.CustomerName,
                line.Quantity,
                Cancelled = db.Invoices.Any(credit => credit.CreditsInvoiceId == invoice.Id),
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.Id)
            .Select(group => new TraceSale(group.Key, group.First().Number, group.First().IssuedOn, group.First().CustomerName, group.Sum(row => row.Quantity), group.First().Cancelled))
            .OrderBy(sale => sale.Number)
            .ToList();
    }

    /// <summary>
    /// Everything recorded about the batch, oldest first, and the containers it has been in. Work done to a container
    /// counts while the batch was in it: from its first arrival until it last left, or until now if some are still there.
    /// </summary>
    private static async Task<(List<TraceStep> Steps, List<TraceContainer> Containers)> LoadWorkAsync(
        Guid id,
        GrowLogDbContext db,
        EventStore store,
        Dictionary<Guid, string> paths,
        CancellationToken cancellationToken)
    {
        var lines = await db.EventLines
            .AsNoTracking()
            .Where(line => line.BatchId == id && !db.Events.Any(correction => correction.CorrectsEventId == line.EventId))
            .Select(line => new BatchLine(line.EventId, line.ContainerId, line.QuantityChange))
            .ToListAsync(cancellationToken);

        var containerIds = lines.Select(line => line.ContainerId).Distinct().ToList();
        var lineEventIds = lines.Select(line => line.EventId).Distinct().ToList();
        var events = await LoadEventsAsync(db, lineEventIds, containerIds, cancellationToken);

        // A split names the new container, which may never have held this batch.
        var named = containerIds.Concat(events.Select(e => e.ContainerId).OfType<Guid>()).Distinct().ToList();
        var labels = await db.Containers
            .AsNoTracking()
            .Where(container => named.Contains(container.Id))
            .ToDictionaryAsync(container => container.Id, cancellationToken);
        var stock = (await store.GetStockAsync(cancellationToken)).Where(row => row.BatchId == id).ToDictionary(row => row.ContainerId, row => row.Quantity);
        var placements = await store.GetPlacementsAsync(containerIds, cancellationToken);

        var times = events.ToDictionary(e => e.Id, e => e.At);
        var windows = lines
            .GroupBy(line => line.ContainerId)
            .ToDictionary(
                group => group.Key,
                group => (From: group.Min(line => times[line.EventId]), Until: stock.ContainsKey(group.Key) ? (DateTimeOffset?)null : group.Max(line => times[line.EventId])));

        var steps = events
            .Where(e => lineEventIds.Contains(e.Id) || Applies(e, id, windows))
            .Select(e => StepOf(e, lines, labels, paths))
            .ToList();

        var containers = containerIds
            .Select(containerId => new TraceContainer(
                labels[containerId].LabelCode,
                labels[containerId].Kind,
                stock.GetValueOrDefault(containerId),
                placements.GetValueOrDefault(containerId)?.LocationId is { } place ? paths.GetValueOrDefault(place) : null))
            .OrderBy(container => container.LabelCode, StringComparer.Ordinal)
            .ToList();

        return (steps, containers);
    }

    /// <summary>Standing events with a line for the batch, and work done to the containers it has been in.</summary>
    private static async Task<List<StandingEvent>> LoadEventsAsync(GrowLogDbContext db, List<Guid> lineEventIds, List<Guid> containerIds, CancellationToken cancellationToken)
    {
        var rows = await db.Events
            .AsNoTracking()
            .Where(e => !db.Events.Any(correction => correction.CorrectsEventId == e.Id)
                && (lineEventIds.Contains(e.Id)
                    || (e.ContainerId != null
                        && containerIds.Contains(e.ContainerId.Value)
                        && !e.Lines.Any()
                        && (ContainerWork.Contains(e.Type) || (e.Type == EventType.Corrected && e.LocationId != null)))))
            .Select(e => new
            {
                e.Id,
                e.Type,
                e.OccurredAt,
                e.RecordedAt,
                e.ContainerId,
                e.LocationId,
                e.Payload,
                RecordedBy = db.Users.Where(user => user.Id == e.RecordedBy).Select(user => user.UserName).FirstOrDefault(),
                OriginalType = db.Events.Where(original => original.Id == e.CorrectsEventId).Select(original => (EventType?)original.Type).FirstOrDefault(),
                OriginalAt = db.Events.Where(original => original.Id == e.CorrectsEventId).Select(original => (DateTimeOffset?)original.OccurredAt).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        // A correction that replaced work stands where the original stood, as the original kind of work.
        return rows
            .Select(row => new StandingEvent(
                row.Id,
                row.OriginalType ?? row.Type,
                row.OriginalAt ?? row.OccurredAt,
                row.ContainerId,
                row.LocationId,
                row.Payload,
                row.RecordedBy,
                row.RecordedAt,
                Corrected: row.OriginalType is not null))
            .OrderBy(e => e.At)
            .ThenBy(e => e.RecordedAt)
            .ToList();
    }

    private static bool Applies(StandingEvent work, Guid batchId, Dictionary<Guid, (DateTimeOffset From, DateTimeOffset? Until)> windows)
    {
        if (work.ContainerId is not { } containerId || !windows.TryGetValue(containerId, out var window) || work.At < window.From || work.At > window.Until)
        {
            return false;
        }

        // A stock count is of one batch.
        return work.Type != EventType.StockCounted || EventStore.CountOf(work.Payload)?.BatchId == batchId;
    }

    private static TraceStep StepOf(StandingEvent work, List<BatchLine> lines, Dictionary<Guid, Container> containers, Dictionary<Guid, string> paths)
    {
        var own = lines.Where(line => line.EventId == work.Id).ToList();
        var into = own.Where(line => line.QuantityChange > 0).ToList();
        var outOf = own.Where(line => line.QuantityChange < 0).ToList();

        // Moving plants into a new container shows how many went in; anything else shows the change.
        var moved = into.Count > 0 && outOf.Count > 0;
        int? quantity = own.Count == 0 ? null : moved ? into.Sum(line => line.QuantityChange) : own.Sum(line => line.QuantityChange);
        var where = work.ContainerId ?? own.FirstOrDefault()?.ContainerId;

        return new TraceStep(
            work.Id,
            work.Type,
            work.At,
            where is { } containerId ? containers[containerId].LabelCode : null,
            moved ? containers[outOf[0].ContainerId].LabelCode : null,
            quantity,
            work.LocationId is { } place ? paths.GetValueOrDefault(place) : null,
            work.RecordedBy,
            JsonSerializer.Deserialize<JsonElement>(work.Payload),
            work.Corrected);
    }

    private sealed record BatchLine(Guid EventId, Guid ContainerId, int QuantityChange);

    private sealed record StandingEvent(Guid Id, EventType Type, DateTimeOffset At, Guid? ContainerId, Guid? LocationId, string Payload, string? RecordedBy, DateTimeOffset RecordedAt, bool Corrected);

    internal sealed record TraceMatch(Guid BatchId, string TraceabilityCode, string CommonName, string? Variety, BatchSource Source, DateTimeOffset StartedAt);

    internal sealed record TracePlant(Guid Id, string CommonName, string? Variety, string? BotanicalName, bool? XylellaHost);

    internal sealed record TraceSupplier(Guid Id, string Name, string? PassportRegistrationNumber);

    internal sealed record TracePurchase(Guid Id, DateOnly PurchasedOn, string? Reference);

    internal sealed record TraceBatchLink(Guid Id, string TraceabilityCode, string CommonName, string? Variety);

    internal sealed record TracePassport(DateOnly ReceivedOn, string SupplierName, string PartA, string PartB, string? PartC, string PartD, string? PartE, ReceivedPassportStatus Status);

    internal sealed record TraceContainer(string LabelCode, ContainerKind Kind, int Now, string? Place);

    /// <param name="LabelCode">The container the work happened in, or the plants went into.</param>
    /// <param name="FromLabelCode">For plants moved into a new container, the one they came from.</param>
    /// <param name="Quantity">Plants added (positive) or taken away (negative), or for a move how many moved.</param>
    internal sealed record TraceStep(
        Guid EventId,
        EventType Type,
        DateTimeOffset OccurredAt,
        string? LabelCode,
        string? FromLabelCode,
        int? Quantity,
        string? Place,
        string? RecordedBy,
        JsonElement Details,
        bool Corrected);

    internal sealed record TraceSale(Guid InvoiceId, int Number, DateOnly IssuedOn, string CustomerName, int Quantity, bool Cancelled);

    internal sealed record TraceTotals(int Lost, int Sold, int Now);

    internal sealed record BatchTrace(
        Guid Id,
        string TraceabilityCode,
        TracePlant Plant,
        BatchSource Source,
        string? SourceReference,
        string? MotherPlant,
        string OriginCountry,
        DateTimeOffset StartedAt,
        TraceSupplier? Supplier,
        TracePurchase? Purchase,
        TraceBatchLink? Parent,
        IReadOnlyList<TraceBatchLink> Children,
        IReadOnlyList<TracePassport> ReceivedPassports,
        IReadOnlyList<TraceContainer> Containers,
        IReadOnlyList<TraceStep> Steps,
        IReadOnlyList<TraceSale> Sales,
        TraceTotals Totals);
}
