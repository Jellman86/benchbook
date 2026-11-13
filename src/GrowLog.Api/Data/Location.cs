namespace GrowLog.Api.Data;

/// <summary>A site, or any place within it: a greenhouse, a bench in it, a bay on the bench, a bed outside.</summary>
internal sealed class Location : IAudited, IArchivable
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    public required LocationKind Kind { get; init; }

    /// <summary>The place this one sits in. Null only for a site.</summary>
    public Guid? ParentId { get; set; }

    /// <summary>A short code such as P0001 for the place's printed label, given by the database.</summary>
    public string LabelCode { get; private set; } = null!;

    /// <summary>Its outline on the map, as JSON [[latitude, longitude], …], or null if it hasn't been drawn.</summary>
    public string? Outline { get; set; }

    /// <summary>
    /// Its outline on the nursery's own map picture, as JSON [[down, across], …] in fractions of the picture's height
    /// and width, or null. Kept apart from <see cref="Outline"/> because a picture and the earth don't line up.
    /// </summary>
    public string? PictureOutline { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }
}
