using GrowLog.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace GrowLog.Api.Accounts;

internal static class AccountServices
{
    public const string OwnerRole = "Owner";

    public static void ConfigureIdentity(IdentityOptions options)
    {
        options.User.AllowedUserNameCharacters = string.Empty;

        // Version 3 adds the passkey table.
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    }

    public static IServiceCollection AddGrowLogAccounts(this IServiceCollection services)
    {
        // Kept in the database, so a redeploy of the container does not sign everyone out.
        services.AddDataProtection().SetApplicationName("GrowLog").PersistKeysToDbContext<GrowLogDbContext>();

        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "growlog.session";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromDays(30);
            options.SlidingExpiration = true;

            // This is an API, so answer with a status code rather than redirecting to a page.
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
        });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddIdentityCore<AppUser>(ConfigureIdentity)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<GrowLogDbContext>()
            .AddSignInManager();

        services.AddOptions<IdentityPasskeyOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            var domain = configuration["Passkeys:ServerDomain"]
                ?? throw new InvalidOperationException("Passkeys:ServerDomain is not configured.");

            options.ServerDomain = domain;

            // Compared with the configured address, so this does not depend on proxy headers.
            options.ValidateOrigin = context => ValueTask.FromResult(
                !context.CrossOrigin && string.Equals(context.Origin, $"https://{domain}", StringComparison.Ordinal));
        });

        return services;
    }
}
