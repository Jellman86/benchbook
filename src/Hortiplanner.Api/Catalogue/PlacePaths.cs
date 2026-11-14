using Hortiplanner.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Catalogue;

/// <summary>
/// Each place named with the places it sits in, such as "Greenhouse 2, Bench A", because two greenhouses
/// can each have a Bench A. The site is left out unless the place is the site, as most nurseries have one.
/// </summary>
internal static class PlacePaths
{
    private const int DeepestPath = 12;

    public static async Task<Dictionary<Guid, string>> LoadAsync(HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        var places = await db.Locations
            .AsNoTracking()
            .Select(place => new Place(place.Id, place.Name, place.Kind, place.ParentId))
            .ToDictionaryAsync(place => place.Id, cancellationToken);

        return places.Values.ToDictionary(place => place.Id, place => PathOf(place, places));
    }

    private static string PathOf(Place place, Dictionary<Guid, Place> places)
    {
        var names = new List<string> { place.Name };
        var current = place;

        while (current.ParentId is { } parentId && places.TryGetValue(parentId, out var parent) && names.Count < DeepestPath)
        {
            if (parent.Kind != LocationKind.Site)
            {
                names.Add(parent.Name);
            }

            current = parent;
        }

        names.Reverse();
        return string.Join(", ", names);
    }

    private sealed record Place(Guid Id, string Name, LocationKind Kind, Guid? ParentId);
}
