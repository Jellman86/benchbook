namespace GrowLog.Api.Data;

/// <summary>One reading from a sensor, kept against the place the sensor was in at the time.</summary>
internal sealed class SensorReading
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid SensorId { get; init; }

    public required Guid LocationId { get; init; }

    public required DateTimeOffset TakenAt { get; init; }

    /// <summary>Degrees Celsius.</summary>
    public decimal? Temperature { get; init; }

    /// <summary>Relative humidity, as a percentage.</summary>
    public decimal? Humidity { get; init; }
}
