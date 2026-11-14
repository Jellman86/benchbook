namespace Hortiplanner.Api.Data;

/// <summary>
/// What upstream sources say about a species, with source and licence per field.
/// Reference data: the suggested fields only reach a plant when a person accepts them.
/// </summary>
internal sealed class SpeciesDetails
{
    public required Guid SpeciesId { get; init; }

    public string? Family { get; set; }

    public string? FamilySource { get; set; }

    public string? FamilyLicence { get; set; }

    public bool? NativeToUk { get; set; }

    public string? NativeSource { get; set; }

    public string? Description { get; set; }

    public string? GrowingText { get; set; }

    public string? ArticleSource { get; set; }

    public string? ArticleLicence { get; set; }

    public string? ArticleUrl { get; set; }

    public string? SuggestedPosition { get; set; }

    public string? SuggestedWatering { get; set; }

    public string? SuggestedHardiness { get; set; }

    public DateTimeOffset EnrichedAt { get; set; }
}
