namespace GrowLog.Api.Data;

internal sealed class Plant
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string CommonName { get; set; }

    public string? Variety { get; set; }

    /// <summary>Optional while recording work. Validated before a passport is issued.</summary>
    public string? BotanicalName { get; set; }

    public string? Category { get; set; }

    public string? Position { get; set; }

    public string? Watering { get; set; }

    public string? Hardiness { get; set; }

    public bool XylellaHost { get; set; }

    public bool ProtectedVariety { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    public ICollection<PlantPrice> Prices { get; } = new List<PlantPrice>();
}
