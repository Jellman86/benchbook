using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using static Hortiplanner.Tests.AccountHarness;

namespace Hortiplanner.Tests;

public sealed class PhotoTests(PostgresFixture database)
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

    private static async Task<Guid> AddPlantAsync(HttpClient client) =>
        Json(await PostForTextAsync(client, "/api/plants", new { commonName = "Lavender", xylellaHost = false, protectedVariety = false })).GetProperty("id").GetGuid();

    private static async Task<Guid> StartBatchAsync(HttpClient client, Guid plantId) =>
        Json(await PostForTextAsync(client, "/api/batches", new
        {
            eventId = Guid.CreateVersion7(),
            occurredAt = DateTimeOffset.UtcNow,
            plantId,
            quantity = 10,
            source = "SeedLot",
            originCountry = "GB",
            containerKind = "Tray",
        })).GetProperty("batchId").GetGuid();

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string path, byte[] body, bool put = false)
    {
        using var content = new ByteArrayContent(body);

        return put
            ? await client.PutAsync(Route(path), content, Cancellation)
            : await client.PostAsync(Route(path), content, Cancellation);
    }

    [Fact]
    public async Task A_packet_photo_can_be_added_to_a_batch_and_read_back()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);
        var batchId = await StartBatchAsync(owner, await AddPlantAsync(owner));

        using var added = await UploadAsync(owner, $"/api/batches/{batchId}/photos?kind=Packet", Jpeg);
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        var photo = Assert.Single((await owner.GetFromJsonAsync<JsonElement>(Route($"/api/batches/{batchId}/photos"), Cancellation)).EnumerateArray());
        Assert.Equal("Packet", photo.GetProperty("kind").GetString());

        using var read = await owner.GetAsync(Route($"/api/batch-photos/{photo.GetProperty("id").GetGuid()}"), Cancellation);
        Assert.Equal(Jpeg, await read.Content.ReadAsByteArrayAsync(Cancellation));
        Assert.Equal("nosniff", Assert.Single(read.Headers.GetValues("X-Content-Type-Options")));
    }

    [Fact]
    public async Task Photos_of_the_plants_show_the_day_and_stage_and_a_wrong_one_can_be_removed()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var started = Json(await PostForTextAsync(nursery.Staff, "/api/batches", new
        {
            eventId = Guid.CreateVersion7(),
            occurredAt = DateTimeOffset.UtcNow.AddDays(-9),
            plantId = await AddPlantAsync(nursery.Owner),
            quantity = 10,
            source = "SeedLot",
            originCountry = "GB",
            containerKind = "Tray",
        }));
        var batchId = started.GetProperty("batchId").GetGuid();
        var tray = started.GetProperty("containerId").GetGuid();
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(nursery.Staff, $"/api/containers/{tray}/germinated", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow.AddDays(-2) }));

        using var staffPhoto = await UploadAsync(nursery.Staff, $"/api/batches/{batchId}/photos?kind=Growing&containerId={tray}", Jpeg);
        using var ownerPhoto = await UploadAsync(nursery.Owner, $"/api/batches/{batchId}/photos?kind=Packet", Jpeg);
        Assert.Equal(HttpStatusCode.Created, staffPhoto.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UploadAsync(nursery.Staff, $"/api/batches/{batchId}/photos?kind=Growing&containerId={Guid.NewGuid()}", Jpeg)).StatusCode);

        var seen = (await nursery.Staff.GetFromJsonAsync<JsonElement>(Route($"/api/batches/{batchId}/photos"), Cancellation)).EnumerateArray().ToList();
        Assert.Equal(2, seen.Count);
        Assert.Equal("Growing", seen[0].GetProperty("kind").GetString());
        Assert.Equal(10, seen[0].GetProperty("day").GetInt32());
        Assert.Equal("Germinated", seen[0].GetProperty("stage").GetString());
        Assert.True(seen[0].GetProperty("canRemove").GetBoolean());
        Assert.False(seen[1].GetProperty("canRemove").GetBoolean());

        using var refused = await nursery.Staff.DeleteAsync(Route($"/api/batch-photos/{seen[1].GetProperty("id").GetGuid()}"), Cancellation);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        using var removed = await nursery.Staff.DeleteAsync(Route($"/api/batch-photos/{seen[0].GetProperty("id").GetGuid()}"), Cancellation);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        using var byOwner = await nursery.Owner.DeleteAsync(Route($"/api/batch-photos/{seen[1].GetProperty("id").GetGuid()}"), Cancellation);
        Assert.Equal(HttpStatusCode.NoContent, byOwner.StatusCode);
        Assert.Empty((await nursery.Owner.GetFromJsonAsync<JsonElement>(Route($"/api/batches/{batchId}/photos"), Cancellation)).EnumerateArray());
    }

    [Fact]
    public async Task Something_that_is_not_a_picture_is_refused()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);
        var batchId = await StartBatchAsync(owner, await AddPlantAsync(owner));

        using var refused = await UploadAsync(owner, $"/api/batches/{batchId}/photos?kind=Label", "<svg onload=alert(1)>"u8.ToArray());

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, refused.StatusCode);
    }

    [Fact]
    public async Task A_photo_that_is_too_big_is_refused()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);
        var batchId = await StartBatchAsync(owner, await AddPlantAsync(owner));

        using var refused = await UploadAsync(owner, $"/api/batches/{batchId}/photos?kind=Label", new byte[(8 * 1024 * 1024) + 1]);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, refused.StatusCode);
    }

    [Fact]
    public async Task A_plant_has_one_picture_which_can_be_replaced()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);
        var plantId = await AddPlantAsync(owner);

        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(owner, $"/api/plants/{plantId}/photo"));

        using var first = await UploadAsync(owner, $"/api/plants/{plantId}/photo", Jpeg, put: true);
        byte[] replacement = [0x89, 0x50, 0x4E, 0x47, 9];
        using var second = await UploadAsync(owner, $"/api/plants/{plantId}/photo", replacement, put: true);

        using var read = await owner.GetAsync(Route($"/api/plants/{plantId}/photo"), Cancellation);
        Assert.Equal(replacement, await read.Content.ReadAsByteArrayAsync(Cancellation));
        Assert.Equal("image/png", read.Content.Headers.ContentType!.MediaType);

        var plant = Assert.Single((await owner.GetFromJsonAsync<JsonElement>(Route("/api/plants"), Cancellation)).EnumerateArray());
        Assert.True(plant.GetProperty("hasPhoto").GetBoolean());
    }

    [Fact]
    public async Task Photos_need_someone_signed_in()
    {
        await using var app = await CreateAppAsync(database);
        using var client = Client(app);

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(client, $"/api/batches/{Guid.CreateVersion7()}/photos"));
    }

    private static async Task<JsonElement> FindAsync(HttpClient client, string path, Guid id) =>
        (await client.GetFromJsonAsync<JsonElement>(Route(path), Cancellation)).EnumerateArray().Single(entry => entry.GetProperty("id").GetGuid() == id);

    [Fact]
    public async Task A_pot_size_can_have_photos_and_a_count()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);
        var id = Json(await PostForTextAsync(owner, "/api/pot-sizes", new { name = "9cm", quantityInStock = 40 })).GetProperty("id").GetGuid();

        using var photo = await UploadAsync(owner, $"/api/catalogue-photos/PotSize/{id}", Jpeg);
        Assert.Equal(HttpStatusCode.Created, photo.StatusCode);
        using var counted = await owner.PutAsJsonAsync(Route($"/api/pot-sizes/{id}/quantity"), new { quantity = 120 }, Cancellation);
        Assert.Equal(HttpStatusCode.NoContent, counted.StatusCode);

        var size = await FindAsync(owner, "/api/pot-sizes", id);
        Assert.Equal(120, size.GetProperty("quantityInStock").GetInt32());
        using var read = await owner.GetAsync(Route($"/api/catalogue-photos/{size.GetProperty("photoIds")[0].GetGuid()}"), Cancellation);
        Assert.Equal(Jpeg, await read.Content.ReadAsByteArrayAsync(Cancellation));
    }

    [Fact]
    public async Task A_negative_pot_count_is_refused()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);
        var id = Json(await PostForTextAsync(owner, "/api/pot-sizes", new { name = "9cm" })).GetProperty("id").GetGuid();

        using var refused = await owner.PutAsJsonAsync(Route($"/api/pot-sizes/{id}/quantity"), new { quantity = -1 }, Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task A_place_at_any_level_can_have_several_photos()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);
        var farm = Json(await PostForTextAsync(owner, "/api/locations", new { name = "The Farm", kind = "Site" })).GetProperty("id").GetGuid();
        var house = Json(await PostForTextAsync(owner, "/api/locations", new { name = "Greenhouse 1", kind = "Greenhouse", parentId = farm })).GetProperty("id").GetGuid();
        var bench = Json(await PostForTextAsync(owner, "/api/locations", new { name = "Bench 1", kind = "Bench", parentId = house })).GetProperty("id").GetGuid();

        foreach (var place in new[] { farm, house, bench, bench })
        {
            using var added = await UploadAsync(owner, $"/api/catalogue-photos/Location/{place}", Jpeg);
            Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        }

        Assert.Equal(1, (await FindAsync(owner, "/api/locations", farm)).GetProperty("photoIds").GetArrayLength());
        Assert.Equal(2, (await FindAsync(owner, "/api/locations", bench)).GetProperty("photoIds").GetArrayLength());
    }

    [Fact]
    public async Task Only_the_owner_can_remove_a_photo_and_a_missing_place_is_refused()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);
        var farm = Json(await PostForTextAsync(owner, "/api/locations", new { name = "The Farm", kind = "Site" })).GetProperty("id").GetGuid();
        using var added = await UploadAsync(owner, $"/api/catalogue-photos/Location/{farm}", Jpeg);
        var photoId = (await FindAsync(owner, "/api/locations", farm)).GetProperty("photoIds")[0].GetGuid();
        await AllowNameOnlyAsync(owner);
        var staffId = Json(await PostForTextAsync(owner, "/api/staff", new { name = "Jo Reed", nameOnly = true })).GetProperty("id").GetGuid();
        using var staff = Client(app);
        await PostStatusAsync(staff, "/api/account/sign-in/name", new { userId = staffId });

        using var refused = await staff.DeleteAsync(Route($"/api/catalogue-photos/{photoId}"), Cancellation);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        using var removed = await owner.DeleteAsync(Route($"/api/catalogue-photos/{photoId}"), Cancellation);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        using var missing = await UploadAsync(owner, $"/api/catalogue-photos/Location/{Guid.CreateVersion7()}", Jpeg);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
