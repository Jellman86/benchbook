using GrowLog.Api.Accounts;
using GrowLog.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Settings;

/// <summary>Which optional parts of GrowLog are on. Everyone signed in can see this; only the owner changes it.</summary>
internal static class FeatureEndpoints
{
    public static void MapFeatureEndpoints(this IEndpointRouteBuilder app)
    {
        var features = app.MapGroup("/api/settings/features");
        features.MapGet("/", ListAsync);
        features.MapPut("/{feature}", SetAsync).RequireAuthorization(AccountServices.OwnerPolicy);
    }

    private static async Task<List<FeatureState>> ListAsync(GrowLogDbContext db, CancellationToken cancellationToken)
    {
        var off = (await db.GetNurserySettingsAsync(cancellationToken)).FeaturesOff;
        return [.. Enum.GetValues<NurseryFeature>().Select(feature => new FeatureState(feature, !off.Contains(feature)))];
    }

    private static async Task<IResult> SetAsync(NurseryFeature feature, FeatureChange change, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        var settings = await db.NurserySettings.FirstOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            settings = new NurserySettings();
            db.NurserySettings.Add(settings);
        }

        var off = settings.FeaturesOff.Where(item => item != feature).ToList();

        if (!change.On)
        {
            off.Add(feature);
        }

        settings.FeaturesOff = [.. off.Order()];
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    internal sealed record FeatureState(NurseryFeature Feature, bool On);

    internal sealed record FeatureChange(bool On);
}
