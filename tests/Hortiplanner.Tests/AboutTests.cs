using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using static Hortiplanner.Tests.AccountHarness;

namespace Hortiplanner.Tests;

public sealed class AboutTests(PostgresFixture database)
{
    private static Task<JsonElement> AboutAsync(HttpClient client) =>
        client.GetFromJsonAsync<JsonElement>(Route("/api/about"), Cancellation);

    [Fact]
    public async Task Staff_see_which_version_this_is_and_where_its_source_is()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);

        var about = await AboutAsync(nursery.Staff);

        Assert.Equal("development", about.GetProperty("version").GetString());
        Assert.Equal(JsonValueKind.Null, about.GetProperty("commit").ValueKind);
        Assert.Equal("https://github.com/Jellman86/hortiplanner", about.GetProperty("sourceUrl").GetString());
        Assert.StartsWith(".NET", about.GetProperty("runtime").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("PostgreSQL 1", about.GetProperty("database").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_built_image_links_to_the_exact_source_it_was_built_from()
    {
        const string commit = "607f38f0c0ffee";
        await using var app = await CreateAppAsync(
            database,
            settings: [("Build:Version", "1.4.0"), ("Build:Commit", commit), ("Build:Date", "2026-11-10T12:00:00Z")]);
        using var owner = Client(app);
        using var authenticator = new TestAuthenticator();
        await SetUpOwnerAsync(owner, authenticator);

        var about = await AboutAsync(owner);

        Assert.Equal("1.4.0", about.GetProperty("version").GetString());
        Assert.Equal(commit, about.GetProperty("commit").GetString());
        Assert.Equal($"https://github.com/Jellman86/hortiplanner/tree/{commit}", about.GetProperty("sourceUrl").GetString());
        Assert.Equal(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero), about.GetProperty("builtAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Someone_not_signed_in_is_refused()
    {
        await using var app = await CreateAppAsync(database);
        using var client = Client(app);

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(client, "/api/about"));
    }
}
