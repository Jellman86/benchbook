using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrowLog.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GrowLog.Tests.AccountHarness;

namespace GrowLog.Tests;

public sealed class WorkApiTests(PostgresFixture database)
{
    /// <summary>A nursery with a plant, pot sizes and two benches, ready to record work.</summary>
    private sealed record Scene(TwoPersonNursery Nursery, Guid Plant, Guid SmallPot, Guid LargePot, Guid Bench1, Guid Bench2) : IAsyncDisposable
    {
        public HttpClient Staff => Nursery.Staff;

        public ValueTask DisposeAsync() => Nursery.DisposeAsync();
    }

    private sealed record StartedBatch(Guid EventId, Guid BatchId, string TraceabilityCode, Guid ContainerId, string LabelCode);

    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

    private static async Task<Guid> AddAsync(HttpClient client, string path, object body) =>
        Json(await PostForTextAsync(client, path, body)).GetProperty("id").GetGuid();

    private async Task<Scene> CreateSceneAsync()
    {
        var nursery = await TwoPersonNursery.CreateAsync(database);
        var owner = nursery.Owner;
        var plant = await AddAsync(owner, "/api/plants", new { commonName = "Lavender", variety = "Hidcote", xylellaHost = false, protectedVariety = false });
        var small = await AddAsync(owner, "/api/pot-sizes", new { name = "9cm" });
        var large = await AddAsync(owner, "/api/pot-sizes", new { name = "2L" });
        var site = await AddAsync(owner, "/api/locations", new { name = "Croft", kind = "Site" });
        var house = await AddAsync(owner, "/api/locations", new { name = "Greenhouse 2", kind = "Greenhouse", parentId = site });
        var bench1 = await AddAsync(owner, "/api/locations", new { name = "Bench 1", kind = "Bench", parentId = house });
        var bench2 = await AddAsync(owner, "/api/locations", new { name = "Bench 2", kind = "Bench", parentId = house });
        return new Scene(nursery, plant, small, large, bench1, bench2);
    }

    private static object StartBody(Scene scene, Guid eventId, string source = "SeedLot", int quantity = 40) => new
    {
        eventId,
        occurredAt = Now,
        plantId = scene.Plant,
        quantity,
        source,
        originCountry = "gb",
        containerKind = "Tray",
        locationId = scene.Bench1,
        potSizeId = scene.SmallPot,
    };

    private static async Task<StartedBatch> StartAsync(Scene scene, int quantity = 40)
    {
        using var response = await PostAsync(scene.Staff, "/api/batches", StartBody(scene, Guid.NewGuid(), quantity: quantity));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StartedBatch>(Cancellation))!;
    }

    private static Task<JsonElement> ScanAsync(Scene scene, string label) =>
        scene.Staff.GetFromJsonAsync<JsonElement>(Route($"/api/containers/by-label/{label}"), Cancellation);

    private static async Task<int> QuantityAsync(Scene scene, string label)
    {
        var contents = (await ScanAsync(scene, label)).GetProperty("contents");
        return contents.GetArrayLength() == 0 ? 0 : contents[0].GetProperty("quantity").GetInt32();
    }

    private static Task<HttpStatusCode> LoseAsync(Scene scene, StartedBatch batch, int quantity, Guid? eventId = null, string reason = "Pest") =>
        PostStatusAsync(scene.Staff, $"/api/containers/{batch.ContainerId}/losses", new { eventId = eventId ?? Guid.NewGuid(), occurredAt = Now, batchId = batch.BatchId, quantity, reason });

    private static async Task<string> ProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        return Json(await response.Content.ReadAsStringAsync(Cancellation)).GetProperty("problem").GetString()!;
    }

    private static Task<JsonElement> RecentAsync(HttpClient client, string query = "") =>
        client.GetFromJsonAsync<JsonElement>(Route($"/api/events/recent{query}"), Cancellation);

    private static Task<HttpResponseMessage> CorrectAsync(Scene scene, Guid original, object? changes = null)
    {
        var quantity = changes?.GetType().GetProperty("quantity")?.GetValue(changes);
        var locationId = changes?.GetType().GetProperty("locationId")?.GetValue(changes);
        return PostAsync(scene.Staff, $"/api/events/{original}/correct", new { eventId = Guid.NewGuid(), occurredAt = Now, quantity, locationId });
    }

    [Fact]
    public async Task Starting_a_batch_creates_a_labelled_container_a_scan_can_find()
    {
        await using var scene = await CreateSceneAsync();

        var batch = await StartAsync(scene);

        var scanned = await ScanAsync(scene, batch.LabelCode);
        Assert.Equal(batch.ContainerId, scanned.GetProperty("id").GetGuid());
        Assert.Equal("Bench 1", scanned.GetProperty("locationName").GetString());
        Assert.Equal("9cm", scanned.GetProperty("potSizeName").GetString());
        var line = scanned.GetProperty("contents").EnumerateArray().Single();
        Assert.Equal("Lavender", line.GetProperty("commonName").GetString());
        Assert.Equal(batch.TraceabilityCode, line.GetProperty("traceabilityCode").GetString());
        Assert.Equal(40, line.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task A_scan_ignores_capitals_and_an_unknown_label_is_not_found()
    {
        await using var scene = await CreateSceneAsync();
        var batch = await StartAsync(scene);

        Assert.Equal(40, await QuantityAsync(scene, batch.LabelCode.ToLowerInvariant()));
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(scene.Staff, "/api/containers/by-label/NOPE"));
    }

    [Theory]
    [InlineData("SeedLot", "Sown")]
    [InlineData("BoughtIn", "Received")]
    [InlineData("Cuttings", "CuttingsTaken")]
    public async Task The_first_event_depends_on_where_the_plants_came_from(string source, string eventType)
    {
        await using var scene = await CreateSceneAsync();

        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(scene.Staff, "/api/batches", StartBody(scene, Guid.NewGuid(), source)));

        Assert.Equal(eventType, (await RecentAsync(scene.Staff))[0].GetProperty("type").GetString());
    }

    [Fact]
    public async Task Resending_a_started_batch_returns_the_original_and_creates_nothing_new()
    {
        await using var scene = await CreateSceneAsync();
        var eventId = Guid.NewGuid();
        var body = StartBody(scene, eventId);

        var first = Json(await PostForTextAsync(scene.Staff, "/api/batches", body));
        using var second = await PostAsync(scene.Staff, "/api/batches", body);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var resent = Json(await second.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal(first.GetProperty("labelCode").GetString(), resent.GetProperty("labelCode").GetString());

        await using var scope = scene.Nursery.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GrowLogDbContext>();
        Assert.Equal(1, await db.Containers.CountAsync(Cancellation));
        Assert.Equal(1, await db.Batches.CountAsync(Cancellation));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1_000_001)]
    public async Task A_batch_needs_a_sensible_quantity(int quantity)
    {
        await using var scene = await CreateSceneAsync();

        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(scene.Staff, "/api/batches", StartBody(scene, Guid.NewGuid(), quantity: quantity)));
    }

    [Fact]
    public async Task A_batch_for_an_unknown_plant_is_refused_and_leaves_nothing_behind()
    {
        await using var scene = await CreateSceneAsync();
        var body = new { eventId = Guid.NewGuid(), occurredAt = Now, plantId = Guid.NewGuid(), quantity = 5, source = "SeedLot", originCountry = "GB", containerKind = "Tray" };

        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(scene.Staff, "/api/batches", body));

        await using var scope = scene.Nursery.App.Services.CreateAsyncScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<GrowLogDbContext>().Containers.CountAsync(Cancellation));
    }

    [Fact]
    public async Task A_loss_reduces_the_count_and_records_who_and_why()
    {
        await using var scene = await CreateSceneAsync();
        var batch = await StartAsync(scene);

        Assert.Equal(HttpStatusCode.Created, await LoseAsync(scene, batch, 3, reason: "DampingOff"));

        Assert.Equal(37, await QuantityAsync(scene, batch.LabelCode));

        var latest = (await RecentAsync(scene.Staff))[0];
        Assert.Equal("Lost", latest.GetProperty("type").GetString());
        Assert.Equal("DampingOff", latest.GetProperty("details").GetProperty("reason").GetString());

        await using var scope = scene.Nursery.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GrowLogDbContext>();
        var id = latest.GetProperty("id").GetGuid();
        Assert.Equal(scene.Nursery.StaffId, (await db.Events.SingleAsync(e => e.Id == id, Cancellation)).RecordedBy);
    }

    [Fact]
    public async Task Losing_more_than_are_there_is_refused_with_a_reason_a_screen_can_use()
    {
        await using var scene = await CreateSceneAsync();
        var batch = await StartAsync(scene, 2);

        using var response = await PostAsync(scene.Staff, $"/api/containers/{batch.ContainerId}/losses", new { eventId = Guid.NewGuid(), occurredAt = Now, batchId = batch.BatchId, quantity = 3, reason = "Frost" });

        Assert.Equal("BelowZero", await ProblemAsync(response));
        Assert.Equal(2, await QuantityAsync(scene, batch.LabelCode));
    }

    [Fact]
    public async Task An_unknown_loss_reason_is_refused()
    {
        await using var scene = await CreateSceneAsync();
        var batch = await StartAsync(scene);

        Assert.Equal(HttpStatusCode.BadRequest, await LoseAsync(scene, batch, 1, reason: "Gremlins"));
    }

    [Fact]
    public async Task Resending_a_loss_counts_it_once()
    {
        await using var scene = await CreateSceneAsync();
        var batch = await StartAsync(scene);
        var eventId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Created, await LoseAsync(scene, batch, 5, eventId));
        Assert.Equal(HttpStatusCode.OK, await LoseAsync(scene, batch, 5, eventId));

        Assert.Equal(35, await QuantityAsync(scene, batch.LabelCode));
    }

    [Fact]
    public async Task Moving_a_container_changes_where_it_is()
    {
        await using var scene = await CreateSceneAsync();
        var batch = await StartAsync(scene);

        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(scene.Staff, $"/api/containers/{batch.ContainerId}/move", new { eventId = Guid.NewGuid(), occurredAt = Now, locationId = scene.Bench2 }));

        Assert.Equal("Bench 2", (await ScanAsync(scene, batch.LabelCode)).GetProperty("locationName").GetString());
    }

    [Fact]
    public async Task Moving_to_an_unknown_place_is_refused()
    {
        await using var scene = await CreateSceneAsync();
        var batch = await StartAsync(scene);

        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(scene.Staff, $"/api/containers/{batch.ContainerId}/move", new { eventId = Guid.NewGuid(), occurredAt = Now, locationId = Guid.NewGuid() }));
    }

    private static object TransferBody(Scene scene, StartedBatch batch, Guid eventId, int quantity, string type = "PottedOn") => new
    {
        eventId,
        occurredAt = Now,
        type,
        batchId = batch.BatchId,
        quantity,
        containerKind = "PotGroup",
        potSizeId = scene.LargePot,
    };

    [Fact]
    public async Task Potting_on_part_of_a_tray_makes_a_new_labelled_container_in_the_same_batch()
    {
        await using var scene = await CreateSceneAsync();
        var batch = await StartAsync(scene);

        var moved = Json(await PostForTextAsync(scene.Staff, $"/api/containers/{batch.ContainerId}/transfer", TransferBody(scene, batch, Guid.NewGuid(), 15)));

        var label = moved.GetProperty("labelCode").GetString()!;
        Assert.NotEqual(batch.LabelCode, label, StringComparer.Ordinal);
        Assert.Equal(25, await QuantityAsync(scene, batch.LabelCode));

        var target = await ScanAsync(scene, label);
        Assert.Equal(15, target.GetProperty("contents")[0].GetProperty("quantity").GetInt32());
        Assert.Equal(batch.TraceabilityCode, target.GetProperty("contents")[0].GetProperty("traceabilityCode").GetString());
        Assert.Equal("2L", target.GetProperty("potSizeName").GetString());
        Assert.Equal("Bench 1", target.GetProperty("locationName").GetString());
    }

    [Fact]
    public async Task Resending_a_pot_on_does_not_make_another_container()
    {
        await using var scene = await CreateSceneAsync();
        var batch = await StartAsync(scene);
        var eventId = Guid.NewGuid();
        var body = TransferBody(scene, batch, eventId, 15);

        var first = Json(await PostForTextAsync(scene.Staff, $"/api/containers/{batch.ContainerId}/transfer", body));
        var second = Json(await PostForTextAsync(scene.Staff, $"/api/containers/{batch.ContainerId}/transfer", body));

        Assert.Equal(first.GetProperty("labelCode").GetString(), second.GetProperty("labelCode").GetString());
        Assert.Equal(25, await QuantityAsync(scene, batch.LabelCode));
    }

    [Fact]
    public async Task Potting_on_more_than_are_there_is_refused_and_makes_no_container()
    {
        await using var scene = await CreateSceneAsync();
        var batch = await StartAsync(scene, 10);

        using var response = await PostAsync(scene.Staff, $"/api/containers/{batch.ContainerId}/transfer", TransferBody(scene, batch, Guid.NewGuid(), 11));

        Assert.Equal("BelowZero", await ProblemAsync(response));

        await using var scope = scene.Nursery.App.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<GrowLogDbContext>().Containers.CountAsync(Cancellation));
    }

    [Fact]
    public async Task Only_potting_on_and_pricking_out_are_transfers()
    {
        await using var scene = await CreateSceneAsync();
        var batch = await StartAsync(scene);

        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(scene.Staff, $"/api/containers/{batch.ContainerId}/transfer", TransferBody(scene, batch, Guid.NewGuid(), 5, "Lost")));
    }

    [Theory]
    [InlineData(31)]
    [InlineData(40)]
    [InlineData(55)]
    public async Task A_stock_count_sets_the_number(int counted)
    {
        await using var scene = await CreateSceneAsync();
        var batch = await StartAsync(scene);

        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(scene.Staff, $"/api/containers/{batch.ContainerId}/count", new { eventId = Guid.NewGuid(), occurredAt = Now, batchId = batch.BatchId, counted }));

        Assert.Equal(counted, await QuantityAsync(scene, batch.LabelCode));
    }

    [Fact]
    public async Task A_treatment_records_the_product_dose_and_area()
    {
        await using var scene = await CreateSceneAsync();
        var batch = await StartAsync(scene);

        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(scene.Staff, $"/api/containers/{batch.ContainerId}/treatments", new { eventId = Guid.NewGuid(), occurredAt = Now, product = "Neem oil", dose = "5ml/L", area = "Tray" }));

        var details = (await RecentAsync(scene.Staff))[0].GetProperty("details");
        Assert.Equal("Neem oil", details.GetProperty("product").GetString());
        Assert.Equal("5ml/L", details.GetProperty("dose").GetString());
        Assert.Equal("Tray", details.GetProperty("area").GetString());
    }

    [Fact]
    public async Task A_treatment_needs_all_its_details()
    {
        await using var scene = await CreateSceneAsync();
        var batch = await StartAsync(scene);

        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(scene.Staff, $"/api/containers/{batch.ContainerId}/treatments", new { eventId = Guid.NewGuid(), occurredAt = Now, product = "Neem oil", dose = " ", area = "Tray" }));
    }

    [Fact]
    public async Task Germination_and_notes_are_recorded()
    {
        await using var scene = await CreateSceneAsync();
        var batch = await StartAsync(scene);

        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(scene.Staff, $"/api/containers/{batch.ContainerId}/germinated", new { eventId = Guid.NewGuid(), occurredAt = Now }));
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(scene.Staff, $"/api/containers/{batch.ContainerId}/notes", new { eventId = Guid.NewGuid(), occurredAt = Now, note = "Looking strong" }));

        var recent = await RecentAsync(scene.Staff);
        Assert.Equal("Note", recent[0].GetProperty("type").GetString());
        Assert.Equal("Germinated", recent[1].GetProperty("type").GetString());
        Assert.Equal(40, await QuantityAsync(scene, batch.LabelCode));
    }

    [Fact]
    public async Task Undo_reverses_a_loss_and_removes_it_from_recent_actions()
    {
        await using var scene = await CreateSceneAsync();
        var batch = await StartAsync(scene);
        var loss = Guid.NewGuid();
        await LoseAsync(scene, batch, 5, loss);

        using var response = await CorrectAsync(scene, loss);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(40, await QuantityAsync(scene, batch.LabelCode));
        Assert.DoesNotContain((await RecentAsync(scene.Staff)).EnumerateArray(), action => action.GetProperty("id").GetGuid() == loss);
    }

    [Fact]
    public async Task A_correction_can_replace_a_quantity_and_keeps_the_loss_reason()
    {
        await using var scene = await CreateSceneAsync();
        var batch = await StartAsync(scene);
        var loss = Guid.NewGuid();
        await LoseAsync(scene, batch, 5, loss, "Frost");

        using var response = await CorrectAsync(scene, loss, new { quantity = (int?)2 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(38, await QuantityAsync(scene, batch.LabelCode));

        await using var scope = scene.Nursery.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GrowLogDbContext>();
        var correction = await db.Events.SingleAsync(e => e.CorrectsEventId == loss, Cancellation);
        Assert.Equal("Frost", Json(correction.Payload).GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Correcting_a_pot_on_quantity_adjusts_both_containers()
    {
        await using var scene = await CreateSceneAsync();
        var batch = await StartAsync(scene);
        var transfer = Guid.NewGuid();
        var moved = Json(await PostForTextAsync(scene.Staff, $"/api/containers/{batch.ContainerId}/transfer", TransferBody(scene, batch, transfer, 15)));

        using var response = await CorrectAsync(scene, transfer, new { quantity = (int?)10 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(30, await QuantityAsync(scene, batch.LabelCode));
        Assert.Equal(10, await QuantityAsync(scene, moved.GetProperty("labelCode").GetString()!));
    }

    [Fact]
    public async Task A_correction_can_replace_where_something_went_without_losing_its_count()
    {
        await using var scene = await CreateSceneAsync();
        var batch = await StartAsync(scene);
        var recent = await RecentAsync(scene.Staff);

        using var response = await CorrectAsync(scene, recent[0].GetProperty("id").GetGuid(), new { locationId = (Guid?)scene.Bench2 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var scanned = await ScanAsync(scene, batch.LabelCode);
        Assert.Equal("Bench 2", scanned.GetProperty("locationName").GetString());
        Assert.Equal(40, scanned.GetProperty("contents")[0].GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task An_event_can_only_be_corrected_once()
    {
        await using var scene = await CreateSceneAsync();
        var batch = await StartAsync(scene);
        var loss = Guid.NewGuid();
        await LoseAsync(scene, batch, 5, loss);
        using var first = await CorrectAsync(scene, loss);

        using var second = await CorrectAsync(scene, loss);

        Assert.Equal("AlreadyCorrected", await ProblemAsync(second));
    }

    [Fact]
    public async Task A_correction_that_would_leave_a_negative_count_is_refused()
    {
        await using var scene = await CreateSceneAsync();
        var batch = await StartAsync(scene);
        await LoseAsync(scene, batch, 35);
        var recent = await RecentAsync(scene.Staff);
        var start = recent.EnumerateArray().Single(a => string.Equals(a.GetProperty("type").GetString(), "Sown", StringComparison.Ordinal)).GetProperty("id").GetGuid();

        using var response = await CorrectAsync(scene, start, new { quantity = (int?)10 });

        Assert.Equal("BelowZero", await ProblemAsync(response));
    }

    [Fact]
    public async Task A_stock_count_can_be_undone_but_not_given_a_new_quantity()
    {
        await using var scene = await CreateSceneAsync();
        var batch = await StartAsync(scene);
        var count = Guid.NewGuid();
        await PostStatusAsync(scene.Staff, $"/api/containers/{batch.ContainerId}/count", new { eventId = count, occurredAt = Now, batchId = batch.BatchId, counted = 31 });

        using var withQuantity = await CorrectAsync(scene, count, new { quantity = (int?)20 });
        Assert.Equal(HttpStatusCode.BadRequest, withQuantity.StatusCode);

        using var undo = await CorrectAsync(scene, count);
        Assert.Equal(HttpStatusCode.Created, undo.StatusCode);
        Assert.Equal(40, await QuantityAsync(scene, batch.LabelCode));
    }

    [Fact]
    public async Task Correcting_an_unknown_event_is_not_found()
    {
        await using var scene = await CreateSceneAsync();

        using var response = await CorrectAsync(scene, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Recent_actions_are_each_persons_own_newest_first_with_what_a_repeat_needs()
    {
        await using var scene = await CreateSceneAsync();
        var batch = await StartAsync(scene);
        await LoseAsync(scene, batch, 1);

        var mine = await RecentAsync(scene.Staff);
        Assert.Equal("Lost", mine[0].GetProperty("type").GetString());
        Assert.Equal("Sown", mine[1].GetProperty("type").GetString());

        var line = mine[1].GetProperty("lines")[0];
        Assert.Equal(scene.Plant, line.GetProperty("plantId").GetGuid());
        Assert.Equal("Hidcote", line.GetProperty("variety").GetString());
        Assert.Equal(40, line.GetProperty("quantityChange").GetInt32());
        Assert.Equal("SeedLot", line.GetProperty("source").GetString());
        Assert.Equal("Bench 1", mine[1].GetProperty("locationName").GetString());

        Assert.Equal(0, (await RecentAsync(scene.Nursery.Owner)).GetArrayLength());
    }

    [Fact]
    public async Task Recent_actions_respect_the_limit()
    {
        await using var scene = await CreateSceneAsync();
        var batch = await StartAsync(scene);
        await LoseAsync(scene, batch, 1);
        await LoseAsync(scene, batch, 1);

        Assert.Equal(2, (await RecentAsync(scene.Staff, "?limit=2")).GetArrayLength());
    }

    [Fact]
    public async Task Someone_not_signed_in_cannot_record_or_read_work()
    {
        await using var scene = await CreateSceneAsync();
        var batch = await StartAsync(scene);
        using var stranger = Client(scene.Nursery.App);

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(stranger, $"/api/containers/by-label/{batch.LabelCode}"));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(stranger, "/api/events/recent"));
        Assert.Equal(HttpStatusCode.Unauthorized, await PostStatusAsync(stranger, "/api/batches", StartBody(scene, Guid.NewGuid())));
    }
}
