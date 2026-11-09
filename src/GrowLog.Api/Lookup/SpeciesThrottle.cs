using System.Threading.RateLimiting;

namespace GrowLog.Api.Lookup;

/// <summary>Keeps requests to iNaturalist within the one or so a second it asks of API users.</summary>
internal sealed class SpeciesThrottle : IDisposable
{
    private readonly TokenBucketRateLimiter limiter = new(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 6,
        TokensPerPeriod = 1,
        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
        QueueLimit = 6,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        AutoReplenishment = true,
    });

    public ValueTask<RateLimitLease> WaitAsync(CancellationToken cancellationToken) => limiter.AcquireAsync(1, cancellationToken);

    public void Dispose() => limiter.Dispose();
}
