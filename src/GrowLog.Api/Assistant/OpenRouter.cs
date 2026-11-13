using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GrowLog.Api.Assistant;

/// <summary>
/// Talks to OpenRouter, which passes questions on to the chosen model. Every request asks for providers that don't
/// keep or train on what they're sent.
/// </summary>
internal static class OpenRouter
{
    public const string ClientName = "OpenRouter";

    private static readonly Uri Models = new("https://openrouter.ai/api/v1/models?supported_parameters=tools");
    private static readonly Uri Key = new("https://openrouter.ai/api/v1/key");
    private static readonly Uri Chat = new("https://openrouter.ai/api/v1/chat/completions");

    private static readonly Lock CacheLock = new();
    private static (DateTimeOffset At, List<ModelChoice> Models)? cache;
    private static DateTimeOffset? failedAt;

    /// <summary>The models that can use GrowLog's tools, cheapest first, kept for an hour.</summary>
    public static async Task<List<ModelChoice>> ModelsAsync(HttpClient client, TimeProvider clock, CancellationToken cancellationToken)
    {
        lock (CacheLock)
        {
            if (cache is { } kept && clock.GetUtcNow() - kept.At < TimeSpan.FromHours(1))
            {
                return kept.Models;
            }
        }

        using var response = await client.GetAsync(Models, cancellationToken);
        response.EnsureSuccessStatusCode();
        var data = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))?["data"]?.AsArray() ?? [];
        var models = data
            .Select(ModelChoice.From)
            .OfType<ModelChoice>()
            .OrderBy(model => model.PromptPerMillion + model.CompletionPerMillion)
            .ToList();

        lock (CacheLock)
        {
            cache = (clock.GetUtcNow(), models);
        }

        return models;
    }

    /// <summary>
    /// The models, or null if OpenRouter can't be reached quickly. A failure is remembered for five minutes, so a screen
    /// asking again and again doesn't wait each time.
    /// </summary>
    public static async Task<List<ModelChoice>?> TryModelsAsync(HttpClient client, TimeProvider clock, CancellationToken cancellationToken)
    {
        lock (CacheLock)
        {
            if (failedAt is { } failed && clock.GetUtcNow() - failed < TimeSpan.FromMinutes(5))
            {
                return null;
            }
        }

        using var quickly = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        quickly.CancelAfter(TimeSpan.FromSeconds(6));

        try
        {
            return await ModelsAsync(client, clock, quickly.Token);
        }
        catch (Exception problem) when (problem is HttpRequestException or JsonException or InvalidOperationException
            || (problem is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            lock (CacheLock)
            {
                failedAt = clock.GetUtcNow();
            }

            return null;
        }
    }

    /// <summary>What the key has spent and its limit, in US dollars as OpenRouter reports them, or null if unknown.</summary>
    public static async Task<KeyUsage?> UsageAsync(HttpClient client, string key, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Key);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var data = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))?["data"];
            return new KeyUsage(Number(data?["usage"]) ?? 0, Number(data?["limit"]));
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>One turn of the conversation: the model's reply, which may ask for tools, and what it cost.</summary>
    public static async Task<JsonNode?> AskAsync(HttpClient client, string key, JsonObject body, CancellationToken cancellationToken)
    {
        body["provider"] = new JsonObject { ["data_collection"] = "deny" };
        body["usage"] = new JsonObject { ["include"] = true };

        using var request = new HttpRequestMessage(HttpMethod.Post, Chat)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Add("X-Title", "GrowLog");

        using var response = await client.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new AssistantUnavailableException((int)response.StatusCode);
        }

        return JsonNode.Parse(text);
    }

    private static decimal? Number(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<double>(out var number) ? (decimal)number
        : node is JsonValue text && text.TryGetValue<string>(out var words) && decimal.TryParse(words, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed
        : null;

    /// <summary>A model the owner can choose, with its price per million tokens in US dollars.</summary>
    internal sealed record ModelChoice(string Id, string Name, decimal PromptPerMillion, decimal CompletionPerMillion, bool ReadsImages, int ContextLength)
    {
        public static ModelChoice? From(JsonNode? node)
        {
            if (node?["id"]?.GetValue<string>() is not { } id)
            {
                return null;
            }

            var inputs = node["architecture"]?["input_modalities"]?.AsArray().Select(item => item?.GetValue<string>()) ?? [];
            return new ModelChoice(
                id,
                node["name"]?.GetValue<string>() ?? id,
                (Number(node["pricing"]?["prompt"]) ?? 0) * 1_000_000,
                (Number(node["pricing"]?["completion"]) ?? 0) * 1_000_000,
                inputs.Contains("image", StringComparer.Ordinal),
                node["context_length"]?.GetValue<int>() ?? 0);
        }
    }

    /// <summary>A key's spend so far and its limit, if it has one.</summary>
    internal sealed record KeyUsage(decimal Spent, decimal? Limit);
}
