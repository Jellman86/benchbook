using System.Security.Claims;
using Hortiplanner.Api.Accounts;
using Hortiplanner.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Demo;

/// <summary>Example data for trying Hortiplanner out. Off unless Demo:Enabled is set, and only into an empty system.</summary>
internal static class DemoEndpoints
{
    public static void MapDemoEndpoints(this IEndpointRouteBuilder app)
    {
        var demo = app.MapGroup("/api/demo").RequireAuthorization(AccountServices.OwnerPolicy);
        demo.MapGet("/", GetStatusAsync);
        demo.MapPost("/load", LoadAsync);
    }

    private static bool IsEnabled(IConfiguration configuration) => configuration.GetValue<bool>("Demo:Enabled");

    private static async Task<DemoStatus> GetStatusAsync(IConfiguration configuration, HortiplannerDbContext db, CancellationToken cancellationToken) =>
        new(
            IsEnabled(configuration) && !await db.Batches.AnyAsync(cancellationToken) && !await db.Plants.AnyAsync(cancellationToken),
            IsEnabled(configuration));

    private static async Task<IResult> LoadAsync(
        ClaimsPrincipal user,
        IConfiguration configuration,
        HortiplannerDbContext db,
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

        // Marked as having left, so nobody can sign in as these at any sign-in level.
        foreach (var name in new[] { "Example Worker", "Sample Helper" })
        {
            await users.CreateAsync(new AppUser { Id = Guid.CreateVersion7(), UserName = name, ArchivedAt = DateTimeOffset.UtcNow });
        }

        return TypedResults.NoContent();
    }

    /// <summary>Whether example data can be loaded now, and whether the owner may clear the records (only while trying Hortiplanner out).</summary>
    internal sealed record DemoStatus(bool Available, bool CanClear);
}
