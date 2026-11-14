namespace Hortiplanner.Api.Data;

/// <summary>
/// The nursery's own picture to draw places on, such as an aerial photo or a plan, for a nursery without a satellite
/// map key. Kept in its own row, away from the settings read on every request. There is only ever one.
/// </summary>
internal sealed class MapPicture
{
    public const int SingleId = 1;

    public int Id { get; init; } = SingleId;

    public required byte[] Content { get; set; }

    public required string ContentType { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
