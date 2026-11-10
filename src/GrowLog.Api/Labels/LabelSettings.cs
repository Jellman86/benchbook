using System.Text.Json;
using System.Text.Json.Serialization;
using GrowLog.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Labels;

/// <summary>Reads and writes each kind of label's style, filling in anything not chosen yet.</summary>
internal static class LabelSettings
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static Dictionary<LabelKind, LabelStyle> Read(string json)
    {
        var saved = JsonSerializer.Deserialize<Dictionary<LabelKind, LabelStyle>>(json, Options) ?? [];
        return Enum.GetValues<LabelKind>().ToDictionary(kind => kind, kind => saved.GetValueOrDefault(kind) ?? LabelStyle.DefaultFor(kind));
    }

    public static string Write(Dictionary<LabelKind, LabelStyle> styles) => JsonSerializer.Serialize(styles, Options);

    /// <summary>Everything about the nursery a label of this kind needs.</summary>
    public static async Task<LabelContext> LoadAsync(GrowLogDbContext db, LabelKind kind, CancellationToken cancellationToken)
    {
        var settings = await db.GetNurserySettingsAsync(cancellationToken);
        return new LabelContext(Read(settings.LabelStyles)[kind], settings.NurseryName, settings.Logo, settings.LogoContentType);
    }
}
