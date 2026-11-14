namespace Hortiplanner.Api.Data;

/// <summary>
/// A person's picture, shown beside their name on the sign-in screen, at a bench and in Staff. Uploaded in Hortiplanner,
/// or taken from the shared staff login's standard <c>picture</c> claim when they haven't uploaded one. Kept in the
/// database so it is covered by the same backups.
/// </summary>
internal sealed class PersonPicture
{
    public required Guid UserId { get; init; }

    public required string ContentType { get; set; }

    public required byte[] Content { get; set; }

    public required PersonPictureSource Source { get; set; }

    /// <summary>Where a staff-login picture came from, so it's only fetched again when that changes.</summary>
    public string? SourceUrl { get; set; }

    public required DateTimeOffset UpdatedAt { get; set; }
}
