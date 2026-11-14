namespace Hortiplanner.Api.Accounts;

/// <summary>A shared device the owner has approved for staff to sign in on by name and PIN.</summary>
internal sealed class BenchStation
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; set; }
}
