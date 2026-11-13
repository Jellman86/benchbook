using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using QRCoder;

namespace GrowLog.Api.Labels;

/// <summary>
/// Makes PDFs with Typst. Text is handed over as a data file and never placed in the template,
/// so nothing a person types can be run as Typst code.
/// </summary>
internal sealed class LabelPrinter(IConfiguration configuration)
{
    private static readonly SemaphoreSlim Processes = new(Math.Max(1, Environment.ProcessorCount));

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public const int MostCopies = 100;

    /// <summary>The page set-up and fitting that every design imports.</summary>
    private const string Shared = "Shared";

    /// <summary>
    /// Renders one label, or several copies of it as pages of one PDF, in the style chosen for its kind.
    /// As a picture it is a single PNG at the printer's 300 dots an inch, for showing on a screen.
    /// </summary>
    public Task<byte[]> RenderAsync(LabelKind kind, object data, string? qrText, LabelContext context, int copies, CancellationToken cancellationToken, bool asPicture = false, int dotsPerInch = 300) =>
        RenderAsync(
            new LabelJob(kind, data, qrText, context),
            new Output(asPicture ? "png" : "pdf", context.Style.WidthMm, context.Style.HeightMm, asPicture ? dotsPerInch : null),
            asPicture ? 1 : copies,
            cancellationToken);

    /// <summary>
    /// Labels laid out on A4 sticker sheets for an ordinary printer: each is drawn by its own design at the size of
    /// one sticker, then placed on the sheet's grid, starting at any sticker so part-used sheets can be finished.
    /// </summary>
    /// <param name="asPicture">The first sheet as a picture, to check on screen before printing.</param>
    public async Task<byte[]> RenderSheetAsync(IReadOnlyList<LabelJob> jobs, LabelSheet sheet, int startAt, bool outlines, CancellationToken cancellationToken, bool asPicture = false)
    {
        var directory = Directory.CreateTempSubdirectory("growlog-sheet-");

        try
        {
            var drawn = new byte[jobs.Count][];
            var size = new Output("svg", sheet.WidthMm, sheet.HeightMm, null);

            var unique = Enumerable.Range(0, jobs.Count).GroupBy(index => JsonSerializer.Serialize(jobs[index]), StringComparer.Ordinal);
            // Identical labels share one render, including when they fill several sheets.
            await Parallel.ForEachAsync(
                unique,
                new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken },
                async (group, token) =>
                {
                    var label = await RenderAsync(jobs[group.First()], size, 1, token);
                    foreach (var index in group)
                    {
                        drawn[index] = label;
                    }
                });

            for (var index = 0; index < drawn.Length; index++)
            {
                await File.WriteAllBytesAsync(Path.Join(directory.FullName, $"label-{index}.svg"), drawn[index], cancellationToken);
            }

            var pages = LabelSheets.Lay(sheet, jobs.Count, startAt)
                .Select(page => page.Select(cell => new { x = cell.X, y = cell.Y, w = sheet.WidthMm, h = sheet.HeightMm, file = $"label-{cell.Index}.svg" }))
                .ToList();

            var template = Path.Join(directory.FullName, "sheet.typ");
            await File.WriteAllTextAsync(template, await ReadTemplateAsync("Sheet", cancellationToken), cancellationToken);
            await File.WriteAllTextAsync(Path.Join(directory.FullName, "sheet.json"), JsonSerializer.Serialize(new { pages, outlines }), cancellationToken);

            var output = Path.Join(directory.FullName, asPicture ? "sheet.png" : "sheet.pdf");
            var page = asPicture
                ? new Output("png", LabelSheets.PageWidthMm, LabelSheets.PageHeightMm, 72, FirstPageOnly: true)
                : new Output("pdf", LabelSheets.PageWidthMm, LabelSheets.PageHeightMm, null);
            await RunTypstAsync(page, directory.FullName, template, output, cancellationToken);
            return await File.ReadAllBytesAsync(output, cancellationToken);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private async Task<byte[]> RenderAsync(LabelJob job, Output output, int copies, CancellationToken cancellationToken)
    {
        var (kind, data, qrText, context) = job;
        var directory = Directory.CreateTempSubdirectory("growlog-label-");

        try
        {
            var template = Path.Join(directory.FullName, "label.typ");
            var file = Path.Join(directory.FullName, $"label.{output.Format}");
            var asPicture = !output.IsPdf;
            var logo = await WriteLogoAsync(directory.FullName, context, cancellationToken);
            var style = context.Style;
            (string Svg, int Modules)? qr = qrText is null ? null : Qr(qrText);

            var everything = new
            {
                item = data,
                copies = asPicture ? 1 : Math.Clamp(copies, 1, MostCopies),
                nursery = context.NurseryName ?? string.Empty,
                logo,
                qrModules = qr?.Modules ?? 0,
                style = new
                {
                    layout = style.Layout.ToString(),
                    showLogo = style.ShowLogo && logo.Length > 0,
                    showNurseryName = style.ShowNurseryName && !string.IsNullOrWhiteSpace(context.NurseryName),
                    showBotanical = style.ShowBotanical,
                    showPrice = style.ShowPrice,
                    showCare = style.ShowCare,
                },
            };

            await File.WriteAllTextAsync(template, await ReadTemplateAsync(kind.ToString(), cancellationToken), cancellationToken);
            await File.WriteAllTextAsync(Path.Join(directory.FullName, $"{Shared}.typ"), await ReadTemplateAsync(Shared, cancellationToken), cancellationToken);
            await File.WriteAllTextAsync(Path.Join(directory.FullName, "data.json"), JsonSerializer.Serialize(everything), cancellationToken);

            if (qr is not null)
            {
                await File.WriteAllTextAsync(Path.Join(directory.FullName, "qr.svg"), qr.Value.Svg, cancellationToken);
            }

            await RunTypstAsync(output, directory.FullName, template, file, cancellationToken);
            return await File.ReadAllBytesAsync(file, cancellationToken);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>
    /// The QR code as a picture, and how many squares wide it is with its white border. The design
    /// uses the count to make every square a whole number of printer dots, which keeps edges crisp.
    /// </summary>
    internal static (string Svg, int Modules) Qr(string text)
    {
        using var generator = new QRCodeGenerator();
        using var code = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        using var svg = new SvgQRCode(code);
        return (svg.GetGraphic(8), code.ModuleMatrix.Count);
    }

    private static async Task<string> ReadTemplateAsync(string name, CancellationToken cancellationToken)
    {
        await using var stream = typeof(LabelPrinter).Assembly.GetManifestResourceStream($"{name}.typ")
            ?? throw new InvalidOperationException($"The {name} template is missing.");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    /// <summary>Puts the logo beside the template under a fixed name, so its file name can't come from anyone.</summary>
    private static async Task<string> WriteLogoAsync(string directory, LabelContext context, CancellationToken cancellationToken)
    {
        var name = context.LogoContentType switch
        {
            "image/png" => "logo.png",
            "image/jpeg" => "logo.jpg",
            _ => null,
        };

        if (name is null || context.Logo is not { Length: > 0 } logo)
        {
            return string.Empty;
        }

        await File.WriteAllBytesAsync(Path.Join(directory, name), logo, cancellationToken);
        return name;
    }

    private async Task RunTypstAsync(Output size, string root, string template, string output, CancellationToken cancellationToken)
    {
        await Processes.WaitAsync(cancellationToken);
        try
        {
            await CompileAsync(size, root, template, output, cancellationToken);
        }
        finally
        {
            Processes.Release();
        }
    }

    private async Task CompileAsync(Output size, string root, string template, string output, CancellationToken cancellationToken)
    {
        var width = size.WidthMm.ToString(CultureInfo.InvariantCulture);
        var height = size.HeightMm.ToString(CultureInfo.InvariantCulture);

        var start = new ProcessStartInfo(configuration["Typst:Path"] ?? "typst")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        string[] arguments =
        [
            "compile",
            "--root", root,
            "--ignore-system-fonts",
            "--input", $"width={width}",
            "--input", $"height={height}",
            .. size.IsPdf ? Array.Empty<string>() : ["--format", size.Format],
            .. size.PixelsPerInch is { } ppi ? ["--ppi", ppi.ToString(CultureInfo.InvariantCulture)] : Array.Empty<string>(),
            .. size.FirstPageOnly ? ["--pages", "1"] : Array.Empty<string>(),
            template,
            output,
        ];

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Typst did not start.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        var errors = process.StandardError.ReadToEndAsync(timeout.Token);
        var messages = process.StandardOutput.ReadToEndAsync(timeout.Token);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(errors, messages);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        if (process.ExitCode != 0)
        {
            var detail = await errors;
            if (detail.Contains("label stock is too small for this passport", StringComparison.Ordinal))
            {
                throw new LabelStockTooSmallException();
            }
            throw new InvalidOperationException($"Typst could not make the label: {detail}");
        }
    }

    /// <summary>What Typst makes: a PDF, a PNG at so many pixels an inch, or an SVG; and at what size.</summary>
    private sealed record Output(string Format, decimal WidthMm, decimal HeightMm, int? PixelsPerInch, bool FirstPageOnly = false)
    {
        public bool IsPdf => string.Equals(Format, "pdf", StringComparison.Ordinal);
    }
}
