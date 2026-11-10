using System.Net;
using System.Net.Http.Json;
using System.Text;
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
}
