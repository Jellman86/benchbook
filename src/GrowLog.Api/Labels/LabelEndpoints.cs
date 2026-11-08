using System.Globalization;
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
    }

    private static RouteHandlerBuilder ProducesPdf(this RouteHandlerBuilder builder) =>
        builder.Produces(StatusCodes.Status200OK, contentType: "application/pdf");

    private static string PlantName(string commonName, string? variety) =>
        variety is null ? commonName : $"{commonName} '{variety}'";

    private static string Money(decimal value) =>
        string.Create(CultureInfo.InvariantCulture, $"£{value:0.00}");

    private static FileContentHttpResult Pdf(byte[] content, string name) =>
        TypedResults.File(content, "application/pdf", $"{name}.pdf");

    private static async Task<IResult> PrintContainerLabelAsync(
        string label,
        GrowLogDbContext db,
        EventStore store,
        LabelPrinter printer,
        CancellationToken cancellationToken)
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

        return Pdf(await printer.RenderAsync(LabelKind.ContainerLabel, data, container.LabelCode, cancellationToken), container.LabelCode);
    }

    private static async Task<IResult> PrintPlantLabelAsync(
        Guid id,
        Guid? potSizeId,
        GrowLogDbContext db,
        LabelPrinter printer,
        CancellationToken cancellationToken)
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
        };

        return Pdf(await printer.RenderAsync(LabelKind.PlantLabel, data, qrText: null, cancellationToken), "plant-label");
    }

    private static async Task<IResult> PrintBenchCardAsync(
        Guid id,
        GrowLogDbContext db,
        LabelPrinter printer,
        CancellationToken cancellationToken)
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
            prices = plant.Prices.Select(price => new { size = potSizeNames[price.PotSizeId], price = Money(price.Price) }).ToList(),
        };

        return Pdf(await printer.RenderAsync(LabelKind.BenchCard, data, qrText: null, cancellationToken), "bench-card");
    }
}
