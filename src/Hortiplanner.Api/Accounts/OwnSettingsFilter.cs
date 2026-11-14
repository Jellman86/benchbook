using Microsoft.AspNetCore.Identity;

namespace Hortiplanner.Api.Accounts;

/// <summary>
/// Changing how someone signs in needs their own session: never a bench session, which a PIN or a
/// tapped name opened, and never the shared nursery team account. Otherwise a bench could be turned
/// into the owner's account by setting a password.
/// </summary>
internal sealed class OwnSettingsFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var user = context.HttpContext.User;

        if (user.FindFirst(AccountServices.BenchSessionClaim) is not null)
        {
            return TypedResults.Forbid();
        }

        var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();

        if (await users.GetUserAsync(user) is { IsTeamAccount: true })
        {
            return TypedResults.Forbid();
        }

        return await next(context);
    }
}
