using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;
using static GrowLog.Tests.AccountHarness;

namespace GrowLog.Tests;

// These run the real Typst program, so it must be installed.
public sealed class LabelTests(PostgresFixture database)
{
    private static async Task<Guid> AddAsync(HttpClient client, string path, object body) =>
        Json(await PostForTextAsync(client, path, body)).GetProperty("id").GetGuid();

    private static Task<Guid> AddPlantAsync(HttpClient client, string commonName, object[]? prices = null) =>
        AddAsync(client, "/api/plants", new
        {
            commonName,
            variety = "Hidcote",
            botanicalName = "Lavandula angustifolia",
            position = "Full sun",
            watering = "Low",
            xylellaHost = false,
            protectedVariety = false,
            prices = prices ?? [],
        });

    private static async Task<string> StartBatchAsync(HttpClient client, Guid plantId)
    {
        var started = Json(await PostForTextAsync(client, "/api/batches", new
        {
            eventId = Guid.NewGuid(),
            occurredAt = DateTimeOffset.UtcNow,
            plantId,
            quantity = 10,
            source = "SeedLot",
            originCountry = "GB",
            containerKind = "Tray",
        }));

        return started.GetProperty("labelCode").GetString()!;
    }

    private static async Task AssertIsPdfAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(Route(path), Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(await response.Content.ReadAsByteArrayAsync(Cancellation), 0, 4), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_container_label_is_a_pdf()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = await AddPlantAsync(nursery.Owner, "Lavender");
        var label = await StartBatchAsync(nursery.Staff, plant);

        await AssertIsPdfAsync(nursery.Staff, $"/api/labels/containers/{label}");
    }

    [Fact]
    public async Task A_plant_label_can_show_the_price_for_a_pot_size()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var potSize = await AddAsync(nursery.Owner, "/api/pot-sizes", new { name = "9cm" });
        var plant = await AddPlantAsync(nursery.Owner, "Lavender", [new { potSizeId = potSize, price = 4.5m }]);

        await AssertIsPdfAsync(nursery.Staff, $"/api/labels/plants/{plant}?potSizeId={potSize}");
        await AssertIsPdfAsync(nursery.Staff, $"/api/labels/plants/{plant}");
    }

    [Fact]
    public async Task A_bench_card_is_a_pdf()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var potSize = await AddAsync(nursery.Owner, "/api/pot-sizes", new { name = "9cm" });
        var plant = await AddPlantAsync(nursery.Owner, "Lavender", [new { potSizeId = potSize, price = 4.5m }]);

        await AssertIsPdfAsync(nursery.Staff, $"/api/labels/bench-cards/{plant}");
    }

    [Fact]
    public async Task A_place_label_is_a_pdf_and_a_typed_name_is_only_printed()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var site = await AddAsync(nursery.Owner, "/api/locations", new { name = "Farm", kind = "Site" });
        var bench = await AddAsync(nursery.Owner, "/api/locations", new { name = "#panic(\"boom\")", kind = "Bench", parentId = site });

        await AssertIsPdfAsync(nursery.Staff, $"/api/labels/places/{bench}");
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(nursery.Staff, $"/api/labels/places/{Guid.NewGuid()}"));
    }

    [Theory]
    [InlineData("#panic(\"boom\")")]
    [InlineData("]  #let x = [")]
    [InlineData("$ \\ * _ `code` <tag> @ref")]
    [InlineData("Very long plant name that goes on and on and on and on and on and on and on and on and on and on and on")]
    public async Task Whatever_is_typed_as_a_name_is_only_ever_printed(string name)
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = await AddPlantAsync(nursery.Owner, name);
        var label = await StartBatchAsync(nursery.Staff, plant);

        await AssertIsPdfAsync(nursery.Staff, $"/api/labels/containers/{label}");
        await AssertIsPdfAsync(nursery.Staff, $"/api/labels/plants/{plant}");
        await AssertIsPdfAsync(nursery.Staff, $"/api/labels/bench-cards/{plant}");
    }

    [Fact]
    public async Task An_unknown_label_or_plant_is_not_found()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);

        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(nursery.Staff, "/api/labels/containers/NOPE"));
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(nursery.Staff, $"/api/labels/plants/{Guid.NewGuid()}"));
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(nursery.Staff, $"/api/labels/bench-cards/{Guid.NewGuid()}"));
    }

    [Fact]
    public async Task Someone_not_signed_in_cannot_print()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        using var stranger = Client(nursery.App);

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(stranger, "/api/labels/containers/C000001"));
    }

    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static async Task<HttpStatusCode> PutBytesAsync(HttpClient client, string path, byte[] content)
    {
        using var body = new ByteArrayContent(content);
        using var response = await client.PutAsync(Route(path), body, Cancellation);
        return response.StatusCode;
    }

    private static int Pages(byte[] pdf) =>
        System.Text.RegularExpressions.Regex.Count(
            System.Text.Encoding.Latin1.GetString(pdf),
            "/Type\\s*/Page(?![s])",
            System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromSeconds(1));

    [Fact]
    public async Task The_owner_sets_the_nurserys_details_and_logo_and_staff_can_read_them()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var details = new { name = "Croft View Nursery", address = "Croft Lane", phone = (string?)null, email = (string?)null, website = (string?)null, registrationNumber = "GB-12345", vatNumber = (string?)null, hasLogo = false };

        using var refused = await nursery.Staff.PutAsJsonAsync(Route("/api/settings/nursery"), details, Cancellation);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        using var saved = await nursery.Owner.PutAsJsonAsync(Route("/api/settings/nursery"), details, Cancellation);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, await PutBytesAsync(nursery.Owner, "/api/settings/nursery/logo", "<svg/>"u8.ToArray()));
        Assert.Equal(HttpStatusCode.NoContent, await PutBytesAsync(nursery.Owner, "/api/settings/nursery/logo", Png));

        var read = await nursery.Staff.GetFromJsonAsync<JsonElement>(Route("/api/settings/nursery"), Cancellation);
        Assert.Equal("Croft View Nursery", read.GetProperty("name").GetString());
        Assert.Equal("GB-12345", read.GetProperty("registrationNumber").GetString());
        Assert.True(read.GetProperty("hasLogo").GetBoolean());
    }

    [Fact]
    public async Task A_chosen_style_with_the_logo_prints_and_copies_come_as_pages()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = await AddPlantAsync(nursery.Owner, "Lavender");
        Assert.Equal(HttpStatusCode.NoContent, await PutBytesAsync(nursery.Owner, "/api/settings/nursery/logo", Png));
        var style = new { layout = "Large", widthMm = 50, heightMm = 30, showLogo = true, showNurseryName = true, showBotanical = true, showPrice = true, showCare = true };

        using var saved = await nursery.Owner.PutAsJsonAsync(Route("/api/settings/labels/PlantLabel"), style, Cancellation);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var styles = await nursery.Staff.GetFromJsonAsync<JsonElement>(Route("/api/settings/labels"), Cancellation);
        Assert.Equal(50, styles.GetProperty("PlantLabel").GetProperty("widthMm").GetInt32());

        using var three = await nursery.Staff.GetAsync(Route($"/api/labels/plants/{plant}?copies=3"), Cancellation);
        Assert.Equal(HttpStatusCode.OK, three.StatusCode);
        Assert.Equal(3, Pages(await three.Content.ReadAsByteArrayAsync(Cancellation)));
        await AssertIsPdfAsync(nursery.Staff, $"/api/labels/bench-cards/{plant}?copies=2");
    }

    [Fact]
    public async Task The_owner_can_preview_a_style_before_saving_it_but_not_a_silly_size()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var style = new { layout = "Compact", widthMm = 40, heightMm = 20, showLogo = false, showNurseryName = false, showBotanical = true, showPrice = true, showCare = false };

        foreach (var kind in new[] { "PlantLabel", "BenchCard", "ContainerLabel", "PlaceLabel" })
        {
            using var preview = await nursery.Owner.PostAsJsonAsync(Route($"/api/labels/preview/{kind}"), style, Cancellation);
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            Assert.Equal("application/pdf", preview.Content.Headers.ContentType?.MediaType);
        }

        using var huge = await nursery.Owner.PutAsJsonAsync(Route("/api/settings/labels/PlantLabel"), new { layout = "Compact", widthMm = 5, heightMm = 20, showLogo = false, showNurseryName = false, showBotanical = true, showPrice = true, showCare = false }, Cancellation);
        Assert.Equal(HttpStatusCode.BadRequest, huge.StatusCode);
        using var staff = await nursery.Staff.PostAsJsonAsync(Route("/api/labels/preview/PlantLabel"), style, Cancellation);
        Assert.Equal(HttpStatusCode.Forbidden, staff.StatusCode);
    }
}
