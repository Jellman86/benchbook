using System.Globalization;
using Hortiplanner.Api.Accounts;
using Hortiplanner.Api.Data;
using Hortiplanner.Api.Production;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Reminders;

/// <summary>
/// Things the owner should look at, worked out from the records each time, so each says why it is there and
/// goes once it's dealt with. Nothing is stored and nothing needs dismissing.
/// </summary>
internal static class AlertEndpoints
{
    private const int RenewalWarningDays = 42;
    private const int ReadyTooLongDays = 28;
    private const int UncountedTooLongDays = 21;
    private const int JobLateDays = 2;

    public static void MapAlertEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/alerts", ListAsync).RequireAuthorization(AccountServices.OwnerPolicy);

    private static string Day(DateOnly day) => day.ToString("ddd d MMM yyyy", CultureInfo.GetCultureInfo("en-GB"));

    private static string Plural(int count, string one, string many) => count == 1 ? $"1 {one}" : $"{count} {many}";

    internal static async Task<List<Alert>> ListAsync(HortiplannerDbContext db, EventStore store, TimeProvider clock, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var alerts = new List<Alert>();

        // Nothing is raised about a part of Hortiplanner the owner has turned off.
        var off = (await db.GetNurserySettingsAsync(cancellationToken)).FeaturesOff;

        if (!off.Contains(NurseryFeature.Passports) && await RenewalAsync(db, today, cancellationToken) is { } renewal)
        {
            alerts.Add(renewal);
        }

        if (!off.Contains(NurseryFeature.Invoices) && await OverdueInvoicesAsync(db, today, cancellationToken) is { } invoices)
        {
            alerts.Add(invoices);
        }

        alerts.AddRange(await TraysAsync(db, store, clock, cancellationToken));

        if (!off.Contains(NurseryFeature.Jobs) && await LateJobsAsync(db, today, cancellationToken) is { } lateJobs)
        {
            alerts.Add(lateJobs);
        }

        return alerts;
    }

    private static async Task<Alert?> LateJobsAsync(HortiplannerDbContext db, DateOnly today, CancellationToken cancellationToken)
    {
        var late = await db.Jobs.CountAsync(job => job.ArchivedAt == null && job.NextDue < today.AddDays(-JobLateDays), cancellationToken);

        return late == 0
            ? null
            : new Alert(
                "JobsLate",
                $"{Plural(late, "job is", "jobs are")} running late",
                late == 1
                    ? $"It was due more than {JobLateDays} days ago and nobody has ticked it off yet."
                    : $"Each one was due more than {JobLateDays} days ago and hasn't been ticked off yet.",
                "jobs");
    }

    private static async Task<Alert?> RenewalAsync(HortiplannerDbContext db, DateOnly today, CancellationToken cancellationToken)
    {
        var renews = (await db.GetNurserySettingsAsync(cancellationToken)).AuthorisationRenewsOn;

        if (renews is not { } date || date > today.AddDays(RenewalWarningDays))
        {
            return null;
        }

        var days = date.DayNumber - today.DayNumber;
        var why = days < 0
            ? $"It was due on {Day(date)}. Once you've renewed it, add the next renewal date in Nursery details."
            : $"It's due on {Day(date)}, which is in {Plural(days, "day", "days")}. Book the inspection, and once it's renewed, add the next date in Nursery details.";

        return new Alert("Authorisation", "Your plant health authorisation needs renewing", why, "nursery");
    }

    private static async Task<Alert?> OverdueInvoicesAsync(HortiplannerDbContext db, DateOnly today, CancellationToken cancellationToken)
    {
        var overdue = await db.Invoices
            .Where(invoice => invoice.Kind == InvoiceKind.Invoice
                && invoice.PaidOn == null
                && invoice.DueOn != null
                && invoice.DueOn < today
                && !db.Invoices.Any(credit => credit.CreditsInvoiceId == invoice.Id))
            .Select(invoice => invoice.DueOn!.Value)
            .ToListAsync(cancellationToken);

        return overdue.Count == 0
            ? null
            : new Alert(
                "InvoicesOverdue",
                $"{Plural(overdue.Count, "invoice is", "invoices are")} overdue",
                overdue.Count == 1
                    ? $"It hasn't been marked as paid, and it was due on {Day(overdue.Min())}."
                    : $"They haven't been marked as paid. The oldest was due on {Day(overdue.Min())}.",
                "invoices");
    }

    /// <summary>Trays ready for a while, which may need selling or potting on, and seed nobody has counted for weeks.</summary>
    private static async Task<List<Alert>> TraysAsync(HortiplannerDbContext db, EventStore store, TimeProvider clock, CancellationToken cancellationToken)
    {
        var stock = await store.GetStockAsync(cancellationToken);
        var stages = await ProductionEndpoints.FindStagesAsync([.. stock.Select(row => row.ContainerId).Distinct()], db, cancellationToken);
        var now = clock.GetUtcNow();
        var alerts = new List<Alert>();

        var ready = stages.Count(stage => stage.Value.Stage == ProductionStage.Ready && now - stage.Value.Since > TimeSpan.FromDays(ReadyTooLongDays));

        if (ready > 0)
        {
            alerts.Add(new Alert(
                "ReadyTooLong",
                $"{Plural(ready, "tray has", "trays have")} been ready for over four weeks",
                ready == 1
                    ? "It was marked ready for sale more than four weeks ago. It might need selling, potting on, or a look at its price."
                    : "They were marked ready for sale more than four weeks ago. They might need selling, potting on, or a look at their prices.",
                "production"));
        }

        var uncounted = stock
            .Where(row => row.NotCounted)
            .Select(row => row.ContainerId)
            .Distinct()
            .Count(id => stages.TryGetValue(id, out var stage) && now - stage.Since > TimeSpan.FromDays(UncountedTooLongDays));

        if (uncounted > 0)
        {
            alerts.Add(new Alert(
                "NotCountedLong",
                $"{Plural(uncounted, "tray hasn't", "trays haven't")} been counted in three weeks",
                uncounted == 1
                    ? "It was sown without counting the seeds, so we don't know how many plants are in it yet. Counting it when you prick it out, or now if you have a minute, keeps the figures accurate."
                    : "They were sown without counting the seeds, so we don't know how many plants are in them yet. Counting them when you prick them out, or now if you have a minute, keeps the figures accurate.",
                "production"));
        }

        return alerts;
    }

    /// <param name="Kind">What sort of alert, so a screen can choose an icon.</param>
    /// <param name="Why">Why it appeared, in words.</param>
    /// <param name="Where">Where to deal with it: "nursery", "invoices", "production" or "jobs".</param>
    internal sealed record Alert(string Kind, string Title, string Why, string Where);
}
