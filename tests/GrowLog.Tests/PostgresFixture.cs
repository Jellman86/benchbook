using GrowLog.Api.Accounts;
using GrowLog.Api.Data;
using GrowLog.Tests;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

[assembly: AssemblyFixture(typeof(PostgresFixture))]

namespace GrowLog.Tests;

/// <summary>One throwaway PostgreSQL server for the whole test run. Needs Docker.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string Image =
        "postgres:18.6-alpine@sha256:123b17998ccaf47e53b502e8c6a1e79afd8f579c6f3eec83c563da026f957395";

    // Every test application keeps its own pool of connections, and many run at once.
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder(Image).WithCommand("-c", "max_connections=1000").Build();

    // The Identity tables in the model depend on these options, as they do in the application.
    private readonly ServiceProvider services = new ServiceCollection()
        .Configure<IdentityOptions>(AccountServices.ConfigureIdentity)
        .BuildServiceProvider();

    public string ConnectionString => container.GetConnectionString();

    /// <summary>An existing person, for events to be recorded against.</summary>
    public Guid UserId { get; } = Guid.CreateVersion7();

    public async ValueTask InitializeAsync()
    {
        await container.StartAsync(TestContext.Current.CancellationToken);

        await using var db = CreateContext();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);

        db.Users.Add(new AppUser { Id = UserId, UserName = "Test worker", NormalizedUserName = "TEST WORKER" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Creates an empty database, for tests that need a system with no accounts.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"isolated_{Guid.NewGuid():N}";
        var result = await container.ExecScriptAsync($"CREATE DATABASE {name};", TestContext.Current.CancellationToken);
        Assert.True(result.ExitCode == 0, result.Stderr);

        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync();
        await container.DisposeAsync();
    }

    internal GrowLogDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<GrowLogDbContext>();
        GrowLogDbContext.Configure(options, ConnectionString);
        options.UseApplicationServiceProvider(services);
        return new GrowLogDbContext(options.Options);
    }
}
