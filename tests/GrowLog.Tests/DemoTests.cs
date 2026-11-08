using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrowLog.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GrowLog.Tests.AccountHarness;

namespace GrowLog.Tests;

public sealed class DemoTests(PostgresFixture database)
{
    private static Task<JsonElement> GetAsync(HttpClient client, string path) =>
        client.GetFromJsonAsync<JsonElement>(Route(path), Cancellation);

    private static async Task<bool> AvailableAsync(HttpClient owner) =>
        (await GetAsync(owner, "/api/demo")).GetProperty("available").GetBoolean();

    [Fact]
    public async Task Example_data_is_off_unless_it_is_switched_on()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);

        Assert.False(await AvailableAsync(nursery.Owner));
        Assert.Equal(HttpStatusCode.NotFound, await PostStatusAsync(nursery.Owner, "/api/demo/load"));
        Assert.Equal(0, (await GetAsync(nursery.Owner, "/api/plants")).GetArrayLength());
    }

    [Fact]
    public async Task Loading_fills_every_part_of_the_app_and_then_it_cannot_be_loaded_again()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database, demo: true);
        Assert.True(await AvailableAsync(nursery.Owner));

        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(nursery.Owner, "/api/demo/load"));

        Assert.False(await AvailableAsync(nursery.Owner));
        Assert.Equal(8, (await GetAsync(nursery.Owner, "/api/plants")).GetArrayLength());
        Assert.Equal(3, (await GetAsync(nursery.Owner, "/api/suppliers")).GetArrayLength());
        Assert.Equal(4, (await GetAsync(nursery.Owner, "/api/pot-sizes")).GetArrayLength());
        Assert.Equal(5, (await GetAsync(nursery.Owner, "/api/purchases")).GetArrayLength());
        Assert.Equal(12, (await GetAsync(nursery.Owner, "/api/containers/active")).GetArrayLength());
        Assert.True((await GetAsync(nursery.Owner, "/api/events/recent")).GetArrayLength() > 0);

        var figures = await GetAsync(nursery.Owner, "/api/figures");
        Assert.Equal(8, figures.GetProperty("plants").GetArrayLength());
        Assert.Equal(6, figures.GetProperty("losses").GetArrayLength());

        Assert.Equal(HttpStatusCode.Conflict, await PostStatusAsync(nursery.Owner, "/api/demo/load"));
    }

    [Fact]
    public async Task Example_people_are_listed_but_have_no_way_to_sign_in()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database, demo: true);
        await PostStatusAsync(nursery.Owner, "/api/demo/load");

        var staff = await GetAsync(nursery.Owner, "/api/staff");
        Assert.Contains(staff.EnumerateArray(), s => string.Equals(s.GetProperty("name").GetString(), "Example Worker", StringComparison.Ordinal));

        await using var scope = nursery.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GrowLogDbContext>();
        var worker = await db.Users.SingleAsync(u => u.UserName == "Example Worker", Cancellation);
        Assert.Null(worker.PinHash);
        Assert.Null(worker.PasswordHash);
    }

    [Fact]
    public async Task Example_data_will_not_load_over_a_catalogue_already_started()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database, demo: true);
        await PostForTextAsync(nursery.Owner, "/api/plants", new { commonName = "Real plant", xylellaHost = false, protectedVariety = false });

        Assert.Equal(HttpStatusCode.Conflict, await PostStatusAsync(nursery.Owner, "/api/demo/load"));
    }

    [Fact]
    public async Task Only_the_owner_can_load_example_data()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database, demo: true);

        Assert.Equal(HttpStatusCode.Forbidden, await PostStatusAsync(nursery.Staff, "/api/demo/load"));
        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync(nursery.Staff, "/api/demo"));
    }
}
