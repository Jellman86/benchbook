using System.Security.Claims;
using GrowLog.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Accounts;

/// <summary>
/// Signing in through the nursery's shared staff login, when one is configured.
/// It sits alongside GrowLog's own sign-in, which keeps working if the shared login is down.
/// </summary>
internal static class ExternalSignInEndpoints
{
    private const string CompletePath = "/api/account/external/complete";
    private const string LinkItem = "growlog_link_user";
    private const string NameClaim = "name";
    private const string UsernameClaim = "preferred_username";
    private const string GroupsClaim = "groups";

    public static void MapExternalSignInEndpoints(this IEndpointRouteBuilder app)
    {
        var external = app.MapGroup("/api/account/external");
        external.MapGet("/", GetOptions).AllowAnonymous();
        external.MapGet("/login", LoginAsync).AllowAnonymous();
        external.MapGet("/link", LinkAsync).AddEndpointFilter<OwnSettingsFilter>();
        external.MapGet("/complete", CompleteAsync).AllowAnonymous();
    }

    private static RedirectHttpResult Back(string problem) => TypedResults.Redirect($"/?sso={problem}");

    private static ExternalSignInOptions GetOptions(ExternalSignInStore store) =>
        new(store.Current.IsOn, store.Current.DisplayName);

    private static Task<IResult> LoginAsync(HttpContext context, SignInManager<AppUser> signIn, ExternalSignInStore store) =>
        ChallengeAsync(context, signIn, store, linkUser: null);

    private static Task<IResult> LinkAsync(HttpContext context, ClaimsPrincipal user, UserManager<AppUser> users, SignInManager<AppUser> signIn, ExternalSignInStore store) =>
        ChallengeAsync(context, signIn, store, users.GetUserId(user));

    private static async Task<IResult> ChallengeAsync(HttpContext context, SignInManager<AppUser> signIn, ExternalSignInStore store, string? linkUser)
    {
        if (!store.Current.IsOn)
        {
            return Back("unavailable");
        }

        var properties = signIn.ConfigureExternalAuthenticationProperties(AccountServices.OidcScheme, CompletePath);

        // Kept in the protected sign-in state: the session cookie doesn't survive the trip back.
        if (linkUser is not null)
        {
            properties.Items[LinkItem] = linkUser;
        }

        try
        {
            await context.ChallengeAsync(AccountServices.OidcScheme, properties);
            return TypedResults.Empty;
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or IOException)
        {
            return Back("unavailable");
        }
    }

    private static async Task<IResult> CompleteAsync(
        HttpContext context,
        UserManager<AppUser> users,
        RoleManager<IdentityRole<Guid>> roles,
        SignInManager<AppUser> signIn,
        ExternalSignInStore store)
    {
        if (!store.Current.IsOn || await signIn.GetExternalLoginInfoAsync() is not { } info)
        {
            return Back("failed");
        }

        await context.SignOutAsync(IdentityConstants.ExternalScheme);
        var subject = info.Principal.FindFirst("sub") ?? info.Principal.FindFirst(ClaimTypes.NameIdentifier);
        if (subject is null || !string.Equals(subject.Issuer.TrimEnd('/'), store.Current.Authority?.TrimEnd('/'), StringComparison.Ordinal))
        {
            return Back("failed");
        }
        info = new ExternalLoginInfo(info.Principal, info.LoginProvider, RequestFingerprint.For(new { subject.Issuer, Subject = subject.Value }), info.ProviderDisplayName ?? AccountServices.OidcScheme)
        {
            AuthenticationProperties = info.AuthenticationProperties,
        };

        if (info.AuthenticationProperties?.Items.TryGetValue(LinkItem, out var linkUser) == true && linkUser is not null)
        {
            return await LinkLoginAsync(linkUser, info, users);
        }

        var person = await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);

        if (person is null)
        {
            var (created, problem) = await CreatePersonAsync(info, users);

            if (created is null)
            {
                return Back(problem);
            }

            person = created;
        }

        if (person.ArchivedAt is not null || await users.IsLockedOutAsync(person))
        {
            return Back("refused");
        }

        await MatchOwnerRoleAsync(person, info, users, roles, store);
        await AccountServices.SignInAsync(signIn, person, "oidc");
        return TypedResults.Redirect("/");
    }

    /// <summary>Connects the shared login to the account already signed in here.</summary>
    private static async Task<IResult> LinkLoginAsync(string userId, ExternalLoginInfo info, UserManager<AppUser> users)
    {
        if (await users.FindByIdAsync(userId) is not { ArchivedAt: null } person)
        {
            return Back("refused");
        }

        var existing = await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);

        if (existing is not null)
        {
            return existing.Id == person.Id ? TypedResults.Redirect("/") : Back("already-linked");
        }

        if (!(await users.AddLoginAsync(person, info)).Succeeded)
        {
            return Back("failed");
        }

        person.NameOnlySignIn = false;
        await users.UpdateAsync(person);
        return TypedResults.Redirect("/");
    }

    /// <summary>
    /// Adds a member of staff the first time they arrive. A name already in use is never taken over,
    /// because people can edit their own display name in the shared login.
    /// </summary>
    private static async Task<(AppUser? Person, string Problem)> CreatePersonAsync(ExternalLoginInfo info, UserManager<AppUser> users)
    {
        var name = (info.Principal.FindFirstValue(NameClaim) ?? info.Principal.FindFirstValue(UsernameClaim))?.Trim();

        // The first account has to be the owner's, made with the setup code.
        if (string.IsNullOrEmpty(name) || !await users.Users.AnyAsync())
        {
            return (null, "failed");
        }

        if (await users.FindByNameAsync(name) is not null)
        {
            return (null, "name-taken");
        }

        var person = new AppUser { Id = Guid.CreateVersion7(), UserName = name };

        return (await users.CreateAsync(person)).Succeeded && (await users.AddLoginAsync(person, info)).Succeeded
            ? (person, string.Empty)
            : (null, "failed");
    }

    /// <summary>The group decides who is an owner. The last owner always stays one, so nobody is locked out of Manage.</summary>
    private static async Task MatchOwnerRoleAsync(
        AppUser person,
        ExternalLoginInfo info,
        UserManager<AppUser> users,
        RoleManager<IdentityRole<Guid>> roles,
        ExternalSignInStore store)
    {
        if (store.Current.OwnerGroup is not { Length: > 0 } group)
        {
            return;
        }

        var inGroup = info.Principal.FindAll(GroupsClaim).Any(claim => string.Equals(claim.Value, group, StringComparison.OrdinalIgnoreCase));
        var isOwner = await users.IsInRoleAsync(person, AccountServices.OwnerRole);

        if (inGroup && !isOwner)
        {
            if (!await roles.RoleExistsAsync(AccountServices.OwnerRole))
            {
                await roles.CreateAsync(new IdentityRole<Guid>(AccountServices.OwnerRole));
            }

            await users.AddToRoleAsync(person, AccountServices.OwnerRole);
        }
        else if (!inGroup && isOwner && (await users.GetUsersInRoleAsync(AccountServices.OwnerRole)).Count(owner => owner.ArchivedAt is null) > 1)
        {
            await users.RemoveFromRoleAsync(person, AccountServices.OwnerRole);
            await users.UpdateSecurityStampAsync(person);
        }
    }

    internal sealed record ExternalSignInOptions(bool Enabled, string Name);
}
