using GrowLog.Api.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace GrowLog.Api.Accounts;

internal static class AccountServices
{
    public const string OwnerRole = "Owner";
    public const string OwnerPolicy = "Owner";
    public const string BenchPolicy = "BenchStation";
    public const string BenchScheme = "BenchStation";

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
            .AddPolicy(OwnerPolicy, policy => policy.RequireRole(OwnerRole))
            .AddPolicy(BenchPolicy, policy => policy.AddAuthenticationSchemes(BenchScheme).RequireAuthenticatedUser());

        services.AddIdentityCore<AppUser>(ConfigureIdentity)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<GrowLogDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromDays(7));

        services.AddOptions<IdentityPasskeyOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            var domain = configuration["Passkeys:ServerDomain"]
                ?? throw new InvalidOperationException("Passkeys:ServerDomain is not configured.");

            var origin = configuration["Passkeys:Origin"] ?? $"https://{domain}";

            options.ServerDomain = domain;

            // Compared with the configured address, so this does not depend on proxy headers.
            options.ValidateOrigin = context => ValueTask.FromResult(
                !context.CrossOrigin && string.Equals(context.Origin, origin, StringComparison.Ordinal));
        });

        return services;
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
