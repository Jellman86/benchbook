using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using static Hortiplanner.Tests.AccountHarness;

namespace Hortiplanner.Tests;

public sealed class MotherPlantTests(PostgresFixture database)
{
    private static async Task<Guid> AddAsync(HttpClient client, string path, object body) =>
        Json(await PostForTextAsync(client, path, body)).GetProperty("id").GetGuid();

    private static Task<HttpResponseMessage> CuttingsAsync(HttpClient client, Guid plant, Guid? mother, string source = "Cuttings") =>
        PostAsync(client, "/api/batches", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow, plantId = plant, quantity = 24, source, originCountry = "GB", containerKind = "Tray", motherPlantId = mother });

    [Fact]
    public async Task Cuttings_are_numbered_from_their_mother_and_lead_back_to_her()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = await AddAsync(nursery.Owner, "/api/plants", new { commonName = "Lavender", variety = "Hidcote", xylellaHost = false, protectedVariety = false });
        var mother = await AddAsync(nursery.Staff, "/api/mother-plants", new { plantId = plant, name = "By the door" });

        using var first = await CuttingsAsync(nursery.Staff, plant, mother);
        using var second = await CuttingsAsync(nursery.Staff, plant, mother);
        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.Created), (first.StatusCode, second.StatusCode));
        var secondBatch = Json(await second.Content.ReadAsStringAsync(Cancellation)).GetProperty("batchId").GetGuid();

        var details = await nursery.Staff.GetFromJsonAsync<JsonElement>(Route($"/api/mother-plants/{mother}"), Cancellation);
        Assert.Equal([1, 2], details.GetProperty("batches").EnumerateArray().Select(batch => batch.GetProperty("round").GetInt32()).ToArray());

        var trace = await nursery.Owner.GetFromJsonAsync<JsonElement>(Route($"/api/batches/{secondBatch}/trace"), Cancellation);
        Assert.Equal(("By the door", 2), (trace.GetProperty("mother").GetProperty("name").GetString(), trace.GetProperty("mother").GetProperty("round").GetInt32()));

        var listed = Assert.Single((await nursery.Staff.GetFromJsonAsync<JsonElement>(Route($"/api/mother-plants?plantId={plant}"), Cancellation)).EnumerateArray());
        Assert.Equal(2, listed.GetProperty("cuttingsTaken").GetInt32());

        // Cuttings from a mother stay hers.
        using var edit = await nursery.Staff.PutAsJsonAsync(Route($"/api/batches/{secondBatch}"), new { plantId = plant, source = "SeedLot", originCountry = "GB" }, Cancellation);
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
    }

    [Fact]
    public async Task Only_cuttings_of_the_same_plant_from_a_mother_still_in_use_are_taken()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var plant = await AddAsync(nursery.Owner, "/api/plants", new { commonName = "Rosemary", xylellaHost = false, protectedVariety = false });
        var other = await AddAsync(nursery.Owner, "/api/plants", new { commonName = "Sage", xylellaHost = false, protectedVariety = false });
        var mother = await AddAsync(nursery.Staff, "/api/mother-plants", new { plantId = plant, name = "Old one" });

        using var duplicate = await PostAsync(nursery.Staff, "/api/mother-plants", new { plantId = plant, name = "old ONE" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        using var seed = await CuttingsAsync(nursery.Staff, plant, mother, source: "SeedLot");
        using var wrongPlant = await CuttingsAsync(nursery.Staff, other, mother);
        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (seed.StatusCode, wrongPlant.StatusCode));

        Assert.Equal(HttpStatusCode.Forbidden, await PostStatusAsync(nursery.Staff, $"/api/mother-plants/{mother}/archive"));
        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(nursery.Owner, $"/api/mother-plants/{mother}/archive"));
        using var archived = await CuttingsAsync(nursery.Staff, plant, mother);
        Assert.Equal(HttpStatusCode.BadRequest, archived.StatusCode);
    }
}
