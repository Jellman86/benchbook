using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hortiplanner.Api.Accounts;
using Xunit;
using static Hortiplanner.Tests.AccountHarness;

namespace Hortiplanner.Tests;

public sealed class SignInTuneTests(PostgresFixture database)
{
    // An MP3 frame header (MPEG-1 Layer III) followed by a little padding.
    private static readonly byte[] Mp3 = [0xFF, 0xFB, 0x90, 0x64, 0, 0, 0, 0, 0, 0, 0, 0];

    private static readonly byte[] M4a = [0, 0, 0, 0x20, .. "ftypM4A "u8.ToArray(), 0, 0, 0, 0];

    private static async Task<HttpResponseMessage> PutTuneAsync(HttpClient client, Guid personId, byte[] body)
    {
        using var content = new ByteArrayContent(body);
        return await client.PutAsync(Route($"/api/staff/{personId}/tune"), content, Cancellation);
    }

    private static async Task<JsonElement> StaffMemberAsync(HttpClient owner, Guid id) =>
        (await owner.GetFromJsonAsync<JsonElement>(Route("/api/staff"), Cancellation)).EnumerateArray().Single(member => member.GetProperty("id").GetGuid() == id);

    [Theory]
    [InlineData(new byte[] { 0x49, 0x44, 0x33, 4, 0, 0 }, "audio/mpeg")]
    [InlineData(new byte[] { 0xFF, 0xFB, 0x90, 0x64 }, "audio/mpeg")]
    [InlineData(new byte[] { 0xFF, 0xF1, 0x50, 0x80 }, "audio/aac")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x41, 0x56, 0x45 }, "audio/wav")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, null)]
    [InlineData(new byte[] { 0x4F, 0x67, 0x67, 0x53 }, null)]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x48, 0x00 }, null)]
    [InlineData(new byte[] { 0, 0, 0, 0x18, 0x66, 0x74, 0x79, 0x70, 0x68, 0x65, 0x69, 0x63, 0, 0, 0, 0 }, null)]
    [InlineData(new byte[] { 0, 0, 0, 0x1C, 0x66, 0x74, 0x79, 0x70, 0x4D, 0x34, 0x41, 0x20, 0, 0, 0, 0 }, "audio/mp4")]
    public void Only_audio_a_phone_can_play_is_recognised(byte[] content, string? expected) =>
        Assert.Equal(expected, SignInTuneEndpoints.DetectType(content));

    [Fact]
    public async Task The_owner_gives_someone_a_tune_which_plays_for_them_until_they_turn_it_off()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);

        using (var set = await PutTuneAsync(nursery.Owner, nursery.StaffId, M4a))
        {
            Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);
        }

        Assert.True((await StaffMemberAsync(nursery.Owner, nursery.StaffId)).GetProperty("hasTune").GetBoolean());

        using (var played = await nursery.Staff.GetAsync(Route("/api/account/tune"), Cancellation))
        {
            Assert.Equal(HttpStatusCode.OK, played.StatusCode);
            Assert.Equal("audio/mp4", played.Content.Headers.ContentType?.MediaType);
            Assert.Equal(M4a, await played.Content.ReadAsByteArrayAsync(Cancellation));
            Assert.Equal("bytes", Assert.Single(played.Headers.AcceptRanges));
        }

        var settings = await nursery.Staff.GetFromJsonAsync<JsonElement>(Route("/api/account/settings"), Cancellation);
        Assert.True(settings.GetProperty("hasTune").GetBoolean());
        Assert.True(settings.GetProperty("tuneOn").GetBoolean());

        using (var off = await nursery.Staff.PutAsJsonAsync(Route("/api/account/tune/on"), new { on = false }, Cancellation))
        {
            Assert.Equal(HttpStatusCode.NoContent, off.StatusCode);
        }

        using (var silent = await nursery.Staff.GetAsync(Route("/api/account/tune"), Cancellation))
        {
            Assert.Equal(HttpStatusCode.NotFound, silent.StatusCode);
        }

        // The owner can still hear what they chose, and a new tune doesn't turn it back on behind the person's back.
        using (var preview = await nursery.Owner.GetAsync(Route($"/api/staff/{nursery.StaffId}/tune"), Cancellation))
        {
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        }

        using (var replaced = await PutTuneAsync(nursery.Owner, nursery.StaffId, Mp3))
        {
            Assert.Equal(HttpStatusCode.NoContent, replaced.StatusCode);
        }

        Assert.False((await nursery.Staff.GetFromJsonAsync<JsonElement>(Route("/api/account/settings"), Cancellation)).GetProperty("tuneOn").GetBoolean());
    }

    [Fact]
    public async Task A_phone_can_ask_for_part_of_the_tune_and_nobody_else_can_hear_or_change_it()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);

        using (var set = await PutTuneAsync(nursery.Owner, nursery.StaffId, M4a))
        {
            Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);
        }

        using var part = new HttpRequestMessage(HttpMethod.Get, Route("/api/account/tune"));
        part.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 3);

        using (var partial = await nursery.Staff.SendAsync(part, Cancellation))
        {
            Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
            Assert.Equal(M4a[..4], await partial.Content.ReadAsByteArrayAsync(Cancellation));
            Assert.Equal("nosniff", Assert.Single(partial.Headers.GetValues("X-Content-Type-Options")));
            Assert.True(partial.Headers.CacheControl?.NoStore);
        }

        using (var peek = await nursery.Staff.GetAsync(Route($"/api/staff/{nursery.StaffId}/tune"), Cancellation))
        {
            Assert.Equal(HttpStatusCode.Forbidden, peek.StatusCode);
        }

        using (var remove = await nursery.Staff.DeleteAsync(Route($"/api/staff/{nursery.StaffId}/tune"), Cancellation))
        {
            Assert.Equal(HttpStatusCode.Forbidden, remove.StatusCode);
        }

        using var stranger = nursery.App.CreateClient();

        using (var anonymous = await stranger.GetAsync(Route("/api/account/tune"), Cancellation))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }
    }

    [Fact]
    public async Task Only_the_owner_sets_tunes_and_only_playable_audio_is_taken()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database);
        var ownerId = await nursery.OwnerIdAsync();

        using (var staffTries = await PutTuneAsync(nursery.Staff, ownerId, Mp3))
        {
            Assert.Equal(HttpStatusCode.Forbidden, staffTries.StatusCode);
        }

        using (var picture = await PutTuneAsync(nursery.Owner, nursery.StaffId, [0xFF, 0xD8, 0xFF, 0xE0, 1, 2]))
        {
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, picture.StatusCode);
        }

        using (var tooBig = await PutTuneAsync(nursery.Owner, nursery.StaffId, [.. Mp3, .. new byte[SignInTuneEndpoints.MaxBytes]]))
        {
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooBig.StatusCode);
        }

        using (var nobody = await PutTuneAsync(nursery.Owner, Guid.CreateVersion7(), Mp3))
        {
            Assert.Equal(HttpStatusCode.NotFound, nobody.StatusCode);
        }

        // Without a tune there's nothing to play or switch.
        using (var none = await nursery.Staff.GetAsync(Route("/api/account/tune"), Cancellation))
        {
            Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
        }

        using (var noSwitch = await nursery.Staff.PutAsJsonAsync(Route("/api/account/tune/on"), new { on = false }, Cancellation))
        {
            Assert.Equal(HttpStatusCode.NotFound, noSwitch.StatusCode);
        }

        using (var set = await PutTuneAsync(nursery.Owner, nursery.StaffId, Mp3))
        {
            Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);
        }

        using (var removed = await nursery.Owner.DeleteAsync(Route($"/api/staff/{nursery.StaffId}/tune"), Cancellation))
        {
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        }

        Assert.False((await StaffMemberAsync(nursery.Owner, nursery.StaffId)).GetProperty("hasTune").GetBoolean());
    }
}
