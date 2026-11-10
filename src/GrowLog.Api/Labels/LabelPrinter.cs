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
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public const int MostCopies = 100;

    /// <summary>Renders one label, or several copies of it as pages of one PDF, in the style chosen for its kind.</summary>
    public async Task<byte[]> RenderAsync(LabelKind kind, object data, string? qrText, LabelContext context, int copies, CancellationToken cancellationToken)
    {
        var directory = Directory.CreateTempSubdirectory("growlog-label-");

        try
        {
            var template = Path.Join(directory.FullName, "label.typ");
            var output = Path.Join(directory.FullName, "label.pdf");
            var logo = await WriteLogoAsync(directory.FullName, context, cancellationToken);
            var style = context.Style;

            var everything = new
            {
                item = data,
                copies = Math.Clamp(copies, 1, MostCopies),
                nursery = context.NurseryName ?? string.Empty,
                logo,
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

            await File.WriteAllTextAsync(template, await ReadTemplateAsync(kind, cancellationToken), cancellationToken);
            await File.WriteAllTextAsync(Path.Join(directory.FullName, "data.json"), JsonSerializer.Serialize(everything), cancellationToken);

            if (qrText is not null)
            {
                await File.WriteAllTextAsync(Path.Join(directory.FullName, "qr.svg"), QrSvg(qrText), cancellationToken);
            }

            await RunTypstAsync(style, directory.FullName, template, output, cancellationToken);
            return await File.ReadAllBytesAsync(output, cancellationToken);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    internal static string QrSvg(string text)
    {
        using var generator = new QRCodeGenerator();
        using var code = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        using var svg = new SvgQRCode(code);
        return svg.GetGraphic(8);
    }

    private static async Task<string> ReadTemplateAsync(LabelKind kind, CancellationToken cancellationToken)
    {
        await using var stream = typeof(LabelPrinter).Assembly.GetManifestResourceStream($"{kind}.typ")
            ?? throw new InvalidOperationException($"The {kind} template is missing.");
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

    private async Task RunTypstAsync(LabelStyle style, string root, string template, string output, CancellationToken cancellationToken)
    {
        var width = style.WidthMm.ToString(CultureInfo.InvariantCulture);
        var height = style.HeightMm.ToString(CultureInfo.InvariantCulture);

        var start = new ProcessStartInfo(configuration["Typst:Path"] ?? "typst")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        foreach (var argument in new[]
        {
            "compile",
            "--root", root,
            "--ignore-system-fonts",
            "--input", $"width={width}",
            "--input", $"height={height}",
            template,
            output,
        })
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
            throw new InvalidOperationException($"Typst could not make the label: {await errors}");
        }
    }
}
