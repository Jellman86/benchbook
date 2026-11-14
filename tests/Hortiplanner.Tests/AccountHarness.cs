using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Hortiplanner.Tests;

/// <summary>Drives the account endpoints over HTTP. Each HttpClient acts as a separate device.</summary>
internal static class AccountHarness
{
    public const string SetupToken = "correct-setup-token";

    public static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static async Task<TestApp> CreateAppAsync(
        PostgresFixture database,
        string? setupToken = SetupToken,
        TimeProvider? clock = null,
        bool demo = false,
        HttpMessageHandler? outsideWorld = null,
        params (string Key, string? Value)[] settings)
    {
        var connectionString = await database.CreateDatabaseAsync();

        return new TestApp(
            [
                ("ConnectionStrings:Hortiplanner", connectionString),
                ("Passkeys:ServerDomain", "localhost"),
                ("Setup:Token", setupToken),
                ("Demo:Enabled", demo ? "true" : "false"),
                .. settings,
            ])
        {
            Clock = clock,
            OutsideWorld = outsideWorld,
        };
    }

    public static HttpClient Client(TestApp app) =>
        app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    public static Uri Route(string path) => new(path, UriKind.Relative);

    public static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object? body = null) =>
        body is null
            ? client.PostAsync(Route(path), content: null, Cancellation)
            : client.PostAsJsonAsync(Route(path), body, Cancellation);

    public static async Task<HttpStatusCode> PostStatusAsync(HttpClient client, string path, object? body = null)
    {
        using var response = await PostAsync(client, path, body);
        return response.StatusCode;
    }

    public static async Task<string> PostForTextAsync(HttpClient client, string path, object? body = null)
    {
        using var response = await PostAsync(client, path, body);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(Cancellation);
    }

    public static Task<string> SetupOptionsAsync(HttpClient client, string token = SetupToken) =>
        PostForTextAsync(client, "/api/setup/passkey-options", new { token, name = "Sam Fielding" });

    public static Task<HttpResponseMessage> CompleteSetupAsync(HttpClient client, string credential, string token = SetupToken) =>
        PostAsync(client, "/api/setup/complete", new { token, credential = Json(credential) });

    public static async Task SetUpOwnerAsync(HttpClient client, TestAuthenticator authenticator)
    {
        var options = await SetupOptionsAsync(client);
        using var response = await CompleteSetupAsync(client, authenticator.Register(options));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    public static async Task<HttpStatusCode> SignInAsync(HttpClient client, TestAuthenticator authenticator)
    {
        var options = await PostForTextAsync(client, "/api/account/passkey-options");
        return await PostStatusAsync(client, "/api/account/sign-in", new { credential = Json(authenticator.Authenticate(options)) });
    }

    public static async Task<HttpStatusCode> GetStatusAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(Route(path), Cancellation);
        return response.StatusCode;
    }

    public static Task<HttpStatusCode> MeStatusAsync(HttpClient client) => GetStatusAsync(client, "/api/account/me");

    /// <summary>Lets the owner choose name-only sign-in for someone: anything but the strict level.</summary>
    public static Task AllowNameOnlyAsync(HttpClient owner) => SetSignInLevelAsync(owner, "Personal");

    public static async Task SetSignInLevelAsync(HttpClient owner, string level)
    {
        using var response = await owner.PutAsJsonAsync(Route("/api/staff/sign-in-settings"), new { level }, Cancellation);
        response.EnsureSuccessStatusCode();
    }
}
