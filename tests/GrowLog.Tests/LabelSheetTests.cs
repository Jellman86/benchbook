using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GrowLog.Api.Labels;
using Xunit;
using static GrowLog.Tests.AccountHarness;

namespace GrowLog.Tests;

public sealed class LabelSheetTests(PostgresFixture database)
{
    private static readonly string[] NoSuchTray = ["C999999"];

    [Fact]
    public void Every_sheet_fits_on_a4_with_its_grid_in_the_middle()
    {
        foreach (var sheet in LabelSheets.All)
        {
            var cells = Assert.Single(LabelSheets.Lay(sheet, sheet.PerSheet, 1));
            Assert.Equal(sheet.PerSheet, cells.Count);
            Assert.All(cells, cell => Assert.True(cell.X >= 0 && cell.Y >= 0 && cell.X + sheet.WidthMm <= 210 && cell.Y + sheet.HeightMm <= 297, sheet.Id));
            Assert.Equal(cells[0].X, 210 - (cells[^1].X + sheet.WidthMm), 3);
        }
    }

    [Fact]
    public void Labels_start_at_the_chosen_sticker_and_run_on_to_the_next_sheet()
    {
        var sheet = LabelSheets.Find("21")!;

        var pages = LabelSheets.Lay(sheet, 3, startAt: 20);

        Assert.Equal(2, pages.Count);
        Assert.Equal([0, 1], pages[0].Select(cell => cell.Index));
        Assert.Equal((7.21m + 66.04m, 15.15m + (6 * 38.1m)), (pages[0][0].X, pages[0][0].Y));
        Assert.Equal((7.21m, 15.15m), (pages[1][0].X, pages[1][0].Y));
    }

    [Fact]
    public async Task Tray_labels_print_on_a_sheet_and_nonsense_is_refused()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = Json(await PostForTextAsync(nursery.Owner, "/api/plants", new { commonName = "Lavender", xylellaHost = false, protectedVariety = false })).GetProperty("id").GetGuid();
        var labels = new List<string>();

        for (var tray = 0; tray < 2; tray++)
        {
            var started = Json(await PostForTextAsync(nursery.Staff, "/api/batches", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow, plantId = plant, quantity = 20, source = "SeedLot", originCountry = "GB", containerKind = "Tray" }));
            labels.Add(started.GetProperty("labelCode").GetString()!);
        }

        var layouts = await nursery.Staff.GetFromJsonAsync<JsonElement>(Route("/api/labels/sheet-layouts"), Cancellation);
        Assert.Equal(5, layouts.GetArrayLength());

        using var sheet = await PostAsync(nursery.Staff, "/api/labels/sheets", new { kind = "ContainerLabel", targets = labels, sheet = "21", startAt = 5, copies = 2, outlines = true });
        Assert.Equal(HttpStatusCode.OK, sheet.StatusCode);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(await sheet.Content.ReadAsByteArrayAsync(Cancellation), 0, 4), StringComparison.Ordinal);

        using var picture = await PostAsync(nursery.Staff, "/api/labels/sheets", new { kind = "ContainerLabel", targets = labels, sheet = "21", picture = true });
        Assert.Equal("image/png", picture.Content.Headers.ContentType?.MediaType);
        await File.WriteAllBytesAsync(Path.Join(Path.GetTempPath(), "growlog-sheet-preview.png"), await picture.Content.ReadAsByteArrayAsync(Cancellation), Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(nursery.Staff, "/api/labels/sheets", new { kind = "ContainerLabel", targets = labels, sheet = "99" }));
        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(nursery.Staff, "/api/labels/sheets", new { kind = "Invoice", targets = labels, sheet = "21" }));
        Assert.Equal(HttpStatusCode.NotFound, await PostStatusAsync(nursery.Staff, "/api/labels/sheets", new { kind = "ContainerLabel", targets = NoSuchTray, sheet = "21" }));
    }
}
