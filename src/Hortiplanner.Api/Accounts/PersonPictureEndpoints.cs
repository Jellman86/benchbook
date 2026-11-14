using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using Hortiplanner.Api.Data;
using Hortiplanner.Api.Lookup;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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

    private static readonly TimeSpan DownloadDeadline = TimeSpan.FromSeconds(5);

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

        await SaveUploadAsync(id, image, db, clock, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> RemoveAsync(Guid id, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        await db.PersonPictures.Where(picture => picture.UserId == id).ExecuteDeleteAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Saves an uploaded picture in one statement, replacing whatever is there, so a staff-login sign-in at the same
    /// moment can never make the upload fail or win over it.
    /// </summary>
    private static async Task SaveUploadAsync(Guid id, ImageFile image, HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var uploaded = nameof(PersonPictureSource.Uploaded);
        var now = clock.GetUtcNow();

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO person_picture (user_id, content_type, content, source, source_url, updated_at)
            VALUES ({id}, {image.ContentType}, {image.Content}, {uploaded}, NULL, {now})
            ON CONFLICT (user_id) DO UPDATE SET
                content_type = EXCLUDED.content_type,
                content = EXCLUDED.content,
                source = EXCLUDED.source,
                source_url = NULL,
                updated_at = EXCLUDED.updated_at
            """,
            cancellationToken);
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
        // The whole download, not just its first reply, has a deadline: a slow picture never holds up signing in.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(DownloadDeadline);

        try
        {
            var existing = await db.PersonPictures.AsNoTracking().FirstOrDefaultAsync(picture => picture.UserId == id, cancellationToken);

            if (existing?.Source == PersonPictureSource.Uploaded)
            {
                return;
            }

            if (!Uri.TryCreate(pictureUrl, UriKind.Absolute, out var url) || !string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
            {
                // Only ever the login's own copy: an upload made meanwhile stays.
                await db.PersonPictures
                    .Where(picture => picture.UserId == id && picture.Source == PersonPictureSource.StaffLogin)
                    .ExecuteDeleteAsync(cancellationToken);
                return;
            }

            if (string.Equals(existing?.SourceUrl, url.AbsoluteUri, StringComparison.Ordinal))
            {
                return;
            }

            if (await DownloadAsync(url, clients, deadline.Token) is { } image)
            {
                await SaveFromLoginAsync(id, image, url.AbsoluteUri, db, clock, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException or DbUpdateException)
        {
            LogPictureNotTaken(logger, exception.GetType().Name);
        }
    }

    /// <summary>
    /// Keeps the login's picture without ever replacing one the person uploaded, even one uploaded while this was
    /// downloading: the update only touches a row the login owns, and a new row loses to one inserted meanwhile.
    /// </summary>
    private static async Task SaveFromLoginAsync(Guid id, ImageFile image, string sourceUrl, HortiplannerDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var updated = await db.PersonPictures
            .Where(picture => picture.UserId == id && picture.Source == PersonPictureSource.StaffLogin)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(picture => picture.Content, image.Content)
                    .SetProperty(picture => picture.ContentType, image.ContentType)
                    .SetProperty(picture => picture.SourceUrl, sourceUrl)
                    .SetProperty(picture => picture.UpdatedAt, now),
                cancellationToken);

        if (updated > 0 || await db.PersonPictures.AnyAsync(picture => picture.UserId == id, cancellationToken))
        {
            return;
        }

        db.PersonPictures.Add(new PersonPicture
        {
            UserId = id,
            Content = image.Content,
            ContentType = image.ContentType,
            Source = PersonPictureSource.StaffLogin,
            SourceUrl = sourceUrl,
            UpdatedAt = now,
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // An upload got there first, and it wins.
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// The picture client's connection: no redirects, and only to public addresses, checked on the address actually
    /// connected to, so a name that later resolves somewhere private (DNS rebinding) is refused too. A picture link can
    /// never be used to reach the nursery's own network or this server.
    /// </summary>
    public static SocketsHttpHandler PublicOnlyHandler() => new()
    {
        AllowAutoRedirect = false,

        // A proxy would make the destination its business, past the check below, so pictures never go through one.
        UseProxy = false,
        ConnectCallback = async (context, cancellationToken) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
            var target = addresses.FirstOrDefault(IsPublic)
                ?? throw new HttpRequestException($"{context.DnsEndPoint.Host} isn't a public address.");

            var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

            try
            {
                await socket.ConnectAsync(new IPEndPoint(target, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    /// <summary>Whether an address is on the public internet: not loopback, private, link-local, shared or multicast.</summary>
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var first = address.GetAddressBytes()[0];
            return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || (first & 0xFE) == 0xFC);
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] switch
        {
            0 or 10 or 127 => false,
            100 when bytes[1] is >= 64 and <= 127 => false, // Shared address space, including Tailscale.
            169 when bytes[1] == 254 => false,
            172 when bytes[1] is >= 16 and <= 31 => false,
            192 when bytes[1] == 168 => false,
            192 when bytes[1] == 0 && bytes[2] is 0 or 2 => false,
            198 when bytes[1] is 18 or 19 => false,
            >= 224 => false,
            _ => true,
        };
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
