using System.Security.Cryptography;
using System.Text;
using Hortiplanner.Api.Accounts;
using Hortiplanner.Api.Data;
using Hortiplanner.Api.Settings;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hortiplanner.Api.Climate;

/// <summary>
/// Sensors the owner sets up, each in a place with its own key, and the readings they send. A reading carries the
/// sensor's key rather than anyone's sign-in, as a sensor can't sign in.
/// </summary>
internal static class SensorEndpoints
{
    private const int LongestName = 100;

    private const int KeptYears = 2;

    /// <summary>A sensor sending more often than this is refused, so a fault can't fill the database.</summary>
    private static readonly TimeSpan Fastest = TimeSpan.FromMinutes(1);

    public static void MapSensorEndpoints(this IEndpointRouteBuilder app)
    {
        var sensors = app.MapGroup("/api/sensors").RequireAuthorization(AccountServices.OwnerPolicy).RequireFeature(NurseryFeature.Climate);
        sensors.MapGet("/", ListAsync);
        sensors.MapPost("/", AddAsync).Produces<SensorKey>(StatusCodes.Status201Created);
        sensors.MapPut("/{id:guid}", ChangeAsync);
        sensors.MapPost("/{id:guid}/new-key", NewKeyAsync).Produces<SensorKey>();
        sensors.MapPost("/{id:guid}/stop", StopAsync);

        app.MapPost("/api/sensor-readings", ReceiveAsync).AllowAnonymous();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>A new key, shown once. "glk_" (from the old name, GrowLog) marks it as one of
    /// this app's keys wherever it ends up. Keys already handed out start with it, so it stays.</summary>
    private static (string Key, byte[] Hash) NewKey()
    {
        var key = $"glk_{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";
        return (key, HashOf(key));
    }

    private static byte[] HashOf(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));

    private static async Task<List<SensorSummary>> ListAsync(HortiplannerDbContext db, CancellationToken cancellationToken) =>
        await db.Sensors
            .AsNoTracking()
            .Where(sensor => sensor.ArchivedAt == null)
            .OrderBy(sensor => sensor.Name)
            .Select(sensor => new SensorSummary(
                sensor.Id,
                sensor.Name,
                sensor.LocationId,
                db.SensorReadings.Where(reading => reading.SensorId == sensor.Id).Max(reading => (DateTimeOffset?)reading.TakenAt)))
            .ToListAsync(cancellationToken);

    private static async Task<IResult> AddAsync(SensorRequest request, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        if (Clean(request.Name) is not { Length: <= LongestName } name || !await db.Locations.AnyAsync(place => place.Id == request.LocationId && place.ArchivedAt == null, cancellationToken))
        {
            return TypedResults.BadRequest();
        }

        var (key, hash) = NewKey();
        var sensor = new Sensor { Name = name, LocationId = request.LocationId, KeyHash = hash };
        db.Sensors.Add(sensor);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return TypedResults.Conflict();
        }

        return TypedResults.Created($"/api/sensors/{sensor.Id}", new SensorKey(sensor.Id, key));
    }

    /// <summary>A sensor moved to another place: readings from now on count for the new place.</summary>
    private static async Task<IResult> ChangeAsync(Guid id, SensorRequest request, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        if (Clean(request.Name) is not { Length: <= LongestName } name || !await db.Locations.AnyAsync(place => place.Id == request.LocationId && place.ArchivedAt == null, cancellationToken))
        {
            return TypedResults.BadRequest();
        }

        if (await db.Sensors.FirstOrDefaultAsync(sensor => sensor.Id == id && sensor.ArchivedAt == null, cancellationToken) is not { } sensor)
        {
            return TypedResults.NotFound();
        }

        sensor.Name = name;
        sensor.LocationId = request.LocationId;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.NoContent();
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return TypedResults.Conflict();
        }
    }

    /// <summary>A new key for a sensor whose key was lost or seen by someone else. The old key stops working.</summary>
    private static async Task<IResult> NewKeyAsync(Guid id, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Sensors.FirstOrDefaultAsync(sensor => sensor.Id == id && sensor.ArchivedAt == null, cancellationToken) is not { } sensor)
        {
            return TypedResults.NotFound();
        }

        var (key, hash) = NewKey();
        sensor.KeyHash = hash;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new SensorKey(sensor.Id, key));
    }

    /// <summary>A sensor no longer used. Its key stops working and its readings stay.</summary>
    private static async Task<IResult> StopAsync(Guid id, HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (await db.Sensors.FirstOrDefaultAsync(sensor => sensor.Id == id, cancellationToken) is not { } sensor)
        {
            return TypedResults.NotFound();
        }

        sensor.ArchivedAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ReceiveAsync(SensorReadingRequest request, HttpRequest http, HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var header = http.Headers.Authorization.ToString();

        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.Unauthorized();
        }

        var hash = HashOf(header["Bearer ".Length..].Trim());

        if (await db.Sensors.AsNoTracking().FirstOrDefaultAsync(sensor => sensor.KeyHash == hash && sensor.ArchivedAt == null, cancellationToken) is not { } sensor)
        {
            return TypedResults.Unauthorized();
        }

        // Checked after the key, so nobody without one learns whether climate is on.
        if (!await db.IsOnAsync(NurseryFeature.Climate, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var now = clock.GetUtcNow();
        var takenAt = request.TakenAt ?? now;

        if (request is { Temperature: null, Humidity: null }
            || request.Temperature is < -50 or > 80
            || request.Humidity is < 0 or > 100
            || takenAt > now.AddMinutes(5)
            || takenAt < now.AddDays(-1))
        {
            return TypedResults.BadRequest();
        }

        // The sensor is held while it's checked, so two readings sent at once can't both get past the limit.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM sensors WHERE id = {0} FOR UPDATE", [sensor.Id], cancellationToken);

        now = clock.GetUtcNow();
        var locked = await db.Sensors.SingleAsync(item => item.Id == sensor.Id, cancellationToken);
        if (locked.ArchivedAt is not null || !locked.KeyHash.AsSpan().SequenceEqual(hash))
        {
            return TypedResults.Unauthorized();
        }
        if (await db.SensorReadings.AnyAsync(reading => reading.SensorId == sensor.Id && reading.TakenAt == takenAt, cancellationToken))
        {
            return TypedResults.NoContent();
        }
        if (locked.LastReceivedAt > now - Fastest)
        {
            return TypedResults.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        await SaveReadingAsync(request, locked, takenAt, now, db, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task SaveReadingAsync(SensorReadingRequest request, Sensor sensor, DateTimeOffset takenAt, DateTimeOffset now, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        // Readings are kept two years, enough to compare a season with last year's.
        var kept = now.AddYears(-KeptYears);
        await db.SensorReadings.Where(reading => reading.SensorId == sensor.Id && reading.TakenAt < kept).ExecuteDeleteAsync(cancellationToken);

        sensor.LastReceivedAt = now;
        db.SensorReadings.Add(new SensorReading
        {
            SensorId = sensor.Id,
            LocationId = sensor.LocationId,
            TakenAt = takenAt,
            Temperature = request.Temperature is { } temperature ? Math.Round(temperature, 2) : null,
            Humidity = request.Humidity is { } humidity ? Math.Round(humidity, 2) : null,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    internal sealed record SensorRequest(string Name, Guid LocationId);

    internal sealed record SensorKey(Guid Id, string Key);

    internal sealed record SensorSummary(Guid Id, string Name, Guid LocationId, DateTimeOffset? LastReadingAt);

    internal sealed record SensorReadingRequest(decimal? Temperature, decimal? Humidity, DateTimeOffset? TakenAt);
}
