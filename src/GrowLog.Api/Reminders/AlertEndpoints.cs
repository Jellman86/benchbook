using System.Globalization;
using GrowLog.Api.Accounts;
using GrowLog.Api.Data;
using GrowLog.Api.Production;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Reminders;

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

    private static async Task<List<Alert>> ListAsync(GrowLogDbContext db, EventStore store, TimeProvider clock, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var alerts = new List<Alert>();

        if (await RenewalAsync(db, today, cancellationToken) is { } renewal)
        {
            alerts.Add(renewal);
        }

        if (await OverdueInvoicesAsync(db, today, cancellationToken) is { } invoices)
        {
            alerts.Add(invoices);
        }

        alerts.AddRange(await TraysAsync(db, store, clock, cancellationToken));

        var lateJobs = await db.Jobs.CountAsync(job => job.ArchivedAt == null && job.NextDue < today.AddDays(-JobLateDays), cancellationToken);

        if (lateJobs > 0)
        {
            alerts.Add(new Alert("JobsLate", $"{Plural(lateJobs, "job is", "jobs are")} late", $"Each was due more than {JobLateDays} days ago and hasn't been marked done.", "jobs"));
        }

        return alerts;
    }

    private static async Task<Alert?> RenewalAsync(GrowLogDbContext db, DateOnly today, CancellationToken cancellationToken)
    {
        var renews = (await db.GetNurserySettingsAsync(cancellationToken)).AuthorisationRenewsOn;

        if (renews is not { } date || date > today.AddDays(RenewalWarningDays))
        {
            return null;
        }

        var days = date.DayNumber - today.DayNumber;
        var why = days < 0
            ? $"It was due on {Day(date)}. Renew it, then put the next date in Nursery details."
            : $"It renews on {Day(date)}, in {Plural(days, "day", "days")}. Book the inspection, then put the next date in Nursery details.";

        return new Alert("Authorisation", "Plant health authorisation renewal", why, "nursery");
    }

    private static async Task<Alert?> OverdueInvoicesAsync(GrowLogDbContext db, DateOnly today, CancellationToken cancellationToken)
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
            : new Alert("InvoicesOverdue", $"{Plural(overdue.Count, "invoice is", "invoices are")} overdue", $"Not marked paid, and due since {Day(overdue.Min())} at the earliest.", "invoices");
    }

    /// <summary>Trays ready for a while, which may need selling or potting on, and seed nobody has counted for weeks.</summary>
    private static async Task<List<Alert>> TraysAsync(GrowLogDbContext db, EventStore store, TimeProvider clock, CancellationToken cancellationToken)
    {
        var stock = await store.GetStockAsync(cancellationToken);
        var stages = await ProductionEndpoints.FindStagesAsync([.. stock.Select(row => row.ContainerId).Distinct()], db, cancellationToken);
        var now = clock.GetUtcNow();
        var alerts = new List<Alert>();

        var ready = stages.Count(stage => stage.Value.Stage == ProductionStage.Ready && now - stage.Value.Since > TimeSpan.FromDays(ReadyTooLongDays));

        if (ready > 0)
        {
            alerts.Add(new Alert("ReadyTooLong", $"{Plural(ready, "tray has", "trays have")} been ready over four weeks", "Marked ready for sale more than four weeks ago. They may want selling, potting on or a price check.", "production"));
        }

        var uncounted = stock
            .Where(row => row.NotCounted)
            .Select(row => row.ContainerId)
            .Distinct()
            .Count(id => stages.TryGetValue(id, out var stage) && now - stage.Since > TimeSpan.FromDays(UncountedTooLongDays));

        if (uncounted > 0)
        {
            alerts.Add(new Alert("NotCountedLong", $"{Plural(uncounted, "tray hasn't", "trays haven't")} been counted for three weeks", "Sown without a count and not counted since. A count when they're pricked out, or now, keeps the figures right.", "production"));
        }

        return alerts;
    }

    /// <param name="Kind">What sort of alert, so a screen can choose an icon.</param>
    /// <param name="Why">Why it appeared, in words.</param>
    /// <param name="Where">Where to deal with it: "nursery", "invoices", "production" or "jobs".</param>
    internal sealed record Alert(string Kind, string Title, string Why, string Where);
}
