using GrowLog.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace GrowLog.Api.Accounts;

internal static class AccountServices
{
    public const string OwnerRole = "Owner";
    public const string OwnerPolicy = "Owner";
    public const string BenchPolicy = "BenchStation";
    public const string BenchScheme = "BenchStation";

    /// <summary>Marks a session started with a PIN at a bench station, which never carries the owner's powers.</summary>
    public const string BenchSessionClaim = "growlog:bench_session";
    public const string OidcScheme = "Oidc";

    /// <summary>The address people use, which this application can't tell from behind the proxy.</summary>
    public static string PublicOrigin(IConfiguration configuration)
    {
        var domain = configuration["Passkeys:ServerDomain"]
            ?? throw new InvalidOperationException("Passkeys:ServerDomain is not configured.");

        return configuration["Passkeys:Origin"] ?? $"https://{domain}";
    }

    public static void ConfigureIdentity(IdentityOptions options)
    {
        options.User.AllowedUserNameCharacters = string.Empty;

        // Length is what makes a password strong. Forced symbols only make it harder to remember.
        options.Password.RequiredLength = 8;
        options.Password.RequiredUniqueChars = 1;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;

        // A short PIN is only safe with a firm limit on guesses.
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

        // Version 3 adds the passkey table.
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    }

    /// <summary>Leaving, or a stricter sign-in level, ends sessions within a minute rather than half an hour.</summary>
    private static void CheckSessionsOften(SecurityStampValidatorOptions options)
    {
        options.ValidationInterval = TimeSpan.FromMinutes(1);

        // A bench session stays a bench session when its cookie is renewed.
        options.OnRefreshingPrincipal = context =>
        {
            if (context.CurrentPrincipal?.FindFirst(BenchSessionClaim) is { } bench)
            {
                context.NewPrincipal?.Identities.First().AddClaim(new System.Security.Claims.Claim(bench.Type, bench.Value));
            }

            return Task.CompletedTask;
        };
    }

    public static IServiceCollection AddGrowLogAccounts(this IServiceCollection services)
    {
        // Kept in the database, so a redeploy of the container does not sign everyone out.
        services.AddDataProtection().SetApplicationName("GrowLog").PersistKeysToDbContext<GrowLogDbContext>();

        var authentication = services.AddAuthentication(IdentityConstants.ApplicationScheme);
        authentication.AddIdentityCookies();
        authentication.AddCookie(BenchScheme, options =>
        {
            options.Cookie.Name = "growlog.bench";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromDays(365);
            options.SlidingExpiration = true;
            AnswerWithStatusCodes(options);
        });

        AddExternalSignIn(authentication, services);

        services.Configure<SecurityStampValidatorOptions>(CheckSessionsOften);

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "growlog.session";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromDays(30);
            options.SlidingExpiration = true;
            AnswerWithStatusCodes(options);
        });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(OwnerPolicy, policy => policy
                .RequireRole(OwnerRole)
                .RequireAssertion(context => context.User.FindFirst(BenchSessionClaim) is null))
            .AddPolicy(BenchPolicy, policy => policy.AddAuthenticationSchemes(BenchScheme).RequireAuthenticatedUser());

        services.AddIdentityCore<AppUser>(ConfigureIdentity)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<GrowLogDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromDays(7));

        services.AddOptions<IdentityPasskeyOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            var origin = PublicOrigin(configuration);

            options.ServerDomain = configuration["Passkeys:ServerDomain"];

            // Compared with the configured address, so this does not depend on proxy headers.
            options.ValidateOrigin = context => ValueTask.FromResult(
                !context.CrossOrigin && string.Equals(context.Origin, origin, StringComparison.Ordinal));
        });

        return services;
    }

    /// <summary>Always registered, because the owner can turn it on at any time. It does nothing until they do.</summary>
    private static void AddExternalSignIn(AuthenticationBuilder authentication, IServiceCollection services)
    {
        services.AddSingleton<ExternalSignInStore>();
        services.AddSingleton<IConfigureOptions<OpenIdConnectOptions>, ConfigureExternalSignIn>();
        services.AddHttpClient(ExternalSignInSettingsEndpoints.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(8));

        services.ConfigureExternalCookie(options =>
        {
            options.Cookie.Name = "growlog.external";
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        });

        authentication.AddOpenIdConnect(OidcScheme, options =>
        {
            options.ResponseType = OpenIdConnectResponseType.Code;

            // Returned in the address, so Lax cookies are enough for the trip back.
            options.ResponseMode = OpenIdConnectResponseMode.Query;
            options.CorrelationCookie.SameSite = SameSiteMode.Lax;
            options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
            options.NonceCookie.SameSite = SameSiteMode.Lax;
            options.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;

            options.SignInScheme = IdentityConstants.ExternalScheme;
            options.MapInboundClaims = false;
            options.GetClaimsFromUserInfoEndpoint = false;
            options.Scope.Add("profile");
            options.BackchannelTimeout = TimeSpan.FromSeconds(10);
            options.DisableTelemetry = true;
            options.Events.OnRemoteFailure = context =>
            {
                context.Response.Redirect("/?sso=failed");
                context.HandleResponse();
                return Task.CompletedTask;
            };
        });
    }

    /// <summary>This is an API, so it answers with a status code rather than redirecting to a page.</summary>
    private static void AnswerWithStatusCodes(CookieAuthenticationOptions options)
    {
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    }
}
