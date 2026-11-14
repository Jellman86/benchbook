using System.Security.Cryptography;
using Hortiplanner.Api.Data;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Hortiplanner.Api.Accounts;

/// <summary>
/// The shared staff login currently in force: what the owner saved, or else the deployment's settings.
/// Sign-in reads this on every request, so it is held in memory and replaced when the owner saves.
/// </summary>
internal sealed class ExternalSignInStore(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    IDataProtectionProvider protection,
    IOptionsMonitorCache<OpenIdConnectOptions> handlerOptions)
{
    private const string DefaultDisplayName = "SSO";

    // The app's old name: saved secrets were protected with it and can only be read back with the same purpose.
    private readonly IDataProtector protector = protection.CreateProtector("GrowLog.ExternalSignIn.ClientSecret");
    private readonly Lock gate = new();
    private ExternalSignInConfiguration? current;

    public bool OwnerHasSaved
    {
        get
        {
            using var scope = scopes.CreateScope();
            return scope.ServiceProvider.GetRequiredService<HortiplannerDbContext>().ExternalSignInSettings.Any();
        }
    }

    public ExternalSignInConfiguration Current
    {
        get
        {
            lock (gate)
            {
                if (current is not null)
                {
                    return current;
                }

                try
                {
                    current = Load();
                    return current;
                }
                catch (Exception exception) when (exception is NpgsqlException or TimeoutException)
                {
                    // Worth trying again next time. Hortiplanner's own sign-in doesn't depend on this.
                    return FromDeployment();
                }
            }
        }
    }

    public string Protect(string secret) => protector.Protect(secret);

    /// <summary>Picks up what was just saved. The sign-in handler rebuilds itself on the next request.</summary>
    public void Refresh()
    {
        lock (gate)
        {
            current = null;
        }

        handlerOptions.TryRemove(AccountServices.OidcScheme);
    }

    private ExternalSignInConfiguration Load()
    {
        using var scope = scopes.CreateScope();
        var saved = scope.ServiceProvider.GetRequiredService<HortiplannerDbContext>().ExternalSignInSettings.AsNoTracking().FirstOrDefault();

        return saved is null
            ? FromDeployment()
            : new ExternalSignInConfiguration(saved.Enabled, saved.Authority, saved.ClientId, Unprotect(saved.ProtectedClientSecret), saved.DisplayName, saved.OwnerGroup);
    }

    private ExternalSignInConfiguration FromDeployment() =>
        new(
            !string.IsNullOrEmpty(configuration["Oidc:Authority"]),
            configuration["Oidc:Authority"],
            configuration["Oidc:ClientId"],
            configuration["Oidc:ClientSecret"],
            configuration["Oidc:DisplayName"] ?? DefaultDisplayName,
            configuration["Oidc:OwnerGroup"]);

    private string? Unprotect(string? value)
    {
        try
        {
            return value is null ? null : protector.Unprotect(value);
        }
        catch (CryptographicException)
        {
            // Restored without its keys: it reads as not set, and the owner can enter it again.
            return null;
        }
    }
}
