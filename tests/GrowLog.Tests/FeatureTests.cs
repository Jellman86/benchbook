using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using static GrowLog.Tests.AccountHarness;

namespace GrowLog.Tests;

public sealed class FeatureTests(PostgresFixture database)
{
    private static async Task<Dictionary<string, bool>> FeaturesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>(Route("/api/settings/features"), Cancellation))
            .EnumerateArray()
            .ToDictionary(item => item.GetProperty("feature").GetString()!, item => item.GetProperty("on").GetBoolean(), StringComparer.Ordinal);

    private static async Task<HttpStatusCode> SetAsync(HttpClient client, string feature, bool on)
    {
        using var response = await client.PutAsJsonAsync(Route($"/api/settings/features/{feature}"), new { on }, Cancellation);
        return response.StatusCode;
    }

    [Fact]
    public async Task Everything_is_on_until_the_owner_turns_it_off_and_only_the_owner_can()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);

        var features = await FeaturesAsync(nursery.Staff);
        Assert.Equal(8, features.Count);
        Assert.All(features.Values, Assert.True);

        Assert.Equal(HttpStatusCode.Forbidden, await SetAsync(nursery.Staff, "Climate", on: false));
        Assert.Equal(HttpStatusCode.NoContent, await SetAsync(nursery.Owner, "Climate", on: false));
        Assert.Equal(HttpStatusCode.NoContent, await SetAsync(nursery.Owner, "Climate", on: false));

        features = await FeaturesAsync(nursery.Staff);
        Assert.False(features["Climate"]);
        Assert.True(features["Jobs"]);
    }

    [Fact]
    public async Task A_part_turned_off_is_refused_everywhere_and_comes_back_when_turned_on()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var site = Json(await PostForTextAsync(nursery.Owner, "/api/locations", new { name = "Croft", kind = "Site" })).GetProperty("id").GetGuid();
        var key = Json(await PostForTextAsync(nursery.Owner, "/api/sensors", new { name = "Sensor", locationId = site })).GetProperty("key").GetString()!;

        Assert.Equal(HttpStatusCode.NoContent, await SetAsync(nursery.Owner, "Climate", on: false));

        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(nursery.Owner, "/api/climate"));
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(nursery.Owner, "/api/sensors"));

        using var anyone = Client(nursery.App);
        using (var reading = new HttpRequestMessage(HttpMethod.Post, Route("/api/sensor-readings")) { Content = JsonContent.Create(new { temperature = 12 }) })
        {
            reading.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var refused = await anyone.SendAsync(reading, Cancellation);
            Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        }

        // Other parts carry on as before.
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(nursery.Owner, "/api/jobs"));

        Assert.Equal(HttpStatusCode.NoContent, await SetAsync(nursery.Owner, "Climate", on: true));
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(nursery.Owner, "/api/climate"));
        Assert.Single((await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/sensors"), Cancellation)).EnumerateArray());
    }

    [Theory]
    [InlineData("PlacesMap", "/api/locations/map")]
    [InlineData("Passports", "/api/consignments")]
    [InlineData("MotherPlants", "/api/mother-plants")]
    [InlineData("Jobs", "/api/jobs")]
    [InlineData("Invoices", "/api/invoices")]
    [InlineData("Purchases", "/api/purchases")]
    public async Task Each_part_refuses_its_requests_while_off(string feature, string path)
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(nursery.Owner, path));
        Assert.Equal(HttpStatusCode.NoContent, await SetAsync(nursery.Owner, feature, on: false));
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(nursery.Owner, path));
    }

    [Fact]
    public async Task Late_jobs_are_not_raised_while_jobs_are_off()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var due = DateOnly.FromDateTime(DateTime.Today).AddDays(-10);
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(nursery.Owner, "/api/jobs", new { title = "Water the tunnel", everyDays = 7, firstDue = due }));

        async Task<List<string>> KindsAsync() =>
            [.. (await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/alerts"), Cancellation)).EnumerateArray().Select(alert => alert.GetProperty("kind").GetString()!)];

        Assert.Contains("JobsLate", await KindsAsync(), StringComparer.Ordinal);
        Assert.Equal(HttpStatusCode.NoContent, await SetAsync(nursery.Owner, "Jobs", on: false));
        Assert.DoesNotContain("JobsLate", await KindsAsync(), StringComparer.Ordinal);
    }
}
