using System.Security.Claims;
using Hortiplanner.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Accounts;

/// <summary>
/// A tune the owner chooses for someone, played on their device when they sign in. A bit of fun: the owner sets it,
/// and the person can turn theirs off.
/// </summary>
internal static class SignInTuneEndpoints
{
    /// <summary>Plenty for a minute of music, and well inside the proxy's upload limit.</summary>
    public const int MaxBytes = 4 * 1024 * 1024;

    public static void MapSignInTuneEndpoints(this IEndpointRouteBuilder app)
    {
        var staff = app.MapGroup("/api/staff/{id:guid}/tune").RequireAuthorization(AccountServices.OwnerPolicy);
        staff.MapPut("/", SetAsync);
        staff.MapGet("/", GetForOwnerAsync);
        staff.MapDelete("/", RemoveAsync);

        var own = app.MapGroup("/api/account/tune");
        own.MapGet("/", GetOwnAsync);
        own.MapPut("/on", TurnOnOrOffAsync);
    }

    /// <summary>MP4 brands that hold sound a phone plays: iTunes and Voice Memos audio, and plain MP4 files.</summary>
    private static readonly string[] Mp4Brands = ["M4A ", "M4B ", "mp42", "mp41", "isom", "iso2"];

    /// <summary>What sort of audio the bytes are, if one a phone's browser can play: MP3, AAC, M4A or WAV.</summary>
    public static string? DetectType(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith("ID3"u8))
        {
            return "audio/mpeg";
        }

        if (content.Length >= 4 && content[0] == 0xFF && (content[1] & 0xE0) == 0xE0)
        {
            return MpegFrameType(content[1], content[2]);
        }

        if (content.Length >= 12 && content[4..8].SequenceEqual("ftyp"u8))
        {
            var brand = System.Text.Encoding.ASCII.GetString(content[8..12]);
            return Mp4Brands.Contains(brand, StringComparer.Ordinal) ? "audio/mp4" : null;
        }

        return content.Length >= 12 && content.StartsWith("RIFF"u8) && content[8..12].SequenceEqual("WAVE"u8)
            ? "audio/wav"
            : null;
    }

    /// <summary>
    /// An MPEG frame header, read strictly enough that other files starting 0xFF (such as UTF-16 text) don't pass.
    /// Layer bits of zero mean AAC in an ADTS stream; anything else is MP3.
    /// </summary>
    private static string? MpegFrameType(byte second, byte third)
    {
        var layer = (second >> 1) & 0x03;

        if (layer == 0)
        {
            var sampleRate = (third >> 2) & 0x0F;
            return sampleRate < 13 ? "audio/aac" : null;
        }

        var version = (second >> 3) & 0x03;
        var bitrate = third >> 4;
        var frequency = (third >> 2) & 0x03;

        // Layer I (3) is left out: nobody uses it for music, and it is what a UTF-16 text file's FF FE looks like.
        return layer != 3 && version != 1 && bitrate is not (0 or 0x0F) && frequency != 3 ? "audio/mpeg" : null;
    }

    private static async Task<IResult> SetAsync(Guid id, HttpRequest request, HortiplannerDbContext db, UserManager<AppUser> users, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (await users.FindByIdAsync(id.ToString()) is not { IsTeamAccount: false })
        {
            return TypedResults.NotFound();
        }

        using var body = new MemoryStream();
        var buffer = new byte[81920];
        int read;

        while ((read = await request.Body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            body.Write(buffer, 0, read);

            if (body.Length > MaxBytes)
            {
                return TypedResults.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }
        }

        var content = body.ToArray();

        if (DetectType(content) is not { } contentType)
        {
            return TypedResults.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        if (await db.SignInTunes.FirstOrDefaultAsync(tune => tune.UserId == id, cancellationToken) is { } existing)
        {
            existing.Content = content;
            existing.ContentType = contentType;
            existing.UpdatedAt = clock.GetUtcNow();
        }
        else
        {
            // A new tune starts on: that's the point of the joke. The person can turn it off.
            db.SignInTunes.Add(new SignInTune { UserId = id, Content = content, ContentType = contentType, UpdatedAt = clock.GetUtcNow() });
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetForOwnerAsync(Guid id, HttpContext context, HortiplannerDbContext db, CancellationToken cancellationToken) =>
        await db.SignInTunes.AsNoTracking().FirstOrDefaultAsync(tune => tune.UserId == id, cancellationToken) is { } tune
            ? Play(context, tune)
            : TypedResults.NotFound();

    private static async Task<IResult> RemoveAsync(Guid id, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        await db.SignInTunes.Where(tune => tune.UserId == id).ExecuteDeleteAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>The signed-in person's own tune, unless they've turned it off.</summary>
    private static async Task<IResult> GetOwnAsync(ClaimsPrincipal user, HttpContext context, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        var me = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

        return await db.SignInTunes.AsNoTracking().FirstOrDefaultAsync(tune => tune.UserId == me && !tune.Off, cancellationToken) is { } tune
            ? Play(context, tune)
            : TypedResults.NotFound();
    }

    private static async Task<IResult> TurnOnOrOffAsync(TuneSwitch request, ClaimsPrincipal user, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        var me = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var changed = await db.SignInTunes
            .Where(tune => tune.UserId == me)
            .ExecuteUpdateAsync(set => set.SetProperty(tune => tune.Off, !request.On), cancellationToken);

        return changed == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    // Phones ask for music in ranges, so ranges must be answered. Never kept: the owner can change it at any time.
    private static Microsoft.AspNetCore.Http.HttpResults.FileContentHttpResult Play(HttpContext context, SignInTune tune)
    {
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.CacheControl = "private, no-store";
        return TypedResults.File(tune.Content, tune.ContentType, enableRangeProcessing: true);
    }

    internal sealed record TuneSwitch(bool On);
}
