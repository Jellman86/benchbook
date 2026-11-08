using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using static GrowLog.Tests.AccountHarness;

namespace GrowLog.Tests;

public sealed class StaffTests(PostgresFixture database)
{
    private const string Pin = "4821";

    private static async Task<Guid> AddStaffAsync(HttpClient owner, string name = "Alex Moss")
    {
        var created = Json(await PostForTextAsync(owner, "/api/staff", new { name, pin = Pin }));
        return created.GetProperty("id").GetGuid();
    }

    private static async Task<string> InviteAsync(HttpClient owner, Guid id) =>
        Json(await PostForTextAsync(owner, $"/api/staff/{id}/invite")).GetProperty("token").GetString()!;

    private static async Task<HttpResponseMessage> RedeemInviteAsync(HttpClient device, TestAuthenticator authenticator, Guid userId, string token)
    {
        var options = await PostForTextAsync(device, "/api/invites/passkey-options", new { userId, token });
        return await PostAsync(device, "/api/invites/complete", new { userId, token, credential = Json(authenticator.Register(options)) });
    }

    [Fact]
    public async Task The_owner_can_add_staff_who_then_appear_in_the_list()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var ownerKey = new TestAuthenticator();
        await SetUpOwnerAsync(owner, ownerKey);

        var id = await AddStaffAsync(owner);

        var staff = await owner.GetFromJsonAsync<JsonElement>(Route("/api/staff"), Cancellation);
        Assert.Contains(staff.EnumerateArray(), s => s.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Someone_not_signed_in_cannot_add_staff()
    {
        await using var app = await CreateAppAsync(database);
        using var client = Client(app);

        Assert.Equal(HttpStatusCode.Unauthorized, await PostStatusAsync(client, "/api/staff", new { name = "Intruder", pin = Pin }));
    }

    [Fact]
    public async Task A_member_of_staff_cannot_add_staff()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var phone = Client(app);
        using var ownerKey = new TestAuthenticator();
        using var staffKey = new TestAuthenticator();
        await SetUpOwnerAsync(owner, ownerKey);
        var id = await AddStaffAsync(owner);
        using var redeemed = await RedeemInviteAsync(phone, staffKey, id, await InviteAsync(owner, id));

        Assert.Equal(HttpStatusCode.Forbidden, await PostStatusAsync(phone, "/api/staff", new { name = "Friend", pin = Pin }));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("1234567")]
    [InlineData("12a4")]
    [InlineData("")]
    public async Task A_pin_must_be_four_to_six_digits(string pin)
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var ownerKey = new TestAuthenticator();
        await SetUpOwnerAsync(owner, ownerKey);

        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(owner, "/api/staff", new { name = "Alex", pin }));
    }

    [Fact]
    public async Task Two_people_cannot_have_the_same_name()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var ownerKey = new TestAuthenticator();
        await SetUpOwnerAsync(owner, ownerKey);
        await AddStaffAsync(owner, "Alex Moss");

        Assert.Equal(HttpStatusCode.Conflict, await PostStatusAsync(owner, "/api/staff", new { name = "alex moss", pin = Pin }));
    }

    [Fact]
    public async Task An_invited_person_can_set_up_a_passkey_and_sign_in_as_staff()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var phone = Client(app);
        using var ownerKey = new TestAuthenticator();
        using var staffKey = new TestAuthenticator();
        await SetUpOwnerAsync(owner, ownerKey);
        var id = await AddStaffAsync(owner);

        using var redeemed = await RedeemInviteAsync(phone, staffKey, id, await InviteAsync(owner, id));
        Assert.Equal(HttpStatusCode.NoContent, redeemed.StatusCode);

        using var signOut = await PostAsync(phone, "/api/account/sign-out");
        Assert.Equal(HttpStatusCode.NoContent, await SignInAsync(phone, staffKey));

        var me = await phone.GetFromJsonAsync<JsonElement>(Route("/api/account/me"), Cancellation);
        Assert.Equal("Alex Moss", me.GetProperty("name").GetString());
        Assert.False(me.GetProperty("isOwner").GetBoolean());
    }

    [Fact]
    public async Task An_invitation_can_only_be_used_once()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var phone = Client(app);
        using var ownerKey = new TestAuthenticator();
        using var staffKey = new TestAuthenticator();
        await SetUpOwnerAsync(owner, ownerKey);
        var id = await AddStaffAsync(owner);
        var token = await InviteAsync(owner, id);
        using var redeemed = await RedeemInviteAsync(phone, staffKey, id, token);

        Assert.Equal(HttpStatusCode.Forbidden, await PostStatusAsync(phone, "/api/invites/passkey-options", new { userId = id, token }));
    }

    [Fact]
    public async Task A_made_up_invitation_is_refused()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var phone = Client(app);
        using var ownerKey = new TestAuthenticator();
        await SetUpOwnerAsync(owner, ownerKey);
        var id = await AddStaffAsync(owner);

        Assert.Equal(HttpStatusCode.Forbidden, await PostStatusAsync(phone, "/api/invites/passkey-options", new { userId = id, token = "made-up" }));
    }

    [Fact]
    public async Task Someone_who_has_left_cannot_sign_in_with_their_passkey()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var phone = Client(app);
        using var ownerKey = new TestAuthenticator();
        using var staffKey = new TestAuthenticator();
        await SetUpOwnerAsync(owner, ownerKey);
        var id = await AddStaffAsync(owner);
        using var redeemed = await RedeemInviteAsync(phone, staffKey, id, await InviteAsync(owner, id));
        using var signOut = await PostAsync(phone, "/api/account/sign-out");

        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(owner, $"/api/staff/{id}/archive"));

        Assert.Equal(HttpStatusCode.Unauthorized, await SignInAsync(phone, staffKey));
    }

    [Fact]
    public async Task The_owner_cannot_archive_themselves()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var ownerKey = new TestAuthenticator();
        await SetUpOwnerAsync(owner, ownerKey);
        var staff = await owner.GetFromJsonAsync<JsonElement>(Route("/api/staff"), Cancellation);
        var ownerId = staff.EnumerateArray().Single().GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(owner, $"/api/staff/{ownerId}/archive"));
    }
}
