using Hortiplanner.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Operations;

internal static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health/live", () => TypedResults.Ok(new HealthResponse("ok"))).AllowAnonymous();
        app.MapGet("/api/health", ReadyAsync).AllowAnonymous().Produces<HealthResponse>().Produces(StatusCodes.Status503ServiceUnavailable);
    }

    // These probes must answer even when cookie key loading cannot reach the database.
    public static void UseHealthEndpoints(this WebApplication app) => app.Use(async (context, next) =>
    {
        if (HttpMethods.IsGet(context.Request.Method) && context.Request.Path.Value is "/api/health" or "/api/health/live")
        {
            IResult result = context.Request.Path.Value is "/api/health/live"
                ? TypedResults.Ok(new HealthResponse("ok"))
                : await ReadyAsync(context.RequestServices.GetRequiredService<HortiplannerDbContext>(), context.RequestAborted);
            await result.ExecuteAsync(context);
            return;
        }
        await next(context);
    });

    private static async Task<IResult> ReadyAsync(HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        db.Database.SetCommandTimeout(3);
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT 1", timeout.Token);
            return TypedResults.Ok(new HealthResponse("ok"));
        }
        catch (Exception error) when (error is Npgsql.NpgsqlException or OperationCanceledException)
        {
            return TypedResults.Json(new HealthResponse("database unavailable"), statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
