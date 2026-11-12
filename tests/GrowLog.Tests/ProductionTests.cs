using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using static GrowLog.Tests.AccountHarness;

namespace GrowLog.Tests;

public sealed class ProductionTests(PostgresFixture database)
{
    private static async Task<Guid> SupplierAsync(HttpClient client) =>
        Json(await PostForTextAsync(client, "/api/suppliers", new { name = $"Supplier {Guid.NewGuid():N}" })).GetProperty("id").GetGuid();

    private static async Task<JsonElement> StartAsync(HttpClient client, Guid plantId, string source = "SeedLot", int quantity = 40)
    {
        var body = new
        {
            eventId = Guid.CreateVersion7(),
            occurredAt = DateTimeOffset.UtcNow,
            plantId,
            quantity,
            source,
            supplierId = string.Equals(source, "BoughtIn", StringComparison.Ordinal) ? await SupplierAsync(client) : (Guid?)null,
            originCountry = "GB",
            containerKind = "Tray",
        };

        return Json(await PostForTextAsync(client, "/api/batches", body));
    }

    private static async Task<Guid> AddPlantAsync(HttpClient client) =>
        Json(await PostForTextAsync(client, "/api/plants", new { commonName = "Lavender", xylellaHost = false, protectedVariety = false })).GetProperty("id").GetGuid();

    private static async Task<Guid> RecordAsync(HttpClient client, Guid container, string action)
    {
        var eventId = Guid.CreateVersion7();
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(client, $"/api/containers/{container}/{action}", new { eventId, occurredAt = DateTimeOffset.UtcNow }));
        return eventId;
    }

    private static Guid Container(JsonElement started) => started.GetProperty("containerId").GetGuid();

    private static async Task<List<JsonElement>> ProductionAsync(HttpClient client) =>
        [.. (await client.GetFromJsonAsync<JsonElement>(Route("/api/production"), Cancellation)).EnumerateArray()];

    private static string? Stage(JsonElement row) => row.GetProperty("stage").GetString();

    private static async Task<JsonElement> TransferAsync(HttpClient client, JsonElement started, int quantity, Guid? eventId = null)
    {
        var body = new
        {
            eventId = eventId ?? Guid.CreateVersion7(),
            occurredAt = DateTimeOffset.UtcNow,
            type = "PottedOn",
            batchId = started.GetProperty("batchId").GetGuid(),
            quantity,
            containerKind = "PotGroup",
        };

        return Json(await PostForTextAsync(client, $"/api/containers/{Container(started)}/transfer", body));
    }

    [Fact]
    public async Task A_tray_moves_through_its_stages_as_work_is_recorded()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);
        var started = await StartAsync(owner, await AddPlantAsync(owner));

        var row = Assert.Single(await ProductionAsync(owner));
        Assert.Equal("Sown", Stage(row));
        Assert.Equal(40, row.GetProperty("quantity").GetInt32());
        Assert.Equal("Lavender", row.GetProperty("commonName").GetString());

        await RecordAsync(owner, Container(started), "germinated");
        Assert.Equal("Germinated", Stage(Assert.Single(await ProductionAsync(owner))));

        await RecordAsync(owner, Container(started), "ready");
        Assert.Equal("Ready", Stage(Assert.Single(await ProductionAsync(owner))));
    }

    [Theory]
    [InlineData("BoughtIn", "BoughtIn")]
    [InlineData("Cuttings", "Cuttings")]
    public async Task Stock_that_was_not_sown_starts_at_its_own_stage(string source, string stage)
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);
        await StartAsync(owner, await AddPlantAsync(owner), source);

        Assert.Equal(stage, Stage(Assert.Single(await ProductionAsync(owner))));
    }

    [Fact]
    public async Task Undoing_ready_puts_the_tray_back_where_it_was()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);
        var started = await StartAsync(owner, await AddPlantAsync(owner));
        await RecordAsync(owner, Container(started), "germinated");
        var ready = await RecordAsync(owner, Container(started), "ready");

        var undo = new { eventId = Guid.CreateVersion7(), occurredAt = DateTimeOffset.UtcNow };
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(owner, $"/api/events/{ready}/correct", undo));

        Assert.Equal("Germinated", Stage(Assert.Single(await ProductionAsync(owner))));
    }

    [Fact]
    public async Task Potting_on_part_of_a_tray_leaves_the_rest_at_its_stage()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);
        var started = await StartAsync(owner, await AddPlantAsync(owner));

        await TransferAsync(owner, started, 15);

        var rows = await ProductionAsync(owner);
        Assert.Equal(2, rows.Count);
        Assert.Equal(25, rows.Single(row => string.Equals(Stage(row), "Sown", StringComparison.Ordinal)).GetProperty("quantity").GetInt32());
        Assert.Equal(15, rows.Single(row => string.Equals(Stage(row), "PottedOn", StringComparison.Ordinal)).GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task An_emptied_tray_is_no_longer_listed()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);
        var started = await StartAsync(owner, await AddPlantAsync(owner));

        await TransferAsync(owner, started, 40);

        Assert.Equal("PottedOn", Stage(Assert.Single(await ProductionAsync(owner))));
    }

    [Fact]
    public async Task Correcting_how_many_were_potted_on_keeps_the_stage()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);
        var started = await StartAsync(owner, await AddPlantAsync(owner));
        var transfer = Guid.CreateVersion7();
        await TransferAsync(owner, started, 15, transfer);

        var correction = new { eventId = Guid.CreateVersion7(), occurredAt = DateTimeOffset.UtcNow, quantity = 10 };
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(owner, $"/api/events/{transfer}/correct", correction));

        var potted = (await ProductionAsync(owner)).Single(row => string.Equals(Stage(row), "PottedOn", StringComparison.Ordinal));
        Assert.Equal(10, potted.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task Staff_can_see_it_but_nobody_signed_out_can()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);
        await StartAsync(owner, await AddPlantAsync(owner));
        await AllowNameOnlyAsync(owner);
        var staffId = Json(await PostForTextAsync(owner, "/api/staff", new { name = "Jo Reed", nameOnly = true })).GetProperty("id").GetGuid();

        using var staff = Client(app);
        await PostStatusAsync(staff, "/api/account/sign-in/name", new { userId = staffId });
        Assert.Single(await ProductionAsync(staff));

        using var nobody = Client(app);
        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(nobody, "/api/production"));
    }
}
