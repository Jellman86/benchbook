using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrowLog.Api.Climate;
using GrowLog.Api.Data;
using GrowLog.Api.Production;
using GrowLog.Api.Reminders;
using GrowLog.Api.Work;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Assistant;

/// <summary>
/// What Ask GrowLog can look up in the nursery's records. Every tool only reads, and none returns customers, money or
/// who did the work. Each records what it looked at, so the answer can link to it.
/// </summary>
internal static class AssistantTools
{
    private const int MostRows = 80;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The tools, as OpenRouter's function definitions, less those for parts of GrowLog that are turned off.</summary>
    public static JsonArray Definitions(IReadOnlyCollection<NurseryFeature> off)
    {
        var all = JsonNode.Parse(DefinitionsJson)!.AsArray();
        var keep = new JsonArray();

        foreach (var tool in all.ToList())
        {
            var name = tool!["function"]!["name"]!.GetValue<string>();

            if ((string.Equals(name, "climate", StringComparison.Ordinal) && off.Contains(NurseryFeature.Climate)) || (string.Equals(name, "jobs", StringComparison.Ordinal) && off.Contains(NurseryFeature.Jobs)))
            {
                continue;
            }

            all.Remove(tool);
            keep.Add(tool);
        }

        return keep;
    }

    public static async Task<string> RunAsync(string name, JsonNode? arguments, ToolContext context)
    {
        object result = name switch
        {
            "places" => await PlacesAsync(context),
            "find_plants" => await FindPlantsAsync(Text(arguments, "query") ?? string.Empty, context),
            "stock" => await StockAsync(arguments, context),
            "tray" => await TrayAsync(Text(arguments, "label") ?? string.Empty, context),
            "losses" => await LossesAsync(Days(arguments, 30), context),
            "treatments" => await TreatmentsAsync(Days(arguments, 30), context),
            "climate" => await ClimateAsync(Guid.TryParse(Text(arguments, "place_id"), out var place) ? place : null, Days(arguments, 7), context),
            "needs_a_look" => await NeedsALookAsync(context),
            "jobs" => await JobsAsync(context),
            _ => new { problem = $"There's no tool called {name}." },
        };

        return JsonSerializer.Serialize(result, Json);
    }

    private static string? Text(JsonNode? arguments, string name) =>
        arguments?[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    private static int Days(JsonNode? arguments, int fallback) =>
        arguments?["days"] is JsonValue value && value.TryGetValue<int>(out var days) ? Math.Clamp(days, 1, 365) : fallback;

    private static async Task<object> PlacesAsync(ToolContext context)
    {
        var places = await context.Db.Locations.AsNoTracking().Where(place => place.ArchivedAt == null)
            .Select(place => new { place.Id, place.Name, place.Kind, place.ParentId, place.LabelCode })
            .ToListAsync(context.Cancellation);

        return places.ConvertAll(place =>
        {
            context.Saw("place", place.Id.ToString(), place.Name);
            return new { id = place.Id, name = place.Name, kind = place.Kind.ToString(), insideId = place.ParentId, label = place.LabelCode };
        });
    }

    private static async Task<object> FindPlantsAsync(string query, ToolContext context)
    {
        var pattern = $"%{query.Replace("%", string.Empty, StringComparison.Ordinal)}%";
        var plants = await context.Db.Plants.AsNoTracking()
            .Where(plant => plant.ArchivedAt == null
                && (EF.Functions.ILike(plant.CommonName, pattern)
                    || (plant.Variety != null && EF.Functions.ILike(plant.Variety, pattern))
                    || (plant.BotanicalName != null && EF.Functions.ILike(plant.BotanicalName, pattern))))
            .Take(20)
            .ToListAsync(context.Cancellation);
        var stock = await ProductionEndpoints.ListAsync(context.Db, context.Store, context.Cancellation);

        return plants.ConvertAll(plant =>
        {
            context.Saw("plant", plant.Id.ToString(), PlantName(plant.CommonName, plant.Variety));
            var rows = stock.Where(row => row.PlantId == plant.Id).ToList();
            return new
            {
                id = plant.Id,
                name = PlantName(plant.CommonName, plant.Variety),
                botanicalName = plant.BotanicalName,
                plant.Category,
                plant.Position,
                plant.Watering,
                plant.Hardiness,
                xylellaHost = plant.XylellaHost,
                traysGrowing = rows.Select(row => row.ContainerId).Distinct().Count(),
                plantsGrowing = rows.Sum(row => row.Quantity),
            };
        });
    }

    private static async Task<object> StockAsync(JsonNode? arguments, ToolContext context)
    {
        var rows = await ProductionEndpoints.ListAsync(context.Db, context.Store, context.Cancellation);
        var stage = Text(arguments, "stage");
        var place = Guid.TryParse(Text(arguments, "place_id"), out var placeId) ? placeId : (Guid?)null;
        var plant = Guid.TryParse(Text(arguments, "plant_id"), out var plantId) ? plantId : (Guid?)null;
        var now = context.Clock.GetUtcNow();
        var matching = rows
            .Where(row => stage is null || string.Equals(row.Stage?.ToString(), stage, StringComparison.OrdinalIgnoreCase))
            .Where(row => place is null || row.LocationId == place)
            .Where(row => plant is null || row.PlantId == plant)
            .ToList();

        return new
        {
            trays = matching.Select(row => row.ContainerId).Distinct().Count(),
            plants = matching.Sum(row => row.Quantity),
            notCountedTrays = matching.Where(row => row.NotCounted).Select(row => row.ContainerId).Distinct().Count(),
            rows = matching.Take(MostRows).Select(row =>
            {
                context.Saw("tray", row.LabelCode, $"{row.LabelCode} {PlantName(row.CommonName, row.Variety)}");
                return new
                {
                    tray = row.LabelCode,
                    plant = PlantName(row.CommonName, row.Variety),
                    batch = row.TraceabilityCode,
                    quantity = row.NotCounted ? (int?)null : row.Quantity,
                    stage = row.Stage?.ToString(),
                    daysAtStage = row.StageSince is { } since ? (int)(now - since).TotalDays : (int?)null,
                    place = row.LocationName,
                    pot = row.PotSizeName,
                };
            }),
            shown = Math.Min(matching.Count, MostRows),
        };
    }

    private static async Task<object> TrayAsync(string label, ToolContext context)
    {
        // Label codes are kept in capitals, such as C000012.
        var code = label.Trim().ToUpperInvariant();
        var container = await context.Db.Containers.AsNoTracking()
            .FirstOrDefaultAsync(item => item.LabelCode == code, context.Cancellation);

        if (container is null)
        {
            return new { problem = $"No tray labelled {label}." };
        }

        context.Saw("tray", container.LabelCode, $"Tray {container.LabelCode}");
        var rows = (await ProductionEndpoints.ListAsync(context.Db, context.Store, context.Cancellation))
            .Where(row => row.ContainerId == container.Id)
            .ToList();
        var history = await ContainerHistoryEndpoints.HistoryAsync(container.Id, context.Db, context.Cancellation);

        return new
        {
            tray = container.LabelCode,
            holds = rows.Select(row => new
            {
                plant = PlantName(row.CommonName, row.Variety),
                batch = row.TraceabilityCode,
                quantity = row.NotCounted ? (int?)null : row.Quantity,
                stage = row.Stage?.ToString(),
                since = row.StageSince?.ToString("d MMM yyyy", CultureInfo.GetCultureInfo("en-GB")),
                place = row.LocationName,
                pot = row.PotSizeName,
            }),
            // Who did the work is left out: it's about the plants, not the people.
            history = history.Where(entry => !entry.Undone).Take(MostRows).Select(entry => new
            {
                what = entry.Type.ToString(),
                when = entry.OccurredAt.ToString("d MMM yyyy HH:mm", CultureInfo.GetCultureInfo("en-GB")),
                quantity = entry.Quantity,
                place = entry.LocationName,
                pot = entry.PotSizeName,
                details = entry.Details,
            }),
        };
    }

    private static async Task<object> LossesAsync(int days, ToolContext context) =>
        await WorkOfTypeAsync(EventType.Lost, days, context, payload => new { reason = Field(payload, "reason") });

    private static async Task<object> TreatmentsAsync(int days, ToolContext context) =>
        await WorkOfTypeAsync(EventType.Treated, days, context, payload => new { product = Field(payload, "product"), dose = Field(payload, "dose"), area = Field(payload, "area") });

    private static async Task<object> WorkOfTypeAsync(EventType type, int days, ToolContext context, Func<string, object> describe)
    {
        var since = context.Clock.GetUtcNow().AddDays(-days);
        var work = await context.Db.Events.AsNoTracking()
            .Where(item => item.Type == type && item.OccurredAt >= since && !context.Db.Events.Any(undo => undo.CorrectsEventId == item.Id))
            .OrderByDescending(item => item.OccurredAt)
            .Take(MostRows)
            .Select(item => new
            {
                item.OccurredAt,
                item.Payload,
                Tray = context.Db.Containers.Where(tray => tray.Id == item.ContainerId).Select(tray => tray.LabelCode).FirstOrDefault(),
                Lines = item.Lines.Select(line => new
                {
                    line.QuantityChange,
                    Plant = context.Db.Batches.Where(batch => batch.Id == line.BatchId)
                        .Select(batch => context.Db.Plants.Where(plant => plant.Id == batch.PlantId).Select(plant => plant.CommonName + (plant.Variety == null ? string.Empty : " '" + plant.Variety + "'")).FirstOrDefault())
                        .FirstOrDefault(),
                }),
            })
            .ToListAsync(context.Cancellation);

        return new
        {
            days,
            count = work.Count,
            work = work.Select(item => new
            {
                when = item.OccurredAt.ToString("d MMM yyyy", CultureInfo.GetCultureInfo("en-GB")),
                tray = item.Tray,
                details = describe(item.Payload),
                plants = item.Lines.Select(line => new { plant = line.Plant, change = line.QuantityChange }),
            }),
        };
    }

    private static string? Field(string payload, string name)
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static async Task<object> ClimateAsync(Guid? place, int days, ToolContext context)
    {
        if (place is null)
        {
            return (await ClimateEndpoints.ListAsync(context.Db, context.Clock, context.Cancellation))
                .Select(item => new { placeId = item.LocationId, place = item.Place, latest = item.Latest, today = item.Today });
        }

        var since = context.Clock.GetUtcNow().AddDays(-days);
        var readings = await context.Db.SensorReadings.AsNoTracking()
            .Where(reading => reading.LocationId == place && reading.TakenAt >= since)
            .Select(reading => new { reading.TakenAt, reading.Temperature, reading.Humidity })
            .ToListAsync(context.Cancellation);

        return readings
            .GroupBy(reading => DateOnly.FromDateTime(reading.TakenAt.UtcDateTime))
            .OrderBy(day => day.Key)
            .Select(day => new
            {
                day = day.Key.ToString("d MMM", CultureInfo.GetCultureInfo("en-GB")),
                lowest = day.Min(reading => reading.Temperature),
                highest = day.Max(reading => reading.Temperature),
                humidity = day.Average(reading => reading.Humidity),
            });
    }

    private static async Task<object> NeedsALookAsync(ToolContext context) =>
        (await AlertEndpoints.ListAsync(context.Db, context.Store, context.Clock, context.Cancellation))
            .Where(alert => !string.Equals(alert.Kind, "InvoicesOverdue", StringComparison.Ordinal))
            .Select(alert => new { alert.Title, alert.Why });

    private static async Task<object> JobsAsync(ToolContext context) =>
        (await JobEndpoints.ListAsync(context.Db, context.Clock, context.Cancellation))
            .Select(job => new { job.Title, job.Notes, job.EveryDays, nextDue = job.NextDue.ToString("d MMM yyyy", CultureInfo.GetCultureInfo("en-GB")), place = job.LocationName, job.Due, job.DaysLate });

    private static string PlantName(string commonName, string? variety) => variety is null ? commonName : $"{commonName} '{variety}'";

    private const string DefinitionsJson = """
        [
          {"type":"function","function":{"name":"places","description":"Every place in the nursery: sites, greenhouses, polytunnels, benches, beds, with which place each sits in.","parameters":{"type":"object","properties":{}}}},
          {"type":"function","function":{"name":"find_plants","description":"Find the nursery's plants by common name, variety or botanical name, with their care notes and how many are growing.","parameters":{"type":"object","properties":{"query":{"type":"string","description":"Part of a name, such as 'lavender'."}},"required":["query"]}}},
          {"type":"function","function":{"name":"stock","description":"What is growing now: trays with their plant, batch, quantity (null if not counted yet), stage, days at that stage, place and pot. Optionally only one stage, place or plant.","parameters":{"type":"object","properties":{"stage":{"type":"string","enum":["Sown","Cuttings","BoughtIn","Germinated","PrickedOut","PottedOn","Ready"]},"place_id":{"type":"string"},"plant_id":{"type":"string"}}}}},
          {"type":"function","function":{"name":"tray","description":"One tray by its label code, such as C000012: what it holds and everything recorded on it.","parameters":{"type":"object","properties":{"label":{"type":"string"}},"required":["label"]}}},
          {"type":"function","function":{"name":"losses","description":"Plants lost over the last so many days, with the reason, tray and plant.","parameters":{"type":"object","properties":{"days":{"type":"integer","minimum":1,"maximum":365}}}}},
          {"type":"function","function":{"name":"treatments","description":"Treatments recorded over the last so many days: product, dose, area, tray and plants.","parameters":{"type":"object","properties":{"days":{"type":"integer","minimum":1,"maximum":365}}}}},
          {"type":"function","function":{"name":"climate","description":"Temperature and humidity from the nursery's sensors. Without a place: every place's latest reading and today's range. With a place: each day's lowest and highest temperature and average humidity.","parameters":{"type":"object","properties":{"place_id":{"type":"string"},"days":{"type":"integer","minimum":1,"maximum":31}}}}},
          {"type":"function","function":{"name":"needs_a_look","description":"What the nursery's records say wants attention now: trays ready a long time, seed not counted, late jobs, renewals.","parameters":{"type":"object","properties":{}}}},
          {"type":"function","function":{"name":"jobs","description":"The nursery's repeating jobs, such as watering and spraying rotas, with when each is next due.","parameters":{"type":"object","properties":{}}}}
        ]
        """;
}
