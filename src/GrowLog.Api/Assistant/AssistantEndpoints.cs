using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrowLog.Api.Accounts;
using GrowLog.Api.Data;
using GrowLog.Api.Settings;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Assistant;

/// <summary>
/// Ask GrowLog: questions about plants, the nursery's records and photos of plants, answered by a model the owner
/// chooses through their own OpenRouter key. It reads records through tools and never changes them. Nothing asked is
/// kept on the server.
/// </summary>
internal static class AssistantEndpoints
{
    private const int MostTurns = 6;
    private const int MostMessages = 24;
    private const int LongestMessage = 4000;
    private const int MostImages = 4;
    private const int LargestImage = 4 * 1024 * 1024;

    public static void MapAssistantEndpoints(this IEndpointRouteBuilder app)
    {
        var assistant = app.MapGroup("/api/assistant").RequireFeature(NurseryFeature.Assistant);
        assistant.MapGet("/status", StatusAsync);
        assistant.MapPost("/ask", AskAsync).Produces<AskAnswer>().Produces<AskRefused>(StatusCodes.Status409Conflict);

        var settings = assistant.MapGroup("/settings").RequireAuthorization(AccountServices.OwnerPolicy);
        settings.MapGet("/", GetSettingsAsync);
        settings.MapPut("/", SaveSettingsAsync);
        settings.MapGet("/models", ModelsAsync);
    }

    private static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static bool MayAsk(ClaimsPrincipal user, NurserySettings settings) =>
        user.IsInRole(AccountServices.OwnerRole) || settings.AssistantForStaff;

    /// <summary>Whether this person can ask, and whether the chosen model reads photos, for the screens to offer it.</summary>
    private static async Task<AssistantStatus> StatusAsync(ClaimsPrincipal user, GrowLogDbContext db, IHttpClientFactory clients, TimeProvider clock, CancellationToken cancellationToken)
    {
        var settings = await db.GetNurserySettingsAsync(cancellationToken);
        var ready = settings is { AssistantKey: not null, AssistantModel: not null };
        var readsImages = false;

        if (ready)
        {
            try
            {
                var models = await OpenRouter.ModelsAsync(clients.CreateClient(OpenRouter.ClientName), clock, cancellationToken);
                readsImages = models.Exists(model => string.Equals(model.Id, settings.AssistantModel, StringComparison.Ordinal) && model.ReadsImages);
            }
            catch (HttpRequestException)
            {
                // Without the list, photos are offered and the model says if it can't read them.
                readsImages = true;
            }
        }

        return new AssistantStatus(ready && MayAsk(user, settings), ready, readsImages);
    }

    private static async Task<AssistantSettings> GetSettingsAsync(GrowLogDbContext db, AssistantKeys keys, IHttpClientFactory clients, CancellationToken cancellationToken)
    {
        var settings = await db.GetNurserySettingsAsync(cancellationToken);
        var key = keys.Read(settings.AssistantKey);
        var usage = key is null ? null : await OpenRouter.UsageAsync(clients.CreateClient(OpenRouter.ClientName), key, cancellationToken);
        return new AssistantSettings(key is not null, settings.AssistantModel, settings.AssistantForStaff, usage?.Spent, usage?.Limit);
    }

    private static async Task<IResult> SaveSettingsAsync(AssistantSettingsRequest request, GrowLogDbContext db, AssistantKeys keys, CancellationToken cancellationToken)
    {
        var key = request.Key?.Trim();
        var model = request.Model?.Trim();

        if (key is { Length: > 300 } || key?.Any(char.IsWhiteSpace) == true || model is { Length: > 200 })
        {
            return TypedResults.BadRequest();
        }

        var settings = await db.NurserySettings.FirstOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            settings = new NurserySettings();
            db.NurserySettings.Add(settings);
        }

        // An empty key leaves the saved one; RemoveKey takes it away.
        if (request.RemoveKey)
        {
            settings.AssistantKey = null;
        }
        else if (!string.IsNullOrEmpty(key))
        {
            settings.AssistantKey = keys.Protect(key);
        }

        settings.AssistantModel = string.IsNullOrEmpty(model) ? null : model;
        settings.AssistantForStaff = request.ForStaff;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ModelsAsync(IHttpClientFactory clients, TimeProvider clock, CancellationToken cancellationToken)
    {
        try
        {
            return TypedResults.Ok(await OpenRouter.ModelsAsync(clients.CreateClient(OpenRouter.ClientName), clock, cancellationToken));
        }
        catch (HttpRequestException)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status502BadGateway, title: "OpenRouter can't be reached");
        }
    }

    private static async Task<IResult> AskAsync(
        AskRequest request,
        ClaimsPrincipal user,
        GrowLogDbContext db,
        EventStore store,
        AssistantKeys keys,
        AskLimiter limiter,
        IHttpClientFactory clients,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var settings = await db.GetNurserySettingsAsync(cancellationToken);

        if (!MayAsk(user, settings))
        {
            return TypedResults.Forbid();
        }

        if (keys.Read(settings.AssistantKey) is not { } key || settings.AssistantModel is not { } model)
        {
            return TypedResults.Conflict(new AskRefused("NotSetUp"));
        }

        if (!IsSensible(request))
        {
            return TypedResults.BadRequest();
        }

        if (!limiter.TryTake(UserId(user)))
        {
            return TypedResults.Conflict(new AskRefused("TooMany"));
        }

        var context = new ToolContext(db, store, clock, cancellationToken);
        var messages = await ConversationAsync(request, context);

        try
        {
            return TypedResults.Ok(await AnswerAsync(clients.CreateClient(OpenRouter.ClientName), key, model, messages, settings.FeaturesOff, context));
        }
        catch (AssistantUnavailableException problem)
        {
            return TypedResults.Conflict(new AskRefused(problem.Status is 401 or 402 or 403 ? "KeyRefused" : "Unavailable"));
        }
        catch (HttpRequestException)
        {
            return TypedResults.Conflict(new AskRefused("Unavailable"));
        }
    }

    private static bool IsSensible(AskRequest request) =>
        request.Messages is { Count: > 0 and <= MostMessages }
        && string.Equals(request.Messages[^1].Role, "user"
, StringComparison.Ordinal) && request.Messages.TrueForAll(message => message.Role is "user" or "assistant" && message.Text.Length is > 0 and <= LongestMessage)
        && (request.Images?.Count ?? 0) <= MostImages
        && (request.Images ?? []).TrueForAll(image => image.Length <= LargestImage * 4 / 3 && image.StartsWith("data:image/", StringComparison.Ordinal));

    /// <summary>The model's instructions, what the person is looking at, then the conversation, with photos on the last question.</summary>
    private static async Task<JsonArray> ConversationAsync(AskRequest request, ToolContext context)
    {
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = Instructions(context.Clock) },
        };

        if (await AboutAsync(request.About, context) is { } about)
        {
            messages.Add(new JsonObject { ["role"] = "system", ["content"] = about });
        }

        var images = new List<string>(request.Images ?? []);

        if (request.About?.PhotoId is { } photoId
            && await context.Db.BatchPhotos.AsNoTracking().Where(photo => photo.Id == photoId).Select(photo => new { photo.ContentType, photo.Content }).FirstOrDefaultAsync(context.Cancellation) is { } photo)
        {
            images.Add($"data:{photo.ContentType};base64,{Convert.ToBase64String(photo.Content)}");
        }

        for (var index = 0; index < request.Messages.Count; index++)
        {
            var message = request.Messages[index];
            var last = index == request.Messages.Count - 1;
            JsonNode content = last && images.Count > 0
                ? new JsonArray([new JsonObject { ["type"] = "text", ["text"] = message.Text }, .. images.Select(image => (JsonNode)new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = image } })])
                : JsonValue.Create(message.Text);
            messages.Add(new JsonObject { ["role"] = message.Role, ["content"] = content });
        }

        return messages;
    }

    /// <summary>Asks, runs the tools the model wants, and asks again, until it answers or the turns run out.</summary>
    private static async Task<AskAnswer> AnswerAsync(HttpClient client, string key, string model, JsonArray messages, IReadOnlyCollection<NurseryFeature> off, ToolContext context)
    {
        var tools = AssistantTools.Definitions(off);
        decimal cost = 0;

        for (var turn = 0; turn < MostTurns; turn++)
        {
            var body = new JsonObject
            {
                ["model"] = model,
                ["messages"] = messages.DeepClone(),
                ["tools"] = tools.DeepClone(),
                ["max_tokens"] = 1500,
            };
            var reply = await OpenRouter.AskAsync(client, key, body, context.Cancellation);
            cost += reply?["usage"]?["cost"] is JsonValue spent && spent.TryGetValue<double>(out var amount) ? (decimal)amount : 0;
            var message = reply?["choices"]?[0]?["message"];
            var calls = message?["tool_calls"]?.AsArray();

            if (calls is not { Count: > 0 } || turn == MostTurns - 1)
            {
                return new AskAnswer(message?["content"]?.GetValue<string>()?.Trim() ?? "There was no answer. Please try again.", context.Sources, cost);
            }

            messages.Add(message!.DeepClone());

            foreach (var call in calls)
            {
                var name = call?["function"]?["name"]?.GetValue<string>() ?? string.Empty;
                var arguments = call?["function"]?["arguments"]?.GetValue<string>() is { Length: > 0 } text ? ParseOrNull(text) : null;
                messages.Add(new JsonObject
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = call?["id"]?.GetValue<string>(),
                    ["content"] = await AssistantTools.RunAsync(name, arguments, context),
                });
            }
        }

        return new AskAnswer("That needed more looking up than one question allows. Try asking something narrower.", context.Sources, cost);
    }

    private static JsonNode? ParseOrNull(string text)
    {
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>What the person asked from: a tray or a plant, given to the model as a starting point.</summary>
    private static async Task<string?> AboutAsync(AskAbout? about, ToolContext context)
    {
        if (about?.TrayLabel is { Length: > 0 } label)
        {
            return $"The person is asking from tray {label}. Look it up with the tray tool before answering about it. {await AssistantTools.RunAsync("tray", new JsonObject { ["label"] = label }, context)}";
        }

        if (about?.PlantId is { } plantId
            && await context.Db.Plants.AsNoTracking().Where(plant => plant.Id == plantId).Select(plant => plant.CommonName + (plant.Variety == null ? string.Empty : " '" + plant.Variety + "'")).FirstOrDefaultAsync(context.Cancellation) is { } name)
        {
            return $"The person is asking about the plant {name}. {await AssistantTools.RunAsync("find_plants", new JsonObject { ["query"] = name.Split(' ')[0] }, context)}";
        }

        return null;
    }

    private static string Instructions(TimeProvider clock) => $"""
        You are Ask GrowLog, the assistant in GrowLog, the records system of a small plant nursery in England. Today is {clock.GetLocalNow().ToString("dddd d MMMM yyyy", CultureInfo.GetCultureInfo("en-GB"))}.
        Answer in plain UK English, briefly, using metric units and UK dates. Use short paragraphs or a short list.
        For anything about this nursery's plants, trays, places, losses, treatments, climate or jobs, look it up with the tools first; never guess or invent records. Name trays by their label code, such as C000012, and plants by name, so the person can find them.
        You can only read records. You can't record, change or delete anything: when something should be done, say what, and the person does it in GrowLog.
        For general growing questions, give sound horticultural advice for the UK climate, and say where the nursery's own records suggest something different.
        For a photo of plants, describe what you see, give the likely causes in order (pests, diseases, watering, nutrients, cold, heat or scorch) with how sure you are, and what to check to be sure. It is never a diagnosis.
        If what you see or hear could be a notifiable or quarantine pest or disease, such as Xylella fastidiosa, Ceratocystis platani, ash dieback on young stock, or Phytophthora ramorum, say so plainly, tell them to isolate the plants, not to move, sell or treat them first, and to report it to the Animal and Plant Health Agency (APHA) through the Plant Health Portal or by phone.
        You don't have customer details, money or staff names, and shouldn't ask for them.
        """;

    /// <summary>Whether this person can ask, whether it's set up at all, and whether the model reads photos.</summary>
    internal sealed record AssistantStatus(bool CanAsk, bool SetUp, bool ReadsImages);

    /// <summary>The owner's choices. The key itself is never sent back: only whether there is one, and what it has spent.</summary>
    internal sealed record AssistantSettings(bool HasKey, string? Model, bool ForStaff, decimal? Spent, decimal? Limit);

    /// <summary>A new key (or empty to keep the saved one), the model, and whether staff can ask.</summary>
    internal sealed record AssistantSettingsRequest(string? Key, string? Model, bool ForStaff, bool RemoveKey = false);

    /// <summary>A line of the conversation: "user" or "assistant".</summary>
    internal sealed record AskMessage(string Role, string Text);

    /// <summary>What the person is asking from, if anything: a tray, a plant, or one of a batch's photos.</summary>
    internal sealed record AskAbout(string? TrayLabel, Guid? PlantId, Guid? PhotoId);

    /// <summary>The conversation so far, the last line being the new question, with any photos as data URLs.</summary>
    internal sealed record AskRequest(List<AskMessage> Messages, List<string>? Images, AskAbout? About);

    /// <summary>The answer, the records it looked at, and what it cost in US dollars.</summary>
    internal sealed record AskAnswer(string Answer, IReadOnlyList<ToolContext.AskSource> Sources, decimal Cost);

    /// <summary>Why a question couldn't be answered: NotSetUp, TooMany, KeyRefused or Unavailable.</summary>
    internal sealed record AskRefused(string Problem);
}
