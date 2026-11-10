using System.Globalization;
using GrowLog.Api.Accounts;
using GrowLog.Api.Catalogue;
using GrowLog.Api.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Labels;

internal static class LabelEndpoints
{
    public static void MapLabelEndpoints(this IEndpointRouteBuilder app)
    {
        var labels = app.MapGroup("/api/labels");
        labels.MapGet("/containers/{label}", PrintContainerLabelAsync).ProducesPdf();
        labels.MapGet("/plants/{id:guid}", PrintPlantLabelAsync).ProducesPdf();
        labels.MapGet("/bench-cards/{id:guid}", PrintBenchCardAsync).ProducesPdf();
        labels.MapGet("/places/{id:guid}", PrintPlaceLabelAsync).ProducesPdf();
        labels.MapPost("/preview/{kind}", PreviewAsync).ProducesPdf().RequireAuthorization(AccountServices.OwnerPolicy);
    }

    private static RouteHandlerBuilder ProducesPdf(this RouteHandlerBuilder builder) =>
        builder.Produces(StatusCodes.Status200OK, contentType: "application/pdf");

    private static string PlantName(string commonName, string? variety) =>
        variety is null ? commonName : $"{commonName} '{variety}'";

    private static string Money(decimal value) =>
        string.Create(CultureInfo.InvariantCulture, $"£{value:0.00}");

    private static string Care(params string?[] parts) =>
        string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));

    private static FileContentHttpResult Pdf(byte[] content, string name) =>
        TypedResults.File(content, "application/pdf", $"{name}.pdf");

    private static async Task<IResult> PrintContainerLabelAsync(
        string label,
        GrowLogDbContext db,
        EventStore store,
        LabelPrinter printer,
        CancellationToken cancellationToken,
        int copies = 1)
    {
        if (await db.Containers.AsNoTracking().FirstOrDefaultAsync(container => container.LabelCode == label, cancellationToken) is not { } container)
        {
            return TypedResults.NotFound();
        }

        var state = await store.GetContainerStateAsync(container.Id, cancellationToken);
        var batchIds = state.Contents.Select(count => count.BatchId).ToList();

        var lines = await db.Batches
            .Where(batch => batchIds.Contains(batch.Id))
            .Join(db.Plants, batch => batch.PlantId, plant => plant.Id, (batch, plant) => new { batch.TraceabilityCode, plant.CommonName, plant.Variety, plant.BotanicalName })
            .ToListAsync(cancellationToken);

        var data = new
        {
            code = container.LabelCode,
            lines = lines.ConvertAll(line => new
            {
                plant = PlantName(line.CommonName, line.Variety),
                botanical = line.BotanicalName ?? string.Empty,
                batch = line.TraceabilityCode,
            }),
        };

        var context = await LabelSettings.LoadAsync(db, LabelKind.ContainerLabel, cancellationToken);
        return Pdf(await printer.RenderAsync(LabelKind.ContainerLabel, data, container.LabelCode, context, copies, cancellationToken), container.LabelCode);
    }

    /// <summary>A label for a bench or bed, so moving a tray can be "scan the tray, scan the bench".</summary>
    private static async Task<IResult> PrintPlaceLabelAsync(Guid id, GrowLogDbContext db, LabelPrinter printer, CancellationToken cancellationToken, int copies = 1)
    {
        if (await db.Locations.AsNoTracking().FirstOrDefaultAsync(place => place.Id == id, cancellationToken) is not { } place)
        {
            return TypedResults.NotFound();
        }

        var paths = await PlacePaths.LoadAsync(db, cancellationToken);
        var data = new { code = place.LabelCode, name = place.Name, path = paths.GetValueOrDefault(place.Id) ?? place.Name };
        var context = await LabelSettings.LoadAsync(db, LabelKind.PlaceLabel, cancellationToken);

        return Pdf(await printer.RenderAsync(LabelKind.PlaceLabel, data, place.LabelCode, context, copies, cancellationToken), place.LabelCode);
    }

    private static async Task<IResult> PrintPlantLabelAsync(
        Guid id,
        Guid? potSizeId,
        GrowLogDbContext db,
        LabelPrinter printer,
        CancellationToken cancellationToken,
        int copies = 1)
    {
        if (await db.Plants.AsNoTracking().Include(plant => plant.Prices).FirstOrDefaultAsync(plant => plant.Id == id, cancellationToken) is not { } plant)
        {
            return TypedResults.NotFound();
        }

        var price = plant.Prices.FirstOrDefault(entry => entry.PotSizeId == potSizeId);

        var data = new
        {
            plant = PlantName(plant.CommonName, plant.Variety),
            botanical = plant.BotanicalName ?? string.Empty,
            price = price is null ? string.Empty : Money(price.Price),
            care = Care(plant.Position, plant.Watering, plant.Hardiness),
        };

        var context = await LabelSettings.LoadAsync(db, LabelKind.PlantLabel, cancellationToken);
        return Pdf(await printer.RenderAsync(LabelKind.PlantLabel, data, qrText: null, context, copies, cancellationToken), "plant-label");
    }

    private static async Task<IResult> PrintBenchCardAsync(
        Guid id,
        GrowLogDbContext db,
        LabelPrinter printer,
        CancellationToken cancellationToken,
        int copies = 1)
    {
        if (await db.Plants.AsNoTracking().Include(plant => plant.Prices).FirstOrDefaultAsync(plant => plant.Id == id, cancellationToken) is not { } plant)
        {
            return TypedResults.NotFound();
        }

        var potSizeNames = await db.PotSizes.AsNoTracking().ToDictionaryAsync(size => size.Id, size => size.Name, cancellationToken);

        var data = new
        {
            plant = PlantName(plant.CommonName, plant.Variety),
            botanical = plant.BotanicalName ?? string.Empty,
            position = plant.Position ?? string.Empty,
            watering = plant.Watering ?? string.Empty,
            hardiness = plant.Hardiness ?? string.Empty,
            prices = plant.Prices.Select(price => new { size = potSizeNames[price.PotSizeId], price = Money(price.Price) }).ToList(),
        };

        var context = await LabelSettings.LoadAsync(db, LabelKind.BenchCard, cancellationToken);
        return Pdf(await printer.RenderAsync(LabelKind.BenchCard, data, qrText: null, context, copies, cancellationToken), "bench-card");
    }

    /// <summary>A sample label in a style not yet saved, so the owner sees a change before keeping it.</summary>
    private static async Task<IResult> PreviewAsync(LabelKind kind, LabelStyle style, GrowLogDbContext db, LabelPrinter printer, CancellationToken cancellationToken)
    {
        if (!style.IsSensible)
        {
            return TypedResults.BadRequest();
        }

        var saved = await LabelSettings.LoadAsync(db, kind, cancellationToken);
        var context = saved with { Style = style };

        object sample = kind switch
        {
            LabelKind.PlantLabel => new { plant = "Lavender 'Hidcote'", botanical = "Lavandula angustifolia", price = Money(4.5m), care = "Full sun · Well-drained soil · RHS H5" },
            LabelKind.BenchCard => new
            {
                plant = "Lavender 'Hidcote'",
                botanical = "Lavandula angustifolia",
                position = "Full sun",
                watering = "Well-drained soil",
                hardiness = "RHS H5",
                prices = new[] { new { size = "9cm", price = Money(4.5m) }, new { size = "2L", price = Money(9.95m) } },
            },
            LabelKind.PlaceLabel => new { code = "P0001", name = "Bench 1", path = "Greenhouse 2, Bench 1" },
            _ => new { code = "C000001", lines = new[] { new { plant = "Lavender 'Hidcote'", botanical = "Lavandula angustifolia", batch = "B000001" } } },
        };

        var qr = kind is LabelKind.ContainerLabel or LabelKind.PlaceLabel ? "PREVIEW" : null;
        return Pdf(await printer.RenderAsync(kind, sample, qr, context, copies: 1, cancellationToken), "preview");
    }
}
