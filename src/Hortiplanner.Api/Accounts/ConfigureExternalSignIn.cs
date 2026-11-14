using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace Hortiplanner.Api.Accounts;

/// <summary>Fills in the sign-in handler from the settings in force each time it is rebuilt.</summary>
internal sealed class ConfigureExternalSignIn(ExternalSignInStore store) : IConfigureNamedOptions<OpenIdConnectOptions>
{
    // The handler runs on every request and refuses to start without these. Nothing is sent here:
    // while sign-in is off, the endpoints turn people away before the handler is ever asked to act.
    private const string PlaceholderAuthority = "https://sign-in-is-off.invalid/";
    private const string PlaceholderClient = "sign-in-is-off";

    public void Configure(string? name, OpenIdConnectOptions options)
    {
        if (!string.Equals(name, AccountServices.OidcScheme, StringComparison.Ordinal))
        {
            return;
        }

        var settings = store.Current;
        options.Authority = settings.IsOn ? settings.Authority : PlaceholderAuthority;
        options.ClientId = settings.IsOn ? settings.ClientId : PlaceholderClient;
        options.ClientSecret = settings.IsOn ? settings.ClientSecret : PlaceholderClient;
    }

    public void Configure(OpenIdConnectOptions options) => Configure(Options.DefaultName, options);
}
