using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

    private static async Task<JsonElement> PlaceAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<JsonElement>(Route("/api/locations"), Cancellation)).EnumerateArray().Single(place => place.GetProperty("id").GetGuid() == id);

    [Fact]
    public async Task A_place_added_as_the_wrong_sort_can_be_corrected_keeping_its_label_and_whats_inside()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var site = await AddAsync(nursery.Owner, "Nursery");
        var house = await AddAsync(nursery.Owner, "Greenhouse 2", site);
        var bench = await AddAsync(nursery.Owner, "Bench A", house);
        var label = (await PlaceAsync(nursery.Owner, house)).GetProperty("labelCode").GetString();

        using (var staffTries = await nursery.Staff.PutAsJsonAsync(Route($"/api/locations/{house}/kind"), new { kind = "Greenhouse" }, Cancellation))
        {
            Assert.Equal(HttpStatusCode.Forbidden, staffTries.StatusCode);
        }

        using (var corrected = await nursery.Owner.PutAsJsonAsync(Route($"/api/locations/{house}/kind"), new { kind = "Greenhouse" }, Cancellation))
        {
            Assert.Equal(HttpStatusCode.NoContent, corrected.StatusCode);
        }

        var now = await PlaceAsync(nursery.Owner, house);
        Assert.Equal("Greenhouse", now.GetProperty("kind").GetString());
        Assert.Equal(label, now.GetProperty("labelCode").GetString());
        Assert.Equal(site, now.GetProperty("parentId").GetGuid());
        Assert.Equal(house, (await PlaceAsync(nursery.Owner, bench)).GetProperty("parentId").GetGuid());

        // Becoming a site lifts it out on its own; a site becoming anything else needs somewhere to sit.
        using (var toSite = await nursery.Owner.PutAsJsonAsync(Route($"/api/locations/{house}/kind"), new { kind = "Site" }, Cancellation))
        {
            Assert.Equal(HttpStatusCode.NoContent, toSite.StatusCode);
        }

        Assert.Equal(JsonValueKind.Null, (await PlaceAsync(nursery.Owner, house)).GetProperty("parentId").ValueKind);

        using (var nowhere = await nursery.Owner.PutAsJsonAsync(Route($"/api/locations/{house}/kind"), new { kind = "Greenhouse" }, Cancellation))
        {
            Assert.Equal(HttpStatusCode.BadRequest, nowhere.StatusCode);
        }

        using (var insideItself = await nursery.Owner.PutAsJsonAsync(Route($"/api/locations/{house}/kind"), new { kind = "Greenhouse", parentId = bench }, Cancellation))
        {
            Assert.Equal(HttpStatusCode.BadRequest, insideItself.StatusCode);
        }

        using (var back = await nursery.Owner.PutAsJsonAsync(Route($"/api/locations/{house}/kind"), new { kind = "Greenhouse", parentId = site }, Cancellation))
        {
            Assert.Equal(HttpStatusCode.NoContent, back.StatusCode);
        }

        Assert.Equal(site, (await PlaceAsync(nursery.Owner, house)).GetProperty("parentId").GetGuid());
    }

    [Fact]
    public async Task Only_a_place_nothing_has_used_can_be_deleted()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var site = await AddAsync(nursery.Owner, "Nursery");
        var mistake = await AddAsync(nursery.Owner, "Bench added by mistake", site);
        var used = await AddAsync(nursery.Owner, "Bench with a tray", site);
        var plant = Json(await PostForTextAsync(nursery.Owner, "/api/plants", new { commonName = "Lavender", xylellaHost = false, protectedVariety = false })).GetProperty("id").GetGuid();
        await PostForTextAsync(nursery.Owner, "/api/batches", new
        {
            eventId = Guid.CreateVersion7(),
            occurredAt = DateTimeOffset.UtcNow,
            plantId = plant,
            quantity = 10,
            source = "SeedLot",
            originCountry = "GB",
            containerKind = "Tray",
            locationId = used,
        });

        using (var staffTries = await nursery.Staff.DeleteAsync(Route($"/api/locations/{mistake}"), Cancellation))
        {
            Assert.Equal(HttpStatusCode.Forbidden, staffTries.StatusCode);
        }

        using (var hasWork = await nursery.Owner.DeleteAsync(Route($"/api/locations/{used}"), Cancellation))
        {
            Assert.Equal(HttpStatusCode.Conflict, hasWork.StatusCode);
            Assert.Equal("HasWork", (await hasWork.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("problem").GetString());
        }

        using (var hasPlaces = await nursery.Owner.DeleteAsync(Route($"/api/locations/{site}"), Cancellation))
        {
            Assert.Equal(HttpStatusCode.Conflict, hasPlaces.StatusCode);
            Assert.Equal("HasPlaces", (await hasPlaces.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("problem").GetString());
        }

        using (var deleted = await nursery.Owner.DeleteAsync(Route($"/api/locations/{mistake}"), Cancellation))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        Assert.DoesNotContain((await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/locations?includeArchived=true"), Cancellation)).EnumerateArray(), place => place.GetProperty("id").GetGuid() == mistake);
    }

    [Fact]
    public async Task A_place_with_trays_in_it_cannot_become_a_site_and_a_left_place_still_blocks_deleting()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var site = await AddAsync(nursery.Owner, "Nursery");
        var bench = await AddAsync(nursery.Owner, "Bench with a tray", site);
        var empty = await AddAsync(nursery.Owner, "Empty bench", site);
        var gone = await AddAsync(nursery.Owner, "Bay no longer used", empty);
        var plant = Json(await PostForTextAsync(nursery.Owner, "/api/plants", new { commonName = "Sage", xylellaHost = false, protectedVariety = false })).GetProperty("id").GetGuid();
        await PostForTextAsync(nursery.Owner, "/api/batches", new
        {
            eventId = Guid.CreateVersion7(),
            occurredAt = DateTimeOffset.UtcNow,
            plantId = plant,
            quantity = 10,
            source = "SeedLot",
            originCountry = "GB",
            containerKind = "Tray",
            locationId = bench,
        });

        using (var toSite = await nursery.Owner.PutAsJsonAsync(Route($"/api/locations/{bench}/kind"), new { kind = "Site" }, Cancellation))
        {
            Assert.Equal(HttpStatusCode.Conflict, toSite.StatusCode);
            Assert.Equal("HasTrays", (await toSite.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("problem").GetString());
        }

        using (var retired = await nursery.Owner.PostAsync(Route($"/api/locations/{gone}/archive"), null, Cancellation))
        {
            Assert.Equal(HttpStatusCode.NoContent, retired.StatusCode);
        }

        using (var blocked = await nursery.Owner.DeleteAsync(Route($"/api/locations/{empty}"), Cancellation))
        {
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Equal("HasPlaces", (await blocked.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("problem").GetString());
        }

        using (var deleteChild = await nursery.Owner.DeleteAsync(Route($"/api/locations/{gone}"), Cancellation))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleteChild.StatusCode);
        }

        using (var deleteParent = await nursery.Owner.DeleteAsync(Route($"/api/locations/{empty}"), Cancellation))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleteParent.StatusCode);
        }

        using (var missing = await nursery.Owner.DeleteAsync(Route($"/api/locations/{empty}"), Cancellation))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
    }
}
