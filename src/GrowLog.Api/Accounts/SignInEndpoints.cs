using System.Security.Claims;
using System.Text.Json;
using GrowLog.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Accounts;

/// <summary>Signing in by name or password, and each person's own sign-in settings.</summary>
internal static class SignInEndpoints
{
    public static void MapSignInEndpoints(this IEndpointRouteBuilder app)
    {
        var account = app.MapGroup("/api/account").AllowAnonymous();
        account.MapGet("/people", ListPeopleAsync);
        account.MapPost("/sign-in/name", SignInByNameAsync);
        account.MapPost("/sign-in/password", SignInWithPasswordAsync);

        var settings = app.MapGroup("/api/account/settings");
        settings.MapGet("/", GetSettingsAsync).Produces<AccountSettings>();
        settings.MapPost("/password", SetOwnPasswordAsync);
        settings.MapPost("/pin", SetOwnPinAsync);
        settings.MapPost("/passkey-options", GetOwnPasskeyOptionsAsync);
        settings.MapPost("/passkey", AddOwnPasskeyAsync);
    }

    /// <summary>Names everyone who can sign in here, so they can tap theirs rather than type it.</summary>
    private static async Task<List<SignInPerson>> ListPeopleAsync(GrowLogDbContext db, UserManager<AppUser> users, CancellationToken cancellationToken)
    {
        var nameOnly = (await db.GetNurserySettingsAsync(cancellationToken)).AllowNameOnlySignIn;

        return await users.Users
            .Where(user => user.ArchivedAt == null
                && ((nameOnly && user.NameOnlySignIn) || user.PasswordHash != null || db.UserPasskeys.Any(passkey => passkey.UserId == user.Id)))
            .OrderBy(user => user.UserName)
            .Select(user => new SignInPerson(user.Id, user.UserName!, nameOnly && user.NameOnlySignIn, user.PasswordHash != null))
            .ToListAsync(cancellationToken);
    }

    private static async Task<IResult> SignInByNameAsync(
        NameSignInRequest request,
        GrowLogDbContext db,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        CancellationToken cancellationToken)
    {
        // The role is checked here too, so that nothing can open up the owner's account.
        if (!(await db.GetNurserySettingsAsync(cancellationToken)).AllowNameOnlySignIn
            || await users.FindByIdAsync(request.UserId.ToString()) is not { ArchivedAt: null, NameOnlySignIn: true } person
            || await users.IsLockedOutAsync(person)
            || await users.IsInRoleAsync(person, AccountServices.OwnerRole))
        {
            return TypedResults.Unauthorized();
        }

        await signIn.SignInAsync(person, isPersistent: true);
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

        await signIn.SignInAsync(person, isPersistent: true);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetSettingsAsync(ClaimsPrincipal principal, UserManager<AppUser> users, ExternalSignInStore store)
    {
        if (await users.GetUserAsync(principal) is not { } me)
        {
            return TypedResults.Unauthorized();
        }

        var passkeys = await users.GetPasskeysAsync(me);
        var logins = await users.GetLoginsAsync(me);
        var externalName = store.Current.IsOn ? store.Current.DisplayName : null;

        return TypedResults.Ok(new AccountSettings(me.PasswordHash is not null, me.PinHash is not null, passkeys.Count > 0, logins.Count > 0, externalName, me.NameOnlySignIn));
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
        await signIn.RefreshSignInAsync(me);
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
        return TypedResults.NoContent();
    }

    internal sealed record SignInPerson(Guid Id, string Name, bool NameOnly, bool HasPassword);

    internal sealed record NameSignInRequest(Guid UserId);

    internal sealed record PasswordSignInRequest(Guid UserId, string Password);

    internal sealed record AccountSettings(bool HasPassword, bool HasPin, bool HasPasskey, bool HasExternalLogin, string? ExternalSignInName, bool NameOnly);

    internal sealed record SetOwnPasswordRequest(string? CurrentPassword, string NewPassword);

    internal sealed record SetOwnPinRequest(string Pin);

    internal sealed record AddOwnPasskeyRequest(JsonElement Credential);
}
