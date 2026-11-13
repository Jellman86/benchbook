using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static GrowLog.Tests.AccountHarness;

namespace GrowLog.Tests;

/// <summary>Ask GrowLog against a pretend OpenRouter: the key stays on the server and only records, never people, are sent.</summary>
public sealed class AssistantTests(PostgresFixture database)
{
    private const string Key = "sk-or-test-key";

    private static readonly string[] NotPhotos = ["https://example.com/a.jpg"];

    /// <summary>Answers like OpenRouter: a model list, a key's spend, and a model that looks up stock before answering.</summary>
    private sealed class PretendOpenRouter : HttpMessageHandler
    {
        public List<(string Authorization, string Body)> Questions { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/models", StringComparison.Ordinal))
            {
                return Reply("""{"data":[{"id":"test/model","name":"Test model","pricing":{"prompt":"0.000001","completion":"0.000002"},"context_length":100000,"architecture":{"input_modalities":["text","image"]}}]}""");
            }

            if (path.EndsWith("/key", StringComparison.Ordinal))
            {
                return Reply("""{"data":{"usage":1.25,"limit":10}}""");
            }

            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Questions.Add((request.Headers.Authorization?.ToString() ?? string.Empty, body));
            var askedForTools = JsonNode.Parse(body)!["messages"]!.AsArray().Any(message => string.Equals(message?["role"]?.GetValue<string>(), "tool", StringComparison.Ordinal));

            return askedForTools
                ? Reply("""{"choices":[{"message":{"role":"assistant","content":"You have 40 Lavender growing."}}],"usage":{"cost":0.002}}""")
                : Reply("""{"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[{"id":"call-1","type":"function","function":{"name":"stock","arguments":"{}"}}]}}],"usage":{"cost":0.001}}""");
        }

        private static HttpResponseMessage Reply(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private static async Task<HttpResponseMessage> AskAsync(HttpClient client, string question) =>
        await client.PostAsJsonAsync(Route("/api/assistant/ask"), new { messages = new[] { new { role = "user", text = question } } }, Cancellation);

    [Fact]
    public async Task The_owner_sets_it_up_and_answers_look_up_records_without_sending_people()
    {
        var openRouter = new PretendOpenRouter();
        await using var nursery = await TwoPersonNursery.CreateAsync(database, outsideWorld: openRouter);
        var plant = Json(await PostForTextAsync(nursery.Owner, "/api/plants", new { commonName = "Lavender", xylellaHost = false, protectedVariety = false })).GetProperty("id").GetGuid();
        var label = Json(await PostForTextAsync(nursery.Staff, "/api/batches", new { eventId = Guid.NewGuid(), occurredAt = DateTimeOffset.UtcNow.AddHours(-1), plantId = plant, quantity = 40, source = "SeedLot", originCountry = "GB", containerKind = "Tray" })).GetProperty("labelCode").GetString()!;

        var status = await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/assistant/status"), Cancellation);
        Assert.False(status.GetProperty("canAsk").GetBoolean());
        using (var early = await AskAsync(nursery.Owner, "What's growing?"))
        {
            Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync(nursery.Staff, "/api/assistant/settings"));
        using (var saved = await nursery.Owner.PutAsJsonAsync(Route("/api/assistant/settings"), new { key = Key, model = "test/model", forStaff = false }, Cancellation))
        {
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        }

        // The key is never sent back, only that there is one and what it has spent.
        var settingsText = await nursery.Owner.GetStringAsync(Route("/api/assistant/settings"), Cancellation);
        Assert.DoesNotContain(Key, settingsText, StringComparison.Ordinal);
        var settings = Json(settingsText);
        Assert.True(settings.GetProperty("hasKey").GetBoolean());
        Assert.Equal(1.25m, settings.GetProperty("spent").GetDecimal());
        Assert.True((await nursery.Owner.GetFromJsonAsync<JsonElement>(Route("/api/assistant/status"), Cancellation)).GetProperty("readsImages").GetBoolean());

        using var answered = await AskAsync(nursery.Owner, "What's growing?");
        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
        var answer = Json(await answered.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal("You have 40 Lavender growing.", answer.GetProperty("answer").GetString());
        Assert.Contains(answer.GetProperty("sources").EnumerateArray(), source => string.Equals(source.GetProperty("key").GetString(), label, StringComparison.Ordinal));
        Assert.Equal(0.003m, answer.GetProperty("cost").GetDecimal());

        Assert.Equal(2, openRouter.Questions.Count);
        Assert.All(openRouter.Questions, question =>
        {
            Assert.Equal($"Bearer {Key}", question.Authorization);
            Assert.Equal("deny", JsonNode.Parse(question.Body)!["provider"]!["data_collection"]!.GetValue<string>());
            Assert.DoesNotContain("Alex Moss", question.Body, StringComparison.Ordinal);
        });
        Assert.Contains(label, openRouter.Questions[1].Body, StringComparison.Ordinal);

        // Staff can ask only once the owner says so.
        using (var refused = await AskAsync(nursery.Staff, "What's growing?"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        using (var opened = await nursery.Owner.PutAsJsonAsync(Route("/api/assistant/settings"), new { key = string.Empty, model = "test/model", forStaff = true }, Cancellation))
        {
            Assert.Equal(HttpStatusCode.NoContent, opened.StatusCode);
        }

        using var staffAnswer = await AskAsync(nursery.Staff, "What's growing?");
        Assert.Equal(HttpStatusCode.OK, staffAnswer.StatusCode);
    }

    [Fact]
    public async Task Turned_off_it_is_refused_and_odd_questions_are_turned_away()
    {
        await using var nursery = await TwoPersonNursery.CreateAsync(database, outsideWorld: new PretendOpenRouter());
        using (var saved = await nursery.Owner.PutAsJsonAsync(Route("/api/assistant/settings"), new { key = Key, model = "test/model", forStaff = false }, Cancellation))
        {
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        }

        using (var empty = await nursery.Owner.PostAsJsonAsync(Route("/api/assistant/ask"), new { messages = Array.Empty<object>() }, Cancellation))
        {
            Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        }

        using (var notAPhoto = await nursery.Owner.PostAsJsonAsync(Route("/api/assistant/ask"), new { messages = new[] { new { role = "user", text = "What's this?" } }, images = NotPhotos }, Cancellation))
        {
            Assert.Equal(HttpStatusCode.BadRequest, notAPhoto.StatusCode);
        }

        using (var off = await nursery.Owner.PutAsJsonAsync(Route("/api/settings/features/Assistant"), new { on = false }, Cancellation))
        {
            Assert.Equal(HttpStatusCode.NoContent, off.StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(nursery.Owner, "/api/assistant/status"));
    }
}
