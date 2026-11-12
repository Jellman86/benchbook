using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using GrowLog.Api.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Work;

/// <summary>
/// Correcting a batch's details after it was started. Each correction is kept, with who made it, when, and the
/// values before and after, so the trace report shows it. The day it was sown and how many are changed by
/// undoing the start and recording it again, never by altering it, and only while nothing else has been
/// recorded on the plants; after that a stock count corrects the number.
/// </summary>
internal static class BatchEditEndpoints
{
    private const int LongestText = 200;

    private static readonly EventType[] Starts = [EventType.Sown, EventType.Received, EventType.CuttingsTaken];

    public static void MapBatchEditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/batches/{id:guid}", GetAsync).Produces<BatchDetails>();
        app.MapPut("/api/batches/{id:guid}", EditAsync).Produces<EditRefused>(StatusCodes.Status409Conflict);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task<IResult> GetAsync(Guid id, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Batches.AsNoTracking().FirstOrDefaultAsync(batch => batch.Id == id, cancellationToken) is not { } batch)
        {
            return TypedResults.NotFound();
        }

        var start = await FindStartAsync(db, id, cancellationToken);

        return TypedResults.Ok(new BatchDetails(
            batch.Id,
            batch.TraceabilityCode,
            batch.PlantId,
            batch.Source,
            batch.SourceReference,
            batch.SupplierId,
            batch.MotherPlant,
            batch.OriginCountry,
            start?.Event.OccurredAt,
            start?.Quantity,
            start is not null && await StartCanChangeAsync(db, id, start, cancellationToken)));
    }

    private static async Task<IResult> EditAsync(
        Guid id,
        EditBatchRequest request,
        ClaimsPrincipal user,
        GrowLogDbContext db,
        EventStore store,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (await db.Batches.FirstOrDefaultAsync(batch => batch.Id == id, cancellationToken) is not { } batch)
        {
            return TypedResults.NotFound();
        }

        if (!await IsValidAsync(request, db, clock, cancellationToken))
        {
            return TypedResults.BadRequest();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var changes = await DescribeAsync(batch, request, db, cancellationToken);
        var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

        if (await RestartAsync(id, request, userId, db, store, clock, changes, cancellationToken) is { } refused)
        {
            return refused;
        }

        if (changes.Count == 0)
        {
            return TypedResults.NoContent();
        }

        batch.PlantId = request.PlantId;
        batch.Source = request.Source;
        batch.SourceReference = Clean(request.SourceReference);
        batch.SupplierId = request.Source == BatchSource.Cuttings ? null : request.SupplierId;
        batch.MotherPlant = Clean(request.MotherPlant);
        batch.OriginCountry = request.OriginCountry.Trim().ToUpperInvariant();

        db.CatalogueChanges.Add(new CatalogueChange
        {
            Entity = nameof(Batch),
            EntityKey = id.ToString(),
            Action = "Modified",
            ChangedBy = userId,
            Changes = JsonSerializer.Serialize(changes),
        });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<bool> IsValidAsync(EditBatchRequest request, GrowLogDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var country = request.OriginCountry?.Trim() ?? "";
        var textFits = (request.SourceReference?.Length ?? 0) <= LongestText && (request.MotherPlant?.Length ?? 0) <= LongestText;
        var startFits = request.Quantity is null or (> 0 and <= 1_000_000)
            && (request.StartedAt is null || request.StartedAt <= clock.GetUtcNow() + DeviceClock.Tolerance);

        if (country.Length != 2 || !country.All(char.IsAsciiLetter) || !textFits || !startFits || !Enum.IsDefined(request.Source))
        {
            return false;
        }

        // Bought-in plants must say who supplied them, for the passport record.
        if (request.Source == BatchSource.BoughtIn && request.SupplierId is null)
        {
            return false;
        }

        var supplierKnown = request.SupplierId is not { } supplierId || request.Source == BatchSource.Cuttings
            || await db.Suppliers.AnyAsync(supplier => supplier.Id == supplierId, cancellationToken);

        return supplierKnown && await db.Plants.AnyAsync(plant => plant.Id == request.PlantId, cancellationToken);
    }

    /// <summary>What the edit changes, as values people can read, with before and after.</summary>
    private static async Task<Dictionary<string, Change>> DescribeAsync(Batch batch, EditBatchRequest request, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        var changes = new Dictionary<string, Change>(StringComparer.Ordinal);

        void Compare(string field, string? before, string? after)
        {
            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                changes[field] = new Change(before, after);
            }
        }

        var supplierId = request.Source == BatchSource.Cuttings ? null : request.SupplierId;
        var plants = await db.Plants.Where(plant => plant.Id == batch.PlantId || plant.Id == request.PlantId).ToDictionaryAsync(plant => plant.Id, cancellationToken);
        var suppliers = await db.Suppliers.Where(supplier => supplier.Id == batch.SupplierId || supplier.Id == supplierId).ToDictionaryAsync(supplier => supplier.Id, supplier => supplier.Name, cancellationToken);

        static string PlantName(Plant plant) => plant.Variety is null ? plant.CommonName : $"{plant.CommonName} '{plant.Variety}'";

        Compare("Plant", PlantName(plants[batch.PlantId]), PlantName(plants[request.PlantId]));
        Compare("Source", batch.Source.ToString(), request.Source.ToString());
        Compare("Supplier", batch.SupplierId is { } before ? suppliers[before] : null, supplierId is { } after ? suppliers[after] : null);
        Compare("Lot or reference", batch.SourceReference, Clean(request.SourceReference));
        Compare("Mother plant", batch.MotherPlant, Clean(request.MotherPlant));
        Compare("Country of origin", batch.OriginCountry, request.OriginCountry.Trim().ToUpperInvariant());
        return changes;
    }

    /// <summary>Undoes the start and records it again with the right day and number, when either changed.</summary>
    private static async Task<IResult?> RestartAsync(
        Guid id,
        EditBatchRequest request,
        Guid userId,
        GrowLogDbContext db,
        EventStore store,
        TimeProvider clock,
        Dictionary<string, Change> changes,
        CancellationToken cancellationToken)
    {
        var start = await FindStartAsync(db, id, cancellationToken);
        var startedAt = request.StartedAt?.ToUniversalTime() ?? start?.Event.OccurredAt;
        var quantity = request.Quantity ?? start?.Quantity;

        if (start is null || (startedAt == start.Event.OccurredAt && quantity == start.Quantity))
        {
            return null;
        }

        if (!await StartCanChangeAsync(db, id, start, cancellationToken))
        {
            return TypedResults.Conflict(new EditRefused("StartLocked"));
        }

        var original = start.Event;
        var undo = new Event { Id = Guid.CreateVersion7(), Type = EventType.Corrected, OccurredAt = clock.GetUtcNow(), RecordedBy = userId, ContainerId = original.ContainerId, CorrectsEventId = original.Id };

        // Recorded again under the person who did the work; the undo shows who corrected it.
        var again = new Event
        {
            Id = Guid.CreateVersion7(),
            Type = original.Type,
            OccurredAt = startedAt!.Value,
            RecordedBy = original.RecordedBy,
            ContainerId = original.ContainerId,
            LocationId = original.LocationId,
            PotSizeId = original.PotSizeId,
            Payload = original.Payload,
        };
        again.Lines.Add(new EventLine { ContainerId = start.ContainerId, BatchId = id, QuantityChange = quantity!.Value });

        if (await store.AppendAsync(undo, cancellationToken) != AppendResult.Stored || await store.AppendAsync(again, cancellationToken) != AppendResult.Stored)
        {
            return TypedResults.Conflict(new EditRefused("StartLocked"));
        }

        if (startedAt != original.OccurredAt)
        {
            changes["Started"] = new Change(original.OccurredAt.ToString("O", CultureInfo.InvariantCulture), startedAt.Value.ToString("O", CultureInfo.InvariantCulture));
        }

        if (quantity != start.Quantity)
        {
            changes["How many"] = new Change(start.Quantity.ToString(CultureInfo.InvariantCulture), quantity.Value.ToString(CultureInfo.InvariantCulture));
        }

        return null;
    }

    /// <summary>The work that started the batch: its first sowing, arrival or cuttings, still standing.</summary>
    private static async Task<Start?> FindStartAsync(GrowLogDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var start = await db.Events
            .AsNoTracking()
            .Where(e => Starts.Contains(e.Type) && !db.Events.Any(correction => correction.CorrectsEventId == e.Id))
            .Where(e => e.Lines.Any(line => line.BatchId == id && line.QuantityChange > 0))
            .OrderBy(e => e.OccurredAt)
            .Select(e => new { Event = e, Line = e.Lines.First(line => line.BatchId == id) })
            .FirstOrDefaultAsync(cancellationToken);

        return start is null ? null : new Start(start.Event, start.Line.ContainerId, start.Line.QuantityChange);
    }

    /// <summary>Only while nothing else has been recorded on the batch or its first container.</summary>
    private static async Task<bool> StartCanChangeAsync(GrowLogDbContext db, Guid id, Start start, CancellationToken cancellationToken)
    {
        // An undo is not work; a correction that replaced work is.
        var standing = db.Events
            .Where(e => e.Id != start.Event.Id && !db.Events.Any(correction => correction.CorrectsEventId == e.Id))
            .Where(e => e.Type != EventType.Corrected || e.Lines.Any() || e.LocationId != null);
        var onBatch = standing.Where(e => e.Lines.Any(line => line.BatchId == id));
        var onContainer = standing.Where(e => e.ContainerId == start.ContainerId || e.Lines.Any(line => line.ContainerId == start.ContainerId));

        return !await onBatch.AnyAsync(cancellationToken) && !await onContainer.AnyAsync(cancellationToken);
    }

    private sealed record Start(Event Event, Guid ContainerId, int Quantity);

    internal sealed record Change(string? Before, string? After);

    internal sealed record EditBatchRequest(
        Guid PlantId,
        BatchSource Source,
        string? SourceReference,
        Guid? SupplierId,
        string? MotherPlant,
        string OriginCountry,
        DateTimeOffset? StartedAt,
        int? Quantity);

    internal sealed record EditRefused(string Problem);

    /// <param name="StartedAt">When it was sown, arrived or had cuttings taken, if its start is still recorded.</param>
    /// <param name="StartCanChange">Whether the day and number can still be corrected: nothing else is recorded yet.</param>
    internal sealed record BatchDetails(
        Guid Id,
        string TraceabilityCode,
        Guid PlantId,
        BatchSource Source,
        string? SourceReference,
        Guid? SupplierId,
        string? MotherPlant,
        string OriginCountry,
        DateTimeOffset? StartedAt,
        int? Quantity,
        bool StartCanChange);
}
