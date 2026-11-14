using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hortiplanner.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Accounts;

internal static class AccountEndpoints
{
    private const string JsonContentType = "application/json";

    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var setup = app.MapGroup("/api/setup").AllowAnonymous();
        setup.MapGet("/status", GetSetupStatusAsync);
        setup.MapPost("/passkey-options", GetSetupPasskeyOptionsAsync);
        setup.MapPost("/complete", CompleteSetupAsync);
        setup.MapPost("/complete-with-password", CompleteSetupWithPasswordAsync);

        var account = app.MapGroup("/api/account");
        account.MapPost("/passkey-options", GetSignInPasskeyOptionsAsync).AllowAnonymous();
        account.MapPost("/sign-in", SignInAsync).AllowAnonymous();
        account.MapPost("/sign-out", SignOutAsync);
        account.MapGet("/me", GetCurrentUserAsync);
    }

    private static async Task<SetupStatus> GetSetupStatusAsync(UserManager<AppUser> users, CancellationToken cancellationToken) =>
        new(!await users.Users.AnyAsync(cancellationToken));

    private static async Task<IResult> GetSetupPasskeyOptionsAsync(
        SetupOptionsRequest request,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (!await SetupIsOpenAsync(request.Token, users, configuration, cancellationToken))
        {
            return TypedResults.StatusCode(StatusCodes.Status403Forbidden);
        }

        var name = request.Name.Trim();

        if (name.Length == 0)
        {
            return TypedResults.BadRequest();
        }

        var options = await signIn.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity
        {
            Id = Guid.CreateVersion7().ToString(),
            Name = name,
            DisplayName = name,
        });

        return TypedResults.Text(options, JsonContentType);
    }

    private static async Task<IResult> CompleteSetupAsync(
        SetupCompleteRequest request,
        UserManager<AppUser> users,
        RoleManager<IdentityRole<Guid>> roles,
        SignInManager<AppUser> signIn,
        HortiplannerDbContext db,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Makes a second simultaneous attempt wait, then find setup already done.
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(7302001)", cancellationToken);

        if (!await SetupIsOpenAsync(request.Token, users, configuration, cancellationToken))
        {
            return TypedResults.StatusCode(StatusCodes.Status403Forbidden);
        }

        var attestation = await signIn.PerformPasskeyAttestationAsync(request.Credential.GetRawText());

        if (!attestation.Succeeded)
        {
            return TypedResults.BadRequest();
        }

        var owner = new AppUser { Id = Guid.Parse(attestation.UserEntity.Id), UserName = attestation.UserEntity.Name };

        var created = await users.CreateAsync(owner);

        if (!created.Succeeded)
        {
            return TypedResults.BadRequest();
        }

        await roles.CreateAsync(new IdentityRole<Guid>(AccountServices.OwnerRole));
        await users.AddToRoleAsync(owner, AccountServices.OwnerRole);
        await users.AddOrUpdatePasskeyAsync(owner, attestation.Passkey);
        await transaction.CommitAsync(cancellationToken);

        await AccountServices.SignInAsync(signIn, owner, "passkey");
        return TypedResults.NoContent();
    }

    private static async Task<IResult> CompleteSetupWithPasswordAsync(
        SetupPasswordRequest request,
        UserManager<AppUser> users,
        RoleManager<IdentityRole<Guid>> roles,
        SignInManager<AppUser> signIn,
        HortiplannerDbContext db,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(7302001)", cancellationToken);

        if (!await SetupIsOpenAsync(request.Token, users, configuration, cancellationToken))
        {
            return TypedResults.StatusCode(StatusCodes.Status403Forbidden);
        }

        var name = request.Name.Trim();

        if (name.Length == 0)
        {
            return TypedResults.BadRequest();
        }

        var owner = new AppUser { Id = Guid.CreateVersion7(), UserName = name };
        var created = await users.CreateAsync(owner, request.Password);

        if (!created.Succeeded)
        {
            return TypedResults.BadRequest();
        }

        await roles.CreateAsync(new IdentityRole<Guid>(AccountServices.OwnerRole));
        await users.AddToRoleAsync(owner, AccountServices.OwnerRole);
        await transaction.CommitAsync(cancellationToken);

        await AccountServices.SignInAsync(signIn, owner, "password");
        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetSignInPasskeyOptionsAsync(SignInManager<AppUser> signIn) =>
        TypedResults.Text(await signIn.MakePasskeyRequestOptionsAsync(user: null), JsonContentType);

    private static async Task<IResult> SignInAsync(
        SignInRequest request,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn)
    {
        var assertion = await signIn.PerformPasskeyAssertionAsync(request.Credential.GetRawText());

        if (!assertion.Succeeded || await users.IsLockedOutAsync(assertion.User))
        {
            return TypedResults.Unauthorized();
        }

        // Saves the new signature counter, which is how a cloned passkey would be noticed.
        await users.AddOrUpdatePasskeyAsync(assertion.User, assertion.Passkey);
        await AccountServices.SignInAsync(signIn, assertion.User, "passkey");
        return TypedResults.NoContent();
    }

    private static async Task<IResult> SignOutAsync(SignInManager<AppUser> signIn)
    {
        await signIn.SignOutAsync();
        return TypedResults.NoContent();
    }

    // A PIN at a bench station is enough for recording work, not for the owner's settings.
    private static async Task<CurrentUser> GetCurrentUserAsync(ClaimsPrincipal user, UserManager<AppUser> users, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        var person = await users.GetUserAsync(user);
        var id = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var pictureAt = await db.PersonPictures.Where(picture => picture.UserId == id).Select(picture => (DateTimeOffset?)picture.UpdatedAt).FirstOrDefaultAsync(cancellationToken);

        return new CurrentUser(
            id,
            user.Identity?.Name ?? string.Empty,
            user.IsInRole(AccountServices.OwnerRole) && user.FindFirst(AccountServices.BenchSessionClaim) is null,
            person?.IsTeamAccount ?? false,
            pictureAt);
    }

    private static async Task<bool> SetupIsOpenAsync(
        string token,
        UserManager<AppUser> users,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var expected = configuration["Setup:Token"];

        return !string.IsNullOrEmpty(expected)
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(expected))
            && !await users.Users.AnyAsync(cancellationToken);
    }

    internal sealed record SetupStatus(bool NeedsSetup);

    internal sealed record SetupOptionsRequest(string Token, string Name);

    internal sealed record SetupCompleteRequest(string Token, JsonElement Credential);

    internal sealed record SetupPasswordRequest(string Token, string Name, string Password);

    internal sealed record SignInRequest(JsonElement Credential);

    /// <param name="IsTeam">Signed in as the nursery team, because the nursery chose no signing in.</param>
    internal sealed record CurrentUser(Guid Id, string Name, bool IsOwner, bool IsTeam, DateTimeOffset? PictureAt);
}
