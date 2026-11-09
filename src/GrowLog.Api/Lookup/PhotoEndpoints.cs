using System.Security.Claims;
using GrowLog.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Lookup;

/// <summary>Photos of packets and labels on a batch, and the one picture kept for each plant.</summary>
internal static class PhotoEndpoints
{
    public static void MapPhotoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/batches/{id:guid}/photos", AddBatchPhotoAsync).Produces<BatchPhotoSummary>(StatusCodes.Status201Created);
        app.MapGet("/api/batches/{id:guid}/photos", ListBatchPhotosAsync);
        app.MapGet("/api/batch-photos/{id:guid}", GetBatchPhotoAsync);

        app.MapPut("/api/plants/{id:guid}/photo", SetPlantPhotoAsync);
        app.MapGet("/api/plants/{id:guid}/photo", GetPlantPhotoAsync);
    }

    private static async Task<IResult> AddBatchPhotoAsync(
        Guid id,
        BatchPhotoKind kind,
        HttpRequest request,
        ClaimsPrincipal user,
        GrowLogDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await db.Batches.AnyAsync(batch => batch.Id == id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var (image, refusal) = await ImageContent.ReadAsync(request.Body, cancellationToken);

        if (image is null)
        {
            return TypedResults.StatusCode(refusal);
        }

        var photo = new BatchPhoto
        {
            BatchId = id,
            Kind = kind,
            ContentType = image.ContentType,
            Content = image.Content,
            UploadedBy = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
        };

        db.BatchPhotos.Add(photo);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/batch-photos/{photo.Id}", new BatchPhotoSummary(photo.Id, photo.Kind));
    }

    private static async Task<List<BatchPhotoSummary>> ListBatchPhotosAsync(Guid id, GrowLogDbContext db, CancellationToken cancellationToken) =>
        await db.BatchPhotos
            .Where(photo => photo.BatchId == id)
            .OrderBy(photo => photo.Id)
            .Select(photo => new BatchPhotoSummary(photo.Id, photo.Kind))
            .ToListAsync(cancellationToken);

    private static async Task<IResult> GetBatchPhotoAsync(Guid id, HttpContext context, GrowLogDbContext db, CancellationToken cancellationToken) =>
        await db.BatchPhotos.AsNoTracking().FirstOrDefaultAsync(photo => photo.Id == id, cancellationToken) is { } photo
            ? SpeciesEndpoints.Picture(context, photo.Content, photo.ContentType, permanent: true)
            : TypedResults.NotFound();

    private static async Task<IResult> SetPlantPhotoAsync(Guid id, HttpRequest request, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Plants.AnyAsync(plant => plant.Id == id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var (image, refusal) = await ImageContent.ReadAsync(request.Body, cancellationToken);

        if (image is null)
        {
            return TypedResults.StatusCode(refusal);
        }

        if (await db.PlantPhotos.FindAsync([id], cancellationToken) is { } photo)
        {
            photo.ContentType = image.ContentType;
            photo.Content = image.Content;
        }
        else
        {
            db.PlantPhotos.Add(new PlantPhoto { PlantId = id, ContentType = image.ContentType, Content = image.Content });
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetPlantPhotoAsync(Guid id, HttpContext context, GrowLogDbContext db, SpeciesSources sources, CancellationToken cancellationToken)
    {
        if (await db.PlantPhotos.AsNoTracking().FirstOrDefaultAsync(photo => photo.PlantId == id, cancellationToken) is { } own)
        {
            return SpeciesEndpoints.Picture(context, own.Content, own.ContentType, permanent: false);
        }

        var speciesId = await db.Plants.Where(plant => plant.Id == id).Select(plant => plant.SpeciesId).FirstOrDefaultAsync(cancellationToken);

        return speciesId is { } species && await SpeciesEndpoints.FindOrFetchPhotoAsync(species, db, sources, cancellationToken) is { } photo
            ? SpeciesEndpoints.Picture(context, photo.Content, photo.ContentType, permanent: false)
            : TypedResults.NotFound();
    }

    internal sealed record BatchPhotoSummary(Guid Id, BatchPhotoKind Kind);
}
