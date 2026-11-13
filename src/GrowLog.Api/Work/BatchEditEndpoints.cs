using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using GrowLog.Api.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrowLog.Api.Work;

/// <summary>
/// Correcting a batch's details after it was started. Each correction is kept, with who made it, when, and the
/// values before and after, so the trace report shows it. The day it was sown and how many are changed by
/// undoing the start and recording it again, never by altering it. Any change can be made while nothing else has
/// been recorded on the plants; after that the start can only move earlier, and a stock count corrects the number.
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
            batch.SowBy,
            batch.SownAs,
            batch.SownAmount,
            batch.PacketShare,
            batch.PacketSays,
            batch.MotherPlantId,
            start?.At,
            start?.Quantity,
            start is not null && await StartCanChangeAsync(db, id, start, cancellationToken),
            await db.Set<ReceivedPassport>().CountAsync(passport => passport.BatchId == id, cancellationToken),
            await db.Batches.CountAsync(other => other.ParentBatchId == id, cancellationToken)));
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

        if (WantedFor(batch, request) is not { } wanted)
        {
            return TypedResults.Conflict(new EditRefused("MotherPlant"));
        }
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Nothing else may be recorded in the tray between checking the start can change and changing it.
        var start = await FindStartAsync(db, id, cancellationToken);

        if (start is not null)
        {
            await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM containers WHERE id = {0} FOR UPDATE", [start.ContainerId], cancellationToken);
            start = await FindStartAsync(db, id, cancellationToken);
        }

        var changes = await DescribeAsync(batch, wanted, db, cancellationToken);
        var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

        try
        {
            if (await RestartAsync(new Restart(batch, start, request, wanted, userId), db, store, clock, changes, cancellationToken) is { } refused)
            {
                return refused;
            }
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Someone else corrected the start at the same moment.
            return TypedResults.Conflict(new EditRefused("StartLocked"));
        }

        if (changes.Count == 0)
        {
            return TypedResults.NoContent();
        }

        Apply(batch, wanted);
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

    /// <summary>
    /// The batch as the edit wants it, or null if it can't be: cuttings taken from a mother plant record stay hers,
    /// so their source, her name and her plant come from the record.
    /// </summary>
    private static Wanted? WantedFor(Batch batch, EditBatchRequest request) =>
        batch.MotherPlantId is null ? Wanted.From(request)
        : request.Source == BatchSource.Cuttings && request.PlantId == batch.PlantId ? Wanted.From(request) with { MotherPlant = batch.MotherPlant }
        : null;

    private static void Apply(Batch batch, Wanted wanted)
    {
        batch.PlantId = wanted.PlantId;
        batch.Source = wanted.Source;
        batch.SourceReference = wanted.SourceReference;
        batch.SupplierId = wanted.SupplierId;
        batch.MotherPlant = wanted.MotherPlant;
        batch.OriginCountry = wanted.OriginCountry;
        batch.SowBy = wanted.SowBy;
        batch.SownAs = wanted.SownAs;
        batch.SownAmount = wanted.SownAmount;
        batch.PacketShare = wanted.PacketShare;
        batch.PacketSays = wanted.PacketSays;
    }

    private static async Task<bool> IsValidAsync(EditBatchRequest request, GrowLogDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var country = request.OriginCountry?.Trim() ?? "";
        var textFits = (request.SourceReference?.Length ?? 0) <= LongestText && (request.MotherPlant?.Length ?? 0) <= LongestText;
        var startFits = request.Quantity is null or (> 0 and <= 1_000_000)
            && (request.StartedAt is null || request.StartedAt <= clock.GetUtcNow() + DeviceClock.Tolerance);

        var sowingFits = request.Source != BatchSource.SeedLot || WorkEndpoints.IsSensibleSowing(request.SownAs, request.SownAmount, request.PacketShare, request.PacketSays);

        if (country.Length != 2 || !country.All(char.IsAsciiLetter) || !textFits || !startFits || !sowingFits || !Enum.IsDefined(request.Source))
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
    private static async Task<Dictionary<string, Change>> DescribeAsync(Batch batch, Wanted wanted, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        var changes = new Dictionary<string, Change>(StringComparer.Ordinal);

        void Compare(string field, string? before, string? after)
        {
            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                changes[field] = new Change(before, after);
            }
        }

        var supplierId = wanted.SupplierId;
        var plants = await db.Plants.Where(plant => plant.Id == batch.PlantId || plant.Id == wanted.PlantId).ToDictionaryAsync(plant => plant.Id, cancellationToken);
        var suppliers = await db.Suppliers.Where(supplier => supplier.Id == batch.SupplierId || supplier.Id == supplierId).ToDictionaryAsync(supplier => supplier.Id, supplier => supplier.Name, cancellationToken);

        static string PlantName(Plant plant) => plant.Variety is null ? plant.CommonName : $"{plant.CommonName} '{plant.Variety}'";

        Compare("Plant", PlantName(plants[batch.PlantId]), PlantName(plants[wanted.PlantId]));
        Compare("Source", batch.Source.ToString(), wanted.Source.ToString());
        Compare("Supplier", batch.SupplierId is { } before ? suppliers[before] : null, supplierId is { } after ? suppliers[after] : null);
        Compare("Lot or reference", batch.SourceReference, wanted.SourceReference);
        Compare("Mother plant", batch.MotherPlant, wanted.MotherPlant);
        Compare("Country of origin", batch.OriginCountry, wanted.OriginCountry);
        Compare("Sown", SowingWords(batch.SownAs, batch.SownAmount, batch.PacketShare, batch.PacketSays), SowingWords(wanted.SownAs, wanted.SownAmount, wanted.PacketShare, wanted.PacketSays));
        Compare("Sow by", batch.SowBy?.ToString("yyyy-MM", CultureInfo.InvariantCulture), wanted.SowBy?.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        return changes;
    }

    /// <summary>
    /// Undoes the start and records it again with the right day, number and kind of work, when any changed.
    /// A batch whose start can't be found, or has work recorded since, keeps its start, and asking to change it is refused.
    /// </summary>
    private static async Task<IResult?> RestartAsync(
        Restart restart,
        GrowLogDbContext db,
        EventStore store,
        TimeProvider clock,
        Dictionary<string, Change> changes,
        CancellationToken cancellationToken)
    {
        var (batch, start, request, wanted, userId) = restart;
        var type = StartOf(wanted.Source);

        if (start is null)
        {
            var asked = request.StartedAt is not null || request.Quantity is not null || wanted.Source != batch.Source;
            return asked ? TypedResults.Conflict(new EditRefused("StartLocked")) : null;
        }

        if (await StartQuantityAsync(restart, start, db, cancellationToken) is not { } quantity)
        {
            return TypedResults.Conflict(new EditRefused("CountInstead"));
        }

        var startedAt = request.StartedAt?.ToUniversalTime() ?? start.At;

        if (startedAt == start.At && quantity == start.Quantity && type == start.Type)
        {
            return null;
        }

        // Sown earlier than recorded is always safe: everything recorded since still comes after it.
        var onlyEarlier = startedAt < start.At && quantity == start.Quantity && type == start.Type;

        if (!onlyEarlier && !await StartCanChangeAsync(db, batch.Id, start, cancellationToken))
        {
            return TypedResults.Conflict(new EditRefused("StartLocked"));
        }

        var original = start.Event;
        var undo = new Event { Id = Guid.CreateVersion7(), Type = EventType.Corrected, OccurredAt = clock.GetUtcNow(), RecordedBy = userId, ContainerId = original.ContainerId, CorrectsEventId = original.Id };

        // Recorded again under the person who did the work; the undo shows who corrected it.
        var again = new Event
        {
            Id = Guid.CreateVersion7(),
            Type = type,
            OccurredAt = startedAt,
            RecordedBy = original.RecordedBy,
            ContainerId = original.ContainerId,
            LocationId = original.LocationId,
            PotSizeId = original.PotSizeId,
            Payload = original.Payload,
        };
        again.Lines.Add(new EventLine { ContainerId = start.ContainerId, BatchId = batch.Id, QuantityChange = quantity });

        // The new start first, then the undo, so the stock never dips below what later work took from it.
        if (await store.AppendAsync(again, cancellationToken) != AppendResult.Stored || await store.AppendAsync(undo, cancellationToken) != AppendResult.Stored)
        {
            return TypedResults.Conflict(new EditRefused("StartLocked"));
        }

        NoteStartChanges(changes, start.At, startedAt, start.Quantity, quantity);
        return null;
    }

    private static void NoteStartChanges(Dictionary<string, Change> changes, DateTimeOffset wasAt, DateTimeOffset isAt, int was, int now)
    {
        if (isAt != wasAt)
        {
            changes["Started"] = new Change(wasAt.ToString("O", CultureInfo.InvariantCulture), isAt.ToString("O", CultureInfo.InvariantCulture));
        }

        if (now != was)
        {
            changes["How many"] = new Change(was.ToString(CultureInfo.InvariantCulture), now.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>How seed was sown, as kept in the record of corrections.</summary>
    private static string? SowingWords(SownMeasure? sownAs, decimal? amount, PacketShare? share, string? says) => sownAs switch
    {
        null => null,
        SownMeasure.About => string.Create(CultureInfo.InvariantCulture, $"About {amount:0} seeds"),
        SownMeasure.Grams => string.Create(CultureInfo.InvariantCulture, $"{amount:0.###} g"),
        SownMeasure.Packet => $"{share?.ToString() ?? "Part"} packet{(says is null ? string.Empty : $" ({says})")}",
        SownMeasure.Unknown => "Not known",
        _ => "Counted",
    };

    private static EventType StartOf(BatchSource source) => source switch
    {
        BatchSource.BoughtIn => EventType.Received,
        BatchSource.Cuttings => EventType.CuttingsTaken,
        _ => EventType.Sown,
    };

    /// <summary>The work that started the batch: its first sowing, arrival or cuttings, still standing.</summary>
    private static async Task<Start?> FindStartAsync(GrowLogDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var candidates = await db.Events.AsNoTracking().Include(e => e.Lines)
            .Where(e => !db.Events.Any(correction => correction.CorrectsEventId == e.Id)
                && e.Lines.Any(line => line.BatchId == id && line.QuantityChange >= 0))
            .ToListAsync(cancellationToken);
        Start? first = null;
        foreach (var candidate in candidates)
        {
            var (type, at) = await EventHistory.ResolveAsync(candidate, db, cancellationToken);
            if (Starts.Contains(type) && (first is null || at < first.At))
            {
                var line = candidate.Lines.First(line => line.BatchId == id);
                first = new Start(candidate, line.ContainerId, line.QuantityChange, type, at);
            }
        }
        return first;
    }

    /// <summary>Only while nothing else has been recorded on the batch or its first container.</summary>
    /// <summary>
    /// The number the start should have, or null when it can't be had. Seed can move between counted and measured
    /// another way (a weight, a packet, about so many) only while nothing has been recorded since it was sown; after
    /// that, only a count gives it a number. Counted needs its number; measured another way starts with none.
    /// </summary>
    private static async Task<int?> StartQuantityAsync(Restart restart, Start start, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        var wasUncounted = start.Quantity == 0;
        var uncounted = restart.Wanted.SownAs is not (null or SownMeasure.Counted);
        var quantity = restart.Request.Quantity;

        if (wasUncounted != uncounted && !await StartCanChangeAsync(db, restart.Batch.Id, start, cancellationToken))
        {
            return null;
        }

        if ((uncounted && quantity is not null) || (wasUncounted && !uncounted && quantity is null))
        {
            return null;
        }

        return uncounted ? 0 : quantity ?? start.Quantity;
    }

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

    private sealed record Start(Event Event, Guid ContainerId, int Quantity, EventType Type, DateTimeOffset At);

    private sealed record Restart(Batch Batch, Start? Start, EditBatchRequest Request, Wanted Wanted, Guid UserId);

    /// <summary>The batch as the edit wants it. Details that don't apply to how it started are cleared.</summary>
    private sealed record Wanted(
        Guid PlantId,
        BatchSource Source,
        string? SourceReference,
        Guid? SupplierId,
        string? MotherPlant,
        string OriginCountry,
        DateOnly? SowBy,
        SownMeasure? SownAs = null,
        decimal? SownAmount = null,
        PacketShare? PacketShare = null,
        string? PacketSays = null)
    {
        public static Wanted From(EditBatchRequest request)
        {
            var cuttings = request.Source == BatchSource.Cuttings;

            return new Wanted(
                request.PlantId,
                request.Source,
                cuttings ? null : Clean(request.SourceReference),
                cuttings ? null : request.SupplierId,
                cuttings ? Clean(request.MotherPlant) : null,
                request.OriginCountry.Trim().ToUpperInvariant(),
                request.Source == BatchSource.SeedLot ? request.SowBy : null,
                request.Source == BatchSource.SeedLot ? request.SownAs : null,
                request.Source == BatchSource.SeedLot && request.SownAs is SownMeasure.About or SownMeasure.Grams ? request.SownAmount : null,
                request.Source == BatchSource.SeedLot && request.SownAs == SownMeasure.Packet ? request.PacketShare : null,
                request.Source == BatchSource.SeedLot && request.SownAs == SownMeasure.Packet ? Clean(request.PacketSays) : null);
        }
    }

    internal sealed record Change(string? Before, string? After);

    internal sealed record EditBatchRequest(
        Guid PlantId,
        BatchSource Source,
        string? SourceReference,
        Guid? SupplierId,
        string? MotherPlant,
        string OriginCountry,
        DateTimeOffset? StartedAt,
        int? Quantity,
        DateOnly? SowBy = null,
        SownMeasure? SownAs = null,
        decimal? SownAmount = null,
        PacketShare? PacketShare = null,
        string? PacketSays = null);

    internal sealed record EditRefused(string Problem);

    /// <param name="StartedAt">When it was sown, arrived or had cuttings taken, if its start is still recorded.</param>
    /// <param name="StartCanChange">Whether the day, number and source can still be corrected: nothing else is recorded yet.</param>
    /// <param name="ReceivedPassports">Passports received with the batch, which keep what was printed on them whatever is corrected.</param>
    /// <param name="SplitInto">Batches split from this one, which keep their own details.</param>
    internal sealed record BatchDetails(
        Guid Id,
        string TraceabilityCode,
        Guid PlantId,
        BatchSource Source,
        string? SourceReference,
        Guid? SupplierId,
        string? MotherPlant,
        string OriginCountry,
        DateOnly? SowBy,
        SownMeasure? SownAs,
        decimal? SownAmount,
        PacketShare? PacketShare,
        string? PacketSays,
        Guid? MotherPlantId,
        DateTimeOffset? StartedAt,
        int? Quantity,
        bool StartCanChange,
        int ReceivedPassports,
        int SplitInto);
}
