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

    public async Task<byte[]> RenderAsync(LabelKind kind, object data, string? qrText, CancellationToken cancellationToken)
    {
        var directory = Directory.CreateTempSubdirectory("growlog-label-");

        try
        {
            var template = Path.Join(directory.FullName, "label.typ");
            var output = Path.Join(directory.FullName, "label.pdf");

            await File.WriteAllTextAsync(template, await ReadTemplateAsync(kind, cancellationToken), cancellationToken);
            await File.WriteAllTextAsync(Path.Join(directory.FullName, "data.json"), JsonSerializer.Serialize(data), cancellationToken);

            if (qrText is not null)
            {
                await File.WriteAllTextAsync(Path.Join(directory.FullName, "qr.svg"), QrSvg(qrText), cancellationToken);
            }

            await RunTypstAsync(kind, directory.FullName, template, output, cancellationToken);
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

    private string Size(LabelKind kind, string dimension, int fallback) =>
        (configuration.GetValue<int?>($"Labels:{kind}:{dimension}Mm") ?? fallback).ToString(CultureInfo.InvariantCulture);

    private async Task RunTypstAsync(LabelKind kind, string root, string template, string output, CancellationToken cancellationToken)
    {
        var (width, height) = kind switch
        {
            LabelKind.ContainerLabel or LabelKind.PlaceLabel => (70, 40),
            LabelKind.PlantLabel => (70, 25),
            _ => (105, 148),
        };

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
            "--input", $"width={Size(kind, "Width", width)}",
            "--input", $"height={Size(kind, "Height", height)}",
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
            throw new InvalidOperationException($"Typst could not make the {kind}: {await errors}");
        }
    }
}
