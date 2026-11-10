using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using static GrowLog.Tests.AccountHarness;

namespace GrowLog.Tests;

public sealed class SignInOptionsTests(PostgresFixture database)
{
    private const string Password = "a long enough password";

    private static async Task<Guid> AddAsync(HttpClient owner, object person) =>
        Json(await PostForTextAsync(owner, "/api/staff", person)).GetProperty("id").GetGuid();

    private static async Task<Guid> AddNameOnlyAsync(HttpClient owner, string name = "Jo Reed")
    {
        await AllowNameOnlyAsync(owner);
        return await AddAsync(owner, new { name, nameOnly = true });
    }

    private static Task<Guid> AddWithPasswordAsync(HttpClient owner, string name = "Jo Reed") =>
        AddAsync(owner, new { name, password = Password });

    private static Task<HttpStatusCode> ByNameAsync(HttpClient client, Guid userId) =>
        PostStatusAsync(client, "/api/account/sign-in/name", new { userId });

    private static Task<HttpStatusCode> ByPasswordAsync(HttpClient client, Guid userId, string password = Password) =>
        PostStatusAsync(client, "/api/account/sign-in/password", new { userId, password });

    private static async Task<JsonElement> PeopleAsync(HttpClient client) =>
        await client.GetFromJsonAsync<JsonElement>(Route("/api/account/people"), Cancellation);

    private static async Task<Guid> OwnerIdAsync(HttpClient client) =>
        (await PeopleAsync(client)).EnumerateArray().Single(person => string.Equals(person.GetProperty("name").GetString(), "Sam Fielding", StringComparison.Ordinal)).GetProperty("id").GetGuid();

    [Fact]
    public async Task Someone_chosen_for_name_only_sign_in_gets_in_by_tapping_their_name()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var ownerKey = new TestAuthenticator();
        await SetUpOwnerAsync(owner, ownerKey);
        var id = await AddNameOnlyAsync(owner);

        using var phone = Client(app);

        Assert.Equal(HttpStatusCode.NoContent, await ByNameAsync(phone, id));
        Assert.Equal(HttpStatusCode.OK, await MeStatusAsync(phone));
    }

    [Fact]
    public async Task Tapping_a_name_is_refused_for_anyone_not_chosen_for_it()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var ownerKey = new TestAuthenticator();
        await SetUpOwnerAsync(owner, ownerKey);
        var pinOnly = await AddAsync(owner, new { name = "Alex Moss", pin = "4821" });
        var withPassword = await AddWithPasswordAsync(owner);

        using var phone = Client(app);

        Assert.Equal(HttpStatusCode.Unauthorized, await ByNameAsync(phone, pinOnly));
        Assert.Equal(HttpStatusCode.Unauthorized, await ByNameAsync(phone, withPassword));
    }

    [Fact]
    public async Task The_owner_can_never_be_made_name_only()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var ownerKey = new TestAuthenticator();
        await SetUpOwnerAsync(owner, ownerKey);
        var ownerId = await OwnerIdAsync(owner);

        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(owner, $"/api/staff/{ownerId}/name-only"));

        using var phone = Client(app);
        Assert.Equal(HttpStatusCode.Unauthorized, await ByNameAsync(phone, ownerId));
    }

    [Fact]
    public async Task A_password_lets_someone_in_and_a_wrong_one_does_not()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var ownerKey = new TestAuthenticator();
        await SetUpOwnerAsync(owner, ownerKey);
        var id = await AddWithPasswordAsync(owner);

        using var phone = Client(app);

        Assert.Equal(HttpStatusCode.Unauthorized, await ByPasswordAsync(phone, id, "not the password"));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeStatusAsync(phone));
        Assert.Equal(HttpStatusCode.NoContent, await ByPasswordAsync(phone, id));
        Assert.Equal(HttpStatusCode.OK, await MeStatusAsync(phone));
    }

    [Fact]
    public async Task Repeated_wrong_passwords_lock_the_account_until_the_owner_sets_a_new_one()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var ownerKey = new TestAuthenticator();
        await SetUpOwnerAsync(owner, ownerKey);
        var id = await AddWithPasswordAsync(owner);

        using var phone = Client(app);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, await ByPasswordAsync(phone, id, "wrong password"));
        }

        Assert.Equal(HttpStatusCode.Locked, await ByPasswordAsync(phone, id, "wrong password"));
        Assert.Equal(HttpStatusCode.Locked, await ByPasswordAsync(phone, id));

        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(owner, $"/api/staff/{id}/password", new { password = "a replacement password" }));
        Assert.Equal(HttpStatusCode.NoContent, await ByPasswordAsync(phone, id, "a replacement password"));
    }

    [Fact]
    public async Task A_short_password_is_refused()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var ownerKey = new TestAuthenticator();
        await SetUpOwnerAsync(owner, ownerKey);

        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(owner, "/api/staff", new { name = "Jo Reed", password = "short" }));
    }

    [Fact]
    public async Task The_owner_can_set_up_with_a_password_instead_of_a_passkey()
    {
        await using var app = await CreateAppAsync(database);
        using var device = Client(app);

        Assert.Equal(HttpStatusCode.Forbidden, await PostStatusAsync(device, "/api/setup/complete-with-password", new { token = "wrong", name = "Sam Fielding", password = Password }));
        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(device, "/api/setup/complete-with-password", new { token = SetupToken, name = "Sam Fielding", password = "short" }));
        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(device, "/api/setup/complete-with-password", new { token = SetupToken, name = "Sam Fielding", password = Password }));
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(device, "/api/staff"));

        Assert.Equal(HttpStatusCode.Forbidden, await PostStatusAsync(device, "/api/setup/complete-with-password", new { token = SetupToken, name = "Second Owner", password = Password }));

        using var phone = Client(app);
        Assert.Equal(HttpStatusCode.NoContent, await ByPasswordAsync(phone, await OwnerIdAsync(phone)));
    }

    [Fact]
    public async Task Setting_a_password_in_settings_ends_name_only_sign_in()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var ownerKey = new TestAuthenticator();
        await SetUpOwnerAsync(owner, ownerKey);
        var id = await AddNameOnlyAsync(owner);

        using var phone = Client(app);
        await ByNameAsync(phone, id);

        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(phone, "/api/account/settings/password", new { newPassword = Password }));
        Assert.Equal(HttpStatusCode.OK, await MeStatusAsync(phone));

        using var other = Client(app);
        Assert.Equal(HttpStatusCode.Unauthorized, await ByNameAsync(other, id));
        Assert.Equal(HttpStatusCode.NoContent, await ByPasswordAsync(other, id));
    }

    [Fact]
    public async Task Changing_a_password_needs_the_current_one()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var ownerKey = new TestAuthenticator();
        await SetUpOwnerAsync(owner, ownerKey);
        var id = await AddWithPasswordAsync(owner);

        using var phone = Client(app);
        await ByPasswordAsync(phone, id);

        Assert.Equal(HttpStatusCode.Forbidden, await PostStatusAsync(phone, "/api/account/settings/password", new { currentPassword = "wrong", newPassword = "a different password" }));
        Assert.Equal(HttpStatusCode.Forbidden, await PostStatusAsync(phone, "/api/account/settings/password", new { newPassword = "a different password" }));
        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(phone, "/api/account/settings/password", new { currentPassword = Password, newPassword = "a different password" }));

        using var other = Client(app);
        Assert.Equal(HttpStatusCode.NoContent, await ByPasswordAsync(other, id, "a different password"));
    }

    [Fact]
    public async Task Someone_can_add_their_own_passkey_and_PIN()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var ownerKey = new TestAuthenticator();
        await SetUpOwnerAsync(owner, ownerKey);
        var id = await AddNameOnlyAsync(owner);

        using var phone = Client(app);
        using var phoneKey = new TestAuthenticator();
        await ByNameAsync(phone, id);

        var options = await PostForTextAsync(phone, "/api/account/settings/passkey-options");
        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(phone, "/api/account/settings/passkey", new { credential = Json(phoneKey.Register(options)) }));
        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(phone, "/api/account/settings/pin", new { pin = "12" }));
        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(phone, "/api/account/settings/pin", new { pin = "4821" }));

        var settings = await phone.GetFromJsonAsync<JsonElement>(Route("/api/account/settings"), Cancellation);
        Assert.True(settings.GetProperty("hasPasskey").GetBoolean());
        Assert.True(settings.GetProperty("hasPin").GetBoolean());
        Assert.False(settings.GetProperty("nameOnly").GetBoolean());

        using var other = Client(app);
        Assert.Equal(HttpStatusCode.Unauthorized, await ByNameAsync(other, id));
        Assert.Equal(HttpStatusCode.NoContent, await SignInAsync(other, phoneKey));
    }

    [Fact]
    public async Task The_owner_can_take_a_password_away_and_make_someone_name_only()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var ownerKey = new TestAuthenticator();
        await SetUpOwnerAsync(owner, ownerKey);
        var id = await AddWithPasswordAsync(owner);
        await AllowNameOnlyAsync(owner);

        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(owner, $"/api/staff/{id}/name-only"));

        using var phone = Client(app);
        Assert.Equal(HttpStatusCode.Unauthorized, await ByPasswordAsync(phone, id));
        Assert.Equal(HttpStatusCode.NoContent, await ByNameAsync(phone, id));
    }

    [Fact]
    public async Task The_sign_in_list_shows_only_people_who_can_sign_in_there()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var ownerKey = new TestAuthenticator();
        await SetUpOwnerAsync(owner, ownerKey);
        await AddAsync(owner, new { name = "Pin Only", pin = "4821" });
        var leaver = await AddNameOnlyAsync(owner, "Has Left");
        await AddNameOnlyAsync(owner);
        await PostStatusAsync(owner, $"/api/staff/{leaver}/archive");

        using var anonymous = Client(app);
        var names = (await PeopleAsync(anonymous)).EnumerateArray().Select(person => person.GetProperty("name").GetString());

        Assert.Equal(["Jo Reed", "Sam Fielding"], names, StringComparer.Ordinal);
        Assert.Equal(HttpStatusCode.Unauthorized, await ByNameAsync(anonymous, leaver));
    }

    [Fact]
    public async Task An_invitation_can_set_a_password_once()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var ownerKey = new TestAuthenticator();
        await SetUpOwnerAsync(owner, ownerKey);
        var userId = await AddAsync(owner, new { name = "Alex Moss", pin = "4821" });
        var token = Json(await PostForTextAsync(owner, $"/api/staff/{userId}/invite")).GetProperty("token").GetString()!;

        using var phone = Client(app);

        Assert.Equal(HttpStatusCode.BadRequest, await PostStatusAsync(phone, "/api/invites/complete-with-password", new { userId, token, password = "short" }));
        Assert.Equal(HttpStatusCode.NoContent, await PostStatusAsync(phone, "/api/invites/complete-with-password", new { userId, token, password = Password }));
        Assert.Equal(HttpStatusCode.OK, await MeStatusAsync(phone));

        using var other = Client(app);
        Assert.Equal(HttpStatusCode.Forbidden, await PostStatusAsync(other, "/api/invites/complete-with-password", new { userId, token, password = "someone elses password" }));
    }

    [Fact]
    public async Task Settings_need_someone_signed_in()
    {
        await using var app = await CreateAppAsync(database);
        using var client = Client(app);

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(client, "/api/account/settings"));
        Assert.Equal(HttpStatusCode.Unauthorized, await PostStatusAsync(client, "/api/account/settings/password", new { newPassword = Password }));
    }

    [Fact]
    public async Task Tapping_a_name_is_off_until_the_owner_turns_it_on()
    {
        await using var app = await CreateAppAsync(database);
        using var owner = Client(app);
        using var ownerKey = new TestAuthenticator();
        await SetUpOwnerAsync(owner, ownerKey);

        Assert.Equal(HttpStatusCode.Conflict, await PostStatusAsync(owner, "/api/staff", new { name = "Jo Reed", nameOnly = true }));

        var id = await AddNameOnlyAsync(owner);
        using var staff = Client(app);
        Assert.Equal(HttpStatusCode.NoContent, await ByNameAsync(staff, id));

        using var off = await owner.PutAsJsonAsync(Route("/api/staff/sign-in-settings"), new { allowNameOnly = false }, Cancellation);
        using var other = Client(app);
        Assert.Equal(HttpStatusCode.Unauthorized, await ByNameAsync(other, id));
        Assert.DoesNotContain((await PeopleAsync(other)).EnumerateArray(), person => person.GetProperty("id").GetGuid() == id);
    }
}
