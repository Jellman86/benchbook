using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using static GrowLog.Tests.AccountHarness;

namespace GrowLog.Tests;

/// <summary>A fresh system with a signed-in owner and a signed-in member of staff, on separate devices.</summary>
internal sealed class TwoPersonNursery : IAsyncDisposable
{
    private readonly TestAuthenticator ownerKey = new();
    private readonly TestAuthenticator staffKey = new();

    private TwoPersonNursery(TestApp app)
    {
        App = app;
        Owner = Client(app);
        Staff = Client(app);
    }

    public TestApp App { get; }

    public HttpClient Owner { get; }

    public HttpClient Staff { get; }

    public Guid StaffId { get; private set; }

    public static async Task<TwoPersonNursery> CreateAsync(PostgresFixture database)
    {
        var nursery = new TwoPersonNursery(await CreateAppAsync(database));
        await SetUpOwnerAsync(nursery.Owner, nursery.ownerKey);

        var userId = Json(await PostForTextAsync(nursery.Owner, "/api/staff", new { name = "Alex Moss", pin = "4821" })).GetProperty("id").GetGuid();
        var token = Json(await PostForTextAsync(nursery.Owner, $"/api/staff/{userId}/invite")).GetProperty("token").GetString();
        var options = await PostForTextAsync(nursery.Staff, "/api/invites/passkey-options", new { userId, token });
        Assert.Equal(
            HttpStatusCode.NoContent,
            await PostStatusAsync(nursery.Staff, "/api/invites/complete", new { userId, token, credential = Json(nursery.staffKey.Register(options)) }));

        nursery.StaffId = userId;
        return nursery;
    }

    public async Task<Guid> OwnerIdAsync()
    {
        var staff = await Owner.GetFromJsonAsync<JsonElement>(Route("/api/staff"), Cancellation);
        return staff.EnumerateArray().Single(s => string.Equals(s.GetProperty("name").GetString(), "Sam Fielding", StringComparison.Ordinal)).GetProperty("id").GetGuid();
    }

    public async ValueTask DisposeAsync()
    {
        Owner.Dispose();
        Staff.Dispose();
        ownerKey.Dispose();
        staffKey.Dispose();
        await App.DisposeAsync();
    }
}
