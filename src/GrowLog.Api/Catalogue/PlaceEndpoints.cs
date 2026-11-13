using GrowLog.Api.Accounts;
using GrowLog.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrowLog.Api.Catalogue;

/// <summary>Arranging places: moving one into another, and adding a row of them at once.</summary>
internal static class PlaceEndpoints
{
    private const int MostAtOnce = 50;

    public static void MapPlaceEndpoints(this IEndpointRouteBuilder app)
    {
        var places = app.MapGroup("/api/locations").RequireAuthorization(AccountServices.OwnerPolicy);
        places.MapPut("/{id:guid}/parent", MoveAsync);
        places.MapPost("/several", AddSeveralAsync).Produces<PlacesCreated>(StatusCodes.Status201Created);
    }

    /// <summary>Puts a place inside another, as long as that wouldn't put it inside itself.</summary>
    private static async Task<IResult> MoveAsync(Guid id, MovePlaceRequest request, GrowLogDbContext db, CancellationToken cancellationToken)
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

    /// <summary>Adds numbered places in one go, such as Bench 1 to Bench 12. All are added, or none.</summary>
    private static async Task<IResult> AddSeveralAsync(AddPlacesRequest request, GrowLogDbContext db, CancellationToken cancellationToken)
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

    private static Task<bool> IsUsableParentAsync(Guid parentId, GrowLogDbContext db, CancellationToken cancellationToken) =>
        db.Locations.AnyAsync(parent => parent.Id == parentId && parent.ArchivedAt == null, cancellationToken);

    /// <summary>Whether a place is the other place or sits somewhere inside it.</summary>
    private static async Task<bool> IsInsideAsync(Guid placeId, Guid containerId, GrowLogDbContext db, CancellationToken cancellationToken)
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

    internal sealed record MovePlaceRequest(Guid ParentId);

    internal sealed record AddPlacesRequest(LocationKind Kind, Guid ParentId, string Prefix, int From, int To);

    internal sealed record PlacesCreated(IReadOnlyList<Guid> Ids);
}
