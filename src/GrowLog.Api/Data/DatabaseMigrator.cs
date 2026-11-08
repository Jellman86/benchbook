using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrowLog.Api.Data;

/// <summary>Brings the database up to date when the application starts, waiting for it if it is still starting too.</summary>
internal sealed partial class DatabaseMigrator(IServiceScopeFactory scopes, ILogger<DatabaseMigrator> logger) : IHostedService
{
    private const int MaxAttempts = 30;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<GrowLogDbContext>().Database.MigrateAsync(cancellationToken);
                return;
            }
            catch (NpgsqlException exception) when (attempt < MaxAttempts)
            {
                LogNotReady(logger, exception, attempt);
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Warning, Message = "The database is not ready yet (attempt {Attempt}).")]
    private static partial void LogNotReady(ILogger logger, Exception exception, int attempt);
}
