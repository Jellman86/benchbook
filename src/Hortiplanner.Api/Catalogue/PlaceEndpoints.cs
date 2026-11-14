using Hortiplanner.Api.Accounts;
using Hortiplanner.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hortiplanner.Api.Catalogue;

/// <summary>Arranging places: moving one into another, and adding a row of them at once.</summary>
internal static class PlaceEndpoints
{
    private const int MostAtOnce = 50;

    public static void MapPlaceEndpoints(this IEndpointRouteBuilder app)
    {
        var places = app.MapGroup("/api/locations").RequireAuthorization(AccountServices.OwnerPolicy);
        places.MapPut("/{id:guid}/parent", MoveAsync);
        places.MapPost("/several", AddSeveralAsync).Produces<PlacesCreated>(StatusCodes.Status201Created);
        places.MapPut("/{id:guid}/kind", ChangeKindAsync)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces<PlaceInUse>(StatusCodes.Status409Conflict);
        places.MapDelete("/{id:guid}", DeleteAsync)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces<PlaceInUse>(StatusCodes.Status409Conflict);
    }

    /// <summary>Puts a place inside another, as long as that wouldn't put it inside itself.</summary>
    private static async Task<IResult> MoveAsync(Guid id, MovePlaceRequest request, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE locations IN SHARE ROW EXCLUSIVE MODE", cancellationToken);
        if (await db.Locations.FindAsync([id], cancellationToken) is not { } place)
        {
            return TypedResults.NotFound();
        }

        if (place.Kind == LocationKind.Site || request.ParentId == id || !await IsUsableParentAsync(request.ParentId, db, cancellationToken))
        {
            return TypedResults.BadRequest();
        }

        if (await IsInsideAsync(request.ParentId, id, db, cancellationToken))
        {
            return TypedResults.BadRequest();
        }

        place.ParentId = request.ParentId;
        var result = await SaveAsync(db, TypedResults.NoContent(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    /// <summary>
    /// Corrects what sort of place it is, such as a greenhouse added as a bench. Its label number, what's inside it and
    /// its history stay as they are. Only a site stands on its own, so becoming a site lifts it out of its parent, and a
    /// site becoming anything else needs a place to sit in.
    /// </summary>
    private static async Task<IResult> ChangeKindAsync(Guid id, ChangePlaceKindRequest request, HortiplannerDbContext db, EventStore store, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE locations IN SHARE ROW EXCLUSIVE MODE", cancellationToken);

        // The place's own row too: work saved meanwhile shares this lock, so it either finishes first and is seen, or
        // waits and sees the change.
        await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM locations WHERE id = {0} FOR UPDATE", [id], cancellationToken);

        if (await db.Locations.FindAsync([id], cancellationToken) is not { } place)
        {
            return TypedResults.NotFound();
        }

        if (request.Kind == LocationKind.Site)
        {
            // Trays never stand on a site, so they'd be stuck where nothing can move them back.
            if (place.Kind != LocationKind.Site && await CatalogueEndpoints.TraysStandingInAsync(id, store, cancellationToken) > 0)
            {
                return TypedResults.Conflict(new PlaceInUse("HasTrays"));
            }

            place.ParentId = null;
        }
        else if (place.Kind == LocationKind.Site || place.ParentId is null)
        {
            if (request.ParentId is not { } parentId
                || parentId == id
                || !await IsUsableParentAsync(parentId, db, cancellationToken)
                || await IsInsideAsync(parentId, id, db, cancellationToken))
            {
                return TypedResults.BadRequest();
            }

            place.ParentId = parentId;
        }

        place.Kind = request.Kind;
        var result = await SaveAsync(db, TypedResults.NoContent(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    /// <summary>
    /// Removes a place added by mistake. Only one nothing has ever used: no places inside it (even ones no longer used),
    /// no work recorded there, and no sensors, mother plants or jobs. A place that has been used is kept for the
    /// records, and is marked no longer used instead.
    /// </summary>
    private static async Task<IResult> DeleteAsync(Guid id, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        // The same lock as moving and changing kind, so nothing is put inside it while it goes.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE locations IN SHARE ROW EXCLUSIVE MODE", cancellationToken);

        // The place's own row too: work saved meanwhile shares this lock, so it either finishes first and is seen, or
        // waits and sees the change.
        await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM locations WHERE id = {0} FOR UPDATE", [id], cancellationToken);

        if (await db.Locations.FindAsync([id], cancellationToken) is not { } place)
        {
            return TypedResults.NotFound();
        }

        var reason = await db.Locations.AnyAsync(inside => inside.ParentId == id, cancellationToken) ? "HasPlaces"
            : await db.Events.AnyAsync(e => e.LocationId == id, cancellationToken) ? "HasWork"
            : await db.Sensors.AnyAsync(sensor => sensor.LocationId == id, cancellationToken)
                || await db.SensorReadings.AnyAsync(reading => reading.LocationId == id, cancellationToken) ? "HasSensors"
            : await db.MotherPlants.AnyAsync(mother => mother.LocationId == id, cancellationToken)
                || await db.Jobs.AnyAsync(job => job.LocationId == id, cancellationToken) ? "HasRecords"
            : null;

        if (reason is not null)
        {
            return TypedResults.Conflict(new PlaceInUse(reason));
        }

        try
        {
            await db.CatalogueEntryPhotos
                .Where(photo => photo.Entry == CataloguePhotoEntry.Location && photo.EntryId == id)
                .ExecuteDeleteAsync(cancellationToken);
            db.Locations.Remove(place);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return TypedResults.NoContent();
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // Something was recorded there in the meantime: keep it.
            return TypedResults.Conflict(new PlaceInUse("JustUsed"));
        }
    }

    /// <summary>Adds numbered places in one go, such as Bench 1 to Bench 12. All are added, or none.</summary>
    private static async Task<IResult> AddSeveralAsync(AddPlacesRequest request, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        var prefix = request.Prefix.Trim();

        if (prefix.Length == 0
            || request.Kind == LocationKind.Site
            || request.From < 0
            || request.To < request.From
            || request.To - request.From >= MostAtOnce
            || !await IsUsableParentAsync(request.ParentId, db, cancellationToken))
        {
            return TypedResults.BadRequest();
        }

        var added = Enumerable.Range(request.From, request.To - request.From + 1)
            .Select(number => new Location { Name = $"{prefix} {number}", Kind = request.Kind, ParentId = request.ParentId })
            .ToList();

        db.Locations.AddRange(added);
        return await SaveAsync(
            db,
            TypedResults.Created("/api/locations", new PlacesCreated(added.ConvertAll(place => place.Id))),
            cancellationToken);
    }

    private static Task<bool> IsUsableParentAsync(Guid parentId, HortiplannerDbContext db, CancellationToken cancellationToken) =>
        db.Locations.AnyAsync(parent => parent.Id == parentId && parent.ArchivedAt == null, cancellationToken);

    /// <summary>Whether a place is the other place or sits somewhere inside it.</summary>
    private static async Task<bool> IsInsideAsync(Guid placeId, Guid containerId, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        var parents = await db.Locations.AsNoTracking().ToDictionaryAsync(place => place.Id, place => place.ParentId, cancellationToken);
        Guid? current = placeId;

        var seen = new HashSet<Guid>();
        while (current is { } id)
        {
            if (id == containerId || !seen.Add(id) || !parents.ContainsKey(id))
            {
                return true;
            }

            current = parents.GetValueOrDefault(id);
        }

        return false;
    }

    private static async Task<IResult> SaveAsync(HortiplannerDbContext db, IResult success, CancellationToken cancellationToken)
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

    internal sealed record MovePlaceRequest(Guid ParentId);

    internal sealed record AddPlacesRequest(LocationKind Kind, Guid ParentId, string Prefix, int From, int To);

    internal sealed record PlacesCreated(IReadOnlyList<Guid> Ids);

    internal sealed record ChangePlaceKindRequest(LocationKind Kind, Guid? ParentId);

    internal sealed record PlaceInUse(string Problem);
}
