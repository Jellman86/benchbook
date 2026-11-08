using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using static GrowLog.Tests.AccountHarness;

namespace GrowLog.Tests;

public sealed class FigureTests(PostgresFixture database)
{
    private sealed record Started(Guid BatchId, Guid ContainerId, string LabelCode);

    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

    private static async Task<Guid> AddPlantAsync(HttpClient owner, string commonName = "Lavender") =>
        Json(await PostForTextAsync(owner, "/api/plants", new { commonName, variety = "Hidcote", xylellaHost = false, protectedVariety = false }))
            .GetProperty("id").GetGuid();

    private static async Task<Started> StartAsync(HttpClient client, Guid plantId, int quantity, string source = "SeedLot")
    {
        var body = Json(await PostForTextAsync(client, "/api/batches", new
        {
            eventId = Guid.NewGuid(),
            occurredAt = Now,
            plantId,
            quantity,
            source,
            originCountry = "GB",
            containerKind = "Tray",
        }));

        return new Started(body.GetProperty("batchId").GetGuid(), body.GetProperty("containerId").GetGuid(), body.GetProperty("labelCode").GetString()!);
    }

    private static async Task<Guid> LoseAsync(HttpClient client, Started batch, int quantity, string reason)
    {
        var eventId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(client, $"/api/containers/{batch.ContainerId}/losses", new { eventId, occurredAt = Now, batchId = batch.BatchId, quantity, reason }));
        return eventId;
    }

    private static Task<HttpStatusCode> GerminatedAsync(HttpClient client, Started batch) =>
        PostStatusAsync(client, $"/api/containers/{batch.ContainerId}/germinated", new { eventId = Guid.NewGuid(), occurredAt = Now });

    private static Task<HttpStatusCode> CorrectAsync(HttpClient client, Guid original, int? quantity = null) =>
        PostStatusAsync(client, $"/api/events/{original}/correct", new { eventId = Guid.NewGuid(), occurredAt = Now, quantity, locationId = (Guid?)null });

    private static Task<JsonElement> FiguresAsync(HttpClient owner) =>
        owner.GetFromJsonAsync<JsonElement>(Route("/api/figures"), Cancellation);

    private static async Task<JsonElement> PlantFiguresAsync(HttpClient owner) =>
        (await FiguresAsync(owner)).GetProperty("plants").EnumerateArray().Single();

    private static int Number(JsonElement figures, string name) => figures.GetProperty(name).GetInt32();

    private static double Rate(JsonElement figures, string name) => figures.GetProperty(name).GetDouble();

    [Fact]
    public async Task Survival_counts_what_was_started_and_what_was_lost()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = await AddPlantAsync(nursery.Owner);
        var batch = await StartAsync(nursery.Staff, plant, 40);
        await LoseAsync(nursery.Staff, batch, 10, "Pest");

        var figures = await PlantFiguresAsync(nursery.Owner);

        Assert.Equal(40, Number(figures, "started"));
        Assert.Equal(10, Number(figures, "lost"));
        Assert.Equal(30, Number(figures, "alive"));
        Assert.Equal(0.75, Rate(figures, "survivalRate"), 3);
    }

    [Fact]
    public async Task Germination_is_only_reported_once_a_batch_has_been_marked_germinated()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = await AddPlantAsync(nursery.Owner);
        var batch = await StartAsync(nursery.Staff, plant, 40);
        await LoseAsync(nursery.Staff, batch, 4, "FailedToGerminate");

        Assert.Equal(JsonValueKind.Null, (await PlantFiguresAsync(nursery.Owner)).GetProperty("germinationRate").ValueKind);

        await GerminatedAsync(nursery.Staff, batch);

        Assert.Equal(0.9, Rate(await PlantFiguresAsync(nursery.Owner), "germinationRate"), 3);
    }

    [Fact]
    public async Task Only_failing_to_germinate_lowers_germination()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = await AddPlantAsync(nursery.Owner);
        var batch = await StartAsync(nursery.Staff, plant, 40);
        await GerminatedAsync(nursery.Staff, batch);
        await LoseAsync(nursery.Staff, batch, 8, "Frost");

        Assert.Equal(1.0, Rate(await PlantFiguresAsync(nursery.Owner), "germinationRate"), 3);
    }

    [Theory]
    [InlineData("BoughtIn")]
    [InlineData("Cuttings")]
    public async Task Plants_not_grown_from_seed_have_no_germination_figure(string source)
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = await AddPlantAsync(nursery.Owner);
        var batch = await StartAsync(nursery.Staff, plant, 40, source);
        await GerminatedAsync(nursery.Staff, batch);

        Assert.Equal(JsonValueKind.Null, (await PlantFiguresAsync(nursery.Owner)).GetProperty("germinationRate").ValueKind);
    }

    [Fact]
    public async Task An_undone_loss_no_longer_counts()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = await AddPlantAsync(nursery.Owner);
        var batch = await StartAsync(nursery.Staff, plant, 40);
        var loss = await LoseAsync(nursery.Staff, batch, 10, "Pest");

        Assert.Equal(HttpStatusCode.Created, await CorrectAsync(nursery.Staff, loss));

        var figures = await FiguresAsync(nursery.Owner);
        Assert.Equal(0, Number(figures.GetProperty("plants")[0], "lost"));
        Assert.Equal(0, figures.GetProperty("losses").GetArrayLength());
    }

    [Fact]
    public async Task A_corrected_loss_counts_as_the_replacement_with_its_reason()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = await AddPlantAsync(nursery.Owner);
        var batch = await StartAsync(nursery.Staff, plant, 40);
        var loss = await LoseAsync(nursery.Staff, batch, 10, "Pest");

        Assert.Equal(HttpStatusCode.Created, await CorrectAsync(nursery.Staff, loss, 3));

        var figures = await FiguresAsync(nursery.Owner);
        Assert.Equal(3, Number(figures.GetProperty("plants")[0], "lost"));
        var reason = figures.GetProperty("losses").EnumerateArray().Single();
        Assert.Equal("Pest", reason.GetProperty("reason").GetString());
        Assert.Equal(3, Number(reason, "quantity"));
    }

    [Fact]
    public async Task A_corrected_starting_quantity_counts_as_the_replacement()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = await AddPlantAsync(nursery.Owner);
        await StartAsync(nursery.Staff, plant, 40);
        var recent = await nursery.Staff.GetFromJsonAsync<JsonElement>(Route("/api/events/recent"), Cancellation);

        Assert.Equal(HttpStatusCode.Created, await CorrectAsync(nursery.Staff, recent[0].GetProperty("id").GetGuid(), 25));

        Assert.Equal(25, Number(await PlantFiguresAsync(nursery.Owner), "started"));
    }

    [Fact]
    public async Task Stock_counts_are_reported_apart_from_losses()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = await AddPlantAsync(nursery.Owner);
        var batch = await StartAsync(nursery.Staff, plant, 40);
        await LoseAsync(nursery.Staff, batch, 10, "Pest");
        await PostStatusAsync(nursery.Staff, $"/api/containers/{batch.ContainerId}/count", new { eventId = Guid.NewGuid(), occurredAt = Now, batchId = batch.BatchId, counted = 28 });

        var figures = await PlantFiguresAsync(nursery.Owner);

        Assert.Equal(10, Number(figures, "lost"));
        Assert.Equal(-2, Number(figures, "adjusted"));
        Assert.Equal(28, Number(figures, "alive"));
        Assert.Equal(0.75, Rate(figures, "survivalRate"), 3);
    }

    [Fact]
    public async Task Potting_on_does_not_change_the_figures()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = await AddPlantAsync(nursery.Owner);
        var batch = await StartAsync(nursery.Staff, plant, 40);
        await PostStatusAsync(nursery.Staff, $"/api/containers/{batch.ContainerId}/transfer", new { eventId = Guid.NewGuid(), occurredAt = Now, type = "PottedOn", batchId = batch.BatchId, quantity = 15, containerKind = "PotGroup" });

        var figures = await PlantFiguresAsync(nursery.Owner);

        Assert.Equal(40, Number(figures, "started"));
        Assert.Equal(0, Number(figures, "lost"));
        Assert.Equal(40, Number(figures, "alive"));
    }

    [Fact]
    public async Task Each_plant_has_its_own_figures_and_losses_are_totalled_by_reason()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var aster = await AddPlantAsync(nursery.Owner, "Aster");
        var salvia = await AddPlantAsync(nursery.Owner, "Salvia");
        var asters = await StartAsync(nursery.Staff, aster, 10);
        var salvias = await StartAsync(nursery.Staff, salvia, 20);
        await LoseAsync(nursery.Staff, asters, 1, "Pest");
        await LoseAsync(nursery.Staff, salvias, 2, "Pest");
        await LoseAsync(nursery.Staff, salvias, 5, "Frost");

        var figures = await FiguresAsync(nursery.Owner);

        Assert.Equal(2, figures.GetProperty("plants").GetArrayLength());
        Assert.Equal("Aster", figures.GetProperty("plants")[0].GetProperty("commonName").GetString());
        Assert.Equal("Frost", figures.GetProperty("losses")[0].GetProperty("reason").GetString());
        Assert.Equal(5, Number(figures.GetProperty("losses")[0], "quantity"));
        Assert.Equal(3, Number(figures.GetProperty("losses")[1], "quantity"));
    }

    [Fact]
    public async Task Figures_are_for_the_owner()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        using var stranger = Client(nursery.App);

        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync(nursery.Staff, "/api/figures"));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(stranger, "/api/figures"));
    }
}
