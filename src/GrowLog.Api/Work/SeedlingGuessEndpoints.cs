using GrowLog.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Work;

/// <summary>
/// For seed sown by weight: how many seedlings a gram of the same plant's seed gave before, from the first count
/// of each earlier batch sown by weight. Offered as a guess to choose, never filled in.
/// </summary>
internal static class SeedlingGuessEndpoints
{
    public static void MapSeedlingGuessEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/batches/{id:guid}/seedling-guess", GuessAsync).Produces<SeedlingGuess>();

    private static async Task<IResult> GuessAsync(Guid id, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Batches.AsNoTracking().FirstOrDefaultAsync(batch => batch.Id == id, cancellationToken) is not { } batch)
        {
            return TypedResults.NotFound();
        }

        if (batch is not { SownAs: SownMeasure.Grams, SownAmount: > 0 })
        {
            return TypedResults.Ok(new SeedlingGuess(null, null, 0));
        }

        var earlier = await db.Batches
            .AsNoTracking()
            .Where(other => other.PlantId == batch.PlantId && other.Id != batch.Id && other.SownAs == SownMeasure.Grams && other.SownAmount > 0)
            .Select(other => new { other.Id, Grams = other.SownAmount!.Value })
            .ToListAsync(cancellationToken);
        var ids = earlier.Select(other => other.Id).ToList();

        // The first count of each still stands for what came up from that weight of seed.
        var counts = (await db.Events
            .AsNoTracking()
            .Where(e => e.Type == EventType.StockCounted && e.Lines.Any(line => ids.Contains(line.BatchId)) && !db.Events.Any(c => c.CorrectsEventId == e.Id))
            .Select(e => new { e.OccurredAt, e.RecordedAt, e.Payload })
            .ToListAsync(cancellationToken))
            .Select(e => (e.OccurredAt, e.RecordedAt, Count: EventStore.CountOf(e.Payload)))
            .Where(e => e.Count is not null)
            .GroupBy(e => e.Count!.BatchId)
            .ToDictionary(group => group.Key, group => group.OrderBy(e => e.OccurredAt).ThenBy(e => e.RecordedAt).First().Count!.Counted);

        var measured = earlier.Where(other => counts.ContainsKey(other.Id)).ToList();

        if (measured.Count == 0)
        {
            return TypedResults.Ok(new SeedlingGuess(null, null, 0));
        }

        var perGram = measured.Sum(other => counts[other.Id]) / measured.Sum(other => other.Grams);
        return TypedResults.Ok(new SeedlingGuess(Math.Round(perGram, 1), (int)Math.Round(perGram * batch.SownAmount!.Value), measured.Count));
    }

    /// <param name="PerGram">Seedlings a gram gave before, or null with nothing to go on.</param>
    /// <param name="ForThisBatch">What that suggests for the weight sown in this batch.</param>
    /// <param name="From">How many earlier batches it comes from.</param>
    internal sealed record SeedlingGuess(decimal? PerGram, int? ForThisBatch, int From);
}
