using GrowLog.Api.Data;
using GrowLog.Tests;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

[assembly: AssemblyFixture(typeof(PostgresFixture))]

namespace GrowLog.Tests;

/// <summary>One throwaway PostgreSQL server for the whole test run. Needs Docker.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string Image =
        "postgres:18.6-alpine@sha256:123b17998ccaf47e53b502e8c6a1e79afd8f579c6f3eec83c563da026f957395";

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder(Image).Build();

    public string ConnectionString => container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await container.StartAsync(TestContext.Current.CancellationToken);

        await using var db = CreateContext();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => container.DisposeAsync();

    internal GrowLogDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<GrowLogDbContext>();
        GrowLogDbContext.Configure(options, ConnectionString);
        return new GrowLogDbContext(options.Options);
    }
}
