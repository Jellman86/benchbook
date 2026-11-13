using GrowLog.Api.Data;

namespace GrowLog.Api.Assistant;

/// <summary>What a question's tools share: the records, the clock, and what they looked at, to link from the answer.</summary>
internal sealed class ToolContext(GrowLogDbContext db, EventStore store, TimeProvider clock, CancellationToken cancellation)
{
    private const int MostSources = 30;

    private readonly List<AskSource> seen = [];

    public GrowLogDbContext Db => db;

    public EventStore Store => store;

    public TimeProvider Clock => clock;

    public CancellationToken Cancellation => cancellation;

    public IReadOnlyList<AskSource> Sources => seen;

    /// <summary>Notes a record a tool returned. Kinds are "tray" (by label), "plant" and "place" (by id).</summary>
    public void Saw(string kind, string key, string label)
    {
        if (seen.Count < MostSources && !seen.Exists(item => string.Equals(item.Kind, kind, StringComparison.Ordinal) && string.Equals(item.Key, key, StringComparison.Ordinal)))
        {
            seen.Add(new AskSource(kind, key, label));
        }
    }

    /// <summary>A record an answer used, to link to.</summary>
    internal sealed record AskSource(string Kind, string Key, string Label);
}
