using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using static GrowLog.Tests.AccountHarness;

namespace GrowLog.Tests;

public sealed class ExternalSignInTests(PostgresFixture database)
{
    private const string Issuer = "https://idp.test/";
    private const string ClientId = "growlog";
    private const string OwnersGroup = "GrowLog owners";
    private const string Login = "/api/account/external/login";

    /// <summary>A stand-in identity provider that signs real tokens, so the whole handshake runs.</summary>
    private sealed class Provider : HttpMessageHandler
    {
        private readonly RSA rsa = RSA.Create(2048);

        public string Nonce { get; set; } = string.Empty;

        public string Subject { get; set; } = "staff-1";

        public string? Name { get; set; } = "Jo Reed";

        public string[] Groups { get; set; } = [];

        public bool Down { get; set; }

        public string? ClaimedIssuer { get; set; }

        private RsaSecurityKey Key => new(rsa) { KeyId = "test" };

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                rsa.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Down)
            {
                throw new HttpRequestException("Unreachable.");
            }

            var path = request.RequestUri!.AbsolutePath;
            var body = path switch
            {
                "/.well-known/openid-configuration" => $$"""
                    {"issuer":"{{ClaimedIssuer ?? $"https://{request.RequestUri!.Host}/"}}","authorization_endpoint":"{{Issuer}}authorize","token_endpoint":"{{Issuer}}token","jwks_uri":"{{Issuer}}jwks"}
                    """,
                "/jwks" => JsonSerializer.Serialize(new { keys = new[] { JsonWebKeyConverter.ConvertFromRSASecurityKey(Key) } }),
                _ => JsonSerializer.Serialize(new { id_token = Token(), access_token = "unused", token_type = "Bearer" }),
            };

            return Task.FromResult(Reply(body));
        }

        private static HttpResponseMessage Reply(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        private string Token()
        {
            var claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["sub"] = Subject,
                ["nonce"] = Nonce,
                ["groups"] = Groups,
            };

            if (Name is not null)
            {
                claims["name"] = Name;
            }

            return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = Issuer,
                Audience = ClientId,
                Expires = DateTime.UtcNow.AddMinutes(5),
                Claims = claims,
                SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.RsaSha256),
            });
        }
    }

    private static async Task<TestApp> CreateAsync(PostgresFixture database, Provider provider, bool enabled = true)
    {
        var connectionString = await database.CreateDatabaseAsync();

        return new TestApp(
            ("ConnectionStrings:GrowLog", connectionString),
            ("Passkeys:ServerDomain", "localhost"),
            ("Setup:Token", SetupToken),
            ("Demo:Enabled", "false"),
            ("Oidc:Authority", enabled ? Issuer : null),
            ("Oidc:ClientId", ClientId),
            ("Oidc:ClientSecret", "test-secret"),
            ("Oidc:OwnerGroup", OwnersGroup))
        {
            OutsideWorld = provider,
            ConfigureServices = services => services.Configure<OpenIdConnectOptions>("Oidc", options => options.BackchannelHttpHandler = provider),
        };
    }

    private static HttpClient Browser(TestApp app) =>
        app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    /// <summary>Follows the round trip a browser makes, and gives where it finally lands.</summary>
    private static async Task<string> GoThroughProviderAsync(HttpClient browser, Provider provider, string start = Login)
    {
        using var challenge = await browser.GetAsync(Route(start), Cancellation);
        var location = challenge.Headers.Location!;

        if (!location.ToString().StartsWith(Issuer, StringComparison.Ordinal))
        {
            return location.ToString();
        }

        var query = QueryHelpers.ParseQuery(location.Query);
        provider.Nonce = query["nonce"].ToString();
        Assert.Equal("https://localhost/signin-oidc", query["redirect_uri"].ToString());

        using var callback = await browser.GetAsync(Route($"/signin-oidc?code=abc&state={Uri.EscapeDataString(query["state"].ToString())}"), Cancellation);
        using var complete = await browser.GetAsync(callback.Headers.Location!, Cancellation);
        return complete.Headers.Location!.ToString();
    }

    private static async Task<JsonElement> MeAsync(HttpClient browser) =>
        await browser.GetFromJsonAsync<JsonElement>(Route("/api/account/me"), Cancellation);

    private static bool IsOwner(JsonElement me) => me.GetProperty("isOwner").GetBoolean();

    private static async Task<List<JsonElement>> StaffAsync(HttpClient owner) =>
        [.. (await owner.GetFromJsonAsync<JsonElement>(Route("/api/staff"), Cancellation)).EnumerateArray()];

    [Fact]
    public async Task A_new_member_of_staff_is_added_and_signed_in_without_owner_rights()
    {
        var provider = new Provider();
        await using var app = await CreateAsync(database, provider);
        using var owner = Browser(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);

        using var browser = Browser(app);

        Assert.Equal("/", await GoThroughProviderAsync(browser, provider));
        var me = await MeAsync(browser);
        Assert.Equal("Jo Reed", me.GetProperty("name").GetString());
        Assert.False(IsOwner(me));
    }

    [Fact]
    public async Task Coming_back_finds_the_same_account_even_after_a_change_of_name()
    {
        var provider = new Provider();
        await using var app = await CreateAsync(database, provider);
        using var owner = Browser(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);

        using (var first = Browser(app))
        {
            await GoThroughProviderAsync(first, provider);
        }

        provider.Name = "Josephine Reed";
        using var second = Browser(app);
        await GoThroughProviderAsync(second, provider);

        Assert.Equal("Jo Reed", (await MeAsync(second)).GetProperty("name").GetString());
        Assert.Equal(2, (await StaffAsync(owner)).Count);
    }

    [Fact]
    public async Task A_name_already_in_use_is_never_taken_over()
    {
        var provider = new Provider { Name = "Sam Fielding", Groups = [OwnersGroup] };
        await using var app = await CreateAsync(database, provider);
        using var owner = Browser(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);

        using var attacker = Browser(app);

        Assert.Equal("/?sso=name-taken", await GoThroughProviderAsync(attacker, provider));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeStatusAsync(attacker));
    }

    [Fact]
    public async Task Being_in_the_owners_group_gives_owner_rights_and_leaving_it_takes_them_away()
    {
        var provider = new Provider { Groups = [OwnersGroup] };
        await using var app = await CreateAsync(database, provider);
        using var owner = Browser(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);

        using var browser = Browser(app);
        await GoThroughProviderAsync(browser, provider);
        Assert.True(IsOwner(await MeAsync(browser)));

        provider.Groups = [];
        using var later = Browser(app);
        await GoThroughProviderAsync(later, provider);
        Assert.False(IsOwner(await MeAsync(later)));
    }

    [Fact]
    public async Task The_last_owner_stays_an_owner_outside_the_group()
    {
        var provider = new Provider { Name = "Sam on Authentik" };
        await using var app = await CreateAsync(database, provider);
        using var owner = Browser(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);

        Assert.Equal("/", await GoThroughProviderAsync(owner, provider, "/api/account/external/link"));

        using var browser = Browser(app);
        await GoThroughProviderAsync(browser, provider);

        Assert.True(IsOwner(await MeAsync(browser)));
    }

    [Fact]
    public async Task Connecting_a_login_uses_the_account_already_here()
    {
        var provider = new Provider { Name = "Sam on Authentik" };
        await using var app = await CreateAsync(database, provider);
        using var owner = Browser(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);

        Assert.Equal("/", await GoThroughProviderAsync(owner, provider, "/api/account/external/link"));

        using var browser = Browser(app);
        await GoThroughProviderAsync(browser, provider);

        Assert.Equal("Sam Fielding", (await MeAsync(browser)).GetProperty("name").GetString());
        Assert.Single(await StaffAsync(owner));
    }

    [Fact]
    public async Task A_login_can_only_be_connected_to_one_account()
    {
        var provider = new Provider();
        await using var app = await CreateAsync(database, provider);
        using var owner = Browser(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);

        using var staff = Browser(app);
        await GoThroughProviderAsync(staff, provider);

        Assert.Equal("/?sso=already-linked", await GoThroughProviderAsync(owner, provider, "/api/account/external/link"));
    }

    [Fact]
    public async Task Someone_who_has_left_is_refused()
    {
        var provider = new Provider();
        await using var app = await CreateAsync(database, provider);
        using var owner = Browser(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);

        using (var first = Browser(app))
        {
            await GoThroughProviderAsync(first, provider);
        }

        var staffId = (await StaffAsync(owner)).Single(member => string.Equals(member.GetProperty("name").GetString(), "Jo Reed", StringComparison.Ordinal)).GetProperty("id").GetGuid();
        await PostStatusAsync(owner, $"/api/staff/{staffId}/archive");

        using var browser = Browser(app);

        Assert.Equal("/?sso=refused", await GoThroughProviderAsync(browser, provider));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeStatusAsync(browser));
    }

    [Fact]
    public async Task A_forged_return_from_the_provider_signs_nobody_in()
    {
        var provider = new Provider();
        await using var app = await CreateAsync(database, provider);
        using var owner = Browser(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);

        using var browser = Browser(app);
        using var forged = await browser.GetAsync(Route("/signin-oidc?code=abc&state=forged"), Cancellation);

        Assert.Equal("/?sso=failed", forged.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, await MeStatusAsync(browser));
    }

    [Fact]
    public async Task The_first_account_cannot_be_made_this_way()
    {
        var provider = new Provider { Groups = [OwnersGroup] };
        await using var app = await CreateAsync(database, provider);
        using var browser = Browser(app);

        Assert.Equal("/?sso=failed", await GoThroughProviderAsync(browser, provider));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeStatusAsync(browser));
    }

    [Fact]
    public async Task With_the_provider_down_the_person_is_told_and_local_sign_in_still_works()
    {
        var provider = new Provider { Down = true };
        await using var app = await CreateAsync(database, provider);
        using var owner = Browser(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);

        using var browser = Browser(app);

        Assert.Equal("/?sso=unavailable", await GoThroughProviderAsync(browser, provider));
        Assert.Equal(HttpStatusCode.NoContent, await SignInAsync(browser, key));
    }

    [Fact]
    public async Task With_nothing_configured_it_says_so_and_goes_nowhere()
    {
        var provider = new Provider();
        await using var app = await CreateAsync(database, provider, enabled: false);
        using var browser = Browser(app);

        var options = await browser.GetFromJsonAsync<JsonElement>(Route("/api/account/external"), Cancellation);

        Assert.False(options.GetProperty("enabled").GetBoolean());
        Assert.Equal("/?sso=unavailable", await GoThroughProviderAsync(browser, provider));
    }

    private static object Settings(bool enabled = true, string authority = Issuer, string? secret = "a-new-secret") =>
        new { enabled, authority, clientId = ClientId, clientSecret = secret, displayName = "Croft View", ownerGroup = OwnersGroup };

    private static async Task<HttpResponseMessage> SaveAsync(HttpClient owner, object settings) =>
        await owner.PutAsJsonAsync(Route("/api/external-sign-in"), settings, Cancellation);

    private static async Task<string> RefusalAsync(HttpClient owner, object settings)
    {
        using var response = await SaveAsync(owner, settings);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("problem").GetString()!;
    }

    [Fact]
    public async Task The_owner_can_turn_it_on_with_no_restart()
    {
        var provider = new Provider();
        await using var app = await CreateAsync(database, provider, enabled: false);
        using var owner = Browser(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);

        using (var saved = await SaveAsync(owner, Settings()))
        {
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        }

        using var browser = Browser(app);
        var options = await browser.GetFromJsonAsync<JsonElement>(Route("/api/account/external"), Cancellation);
        Assert.True(options.GetProperty("enabled").GetBoolean());
        Assert.Equal("Croft View", options.GetProperty("name").GetString());
        Assert.Equal("/", await GoThroughProviderAsync(browser, provider));
        Assert.Equal("Jo Reed", (await MeAsync(browser)).GetProperty("name").GetString());
    }

    [Fact]
    public async Task The_owner_can_turn_it_off_with_no_restart()
    {
        var provider = new Provider();
        await using var app = await CreateAsync(database, provider);
        using var owner = Browser(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);

        using var saved = await SaveAsync(owner, Settings(enabled: false));

        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        using var browser = Browser(app);
        Assert.Equal("/?sso=unavailable", await GoThroughProviderAsync(browser, provider));
    }

    [Fact]
    public async Task The_secret_is_never_shown_and_is_stored_encrypted()
    {
        var provider = new Provider();
        await using var app = await CreateAsync(database, provider, enabled: false);
        using var owner = Browser(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);
        using var saved = await SaveAsync(owner, Settings(secret: "a-very-private-secret"));

        var shown = await owner.GetStringAsync(Route("/api/external-sign-in"), Cancellation);

        Assert.DoesNotContain("a-very-private-secret", shown, StringComparison.Ordinal);
        Assert.True(Json(shown).GetProperty("hasClientSecret").GetBoolean());

        using var scope = app.Services.CreateScope();
        var row = scope.ServiceProvider.GetRequiredService<GrowLog.Api.Data.GrowLogDbContext>().ExternalSignInSettings.Single();
        Assert.DoesNotContain("a-very-private-secret", row.ProtectedClientSecret!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Leaving_the_secret_blank_keeps_it()
    {
        var provider = new Provider();
        await using var app = await CreateAsync(database, provider);
        using var owner = Browser(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);

        using var saved = await SaveAsync(owner, Settings(secret: null));

        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var shown = await owner.GetFromJsonAsync<JsonElement>(Route("/api/external-sign-in"), Cancellation);
        Assert.True(shown.GetProperty("hasClientSecret").GetBoolean());
        Assert.False(shown.GetProperty("fromDeployment").GetBoolean());
    }

    [Fact]
    public async Task Bad_settings_are_refused_and_change_nothing()
    {
        var provider = new Provider();
        await using var app = await CreateAsync(database, provider, enabled: false);
        using var owner = Browser(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);

        Assert.Equal("incomplete", await RefusalAsync(owner, Settings(authority: "http://idp.test/")));

        provider.ClaimedIssuer = "https://someone-else.test/";
        Assert.Equal("wrong-issuer", await RefusalAsync(owner, Settings()));

        provider.ClaimedIssuer = null;
        provider.Down = true;
        Assert.Equal("unreachable", await RefusalAsync(owner, Settings()));

        var options = await owner.GetFromJsonAsync<JsonElement>(Route("/api/account/external"), Cancellation);
        Assert.False(options.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Changing_provider_disconnects_everyone_from_the_old_one()
    {
        var provider = new Provider();
        await using var app = await CreateAsync(database, provider);
        using var owner = Browser(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);
        using (var staff = Browser(app))
        {
            await GoThroughProviderAsync(staff, provider);
        }

        Assert.Contains(await StaffAsync(owner), member => member.GetProperty("hasExternalLogin").GetBoolean());

        using var saved = await SaveAsync(owner, Settings(authority: "https://other.test/"));

        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        Assert.DoesNotContain(await StaffAsync(owner), member => member.GetProperty("hasExternalLogin").GetBoolean());
    }

    [Fact]
    public async Task It_cannot_be_turned_off_if_that_would_lock_every_owner_out()
    {
        var provider = new Provider { Groups = [OwnersGroup] };
        await using var app = await CreateAsync(database, provider);
        using var first = Browser(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(first, key);
        using var second = Browser(app);
        await GoThroughProviderAsync(second, provider);
        var firstId = (await StaffAsync(second)).Single(member => string.Equals(member.GetProperty("name").GetString(), "Sam Fielding", StringComparison.Ordinal)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(second, $"/api/staff/{firstId}/archive"));

        Assert.Equal("last-way-in", await RefusalAsync(second, Settings(enabled: false)));
    }

    [Fact]
    public async Task Only_the_owner_can_see_or_change_this()
    {
        var provider = new Provider();
        await using var app = await CreateAsync(database, provider);
        using var owner = Browser(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);
        using var staff = Browser(app);
        await GoThroughProviderAsync(staff, provider);
        using var nobody = Browser(app);

        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync(staff, "/api/external-sign-in"));
        using var refused = await SaveAsync(staff, Settings());
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(nobody, "/api/external-sign-in"));
    }

    [Fact]
    public async Task The_redirect_address_to_give_the_provider_is_shown()
    {
        var provider = new Provider();
        await using var app = await CreateAsync(database, provider);
        using var owner = Browser(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);

        var shown = await owner.GetFromJsonAsync<JsonElement>(Route("/api/external-sign-in"), Cancellation);

        Assert.Equal("https://localhost/signin-oidc", shown.GetProperty("redirectUri").GetString());
        Assert.True(shown.GetProperty("fromDeployment").GetBoolean());
    }

    [Fact]
    public async Task Changing_the_provider_while_disabled_clears_old_associations()
    {
        var provider = new Provider();
        await using var app = await CreateAsync(database, provider);
        using var owner = Browser(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);
        using var staff = Browser(app);
        Assert.Equal("/", await GoThroughProviderAsync(staff, provider));
        using var disabled = await SaveAsync(owner, Settings(enabled: false));
        Assert.Equal(HttpStatusCode.NoContent, disabled.StatusCode);
        using var changed = await SaveAsync(owner, Settings(enabled: false, authority: "https://another-provider.test/"));
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        using var scope = app.Services.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<GrowLog.Api.Data.GrowLogDbContext>().UserLogins);
    }

    [Fact]
    public async Task A_login_linked_before_issuers_were_recorded_finds_the_same_account()
    {
        var provider = new Provider();
        await using var app = await CreateAsync(database, provider);
        using var owner = Browser(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);

        using (var first = Browser(app))
        {
            Assert.Equal("/", await GoThroughProviderAsync(first, provider));
        }

        // Older versions stored only the subject as the login's key.
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GrowLog.Api.Data.GrowLogDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE \"AspNetUserLogins\" SET provider_key = {0}", [provider.Subject], Cancellation);
        }

        using var second = Browser(app);
        Assert.Equal("/", await GoThroughProviderAsync(second, provider));
        Assert.Equal("Jo Reed", (await MeAsync(second)).GetProperty("name").GetString());
        Assert.Equal(2, (await StaffAsync(owner)).Count);

        using var check = app.Services.CreateScope();
        var logins = check.ServiceProvider.GetRequiredService<GrowLog.Api.Data.GrowLogDbContext>().UserLogins.ToList();
        Assert.NotEqual(provider.Subject, Assert.Single(logins).ProviderKey, StringComparer.Ordinal);
    }
}
