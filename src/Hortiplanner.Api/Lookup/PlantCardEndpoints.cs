using System.Security.Claims;
using Hortiplanner.Api.Accounts;
using Hortiplanner.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Lookup;

/// <summary>Everything known about one plant: what the nursery recorded, what upstream sources say, and growing notes.</summary>
internal static class PlantCardEndpoints
{
    private const int MostNoteLength = 4000;

    public static void MapPlantCardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/species/{id:guid}/details", GetDetailsAsync).Produces<SpeciesDetailsResponse>();

        var plants = app.MapGroup("/api/plants/{id:guid}");
        plants.MapGet("/card", GetCardAsync).Produces<PlantCardResponse>();
        plants.MapPost("/notes", AddNoteAsync).Produces<PlantNoteResponse>(StatusCodes.Status201Created);
        plants.MapPut("/species", LinkSpeciesAsync).RequireAuthorization(AccountServices.OwnerPolicy);
    }

    private static Credited? Article(string? text, SpeciesDetails? details) =>
        text is null ? null : new Credited(text, details?.ArticleSource ?? SpeciesEnricher.WikipediaName, details?.ArticleLicence, details?.ArticleUrl);

    private static async Task<IResult> GetDetailsAsync(
        Guid id,
        HortiplannerDbContext db,
        SpeciesSources sources,
        IConfiguration configuration,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (await db.Species.FirstOrDefaultAsync(species => species.Id == id, cancellationToken) is not { } species)
        {
            return TypedResults.NotFound();
        }

        var (details, unavailable) = await SpeciesEnricher.FindOrFetchAsync(species, db, sources, configuration, clock, cancellationToken);

        return TypedResults.Ok(new SpeciesDetailsResponse(
            details?.Family,
            details?.NativeToUk,
            Article(details?.Description, details),
            Article(details?.GrowingText, details),
            details?.SuggestedPosition,
            details?.SuggestedWatering,
            details?.SuggestedHardiness,
            unavailable));
    }

    private static async Task<IResult> GetCardAsync(
        Guid id,
        HortiplannerDbContext db,
        UserManager<AppUser> users,
        SpeciesSources sources,
        IConfiguration configuration,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (await db.Plants.FirstOrDefaultAsync(plant => plant.Id == id, cancellationToken) is not { } plant)
        {
            return TypedResults.NotFound();
        }

        var species = plant.SpeciesId is { } speciesId ? await db.Species.FirstOrDefaultAsync(entry => entry.Id == speciesId, cancellationToken) : null;
        var (details, unavailable) = species is null
            ? (null, false)
            : await SpeciesEnricher.FindOrFetchAsync(species, db, sources, configuration, clock, cancellationToken);
        var hasOwnPhoto = await db.PlantPhotos.AnyAsync(photo => photo.PlantId == id, cancellationToken);

        var notes = await db.PlantNotes
            .AsNoTracking()
            .Where(note => note.PlantId == id)
            .OrderByDescending(note => note.CreatedAt)
            .Join(users.Users, note => note.AddedBy, user => user.Id, (note, user) => new PlantNoteResponse(note.Id, note.Text, user.UserName!, note.CreatedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new PlantCardResponse(
            plant.Id,
            plant.CommonName,
            plant.Variety,
            plant.BotanicalName,
            plant.Category,
            plant.Position,
            plant.Watering,
            plant.Hardiness,
            plant.XylellaHost,
            plant.ProtectedVariety,
            plant.ArchivedAt != null,
            hasOwnPhoto || species?.PhotoUrl is not null,
            hasOwnPhoto ? null : species?.PhotoCredit,
            species is not null,
            details?.Family,
            details?.NativeToUk,
            Article(details?.Description, details),
            Article(details?.GrowingText, details),
            notes,
            unavailable));
    }

    private static async Task<IResult> AddNoteAsync(
        Guid id,
        AddPlantNoteRequest request,
        ClaimsPrincipal user,
        HortiplannerDbContext db,
        CancellationToken cancellationToken)
    {
        var text = request.Text?.Trim() ?? string.Empty;

        if (text.Length is 0 or > MostNoteLength)
        {
            return TypedResults.BadRequest();
        }

        if (!await db.Plants.AnyAsync(plant => plant.Id == id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var note = new PlantNote { PlantId = id, Text = text, AddedBy = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!) };
        db.PlantNotes.Add(note);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/plants/{id}/card", new PlantNoteResponse(note.Id, note.Text, user.Identity?.Name ?? string.Empty, note.CreatedAt));
    }

    /// <summary>Matches a plant added by hand to a species. Only fills gaps: nothing the nursery recorded is replaced.</summary>
    private static async Task<IResult> LinkSpeciesAsync(
        Guid id,
        LinkSpeciesRequest request,
        HortiplannerDbContext db,
        SpeciesSources sources,
        IConfiguration configuration,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (await db.Plants.FirstOrDefaultAsync(entry => entry.Id == id, cancellationToken) is not { } plant
            || await db.Species.FirstOrDefaultAsync(entry => entry.Id == request.SpeciesId, cancellationToken) is not { } species)
        {
            return TypedResults.NotFound();
        }

        var (details, _) = await SpeciesEnricher.FindOrFetchAsync(species, db, sources, configuration, clock, cancellationToken);

        plant.SpeciesId = species.Id;
        plant.BotanicalName ??= species.AcceptedName ?? species.ScientificName;
        plant.Position ??= details?.SuggestedPosition;
        plant.Watering ??= details?.SuggestedWatering;
        plant.Hardiness ??= details?.SuggestedHardiness;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    internal sealed record Credited(string Text, string Source, string? Licence, string? Url);

    internal sealed record SpeciesDetailsResponse(
        string? Family,
        bool? NativeToUk,
        Credited? Description,
        Credited? Growing,
        string? SuggestedPosition,
        string? SuggestedWatering,
        string? SuggestedHardiness,
        bool Unavailable);

    internal sealed record PlantNoteResponse(Guid Id, string Text, string AddedBy, DateTimeOffset CreatedAt);

    internal sealed record AddPlantNoteRequest(string Text);

    internal sealed record LinkSpeciesRequest(Guid SpeciesId);

    internal sealed record PlantCardResponse(
        Guid Id,
        string CommonName,
        string? Variety,
        string? BotanicalName,
        string? Category,
        string? Position,
        string? Watering,
        string? Hardiness,
        bool? XylellaHost,
        bool ProtectedVariety,
        bool Archived,
        bool HasPhoto,
        string? PhotoCredit,
        bool LinkedToSpecies,
        string? Family,
        bool? NativeToUk,
        Credited? Description,
        Credited? Growing,
        IReadOnlyList<PlantNoteResponse> Notes,
        bool LookupUnavailable);
}
