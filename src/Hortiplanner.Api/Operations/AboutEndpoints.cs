using System.Runtime.InteropServices;
using Hortiplanner.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Operations;

/// <summary>
/// Which Hortiplanner this is and where its source code is. Hortiplanner is under the GNU AGPL, so everyone using it over the
/// network is offered the source of the version they are using. The image build sets Build:Version, Build:Commit
/// and Build:Date.
/// </summary>
internal static class AboutEndpoints
{
    public const string Source = "https://github.com/Jellman86/hortiplanner";

    public static void MapAboutEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/about", GetAboutAsync).RequireAuthorization();

    private static async Task<About> GetAboutAsync(IConfiguration configuration, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        static string? Setting(IConfiguration configuration, string key) =>
            configuration[key] is { Length: > 0 } value ? value.Trim() : null;

        var commit = Setting(configuration, "Build:Commit");
        var built = DateTimeOffset.TryParse(
            Setting(configuration, "Build:Date"),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal,
            out var date)
            ? date
            : (DateTimeOffset?)null;
        var database = await db.Database
            .SqlQuery<string>($"SELECT current_setting('server_version') AS \"Value\"")
            .SingleAsync(cancellationToken);

        return new About(
            Setting(configuration, "Build:Version") ?? "development",
            commit,
            built,
            commit is null ? Source : $"{Source}/tree/{commit}",
            RuntimeInformation.FrameworkDescription,
            $"PostgreSQL {database.Split(' ')[0]}");
    }

    /// <summary>The running version, the exact source it was built from, and what it runs on.</summary>
    internal sealed record About(string Version, string? Commit, DateTimeOffset? BuiltAt, string SourceUrl, string Runtime, string Database);
}
