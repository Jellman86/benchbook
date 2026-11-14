namespace Hortiplanner.Api.Data;

/// <summary>A photo or PDF of a receipt. Kept in the database so it is covered by the same backups.</summary>
internal sealed class PurchaseReceipt
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid PurchaseId { get; init; }

    public required string ContentType { get; init; }

    public required byte[] Content { get; init; }

    public required Guid UploadedBy { get; init; }

    public DateTimeOffset CreatedAt { get; private set; }
}
