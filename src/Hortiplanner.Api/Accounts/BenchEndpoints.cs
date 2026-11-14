using System.Security.Claims;
using Hortiplanner.Api.Data;
using Hortiplanner.Api.Settings;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Accounts;

internal static class BenchEndpoints
{
    private const string StationClaim = "bench_station";

    private static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(5);

    public static void MapBenchEndpoints(this IEndpointRouteBuilder app)
    {
        var stations = app.MapGroup("/api/bench/stations").RequireAuthorization(AccountServices.OwnerPolicy).RequireFeature(NurseryFeature.BenchStations);
        stations.MapGet("/", ListStationsAsync);
        stations.MapPost("/", RegisterStationAsync);
        stations.MapPost("/{id:guid}/revoke", RevokeStationAsync);

        var bench = app.MapGroup("/api/bench").RequireAuthorization(AccountServices.BenchPolicy).RequireFeature(NurseryFeature.BenchStations);
        bench.MapGet("/people", ListPeopleAsync).Produces<List<Person>>();
        bench.MapPost("/sign-in", SignInAsync);
        bench.MapPost("/leave", LeaveAsync);
    }

    private static async Task<List<StationSummary>> ListStationsAsync(HortiplannerDbContext db, CancellationToken cancellationToken) =>
        await db.BenchStations
            .OrderBy(station => station.CreatedAt)
            .Select(station => new StationSummary(station.Id, station.Name, station.RevokedAt != null))
            .ToListAsync(cancellationToken);

    /// <summary>Makes this device a bench station and ends the owner's own session on it.</summary>
    private static async Task<IResult> RegisterStationAsync(
        RegisterStationRequest request,
        HttpContext context,
        HortiplannerDbContext db,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();

        if (name.Length == 0)
        {
            return TypedResults.BadRequest();
        }

        // Without anyone who can sign in there, the device would be stuck on an empty list.
        if (SignInRules.BenchNeedsPin((await db.GetNurserySettingsAsync(cancellationToken)).SignInLevel)
            && !await users.Users.AnyAsync(user => user.ArchivedAt == null && user.PinHash != null, cancellationToken))
        {
            return TypedResults.Conflict();
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

    private static async Task<IResult> RevokeStationAsync(Guid id, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        if (await db.BenchStations.FindAsync([id], cancellationToken) is not { } station)
        {
            return TypedResults.NotFound();
        }

        station.RevokedAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<bool> StationIsActiveAsync(ClaimsPrincipal station, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        var id = Guid.Parse(station.FindFirstValue(StationClaim)!);
        return await db.BenchStations.AnyAsync(s => s.Id == id && s.RevokedAt == null, cancellationToken);
    }

    private static async Task<IResult> ListPeopleAsync(
        ClaimsPrincipal station,
        HortiplannerDbContext db,
        UserManager<AppUser> users,
        CancellationToken cancellationToken)
    {
        if (!await StationIsActiveAsync(station, db, cancellationToken))
        {
            return TypedResults.Unauthorized();
        }

        var needsPin = SignInRules.BenchNeedsPin((await db.GetNurserySettingsAsync(cancellationToken)).SignInLevel);

        // With nothing to type at the bench, the owner isn't offered there at all.
        var owners = needsPin ? [] : (await users.GetUsersInRoleAsync(AccountServices.OwnerRole)).Select(owner => owner.Id).ToList();

        var people = await users.Users
            .Where(user => user.ArchivedAt == null && !user.IsTeamAccount && !owners.Contains(user.Id) && (!needsPin || user.PinHash != null))
            .OrderBy(user => user.UserName)
            .Select(user => new Person(user.Id, user.UserName!, db.PersonPictures.Where(picture => picture.UserId == user.Id).Select(picture => (DateTimeOffset?)picture.UpdatedAt).FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(people);
    }

    private static async Task<IResult> SignInAsync(
        BenchSignInRequest request,
        ClaimsPrincipal station,
        HortiplannerDbContext db,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        IPasswordHasher<AppUser> hasher,
        CancellationToken cancellationToken)
    {
        if (!await StationIsActiveAsync(station, db, cancellationToken))
        {
            return TypedResults.Unauthorized();
        }

        if (await users.FindByIdAsync(request.UserId.ToString()) is not { ArchivedAt: null, IsTeamAccount: false } person)
        {
            return TypedResults.Unauthorized();
        }

        // Some nurseries choose tapping a name at the bench, with no PIN; never for the owner.
        if (SignInRules.BenchNeedsPin((await db.GetNurserySettingsAsync(cancellationToken)).SignInLevel))
        {
            if (await RefusePinAsync(person, request.Pin, users, hasher) is { } refused)
            {
                return refused;
            }
        }
        else if (await users.IsInRoleAsync(person, AccountServices.OwnerRole))
        {
            return TypedResults.Unauthorized();
        }

        // Not persistent, and short: sliding expiry returns the station to the name list when idle.
        await signIn.SignInWithClaimsAsync(
            person,
            new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = DateTimeOffset.UtcNow + IdleLimit,
            },
            [new Claim(AccountServices.BenchSessionClaim, "true"), new Claim(AccountServices.SignInMethodClaim, "pin")]);

        return TypedResults.NoContent();
    }

    /// <summary>Why a PIN is refused, or null when it's right.</summary>
    private static async Task<IResult?> RefusePinAsync(AppUser person, string pin, UserManager<AppUser> users, IPasswordHasher<AppUser> hasher)
    {
        if (person.PinHash is not { } pinHash)
        {
            return TypedResults.Unauthorized();
        }

        if (await users.IsLockedOutAsync(person))
        {
            return TypedResults.StatusCode(StatusCodes.Status423Locked);
        }

        if (hasher.VerifyHashedPassword(person, pinHash, pin) == PasswordVerificationResult.Failed)
        {
            await users.AccessFailedAsync(person);

            return await users.IsLockedOutAsync(person)
                ? TypedResults.StatusCode(StatusCodes.Status423Locked)
                : TypedResults.Unauthorized();
        }

        await users.ResetAccessFailedCountAsync(person);
        return null;
    }

    /// <summary>Stops this device being a bench station, so someone can sign in on it as themselves.</summary>
    private static async Task<IResult> LeaveAsync(HttpContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await context.SignOutAsync(AccountServices.BenchScheme);
        return TypedResults.NoContent();
    }

    internal sealed record StationSummary(Guid Id, string Name, bool Revoked);

    internal sealed record RegisterStationRequest(string Name);

    internal sealed record Person(Guid Id, string Name, DateTimeOffset? PictureAt);

    internal sealed record BenchSignInRequest(Guid UserId, string Pin);
}
