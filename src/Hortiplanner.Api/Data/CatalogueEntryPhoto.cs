namespace Hortiplanner.Api.Data;

/// <summary>A photo of a pot size or a place. Kept in the database so it is covered by the same backups.</summary>
internal sealed class CatalogueEntryPhoto
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required CataloguePhotoEntry Entry { get; init; }

    public required Guid EntryId { get; init; }

    public required string ContentType { get; init; }

    public required byte[] Content { get; init; }

    public required Guid UploadedBy { get; init; }

    public DateTimeOffset CreatedAt { get; private set; }
}
