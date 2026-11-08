using System.Net;
using Xunit;

namespace GrowLog.Tests;

public sealed class HealthEndpointTests(PostgresFixture database)
{
    [Fact]
    public async Task Health_returns_ok()
    {
        await using var factory = new TestApp(("ConnectionStrings:GrowLog", database.ConnectionString));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            new Uri("/api/health", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
