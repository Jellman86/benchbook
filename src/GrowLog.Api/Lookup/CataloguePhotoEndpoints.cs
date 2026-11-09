using System.Security.Claims;
using GrowLog.Api.Accounts;
using GrowLog.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Lookup;

/// <summary>Photos of pot sizes and places, as many as people want to add.</summary>
internal static class CataloguePhotoEndpoints
{
    public static void MapCataloguePhotoEndpoints(this IEndpointRouteBuilder app)
    {
        var photos = app.MapGroup("/api/catalogue-photos");
        photos.MapPost("/{entry}/{id:guid}", AddAsync).Produces<PhotoAdded>(StatusCodes.Status201Created);
        photos.MapGet("/{id:guid}", GetAsync);
        photos.MapDelete("/{id:guid}", RemoveAsync).RequireAuthorization(AccountServices.OwnerPolicy);
    }

    private static async Task<bool> EntryExistsAsync(CataloguePhotoEntry entry, Guid id, GrowLogDbContext db, CancellationToken cancellationToken) =>
        entry == CataloguePhotoEntry.PotSize
            ? await db.PotSizes.AnyAsync(size => size.Id == id, cancellationToken)
            : await db.Locations.AnyAsync(location => location.Id == id, cancellationToken);

    private static async Task<IResult> AddAsync(
        CataloguePhotoEntry entry,
        Guid id,
        HttpRequest request,
        ClaimsPrincipal user,
        GrowLogDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await EntryExistsAsync(entry, id, db, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var (image, refusal) = await ImageContent.ReadAsync(request.Body, cancellationToken);

        if (image is null)
        {
            return TypedResults.StatusCode(refusal);
        }

        var photo = new CatalogueEntryPhoto
        {
            Entry = entry,
            EntryId = id,
            ContentType = image.ContentType,
            Content = image.Content,
            UploadedBy = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
        };

        db.CatalogueEntryPhotos.Add(photo);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/catalogue-photos/{photo.Id}", new PhotoAdded(photo.Id));
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext context, GrowLogDbContext db, CancellationToken cancellationToken) =>
        await db.CatalogueEntryPhotos.AsNoTracking().FirstOrDefaultAsync(photo => photo.Id == id, cancellationToken) is { } photo
            ? SpeciesEndpoints.Picture(context, photo.Content, photo.ContentType, permanent: true)
            : TypedResults.NotFound();

    private static async Task<IResult> RemoveAsync(Guid id, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (await db.CatalogueEntryPhotos.FirstOrDefaultAsync(photo => photo.Id == id, cancellationToken) is not { } photo)
        {
            return TypedResults.NotFound();
        }

        db.CatalogueEntryPhotos.Remove(photo);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    internal sealed record PhotoAdded(Guid Id);
}
