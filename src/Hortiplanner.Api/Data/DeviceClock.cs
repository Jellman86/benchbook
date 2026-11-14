namespace Hortiplanner.Api.Data;

/// <summary>
/// How far the sending device's clock is from the server's, from the time it says it sent the request.
/// A device whose clock is wrong would otherwise put work in the wrong order, before or after a count.
/// </summary>
internal sealed class DeviceClock
{
    public const string Header = "X-Device-Time";

    /// <summary>Differences smaller than this are ordinary and left alone.</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(2);

    /// <summary>Server time minus device time, when the device said.</summary>
    public string? RequestFingerprint { get; set; }

    public TimeSpan? Skew { get; set; }
}
