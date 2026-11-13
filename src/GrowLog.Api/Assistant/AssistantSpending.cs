using System.Data.Common;
using GrowLog.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Assistant;

/// <summary>
/// What Ask GrowLog's questions have cost, person by person, in US dollars as OpenRouter charges. The nursery pays for
/// the key, so each person sees what their own questions cost and the owner sees it for everyone.
/// </summary>
internal static partial class AssistantSpending
{
    /// <summary>
    /// Records what a question cost the person who asked, however it ended, and returns what they've now spent. It's
    /// saved even if the person has gone, as the money is spent either way. A failure to save is logged, not passed on,
    /// so an answer already paid for still reaches them.
    /// </summary>
    public static async Task<YourSpend?> TryRecordAsync(GrowLogDbContext db, Guid askedBy, decimal cost, TimeProvider clock, ILogger logger)
    {
        try
        {
            db.AssistantSpends.Add(new AssistantSpend { AskedBy = askedBy, AskedAt = clock.GetUtcNow(), Cost = Math.Max(cost, 0) });
            await db.SaveChangesAsync(CancellationToken.None);
            return await OfAsync(db, askedBy, clock, CancellationToken.None);
        }
        catch (Exception problem) when (problem is DbUpdateException or DbException or InvalidOperationException)
        {
            LogNotRecorded(logger, cost, problem.GetType().Name);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ask GrowLog spend of {Cost} USD couldn't be recorded: {Kind}")]
    private static partial void LogNotRecorded(ILogger logger, decimal cost, string kind);

    /// <summary>What one person's questions have cost this month and in all.</summary>
    public static async Task<YourSpend> OfAsync(GrowLogDbContext db, Guid person, TimeProvider clock, CancellationToken cancellationToken)
    {
        var monthStart = MonthStart(clock);
        var theirs = db.AssistantSpends.AsNoTracking().Where(spend => spend.AskedBy == person);
        var thisMonth = await theirs.Where(spend => spend.AskedAt >= monthStart).SumAsync(spend => spend.Cost, cancellationToken);
        var inAll = await theirs.SumAsync(spend => spend.Cost, cancellationToken);
        return new YourSpend(thisMonth, inAll);
    }

    /// <summary>Everyone who has asked, most spent this month first, for the owner.</summary>
    public static async Task<List<PersonSpending>> EveryoneAsync(GrowLogDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var monthStart = MonthStart(clock);
        var totals = await db.AssistantSpends.AsNoTracking()
            .GroupBy(spend => spend.AskedBy)
            .Select(group => new
            {
                Person = group.Key,
                ThisMonth = group.Where(spend => spend.AskedAt >= monthStart).Sum(spend => spend.Cost),
                ThisMonthQuestions = group.Count(spend => spend.AskedAt >= monthStart),
                InAll = group.Sum(spend => spend.Cost),
            })
            .ToListAsync(cancellationToken);
        var people = totals.Select(total => total.Person).ToList();
        var names = await db.Users.AsNoTracking()
            .Where(user => people.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.UserName ?? "Someone", cancellationToken);

        return totals
            .Select(total => new PersonSpending(names.GetValueOrDefault(total.Person, "Someone who has left"), total.ThisMonth, total.ThisMonthQuestions, total.InAll))
            .OrderByDescending(person => person.ThisMonth)
            .ThenByDescending(person => person.InAll)
            .ToList();
    }

    /// <summary>The start of this calendar month, by the UTC clock.</summary>
    private static DateTimeOffset MonthStart(TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        return new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
    }

    /// <summary>What this person's questions have cost, in US dollars.</summary>
    internal sealed record YourSpend(decimal ThisMonth, decimal InAll);

    /// <summary>One person's questions this month and what they've cost, in US dollars, for the owner.</summary>
    internal sealed record PersonSpending(string Name, decimal ThisMonth, int ThisMonthQuestions, decimal InAll);
}
