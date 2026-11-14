using Hortiplanner.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hortiplanner.Api.Lookup;

/// <summary>Gathers what upstream sources say about a species, once, and keeps it for later.</summary>
internal static class SpeciesEnricher
{
    public const string WikipediaName = "Wikipedia";
    public const string WikipediaLicence = "CC BY-SA 4.0";

    private static readonly TimeSpan KeptFor = TimeSpan.FromDays(180);

    // A plant left with no article is worth looking at again much sooner.
    private static readonly TimeSpan KeptWithoutArticle = TimeSpan.FromHours(1);

    /// <summary>Gives what is known. Unavailable means the sources couldn't be reached, so it may be incomplete.</summary>
    public static async Task<(SpeciesDetails? Details, bool Unavailable)> FindOrFetchAsync(
        Species species,
        HortiplannerDbContext db,
        SpeciesSources sources,
        IConfiguration configuration,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var saved = await db.SpeciesDetails.FirstOrDefaultAsync(details => details.SpeciesId == species.Id, cancellationToken);
        var now = clock.GetUtcNow();

        var keptFor = saved?.ArticleSource is null ? KeptWithoutArticle : KeptFor;

        if ((saved is not null && saved.EnrichedAt > now - keptFor) || !configuration.GetValue("Species:Online", defaultValue: true))
        {
            return (saved, false);
        }

        var name = species.AcceptedName ?? species.ScientificName;
        var (article, reached) = await sources.FetchArticleAsync(name, cancellationToken);

        // Nothing is kept from a failed attempt, so the next look tries again.
        if (!reached)
        {
            return (saved, true);
        }

        var check = await sources.CheckNameAsync(name, cancellationToken);
        var native = species.InaturalistId is { } taxon ? await sources.FetchNativeToUkAsync(taxon, cancellationToken) : null;

        if (check is not null && species.NameCheckedAt is null)
        {
            species.AcceptedName = check.AcceptedName;
            species.AcceptedNameLicence = SpeciesSources.GbifLicence;
            species.GbifKey = check.GbifKey;
            species.NameCheckedAt = now;
        }

        var details = saved ?? db.SpeciesDetails.Add(new SpeciesDetails { SpeciesId = species.Id }).Entity;
        Apply(details, check, native, article, now);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
        }

        return (details, false);
    }

    private static void Apply(SpeciesDetails details, CheckedName? check, bool? native, FoundArticle? article, DateTimeOffset now)
    {
        details.EnrichedAt = now;

        if (check?.Family is { } family)
        {
            details.Family = family;
            details.FamilySource = SpeciesSources.GbifName;
            details.FamilyLicence = SpeciesSources.GbifLicence;
        }

        if (native is not null)
        {
            details.NativeToUk = native;
            details.NativeSource = SpeciesSources.INaturalistName;
        }

        if (article is null)
        {
            return;
        }

        details.Description = ArticleText.Description(article.Text);
        details.GrowingText = ArticleText.Growing(article.Text);
        details.ArticleSource = WikipediaName;
        details.ArticleLicence = WikipediaLicence;
        details.ArticleUrl = article.Url;
        details.SuggestedPosition = ArticleText.Position(article.Text);
        details.SuggestedWatering = ArticleText.Watering(article.Text);
        details.SuggestedHardiness = ArticleText.Hardiness(article.Text);
    }
}
