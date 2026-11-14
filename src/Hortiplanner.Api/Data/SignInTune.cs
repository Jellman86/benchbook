namespace Hortiplanner.Api.Data;

/// <summary>
/// A short piece of music the owner has chosen for one person, played on their device when they sign in. Kept in the
/// database so it is covered by the same backups. The person can turn it off for themselves.
/// </summary>
internal sealed class SignInTune
{
    public required Guid UserId { get; init; }

    public required string ContentType { get; set; }

    public required byte[] Content { get; set; }

    /// <summary>The person has turned their tune off. It stays, so they can turn it back on.</summary>
    public bool Off { get; set; }

    public required DateTimeOffset UpdatedAt { get; set; }
}
