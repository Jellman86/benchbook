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
}
