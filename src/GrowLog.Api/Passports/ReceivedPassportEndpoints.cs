using System.Security.Claims;
using GrowLog.Api.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrowLog.Api.Passports;

internal static class ReceivedPassportEndpoints
{
    private static readonly EventType[] ChangesThePlants = [EventType.PottedOn, EventType.PrickedOut, EventType.Split];

    public static void MapReceivedPassportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/batches/{id:guid}/received-passport-draft", GetDraftAsync).Produces<PassportDraft>();

        var passports = app.MapGroup("/api/received-passports");
        passports.MapGet("/", ListAsync);
        passports.MapPost("/", AddAsync).Produces<PassportCreated>(StatusCodes.Status201Created);
        passports.MapPost("/{id:guid}/status", SetStatusAsync);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>What can be filled in from records already held. People check it against the label.</summary>
    private static async Task<IResult> GetDraftAsync(Guid id, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        var draft = await db.Batches
            .AsNoTracking()
            .Where(batch => batch.Id == id)
            .Join(db.Plants, batch => batch.PlantId, plant => plant.Id, (batch, plant) => new PassportDraft(
                batch.SupplierId,
                plant.BotanicalName,
                db.Suppliers.Where(supplier => supplier.Id == batch.SupplierId).Select(supplier => supplier.PassportRegistrationNumber).FirstOrDefault(),
                batch.OriginCountry))
            .FirstOrDefaultAsync(cancellationToken);

        return draft is null ? TypedResults.NotFound() : TypedResults.Ok(draft);
    }

    private static async Task<List<PassportResponse>> ListAsync(Guid batchId, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        var changed = await db.Events
            .Where(e => !db.Events.Any(c => c.CorrectsEventId == e.Id)
                && (ChangesThePlants.Contains(e.Type)
                    || (e.Type == EventType.Corrected && db.Events.Any(o => o.Id == e.CorrectsEventId && ChangesThePlants.Contains(o.Type)))))
            .AnyAsync(e => e.Lines.Any(line => line.BatchId == batchId), cancellationToken);

        return await db.Set<ReceivedPassport>()
            .AsNoTracking()
            .Where(passport => passport.BatchId == batchId)
            .OrderByDescending(passport => passport.ReceivedOn)
            .ThenByDescending(passport => passport.Id)
            .Join(db.Suppliers, passport => passport.SupplierId, supplier => supplier.Id, (passport, supplier) => new PassportResponse(
                passport.Id,
                passport.BatchId,
                passport.SupplierId,
                supplier.Name,
                passport.ReceivedOn,
                passport.PartA,
                passport.PartB,
                passport.PartC,
                passport.PartD,
                passport.PartE,
                passport.Status,
                changed))
            .ToListAsync(cancellationToken);
    }

    private static async Task<IResult> AddAsync(PassportRequest request, ClaimsPrincipal user, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (Clean(request.PartA) is not { } partA
            || Clean(request.PartB) is not { } partB
            || Clean(request.PartD) is not { } partD
            || request.ReceivedOn > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
        {
            return TypedResults.BadRequest();
        }

        var passport = new ReceivedPassport
        {
            BatchId = request.BatchId,
            SupplierId = request.SupplierId,
            ReceivedOn = request.ReceivedOn,
            PartA = partA,
            PartB = partB,
            PartC = Clean(request.PartC),
            PartD = partD,
            PartE = Clean(request.PartE),
            RecordedBy = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
        };

        db.Add(passport);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Created(passport.Id);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            return TypedResults.BadRequest();
        }
    }

    private static Created<PassportCreated> Created(Guid id) =>
        TypedResults.Created($"/api/received-passports/{id}", new PassportCreated(id));

    private static async Task<IResult> SetStatusAsync(Guid id, StatusRequest request, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Set<ReceivedPassport>().FindAsync([id], cancellationToken) is not { } passport)
        {
            return TypedResults.NotFound();
        }

        passport.Status = request.Status;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    internal sealed record PassportDraft(Guid? SupplierId, string? PartA, string? PartB, string PartD);

    internal sealed record PassportRequest(
        Guid BatchId,
        Guid SupplierId,
        DateOnly ReceivedOn,
        string PartA,
        string PartB,
        string? PartC,
        string PartD,
        string? PartE);

    internal sealed record PassportCreated(Guid Id);

    internal sealed record StatusRequest(ReceivedPassportStatus Status);

    internal sealed record PassportResponse(
        Guid Id,
        Guid BatchId,
        Guid SupplierId,
        string SupplierName,
        DateOnly ReceivedOn,
        string PartA,
        string PartB,
        string? PartC,
        string PartD,
        string? PartE,
        ReceivedPassportStatus Status,
        bool PlantsChangedSince);
}
