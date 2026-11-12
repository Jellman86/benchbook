using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GrowLog.Api.Data;
using GrowLog.Api.Passports;
using Xunit;
using static GrowLog.Tests.AccountHarness;

namespace GrowLog.Tests;

public sealed class ConsignmentTests(PostgresFixture database)
{
    [Theory]
    [InlineData("TradeSale", true)]
    [InlineData("DistanceSale", true)]
    [InlineData("InPerson", false)]
    [InlineData("OwnSite", false)]
    public void Trade_and_distance_sales_need_a_passport(string kind, bool needed) =>
        Assert.Equal(needed, ConsignmentEndpoints.PassportNeeded(Enum.Parse<ConsignmentKind>(kind)).Needed);

    private static async Task<Guid> AddAsync(HttpClient client, string path, object body) =>
        Json(await PostForTextAsync(client, path, body)).GetProperty("id").GetGuid();

    private static async Task<(Guid Batch, Guid Tray)> SowAsync(TwoPersonNursery nursery, string name, string botanical)
    {
        var plant = await AddAsync(nursery.Owner, "/api/plants", new { commonName = name, botanicalName = botanical, xylellaHost = false, protectedVariety = false });
        var started = Json(await PostForTextAsync(nursery.Staff, "/api/batches", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow, plantId = plant, quantity = 40, source = "SeedLot", originCountry = "GB", containerKind = "Tray" }));
        return (started.GetProperty("batchId").GetGuid(), started.GetProperty("containerId").GetGuid());
    }

    private static object Line(Guid tray, Guid batch, int quantity) => new { containerId = tray, batchId = batch, quantity };

    [Fact]
    public async Task Each_package_gets_one_passport_covering_every_batch_in_it()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        using var settings = await nursery.Owner.PutAsJsonAsync(Route("/api/settings/nursery"), new { name = "Croft View", registrationNumber = "123456", hasLogo = false, vatRegistered = false, paymentTermsDays = 30 }, Cancellation);
        settings.EnsureSuccessStatusCode();
        var (lavender, lavenderTray) = await SowAsync(nursery, "Lavender", "Lavandula angustifolia");
        var (sage, sageTray) = await SowAsync(nursery, "Sage", "Salvia officinalis");

        using var tooMany = await PostAsync(nursery.Owner, "/api/consignments", new { kind = "TradeSale", sentTo = "Hilltop", packages = new[] { new { lines = new[] { Line(lavenderTray, lavender, 41) } } } });
        Assert.Equal(HttpStatusCode.Conflict, tooMany.StatusCode);

        var created = Json(await PostForTextAsync(nursery.Owner, "/api/consignments", new
        {
            kind = "TradeSale",
            sentTo = "Hilltop Garden Centre",
            packages = new[] { new { lines = new[] { Line(lavenderTray, lavender, 10), Line(sageTray, sage, 5) } }, new { lines = new[] { Line(sageTray, sage, 5) } } },
        }));
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal(1, created.GetProperty("number").GetInt32());

        var details = await nursery.Owner.GetFromJsonAsync<JsonElement>(Route($"/api/consignments/{id}"), Cancellation);
        Assert.True(details.GetProperty("passportNeeded").GetBoolean());
        var package = details.GetProperty("packages")[0].GetProperty("id").GetGuid();

        using var unexamined = await PostAsync(nursery.Owner, $"/api/consignments/{id}/packages/{package}/passport", new { examined = false });
        Assert.Equal(HttpStatusCode.Conflict, unexamined.StatusCode);
        Assert.Equal(HttpStatusCode.Created, await PostStatusAsync(nursery.Owner, $"/api/consignments/{id}/packages/{package}/passport", new { examined = true }));
        Assert.Equal(HttpStatusCode.OK, await PostStatusAsync(nursery.Owner, $"/api/consignments/{id}/packages/{package}/passport", new { examined = true }));

        var passport = (await nursery.Owner.GetFromJsonAsync<JsonElement>(Route($"/api/consignments/{id}"), Cancellation)).GetProperty("packages")[0].GetProperty("passport");
        Assert.Equal("Lavandula angustifolia; Salvia officinalis", passport.GetProperty("partA").GetString());
        Assert.Equal(2, passport.GetProperty("partC").GetString()!.Split(", ").Length);
        Assert.Equal("123456", passport.GetProperty("partB").GetString());

        using var label = await nursery.Owner.GetAsync(Route($"/api/labels/passports/{package}"), Cancellation);
        Assert.Equal(HttpStatusCode.OK, label.StatusCode);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(await label.Content.ReadAsByteArrayAsync(Cancellation), 0, 4), StringComparison.Ordinal);

        var trace = await nursery.Owner.GetFromJsonAsync<JsonElement>(Route($"/api/batches/{lavender}/trace"), Cancellation);
        Assert.Equal("Hilltop Garden Centre", Assert.Single(trace.GetProperty("issuedPassports").EnumerateArray()).GetProperty("issuedTo").GetString());

        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync(nursery.Staff, "/api/consignments"));
    }
}
