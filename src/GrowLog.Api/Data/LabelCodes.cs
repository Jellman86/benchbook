using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Data;

/// <summary>Short serial numbers for traceability codes and labels.</summary>
internal static class LabelCodes
{
    public static Task<string> NextBatchAsync(GrowLogDbContext db, CancellationToken cancellationToken) =>
        NextAsync(db, "B", $"SELECT nextval('batch_numbers') AS \"Value\"", cancellationToken);

    public static Task<string> NextContainerAsync(GrowLogDbContext db, CancellationToken cancellationToken) =>
        NextAsync(db, "C", $"SELECT nextval('container_numbers') AS \"Value\"", cancellationToken);

    private static async Task<string> NextAsync(GrowLogDbContext db, string prefix, FormattableString nextValue, CancellationToken cancellationToken)
    {
        var value = (await db.Database.SqlQuery<long>(nextValue).ToListAsync(cancellationToken)).Single();
        return string.Create(CultureInfo.InvariantCulture, $"{prefix}{value:D6}");
    }
}
