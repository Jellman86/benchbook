namespace Hortiplanner.Api.Reminders;

/// <summary>When a repeating job comes round again.</summary>
internal static class JobDates
{
    /// <summary>
    /// The next date after a job was done on <paramref name="doneOn"/>: its interval on, and on again until it is
    /// after today, so a job done late doesn't leave a backlog of dates already past.
    /// </summary>
    public static DateOnly NextAfter(DateOnly doneOn, int everyDays, DateOnly today)
    {
        var next = doneOn.AddDays(everyDays);

        while (next <= today)
        {
            next = next.AddDays(everyDays);
        }

        return next;
    }
}
