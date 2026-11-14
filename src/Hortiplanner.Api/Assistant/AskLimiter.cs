namespace Hortiplanner.Api.Assistant;

/// <summary>Keeps one person from asking more than a set number of questions an hour, so a key can't be run up.</summary>
internal sealed class AskLimiter(TimeProvider clock)
{
    public const int PerHour = 40;

    private readonly Lock gate = new();
    private readonly Dictionary<Guid, Queue<DateTimeOffset>> asked = [];

    public bool TryTake(Guid person)
    {
        var now = clock.GetUtcNow();

        lock (gate)
        {
            if (!asked.TryGetValue(person, out var times))
            {
                times = new Queue<DateTimeOffset>();
                asked[person] = times;
            }

            while (times.Count > 0 && now - times.Peek() > TimeSpan.FromHours(1))
            {
                times.Dequeue();
            }

            if (times.Count >= PerHour)
            {
                return false;
            }

            times.Enqueue(now);
            return true;
        }
    }
}
