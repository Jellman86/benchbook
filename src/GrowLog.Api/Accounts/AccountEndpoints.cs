using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GrowLog.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Accounts;

internal static class AccountEndpoints
{
    private const string JsonContentType = "application/json";

    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var setup = app.MapGroup("/api/setup").AllowAnonymous();
        setup.MapGet("/status", GetSetupStatusAsync);
        setup.MapPost("/passkey-options", GetSetupPasskeyOptionsAsync);
        setup.MapPost("/complete", CompleteSetupAsync);

        var account = app.MapGroup("/api/account");
        account.MapPost("/passkey-options", GetSignInPasskeyOptionsAsync).AllowAnonymous();
        account.MapPost("/sign-in", SignInAsync).AllowAnonymous();
        account.MapPost("/sign-out", SignOutAsync);
        account.MapGet("/me", GetCurrentUser);
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
        GrowLogDbContext db,
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

        await signIn.SignInAsync(owner, isPersistent: true);
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
        await signIn.SignInAsync(assertion.User, isPersistent: true);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> SignOutAsync(SignInManager<AppUser> signIn)
    {
        await signIn.SignOutAsync();
        return TypedResults.NoContent();
    }

    private static CurrentUser GetCurrentUser(ClaimsPrincipal user) =>
        new(user.Identity?.Name ?? string.Empty, user.IsInRole(AccountServices.OwnerRole));

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

    internal sealed record SignInRequest(JsonElement Credential);

    internal sealed record CurrentUser(string Name, bool IsOwner);
}
