using Hortiplanner.Api.Accounts;
using Hortiplanner.Api.Assistant;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hortiplanner.Tests;

/// <summary>
/// Hortiplanner was called GrowLog. Saved keys and the secrets they protect were made under the old name, so these
/// names must never be tidied up, or the saved OpenRouter key, staff-login secret and sign-ins become unreadable.
/// </summary>
public sealed class OldNameTests(PostgresFixture database)
{
    [Fact]
    public async Task Saved_secrets_are_still_read_with_the_old_name()
    {
        await using var factory = new TestApp(("ConnectionStrings:Hortiplanner", database.ConnectionString));
        var services = factory.Services;
        var protection = services.GetRequiredService<IDataProtectionProvider>();

        Assert.Equal("GrowLog", services.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator);

        var openRouterKey = protection.CreateProtector("GrowLog.Assistant.OpenRouterKey").Protect("sk-or-saved");
        Assert.Equal("sk-or-saved", new AssistantKeys(protection).Read(openRouterKey));

        var clientSecret = services.GetRequiredService<ExternalSignInStore>().Protect("saved-secret");
        Assert.Equal("saved-secret", protection.CreateProtector("GrowLog.ExternalSignIn.ClientSecret").Unprotect(clientSecret));
    }
}
