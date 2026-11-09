using System.Security.Claims;
using System.Text.Json;
using GrowLog.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Accounts;

internal static class StaffEndpoints
{
    private const string InvitePurpose = "passkey-invite";

    public static void MapStaffEndpoints(this IEndpointRouteBuilder app)
    {
        var staff = app.MapGroup("/api/staff").RequireAuthorization(AccountServices.OwnerPolicy);
        staff.MapGet("/", ListAsync);
        staff.MapPost("/", AddAsync).Produces<StaffCreated>(StatusCodes.Status201Created);
        staff.MapPost("/{id:guid}/pin", SetPinAsync);
        staff.MapPost("/{id:guid}/password", SetPasswordAsync);
        staff.MapPost("/{id:guid}/name-only", SetNameOnlyAsync);
        staff.MapPost("/{id:guid}/invite", InviteAsync).Produces<InviteCreated>();
        staff.MapPost("/{id:guid}/archive", ArchiveAsync);

        var invites = app.MapGroup("/api/invites").AllowAnonymous();
        invites.MapPost("/passkey-options", GetInvitePasskeyOptionsAsync);
        invites.MapPost("/complete", CompleteInviteAsync);
        invites.MapPost("/complete-with-password", CompleteInviteWithPasswordAsync);
    }

    internal static bool IsValidPin(string pin) => pin.Length is >= 4 and <= 6 && pin.All(char.IsAsciiDigit);

    private static async Task<List<StaffMember>> ListAsync(GrowLogDbContext db, UserManager<AppUser> users, CancellationToken cancellationToken)
    {
        var owners = (await users.GetUsersInRoleAsync(AccountServices.OwnerRole)).Select(owner => owner.Id).ToHashSet();

        var staff = await users.Users
            .OrderBy(user => user.UserName)
            .Select(user => new StaffMember(
                user.Id,
                user.UserName!,
                user.ArchivedAt != null,
                false,
                user.NameOnlySignIn,
                user.PasswordHash != null,
                db.UserPasskeys.Any(passkey => passkey.UserId == user.Id),
                user.PinHash != null))
            .ToListAsync(cancellationToken);

        return [.. staff.Select(member => member with { IsOwner = owners.Contains(member.Id) })];
    }

    private static async Task<IResult> AddAsync(
        AddStaffRequest request,
        UserManager<AppUser> users,
        IPasswordHasher<AppUser> hasher)
    {
        var name = request.Name.Trim();

        if (name.Length == 0 || (request.Pin is { } chosenPin && !IsValidPin(chosenPin)) || (request.NameOnly && request.Password is not null))
        {
            return TypedResults.BadRequest();
        }

        if (await users.FindByNameAsync(name) is not null)
        {
            return TypedResults.Conflict();
        }

        var member = new AppUser { Id = Guid.CreateVersion7(), UserName = name, NameOnlySignIn = request.NameOnly };

        if (request.Pin is { } pin)
        {
            member.PinHash = hasher.HashPassword(member, pin);
        }

        var created = request.Password is { } password
            ? await users.CreateAsync(member, password)
            : await users.CreateAsync(member);

        return created.Succeeded
            ? TypedResults.Created($"/api/staff/{member.Id}", new StaffCreated(member.Id))
            : TypedResults.BadRequest();
    }

    private static async Task<IResult> SetPinAsync(
        Guid id,
        SetPinRequest request,
        UserManager<AppUser> users,
        IPasswordHasher<AppUser> hasher)
    {
        if (!IsValidPin(request.Pin))
        {
            return TypedResults.BadRequest();
        }

        if (await users.FindByIdAsync(id.ToString()) is not { ArchivedAt: null } member)
        {
            return TypedResults.NotFound();
        }

        member.PinHash = hasher.HashPassword(member, request.Pin);
        await users.UpdateAsync(member);

        // A new PIN also lifts any lockout from wrong guesses.
        await users.SetLockoutEndDateAsync(member, lockoutEnd: null);
        await users.ResetAccessFailedCountAsync(member);
        return TypedResults.NoContent();
    }

    /// <summary>Lets the owner give someone a password, or replace one they have forgotten.</summary>
    private static async Task<IResult> SetPasswordAsync(
        Guid id,
        SetPasswordRequest request,
        ClaimsPrincipal currentUser,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn)
    {
        if (await users.FindByIdAsync(id.ToString()) is not { ArchivedAt: null } member)
        {
            return TypedResults.NotFound();
        }

        if (!await ReplacePasswordAsync(member, request.Password, users))
        {
            return TypedResults.BadRequest();
        }

        await users.SetLockoutEndDateAsync(member, lockoutEnd: null);
        await users.ResetAccessFailedCountAsync(member);

        if (string.Equals(users.GetUserId(currentUser), id.ToString(), StringComparison.Ordinal))
        {
            await signIn.RefreshSignInAsync(member);
        }

        return TypedResults.NoContent();
    }

    /// <summary>Lets someone sign in by tapping their name. The owner's account always keeps a password or passkey.</summary>
    private static async Task<IResult> SetNameOnlyAsync(Guid id, UserManager<AppUser> users)
    {
        if (await users.FindByIdAsync(id.ToString()) is not { ArchivedAt: null } member)
        {
            return TypedResults.NotFound();
        }

        if (await users.IsInRoleAsync(member, AccountServices.OwnerRole))
        {
            return TypedResults.BadRequest();
        }

        member.NameOnlySignIn = true;
        await users.RemovePasswordAsync(member);
        await users.SetLockoutEndDateAsync(member, lockoutEnd: null);
        await users.ResetAccessFailedCountAsync(member);
        return TypedResults.NoContent();
    }

    private static async Task<bool> ReplacePasswordAsync(AppUser member, string password, UserManager<AppUser> users)
    {
        var token = await users.GeneratePasswordResetTokenAsync(member);
        member.NameOnlySignIn = false;
        return (await users.ResetPasswordAsync(member, token, password)).Succeeded;
    }

    private static async Task<IResult> InviteAsync(Guid id, UserManager<AppUser> users)
    {
        if (await users.FindByIdAsync(id.ToString()) is not { ArchivedAt: null } member)
        {
            return TypedResults.NotFound();
        }

        var token = await users.GenerateUserTokenAsync(member, TokenOptions.DefaultProvider, InvitePurpose);
        return TypedResults.Ok(new InviteCreated(token));
    }

    private static async Task<IResult> ArchiveAsync(Guid id, ClaimsPrincipal currentUser, UserManager<AppUser> users)
    {
        if (string.Equals(users.GetUserId(currentUser), id.ToString(), StringComparison.Ordinal))
        {
            return TypedResults.BadRequest();
        }

        if (await users.FindByIdAsync(id.ToString()) is not { } member)
        {
            return TypedResults.NotFound();
        }

        member.ArchivedAt = DateTimeOffset.UtcNow;
        await users.UpdateAsync(member);

        // The lockout stops every kind of sign-in. The new stamp ends sessions already open.
        await users.SetLockoutEndDateAsync(member, DateTimeOffset.MaxValue);
        await users.UpdateSecurityStampAsync(member);
        return TypedResults.NoContent();
    }

    private static async Task<AppUser?> FindInvitedAsync(Guid userId, string token, UserManager<AppUser> users) =>
        await users.FindByIdAsync(userId.ToString()) is { ArchivedAt: null } member
            && await users.VerifyUserTokenAsync(member, TokenOptions.DefaultProvider, InvitePurpose, token)
            ? member
            : null;

    private static async Task<IResult> GetInvitePasskeyOptionsAsync(
        InviteRequest request,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn)
    {
        if (await FindInvitedAsync(request.UserId, request.Token, users) is not { } member)
        {
            return TypedResults.StatusCode(StatusCodes.Status403Forbidden);
        }

        var options = await signIn.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity
        {
            Id = member.Id.ToString(),
            Name = member.UserName!,
            DisplayName = member.UserName!,
        });

        return TypedResults.Text(options, "application/json");
    }

    private static async Task<IResult> CompleteInviteWithPasswordAsync(
        CompleteInviteWithPasswordRequest request,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn)
    {
        if (await FindInvitedAsync(request.UserId, request.Token, users) is not { } member)
        {
            return TypedResults.StatusCode(StatusCodes.Status403Forbidden);
        }

        // The password change also changes the stamp, which makes the invitation single use.
        if (!await ReplacePasswordAsync(member, request.Password, users))
        {
            return TypedResults.BadRequest();
        }

        await signIn.SignInAsync(member, isPersistent: true);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> CompleteInviteAsync(
        CompleteInviteRequest request,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn)
    {
        if (await FindInvitedAsync(request.UserId, request.Token, users) is not { } member)
        {
            return TypedResults.StatusCode(StatusCodes.Status403Forbidden);
        }

        var attestation = await signIn.PerformPasskeyAttestationAsync(request.Credential.GetRawText());

        if (!attestation.Succeeded
            || !string.Equals(attestation.UserEntity.Id, member.Id.ToString(), StringComparison.Ordinal))
        {
            return TypedResults.BadRequest();
        }

        member.NameOnlySignIn = false;
        await users.AddOrUpdatePasskeyAsync(member, attestation.Passkey);

        // Changing the stamp makes the invitation single use.
        await users.UpdateSecurityStampAsync(member);
        await signIn.SignInAsync(member, isPersistent: true);
        return TypedResults.NoContent();
    }

    internal sealed record StaffMember(Guid Id, string Name, bool Archived, bool IsOwner, bool NameOnly, bool HasPassword, bool HasPasskey, bool HasPin);

    internal sealed record AddStaffRequest(string Name, string? Pin, string? Password, bool NameOnly);

    internal sealed record StaffCreated(Guid Id);

    internal sealed record SetPinRequest(string Pin);

    internal sealed record SetPasswordRequest(string Password);

    internal sealed record InviteCreated(string Token);

    internal sealed record InviteRequest(Guid UserId, string Token);

    internal sealed record CompleteInviteRequest(Guid UserId, string Token, JsonElement Credential);

    internal sealed record CompleteInviteWithPasswordRequest(Guid UserId, string Token, string Password);
}
