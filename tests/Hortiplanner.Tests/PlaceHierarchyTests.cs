using System.Net;
using System.Net.Http.Json;
using Xunit;
using static Hortiplanner.Tests.AccountHarness;

namespace Hortiplanner.Tests;

public sealed class PlaceHierarchyTests(PostgresFixture database)
{
    private static async Task<Guid> AddAsync(HttpClient owner, string name, Guid? parent = null) =>
        Json(await PostForTextAsync(owner, "/api/locations", new { name, kind = parent is null ? "Site" : "Bench", parentId = parent })).GetProperty("id").GetGuid();

    [Fact]
    public async Task Concurrent_moves_cannot_put_two_places_inside_each_other()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var site = await AddAsync(nursery.Owner, "Nursery");
        var first = await AddAsync(nursery.Owner, "First", site);
        var second = await AddAsync(nursery.Owner, "Second", site);
        var replies = await Task.WhenAll(
            nursery.Owner.PutAsJsonAsync(Route($"/api/locations/{first}/parent"), new { parentId = second }, Cancellation),
            nursery.Owner.PutAsJsonAsync(Route($"/api/locations/{second}/parent"), new { parentId = first }, Cancellation));
        Assert.Equal(1, replies.Count(reply => reply.StatusCode == HttpStatusCode.NoContent));
        Assert.Equal(1, replies.Count(reply => reply.StatusCode == HttpStatusCode.BadRequest));
        foreach (var reply in replies)
        {
            reply.Dispose();
        }
    }

    [Fact]
    public async Task Cycle_detection_follows_the_whole_chain_beyond_twelve_places()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var site = await AddAsync(nursery.Owner, "Nursery");
        var first = await AddAsync(nursery.Owner, "First", site);
        var last = first;
        for (var index = 0; index < 14; index++)
        {
            last = await AddAsync(nursery.Owner, $"Nested {index}", last);
        }
        using var reply = await nursery.Owner.PutAsJsonAsync(Route($"/api/locations/{first}/parent"), new { parentId = last }, Cancellation);
        Assert.Equal(HttpStatusCode.BadRequest, reply.StatusCode);
    }
}
