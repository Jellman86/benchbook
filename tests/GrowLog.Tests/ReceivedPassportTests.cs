using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrowLog.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using static GrowLog.Tests.AccountHarness;

namespace GrowLog.Tests;

public sealed class ReceivedPassportTests(PostgresFixture database)
{
    private sealed record Scene(TwoPersonNursery Nursery, Guid Supplier, Guid Batch, Guid Container) : IAsyncDisposable
    {
        public HttpClient Staff => Nursery.Staff;

        public ValueTask DisposeAsync() => Nursery.DisposeAsync();
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<Guid> AddAsync(HttpClient client, string path, object body) =>
        Json(await PostForTextAsync(client, path, body)).GetProperty("id").GetGuid();

    private async Task<Scene> CreateSceneAsync()
    {
        var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = await AddAsync(nursery.Owner, "/api/plants", new { commonName = "Lavender", variety = "Hidcote", botanicalName = "Lavandula angustifolia", xylellaHost = false, protectedVariety = false });
        var supplier = await AddAsync(nursery.Owner, "/api/suppliers", new { name = "Hillside Plants", passportRegistrationNumber = "12345" });
        var started = Json(await PostForTextAsync(nursery.Staff, "/api/batches", new
        {
            eventId = Guid.NewGuid(),
            occurredAt = DateTimeOffset.UtcNow,
            plantId = plant,
            quantity = 40,
            source = "BoughtIn",
            supplierId = supplier,
            originCountry = "NL",
            containerKind = "Tray",
        }));

        return new Scene(nursery, supplier, started.GetProperty("batchId").GetGuid(), started.GetProperty("containerId").GetGuid());
    }

    private static object Passport(Scene scene, string partA = "Lavandula angustifolia", string partB = "12345", string? partC = "LOT-77", string partD = "NL", string? partE = null, DateOnly? receivedOn = null) => new
    {
        batchId = scene.Batch,
        supplierId = scene.Supplier,
        receivedOn = receivedOn ?? Today,
        partA,
        partB,
        partC,
        partD,
        partE,
    };

    private static Task<JsonElement> ListAsync(Scene scene) =>
        scene.Staff.GetFromJsonAsync<JsonElement>(Route($"/api/received-passports?batchId={scene.Batch}"), Cancellation);

    private static Task<Guid> AddPassportAsync(Scene scene) =>
        AddAsync(scene.Staff, "/api/received-passports", Passport(scene));

    [Fact]
    public async Task A_passport_is_kept_exactly_as_printed_with_who_supplied_it()
    {
        await using var scene = await CreateSceneAsync();

        await AddAsync(scene.Staff, "/api/received-passports", Passport(scene, partB: "GB 12345", partC: "Lot/77 b"));

        var stored = (await ListAsync(scene)).EnumerateArray().Single();
        Assert.Equal("GB 12345", stored.GetProperty("partB").GetString());
        Assert.Equal("Lot/77 b", stored.GetProperty("partC").GetString());
        Assert.Equal("Hillside Plants", stored.GetProperty("supplierName").GetString());
        Assert.Equal("Held", stored.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Part_c_and_part_e_can_be_left_blank_but_the_others_cannot()
    {
        await using var scene = await CreateSceneAsync();

        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(scene.Staff, "/api/received-passports", Passport(scene, partC: null)));

        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(scene.Staff, "/api/received-passports", Passport(scene, partA: " ")));
        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(scene.Staff, "/api/received-passports", Passport(scene, partB: "")));
        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(scene.Staff, "/api/received-passports", Passport(scene, partD: "")));
    }

    [Fact]
    public async Task A_date_in_the_future_is_refused()
    {
        await using var scene = await CreateSceneAsync();

        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(scene.Staff, "/api/received-passports", Passport(scene, receivedOn: Today.AddDays(5))));
    }

    [Fact]
    public async Task A_passport_needs_a_known_supplier_and_batch()
    {
        await using var scene = await CreateSceneAsync();
        var body = new { batchId = scene.Batch, supplierId = Guid.NewGuid(), receivedOn = Today, partA = "A", partB = "B", partC = (string?)null, partD = "NL", partE = (string?)null };

        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(scene.Staff, "/api/received-passports", body));
    }

    [Fact]
    public async Task A_draft_fills_in_what_is_already_known()
    {
        await using var scene = await CreateSceneAsync();

        var draft = await scene.Staff.GetFromJsonAsync<JsonElement>(Route($"/api/batches/{scene.Batch}/received-passport-draft"), Cancellation);

        Assert.Equal(scene.Supplier, draft.GetProperty("supplierId").GetGuid());
        Assert.Equal("Lavandula angustifolia", draft.GetProperty("partA").GetString());
        Assert.Equal("12345", draft.GetProperty("partB").GetString());
        Assert.Equal("NL", draft.GetProperty("partD").GetString());
    }

    [Theory]
    [InlineData("UsedUp")]
    [InlineData("NoLongerValid")]
    [InlineData("Held")]
    public async Task The_status_can_change_and_the_change_is_recorded(string status)
    {
        await using var scene = await CreateSceneAsync();
        var id = await AddPassportAsync(scene);

        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(scene.Staff, $"/api/received-passports/{id}/status", new { status }));

        Assert.Equal(status, (await ListAsync(scene))[0].GetProperty("status").GetString());

        await using var scope = scene.Nursery.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GrowLogDbContext>();
        Assert.Equal(scene.Nursery.StaffId, (await db.CatalogueChanges.OrderBy(c => c.Id).LastAsync(c => c.EntityKey == id.ToString(), Cancellation)).ChangedBy);
    }

    [Fact]
    public async Task An_unknown_status_is_refused()
    {
        await using var scene = await CreateSceneAsync();
        var id = await AddPassportAsync(scene);

        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(scene.Staff, $"/api/received-passports/{id}/status", new { status = "Lost" }));
    }

    [Fact]
    public async Task The_database_refuses_to_change_what_was_printed_or_remove_a_passport()
    {
        await using var scene = await CreateSceneAsync();
        var id = await AddPassportAsync(scene);
        await using var scope = scene.Nursery.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GrowLogDbContext>();
        var passports = db.Set<ReceivedPassport>().Where(p => p.Id == id);

        var update = await Assert.ThrowsAsync<PostgresException>(() =>
            passports.ExecuteUpdateAsync(set => set.SetProperty(p => p.PartB, "99999"), Cancellation));
        var delete = await Assert.ThrowsAsync<PostgresException>(() => passports.ExecuteDeleteAsync(Cancellation));
        var truncate = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("TRUNCATE received_passports", Cancellation));

        Assert.Contains("append-only", update.MessageText, StringComparison.Ordinal);
        Assert.Contains("append-only", delete.MessageText, StringComparison.Ordinal);
        Assert.Contains("append-only", truncate.MessageText, StringComparison.Ordinal);
    }

    private static Task<HttpResponseMessage> PotOnAsync(Scene scene, Guid eventId) =>
        PostAsync(scene.Staff, $"/api/containers/{scene.Container}/transfer", new { eventId, occurredAt = DateTimeOffset.UtcNow, type = "PottedOn", batchId = scene.Batch, quantity = 5, containerKind = "PotGroup" });

    [Fact]
    public async Task A_passport_shows_when_its_plants_have_since_been_potted_on()
    {
        await using var scene = await CreateSceneAsync();
        await AddPassportAsync(scene);
        Assert.False((await ListAsync(scene))[0].GetProperty("plantsChangedSince").GetBoolean());

        using var potted = await PotOnAsync(scene, Guid.NewGuid());

        Assert.True((await ListAsync(scene))[0].GetProperty("plantsChangedSince").GetBoolean());
    }

    [Fact]
    public async Task Undoing_the_pot_on_clears_that_warning()
    {
        await using var scene = await CreateSceneAsync();
        await AddPassportAsync(scene);
        var eventId = Guid.NewGuid();
        using var potted = await PotOnAsync(scene, eventId);

        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(scene.Staff, $"/api/events/{eventId}/correct", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow }));

        Assert.False((await ListAsync(scene))[0].GetProperty("plantsChangedSince").GetBoolean());
    }

    [Fact]
    public async Task A_corrected_pot_on_still_counts_as_potted_on()
    {
        await using var scene = await CreateSceneAsync();
        await AddPassportAsync(scene);
        var eventId = Guid.NewGuid();
        using var potted = await PotOnAsync(scene, eventId);

        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(scene.Staff, $"/api/events/{eventId}/correct", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow, quantity = 2 }));

        Assert.True((await ListAsync(scene))[0].GetProperty("plantsChangedSince").GetBoolean());
    }

    [Fact]
    public async Task Someone_not_signed_in_cannot_see_or_add_passports()
    {
        await using var scene = await CreateSceneAsync();
        using var stranger = Client(scene.Nursery.App);

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(stranger, $"/api/received-passports?batchId={scene.Batch}"));
        Assert.Equal(HttpStatusCode.Unauthorized, await PostStatusAsync(stranger, "/api/received-passports", Passport(scene)));
    }
}
