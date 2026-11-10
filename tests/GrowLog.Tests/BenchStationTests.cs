using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using static GrowLog.Tests.AccountHarness;

namespace GrowLog.Tests;

public sealed class BenchStationTests(PostgresFixture database)
{
    private const string Pin = "4821";
    private const string WrongPin = "0000";

    /// <summary>A system with an owner, one member of staff and a registered bench station.</summary>
    private sealed class Nursery : IAsyncDisposable
    {
        private readonly TestAuthenticator ownerKey = new();

        private Nursery(TestApp app)
        {
            App = app;
            Owner = Client(app);
            Bench = Client(app);
        }

        public TestApp App { get; }

        public HttpClient Owner { get; }

        public HttpClient Bench { get; }

        public Guid StaffId { get; private set; }

        public TestAuthenticator OwnerKey => ownerKey;

        public static async Task<Nursery> CreateAsync(PostgresFixture database, TimeProvider? clock = null)
        {
            var nursery = new Nursery(await CreateAppAsync(database, clock: clock));
            await SetUpOwnerAsync(nursery.Owner, nursery.ownerKey);
            var created = Json(await PostForTextAsync(nursery.Owner, "/api/staff", new { name = "Alex Moss", pin = Pin }));
            nursery.StaffId = created.GetProperty("id").GetGuid();

            Assert.Equal(HttpStatusCode.NoContent, await SignInAsync(nursery.Bench, nursery.ownerKey));
            Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(nursery.Bench, "/api/bench/stations", new { name = "Potting shed" }));
            return nursery;
        }

        public Task<HttpStatusCode> PinSignInAsync(string pin) =>
            PostStatusAsync(Bench, "/api/bench/sign-in", new { userId = StaffId, pin });

        public async ValueTask DisposeAsync()
        {
            Owner.Dispose();
            Bench.Dispose();
            ownerKey.Dispose();
            await App.DisposeAsync();
        }
    }

    [Fact]
    public async Task Registering_a_station_ends_the_owners_session_on_that_device()
    {
        await using var nursery = await Nursery.CreateAsync(database);

        Assert.Equal(HttpStatusCode.Unauthorized, await MeStatusAsync(nursery.Bench));
        Assert.Equal(HttpStatusCode.OK, await MeStatusAsync(nursery.Owner));
    }

    [Fact]
    public async Task A_station_lists_people_who_have_a_pin()
    {
        await using var nursery = await Nursery.CreateAsync(database);

        var people = await nursery.Bench.GetFromJsonAsync<JsonElement>(Route("/api/bench/people"), Cancellation);

        var person = Assert.Single(people.EnumerateArray());
        Assert.Equal("Alex Moss", person.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Tapping_a_name_and_entering_the_pin_signs_that_person_in()
    {
        await using var nursery = await Nursery.CreateAsync(database);

        Assert.Equal(HttpStatusCode.NoContent, await nursery.PinSignInAsync(Pin));

        var me = await nursery.Bench.GetFromJsonAsync<JsonElement>(Route("/api/account/me"), Cancellation);
        Assert.Equal("Alex Moss", me.GetProperty("name").GetString());
        Assert.False(me.GetProperty("isOwner").GetBoolean());
    }

    [Fact]
    public async Task Signing_out_returns_to_the_name_list_without_losing_the_station()
    {
        await using var nursery = await Nursery.CreateAsync(database);
        await nursery.PinSignInAsync(Pin);

        using var signOut = await PostAsync(nursery.Bench, "/api/account/sign-out");

        Assert.Equal(HttpStatusCode.Unauthorized, await MeStatusAsync(nursery.Bench));
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(nursery.Bench, "/api/bench/people"));
    }

    [Fact]
    public async Task A_wrong_pin_is_refused()
    {
        await using var nursery = await Nursery.CreateAsync(database);

        Assert.Equal(HttpStatusCode.Unauthorized, await nursery.PinSignInAsync(WrongPin));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeStatusAsync(nursery.Bench));
    }

    [Fact]
    public async Task Repeated_wrong_pins_lock_the_person_out_even_with_the_right_pin()
    {
        await using var nursery = await Nursery.CreateAsync(database);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, await nursery.PinSignInAsync(WrongPin));
        }

        Assert.Equal(HttpStatusCode.Locked, await nursery.PinSignInAsync(WrongPin));
        Assert.Equal(HttpStatusCode.Locked, await nursery.PinSignInAsync(Pin));
    }

    [Fact]
    public async Task A_right_pin_clears_earlier_wrong_guesses()
    {
        await using var nursery = await Nursery.CreateAsync(database);

        for (var round = 0; round < 3; round++)
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                await nursery.PinSignInAsync(WrongPin);
            }

            Assert.Equal(HttpStatusCode.NoContent, await nursery.PinSignInAsync(Pin));
        }
    }

    [Fact]
    public async Task The_owner_setting_a_new_pin_lifts_a_lockout()
    {
        await using var nursery = await Nursery.CreateAsync(database);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await nursery.PinSignInAsync(WrongPin);
        }

        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(nursery.Owner, $"/api/staff/{nursery.StaffId}/pin", new { pin = "7310" }));

        Assert.Equal(HttpStatusCode.NoContent, await nursery.PinSignInAsync("7310"));
    }

    [Fact]
    public async Task A_device_that_is_not_a_station_cannot_use_a_pin()
    {
        await using var nursery = await Nursery.CreateAsync(database);
        using var stranger = Client(nursery.App);

        Assert.Equal(HttpStatusCode.Unauthorized, await PostStatusAsync(stranger, "/api/bench/sign-in", new { userId = nursery.StaffId, pin = Pin }));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(stranger, "/api/bench/people"));
    }

    [Fact]
    public async Task A_signed_in_person_on_a_phone_is_not_a_station()
    {
        await using var nursery = await Nursery.CreateAsync(database);

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(nursery.Owner, "/api/bench/people"));
    }

    [Fact]
    public async Task A_station_cannot_do_anything_a_person_would_need_to_sign_in_for()
    {
        await using var nursery = await Nursery.CreateAsync(database);

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(nursery.Bench, "/api/staff"));
    }

    [Fact]
    public async Task A_member_of_staff_on_a_station_cannot_register_or_revoke_stations()
    {
        await using var nursery = await Nursery.CreateAsync(database);
        await nursery.PinSignInAsync(Pin);

        Assert.Equal(HttpStatusCode.Forbidden, await PostStatusAsync(nursery.Bench, "/api/bench/stations", new { name = "Mine" }));
    }

    [Fact]
    public async Task A_revoked_station_can_no_longer_be_used()
    {
        await using var nursery = await Nursery.CreateAsync(database);
        var stations = await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/bench/stations"), Cancellation);
        var id = stations.EnumerateArray().Single().GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(nursery.Owner, $"/api/bench/stations/{id}/revoke"));

        Assert.Equal(HttpStatusCode.Unauthorized, await nursery.PinSignInAsync(Pin));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(nursery.Bench, "/api/bench/people"));
    }

    [Fact]
    public async Task Someone_who_has_left_is_not_listed_and_cannot_use_their_pin()
    {
        await using var nursery = await Nursery.CreateAsync(database);

        await PostStatusAsync(nursery.Owner, $"/api/staff/{nursery.StaffId}/archive");

        var people = await nursery.Bench.GetFromJsonAsync<JsonElement>(Route("/api/bench/people"), Cancellation);
        Assert.Empty(people.EnumerateArray());
        Assert.Equal(HttpStatusCode.Locked, await nursery.PinSignInAsync(Pin));
    }

    [Fact]
    public async Task A_station_returns_to_the_name_list_after_being_left_idle()
    {
        var clock = new TestClock();
        await using var nursery = await Nursery.CreateAsync(database, clock);
        await nursery.PinSignInAsync(Pin);
        Assert.Equal(HttpStatusCode.OK, await MeStatusAsync(nursery.Bench));

        clock.Advance(TimeSpan.FromMinutes(6));

        Assert.Equal(HttpStatusCode.Unauthorized, await MeStatusAsync(nursery.Bench));
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(nursery.Bench, "/api/bench/people"));
    }

    [Fact]
    public async Task A_station_stays_signed_in_while_someone_keeps_working_but_still_times_out_afterwards()
    {
        var clock = new TestClock();
        await using var nursery = await Nursery.CreateAsync(database, clock);
        await nursery.PinSignInAsync(Pin);

        // Long enough to pass the 30-minute point where sessions are checked against the account.
        for (var round = 0; round < 12; round++)
        {
            clock.Advance(TimeSpan.FromMinutes(4));
            Assert.Equal(HttpStatusCode.OK, await MeStatusAsync(nursery.Bench));
        }

        clock.Advance(TimeSpan.FromMinutes(6));

        Assert.Equal(HttpStatusCode.Unauthorized, await MeStatusAsync(nursery.Bench));
    }

    [Fact]
    public async Task The_owner_signed_in_at_a_bench_can_record_work_but_not_manage()
    {
        await using var nursery = await Nursery.CreateAsync(database);
        var ownerId = (await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/account/me"), Cancellation)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(nursery.Owner, $"/api/staff/{ownerId}/pin", new { pin = "9876" }));

        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(nursery.Bench, "/api/bench/sign-in", new { userId = ownerId, pin = "9876" }));

        var me = await nursery.Bench.GetFromJsonAsync<JsonElement>(Route("/api/account/me"), Cancellation);
        Assert.False(me.GetProperty("isOwner").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync(nursery.Bench, "/api/staff"));
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(nursery.Bench, "/api/containers/active"));
    }

    [Fact]
    public async Task A_station_cannot_be_made_before_anyone_has_a_pin()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var ownerKey = new TestAuthenticator();
        await SetUpOwnerAsync(owner, ownerKey);

        Assert.Equal(HttpStatusCode.Conflict, await PostStatusAsync(owner, "/api/bench/stations", new { name = "Potting shed" }));
        Assert.Equal(HttpStatusCode.OK, await MeStatusAsync(owner));
    }

    [Fact]
    public async Task A_device_can_stop_being_a_bench_station()
    {
        await using var nursery = await Nursery.CreateAsync(database);

        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(nursery.Bench, "/api/bench/leave"));

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(nursery.Bench, "/api/bench/people"));
    }
}
