using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using static GrowLog.Tests.AccountHarness;

namespace GrowLog.Tests;

public sealed class PlaceMapTests(PostgresFixture database)
{
    private static readonly double[][] Greenhouse =
    [
        [53.0823411, -1.5432101],
        [53.0824012, -1.5428803],
        [53.0821706, -1.5427904],
        [53.0821105, -1.5431208],
    ];

    private static async Task<Guid> AddPlaceAsync(HttpClient owner, string name = "Croft") =>
        Json(await PostForTextAsync(owner, "/api/locations", new { name, kind = "Site" })).GetProperty("id").GetGuid();

    private static Task<JsonElement> MapAsync(HttpClient client) =>
        client.GetFromJsonAsync<JsonElement>(Route("/api/locations/map"), Cancellation);

    private static async Task<HttpStatusCode> PutStatusAsync(HttpClient client, string path, object body)
    {
        using var response = await client.PutAsJsonAsync(Route(path), body, Cancellation);
        return response.StatusCode;
    }

    [Fact]
    public async Task The_owner_draws_a_place_on_the_map_and_can_take_it_off_again()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var site = await AddPlaceAsync(nursery.Owner);
        await AddPlaceAsync(nursery.Owner, "Not drawn");

        Assert.Equal(HttpStatusCode.NoContent, await PutStatusAsync(nursery.Owner, $"/api/locations/{site}/outline", new { points = Greenhouse }));

        var outline = Assert.Single((await MapAsync(nursery.Owner)).GetProperty("outlines").EnumerateArray());
        Assert.Equal(site, outline.GetProperty("id").GetGuid());
        Assert.Equal(53.0823411, outline.GetProperty("points")[0][0].GetDouble());
        Assert.Equal(4, outline.GetProperty("points").GetArrayLength());

        Assert.Equal(HttpStatusCode.NoContent, await PutStatusAsync(nursery.Owner, $"/api/locations/{site}/outline", new Dictionary<string, object?>(StringComparer.Ordinal) { ["points"] = null }));
        Assert.Equal(0, (await MapAsync(nursery.Owner)).GetProperty("outlines").GetArrayLength());
    }

    [Theory]
    [InlineData("[[53.0, -1.5], [53.1, -1.5]]")]
    [InlineData("[[53.0, -1.5], [53.0, -1.5], [53.0, -1.5]]")]
    [InlineData("[[95.0, -1.5], [53.1, -1.5], [53.1, -1.4]]")]
    [InlineData("[[53.0], [53.1, -1.5], [53.1, -1.4]]")]
    public async Task Something_that_is_not_a_shape_is_refused(string points)
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var site = await AddPlaceAsync(nursery.Owner);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            await PutStatusAsync(nursery.Owner, $"/api/locations/{site}/outline", new { points = JsonSerializer.Deserialize<double[][]>(points) }));
    }

    [Fact]
    public async Task The_owner_gives_an_imagery_key_and_can_remove_it()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);

        Assert.Equal(JsonValueKind.Null, (await MapAsync(nursery.Owner)).GetProperty("imageryKey").ValueKind);
        Assert.Equal(HttpStatusCode.NoContent, await PutStatusAsync(nursery.Owner, "/api/settings/map", new { imageryKey = " AAPK-test-only " }));
        Assert.Equal("AAPK-test-only", (await MapAsync(nursery.Owner)).GetProperty("imageryKey").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, await PutStatusAsync(nursery.Owner, "/api/settings/map", new { imageryKey = "two words" }));
        Assert.Equal(HttpStatusCode.NoContent, await PutStatusAsync(nursery.Owner, "/api/settings/map", new { imageryKey = "" }));
        Assert.Equal(JsonValueKind.Null, (await MapAsync(nursery.Owner)).GetProperty("imageryKey").ValueKind);
    }

    [Fact]
    public async Task Staff_can_neither_see_nor_change_the_map()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var site = await AddPlaceAsync(nursery.Owner);

        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync(nursery.Staff, "/api/locations/map"));
        Assert.Equal(HttpStatusCode.Forbidden, await PutStatusAsync(nursery.Staff, $"/api/locations/{site}/outline", new { points = Greenhouse }));
        Assert.Equal(HttpStatusCode.Forbidden, await PutStatusAsync(nursery.Staff, "/api/settings/map", new { imageryKey = "AAPK" }));
    }
}
