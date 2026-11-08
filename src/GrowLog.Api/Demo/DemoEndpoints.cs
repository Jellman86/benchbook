using System.Security.Claims;
using GrowLog.Api.Accounts;
using GrowLog.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Demo;

/// <summary>Example data for trying GrowLog out. Off unless Demo:Enabled is set, and only into an empty system.</summary>
internal static class DemoEndpoints
{
    public static void MapDemoEndpoints(this IEndpointRouteBuilder app)
    {
        var demo = app.MapGroup("/api/demo").RequireAuthorization(AccountServices.OwnerPolicy);
        demo.MapGet("/", GetStatusAsync);
        demo.MapPost("/load", LoadAsync);
    }

    private static bool IsEnabled(IConfiguration configuration) => configuration.GetValue<bool>("Demo:Enabled");

    private static async Task<DemoStatus> GetStatusAsync(IConfiguration configuration, GrowLogDbContext db, CancellationToken cancellationToken) =>
        new(IsEnabled(configuration) && !await db.Batches.AnyAsync(cancellationToken));

    private static async Task<IResult> LoadAsync(
        ClaimsPrincipal user,
        IConfiguration configuration,
        GrowLogDbContext db,
        EventStore store,
        UserManager<AppUser> users,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled(configuration))
        {
            return TypedResults.NotFound();
        }

        if (await db.Batches.AnyAsync(cancellationToken) || await db.Plants.AnyAsync(cancellationToken))
        {
            return TypedResults.Conflict();
        }

        await new ExampleData(db, store).LoadAsync(Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!), cancellationToken);

        // No PIN or passkey, so nobody can sign in as these.
        foreach (var name in new[] { "Example Worker", "Sample Helper" })
        {
            await users.CreateAsync(new AppUser { Id = Guid.CreateVersion7(), UserName = name });
        }

        return TypedResults.NoContent();
    }

    internal sealed record DemoStatus(bool Available);
}
