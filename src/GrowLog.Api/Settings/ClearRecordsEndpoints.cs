using GrowLog.Api.Accounts;
using GrowLog.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Settings;

/// <summary>
/// Lets the owner empty the nursery's records and start afresh after trying GrowLog out. Only while Demo:Enabled is set,
/// because real plant passport records must be kept for three years. Sign-ins, staff, bench devices, nursery details,
/// label styles and the plant lookup cache are kept.
/// </summary>
internal static class ClearRecordsEndpoints
{
    /// <summary>What the owner types to show they mean it.</summary>
    public const string Confirmation = "clear everything";

    /// <summary>Every table of nursery records. Listed together so PostgreSQL refuses if a table that refers to them is left out.</summary>
    private static readonly string[] RecordTables =
    [
        "invoice_lines", "invoices", "customers",
        "till_takings", "purchase_receipts", "purchase_lines", "purchases", "received_passports", "issued_passports", "package_lines", "consignment_packages", "consignments",
        "count_adjustments", "event_lines", "events", "batch_photos", "containers", "batches", "mother_plants",
        "plant_notes", "plant_photos", "plant_prices", "plants", "catalogue_entry_photos", "catalogue_changes",
        "jobs_done", "jobs", "sensor_readings", "sensors", "pot_sizes", "suppliers", "locations",
    ];

    private static readonly string[] Numbering = ["batch_numbers", "container_numbers", "place_numbers"];

    /// <summary>
    /// The append-only records refuse to be removed, so their guards are lifted for the one statement that empties them.
    /// The whole script runs in a transaction, so a failure leaves every guard and record as it was.
    /// </summary>
    private static readonly string ClearScript = string.Join(
        ";\n",
        [
            .. RecordTables.Select(table => "ALTER TABLE " + table + " DISABLE TRIGGER USER"),
            "TRUNCATE " + string.Join(", ", RecordTables),
            .. RecordTables.Select(table => "ALTER TABLE " + table + " ENABLE TRIGGER USER"),
            .. Numbering.Select(sequence => "ALTER SEQUENCE " + sequence + " RESTART"),
        ]);

    private static readonly string[] ExamplePeople = ["Example Worker", "Sample Helper"];

    public static void MapClearRecordsEndpoints(this IEndpointRouteBuilder app) =>
        app.MapPost("/api/settings/clear-records", ClearAsync).RequireAuthorization(AccountServices.OwnerPolicy);

    private static async Task<IResult> ClearAsync(ClearRecordsRequest request, IConfiguration configuration, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Demo:Enabled"))
        {
            return TypedResults.NotFound();
        }

        if (!string.Equals(request.Confirmation?.Trim(), Confirmation, StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.BadRequest();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync(ClearScript, cancellationToken);

        // The made-up people from the example data have no way in, so they go too.
        await db.Users.Where(user => ExamplePeople.Contains(user.UserName) && user.ArchivedAt != null).ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    internal sealed record ClearRecordsRequest(string? Confirmation);
}
