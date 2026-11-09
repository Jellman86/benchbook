namespace GrowLog.Api.Data;

/// <summary>
/// A plant found by searching an online source and kept so it need not be looked up again.
/// Reference data only: nothing here reaches the nursery's own plants until a person picks it.
/// Source and licence are held per field.
/// </summary>
internal sealed class Species
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string ScientificName { get; init; }

    public required string ScientificNameSource { get; set; }

    public string? ScientificNameLicence { get; set; }

    public string? CommonName { get; set; }

    public string? CommonNameSource { get; set; }

    public string? CommonNameLicence { get; set; }

    public required string Rank { get; set; }

    /// <summary>Lower-case names, including any alternative that a search matched on.</summary>
    public required string SearchText { get; set; }

    /// <summary>How often the plant is recorded. Puts the plants people mean ahead of obscure namesakes.</summary>
    public int Popularity { get; set; }

    public long? InaturalistId { get; set; }

    public long? GbifKey { get; set; }

    /// <summary>The currently accepted botanical name according to GBIF, once checked.</summary>
    public string? AcceptedName { get; set; }

    public string? AcceptedNameLicence { get; set; }

    public DateTimeOffset? NameCheckedAt { get; set; }

    public string? PhotoUrl { get; set; }

    public string? PhotoSource { get; set; }

    public string? PhotoLicence { get; set; }

    public string? PhotoCredit { get; set; }

    public DateTimeOffset FetchedAt { get; set; }
}
