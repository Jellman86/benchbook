using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using GrowLog.Api.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrowLog.Api.Work;

/// <summary>Recording what happened. Every request carries a client-made event identifier, so a resend is safe.</summary>
internal static class WorkEndpoints
{
    private const int MaxQuantity = 1_000_000;
    private const int MaxRecent = 50;

    public static void MapWorkEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/batches", StartBatchAsync);

        var containers = app.MapGroup("/api/containers");
        containers.MapGet("/by-label/{label}", FindByLabelAsync);
        containers.MapPost("/{id:guid}/germinated", RecordGerminatedAsync);
        containers.MapPost("/{id:guid}/losses", RecordLossAsync);
        containers.MapPost("/{id:guid}/move", MoveAsync);
        containers.MapPost("/{id:guid}/transfer", TransferAsync);
        containers.MapPost("/{id:guid}/count", CountAsync);
        containers.MapPost("/{id:guid}/treatments", RecordTreatmentAsync);
        containers.MapPost("/{id:guid}/notes", RecordNoteAsync);

        var events = app.MapGroup("/api/events");
        events.MapGet("/recent", RecentAsync);
        events.MapPost("/{id:guid}/correct", CorrectAsync);
    }

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
                _ => Refused("BelowZero"),
            };
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
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
        StoreAsync(() => store.AppendAsync(work, cancellationToken), () => Task.FromResult<object>(new WorkRecorded(work.Id)));

    private static async Task<string> NextCodeAsync(GrowLogDbContext db, string prefix, FormattableString nextValue, CancellationToken cancellationToken)
    {
        var value = (await db.Database.SqlQuery<long>(nextValue).ToListAsync(cancellationToken)).Single();
        return string.Create(CultureInfo.InvariantCulture, $"{prefix}{value:D6}");
    }

    private static Task<string> NextContainerLabelAsync(GrowLogDbContext db, CancellationToken cancellationToken) =>
        NextCodeAsync(db, "C", $"SELECT nextval('container_numbers') AS \"Value\"", cancellationToken);

    private static async Task<IResult> StartBatchAsync(
        StartBatchRequest request,
        ClaimsPrincipal user,
        GrowLogDbContext db,
        EventStore store,
        CancellationToken cancellationToken)
    {
        if (!IsValidQuantity(request.Quantity))
        {
            return TypedResults.BadRequest();
        }

        var batch = new Batch
        {
            PlantId = request.PlantId,
            TraceabilityCode = await NextCodeAsync(db, "B", $"SELECT nextval('batch_numbers') AS \"Value\"", cancellationToken),
            Source = request.Source,
            SourceReference = Clean(request.SourceReference),
            SupplierId = request.SupplierId,
            ParentBatchId = request.ParentBatchId,
            MotherPlant = Clean(request.MotherPlant),
            OriginCountry = request.OriginCountry.Trim().ToUpperInvariant(),
        };
        var container = new Container { LabelCode = await NextContainerLabelAsync(db, cancellationToken), Kind = request.ContainerKind };

        var type = request.Source switch
        {
            BatchSource.SeedLot => EventType.Sown,
            BatchSource.BoughtIn => EventType.Received,
            _ => EventType.CuttingsTaken,
        };
        var work = NewEvent(request.EventId, type, request.OccurredAt, user, container.Id, request.LocationId, request.PotSizeId);
        AddLine(work, container.Id, batch.Id, request.Quantity);

        // Tracked now and saved with the event, so a refused or repeated request leaves nothing behind.
        db.AddRange(batch, container);

        return await StoreAsync(
            () => store.AppendAsync(work, cancellationToken),
            async () => await DescribeStartedBatchAsync(request.EventId, db, cancellationToken));
    }

    private static async Task<object> DescribeStartedBatchAsync(Guid eventId, GrowLogDbContext db, CancellationToken cancellationToken) =>
        await db.EventLines
            .AsNoTracking()
            .Where(line => line.EventId == eventId)
            .Join(db.Containers, line => line.ContainerId, container => container.Id, (line, container) => new { line.BatchId, container })
            .Join(db.Batches, row => row.BatchId, batch => batch.Id, (row, batch) => new BatchStarted(eventId, batch.Id, batch.TraceabilityCode, row.container.Id, row.container.LabelCode))
            .SingleAsync(cancellationToken);

    private static async Task<IResult> FindByLabelAsync(string label, GrowLogDbContext db, EventStore store, CancellationToken cancellationToken)
    {
        if (await db.Containers.AsNoTracking().FirstOrDefaultAsync(container => container.LabelCode == label, cancellationToken) is not { } container)
        {
            return TypedResults.NotFound();
        }

        var state = await store.GetContainerStateAsync(container.Id, cancellationToken);
        var batchIds = state.Contents.Select(count => count.BatchId).ToList();

        var batches = await db.Batches
            .Where(batch => batchIds.Contains(batch.Id))
            .Join(db.Plants, batch => batch.PlantId, plant => plant.Id, (batch, plant) => new { batch.Id, batch.TraceabilityCode, PlantId = plant.Id, plant.CommonName, plant.Variety })
            .ToListAsync(cancellationToken);

        var contents = state.Contents
            .Join(batches, count => count.BatchId, batch => batch.Id, (count, batch) => new ContainerLine(batch.Id, batch.TraceabilityCode, batch.PlantId, batch.CommonName, batch.Variety, count.Quantity))
            .ToList();

        var locationName = await db.Locations.Where(location => location.Id == state.LocationId).Select(location => location.Name).FirstOrDefaultAsync(cancellationToken);
        var potSizeName = await db.PotSizes.Where(size => size.Id == state.PotSizeId).Select(size => size.Name).FirstOrDefaultAsync(cancellationToken);

        return TypedResults.Ok(new ContainerResponse(container.Id, container.LabelCode, container.Kind, contents, state.LocationId, locationName, state.PotSizeId, potSizeName));
    }

    private static Task<IResult> RecordGerminatedAsync(Guid id, GerminatedRequest request, ClaimsPrincipal user, EventStore store, CancellationToken cancellationToken) =>
        StoreSimpleAsync(store, NewEvent(request.EventId, EventType.Germinated, request.OccurredAt, user, id), cancellationToken);

    private static async Task<IResult> RecordLossAsync(Guid id, LossRequest request, ClaimsPrincipal user, EventStore store, CancellationToken cancellationToken)
    {
        if (!IsValidQuantity(request.Quantity))
        {
            return TypedResults.BadRequest();
        }

        var work = NewEvent(request.EventId, EventType.Lost, request.OccurredAt, user, id, details: new { reason = request.Reason.ToString() });
        AddLine(work, id, request.BatchId, -request.Quantity);
        return await StoreSimpleAsync(store, work, cancellationToken);
    }

    private static Task<IResult> MoveAsync(Guid id, MoveRequest request, ClaimsPrincipal user, EventStore store, CancellationToken cancellationToken) =>
        StoreSimpleAsync(store, NewEvent(request.EventId, EventType.Moved, request.OccurredAt, user, id, request.LocationId), cancellationToken);

    /// <summary>Potting on or pricking out: moves plants into a new container, within the same batch.</summary>
    private static async Task<IResult> TransferAsync(
        Guid id,
        TransferRequest request,
        ClaimsPrincipal user,
        GrowLogDbContext db,
        EventStore store,
        CancellationToken cancellationToken)
    {
        if (!IsValidQuantity(request.Quantity) || request.Type is not (EventType.PottedOn or EventType.PrickedOut))
        {
            return TypedResults.BadRequest();
        }

        var locationId = request.LocationId ?? (await store.GetContainerStateAsync(id, cancellationToken)).LocationId;
        var target = new Container { LabelCode = await NextContainerLabelAsync(db, cancellationToken), Kind = request.ContainerKind };

        var work = NewEvent(request.EventId, request.Type, request.OccurredAt, user, target.Id, locationId, request.PotSizeId);
        AddLine(work, id, request.BatchId, -request.Quantity);
        AddLine(work, target.Id, request.BatchId, request.Quantity);

        db.Add(target);

        return await StoreAsync(
            () => store.AppendAsync(work, cancellationToken),
            async () => await db.Events
                .AsNoTracking()
                .Where(e => e.Id == request.EventId)
                .Join(db.Containers, e => e.ContainerId, container => container.Id, (e, container) => new ContainerCreated(e.Id, container.Id, container.LabelCode))
                .SingleAsync(cancellationToken));
    }

    private static async Task<IResult> CountAsync(Guid id, CountRequest request, ClaimsPrincipal user, EventStore store, CancellationToken cancellationToken)
    {
        if (request.Counted is < 0 or > MaxQuantity)
        {
            return TypedResults.BadRequest();
        }

        var work = NewEvent(request.EventId, EventType.StockCounted, request.OccurredAt, user, id);

        return await StoreAsync(
            () => store.AppendStockCountAsync(work, request.BatchId, request.Counted, cancellationToken),
            () => Task.FromResult<object>(new WorkRecorded(work.Id)));
    }

    private static async Task<IResult> RecordTreatmentAsync(Guid id, TreatmentRequest request, ClaimsPrincipal user, EventStore store, CancellationToken cancellationToken)
    {
        if (Clean(request.Product) is not { } product || Clean(request.Dose) is not { } dose || Clean(request.Area) is not { } area)
        {
            return TypedResults.BadRequest();
        }

        return await StoreSimpleAsync(store, NewEvent(request.EventId, EventType.Treated, request.OccurredAt, user, id, details: new { product, dose, area }), cancellationToken);
    }

    private static async Task<IResult> RecordNoteAsync(Guid id, NoteRequest request, ClaimsPrincipal user, EventStore store, CancellationToken cancellationToken)
    {
        if (Clean(request.Note) is not { } note)
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
        GrowLogDbContext db,
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

        if (original.Type == EventType.Corrected || !quantityIsValid || (request.LocationId is not null && original.LocationId is null))
        {
            return TypedResults.BadRequest();
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
                AddLine(correction, line.ContainerId, line.BatchId, change);
            }
        }

        return await StoreSimpleAsync(store, correction, cancellationToken);
    }

    /// <summary>The signed-in person's latest actions, with what a screen needs to offer them again in one press.</summary>
    private static async Task<List<RecentAction>> RecentAsync(ClaimsPrincipal user, GrowLogDbContext db, int limit = 10, CancellationToken cancellationToken = default)
    {
        var userId = UserId(user);

        var rows = await db.Events
            .AsNoTracking()
            .Where(e => e.RecordedBy == userId && e.Type != EventType.Corrected && !db.Events.Any(c => c.CorrectsEventId == e.Id))
            .OrderByDescending(e => e.RecordedAt)
            .ThenByDescending(e => e.Id)
            .Take(Math.Clamp(limit, 1, MaxRecent))
            .Select(e => new
            {
                e.Id,
                e.Type,
                e.OccurredAt,
                e.ContainerId,
                LabelCode = db.Containers.Where(c => c.Id == e.ContainerId).Select(c => c.LabelCode).FirstOrDefault(),
                e.LocationId,
                LocationName = db.Locations.Where(l => l.Id == e.LocationId).Select(l => l.Name).FirstOrDefault(),
                e.PotSizeId,
                PotSizeName = db.PotSizes.Where(s => s.Id == e.PotSizeId).Select(s => s.Name).FirstOrDefault(),
                e.Payload,
            })
            .ToListAsync(cancellationToken);

        var eventIds = rows.Select(row => row.Id).ToList();

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
                    row.batch.OriginCountry),
            })
            .ToListAsync(cancellationToken);

        return rows
            .ConvertAll(row => new RecentAction(
                row.Id,
                row.Type,
                row.OccurredAt,
                row.ContainerId,
                row.LabelCode,
                row.LocationId,
                row.LocationName,
                row.PotSizeId,
                row.PotSizeName,
                JsonSerializer.Deserialize<JsonElement>(row.Payload),
                lines.Where(line => line.EventId == row.Id).Select(line => line.Line).ToList()));
    }

    internal sealed record WorkRecorded(Guid EventId);

    internal sealed record WorkRefused(string Problem);

    internal sealed record BatchStarted(Guid EventId, Guid BatchId, string TraceabilityCode, Guid ContainerId, string LabelCode);

    internal sealed record ContainerCreated(Guid EventId, Guid ContainerId, string LabelCode);

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
        Guid? PotSizeId);

    internal sealed record GerminatedRequest(Guid EventId, DateTimeOffset OccurredAt);

    internal sealed record LossRequest(Guid EventId, DateTimeOffset OccurredAt, Guid BatchId, int Quantity, LossReason Reason);

    internal sealed record MoveRequest(Guid EventId, DateTimeOffset OccurredAt, Guid LocationId);

    internal sealed record TransferRequest(
        Guid EventId,
        DateTimeOffset OccurredAt,
        EventType Type,
        Guid BatchId,
        int Quantity,
        ContainerKind ContainerKind,
        Guid? PotSizeId,
        Guid? LocationId);

    internal sealed record CountRequest(Guid EventId, DateTimeOffset OccurredAt, Guid BatchId, int Counted);

    internal sealed record TreatmentRequest(Guid EventId, DateTimeOffset OccurredAt, string Product, string Dose, string Area);

    internal sealed record NoteRequest(Guid EventId, DateTimeOffset OccurredAt, string Note);

    internal sealed record CorrectRequest(Guid EventId, DateTimeOffset OccurredAt, int? Quantity, Guid? LocationId);

    internal sealed record ContainerLine(Guid BatchId, string TraceabilityCode, Guid PlantId, string CommonName, string? Variety, int Quantity);

    internal sealed record ContainerResponse(
        Guid Id,
        string LabelCode,
        ContainerKind Kind,
        IReadOnlyList<ContainerLine> Contents,
        Guid? LocationId,
        string? LocationName,
        Guid? PotSizeId,
        string? PotSizeName);

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
        string OriginCountry);

    internal sealed record RecentAction(
        Guid Id,
        EventType Type,
        DateTimeOffset OccurredAt,
        Guid? ContainerId,
        string? LabelCode,
        Guid? LocationId,
        string? LocationName,
        Guid? PotSizeId,
        string? PotSizeName,
        JsonElement Details,
        IReadOnlyList<RecentLine> Lines);
}
