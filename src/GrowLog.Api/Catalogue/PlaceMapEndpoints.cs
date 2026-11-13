using System.Text.Json;
using GrowLog.Api.Accounts;
using GrowLog.Api.Data;
using GrowLog.Api.Settings;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Catalogue;

/// <summary>
/// Places drawn on a map. Each place can have an outline, traced over satellite pictures where the owner has given
/// GrowLog an imagery key, or over a street map otherwise. The map is optional: nothing else depends on it.
/// </summary>
internal static class PlaceMapEndpoints
{
    private const int FewestPoints = 3;
    private const int MostPoints = 500;
    private const int LongestKey = 500;

    public static void MapPlaceMapEndpoints(this IEndpointRouteBuilder app)
    {
        var map = app.MapGroup("/api/locations").RequireAuthorization(AccountServices.OwnerPolicy).RequireFeature(NurseryFeature.PlacesMap);
        map.MapGet("/map", GetMapAsync);
        map.MapPut("/{id:guid}/outline", SaveOutlineAsync);
        app.MapPut("/api/settings/map", SaveSettingsAsync).RequireAuthorization(AccountServices.OwnerPolicy).RequireFeature(NurseryFeature.PlacesMap);
    }

    private static async Task<PlaceMap> GetMapAsync(GrowLogDbContext db, CancellationToken cancellationToken)
    {
        var settings = await db.GetNurserySettingsAsync(cancellationToken);
        var picture = await db.MapPictures.AsNoTracking()
            .Select(item => new { item.UpdatedAt })
            .FirstOrDefaultAsync(cancellationToken);
        var onPicture = settings.MapUsesPicture && picture is not null;

        // Each map keeps its own outlines: the picture's are in fractions of the picture, the earth's in degrees.
        var outlines = await db.Locations.AsNoTracking()
            .Where(place => place.ArchivedAt == null)
            .Select(place => new { place.Id, Outline = onPicture ? place.PictureOutline : place.Outline })
            .Where(place => place.Outline != null)
            .ToListAsync(cancellationToken);

        return new PlaceMap(
            settings.MapImageryKey,
            outlines.ConvertAll(place => new PlaceOutline(place.Id, JsonSerializer.Deserialize<double[][]>(place.Outline!)!)),
            onPicture,
            picture?.UpdatedAt);
    }

    private static async Task<IResult> SaveOutlineAsync(Guid id, OutlineRequest request, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Locations.FindAsync([id], cancellationToken) is not { ArchivedAt: null } place)
        {
            return TypedResults.NotFound();
        }

        var points = request.Points is null ? null : Tidy(request.Points, request.OnPicture);

        if (request.Points is not null && points is null)
        {
            return TypedResults.BadRequest();
        }

        var outline = points is null ? null : JsonSerializer.Serialize(points);

        if (request.OnPicture)
        {
            place.PictureOutline = outline;
        }
        else
        {
            place.Outline = outline;
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Points as [latitude, longitude] rounded to about a centimetre, or on a picture as fractions of its height and
    /// width, a little beyond its edges allowed; null if they can't make a shape.
    /// </summary>
    internal static double[][]? Tidy(IReadOnlyList<double[]> points, bool onPicture = false)
    {
        var (most, mostAcross, least) = onPicture ? (1.1, 1.1, -0.1) : (90.0, 180.0, -180.0);

        if (points.Count is < FewestPoints or > MostPoints
            || points.Any(point => point is not [var down, var across]
                || !double.IsFinite(down) || !double.IsFinite(across)
                || down > most || down < Math.Max(least, -most)
                || across > mostAcross || across < least))
        {
            return null;
        }

        var tidied = points.Select(point => new[] { Math.Round(point[0], 7), Math.Round(point[1], 7) }).ToArray();
        return tidied.DistinctBy(point => (point[0], point[1])).Count() < FewestPoints ? null : tidied;
    }

    private static async Task<IResult> SaveSettingsAsync(MapSettingsRequest request, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        var key = request.ImageryKey?.Trim();

        if (key is { Length: > LongestKey } || key?.Any(char.IsWhiteSpace) == true)
        {
            return TypedResults.BadRequest();
        }

        var settings = await db.NurserySettings.FirstOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            settings = new NurserySettings();
            db.NurserySettings.Add(settings);
        }

        settings.MapImageryKey = string.IsNullOrEmpty(key) ? null : key;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// The imagery key, which the browser needs to fetch pictures, and every place drawn so far on the map in use:
    /// the nursery's own picture when <paramref name="OnPicture"/>, changed at <paramref name="PictureChangedAt"/>.
    /// </summary>
    internal sealed record PlaceMap(string? ImageryKey, IReadOnlyList<PlaceOutline> Outlines, bool OnPicture, DateTimeOffset? PictureChangedAt);

    /// <summary>A place's outline as [latitude, longitude] points.</summary>
    internal sealed record PlaceOutline(Guid Id, double[][] Points);

    /// <summary>The outline to keep, or null to take the place off the map; on the nursery's picture or the earth.</summary>
    internal sealed record OutlineRequest(double[][]? Points, bool OnPicture = false);

    /// <summary>An ArcGIS Location Platform key, or empty to use the street map.</summary>
    internal sealed record MapSettingsRequest(string? ImageryKey);
}
