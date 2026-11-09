using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using static GrowLog.Tests.AccountHarness;

namespace GrowLog.Tests;

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
}
