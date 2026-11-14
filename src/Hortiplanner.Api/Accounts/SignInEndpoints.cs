using System.Security.Claims;
using System.Text.Json;
using Hortiplanner.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Accounts;

/// <summary>Signing in by name or password, and each person's own sign-in settings.</summary>
internal static class SignInEndpoints
{
    public static void MapSignInEndpoints(this IEndpointRouteBuilder app)
    {
        var account = app.MapGroup("/api/account").AllowAnonymous();
        account.MapGet("/people", ListPeopleAsync);
        account.MapPost("/sign-in/name", SignInByNameAsync);
        account.MapPost("/sign-in/password", SignInWithPasswordAsync);
        account.MapPost("/sign-in/team", SignInAsTeamAsync);
        account.MapGet("/sign-in-mode", GetModeAsync);

        var settings = app.MapGroup("/api/account/settings");
        settings.MapGet("/", GetSettingsAsync).Produces<AccountSettings>();
        settings.MapPost("/password", SetOwnPasswordAsync).AddEndpointFilter<OwnSettingsFilter>();
        settings.MapPost("/pin", SetOwnPinAsync).AddEndpointFilter<OwnSettingsFilter>();
        settings.MapPost("/passkey-options", GetOwnPasskeyOptionsAsync).AddEndpointFilter<OwnSettingsFilter>();
        settings.MapPost("/passkey", AddOwnPasskeyAsync).AddEndpointFilter<OwnSettingsFilter>();
    }

    /// <summary>How much signing in the nursery wants, so a device knows what to show first.</summary>
    private static async Task<SignInMode> GetModeAsync(HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        var level = (await db.GetNurserySettingsAsync(cancellationToken)).SignInLevel;
        return new SignInMode(level, SignInRules.BenchNeedsPin(level));
    }

    /// <summary>Names everyone who can sign in here, so they can tap theirs rather than type it.</summary>
    private static async Task<List<SignInPerson>> ListPeopleAsync(HortiplannerDbContext db, UserManager<AppUser> users, CancellationToken cancellationToken)
    {
        var level = (await db.GetNurserySettingsAsync(cancellationToken)).SignInLevel;
        var owners = (await users.GetUsersInRoleAsync(AccountServices.OwnerRole)).Select(owner => owner.Id).ToHashSet();

        var people = await users.Users
            .Where(user => user.ArchivedAt == null && !user.IsTeamAccount)
            .OrderBy(user => user.UserName)
            .Select(user => new
            {
                user.Id,
                Name = user.UserName!,
                user.NameOnlySignIn,
                HasPassword = user.PasswordHash != null,
                HasPasskey = db.UserPasskeys.Any(passkey => passkey.UserId == user.Id),
            })
            .ToListAsync(cancellationToken);

        return people
            .Select(person => (Person: person, Tap: SignInRules.CanTapName(level, person.NameOnlySignIn, owners.Contains(person.Id))))
            .Where(row => row.Tap || row.Person.HasPassword || row.Person.HasPasskey)
            .Select(row => new SignInPerson(row.Person.Id, row.Person.Name, row.Tap, row.Person.HasPassword))
            .ToList();
    }

    private static async Task<IResult> SignInByNameAsync(
        NameSignInRequest request,
        HortiplannerDbContext db,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        CancellationToken cancellationToken)
    {
        var level = (await db.GetNurserySettingsAsync(cancellationToken)).SignInLevel;

        // The role is checked here too, so that nothing can open up the owner's account.
        if (await users.FindByIdAsync(request.UserId.ToString()) is not { ArchivedAt: null, IsTeamAccount: false } person
            || await users.IsLockedOutAsync(person)
            || !SignInRules.CanTapName(level, person.NameOnlySignIn, await users.IsInRoleAsync(person, AccountServices.OwnerRole)))
        {
            return TypedResults.Unauthorized();
        }

        await AccountServices.SignInAsync(signIn, person, "name");
        return TypedResults.NoContent();
    }

    /// <summary>With no signing in chosen, a device goes straight in as the nursery team.</summary>
    private static async Task<IResult> SignInAsTeamAsync(
        HortiplannerDbContext db,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        CancellationToken cancellationToken)
    {
        if ((await db.GetNurserySettingsAsync(cancellationToken)).SignInLevel != SignInLevel.Open)
        {
            return TypedResults.Conflict();
        }

        var team = await users.Users.FirstOrDefaultAsync(user => user.IsTeamAccount, cancellationToken);

        if (team is null)
        {
            var name = await users.FindByNameAsync(SignInRules.TeamName) is null ? SignInRules.TeamName : $"{SignInRules.TeamName} (shared)";
            team = new AppUser { Id = Guid.CreateVersion7(), UserName = name, IsTeamAccount = true };

            if (!(await users.CreateAsync(team)).Succeeded)
            {
                return TypedResults.Problem("The nursery team account couldn't be made.");
            }
        }

        await AccountServices.SignInAsync(signIn, team, "team");
        return TypedResults.NoContent();
    }

    private static async Task<IResult> SignInWithPasswordAsync(
        PasswordSignInRequest request,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn)
    {
        // Without a password there is nothing to guess, so a try must not count towards a lockout.
        if (await users.FindByIdAsync(request.UserId.ToString()) is not { ArchivedAt: null, PasswordHash: not null } person)
        {
            return TypedResults.Unauthorized();
        }

        var result = await signIn.CheckPasswordSignInAsync(person, request.Password, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            return TypedResults.StatusCode(StatusCodes.Status423Locked);
        }

        if (!result.Succeeded)
        {
            return TypedResults.Unauthorized();
        }

        await AccountServices.SignInAsync(signIn, person, "password");
        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetSettingsAsync(ClaimsPrincipal principal, UserManager<AppUser> users, ExternalSignInStore store, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        if (await users.GetUserAsync(principal) is not { } me)
        {
            return TypedResults.Unauthorized();
        }

        var passkeys = await users.GetPasskeysAsync(me);
        var logins = await users.GetLoginsAsync(me);
        var externalName = store.Current.IsOn ? store.Current.DisplayName : null;

        var tuneOff = await db.SignInTunes.Where(tune => tune.UserId == me.Id).Select(tune => (bool?)tune.Off).FirstOrDefaultAsync(cancellationToken);

        return TypedResults.Ok(new AccountSettings(me.PasswordHash is not null, me.PinHash is not null, passkeys.Count > 0, logins.Count > 0, externalName, me.NameOnlySignIn, tuneOff is not null, tuneOff == false));
    }

    private static async Task<IResult> SetOwnPasswordAsync(
        SetOwnPasswordRequest request,
        ClaimsPrincipal principal,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn)
    {
        if (await users.GetUserAsync(principal) is not { } me)
        {
            return TypedResults.Unauthorized();
        }

        if (me.PasswordHash is not null && !await users.CheckPasswordAsync(me, request.CurrentPassword ?? string.Empty))
        {
            return TypedResults.StatusCode(StatusCodes.Status403Forbidden);
        }

        me.NameOnlySignIn = false;

        var result = me.PasswordHash is null
            ? await users.AddPasswordAsync(me, request.NewPassword)
            : await users.ChangePasswordAsync(me, request.CurrentPassword!, request.NewPassword);

        if (!result.Succeeded)
        {
            return TypedResults.BadRequest();
        }

        // The change ends other sessions. This one carries on.
        await AccountServices.SignInAsync(signIn, me, "password");
        return TypedResults.NoContent();
    }

    private static async Task<IResult> SetOwnPinAsync(
        SetOwnPinRequest request,
        ClaimsPrincipal principal,
        UserManager<AppUser> users,
        IPasswordHasher<AppUser> hasher)
    {
        if (!StaffEndpoints.IsValidPin(request.Pin))
        {
            return TypedResults.BadRequest();
        }

        if (await users.GetUserAsync(principal) is not { } me)
        {
            return TypedResults.Unauthorized();
        }

        me.PinHash = hasher.HashPassword(me, request.Pin);
        await users.UpdateAsync(me);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetOwnPasskeyOptionsAsync(
        ClaimsPrincipal principal,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn)
    {
        if (await users.GetUserAsync(principal) is not { } me)
        {
            return TypedResults.Unauthorized();
        }

        var options = await signIn.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity
        {
            Id = me.Id.ToString(),
            Name = me.UserName!,
            DisplayName = me.UserName!,
        });

        return TypedResults.Text(options, "application/json");
    }

    private static async Task<IResult> AddOwnPasskeyAsync(
        AddOwnPasskeyRequest request,
        ClaimsPrincipal principal,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn)
    {
        if (await users.GetUserAsync(principal) is not { } me)
        {
            return TypedResults.Unauthorized();
        }

        var attestation = await signIn.PerformPasskeyAttestationAsync(request.Credential.GetRawText());

        if (!attestation.Succeeded
            || !string.Equals(attestation.UserEntity.Id, me.Id.ToString(), StringComparison.Ordinal))
        {
            return TypedResults.BadRequest();
        }

        me.NameOnlySignIn = false;
        await users.AddOrUpdatePasskeyAsync(me, attestation.Passkey);
        await users.UpdateAsync(me);
        await AccountServices.SignInAsync(signIn, me, "passkey");
        return TypedResults.NoContent();
    }

    internal sealed record SignInPerson(Guid Id, string Name, bool NameOnly, bool HasPassword);

    internal sealed record NameSignInRequest(Guid UserId);

    internal sealed record SignInMode(SignInLevel Level, bool BenchNeedsPin);

    internal sealed record PasswordSignInRequest(Guid UserId, string Password);

    internal sealed record AccountSettings(bool HasPassword, bool HasPin, bool HasPasskey, bool HasExternalLogin, string? ExternalSignInName, bool NameOnly, bool HasTune, bool TuneOn);

    internal sealed record SetOwnPasswordRequest(string? CurrentPassword, string NewPassword);

    internal sealed record SetOwnPinRequest(string Pin);

    internal sealed record AddOwnPasskeyRequest(JsonElement Credential);
}
