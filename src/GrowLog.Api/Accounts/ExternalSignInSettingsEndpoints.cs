using System.Net.Http.Json;
using System.Text.Json;
using GrowLog.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Accounts;

/// <summary>Lets the owner choose the shared staff login from inside GrowLog.</summary>
internal static class ExternalSignInSettingsEndpoints
{
    public const string HttpClientName = "ExternalSignIn";

    private const string Incomplete = "incomplete";
    private const string Unreachable = "unreachable";
    private const string WrongIssuer = "wrong-issuer";
    private const string LastWayIn = "last-way-in";

    public static void MapExternalSignInSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var settings = app.MapGroup("/api/external-sign-in").RequireAuthorization(AccountServices.OwnerPolicy);
        settings.MapGet("/", Get).Produces<ExternalSignInSettings>();
        settings.MapPut("/", SaveAsync).Produces(StatusCodes.Status204NoContent).Produces<SaveRefused>(StatusCodes.Status400BadRequest);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? WithoutSlash(string? address) => address?.TrimEnd('/');

    private static bool SameProvider(string? first, string? second) =>
        string.Equals(WithoutSlash(first), WithoutSlash(second), StringComparison.Ordinal);

    private static ExternalSignInSettings Get(ExternalSignInStore store, IConfiguration configuration)
    {
        var current = store.Current;

        return new ExternalSignInSettings(
            current.IsOn,
            current.Authority,
            current.ClientId,
            !string.IsNullOrEmpty(current.ClientSecret),
            current.DisplayName,
            current.OwnerGroup,
            $"{AccountServices.PublicOrigin(configuration)}/signin-oidc",
            !store.OwnerHasSaved);
    }

    private static async Task<IResult> SaveAsync(
        SaveExternalSignIn request,
        GrowLogDbContext db,
        UserManager<AppUser> users,
        ExternalSignInStore store,
        IHttpClientFactory httpClients,
        CancellationToken cancellationToken)
    {
        var before = store.Current;
        var authority = Clean(request.Authority);
        var clientId = Clean(request.ClientId);
        var secret = Clean(request.ClientSecret) ?? before.ClientSecret;

        if (request.Enabled && await FindProblemAsync(authority, clientId, secret, httpClients, cancellationToken) is { } problem)
        {
            return TypedResults.BadRequest(new SaveRefused(problem));
        }

        // Signed-in people are matched by an identifier that only means something to one provider.
        var leavingProvider = before.IsOn && (!request.Enabled || !SameProvider(before.Authority, authority));

        if (leavingProvider && !await AnOwnerHasAnotherWayInAsync(db, users, cancellationToken))
        {
            return TypedResults.BadRequest(new SaveRefused(LastWayIn));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var saved = await db.ExternalSignInSettings.FirstOrDefaultAsync(cancellationToken);

        if (saved is null)
        {
            saved = new ExternalSignInSetting { DisplayName = string.Empty };
            db.ExternalSignInSettings.Add(saved);
        }

        saved.Enabled = request.Enabled;
        saved.Authority = authority;
        saved.ClientId = clientId;
        saved.ProtectedClientSecret = secret is null ? null : store.Protect(secret);
        saved.DisplayName = Clean(request.DisplayName) ?? before.DisplayName;
        saved.OwnerGroup = Clean(request.OwnerGroup);
        await db.SaveChangesAsync(cancellationToken);

        if (!SameProvider(before.Authority, authority))
        {
            await db.UserLogins.Where(login => login.LoginProvider == AccountServices.OidcScheme).ExecuteDeleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        store.Refresh();
        return TypedResults.NoContent();
    }

    private static async Task<string?> FindProblemAsync(string? authority, string? clientId, string? secret, IHttpClientFactory httpClients, CancellationToken cancellationToken)
    {
        if (!ExternalSignInConfiguration.IsSecureAddress(authority) || clientId is null || secret is null)
        {
            return Incomplete;
        }

        try
        {
            using var client = httpClients.CreateClient(HttpClientName);
            var discovery = await client.GetFromJsonAsync<JsonElement>(new Uri($"{WithoutSlash(authority)}/.well-known/openid-configuration"), cancellationToken);
            return SameProvider(discovery.GetProperty("issuer").GetString(), authority) ? null : WrongIssuer;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return Unreachable;
        }
    }

    private static async Task<bool> AnOwnerHasAnotherWayInAsync(GrowLogDbContext db, UserManager<AppUser> users, CancellationToken cancellationToken)
    {
        var owners = await users.GetUsersInRoleAsync(AccountServices.OwnerRole);
        var active = owners.Where(owner => owner.ArchivedAt is null).ToList();
        var ids = active.ConvertAll(owner => owner.Id);

        return active.Exists(owner => owner.PasswordHash is not null)
            || await db.UserPasskeys.AnyAsync(passkey => ids.Contains(passkey.UserId), cancellationToken);
    }

    internal sealed record ExternalSignInSettings(
        bool Enabled,
        string? Authority,
        string? ClientId,
        bool HasClientSecret,
        string DisplayName,
        string? OwnerGroup,
        string RedirectUri,
        bool FromDeployment);

    internal sealed record SaveExternalSignIn(
        bool Enabled,
        string? Authority,
        string? ClientId,
        string? ClientSecret,
        string? DisplayName,
        string? OwnerGroup);

    internal sealed record SaveRefused(string Problem);
}
