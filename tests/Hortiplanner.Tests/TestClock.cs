namespace Hortiplanner.Tests;

/// <summary>A clock tests can move forward, for checking timeouts without waiting.</summary>
internal sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan time) => now += time;
}
