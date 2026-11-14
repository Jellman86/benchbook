using System.Security.Claims;
using System.Text.Json;
using Hortiplanner.Api.Accounts;
using Hortiplanner.Api.Catalogue;
using Hortiplanner.Api.Data;
using Hortiplanner.Api.Production;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hortiplanner.Api.Work;

/// <summary>Recording what happened. Every request carries a client-made event identifier, so a resend is safe.</summary>
internal static class WorkEndpoints
{
    private const int MaxQuantity = 1_000_000;
    private const int MaxRecent = 50;
    private const int LongestNote = 1000;
    private const int LongestShortText = 200;

    public static void MapWorkEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/batches", StartBatchAsync).AddEndpointFilter(RequestFingerprint.Capture).Produces<BatchStarted>(StatusCodes.Status201Created).Produces<BatchStarted>().Produces<WorkRefused>(StatusCodes.Status409Conflict);

        var containers = app.MapGroup("/api/containers").AddEndpointFilter(RequestFingerprint.Capture);
        containers.MapGet("/by-label/{label}", FindByLabelAsync).Produces<ContainerResponse>();
        containers.MapGet("/active", ListActiveAsync);
        containers.MapPost("/{id:guid}/germinated", RecordGerminatedAsync).ProducesWork();
        containers.MapPost("/{id:guid}/ready", RecordReadyAsync).ProducesWork();
        containers.MapPost("/{id:guid}/losses", RecordLossAsync).ProducesWork();
        containers.MapPost("/{id:guid}/move", MoveAsync).ProducesWork();
        containers.MapPost("/{id:guid}/transfer", TransferAsync).Produces<ContainerCreated>(StatusCodes.Status201Created).Produces<ContainerCreated>().Produces<WorkRefused>(StatusCodes.Status409Conflict);
        containers.MapPost("/{id:guid}/count", CountAsync).ProducesWork();
        containers.MapPost("/{id:guid}/treatments", RecordTreatmentAsync).ProducesWork();
        containers.MapPost("/{id:guid}/notes", RecordNoteAsync).ProducesWork();

        var events = app.MapGroup("/api/events").AddEndpointFilter(RequestFingerprint.Capture);
        events.MapGet("/recent", RecentAsync);
        events.MapPost("/{id:guid}/correct", CorrectAsync).ProducesWork();
    }

    private static RouteHandlerBuilder ProducesWork(this RouteHandlerBuilder builder) =>
        builder.Produces<WorkRecorded>(StatusCodes.Status201Created).Produces<WorkRecorded>().Produces<WorkRefused>(StatusCodes.Status409Conflict);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsValidQuantity(int quantity) => quantity is > 0 and <= MaxQuantity;

    private static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static Event NewEvent(
        Guid id,
        EventType type,
        DateTimeOffset occurredAt,
        ClaimsPrincipal user,
        Guid? containerId,
        Guid? locationId = null,
        Guid? potSizeId = null,
        object? details = null,
        Guid? correctsEventId = null) => new()
        {
            Id = id,
            Type = type,
            OccurredAt = occurredAt,
            RecordedBy = UserId(user),
            ContainerId = containerId,
            LocationId = locationId,
            PotSizeId = potSizeId,
            CorrectsEventId = correctsEventId,
            Payload = details is null ? "{}" : JsonSerializer.Serialize(details),
        };

    private static void AddLine(Event work, Guid containerId, Guid batchId, int change) =>
        work.Lines.Add(new EventLine { ContainerId = containerId, BatchId = batchId, QuantityChange = change });

    private static Conflict<WorkRefused> Refused(string problem) => TypedResults.Conflict(new WorkRefused(problem));

    /// <summary>Stores the event and anything tracked with it, turning refusals into answers a screen can act on.</summary>
    private static async Task<IResult> StoreAsync(Func<Task<AppendResult>> append, Func<Task<object>> describe)
    {
        try
        {
            return await append() switch
            {
                AppendResult.Stored => TypedResults.Json(await describe(), statusCode: StatusCodes.Status201Created),
                AppendResult.AlreadyStored => TypedResults.Json(await describe(), statusCode: StatusCodes.Status200OK),
                AppendResult.IdInUse => Refused("IdInUse"),
                AppendResult.NotInContainer => Refused("NotInContainer"),
                AppendResult.BeforeStart => Refused("BeforeStart"),
                AppendResult.OnSite => Refused("OnSite"),
                _ => Refused("BelowZero"),
            };
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ix_events_corrects_event_id",
        })
        {
            return Refused("AlreadyCorrected");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.CheckViolation,
        })
        {
            return TypedResults.BadRequest();
        }
    }

    private static Task<IResult> StoreSimpleAsync(EventStore store, Event work, CancellationToken cancellationToken) =>
        StoreAsync(() => store.AppendAsync(work, cancellationToken), () => Task.FromResult<object>(new WorkRecorded(work.Id, CountedSince: work.Adjustments.Any(adjustment => adjustment.CountEventId != work.Id))));

    private static async Task<IResult> StartBatchAsync(
        StartBatchRequest request,
        ClaimsPrincipal user,
        HortiplannerDbContext db,
        EventStore store,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await store.LockRequestAsync(request.EventId, cancellationToken);
        if (await store.MatchRequestAsync(request.EventId, UserId(user), cancellationToken) is { } repeated)
        {
            return repeated == AppendResult.AlreadyStored ? TypedResults.Ok(await DescribeStartedBatchAsync(request.EventId, db, cancellationToken)) : Refused("IdInUse");
        }
        // Seed sown without a count starts the tray at nought, "not counted yet", until the first count.
        var sownAs = request.Source == BatchSource.SeedLot ? request.SownAs : null;
        var counted = sownAs is null or SownMeasure.Counted;

        // Bought-in plants must say who supplied them, for the passport record.
        if ((counted && !IsValidQuantity(request.Quantity))
            || (request.Source == BatchSource.BoughtIn && request.SupplierId is null)
            || !IsSensibleSowing(request)
            || (request.MotherPlantId is not null && request.Source != BatchSource.Cuttings))
        {
            return TypedResults.BadRequest();
        }

        // Cuttings from a mother plant take its next number. The mother is locked while it's chosen, so two people
        // taking cuttings at once can't both have the same number.
        var mother = await MotherForAsync(request, db, cancellationToken);

        if (request.MotherPlantId is not null && mother is null)
        {
            return TypedResults.BadRequest();
        }

        var batch = await NewBatchAsync(request, sownAs, mother, db, cancellationToken);
        var container = new Container { LabelCode = await LabelCodes.NextContainerAsync(db, cancellationToken), Kind = request.ContainerKind };

        var type = request.Source switch
        {
            BatchSource.SeedLot => EventType.Sown,
            BatchSource.BoughtIn => EventType.Received,
            _ => EventType.CuttingsTaken,
        };
        var work = NewEvent(request.EventId, type, request.OccurredAt, user, container.Id, request.LocationId, request.PotSizeId);
        AddLine(work, container.Id, batch.Id, counted ? request.Quantity : 0);

        // Tracked now and saved with the event, so a refused or repeated request leaves nothing behind.
        db.AddRange(batch, container);

        var result = await StoreAsync(
            () => store.AppendAsync(work, cancellationToken),
            async () => await DescribeStartedBatchAsync(request.EventId, db, cancellationToken));

        if (result is IStatusCodeHttpResult { StatusCode: StatusCodes.Status200OK or StatusCodes.Status201Created })
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return result;
    }

    /// <summary>The mother plant cuttings are taken from, locked until the batch is saved; null if there's none or it won't do.</summary>
    private static async Task<MotherPlant?> MotherForAsync(StartBatchRequest request, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        if (request.MotherPlantId is not { } motherId)
        {
            return null;
        }

        await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM mother_plants WHERE id = {0} FOR UPDATE", [motherId], cancellationToken);
        return await db.MotherPlants.FirstOrDefaultAsync(
            mother => mother.Id == motherId && mother.ArchivedAt == null && mother.PlantId == request.PlantId,
            cancellationToken);
    }

    private static async Task<Batch> NewBatchAsync(StartBatchRequest request, SownMeasure? sownAs, MotherPlant? mother, HortiplannerDbContext db, CancellationToken cancellationToken) => new()
    {
        PlantId = request.PlantId,
        TraceabilityCode = await LabelCodes.NextBatchAsync(db, cancellationToken),
        Source = request.Source,
        SourceReference = Clean(request.SourceReference),
        SupplierId = request.SupplierId,
        PurchaseLineId = request.PurchaseLineId,
        ParentBatchId = request.ParentBatchId,
        MotherPlant = mother?.Name ?? Clean(request.MotherPlant),
        MotherPlantId = mother?.Id,
        CuttingsRound = mother is null
            ? null
            : (await db.Batches.Where(batch => batch.MotherPlantId == mother.Id).MaxAsync(batch => batch.CuttingsRound, cancellationToken) ?? 0) + 1,
        OriginCountry = request.OriginCountry.Trim().ToUpperInvariant(),
        SowBy = request.Source == BatchSource.SeedLot ? request.SowBy : null,
        SownAs = sownAs,
        SownAmount = sownAs is SownMeasure.About or SownMeasure.Grams ? request.SownAmount : null,
        PacketShare = sownAs == SownMeasure.Packet ? request.PacketShare : null,
        PacketSays = sownAs == SownMeasure.Packet ? Clean(request.PacketSays) : null,
    };

    private static bool IsSensibleSowing(StartBatchRequest request) =>
        IsSensibleSowing(request.SownAs, request.SownAmount, request.PacketShare, request.PacketSays);

    /// <summary>A sowing's measure makes sense: about so many seeds is a whole number, grams are positive, a packet says how much.</summary>
    internal static bool IsSensibleSowing(SownMeasure? sownAs, decimal? amount, PacketShare? share, string? says) => sownAs switch
    {
        SownMeasure.About => amount is >= 1 and <= MaxQuantity && decimal.Truncate(amount.Value) == amount,
        SownMeasure.Grams => amount is > 0 and <= 10_000,
        SownMeasure.Packet => share is not null && (says?.Length ?? 0) <= LongestShortText,
        _ => true,
    };

    private static async Task<object> DescribeStartedBatchAsync(Guid eventId, HortiplannerDbContext db, CancellationToken cancellationToken) =>
        await db.EventLines
            .AsNoTracking()
            .Where(line => line.EventId == eventId)
            .Join(db.Containers, line => line.ContainerId, container => container.Id, (line, container) => new { line.BatchId, container })
            .Join(db.Batches, row => row.BatchId, batch => batch.Id, (row, batch) => new BatchStarted(eventId, batch.Id, batch.TraceabilityCode, row.container.Id, row.container.LabelCode))
            .SingleAsync(cancellationToken);

    private static async Task<IResult> FindByLabelAsync(string label, HortiplannerDbContext db, EventStore store, CancellationToken cancellationToken)
    {
        if (await db.Containers.AsNoTracking().FirstOrDefaultAsync(container => container.LabelCode == label, cancellationToken) is not { } container)
        {
            return TypedResults.NotFound();
        }

        var state = await store.GetContainerStateAsync(container.Id, cancellationToken);
        var batchIds = state.Contents.Select(count => count.BatchId).ToList();

        var batches = await db.Batches
            .Where(batch => batchIds.Contains(batch.Id))
            .Join(db.Plants, batch => batch.PlantId, plant => plant.Id, (batch, plant) => new { batch.Id, batch.TraceabilityCode, PlantId = plant.Id, plant.CommonName, plant.Variety, batch.Source })
            .ToListAsync(cancellationToken);

        var rough = await store.RoughlyCountedAsync([container.Id], cancellationToken);
        var contents = state.Contents
            .Join(batches, count => count.BatchId, batch => batch.Id, (count, batch) => new ContainerLine(batch.Id, batch.TraceabilityCode, batch.PlantId, batch.CommonName, batch.Variety, count.Quantity, batch.Source, count.NotCounted, rough.Contains((container.Id, batch.Id))))
            .ToList();

        var paths = await PlacePaths.LoadAsync(db, cancellationToken);
        var potSizeName = await db.PotSizes.Where(size => size.Id == state.PotSizeId).Select(size => size.Name).FirstOrDefaultAsync(cancellationToken);
        var stage = (await ProductionEndpoints.FindStagesAsync([container.Id], db, cancellationToken)).GetValueOrDefault(container.Id);

        return TypedResults.Ok(new ContainerResponse(
            container.Id,
            container.LabelCode,
            container.Kind,
            contents,
            state.LocationId,
            state.LocationId is { } locationId ? paths.GetValueOrDefault(locationId) : null,
            state.PotSizeId,
            potSizeName,
            stage?.Stage,
            stage?.Since,
            (await StartsAsync([container.Id], db, cancellationToken)).TryGetValue(container.Id, out var startedAt) ? startedAt : null));
    }

    /// <summary>When each container was started: its earliest work that still stands. Work can't be dated before it.</summary>
    private static async Task<Dictionary<Guid, DateTimeOffset>> StartsAsync(List<Guid> containerIds, HortiplannerDbContext db, CancellationToken cancellationToken) =>
        await db.EventLines
            .Where(line => containerIds.Contains(line.ContainerId))
            .Join(db.Events.Where(e => !db.Events.Any(c => c.CorrectsEventId == e.Id)), line => line.EventId, e => e.Id, (line, e) => new { line.ContainerId, OccurredAt = line.EffectiveAt ?? e.OccurredAt })
            .GroupBy(row => row.ContainerId)
            .Select(group => new { group.Key, Started = group.Min(row => row.OccurredAt) })
            .ToDictionaryAsync(row => row.Key, row => row.Started, cancellationToken);

    /// <summary>Every container with plants in it, or seed not counted yet, so a device can still open a label with no signal.</summary>
    private static async Task<List<ContainerResponse>> ListActiveAsync(HortiplannerDbContext db, EventStore store, CancellationToken cancellationToken)
    {
        var stock = await store.GetStockAsync(cancellationToken);
        var containerIds = stock.Select(row => row.ContainerId).Distinct().ToList();

        var containers = await db.Containers.Where(container => containerIds.Contains(container.Id)).ToListAsync(cancellationToken);
        var batches = await db.Batches
            .Where(batch => db.EventLines.Any(line => line.BatchId == batch.Id && containerIds.Contains(line.ContainerId)))
            .Join(db.Plants, batch => batch.PlantId, plant => plant.Id, (batch, plant) => new { batch.Id, batch.TraceabilityCode, PlantId = plant.Id, plant.CommonName, plant.Variety, batch.Source })
            .ToDictionaryAsync(batch => batch.Id, cancellationToken);
        var places = await store.GetPlacementsAsync(containerIds, cancellationToken);
        var locationNames = await PlacePaths.LoadAsync(db, cancellationToken);
        var stages = await ProductionEndpoints.FindStagesAsync(containerIds, db, cancellationToken);
        var potSizeNames = await db.PotSizes.ToDictionaryAsync(size => size.Id, size => size.Name, cancellationToken);
        var rough = await store.RoughlyCountedAsync(containerIds, cancellationToken);
        var starts = await StartsAsync(containerIds, db, cancellationToken);

        return containers.ConvertAll(container =>
        {
            var placement = places.GetValueOrDefault(container.Id);

            var contents = stock
                .Where(row => row.ContainerId == container.Id)
                .Select(row => new ContainerLine(row.BatchId, batches[row.BatchId].TraceabilityCode, batches[row.BatchId].PlantId, batches[row.BatchId].CommonName, batches[row.BatchId].Variety, row.Quantity, batches[row.BatchId].Source, row.NotCounted, rough.Contains((row.ContainerId, row.BatchId))))
                .ToList();
            var stage = stages.GetValueOrDefault(container.Id);

            return new ContainerResponse(
                container.Id,
                container.LabelCode,
                container.Kind,
                contents,
                placement?.LocationId,
                placement?.LocationId is { } locationId ? locationNames[locationId] : null,
                placement?.PotSizeId,
                placement?.PotSizeId is { } potSizeId ? potSizeNames[potSizeId] : null,
                stage?.Stage,
                stage?.Since,
                starts.TryGetValue(container.Id, out var startedAt) ? startedAt : null);
        });
    }

    private static async Task<IResult> RecordGerminatedAsync(Guid id, GerminatedRequest request, ClaimsPrincipal user, HortiplannerDbContext db, EventStore store, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await store.LockRequestAsync(request.EventId, cancellationToken);
        await store.LockAsync([id], cancellationToken);
        var work = NewEvent(request.EventId, EventType.Germinated, request.OccurredAt, user, id,
            details: request.CountEventId is { } countId ? new { countEventId = countId } : null);
        var result = await store.AppendAsync(work, cancellationToken);
        if (result == AppendResult.Stored && request.CountEventId is { } countEvent)
        {
            if (request.BatchId is not { } batch || request.Counted is not (>= 0 and <= MaxQuantity))
            {
                return TypedResults.BadRequest();
            }
            result = await store.AppendStockCountAsync(NewEvent(countEvent, EventType.StockCounted, work.OccurredAt, user, id), batch, request.Counted.Value, cancellationToken, request.Rough);
        }
        if (result is AppendResult.Stored or AppendResult.AlreadyStored)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return await StoreAsync(() => Task.FromResult(result), () => Task.FromResult<object>(new WorkRecorded(work.Id)));
    }

    private static Task<IResult> RecordReadyAsync(Guid id, ReadyRequest request, ClaimsPrincipal user, EventStore store, CancellationToken cancellationToken) =>
        StoreSimpleAsync(store, NewEvent(request.EventId, EventType.Ready, request.OccurredAt, user, id), cancellationToken);

    /// <summary>
    /// A loss of a number of plants, or from a tray nobody has counted, all of them or some: neither changes a count
    /// nobody has made, and "all of them" ends the tray.
    /// </summary>
    private static async Task<IResult> RecordLossAsync(Guid id, LossRequest request, ClaimsPrincipal user, HortiplannerDbContext db, EventStore store, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await store.LockRequestAsync(request.EventId, cancellationToken);
        await store.LockAsync([id], cancellationToken);
        if (await store.MatchRequestAsync(request.EventId, UserId(user), cancellationToken) is { } repeated)
        {
            return repeated == AppendResult.AlreadyStored ? TypedResults.Ok(new WorkRecorded(request.EventId)) : Refused("IdInUse");
        }
        var result = await RecordLockedLossAsync(id, request, user, store, cancellationToken);
        if (result is IStatusCodeHttpResult { StatusCode: StatusCodes.Status200OK or StatusCodes.Status201Created })
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return result;
    }

    private static async Task<IResult> RecordLockedLossAsync(Guid id, LossRequest request, ClaimsPrincipal user, EventStore store, CancellationToken cancellationToken)
    {
        // Sent as "all" or "some" from a tray that someone has counted since, as when work queued offline arrives late.
        if (request.All is { } said && !await store.IsNotCountedAsync(id, request.BatchId, cancellationToken))
        {
            if (!said)
            {
                return Refused("NowCounted");
            }

            var here = (await store.GetContainerStateAsync(id, cancellationToken)).Contents.FirstOrDefault(line => line.BatchId == request.BatchId)?.Quantity ?? 0;

            if (here <= 0)
            {
                return Refused("BelowZero");
            }

            var everyOne = NewEvent(request.EventId, EventType.Lost, request.OccurredAt, user, id, details: new { reason = request.Reason.ToString(), all = true });
            AddLine(everyOne, id, request.BatchId, -here);
            return await StoreSimpleAsync(store, everyOne, cancellationToken);
        }

        if (await store.IsNotCountedAsync(id, request.BatchId, cancellationToken))
        {
            if (request.All is not { } all)
            {
                return TypedResults.BadRequest();
            }

            var unknown = NewEvent(request.EventId, EventType.Lost, request.OccurredAt, user, id, details: new { reason = request.Reason.ToString(), all });
            AddLine(unknown, id, request.BatchId, 0);
            return await StoreSimpleAsync(store, unknown, cancellationToken);
        }

        if (!IsValidQuantity(request.Quantity))
        {
            return TypedResults.BadRequest();
        }

        var work = NewEvent(request.EventId, EventType.Lost, request.OccurredAt, user, id, details: new { reason = request.Reason.ToString() });
        AddLine(work, id, request.BatchId, -request.Quantity);
        return await StoreSimpleAsync(store, work, cancellationToken);
    }

    /// <summary>A tray goes on a bench, bed or house that's still in use, never on the site as a whole.</summary>
    private static async Task<IResult> MoveAsync(Guid id, MoveRequest request, ClaimsPrincipal user, HortiplannerDbContext db, EventStore store, CancellationToken cancellationToken)
    {
        if (!await db.Locations.AnyAsync(place => place.Id == request.LocationId && place.ArchivedAt == null && place.Kind != LocationKind.Site, cancellationToken))
        {
            return TypedResults.BadRequest();
        }

        return await StoreSimpleAsync(store, NewEvent(request.EventId, EventType.Moved, request.OccurredAt, user, id, request.LocationId), cancellationToken);
    }

    /// <summary>Potting on or pricking out: moves plants into a new container, within the same batch.</summary>
    private static async Task<IResult> TransferAsync(
        Guid id,
        TransferRequest request,
        ClaimsPrincipal user,
        HortiplannerDbContext db,
        EventStore store,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await store.LockRequestAsync(request.EventId, cancellationToken);
        await store.LockAsync([id], cancellationToken);
        if (await store.MatchRequestAsync(request.EventId, UserId(user), cancellationToken) is { } repeated)
        {
            return repeated == AppendResult.AlreadyStored
                ? TypedResults.Ok(await db.Events.Where(e => e.Id == request.EventId).Join(db.Containers, e => e.ContainerId, container => container.Id, (e, container) => new ContainerCreated(e.Id, container.Id, container.LabelCode, false)).SingleAsync(cancellationToken))
                : Refused("IdInUse");
        }
        if (!IsValidQuantity(request.Quantity) || request.Type is not (EventType.PottedOn or EventType.PrickedOut))
        {
            return TypedResults.BadRequest();
        }

        var locationId = request.LocationId ?? (await store.GetContainerStateAsync(id, cancellationToken)).LocationId;
        var target = new Container { LabelCode = await LabelCodes.NextContainerAsync(db, cancellationToken), Kind = request.ContainerKind };

        var work = NewEvent(request.EventId, request.Type, request.OccurredAt, user, target.Id, locationId, request.PotSizeId);
        AddLine(work, id, request.BatchId, -request.Quantity);
        AddLine(work, target.Id, request.BatchId, request.Quantity);

        // From a tray nobody had counted: what's taken and what's left are the first count, just before the move.
        // Taken and left, when given, are a real count made as the plants were moved, so they stand even if someone
        // counted the tray in the meantime.
        var counting = request.LeftInTray is not null || await store.IsNotCountedAsync(id, request.BatchId, cancellationToken);

        if (counting && (request.LeftInTray is not ({ } left and >= 0 and <= MaxQuantity) || request.CountEventId is not { } countId))
        {
            return TypedResults.Conflict(new WorkRefused("NotCounted"));
        }

        db.Add(target);

        var result = await StoreAsync(
            () => counting
                ? store.AppendCountThenAsync(NewEvent(request.CountEventId!.Value, EventType.StockCounted, request.OccurredAt, user, id), request.BatchId, request.Quantity + request.LeftInTray!.Value, work, cancellationToken)
                : store.AppendAsync(work, cancellationToken),
            async () => await db.Events
                .AsNoTracking()
                .Where(e => e.Id == request.EventId)
                .Join(db.Containers, e => e.ContainerId, container => container.Id, (e, container) => new ContainerCreated(e.Id, container.Id, container.LabelCode, false))
                .SingleAsync(cancellationToken) with
            {
                // Taken from a tray counted after the time it's dated, which keeps its count.
                CountedSince = work.Adjustments.Any(adjustment => adjustment.ContainerId == id),
            });
        if (result is IStatusCodeHttpResult { StatusCode: StatusCodes.Status200OK or StatusCodes.Status201Created })
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return result;
    }

    private static async Task<IResult> CountAsync(Guid id, CountRequest request, ClaimsPrincipal user, EventStore store, CancellationToken cancellationToken)
    {
        if (request.Counted is < 0 or > MaxQuantity)
        {
            return TypedResults.BadRequest();
        }

        var work = NewEvent(request.EventId, EventType.StockCounted, request.OccurredAt, user, id);

        return await StoreAsync(
            () => store.AppendStockCountAsync(work, request.BatchId, request.Counted, cancellationToken, request.Rough),
            () => Task.FromResult<object>(new WorkRecorded(work.Id)));
    }

    private static async Task<IResult> RecordTreatmentAsync(Guid id, TreatmentRequest request, ClaimsPrincipal user, EventStore store, CancellationToken cancellationToken)
    {
        if (Clean(request.Product) is not { Length: <= LongestShortText } product
            || Clean(request.Dose) is not { Length: <= LongestShortText } dose
            || Clean(request.Area) is not { Length: <= LongestShortText } area)
        {
            return TypedResults.BadRequest();
        }

        return await StoreSimpleAsync(store, NewEvent(request.EventId, EventType.Treated, request.OccurredAt, user, id, details: new { product, dose, area }), cancellationToken);
    }

    private static async Task<IResult> RecordNoteAsync(Guid id, NoteRequest request, ClaimsPrincipal user, EventStore store, CancellationToken cancellationToken)
    {
        if (Clean(request.Note) is not { Length: <= LongestNote } note)
        {
            return TypedResults.BadRequest();
        }

        return await StoreSimpleAsync(store, NewEvent(request.EventId, EventType.Note, request.OccurredAt, user, id, details: new { note }), cancellationToken);
    }

    /// <summary>Reverses an event. With no replacement this is an undo.</summary>
    private static async Task<IResult> CorrectAsync(
        Guid id,
        CorrectRequest request,
        ClaimsPrincipal user,
        HortiplannerDbContext db,
        EventStore store,
        CancellationToken cancellationToken)
    {
        if (await db.Events.AsNoTracking().Include(e => e.Lines).FirstOrDefaultAsync(e => e.Id == id, cancellationToken) is not { } original)
        {
            return TypedResults.NotFound();
        }

        var quantityIsValid = request.Quantity is null
            || (IsValidQuantity(request.Quantity.Value)
                && original.Type != EventType.StockCounted
                && original.Lines.Count > 0
                && original.Lines.Select(line => Math.Abs(line.QuantityChange)).Distinct().Count() == 1);

        // A sowing nobody counted has no number to correct: a count gives it one.
        var notCounted = request.Quantity is not null && original.Lines.Count > 0 && original.Lines.All(line => line.QuantityChange == 0);

        if (original.Type == EventType.Corrected || !quantityIsValid || notCounted || (request.LocationId is not null && original.LocationId is null))
        {
            return TypedResults.BadRequest();
        }

        // A sale belongs to its invoice, and is reversed only by a credit note, so the two always agree.
        if (original.Type == EventType.Sold)
        {
            return Refused("SoldByInvoice");
        }

        // A passport is issued only after an examination, so that examination stays with it.
        if (await db.IssuedPassports.AnyAsync(passport => passport.ExaminationEventId == original.Id, cancellationToken))
        {
            return Refused("ExaminedForPassport");
        }

        var replacing = request.Quantity is not null || request.LocationId is not null;

        var correction = new Event
        {
            Id = request.EventId,
            Type = EventType.Corrected,
            OccurredAt = request.OccurredAt,
            RecordedBy = UserId(user),
            ContainerId = original.ContainerId,
            LocationId = replacing ? request.LocationId ?? original.LocationId : null,
            PotSizeId = replacing ? original.PotSizeId : null,
            CorrectsEventId = original.Id,
            Payload = original.Payload,
        };

        if (replacing)
        {
            foreach (var line in original.Lines)
            {
                var change = request.Quantity is { } quantity ? quantity * Math.Sign(line.QuantityChange) : line.QuantityChange;
                // The replacement stands where the original stood in time.
                correction.Lines.Add(new EventLine { ContainerId = line.ContainerId, BatchId = line.BatchId, QuantityChange = change, EffectiveAt = original.OccurredAt });
            }
        }

        return await StoreCorrectionAsync(original, correction, db, store, cancellationToken);
    }

    private static async Task<IResult> StoreCorrectionAsync(Event original, Event correction, HortiplannerDbContext db, EventStore store, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await store.LockRequestAsync(correction.Id, cancellationToken);
        await store.LockAsync(original.Lines.Select(line => line.ContainerId).Concat(original.ContainerId is { } id ? [id] : []), cancellationToken);
        if (await store.MatchRequestAsync(correction.Id, correction.RecordedBy, cancellationToken) is { } repeated)
        {
            return repeated == AppendResult.AlreadyStored ? TypedResults.Ok(new WorkRecorded(correction.Id)) : Refused("IdInUse");
        }
        using var details = JsonDocument.Parse(original.Payload);
        if (original.Type == EventType.Germinated && details.RootElement.TryGetProperty("countEventId", out var count))
        {
            var undoCount = new Event
            {
                Id = Guid.CreateVersion7(),
                Type = EventType.Corrected,
                OccurredAt = correction.OccurredAt,
                RecordedBy = correction.RecordedBy,
                ContainerId = original.ContainerId,
                CorrectsEventId = count.GetGuid(),
            };
            if (await store.AppendAsync(undoCount, cancellationToken) is not (AppendResult.Stored or AppendResult.AlreadyStored))
            {
                return Refused("BelowZero");
            }
        }
        var result = await StoreSimpleAsync(store, correction, cancellationToken);
        if (result is IStatusCodeHttpResult { StatusCode: StatusCodes.Status200OK or StatusCodes.Status201Created })
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return result;
    }

    /// <summary>The signed-in person's latest actions, with what a screen needs to offer them again in one press.</summary>
    /// <param name="everyone">For the owner: everyone's work, not just their own.</param>
    private static async Task<List<RecentAction>> RecentAsync(ClaimsPrincipal user, HortiplannerDbContext db, int limit = 10, bool everyone = false, CancellationToken cancellationToken = default)
    {
        Guid? userId = everyone && user.IsInRole(AccountServices.OwnerRole) && user.FindFirst(AccountServices.BenchSessionClaim) is null
            ? null
            : UserId(user);

        var rows = await db.Events
            .AsNoTracking()
            .Where(e => (userId == null || e.RecordedBy == userId) && e.Type != EventType.Corrected && !db.Events.Any(c => c.CorrectsEventId == e.Id))
            .OrderByDescending(e => e.RecordedAt)
            .ThenByDescending(e => e.Id)
            .Take(Math.Clamp(limit, 1, MaxRecent))
            .Select(e => new
            {
                e.Id,
                e.Type,
                e.OccurredAt,
                e.ContainerId,
                RecordedBy = db.Users.Where(person => person.Id == e.RecordedBy).Select(person => person.UserName).FirstOrDefault(),
                LabelCode = db.Containers.Where(c => c.Id == e.ContainerId).Select(c => c.LabelCode).FirstOrDefault(),
                ContainerKind = db.Containers.Where(c => c.Id == e.ContainerId).Select(c => (ContainerKind?)c.Kind).FirstOrDefault(),
                e.LocationId,
                LocationName = db.Locations.Where(l => l.Id == e.LocationId).Select(l => l.Name).FirstOrDefault(),
                e.PotSizeId,
                PotSizeName = db.PotSizes.Where(s => s.Id == e.PotSizeId).Select(s => s.Name).FirstOrDefault(),
                e.Payload,
            })
            .ToListAsync(cancellationToken);

        var lines = await LoadRecentLinesAsync(rows.ConvertAll(row => row.Id), db, cancellationToken);
        var paths = await PlacePaths.LoadAsync(db, cancellationToken);

        return rows
            .ConvertAll(row => new RecentAction(
                row.Id,
                row.Type,
                row.OccurredAt,
                row.ContainerId,
                row.LabelCode,
                row.ContainerKind,
                row.LocationId,
                row.LocationId is { } locationId ? paths.GetValueOrDefault(locationId) : row.LocationName,
                row.PotSizeId,
                row.PotSizeName,
                JsonSerializer.Deserialize<JsonElement>(row.Payload),
                lines[row.Id].ToList(),
                row.RecordedBy));
    }

    private static async Task<ILookup<Guid, RecentLine>> LoadRecentLinesAsync(List<Guid> eventIds, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        var lines = await db.EventLines
            .Where(line => eventIds.Contains(line.EventId))
            .Join(db.Batches, line => line.BatchId, batch => batch.Id, (line, batch) => new { line, batch })
            .Join(db.Plants, row => row.batch.PlantId, plant => plant.Id, (row, plant) => new
            {
                row.line.EventId,
                Line = new RecentLine(
                    row.line.ContainerId,
                    row.line.BatchId,
                    row.line.QuantityChange,
                    plant.Id,
                    plant.CommonName,
                    plant.Variety,
                    row.batch.Source,
                    row.batch.SourceReference,
                    row.batch.SupplierId,
                    row.batch.OriginCountry,
                    row.batch.MotherPlantId,
                    row.batch.MotherPlant),
            })
            .ToListAsync(cancellationToken);

        return lines.ToLookup(line => line.EventId, line => line.Line);
    }

    /// <param name="CountedSince">
    /// The tray was counted after the time this work was dated, so the count still says how many are there and the
    /// work leaves the number as it was. Said to the person, as otherwise it looks as though nothing happened.
    /// </param>
    internal sealed record WorkRecorded(Guid EventId, bool CountedSince = false);

    internal sealed record WorkRefused(string Problem);

    internal sealed record BatchStarted(Guid EventId, Guid BatchId, string TraceabilityCode, Guid ContainerId, string LabelCode);

    internal sealed record ContainerCreated(Guid EventId, Guid ContainerId, string LabelCode, bool CountedSince = false);

    internal sealed record StartBatchRequest(
        Guid EventId,
        DateTimeOffset OccurredAt,
        Guid PlantId,
        int Quantity,
        BatchSource Source,
        string? SourceReference,
        Guid? SupplierId,
        Guid? ParentBatchId,
        string? MotherPlant,
        string OriginCountry,
        ContainerKind ContainerKind,
        Guid? LocationId,
        Guid? PotSizeId,
        Guid? PurchaseLineId,
        DateOnly? SowBy = null,
        SownMeasure? SownAs = null,
        decimal? SownAmount = null,
        PacketShare? PacketShare = null,
        string? PacketSays = null,
        Guid? MotherPlantId = null);

    internal sealed record GerminatedRequest(Guid EventId, DateTimeOffset OccurredAt, Guid? CountEventId = null, Guid? BatchId = null, int? Counted = null, bool Rough = false);

    /// <param name="All">For a tray nobody has counted: whether all of them were lost, or some.</param>
    internal sealed record LossRequest(Guid EventId, DateTimeOffset OccurredAt, Guid BatchId, int Quantity, LossReason Reason, bool? All = null);

    internal sealed record MoveRequest(Guid EventId, DateTimeOffset OccurredAt, Guid LocationId);

    internal sealed record TransferRequest(
        Guid EventId,
        DateTimeOffset OccurredAt,
        EventType Type,
        Guid BatchId,
        int Quantity,
        ContainerKind ContainerKind,
        Guid? PotSizeId,
        Guid? LocationId,
        int? LeftInTray = null,
        Guid? CountEventId = null);

    // LeftInTray and CountEventId are needed only from a tray nobody has counted: what's taken plus what's left
    // becomes its first count, recorded under CountEventId just before the move.

    /// <param name="Rough">Only a guess, as when seedlings are estimated as they come up.</param>
    internal sealed record CountRequest(Guid EventId, DateTimeOffset OccurredAt, Guid BatchId, int Counted, bool Rough = false);

    internal sealed record TreatmentRequest(Guid EventId, DateTimeOffset OccurredAt, string Product, string Dose, string Area);

    internal sealed record NoteRequest(Guid EventId, DateTimeOffset OccurredAt, string Note);

    internal sealed record CorrectRequest(Guid EventId, DateTimeOffset OccurredAt, int? Quantity, Guid? LocationId);

    internal sealed record ReadyRequest(Guid EventId, DateTimeOffset OccurredAt);

    /// <param name="NotCounted">Sown without a count and not counted since: Quantity is 0 because nobody knows it yet.</param>
    /// <param name="Rough">The latest count was only a rough guess, so Quantity is about right.</param>
    internal sealed record ContainerLine(Guid BatchId, string TraceabilityCode, Guid PlantId, string CommonName, string? Variety, int Quantity, BatchSource Source, bool NotCounted = false, bool Rough = false);

    internal sealed record ContainerResponse(
        Guid Id,
        string LabelCode,
        ContainerKind Kind,
        IReadOnlyList<ContainerLine> Contents,
        Guid? LocationId,
        string? LocationName,
        Guid? PotSizeId,
        string? PotSizeName,
        ProductionStage? Stage,
        DateTimeOffset? StageSince,
        DateTimeOffset? StartedAt = null);

    internal sealed record RecentLine(
        Guid ContainerId,
        Guid BatchId,
        int QuantityChange,
        Guid PlantId,
        string CommonName,
        string? Variety,
        BatchSource Source,
        string? SourceReference,
        Guid? SupplierId,
        string OriginCountry,
        Guid? MotherPlantId = null,
        string? MotherPlant = null);

    internal sealed record RecentAction(
        Guid Id,
        EventType Type,
        DateTimeOffset OccurredAt,
        Guid? ContainerId,
        string? LabelCode,
        ContainerKind? ContainerKind,
        Guid? LocationId,
        string? LocationName,
        Guid? PotSizeId,
        string? PotSizeName,
        JsonElement Details,
        IReadOnlyList<RecentLine> Lines,
        string? RecordedBy);
}
