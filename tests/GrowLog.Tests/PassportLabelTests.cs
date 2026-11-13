using GrowLog.Api.Labels;
using Microsoft.Extensions.Configuration;
using UglyToad.PdfPig;
using Xunit;

namespace GrowLog.Tests;

public sealed class PassportLabelTests
{
    private static LabelPrinter Printer() => new(new ConfigurationBuilder().AddEnvironmentVariables().Build());

    private static string Text(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join(" ", document.GetPages().SelectMany(page => page.GetWords()).Select(word => word.Text));
    }

    [Fact]
    public async Task A_long_botanical_name_keeps_every_required_passport_part()
    {
        var style = LabelStyle.DefaultFor(LabelKind.PlantPassport) with { WidthMm = 100, HeightMm = 60 };
        var name = "Pelargonium domesticum grandiflorum interspecificum cultivar exceptionally long botanical name";
        var pdf = await Printer().RenderAsync(LabelKind.PlantPassport,
            new { partA = name, partB = "123456", partC = "B000001", partD = "GB" }, null,
            new LabelContext(style, null, null, null), 1, TestContext.Current.CancellationToken);
        var text = Text(pdf);
        Assert.Contains("UK Plant Passport", text, StringComparison.Ordinal);
        foreach (var part in new[] { "A", "B", "C", "D", "E", "123456", "B000001", "GB" })
        {
            Assert.Contains(part, text.Split(' '), StringComparer.Ordinal);
        }
        foreach (var word in name.Split(' '))
        {
            Assert.Contains(word, text.Split(' '), StringComparer.Ordinal);
        }
    }

    [Fact]
    public async Task Stock_too_small_for_a_passport_gives_a_clear_refusal()
    {
        var style = LabelStyle.DefaultFor(LabelKind.PlantPassport) with { WidthMm = 20, HeightMm = 20 };
        var error = await Assert.ThrowsAsync<LabelStockTooSmallException>(() => Printer().RenderAsync(LabelKind.PlantPassport,
            new { partA = new string('a', 200), partB = "123456", partC = "B000001", partD = "GB" }, null,
            new LabelContext(style, null, null, null), 1, TestContext.Current.CancellationToken));
        Assert.Contains("label stock is too small for this passport", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_package_passport_prints_a_blank_part_e()
    {
        var pdf = await Printer().RenderAsync(LabelKind.PackagePassport,
            new { partB = "123456", package = "Package 1", lines = new[] { new { partA = "Lavandula angustifolia", partC = "B000001", partD = "GB" } } }, null,
            new LabelContext(LabelStyle.DefaultFor(LabelKind.PackagePassport), null, null, null), 1, TestContext.Current.CancellationToken);
        foreach (var part in new[] { "A", "B", "C", "D", "E", "123456", "B000001", "GB" })
        {
            Assert.Contains(part, Text(pdf).Split(' '), StringComparer.Ordinal);
        }
    }
}
