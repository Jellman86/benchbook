using GrowLog.Api.Accounts;
using GrowLog.Api.Data;
using GrowLog.Api.Settings;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Catalogue;

/// <summary>
/// The nursery's own picture to draw places on, such as an aerial photo, a drone shot or a plan, for a nursery
/// without a satellite map key. The owner uploads it and chooses whether places are drawn on it or on the map.
/// </summary>
internal static class MapPictureEndpoints
{
    // The app shrinks a picture before sending it; this is a backstop.
    private const int LargestPicture = 12 * 1024 * 1024;

    public static void MapMapPictureEndpoints(this IEndpointRouteBuilder app)
    {
        var picture = app.MapGroup("/api/settings/map")
            .RequireAuthorization(AccountServices.OwnerPolicy)
            .RequireFeature(NurseryFeature.PlacesMap);
        picture.MapGet("/picture", GetAsync);
        picture.MapPut("/picture", SaveAsync);
        picture.MapDelete("/picture", RemoveAsync);
        picture.MapPut("/use-picture", UseAsync);
    }

    private static async Task<IResult> GetAsync(GrowLogDbContext db, CancellationToken cancellationToken) =>
        await db.MapPictures.AsNoTracking().FirstOrDefaultAsync(cancellationToken) is { } picture
            ? TypedResults.File(picture.Content, picture.ContentType)
            : TypedResults.NotFound();

    /// <summary>Takes a PNG, JPEG or WebP, recognised by its contents, and starts drawing on it.</summary>
    private static async Task<IResult> SaveAsync(HttpRequest request, GrowLogDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);

        if (buffer.Length is 0 or > LargestPicture || TypeOf(buffer.ToArray()) is not { } type)
        {
            return TypedResults.BadRequest();
        }

        var picture = await db.MapPictures.FirstOrDefaultAsync(cancellationToken);

        if (picture is null)
        {
            db.MapPictures.Add(new MapPicture { Content = buffer.ToArray(), ContentType = type, UpdatedAt = clock.GetUtcNow() });
        }
        else
        {
            picture.Content = buffer.ToArray();
            picture.ContentType = type;
            picture.UpdatedAt = clock.GetUtcNow();
        }

        (await SettingsAsync(db, cancellationToken)).MapUsesPicture = true;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>Removes the picture and goes back to the map. Outlines drawn on the picture are kept, unseen.</summary>
    private static async Task<IResult> RemoveAsync(GrowLogDbContext db, CancellationToken cancellationToken)
    {
        await db.MapPictures.ExecuteDeleteAsync(cancellationToken);
        (await SettingsAsync(db, cancellationToken)).MapUsesPicture = false;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> UseAsync(UsePictureRequest request, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (request.Use && !await db.MapPictures.AnyAsync(cancellationToken))
        {
            return TypedResults.Conflict();
        }

        (await SettingsAsync(db, cancellationToken)).MapUsesPicture = request.Use;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<NurserySettings> SettingsAsync(GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (await db.NurserySettings.FirstOrDefaultAsync(cancellationToken) is { } settings)
        {
            return settings;
        }

        settings = new NurserySettings();
        db.NurserySettings.Add(settings);
        return settings;
    }

    private static string? TypeOf(byte[] content) =>
        content.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47 }) ? "image/png"
        : content.AsSpan().StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }) ? "image/jpeg"
        : content.Length > 12 && content.AsSpan(0, 4).SequenceEqual("RIFF"u8) && content.AsSpan(8, 4).SequenceEqual("WEBP"u8) ? "image/webp"
        : null;

    /// <summary>Whether places are drawn on the nursery's picture rather than the map.</summary>
    internal sealed record UsePictureRequest(bool Use);
}
