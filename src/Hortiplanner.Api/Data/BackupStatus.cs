namespace Hortiplanner.Api.Data;

/// <summary>A single row kept up to date by the backup service, so the application can warn the owner.</summary>
internal sealed class BackupStatus
{
    public int Id { get; init; } = 1;

    public DateTimeOffset? LastDumpAt { get; set; }

    public DateTimeOffset? LastTransferAt { get; set; }
}
