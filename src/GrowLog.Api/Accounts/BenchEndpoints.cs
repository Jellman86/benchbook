using System.Security.Claims;
using GrowLog.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Accounts;

internal static class BenchEndpoints
{
    private const string StationClaim = "bench_station";

    private static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(5);

    public static void MapBenchEndpoints(this IEndpointRouteBuilder app)
    {
        var stations = app.MapGroup("/api/bench/stations").RequireAuthorization(AccountServices.OwnerPolicy);
        stations.MapGet("/", ListStationsAsync);
        stations.MapPost("/", RegisterStationAsync);
        stations.MapPost("/{id:guid}/revoke", RevokeStationAsync);

        var bench = app.MapGroup("/api/bench").RequireAuthorization(AccountServices.BenchPolicy);
        bench.MapGet("/people", ListPeopleAsync).Produces<List<Person>>();
        bench.MapPost("/sign-in", SignInAsync);
    }

    private static async Task<List<StationSummary>> ListStationsAsync(GrowLogDbContext db, CancellationToken cancellationToken) =>
        await db.BenchStations
            .OrderBy(station => station.CreatedAt)
            .Select(station => new StationSummary(station.Id, station.Name, station.RevokedAt != null))
            .ToListAsync(cancellationToken);

    /// <summary>Makes this device a bench station and ends the owner's own session on it.</summary>
    private static async Task<IResult> RegisterStationAsync(
        RegisterStationRequest request,
        HttpContext context,
        GrowLogDbContext db,
        SignInManager<AppUser> signIn,
        CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();

        if (name.Length == 0)
        {
            return TypedResults.BadRequest();
        }

        var station = new BenchStation { Name = name };
        db.BenchStations.Add(station);
        await db.SaveChangesAsync(cancellationToken);

        await signIn.SignOutAsync();

        var identity = new ClaimsIdentity([new Claim(StationClaim, station.Id.ToString())], AccountServices.BenchScheme);
        await context.SignInAsync(
            AccountServices.BenchScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });

        return TypedResults.NoContent();
    }

    private static async Task<IResult> RevokeStationAsync(Guid id, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (await db.BenchStations.FindAsync([id], cancellationToken) is not { } station)
        {
            return TypedResults.NotFound();
        }

        station.RevokedAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<bool> StationIsActiveAsync(ClaimsPrincipal station, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        var id = Guid.Parse(station.FindFirstValue(StationClaim)!);
        return await db.BenchStations.AnyAsync(s => s.Id == id && s.RevokedAt == null, cancellationToken);
    }

    private static async Task<IResult> ListPeopleAsync(
        ClaimsPrincipal station,
        GrowLogDbContext db,
        UserManager<AppUser> users,
        CancellationToken cancellationToken)
    {
        if (!await StationIsActiveAsync(station, db, cancellationToken))
        {
            return TypedResults.Unauthorized();
        }

        var people = await users.Users
            .Where(user => user.ArchivedAt == null && user.PinHash != null)
            .OrderBy(user => user.UserName)
            .Select(user => new Person(user.Id, user.UserName!))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(people);
    }

    private static async Task<IResult> SignInAsync(
        BenchSignInRequest request,
        ClaimsPrincipal station,
        GrowLogDbContext db,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        IPasswordHasher<AppUser> hasher,
        CancellationToken cancellationToken)
    {
        if (!await StationIsActiveAsync(station, db, cancellationToken))
        {
            return TypedResults.Unauthorized();
        }

        if (await users.FindByIdAsync(request.UserId.ToString()) is not { PinHash: { } pinHash } person)
        {
            return TypedResults.Unauthorized();
        }

        if (await users.IsLockedOutAsync(person))
        {
            return TypedResults.StatusCode(StatusCodes.Status423Locked);
        }

        if (hasher.VerifyHashedPassword(person, pinHash, request.Pin) == PasswordVerificationResult.Failed)
        {
            await users.AccessFailedAsync(person);

            return await users.IsLockedOutAsync(person)
                ? TypedResults.StatusCode(StatusCodes.Status423Locked)
                : TypedResults.Unauthorized();
        }

        await users.ResetAccessFailedCountAsync(person);

        // Not persistent, and short: sliding expiry returns the station to the name list when idle.
        await signIn.SignInAsync(person, new AuthenticationProperties
        {
            IsPersistent = false,
            ExpiresUtc = DateTimeOffset.UtcNow + IdleLimit,
        });

        return TypedResults.NoContent();
    }

    internal sealed record StationSummary(Guid Id, string Name, bool Revoked);

    internal sealed record RegisterStationRequest(string Name);

    internal sealed record Person(Guid Id, string Name);

    internal sealed record BenchSignInRequest(Guid UserId, string Pin);
}
