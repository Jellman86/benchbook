using Hortiplanner.Api.Accounts;
using Hortiplanner.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Operations;

internal static class BackupStatusEndpoints
{
    // Both jobs run hourly and report on each other's schedule, so allow for that and a restart.
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(150);

    public static void MapBackupStatusEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/backups/status", GetStatusAsync).RequireAuthorization(AccountServices.OwnerPolicy);

    private static async Task<BackupHealth> GetStatusAsync(HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var status = await db.BackupStatus.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

        if (status is null)
        {
            var firstAccount = await db.Users.MinAsync(person => (DateTimeOffset?)person.CreatedAt, cancellationToken);
            var inGrace = firstAccount is null || clock.GetUtcNow() - firstAccount < TimeSpan.FromHours(24);
            return new BackupHealth(Known: false, Healthy: inGrace, LastDumpAt: null, LastTransferAt: null);
        }

        var oldest = clock.GetUtcNow() - Limit;
        var healthy = status.LastDumpAt >= oldest && status.LastTransferAt >= oldest;

        return new BackupHealth(Known: true, healthy, status.LastDumpAt, status.LastTransferAt);
    }

    internal sealed record BackupHealth(bool Known, bool Healthy, DateTimeOffset? LastDumpAt, DateTimeOffset? LastTransferAt);
}
