using GrowLog.Api.Catalogue;
using GrowLog.Api.Data;
using GrowLog.Api.Settings;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Climate;

/// <summary>How warm and damp each place with a sensor is: now, today, and hour by hour for a chart.</summary>
internal static class ClimateEndpoints
{
    public static void MapClimateEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/climate", ListAsync).RequireFeature(NurseryFeature.Climate);
        app.MapGet("/api/climate/{locationId:guid}", HistoryAsync).Produces<PlaceClimateHistory>().RequireFeature(NurseryFeature.Climate);
    }

    private static DateTimeOffset StartOfToday(TimeProvider clock)
    {
        var local = clock.GetLocalNow();
        return new DateTimeOffset(local.Date, local.Offset);
    }

    /// <summary>Every place with a sensor in it, or a reading in the last month.</summary>
    private static async Task<List<PlaceClimate>> ListAsync(GrowLogDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var today = StartOfToday(clock);
        var month = clock.GetUtcNow().AddDays(-31);
        var paths = await PlacePaths.LoadAsync(db, cancellationToken);
        var places = await db.Sensors.Where(sensor => sensor.ArchivedAt == null).Select(sensor => sensor.LocationId)
            .Union(db.SensorReadings.Where(reading => reading.TakenAt > month).Select(reading => reading.LocationId))
            .ToListAsync(cancellationToken);
        var latest = await db.SensorReadings
            .Where(reading => places.Contains(reading.LocationId))
            .GroupBy(reading => reading.LocationId)
            .Select(group => group.OrderByDescending(reading => reading.TakenAt).First())
            .ToDictionaryAsync(reading => reading.LocationId, cancellationToken);
        var todays = await db.SensorReadings
            .Where(reading => places.Contains(reading.LocationId) && reading.TakenAt >= today)
            .GroupBy(reading => reading.LocationId)
            .Select(group => new
            {
                group.Key,
                Range = new ClimateRange(
                    group.Min(reading => reading.Temperature),
                    group.Max(reading => reading.Temperature),
                    group.Min(reading => reading.Humidity),
                    group.Max(reading => reading.Humidity)),
            })
            .ToDictionaryAsync(row => row.Key, row => row.Range, cancellationToken);

        return [.. places
            .Select(place => new PlaceClimate(
                place,
                paths.GetValueOrDefault(place, ""),
                latest.TryGetValue(place, out var reading) ? new ClimateReading(reading.TakenAt, reading.Temperature, reading.Humidity) : null,
                todays.GetValueOrDefault(place)))
            .OrderBy(place => place.Place, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>A place's readings hour by hour over the last few days: the lowest, highest and average in each hour.</summary>
    private static async Task<IResult> HistoryAsync(Guid locationId, GrowLogDbContext db, TimeProvider clock, CancellationToken cancellationToken, int days = 7)
    {
        if (days is < 1 or > 31)
        {
            return TypedResults.BadRequest();
        }

        var paths = await PlacePaths.LoadAsync(db, cancellationToken);

        if (!paths.TryGetValue(locationId, out var place))
        {
            return TypedResults.NotFound();
        }

        var since = StartOfToday(clock).AddDays(1 - days);
        var readings = await db.SensorReadings
            .AsNoTracking()
            .Where(reading => reading.LocationId == locationId && reading.TakenAt >= since)
            .Select(reading => new { reading.TakenAt, reading.Temperature, reading.Humidity })
            .ToListAsync(cancellationToken);

        var hours = readings
            .GroupBy(reading => new DateTimeOffset(reading.TakenAt.UtcDateTime.Date.AddHours(reading.TakenAt.UtcDateTime.Hour), TimeSpan.Zero))
            .OrderBy(group => group.Key)
            .Select(group => new ClimateHour(
                group.Key,
                Average(group.Select(reading => reading.Temperature)),
                group.Min(reading => reading.Temperature),
                group.Max(reading => reading.Temperature),
                Average(group.Select(reading => reading.Humidity))))
            .ToList();

        return TypedResults.Ok(new PlaceClimateHistory(locationId, place, since, hours));
    }

    private static decimal? Average(IEnumerable<decimal?> values)
    {
        var known = values.OfType<decimal>().ToList();
        return known.Count == 0 ? null : Math.Round(known.Average(), 1);
    }

    internal sealed record ClimateReading(DateTimeOffset TakenAt, decimal? Temperature, decimal? Humidity);

    internal sealed record ClimateRange(decimal? LowestTemperature, decimal? HighestTemperature, decimal? LowestHumidity, decimal? HighestHumidity);

    internal sealed record PlaceClimate(Guid LocationId, string Place, ClimateReading? Latest, ClimateRange? Today);

    internal sealed record ClimateHour(DateTimeOffset Hour, decimal? Temperature, decimal? LowestTemperature, decimal? HighestTemperature, decimal? Humidity);

    internal sealed record PlaceClimateHistory(Guid LocationId, string Place, DateTimeOffset Since, IReadOnlyList<ClimateHour> Hours);
}
