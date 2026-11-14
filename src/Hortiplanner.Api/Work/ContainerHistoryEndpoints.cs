using System.Text.Json;
using Hortiplanner.Api.Catalogue;
using Hortiplanner.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Work;

/// <summary>What has happened to a container, and choices remembered from earlier work.</summary>
internal static class ContainerHistoryEndpoints
{
    private const int LongestHistory = 60;
    private const int TreatmentsLookedAt = 200;
    private const int ChoicesOffered = 8;

    public static void MapContainerHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/containers/{id:guid}/history", HistoryAsync);
        app.MapGet("/api/treatments/choices", TreatmentChoicesAsync);
    }

    /// <summary>Newest first. Work that has been undone is shown as undone rather than hidden.</summary>
    internal static async Task<List<HistoryEntry>> HistoryAsync(Guid id, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.Events
            .AsNoTracking()
            .Where(e => e.Type != EventType.Corrected && (e.ContainerId == id || e.Lines.Any(line => line.ContainerId == id)))
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.RecordedAt)
            .Take(LongestHistory)
            .Select(e => new
            {
                e.Id,
                e.Type,
                e.OccurredAt,
                RecordedBy = db.Users.Where(user => user.Id == e.RecordedBy).Select(user => user.UserName).FirstOrDefault(),
                Quantity = e.Lines.Where(line => line.ContainerId == id).Sum(line => (int?)line.QuantityChange),
                e.LocationId,
                PotSizeName = db.PotSizes.Where(size => size.Id == e.PotSizeId).Select(size => size.Name).FirstOrDefault(),
                e.Payload,
                Undone = db.Events.Any(c => c.CorrectsEventId == e.Id),
            })
            .ToListAsync(cancellationToken);

        var paths = await PlacePaths.LoadAsync(db, cancellationToken);

        return rows.ConvertAll(row => new HistoryEntry(
            row.Id,
            row.Type,
            row.OccurredAt,
            row.RecordedBy,
            row.Quantity,
            row.LocationId is { } locationId ? paths.GetValueOrDefault(locationId) : null,
            row.PotSizeName,
            JsonSerializer.Deserialize<JsonElement>(row.Payload),
            row.Undone));
    }

    /// <summary>Products, doses and areas used before, most recent first, so they can be chosen rather than typed.</summary>
    private static async Task<TreatmentChoices> TreatmentChoicesAsync(HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        var payloads = await db.Events
            .AsNoTracking()
            .Where(e => e.Type == EventType.Treated && !db.Events.Any(c => c.CorrectsEventId == e.Id))
            .OrderByDescending(e => e.RecordedAt)
            .Take(TreatmentsLookedAt)
            .Select(e => e.Payload)
            .ToListAsync(cancellationToken);

        var treatments = payloads.ConvertAll(payload => JsonSerializer.Deserialize<JsonElement>(payload));

        return new TreatmentChoices(
            Recent(treatments, "product"),
            Recent(treatments, "dose"),
            Recent(treatments, "area"));
    }

    private static List<string> Recent(List<JsonElement> treatments, string property) =>
        treatments
            .Select(treatment => treatment.TryGetProperty(property, out var value) ? value.GetString() : null)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(ChoicesOffered)
            .ToList();

    internal sealed record HistoryEntry(
        Guid EventId,
        EventType Type,
        DateTimeOffset OccurredAt,
        string? RecordedBy,
        int? Quantity,
        string? LocationName,
        string? PotSizeName,
        JsonElement Details,
        bool Undone);

    internal sealed record TreatmentChoices(IReadOnlyList<string> Products, IReadOnlyList<string> Doses, IReadOnlyList<string> Areas);
}
