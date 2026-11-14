namespace Hortiplanner.Api.Data;

/// <summary>
/// A temperature and humidity sensor in one place, such as a Home Assistant sensor or a small board on the bench.
/// It sends readings with its own key; only a hash of the key is kept, so a stolen backup can't send readings.
/// </summary>
internal sealed class Sensor : IArchivable
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    public required Guid LocationId { get; set; }

    public required byte[] KeyHash { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    public DateTimeOffset? LastReceivedAt { get; set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
