using System.Net;
using Xunit;
using static GrowLog.Tests.AccountHarness;

namespace GrowLog.Tests;

public sealed class WebAppTests(PostgresFixture database) : IDisposable
{
    private readonly string webRoot = Directory.CreateTempSubdirectory("growlog-web").FullName;

    public void Dispose() => Directory.Delete(webRoot, recursive: true);

    private async Task<TestApp> CreateAppAsync()
    {
        await File.WriteAllTextAsync(Path.Join(webRoot, "index.html"), "<title>GrowLog page</title>", Cancellation);

        return new TestApp(("ConnectionStrings:GrowLog", database.ConnectionString), ("webroot", webRoot));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/invite")]
    [InlineData("/some/deep/link")]
    public async Task Pages_load_the_web_app_without_signing_in(string path)
    {
        await using var app = await CreateAppAsync();
        using var client = Client(app);

        using var response = await client.GetAsync(Route(path), Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("GrowLog page", await response.Content.ReadAsStringAsync(Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_api_paths_do_not_load_the_web_app()
    {
        await using var app = await CreateAppAsync();
        using var client = Client(app);

        using var response = await client.GetAsync(Route("/api/nothing-here"), Cancellation);

        Assert.DoesNotContain("GrowLog page", await response.Content.ReadAsStringAsync(Cancellation), StringComparison.Ordinal);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
