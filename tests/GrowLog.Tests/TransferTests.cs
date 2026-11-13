using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GrowLog.Api.Transfer;
using Xunit;
using static GrowLog.Tests.AccountHarness;

namespace GrowLog.Tests;

public sealed class TransferTests(PostgresFixture database)
{
    [Fact]
    public void Spreadsheet_cells_are_quoted_and_formulas_kept_as_text()
    {
        Assert.Equal("\"Hilltop, Bakewell\"", Csv.Cell("Hilltop, Bakewell"));
        Assert.Equal("\"Say \"\"hello\"\"\"", Csv.Cell("Say \"hello\""));
        Assert.Equal("'=HYPERLINK(1)", Csv.Cell("=HYPERLINK(1)"));
        Assert.Equal("-3", Csv.Cell(-3));
        Assert.Equal("+44 1629 123456", Csv.Cell("+44 1629 123456"));
        Assert.Equal("'+SUM(A1)", Csv.Cell("+SUM(A1)"));
        Assert.Equal("Yes", Csv.Cell(true));
        Assert.Equal("2026-11-12", Csv.Cell(new DateOnly(2026, 11, 12)));
    }

    private static object Row(int row, object values) => new { row, values };

    [Fact]
    public async Task Every_problem_row_is_named_and_nothing_is_saved_until_there_are_none()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        await PostForTextAsync(nursery.Owner, "/api/plants", new { commonName = "Lavender", variety = "Munstead", xylellaHost = true, protectedVariety = false });

        object[] rows =
        [
            Row(2, new Dictionary<string, string?>(StringComparer.Ordinal) { ["commonName"] = "Sage", ["botanicalName"] = "Salvia officinalis", ["xylellaHost"] = "yes" }),
            Row(3, new Dictionary<string, string?>(StringComparer.Ordinal) { ["commonName"] = "", ["variety"] = "Alba" }),
            Row(4, new Dictionary<string, string?>(StringComparer.Ordinal) { ["commonName"] = "Thyme", ["protectedVariety"] = "maybe" }),
            Row(5, new Dictionary<string, string?>(StringComparer.Ordinal) { ["commonName"] = "sage" }),
            Row(6, new Dictionary<string, string?>(StringComparer.Ordinal) { ["commonName"] = "lavender", ["variety"] = "munstead" }),
        ];

        var checkedRows = Json(await PostForTextAsync(nursery.Owner, "/api/import/Plants", new { rows, save = true }));
        Assert.False(checkedRows.GetProperty("saved").GetBoolean());
        Assert.Equal([3, 4, 5], checkedRows.GetProperty("problems").EnumerateArray().Select(problem => problem.GetProperty("row").GetInt32()));
        Assert.Contains("Common name is empty", checkedRows.GetProperty("problems")[0].GetProperty("problem").GetString(), StringComparison.Ordinal);
        Assert.Contains("yes or no", checkedRows.GetProperty("problems")[1].GetProperty("problem").GetString(), StringComparison.Ordinal);
        Assert.Equal([6], checkedRows.GetProperty("alreadyThere").EnumerateArray().Select(row => row.GetInt32()));
        Assert.Single((await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/plants"), Cancellation)).EnumerateArray());

        object[] fixedRows = [rows[0], Row(4, new Dictionary<string, string?>(StringComparer.Ordinal) { ["commonName"] = "Thyme", ["protectedVariety"] = "no" }), rows[4]];
        var saved = Json(await PostForTextAsync(nursery.Owner, "/api/import/Plants", new { rows = fixedRows, save = true }));
        Assert.True(saved.GetProperty("saved").GetBoolean());
        Assert.Equal(2, saved.GetProperty("adding").GetInt32());

        var plants = (await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/plants"), Cancellation)).EnumerateArray().ToList();
        Assert.Equal(3, plants.Count);
        Assert.True(plants.Single(plant => string.Equals(plant.GetProperty("commonName").GetString(), "Sage", StringComparison.Ordinal)).GetProperty("xylellaHost").GetBoolean());

        var again = Json(await PostForTextAsync(nursery.Owner, "/api/import/Plants", new { rows = fixedRows, save = true }));
        Assert.Equal(0, again.GetProperty("adding").GetInt32());
        Assert.Equal(3, again.GetProperty("alreadyThere").GetArrayLength());

        Assert.Equal(HttpStatusCode.Forbidden, await PostStatusAsync(nursery.Staff, "/api/import/Customers", new { rows = fixedRows, save = false }));
    }

    [Fact]
    public async Task Everything_comes_out_in_one_download()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = Json(await PostForTextAsync(nursery.Owner, "/api/plants", new { commonName = "=Lavender, the best", xylellaHost = false, protectedVariety = false })).GetProperty("id").GetGuid();
        await PostForTextAsync(nursery.Owner, "/api/customers", new { name = "Hilltop Garden Centre" });
        var tray = Json(await PostForTextAsync(nursery.Staff, "/api/batches", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow.AddDays(-3), plantId = plant, quantity = 40, source = "SeedLot", originCountry = "GB", containerKind = "Tray" })).GetProperty("containerId").GetGuid();
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(nursery.Staff, $"/api/containers/{tray}/germinated", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow }));

        using var response = await nursery.Owner.GetAsync(Route("/api/export"), Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("growlog-records-", response.Content.Headers.ContentDisposition!.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName, StringComparison.Ordinal);

        await using var zipped = await response.Content.ReadAsStreamAsync(Cancellation);
        using var archive = new ZipArchive(zipped);
        Assert.Contains(archive.Entries, entry => string.Equals(entry.Name, "work.csv", StringComparison.Ordinal));
        Assert.Contains(archive.Entries, entry => string.Equals(entry.Name, "count-adjustments.csv", StringComparison.Ordinal));
        Assert.Contains(archive.Entries, entry => string.Equals(entry.Name, "legacy-counts.csv", StringComparison.Ordinal));

        using var plants = new StreamReader(archive.GetEntry("plants.csv")!.Open(), Encoding.UTF8);
        var text = await plants.ReadToEndAsync(Cancellation);
        Assert.StartsWith("Common name,", text, StringComparison.Ordinal);
        Assert.Contains("\"'=Lavender, the best\"", text, StringComparison.Ordinal);

        using var work = new StreamReader(archive.GetEntry("work.csv")!.Open(), Encoding.UTF8);
        var lines = (await work.ReadToEndAsync(Cancellation)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Contains("Line effective time", lines[0], StringComparison.Ordinal);
        Assert.Contains(",Sown,", lines[1], StringComparison.Ordinal);
        Assert.Contains(",40,", lines[1], StringComparison.Ordinal);

        using var trays = new StreamReader(archive.GetEntry("trays-now.csv")!.Open(), Encoding.UTF8);
        Assert.Contains(",40,No,Germinated,", await trays.ReadToEndAsync(Cancellation), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync(nursery.Staff, "/api/export"));
    }
}
