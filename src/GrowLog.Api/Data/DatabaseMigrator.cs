using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Data;

/// <summary>Brings the database up to date when the application really starts, but not for build-time tools.</summary>
internal sealed class DatabaseMigrator(IServiceScopeFactory scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<GrowLogDbContext>().Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
