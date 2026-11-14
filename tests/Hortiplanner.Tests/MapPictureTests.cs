using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using static Hortiplanner.Tests.AccountHarness;

namespace Hortiplanner.Tests;

/// <summary>A nursery without a satellite key draws its places on its own picture, kept apart from the map's outlines.</summary>
public sealed class MapPictureTests(PostgresFixture database)
{
    // The smallest PNG: a signature is all the server checks.
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    private static readonly double[][] OnPicture = [[0.2, 0.3], [0.2, 0.6], [0.5, 0.6], [0.5, 0.3]];

    private static readonly double[][] OnEarth = [[53.08, -1.54], [53.081, -1.54], [53.081, -1.539]];

    private static Task<JsonElement> MapAsync(HttpClient client) =>
        client.GetFromJsonAsync<JsonElement>(Route("/api/locations/map"), Cancellation);

    private static async Task<HttpStatusCode> UploadAsync(HttpClient client, byte[] content)
    {
        using var body = new ByteArrayContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await client.PutAsync(Route("/api/settings/map/picture"), body, Cancellation);
        return response.StatusCode;
    }

    private static async Task<HttpStatusCode> PutStatusAsync(HttpClient client, string path, object body)
    {
        using var response = await client.PutAsJsonAsync(Route(path), body, Cancellation);
        return response.StatusCode;
    }

    [Fact]
    public async Task Places_drawn_on_the_picture_and_on_the_map_are_kept_apart()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var site = Json(await PostForTextAsync(nursery.Owner, "/api/locations", new { name = "Croft", kind = "Site" })).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, await PutStatusAsync(nursery.Owner, $"/api/locations/{site}/outline", new { points = OnEarth }));
        Assert.False((await MapAsync(nursery.Owner)).GetProperty("onPicture").GetBoolean());

        Assert.Equal(HttpStatusCode.Forbidden, await UploadAsync(nursery.Staff, Png));
        Assert.Equal(HttpStatusCode.BadRequest, await UploadAsync(nursery.Owner, [1, 2, 3, 4]));
        Assert.Equal(HttpStatusCode.NoContent, await UploadAsync(nursery.Owner, Png));
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(nursery.Owner, "/api/settings/map/picture"));

        // Uploading starts drawing on the picture, which has nothing drawn yet.
        var map = await MapAsync(nursery.Owner);
        Assert.True(map.GetProperty("onPicture").GetBoolean());
        Assert.Equal(0, map.GetProperty("outlines").GetArrayLength());

        Assert.Equal(HttpStatusCode.BadRequest, await PutStatusAsync(nursery.Owner, $"/api/locations/{site}/outline", new { points = OnEarth, onPicture = true }));
        Assert.Equal(HttpStatusCode.NoContent, await PutStatusAsync(nursery.Owner, $"/api/locations/{site}/outline", new { points = OnPicture, onPicture = true }));
        Assert.Equal(0.2, Assert.Single((await MapAsync(nursery.Owner)).GetProperty("outlines").EnumerateArray()).GetProperty("points")[0][0].GetDouble());

        // Back to the map: its own outline is still there.
        Assert.Equal(HttpStatusCode.NoContent, await PutStatusAsync(nursery.Owner, "/api/settings/map/use-picture", new { use = false }));
        map = await MapAsync(nursery.Owner);
        Assert.False(map.GetProperty("onPicture").GetBoolean());
        Assert.Equal(53.08, Assert.Single(map.GetProperty("outlines").EnumerateArray()).GetProperty("points")[0][0].GetDouble());

        // Removing the picture goes back to the map, and the picture can't be chosen again until there's another.
        Assert.Equal(HttpStatusCode.NoContent, await PutStatusAsync(nursery.Owner, "/api/settings/map/use-picture", new { use = true }));
        using (var removed = await nursery.Owner.DeleteAsync(Route("/api/settings/map/picture"), Cancellation))
        {
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        }

        Assert.False((await MapAsync(nursery.Owner)).GetProperty("onPicture").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, await PutStatusAsync(nursery.Owner, "/api/settings/map/use-picture", new { use = true }));
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(nursery.Owner, "/api/settings/map/picture"));
    }
}
