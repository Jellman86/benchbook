using System.Security.Claims;
using System.Text.Json;
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
        staff.MapPost("/", AddAsync);
        staff.MapPost("/{id:guid}/pin", SetPinAsync);
        staff.MapPost("/{id:guid}/invite", InviteAsync);
        staff.MapPost("/{id:guid}/archive", ArchiveAsync);

        var invites = app.MapGroup("/api/invites").AllowAnonymous();
        invites.MapPost("/passkey-options", GetInvitePasskeyOptionsAsync);
        invites.MapPost("/complete", CompleteInviteAsync);
    }

    private static bool IsValidPin(string pin) => pin.Length is >= 4 and <= 6 && pin.All(char.IsAsciiDigit);

    private static async Task<List<StaffMember>> ListAsync(UserManager<AppUser> users, CancellationToken cancellationToken) =>
        await users.Users
            .OrderBy(user => user.UserName)
            .Select(user => new StaffMember(user.Id, user.UserName!, user.ArchivedAt != null))
            .ToListAsync(cancellationToken);

    private static async Task<IResult> AddAsync(
        AddStaffRequest request,
        UserManager<AppUser> users,
        IPasswordHasher<AppUser> hasher)
    {
        var name = request.Name.Trim();

        if (name.Length == 0 || !IsValidPin(request.Pin))
        {
            return TypedResults.BadRequest();
        }

        if (await users.FindByNameAsync(name) is not null)
        {
            return TypedResults.Conflict();
        }

        var member = new AppUser { Id = Guid.CreateVersion7(), UserName = name };
        member.PinHash = hasher.HashPassword(member, request.Pin);

        var created = await users.CreateAsync(member);

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

        await users.AddOrUpdatePasskeyAsync(member, attestation.Passkey);

        // Changing the stamp makes the invitation single use.
        await users.UpdateSecurityStampAsync(member);
        await signIn.SignInAsync(member, isPersistent: true);
        return TypedResults.NoContent();
    }

    internal sealed record StaffMember(Guid Id, string Name, bool Archived);

    internal sealed record AddStaffRequest(string Name, string Pin);

    internal sealed record StaffCreated(Guid Id);

    internal sealed record SetPinRequest(string Pin);

    internal sealed record InviteCreated(string Token);

    internal sealed record InviteRequest(Guid UserId, string Token);

    internal sealed record CompleteInviteRequest(Guid UserId, string Token, JsonElement Credential);
}
