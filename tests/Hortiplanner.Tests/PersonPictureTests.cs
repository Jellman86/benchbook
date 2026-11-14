using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using static Hortiplanner.Tests.AccountHarness;

namespace Hortiplanner.Tests;

public sealed class PersonPictureTests(PostgresFixture database)
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

    private static async Task<HttpResponseMessage> PutPictureAsync(HttpClient client, string path, byte[] body)
    {
        using var content = new ByteArrayContent(body);
        return await client.PutAsync(Route(path), content, Cancellation);
    }

    [Fact]
    public async Task The_owner_can_give_anyone_a_picture_and_people_who_have_left_are_not_shown()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var ownerId = await nursery.OwnerIdAsync();

        using (var staffTries = await PutPictureAsync(nursery.Staff, $"/api/staff/{ownerId}/picture", Jpeg))
        {
            Assert.Equal(HttpStatusCode.Forbidden, staffTries.StatusCode);
        }

        using (var notAPicture = await PutPictureAsync(nursery.Owner, $"/api/staff/{nursery.StaffId}/picture", [1, 2, 3, 4]))
        {
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, notAPicture.StatusCode);
        }

        using (var set = await PutPictureAsync(nursery.Owner, $"/api/staff/{nursery.StaffId}/picture", Jpeg))
        {
            Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);
        }

        var member = (await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/staff"), Cancellation)).EnumerateArray()
            .Single(person => person.GetProperty("id").GetGuid() == nursery.StaffId);
        Assert.NotEqual(JsonValueKind.Null, member.GetProperty("pictureAt").ValueKind);

        using (var shown = await nursery.Staff.GetAsync(Route($"/api/people/{nursery.StaffId}/picture?v=1"), Cancellation))
        {
            Assert.Equal(Jpeg, await shown.Content.ReadAsByteArrayAsync(Cancellation));
            Assert.Equal("nosniff", Assert.Single(shown.Headers.GetValues("X-Content-Type-Options")));
        }

        using (var left = await nursery.Owner.PostAsync(Route($"/api/staff/{nursery.StaffId}/archive"), null, Cancellation))
        {
            Assert.True(left.IsSuccessStatusCode);
        }

        using var stranger = nursery.App.CreateClient();

        using (var hidden = await stranger.GetAsync(Route($"/api/people/{nursery.StaffId}/picture"), Cancellation))
        {
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        }

        using (var removed = await nursery.Owner.DeleteAsync(Route($"/api/staff/{nursery.StaffId}/picture"), Cancellation))
        {
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        }
    }

    [Fact]
    public async Task Uploads_at_the_same_moment_all_succeed_and_one_picture_is_kept()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);

        var replies = await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
        {
            using var content = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xE0, (byte)index]);
            return await nursery.Staff.PutAsync(Route("/api/account/picture"), content, Cancellation);
        }));

        Assert.All(replies, reply => Assert.Equal(HttpStatusCode.NoContent, reply.StatusCode));

        foreach (var reply in replies)
        {
            reply.Dispose();
        }

        using var shown = await nursery.Staff.GetAsync(Route($"/api/people/{nursery.StaffId}/picture"), Cancellation);
        Assert.Equal(HttpStatusCode.OK, shown.StatusCode);
    }

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.20.0.1", false)]
    [InlineData("192.168.213.103", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.100.100.100", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("::1", false)]
    [InlineData("fd7a:115c:a1e0::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("::ffff:192.168.1.1", false)]
    public void Pictures_are_only_fetched_from_the_public_internet(string address, bool allowed) =>
        Assert.Equal(allowed, Hortiplanner.Api.Accounts.PersonPictureEndpoints.IsPublic(System.Net.IPAddress.Parse(address)));
}
