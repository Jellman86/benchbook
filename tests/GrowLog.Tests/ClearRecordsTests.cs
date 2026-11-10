using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrowLog.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using static GrowLog.Tests.AccountHarness;

namespace GrowLog.Tests;

public sealed class ClearRecordsTests(PostgresFixture database)
{
    private const string Path = "/api/settings/clear-records";

    private static Task<JsonElement> GetAsync(HttpClient client, string path) =>
        client.GetFromJsonAsync<JsonElement>(Route(path), Cancellation);

    private static async Task<int> CountAsync(HttpClient client, string path) =>
        (await GetAsync(client, path)).GetArrayLength();

    private static async Task<List<string?>> LabelsAsync(HttpClient owner) =>
        [.. (await GetAsync(owner, "/api/containers/active")).EnumerateArray().Select(c => c.GetProperty("labelCode").GetString()).Order(StringComparer.Ordinal)];

    private static async Task FillAsync(TwoPersonNursery nursery)
    {
        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(nursery.Owner, "/api/demo/load"));
        using var details = await nursery.Owner.PutAsJsonAsync(Route("/api/settings/nursery"), new { name = "Croft View", address = "Croft Lane", phone = "", email = "", website = "", registrationNumber = "", vatNumber = (string?)null, hasLogo = false, vatRegistered = false, paymentTermsDays = 14 }, Cancellation);
        details.EnsureSuccessStatusCode();
        var customer = Json(await PostForTextAsync(nursery.Owner, "/api/customers", new { name = "Hilltop", address = "", email = "", phone = "", vatNumber = "" })).GetProperty("id").GetGuid();
        await PostForTextAsync(nursery.Owner, "/api/invoices", new { customerId = customer, notes = "", lines = new[] { new { description = "Compost", quantity = 1, unitPrice = 5m, vatRate = 0m } } });
    }

    [Fact]
    public async Task Clearing_empties_the_records_and_keeps_the_people_and_the_nursery_details()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database, demo: true);
        await FillAsync(nursery);
        var staffBefore = await CountAsync(nursery.Owner, "/api/staff");

        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(nursery.Owner, Path, new { confirmation = "Clear everything" }));

        foreach (var path in new[] { "/api/plants", "/api/suppliers", "/api/pot-sizes", "/api/purchases", "/api/containers/active", "/api/locations", "/api/customers", "/api/invoices", "/api/events/recent" })
        {
            Assert.Equal(0, await CountAsync(nursery.Owner, path));
        }

        Assert.Equal("Croft View", (await GetAsync(nursery.Owner, "/api/settings/nursery")).GetProperty("name").GetString());
        Assert.Equal(staffBefore - 2, await CountAsync(nursery.Owner, "/api/staff"));
        Assert.True((await GetAsync(nursery.Staff, "/api/account/me")).GetProperty("name").GetString() is { Length: > 0 });
        Assert.True((await GetAsync(nursery.Owner, "/api/demo")).GetProperty("available").GetBoolean());
    }

    [Fact]
    public async Task Records_are_append_only_again_afterwards_and_numbering_starts_again()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database, demo: true);
        await FillAsync(nursery);
        var firstLabels = await LabelsAsync(nursery.Owner);
        await PostStatusAsync(nursery.Owner, Path, new { confirmation = "clear everything" });

        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(nursery.Owner, "/api/demo/load"));
        Assert.Equal(firstLabels, await LabelsAsync(nursery.Owner));

        await using var scope = nursery.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GrowLogDbContext>();
        var refused = await Assert.ThrowsAsync<PostgresException>(() => db.Events.ExecuteDeleteAsync(Cancellation));
        Assert.Contains("append-only", refused.MessageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Records_cannot_be_cleared_once_example_data_is_switched_off()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        await PostForTextAsync(nursery.Owner, "/api/plants", new { commonName = "Real plant", xylellaHost = false, protectedVariety = false });

        Assert.False((await GetAsync(nursery.Owner, "/api/demo")).GetProperty("canClear").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, await PostStatusAsync(nursery.Owner, Path, new { confirmation = "clear everything" }));
        Assert.Equal(1, await CountAsync(nursery.Owner, "/api/plants"));
    }

    [Fact]
    public async Task Only_the_owner_can_clear_and_only_after_typing_the_words()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database, demo: true);
        await FillAsync(nursery);

        Assert.Equal(HttpStatusCode.Forbidden, await PostStatusAsync(nursery.Staff, Path, new { confirmation = "clear everything" }));
        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(nursery.Owner, Path, new { confirmation = "yes" }));
        Assert.Equal(8, await CountAsync(nursery.Owner, "/api/plants"));
    }
}
