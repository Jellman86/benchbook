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

    [Fact]
    public async Task Readiness_fails_when_the_database_is_unreachable_but_liveness_still_answers()
    {
        await using var factory = new TestApp(("ConnectionStrings:GrowLog", "Host=127.0.0.1;Port=1;Database=unreachable;Username=test;Password=test;Timeout=1"))
        {
            ConfigureServices = services => services.Remove(services.Single(service => service.ImplementationType == typeof(GrowLog.Api.Data.DatabaseMigrator))),
        };
        using var client = factory.CreateClient();
        using var ready = await client.GetAsync(new Uri("/api/health", UriKind.Relative), TestContext.Current.CancellationToken);
        using var live = await client.GetAsync(new Uri("/api/health/live", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

}
