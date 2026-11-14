using System.Security.Claims;
using Hortiplanner.Api.Data;
using Hortiplanner.Api.Lookup;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Accounts;

/// <summary>
/// People's pictures: uploaded in Hortiplanner by the person or the owner, or taken from the shared staff login's
/// standard OpenID Connect <c>picture</c> claim when nothing has been uploaded. An uploaded picture always wins.
/// </summary>
internal static partial class PersonPictureEndpoints
{
    public const string ClientName = "person-pictures";

    /// <summary>A picture is shown small, so the staff login's copy needn't be big.</summary>
    private const int MaxDownloadBytes = 2 * 1024 * 1024;

    public static void MapPersonPictureEndpoints(this IEndpointRouteBuilder app)
    {
        // The sign-in screen shows names before anyone signs in, on the private network, so pictures are shown too.
        app.MapGet("/api/people/{id:guid}/picture", GetAsync).AllowAnonymous();

        var own = app.MapGroup("/api/account/picture").AddEndpointFilter<OwnSettingsFilter>();
        own.MapPut("/", (ClaimsPrincipal user, HttpRequest request, HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken) =>
            UploadAsync(Me(user), request, db, clock, cancellationToken));
        own.MapDelete("/", (ClaimsPrincipal user, HortiplannerDbContext db, CancellationToken cancellationToken) =>
            RemoveAsync(Me(user), db, cancellationToken));

        var staff = app.MapGroup("/api/staff/{id:guid}/picture").RequireAuthorization(AccountServices.OwnerPolicy);
        staff.MapPut("/", async (Guid id, HttpRequest request, HortiplannerDbContext db, UserManager<AppUser> users, TimeProvider clock, CancellationToken cancellationToken) =>
            await users.FindByIdAsync(id.ToString()) is { IsTeamAccount: false }
                ? await UploadAsync(id, request, db, clock, cancellationToken)
                : TypedResults.NotFound());
        staff.MapDelete("/", (Guid id, HortiplannerDbContext db, CancellationToken cancellationToken) => RemoveAsync(id, db, cancellationToken));
    }

    private static Guid Me(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static async Task<IResult> GetAsync(Guid id, HttpContext context, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        var picture = await db.PersonPictures.AsNoTracking()
            .Where(picture => picture.UserId == id
                && db.Users.Any(user => user.Id == id && user.ArchivedAt == null && !user.IsTeamAccount))
            .FirstOrDefaultAsync(cancellationToken);

        // Asked for with its version in the address, so a browser may keep it until the picture changes.
        return picture is null
            ? TypedResults.NotFound()
            : SpeciesEndpoints.Picture(context, picture.Content, picture.ContentType, permanent: context.Request.Query.ContainsKey("v"));
    }

    private static async Task<IResult> UploadAsync(Guid id, HttpRequest request, HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var (image, refusal) = await ImageContent.ReadAsync(request.Body, cancellationToken);

        if (image is null)
        {
            return TypedResults.StatusCode(refusal);
        }

        await SaveAsync(id, image, PersonPictureSource.Uploaded, sourceUrl: null, db, clock, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> RemoveAsync(Guid id, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        await db.PersonPictures.Where(picture => picture.UserId == id).ExecuteDeleteAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task SaveAsync(Guid id, ImageFile image, PersonPictureSource source, string? sourceUrl, HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (await db.PersonPictures.FirstOrDefaultAsync(picture => picture.UserId == id, cancellationToken) is { } existing)
        {
            existing.Content = image.Content;
            existing.ContentType = image.ContentType;
            existing.Source = source;
            existing.SourceUrl = sourceUrl;
            existing.UpdatedAt = clock.GetUtcNow();
        }
        else
        {
            db.PersonPictures.Add(new PersonPicture
            {
                UserId = id,
                Content = image.Content,
                ContentType = image.ContentType,
                Source = source,
                SourceUrl = sourceUrl,
                UpdatedAt = clock.GetUtcNow(),
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Keeps a copy of the picture the staff login sends, unless the person has uploaded their own. Only fetched again
    /// when its address changes, and dropped when the login stops sending one. Anything going wrong is logged and
    /// ignored: a picture never stops anyone signing in.
    /// </summary>
    public static async Task TakeFromStaffLoginAsync(
        Guid id,
        string? pictureUrl,
        HortiplannerDbContext db,
        IHttpClientFactory clients,
        TimeProvider clock,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var existing = await db.PersonPictures.AsNoTracking().FirstOrDefaultAsync(picture => picture.UserId == id, cancellationToken);

            if (existing?.Source == PersonPictureSource.Uploaded)
            {
                return;
            }

            if (!Uri.TryCreate(pictureUrl, UriKind.Absolute, out var url) || !string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
            {
                if (existing is not null)
                {
                    await RemoveAsync(id, db, cancellationToken);
                }

                return;
            }

            if (string.Equals(existing?.SourceUrl, url.AbsoluteUri, StringComparison.Ordinal))
            {
                return;
            }

            if (await DownloadAsync(url, clients, cancellationToken) is { } image)
            {
                await SaveAsync(id, image, PersonPictureSource.StaffLogin, url.AbsoluteUri, db, clock, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException or DbUpdateException)
        {
            LogPictureNotTaken(logger, exception.GetType().Name);
        }
    }

    private static async Task<ImageFile?> DownloadAsync(Uri url, IHttpClientFactory clients, CancellationToken cancellationToken)
    {
        using var client = clients.CreateClient(ClientName);
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxDownloadBytes)
        {
            return null;
        }

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var copy = new MemoryStream();
        var buffer = new byte[81920];
        int read;

        while ((read = await body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            copy.Write(buffer, 0, read);

            if (copy.Length > MaxDownloadBytes)
            {
                return null;
            }
        }

        var content = copy.ToArray();
        return ImageContent.DetectType(content) is { } contentType ? new ImageFile(content, contentType) : null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The staff login's picture couldn't be kept: {Kind}")]
    private static partial void LogPictureNotTaken(ILogger logger, string kind);
}
