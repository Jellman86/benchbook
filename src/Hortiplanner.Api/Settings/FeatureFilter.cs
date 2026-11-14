using Hortiplanner.Api.Data;

namespace Hortiplanner.Api.Settings;

/// <summary>Refuses a request to a part of Hortiplanner the owner has turned off, as if it weren't there.</summary>
internal static class FeatureFilter
{
    public static TBuilder RequireFeature<TBuilder>(this TBuilder builder, NurseryFeature feature)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var db = context.HttpContext.RequestServices.GetRequiredService<HortiplannerDbContext>();

            return await db.IsOnAsync(feature, context.HttpContext.RequestAborted)
                ? await next(context)
                : TypedResults.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Turned off",
                    detail: "This part of Hortiplanner is turned off. The owner can turn it on under Settings, Features.");
        });
}
