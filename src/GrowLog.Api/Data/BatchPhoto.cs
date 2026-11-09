namespace GrowLog.Api.Data;

/// <summary>A photo of a seed packet, label or packaging. Kept in the database so it is covered by the same backups.</summary>
internal sealed class BatchPhoto
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid BatchId { get; init; }

    public required BatchPhotoKind Kind { get; init; }

    public required string ContentType { get; init; }

    public required byte[] Content { get; init; }

    public required Guid UploadedBy { get; init; }

    public DateTimeOffset CreatedAt { get; private set; }
}
