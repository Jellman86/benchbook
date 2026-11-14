using System.Globalization;
using Hortiplanner.Api.Catalogue;
using Hortiplanner.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hortiplanner.Api.Lookup;

/// <summary>Searches the nursery's own plants, then saved species, then online sources.</summary>
internal static class SpeciesEndpoints
{
    private const int MinimumQueryLength = 3;
    private const int MostResults = 8;
    private const string Escape = "\\";

    private static readonly TimeSpan SearchKeptFor = TimeSpan.FromDays(90);

    public static void MapSpeciesEndpoints(this IEndpointRouteBuilder app)
    {
        var species = app.MapGroup("/api/species");
        species.MapGet("/search", SearchAsync).Produces<SpeciesSearchResponse>();
        species.MapGet("/{id:guid}/photo", GetPhotoAsync);

        app.MapPost("/api/plants/from-species", AddPlantFromSpeciesAsync)
            .Produces<PlantFromSpecies>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
    }

    /// <summary>Saved copies of a species picture are permanent, so a browser can keep them too.</summary>
    internal static IResult Picture(HttpContext context, byte[] content, string contentType, bool permanent)
    {
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.CacheControl = permanent ? "private, max-age=31536000, immutable" : "private, no-cache";
        return TypedResults.File(content, contentType);
    }

    internal static async Task<SpeciesPhoto?> FindOrFetchPhotoAsync(Guid speciesId, HortiplannerDbContext db, SpeciesSources sources, CancellationToken cancellationToken)
    {
        if (await db.SpeciesPhotos.AsNoTracking().FirstOrDefaultAsync(photo => photo.SpeciesId == speciesId, cancellationToken) is { } saved)
        {
            return saved;
        }

        var url = await db.Species.Where(species => species.Id == speciesId).Select(species => species.PhotoUrl).FirstOrDefaultAsync(cancellationToken);

        if (url is null || await sources.DownloadPhotoAsync(url, cancellationToken) is not { } image)
        {
            return null;
        }

        var photo = new SpeciesPhoto { SpeciesId = speciesId, ContentType = image.ContentType, Content = image.Content };
        db.SpeciesPhotos.Add(photo);
        await TrySaveAsync(db, cancellationToken);
        return photo;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string CleanQuery(string query) => string.Join(' ', query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToLowerInvariant();

    private static string EscapeLike(string value) =>
        value.Replace(Escape, Escape + Escape, StringComparison.Ordinal)
            .Replace("%", Escape + "%", StringComparison.Ordinal)
            .Replace("_", Escape + "_", StringComparison.Ordinal);

    private static string Capitalise(string name) =>
        name.Length == 0 ? name : string.Concat(char.ToUpper(name[0], CultureInfo.InvariantCulture).ToString(), name.AsSpan(1));

    /// <summary>A second search finding the same plants at the same moment is harmless, so that clash is ignored.</summary>
    private static async Task TrySaveAsync(HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
        }
    }

    private static async Task<IResult> SearchAsync(
        string q,
        HortiplannerDbContext db,
        SpeciesSources sources,
        IConfiguration configuration,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var query = CleanQuery(q);

        if (query.Length < MinimumQueryLength)
        {
            return TypedResults.Ok(new SpeciesSearchResponse([], [], OnlineUnavailable: false));
        }

        var unavailable = false;

        if (configuration.GetValue("Species:Online", defaultValue: true) && !await WasSearchedRecentlyAsync(query, db, clock, cancellationToken))
        {
            unavailable = !await SearchOnlineAsync(query, db, sources, clock, cancellationToken);
        }

        var contains = $"%{EscapeLike(query)}%";

        return TypedResults.Ok(new SpeciesSearchResponse(
            await FindOwnPlantsAsync(contains, db, cancellationToken),
            await FindSavedSpeciesAsync(contains, db, cancellationToken),
            unavailable));
    }

    private static async Task<bool> WasSearchedRecentlyAsync(string query, HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var cutoff = clock.GetUtcNow() - SearchKeptFor;
        return await db.SpeciesSearches.AnyAsync(search => search.Query == query && search.SearchedAt > cutoff, cancellationToken);
    }

    private static async Task<bool> SearchOnlineAsync(string query, HortiplannerDbContext db, SpeciesSources sources, TimeProvider clock, CancellationToken cancellationToken)
    {
        IReadOnlyList<FoundSpecies> found;

        try
        {
            found = await sources.SearchAsync(query, cancellationToken);
        }
        catch (Exception exception) when (SpeciesSources.IsUnavailable(exception) && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        await SaveFoundAsync(query, found, clock.GetUtcNow(), db, cancellationToken);
        return true;
    }

    private static async Task SaveFoundAsync(string query, IReadOnlyList<FoundSpecies> found, DateTimeOffset now, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        var names = found.Select(item => item.ScientificName).ToList();
        var existing = (await db.Species.Where(species => names.Contains(species.ScientificName)).ToListAsync(cancellationToken))
            .ToDictionary(species => species.ScientificName, StringComparer.OrdinalIgnoreCase);

        foreach (var item in found.DistinctBy(item => item.ScientificName, StringComparer.OrdinalIgnoreCase))
        {
            if (existing.TryGetValue(item.ScientificName, out var species))
            {
                Refresh(species, item, now);
            }
            else
            {
                var added = new Species
                {
                    ScientificName = item.ScientificName,
                    ScientificNameSource = item.Source,
                    ScientificNameLicence = item.Licence,
                    Rank = item.Rank,
                    SearchText = string.Empty,
                };

                Refresh(added, item, now);
                db.Species.Add(added);
            }
        }

        if (await db.SpeciesSearches.FindAsync([query], cancellationToken) is { } search)
        {
            search.SearchedAt = now;
        }
        else
        {
            db.SpeciesSearches.Add(new SpeciesSearch { Query = query, SearchedAt = now });
        }

        await TrySaveAsync(db, cancellationToken);
    }

    private static void Refresh(Species species, FoundSpecies item, DateTimeOffset now)
    {
        species.Rank = item.Rank;
        species.Popularity = Math.Max(species.Popularity, item.Popularity);
        species.FetchedAt = now;
        species.InaturalistId ??= item.InaturalistId;
        species.GbifKey ??= item.GbifKey;

        if (item.CommonName is not null)
        {
            species.CommonName = Capitalise(item.CommonName);
            species.CommonNameSource = item.Source;
            species.CommonNameLicence = item.Licence;
        }

        if (species.PhotoUrl is null && item.Photo is { } photo)
        {
            species.PhotoUrl = photo.Url;
            species.PhotoSource = item.Source;
            species.PhotoLicence = photo.Licence;
            species.PhotoCredit = photo.Credit;
        }

        var newTerms = new[] { item.ScientificName, item.CommonName, item.MatchedTerm }
            .OfType<string>()
            .Select(term => term.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Where(term => !species.SearchText.Contains(term, StringComparison.Ordinal))
            .ToList();

        species.SearchText = string.Join(' ', newTerms.Prepend(species.SearchText)).Trim();
    }

    private static async Task<List<OwnPlantResult>> FindOwnPlantsAsync(string contains, HortiplannerDbContext db, CancellationToken cancellationToken) =>
        await db.Plants
            .AsNoTracking()
            .Where(plant => plant.ArchivedAt == null)
            .Where(plant => EF.Functions.ILike(plant.CommonName, contains, Escape)
                || EF.Functions.ILike(plant.Variety!, contains, Escape)
                || EF.Functions.ILike(plant.BotanicalName!, contains, Escape))
            .OrderBy(plant => plant.CommonName)
            .ThenBy(plant => plant.Variety)
            .Take(MostResults)
            .Select(plant => new OwnPlantResult(
                plant.Id,
                plant.CommonName,
                plant.Variety,
                plant.BotanicalName,
                db.PlantPhotos.Any(photo => photo.PlantId == plant.Id) || db.Species.Any(species => species.Id == plant.SpeciesId && species.PhotoUrl != null)))
            .ToListAsync(cancellationToken);

    /// <summary>Plants actually called this come first, most recorded first. Alternative names follow.</summary>
    private static async Task<List<SpeciesResult>> FindSavedSpeciesAsync(string contains, HortiplannerDbContext db, CancellationToken cancellationToken) =>
        await db.Species
            .AsNoTracking()
            .Where(species => EF.Functions.ILike(species.SearchText, contains, Escape))
            .OrderByDescending(species => (species.CommonName != null && EF.Functions.ILike(species.CommonName, contains, Escape)) || EF.Functions.ILike(species.ScientificName, contains, Escape))
            .ThenByDescending(species => species.Popularity)
            .ThenBy(species => species.ScientificName)
            .Take(MostResults)
            .Select(species => new SpeciesResult(species.Id, species.CommonName, species.AcceptedName ?? species.ScientificName, species.Rank, species.PhotoUrl != null, species.PhotoCredit))
            .ToListAsync(cancellationToken);

    private static async Task<IResult> GetPhotoAsync(Guid id, HttpContext context, HortiplannerDbContext db, SpeciesSources sources, CancellationToken cancellationToken) =>
        await FindOrFetchPhotoAsync(id, db, sources, cancellationToken) is { } photo
            ? Picture(context, photo.Content, photo.ContentType, permanent: true)
            : TypedResults.NotFound();

    /// <summary>A person has picked this species, so it joins the nursery's own plants, or finds the entry already there.</summary>
    private static async Task<IResult> AddPlantFromSpeciesAsync(
        PlantFromSpeciesRequest request,
        HortiplannerDbContext db,
        SpeciesSources sources,
        IConfiguration configuration,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (await db.Species.FirstOrDefaultAsync(species => species.Id == request.SpeciesId, cancellationToken) is not { } species)
        {
            return TypedResults.NotFound();
        }

        await SpeciesEnricher.FindOrFetchAsync(species, db, sources, configuration, clock, cancellationToken);

        var commonName = species.CommonName ?? species.ScientificName;
        var variety = string.IsNullOrWhiteSpace(request.Variety) ? null : request.Variety.Trim();
        var botanicalName = species.AcceptedName ?? species.ScientificName;

        var plant = await db.Plants.FirstOrDefaultAsync(entry => entry.CommonName == commonName && entry.Variety == variety, cancellationToken);

        if (plant is { ArchivedAt: not null })
        {
            return TypedResults.Conflict();
        }

        if (plant is null)
        {
            plant = new Plant
            {
                CommonName = commonName,
                Variety = variety,
                Category = Clean(request.Category),
                Position = Clean(request.Position),
                Watering = Clean(request.Watering),
                Hardiness = Clean(request.Hardiness),
                XylellaHost = request.XylellaHost,
                ProtectedVariety = request.ProtectedVariety,
            };

            foreach (var price in request.Prices ?? [])
            {
                plant.Prices.Add(new PlantPrice { PotSizeId = price.PotSizeId, Price = price.Price });
            }

            db.Plants.Add(plant);
        }

        plant.SpeciesId ??= species.Id;
        plant.BotanicalName ??= botanicalName;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return TypedResults.Conflict();
        }

        return TypedResults.Ok(new PlantFromSpecies(plant.Id));
    }

    internal sealed record SpeciesSearchResponse(IReadOnlyList<OwnPlantResult> Plants, IReadOnlyList<SpeciesResult> Species, bool OnlineUnavailable);

    internal sealed record OwnPlantResult(Guid Id, string CommonName, string? Variety, string? BotanicalName, bool HasPhoto);

    internal sealed record SpeciesResult(Guid Id, string? CommonName, string ScientificName, string Rank, bool HasPhoto, string? PhotoCredit);

    internal sealed record PlantFromSpeciesRequest(
        Guid SpeciesId,
        string? Variety,
        string? Category,
        string? Position,
        string? Watering,
        string? Hardiness,
        bool? XylellaHost,
        bool ProtectedVariety,
        IReadOnlyList<CatalogueEndpoints.PlantPriceDto>? Prices);

    internal sealed record PlantFromSpecies(Guid Id);
}
