using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using static Hortiplanner.Tests.AccountHarness;

namespace Hortiplanner.Tests;

// Setup only works on an empty system, so each test gets its own database.
public sealed class AccountTests(PostgresFixture database)
{
    private Task<TestApp> CreateAppAsync(string? setupToken = SetupToken) => AccountHarness.CreateAppAsync(database, setupToken);

    private static async Task<bool> NeedsSetupAsync(HttpClient client)
    {
        var status = await client.GetFromJsonAsync<JsonElement>(Route("/api/setup/status"), Cancellation);
        return status.GetProperty("needsSetup").GetBoolean();
    }

    [Fact]
    public async Task An_empty_system_needs_setup()
    {
        await using var app = await CreateAppAsync();
        using var client = Client(app);

        Assert.True(await NeedsSetupAsync(client));
    }

    [Fact]
    public async Task Setup_creates_the_owner_and_signs_them_in()
    {
        await using var app = await CreateAppAsync();
        using var client = Client(app);
        using var authenticator = new TestAuthenticator();

        await SetUpOwnerAsync(client, authenticator);

        var me = await client.GetFromJsonAsync<JsonElement>(Route("/api/account/me"), Cancellation);
        Assert.Equal("Sam Fielding", me.GetProperty("name").GetString());
        Assert.True(me.GetProperty("isOwner").GetBoolean());
        Assert.False(await NeedsSetupAsync(client));
    }

    [Fact]
    public async Task The_owner_can_sign_out_and_back_in_with_their_passkey()
    {
        await using var app = await CreateAppAsync();
        using var client = Client(app);
        using var authenticator = new TestAuthenticator();
        await SetUpOwnerAsync(client, authenticator);

        using var signOut = await PostAsync(client, "/api/account/sign-out");
        Assert.Equal(HttpStatusCode.Unauthorized, await MeStatusAsync(client));

        Assert.Equal(HttpStatusCode.NoContent, await SignInAsync(client, authenticator));
        Assert.Equal(HttpStatusCode.OK, await MeStatusAsync(client));
    }

    [Fact]
    public async Task An_unregistered_passkey_cannot_sign_in()
    {
        await using var app = await CreateAppAsync();
        using var client = Client(app);
        using var owner = new TestAuthenticator();
        using var stranger = new TestAuthenticator();
        await SetUpOwnerAsync(client, owner);
        using var signOut = await PostAsync(client, "/api/account/sign-out");

        Assert.Equal(HttpStatusCode.Unauthorized, await SignInAsync(client, stranger));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeStatusAsync(client));
    }

    [Fact]
    public async Task A_passkey_used_from_another_website_is_refused()
    {
        await using var app = await CreateAppAsync();
        using var client = Client(app);
        using var phishing = new TestAuthenticator("https://evil.example");
        var options = await SetupOptionsAsync(client);

        using var response = await CompleteSetupAsync(client, phishing.Register(options));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await NeedsSetupAsync(client));
    }

    [Fact]
    public async Task Setup_is_refused_with_the_wrong_token()
    {
        await using var app = await CreateAppAsync();
        using var client = Client(app);

        using var response = await client.PostAsJsonAsync(
            Route("/api/setup/passkey-options"),
            new { token = "wrong", name = "Intruder" },
            Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Completing_setup_is_refused_with_the_wrong_token()
    {
        await using var app = await CreateAppAsync();
        using var client = Client(app);
        using var authenticator = new TestAuthenticator();
        var options = await SetupOptionsAsync(client);

        using var response = await CompleteSetupAsync(client, authenticator.Register(options), "wrong");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(await NeedsSetupAsync(client));
    }

    [Fact]
    public async Task Setup_is_closed_when_no_token_is_configured()
    {
        await using var app = await CreateAppAsync(setupToken: null);
        using var client = Client(app);

        using var response = await client.PostAsJsonAsync(
            Route("/api/setup/passkey-options"),
            new { token = string.Empty, name = "Intruder" },
            Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Setup_cannot_be_repeated_once_an_owner_exists()
    {
        await using var app = await CreateAppAsync();
        using var client = Client(app);
        using var authenticator = new TestAuthenticator();
        await SetUpOwnerAsync(client, authenticator);

        using var response = await client.PostAsJsonAsync(
            Route("/api/setup/passkey-options"),
            new { token = SetupToken, name = "Second owner" },
            Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Protected_endpoints_answer_401_rather_than_redirecting()
    {
        await using var app = await CreateAppAsync();
        using var client = Client(app);

        Assert.Equal(HttpStatusCode.Unauthorized, await MeStatusAsync(client));
    }
}
