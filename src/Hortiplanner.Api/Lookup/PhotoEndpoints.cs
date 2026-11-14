using System.Security.Claims;
using Hortiplanner.Api.Accounts;
using Hortiplanner.Api.Data;
using Hortiplanner.Api.Production;
using Hortiplanner.Api.Settings;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Lookup;

/// <summary>Photos of packets, labels and the plants on a batch, and the one picture kept for each plant.</summary>
internal static class PhotoEndpoints
{
    public static void MapPhotoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/batches/{id:guid}/photos", AddBatchPhotoAsync).Produces<BatchPhotoSummary>(StatusCodes.Status201Created).RequireFeature(NurseryFeature.BatchPhotos);
        app.MapGet("/api/batches/{id:guid}/photos", ListBatchPhotosAsync).RequireFeature(NurseryFeature.BatchPhotos);
        app.MapGet("/api/batch-photos/{id:guid}", GetBatchPhotoAsync).RequireFeature(NurseryFeature.BatchPhotos);
        app.MapDelete("/api/batch-photos/{id:guid}", RemoveBatchPhotoAsync).RequireFeature(NurseryFeature.BatchPhotos);

        app.MapPut("/api/plants/{id:guid}/photo", SetPlantPhotoAsync);
        app.MapGet("/api/plants/{id:guid}/photo", GetPlantPhotoAsync);
    }

    private static async Task<IResult> AddBatchPhotoAsync(
        Guid id,
        BatchPhotoKind kind,
        Guid? containerId,
        HttpRequest request,
        ClaimsPrincipal user,
        HortiplannerDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await db.Batches.AnyAsync(batch => batch.Id == id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        if (containerId is { } tray && !await db.Containers.AnyAsync(container => container.Id == tray, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var (image, refusal) = await ImageContent.ReadAsync(request.Body, cancellationToken);

        if (image is null)
        {
            return TypedResults.StatusCode(refusal);
        }

        var stages = containerId is { } at ? await ProductionEndpoints.FindStagesAsync([at], db, cancellationToken) : [];
        var photo = new BatchPhoto
        {
            BatchId = id,
            Kind = kind,
            ContentType = image.ContentType,
            Content = image.Content,
            UploadedBy = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
            ContainerId = containerId,
            Stage = containerId is { } taken && stages.TryGetValue(taken, out var reached) ? reached.Stage : null,
        };

        db.BatchPhotos.Add(photo);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/batch-photos/{photo.Id}", new BatchPhotoSummary(photo.Id, photo.Kind, DateTimeOffset.UtcNow, null, photo.Stage, CanRemove: true));
    }

    /// <summary>
    /// A batch's photos, oldest first, each with the day of the batch it was taken on (the day it was sown or
    /// arrived is day 1) and the stage its tray had reached.
    /// </summary>
    private static async Task<List<BatchPhotoSummary>> ListBatchPhotosAsync(Guid id, ClaimsPrincipal user, HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var started = await StartedAsync(id, db, cancellationToken);
        var photos = await db.BatchPhotos
            .Where(photo => photo.BatchId == id)
            .OrderBy(photo => photo.CreatedAt)
            .ThenBy(photo => photo.Id)
            .Select(photo => new { photo.Id, photo.Kind, photo.CreatedAt, photo.Stage, photo.UploadedBy })
            .ToListAsync(cancellationToken);

        return photos.ConvertAll(photo => new BatchPhotoSummary(
            photo.Id,
            photo.Kind,
            photo.CreatedAt,
            started is { } start ? Math.Max(1, DayOf(photo.CreatedAt, clock) - DayOf(start, clock) + 1) : null,
            photo.Stage,
            MayRemove(user, photo.UploadedBy)));
    }

    private static int DayOf(DateTimeOffset moment, TimeProvider clock) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, clock.LocalTimeZone).DateTime).DayNumber;

    /// <summary>When the batch was sown, rooted or arrived: its earliest work that still stands.</summary>
    private static async Task<DateTimeOffset?> StartedAsync(Guid id, HortiplannerDbContext db, CancellationToken cancellationToken) =>
        await db.Events
            .Where(e => e.CorrectsEventId == null
                && e.Lines.Any(line => line.BatchId == id)
                && !db.Events.Any(correction => correction.CorrectsEventId == e.Id))
            .MinAsync(e => (DateTimeOffset?)e.OccurredAt, cancellationToken);

    /// <summary>A wrong photo can be removed by whoever took it, or by the owner signed in as themselves.</summary>
    private static bool MayRemove(ClaimsPrincipal user, Guid uploadedBy) =>
        (user.IsInRole(AccountServices.OwnerRole) && user.FindFirst(AccountServices.BenchSessionClaim) is null)
        || string.Equals(user.FindFirstValue(ClaimTypes.NameIdentifier), uploadedBy.ToString(), StringComparison.OrdinalIgnoreCase);

    private static async Task<IResult> RemoveBatchPhotoAsync(Guid id, ClaimsPrincipal user, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        if (await db.BatchPhotos.FirstOrDefaultAsync(photo => photo.Id == id, cancellationToken) is not { } photo)
        {
            return TypedResults.NotFound();
        }

        if (!MayRemove(user, photo.UploadedBy))
        {
            return TypedResults.Forbid();
        }

        db.BatchPhotos.Remove(photo);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetBatchPhotoAsync(Guid id, HttpContext context, HortiplannerDbContext db, CancellationToken cancellationToken) =>
        await db.BatchPhotos.AsNoTracking().FirstOrDefaultAsync(photo => photo.Id == id, cancellationToken) is { } photo
            ? SpeciesEndpoints.Picture(context, photo.Content, photo.ContentType, permanent: true)
            : TypedResults.NotFound();

    private static async Task<IResult> SetPlantPhotoAsync(Guid id, HttpRequest request, HortiplannerDbContext db, CancellationToken cancellationToken)
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

    private static async Task<IResult> GetPlantPhotoAsync(Guid id, HttpContext context, HortiplannerDbContext db, SpeciesSources sources, CancellationToken cancellationToken)
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

    internal sealed record BatchPhotoSummary(Guid Id, BatchPhotoKind Kind, DateTimeOffset TakenAt, int? Day, ProductionStage? Stage, bool CanRemove);
}
