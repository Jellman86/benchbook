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
        labels.MapPost("/preview/{kind}", PreviewAsync)
            .ProducesPdf()
            .Produces(StatusCodes.Status200OK, contentType: "image/png")
            .RequireAuthorization(AccountServices.OwnerPolicy);
    }

    private static RouteHandlerBuilder ProducesPdf(this RouteHandlerBuilder builder) =>
        builder.Produces(StatusCodes.Status200OK, contentType: "application/pdf");

    private static string PlantName(string commonName, string? variety) =>
        variety is null ? commonName : $"{commonName} '{variety}'";

    private static string Money(decimal value) =>
        string.Create(CultureInfo.InvariantCulture, $"£{value:0.00}");

    private static string Care(params string?[] parts) =>
        string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));

    /// <summary>Opened in the browser, ready to print, rather than downloaded first.</summary>
    private static FileContentHttpResult Pdf(byte[] content) => TypedResults.File(content, "application/pdf");

    private static async Task<IResult> PdfOfAsync(LabelJob? job, LabelPrinter printer, int copies, CancellationToken cancellationToken) =>
        job is null
            ? TypedResults.NotFound()
            : Pdf(await printer.RenderAsync(job.Kind, job.Data, job.QrText, job.Context, copies, cancellationToken));

    private static async Task<IResult> PrintContainerLabelAsync(
        string label,
        GrowLogDbContext db,
        EventStore store,
        LabelPrinter printer,
        CancellationToken cancellationToken,
        int copies = 1) =>
        await PdfOfAsync(await ContainerLabelAsync(label, db, store, cancellationToken), printer, copies, cancellationToken);

    /// <summary>What goes on a tray's label, or null if there's no such tray.</summary>
    internal static async Task<LabelJob?> ContainerLabelAsync(string label, GrowLogDbContext db, EventStore store, CancellationToken cancellationToken)
    {
        if (await db.Containers.AsNoTracking().FirstOrDefaultAsync(container => container.LabelCode == label, cancellationToken) is not { } container)
        {
            return null;
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
        return new LabelJob(LabelKind.ContainerLabel, data, container.LabelCode, context);
    }

    /// <summary>A label for a bench or bed, so moving a tray can be "scan the tray, scan the bench".</summary>
    private static async Task<IResult> PrintPlaceLabelAsync(Guid id, GrowLogDbContext db, LabelPrinter printer, CancellationToken cancellationToken, int copies = 1) =>
        await PdfOfAsync(await PlaceLabelAsync(id, db, cancellationToken), printer, copies, cancellationToken);

    internal static async Task<LabelJob?> PlaceLabelAsync(Guid id, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Locations.AsNoTracking().FirstOrDefaultAsync(place => place.Id == id, cancellationToken) is not { } place)
        {
            return null;
        }

        var paths = await PlacePaths.LoadAsync(db, cancellationToken);
        var data = new { code = place.LabelCode, name = place.Name, path = paths.GetValueOrDefault(place.Id) ?? place.Name };
        var context = await LabelSettings.LoadAsync(db, LabelKind.PlaceLabel, cancellationToken);

        return new LabelJob(LabelKind.PlaceLabel, data, place.LabelCode, context);
    }

    private static async Task<IResult> PrintPlantLabelAsync(
        Guid id,
        Guid? potSizeId,
        GrowLogDbContext db,
        LabelPrinter printer,
        CancellationToken cancellationToken,
        int copies = 1) =>
        await PdfOfAsync(await PlantLabelAsync(id, potSizeId, db, cancellationToken), printer, copies, cancellationToken);

    internal static async Task<LabelJob?> PlantLabelAsync(Guid id, Guid? potSizeId, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Plants.AsNoTracking().Include(plant => plant.Prices).FirstOrDefaultAsync(plant => plant.Id == id, cancellationToken) is not { } plant)
        {
            return null;
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
        return new LabelJob(LabelKind.PlantLabel, data, null, context);
    }

    private static async Task<IResult> PrintBenchCardAsync(
        Guid id,
        GrowLogDbContext db,
        LabelPrinter printer,
        CancellationToken cancellationToken,
        int copies = 1) =>
        await PdfOfAsync(await BenchCardAsync(id, db, cancellationToken), printer, copies, cancellationToken);

    internal static async Task<LabelJob?> BenchCardAsync(Guid id, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Plants.AsNoTracking().Include(plant => plant.Prices).FirstOrDefaultAsync(plant => plant.Id == id, cancellationToken) is not { } plant)
        {
            return null;
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
        return new LabelJob(LabelKind.BenchCard, data, null, context);
    }

    /// <summary>
    /// A sample label in a style not yet saved, so the owner sees a change before keeping it. It comes
    /// as a PDF to print as a test, or as a picture, which every phone can show inside the page.
    /// </summary>
    private static async Task<IResult> PreviewAsync(
        LabelKind kind,
        LabelStyle style,
        GrowLogDbContext db,
        LabelPrinter printer,
        CancellationToken cancellationToken,
        bool picture = false)
    {
        if (!style.IsSensible)
        {
            return TypedResults.BadRequest();
        }

        var saved = await LabelSettings.LoadAsync(db, kind, cancellationToken);
        var job = Sample(kind, saved with { Style = style });
        var content = await printer.RenderAsync(kind, job.Data, job.QrText, job.Context, copies: 1, cancellationToken, asPicture: picture);
        return picture ? TypedResults.File(content, "image/png") : Pdf(content);
    }

    /// <summary>A made-up label of the given kind, for previews and a printer's test print.</summary>
    internal static LabelJob Sample(LabelKind kind, LabelContext context)
    {
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
        return new LabelJob(kind, sample, qr, context);
    }
}
