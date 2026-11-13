using System.Text.RegularExpressions;
using GrowLog.Api.Accounts;
using GrowLog.Api.Data;
using GrowLog.Api.Labels;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Printing;

/// <summary>
/// The nursery's network label printers, and printing a label straight to one. Printing as a PDF through the
/// device's print window always works too; a printer here only saves that step.
/// </summary>
internal static partial class PrinterEndpoints
{
    private const int LongestName = 100;
    private static readonly int[] Resolutions = [203, 300, 600];
    private static readonly LabelKind[] Printable = [LabelKind.ContainerLabel, LabelKind.PlantLabel, LabelKind.BenchCard, LabelKind.PlaceLabel, LabelKind.PlantPassport];

    public static void MapPrinterEndpoints(this IEndpointRouteBuilder app)
    {
        var printers = app.MapGroup("/api/printers");
        printers.MapGet("/", ListAsync);
        printers.MapPost("/", AddAsync).Produces<PrinterAdded>(StatusCodes.Status201Created).RequireAuthorization(AccountServices.OwnerPolicy);
        printers.MapPut("/{id:guid}", ChangeAsync).RequireAuthorization(AccountServices.OwnerPolicy);
        printers.MapDelete("/{id:guid}", RemoveAsync).RequireAuthorization(AccountServices.OwnerPolicy);
        printers.MapPost("/{id:guid}/test", TestAsync).Produces<PrintProblem>(StatusCodes.Status502BadGateway).RequireAuthorization(AccountServices.OwnerPolicy);
        printers.MapPost("/{id:guid}/print", PrintAsync).Produces<PrintProblem>(StatusCodes.Status502BadGateway);
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.\-:]{0,252}$", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex AddressPattern();

    private static async Task<List<PrinterDetails>> ListAsync(GrowLogDbContext db, CancellationToken cancellationToken) =>
        (await db.Printers.AsNoTracking().OrderBy(printer => printer.Name).ToListAsync(cancellationToken))
            .ConvertAll(printer => new PrinterDetails(
                printer.Id,
                printer.Name,
                printer.Address,
                printer.Port,
                printer.Language,
                printer.DotsPerInch,
                [.. printer.Labels.Select(Enum.Parse<LabelKind>)]));

    private static bool IsSensible(PrinterRequest request, IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(request.Name) && request.Name.Trim().Length <= LongestName
        && AddressPattern().IsMatch(request.Address.Trim())
        && (PrinterConnection.IsPrinterPort(request.Port) || configuration.GetValue<bool>("Printing:AllowLocal") && request.Port is > 0 and <= 65535)
        && Resolutions.Contains(request.DotsPerInch)
        && Enum.IsDefined(request.Language)
        && request.Labels.All(Printable.Contains);

    private static void Apply(PrinterRequest request, Printer printer)
    {
        printer.Name = request.Name.Trim();
        printer.Address = request.Address.Trim();
        printer.Port = request.Port;
        printer.Language = request.Language;
        printer.DotsPerInch = request.DotsPerInch;
        printer.Labels = [.. request.Labels.Distinct().Select(kind => kind.ToString())];
    }

    private static async Task<IResult> AddAsync(PrinterRequest request, GrowLogDbContext db, IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (!IsSensible(request, configuration))
        {
            return TypedResults.BadRequest();
        }

        var printer = new Printer { Name = string.Empty, Address = string.Empty };
        Apply(request, printer);
        db.Printers.Add(printer);
        return await SaveAsync(db, TypedResults.Created("/api/printers", new PrinterAdded(printer.Id)), cancellationToken);
    }

    private static async Task<IResult> ChangeAsync(Guid id, PrinterRequest request, GrowLogDbContext db, IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (await db.Printers.FindAsync([id], cancellationToken) is not { } printer)
        {
            return TypedResults.NotFound();
        }

        if (!IsSensible(request, configuration))
        {
            return TypedResults.BadRequest();
        }

        Apply(request, printer);
        return await SaveAsync(db, TypedResults.NoContent(), cancellationToken);
    }

    private static async Task<IResult> RemoveAsync(Guid id, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Printers.FindAsync([id], cancellationToken) is not { } printer)
        {
            return TypedResults.NotFound();
        }

        db.Printers.Remove(printer);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> SaveAsync(GrowLogDbContext db, IResult success, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return success;
        }
        catch (DbUpdateException)
        {
            return TypedResults.Conflict();
        }
    }

    /// <summary>A made-up label of the first kind it prints, to check the size, darkness and position.</summary>
    private static async Task<IResult> TestAsync(
        Guid id,
        GrowLogDbContext db,
        LabelPrinter renderer,
        PrinterConnection connection,
        CancellationToken cancellationToken)
    {
        if (await db.Printers.AsNoTracking().FirstOrDefaultAsync(printer => printer.Id == id, cancellationToken) is not { } printer)
        {
            return TypedResults.NotFound();
        }

        var kind = printer.Labels.Length > 0 ? Enum.Parse<LabelKind>(printer.Labels[0]) : LabelKind.PlantLabel;
        var job = LabelEndpoints.Sample(kind, await LabelSettings.LoadAsync(db, kind, cancellationToken));
        return await SendAsync(printer, job, copies: 1, renderer, connection, cancellationToken);
    }

    private static async Task<IResult> PrintAsync(
        Guid id,
        PrintRequest request,
        GrowLogDbContext db,
        EventStore store,
        LabelPrinter renderer,
        PrinterConnection connection,
        CancellationToken cancellationToken)
    {
        if (await db.Printers.AsNoTracking().FirstOrDefaultAsync(printer => printer.Id == id, cancellationToken) is not { } printer)
        {
            return TypedResults.NotFound();
        }

        var job = request.Kind switch
        {
            LabelKind.ContainerLabel => await LabelEndpoints.ContainerLabelAsync(request.Target, db, store, cancellationToken),
            LabelKind.PlaceLabel when Guid.TryParse(request.Target, out var place) => await LabelEndpoints.PlaceLabelAsync(place, db, cancellationToken),
            LabelKind.PlantLabel when Guid.TryParse(request.Target, out var plant) => await LabelEndpoints.PlantLabelAsync(plant, request.PotSizeId, db, cancellationToken),
            LabelKind.BenchCard when Guid.TryParse(request.Target, out var card) => await LabelEndpoints.BenchCardAsync(card, db, cancellationToken),
            LabelKind.PlantPassport when Guid.TryParse(request.Target, out var passport) => await LabelEndpoints.PassportLabelAsync(passport, db, cancellationToken),
            _ => null,
        };

        return job is null
            ? TypedResults.NotFound()
            : await SendAsync(printer, job, Math.Clamp(request.Copies, 1, LabelPrinter.MostCopies), renderer, connection, cancellationToken);
    }

    private static async Task<IResult> SendAsync(Printer printer, LabelJob job, int copies, LabelPrinter renderer, PrinterConnection connection, CancellationToken cancellationToken)
    {
        var picture = await renderer.RenderAsync(job.Kind, job.Data, job.QrText, job.Context, copies: 1, cancellationToken, asPicture: true, dotsPerInch: printer.DotsPerInch);
        var commands = PrinterCommands.For(printer.Language, LabelBitmap.FromPng(picture), copies, printer.DotsPerInch);

        return await connection.SendAsync(printer.Address, printer.Port, commands, cancellationToken) switch
        {
            PrintOutcome.Sent => TypedResults.NoContent(),
            PrintOutcome.NotFound => Problem("not-found"),
            PrintOutcome.NotAllowed => Problem("not-allowed"),
            _ => Problem("unreachable"),
        };

        static IResult Problem(string problem) => TypedResults.Json(new PrintProblem(problem), statusCode: StatusCodes.Status502BadGateway);
    }

    internal sealed record PrinterDetails(Guid Id, string Name, string Address, int Port, PrinterLanguage Language, int DotsPerInch, IReadOnlyList<LabelKind> Labels);

    internal sealed record PrinterRequest(string Name, string Address, int Port, PrinterLanguage Language, int DotsPerInch, IReadOnlyList<LabelKind> Labels);

    internal sealed record PrinterAdded(Guid Id);

    /// <summary>A label to print: a tray's code, or a place's, plant's or bench card's id, and for a plant label the pot size priced.</summary>
    internal sealed record PrintRequest(LabelKind Kind, string Target, Guid? PotSizeId, int Copies = 1);

    /// <summary>Why it didn't print: not-found, not-allowed or unreachable.</summary>
    internal sealed record PrintProblem(string Problem);
}
