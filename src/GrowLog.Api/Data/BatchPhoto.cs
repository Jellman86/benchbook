using GrowLog.Api.Production;

namespace GrowLog.Api.Data;

/// <summary>A photo of a seed packet, label, packaging or the plants. Kept in the database so it is covered by the same backups.</summary>
internal sealed class BatchPhoto
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid BatchId { get; init; }

    public required BatchPhotoKind Kind { get; init; }

    public required string ContentType { get; init; }

    public required byte[] Content { get; init; }

    public required Guid UploadedBy { get; init; }

    /// <summary>The tray or pot it was taken at, when taken from one.</summary>
    public Guid? ContainerId { get; init; }

    /// <summary>The stage that tray had reached when the photo was taken, so the strip reads as the plants grew.</summary>
    public ProductionStage? Stage { get; init; }

    public DateTimeOffset CreatedAt { get; private set; }
}
