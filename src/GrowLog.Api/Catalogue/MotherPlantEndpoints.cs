using GrowLog.Api.Accounts;
using GrowLog.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrowLog.Api.Catalogue;

/// <summary>
/// Mother plants: plants kept for taking cuttings from. Anyone taking cuttings can add one; the owner tidies them.
/// Each lists every batch of cuttings taken from it, in order.
/// </summary>
internal static class MotherPlantEndpoints
{
    private const int LongestName = 100;
    private const int LongestNotes = 500;

    public static void MapMotherPlantEndpoints(this IEndpointRouteBuilder app)
    {
        var mothers = app.MapGroup("/api/mother-plants");
        mothers.MapGet("/", ListAsync);
        mothers.MapGet("/{id:guid}", GetAsync).Produces<MotherDetails>();
        mothers.MapPost("/", AddAsync).Produces<MotherAdded>(StatusCodes.Status201Created);
        mothers.MapPut("/{id:guid}", ChangeAsync).RequireAuthorization(AccountServices.OwnerPolicy);
        mothers.MapPost("/{id:guid}/archive", ArchiveAsync).RequireAuthorization(AccountServices.OwnerPolicy);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Named(string commonName, string? variety) => variety is null ? commonName : $"{commonName} '{variety}'";

    private static async Task<List<MotherSummary>> ListAsync(GrowLogDbContext db, Guid? plantId = null, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var paths = await PlacePaths.LoadAsync(db, cancellationToken);

        var rows = await db.MotherPlants
            .AsNoTracking()
            .Where(mother => (plantId == null || mother.PlantId == plantId) && (includeArchived || mother.ArchivedAt == null))
            .Join(db.Plants, mother => mother.PlantId, plant => plant.Id, (mother, plant) => new
            {
                Mother = mother,
                plant.CommonName,
                plant.Variety,
                Taken = db.Batches.Count(batch => batch.MotherPlantId == mother.Id),
                LastTaken = db.Batches.Where(batch => batch.MotherPlantId == mother.Id).Max(batch => (DateTimeOffset?)batch.CreatedAt),
            })
            .OrderBy(row => row.CommonName)
            .ThenBy(row => row.Mother.Name)
            .ToListAsync(cancellationToken);

        return rows.ConvertAll(row => new MotherSummary(
            row.Mother.Id,
            row.Mother.PlantId,
            Named(row.CommonName, row.Variety),
            row.Mother.Name,
            row.Mother.LocationId,
            row.Mother.LocationId is { } place ? paths.GetValueOrDefault(place) : null,
            row.Mother.Notes,
            row.Taken,
            row.LastTaken,
            row.Mother.ArchivedAt != null));
    }

    /// <summary>A mother plant with every batch of cuttings taken from it, first taken first.</summary>
    private static async Task<IResult> GetAsync(Guid id, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (await db.MotherPlants.AsNoTracking().FirstOrDefaultAsync(mother => mother.Id == id, cancellationToken) is not { } mother)
        {
            return TypedResults.NotFound();
        }

        var plant = await db.Plants.AsNoTracking().FirstAsync(item => item.Id == mother.PlantId, cancellationToken);
        var batches = await db.Batches
            .AsNoTracking()
            .Where(batch => batch.MotherPlantId == id)
            .OrderBy(batch => batch.CuttingsRound)
            .Select(batch => new MotherBatch(
                batch.Id,
                batch.TraceabilityCode,
                batch.CuttingsRound ?? 0,
                db.Events
                    .Where(e => e.Lines.Any(line => line.BatchId == batch.Id) && !db.Events.Any(correction => correction.CorrectsEventId == e.Id))
                    .Min(e => (DateTimeOffset?)e.OccurredAt) ?? batch.CreatedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new MotherDetails(mother.Id, mother.PlantId, Named(plant.CommonName, plant.Variety), mother.Name, mother.Notes, batches));
    }

    private static async Task<bool> IsSensibleAsync(MotherRequest request, GrowLogDbContext db, CancellationToken cancellationToken) =>
        Clean(request.Name) is { Length: <= LongestName }
        && (request.Notes?.Length ?? 0) <= LongestNotes
        && await db.Plants.AnyAsync(plant => plant.Id == request.PlantId, cancellationToken)
        && (request.LocationId is not { } place || await db.Locations.AnyAsync(location => location.Id == place && location.ArchivedAt == null, cancellationToken));

    private static async Task<IResult> AddAsync(MotherRequest request, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (!await IsSensibleAsync(request, db, cancellationToken))
        {
            return TypedResults.BadRequest();
        }

        var mother = new MotherPlant { PlantId = request.PlantId, Name = Clean(request.Name)!, LocationId = request.LocationId, Notes = Clean(request.Notes) };
        db.MotherPlants.Add(mother);
        return await SaveAsync(db, TypedResults.Created($"/api/mother-plants/{mother.Id}", new MotherAdded(mother.Id)), cancellationToken);
    }

    private static async Task<IResult> ChangeAsync(Guid id, MotherRequest request, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (!await IsSensibleAsync(request, db, cancellationToken))
        {
            return TypedResults.BadRequest();
        }

        if (await db.MotherPlants.FindAsync([id], cancellationToken) is not { } mother)
        {
            return TypedResults.NotFound();
        }

        // Its cuttings stay its cuttings, so a mother never becomes a different plant.
        if (mother.PlantId != request.PlantId && await db.Batches.AnyAsync(batch => batch.MotherPlantId == id, cancellationToken))
        {
            return TypedResults.Conflict();
        }

        mother.PlantId = request.PlantId;
        mother.Name = Clean(request.Name)!;
        mother.LocationId = request.LocationId;
        mother.Notes = Clean(request.Notes);
        return await SaveAsync(db, TypedResults.NoContent(), cancellationToken);
    }

    private static async Task<IResult> ArchiveAsync(Guid id, GrowLogDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (await db.MotherPlants.FindAsync([id], cancellationToken) is not { } mother)
        {
            return TypedResults.NotFound();
        }

        mother.ArchivedAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>Two mothers of one plant can't share a name, so they can be told apart.</summary>
    private static async Task<IResult> SaveAsync(GrowLogDbContext db, IResult success, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return success;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return TypedResults.Conflict();
        }
    }

    internal sealed record MotherRequest(Guid PlantId, string Name, Guid? LocationId, string? Notes);

    internal sealed record MotherAdded(Guid Id);

    internal sealed record MotherSummary(
        Guid Id,
        Guid PlantId,
        string PlantName,
        string Name,
        Guid? LocationId,
        string? LocationName,
        string? Notes,
        int CuttingsTaken,
        DateTimeOffset? LastTakenAt,
        bool Archived);

    /// <param name="Round">Which batch of cuttings from the mother: 1 for the first.</param>
    internal sealed record MotherBatch(Guid BatchId, string TraceabilityCode, int Round, DateTimeOffset StartedAt);

    internal sealed record MotherDetails(Guid Id, Guid PlantId, string PlantName, string Name, string? Notes, IReadOnlyList<MotherBatch> Batches);
}
