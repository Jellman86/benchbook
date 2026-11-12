using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using static GrowLog.Tests.AccountHarness;

namespace GrowLog.Tests;

public sealed class SensorTests(PostgresFixture database)
{
    private static async Task<HttpStatusCode> SendAsync(HttpClient client, string? key, object reading)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, Route("/api/sensor-readings")) { Content = JsonContent.Create(reading) };

        if (key is not null)
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

        using var response = await client.SendAsync(message, Cancellation);
        return response.StatusCode;
    }

    [Fact]
    public async Task A_sensor_sends_readings_with_its_key_and_each_place_shows_its_climate()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var site = Json(await PostForTextAsync(nursery.Owner, "/api/locations", new { name = "Croft", kind = "Site" })).GetProperty("id").GetGuid();
        var greenhouse = Json(await PostForTextAsync(nursery.Owner, "/api/locations", new { name = "Greenhouse 1", kind = "Greenhouse", parentId = site })).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Forbidden, await PostStatusAsync(nursery.Staff, "/api/sensors", new { name = "Greenhouse sensor", locationId = greenhouse }));
        var added = Json(await PostForTextAsync(nursery.Owner, "/api/sensors", new { name = "Greenhouse sensor", locationId = greenhouse }));
        var key = added.GetProperty("key").GetString()!;
        Assert.StartsWith("glk_", key, StringComparison.Ordinal);

        using var anyone = Client(nursery.App);
        Assert.Equal(HttpStatusCode.Unauthorized, await SendAsync(anyone, null, new { temperature = 21.5 }));
        Assert.Equal(HttpStatusCode.Unauthorized, await SendAsync(anyone, "glk_wrong", new { temperature = 21.5 }));
        Assert.Equal(HttpStatusCode.BadRequest, await SendAsync(anyone, key, new { temperature = 140 }));
        Assert.Equal(HttpStatusCode.BadRequest, await SendAsync(anyone, key, new { }));
        Assert.Equal(HttpStatusCode.NoContent, await SendAsync(anyone, key, new { temperature = 12.25, humidity = 80, takenAt = DateTimeOffset.UtcNow.AddMinutes(-20) }));
        Assert.Equal(HttpStatusCode.NoContent, await SendAsync(anyone, key, new { temperature = 18.5, humidity = 70 }));
        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAsync(anyone, key, new { temperature = 18.6 }));

        var place = Assert.Single((await nursery.Staff.GetFromJsonAsync<JsonElement>(Route("/api/climate"), Cancellation)).EnumerateArray());
        Assert.Equal("Greenhouse 1", place.GetProperty("place").GetString());
        Assert.Equal(18.5m, place.GetProperty("latest").GetProperty("temperature").GetDecimal());

        var history = await nursery.Staff.GetFromJsonAsync<JsonElement>(Route($"/api/climate/{greenhouse}?days=2"), Cancellation);
        var hours = history.GetProperty("hours").EnumerateArray().ToList();
        Assert.InRange(hours.Count, 1, 2);
        Assert.Equal(18.5m, hours.Max(hour => hour.GetProperty("highestTemperature").GetDecimal()));

        // Two sent at the same moment: only one gets in.
        var at = DateTimeOffset.UtcNow.AddMinutes(-40);
        var both = await Task.WhenAll(SendAsync(anyone, key, new { temperature = 15, takenAt = at }), SendAsync(anyone, key, new { temperature = 15, takenAt = at }));
        Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.TooManyRequests], both.Order());

        // A new key stops the old one working.
        var newKey = Json(await PostForTextAsync(nursery.Owner, $"/api/sensors/{added.GetProperty("id").GetGuid()}/new-key", new { })).GetProperty("key").GetString()!;
        Assert.Equal(HttpStatusCode.Unauthorized, await SendAsync(anyone, key, new { temperature = 19, takenAt = DateTimeOffset.UtcNow.AddMinutes(-10) }));
        Assert.Equal(HttpStatusCode.NoContent, await SendAsync(anyone, newKey, new { temperature = 19, takenAt = DateTimeOffset.UtcNow.AddMinutes(-10) }));

        // A stopped sensor's name can be used again, but not a working one's.
        Assert.Equal(HttpStatusCode.Conflict, await PostStatusAsync(nursery.Owner, "/api/sensors", new { name = "Greenhouse sensor", locationId = greenhouse }));
        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(nursery.Owner, $"/api/sensors/{added.GetProperty("id").GetGuid()}/stop"));
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(nursery.Owner, "/api/sensors", new { name = "Greenhouse sensor", locationId = greenhouse }));
    }
}
