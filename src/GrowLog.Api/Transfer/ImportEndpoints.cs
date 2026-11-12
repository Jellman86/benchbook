using GrowLog.Api.Accounts;
using GrowLog.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrowLog.Api.Transfer;

/// <summary>
/// Plants, suppliers and customers brought in from a spreadsheet. The browser reads the file and matches its
/// columns; here each row is checked, and nothing is saved while any row has a problem. A row for something
/// already recorded is left as it is, so the same spreadsheet can be brought in twice.
/// </summary>
internal static class ImportEndpoints
{
    private const int MostRows = 2000;

    public static void MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        var imports = app.MapGroup("/api/import").RequireAuthorization(AccountServices.OwnerPolicy);
        imports.MapGet("/fields", () => ImportFields.All);
        imports.MapPost("/{kind}", ImportAsync).Produces<ImportResult>().Produces<ImportResult>(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> ImportAsync(ImportKind kind, ImportRequest request, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (request.Rows is not { Count: > 0 and <= MostRows } || request.Rows.Any(row => row?.Values is null))
        {
            return TypedResults.BadRequest();
        }

        var existing = await ExistingAsync(kind, db, cancellationToken);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var problems = new List<ImportProblem>();
        var already = new List<int>();
        var adding = new List<object>();

        foreach (var row in request.Rows)
        {
            var read = new RowReader(row.Values, kind);
            var built = Build(kind, read);

            if (read.Problems.Count > 0 || built is null)
            {
                problems.Add(new ImportProblem(row.Row, string.Join(" ", read.Problems)));
            }
            else if (existing.Contains(built.Value.Key))
            {
                already.Add(row.Row);
            }
            else if (!seen.Add(built.Value.Key))
            {
                problems.Add(new ImportProblem(row.Row, $"{built.Value.Key.Replace('\u001f', ' ').Trim()} is on an earlier row too."));
            }
            else
            {
                adding.Add(built.Value.Entry);
            }
        }

        var result = new ImportResult(adding.Count, already, problems, Saved: false);

        if (!request.Save || problems.Count > 0 || adding.Count == 0)
        {
            return TypedResults.Ok(result);
        }

        return await SaveAsync(db, adding, result, cancellationToken);
    }

    private static async Task<IResult> SaveAsync(GrowLogDbContext db, List<object> adding, ImportResult result, CancellationToken cancellationToken)
    {
        db.AddRange(adding);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok(result with { Saved = true });
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Someone added one of them in the meantime. Checking again names it.
            return TypedResults.Conflict(result);
        }
    }

    /// <summary>What's already recorded, by the same key a row is matched on.</summary>
    private static async Task<HashSet<string>> ExistingAsync(ImportKind kind, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        var keys = kind switch
        {
            ImportKind.Plants => (await db.Plants.Select(plant => new { plant.CommonName, plant.Variety }).ToListAsync(cancellationToken))
                .Select(plant => PlantKey(plant.CommonName, plant.Variety)),
            ImportKind.Suppliers => await db.Suppliers.Select(supplier => supplier.Name).ToListAsync(cancellationToken),
            _ => await db.Customers.Select(customer => customer.Name).ToListAsync(cancellationToken),
        };

        return keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string PlantKey(string commonName, string? variety) => $"{commonName}\u001f{variety}";

    private static (string Key, object Entry)? Build(ImportKind kind, RowReader read) => kind switch
    {
        ImportKind.Plants => read.Required("commonName") is { } name && read.Text("variety") is var variety ? (PlantKey(name, variety), new Plant
        {
            CommonName = name,
            Variety = variety,
            BotanicalName = read.Text("botanicalName"),
            Category = read.Text("category"),
            Position = read.Text("position"),
            Watering = read.Text("watering"),
            Hardiness = read.Text("hardiness"),
            XylellaHost = read.YesNo("xylellaHost"),
            ProtectedVariety = read.YesNo("protectedVariety") ?? false,
        }) : null,
        ImportKind.Suppliers => read.Required("name") is { } name ? (name, new Supplier
        {
            Name = name,
            ContactName = read.Text("contactName"),
            Phone = read.Text("phone"),
            Email = read.Text("email"),
            Address = read.Text("address"),
            AccountReference = read.Text("accountReference"),
            Supplies = read.Text("supplies"),
            TypicalLeadTimeDays = read.Number("typicalLeadTimeDays", 0, 3650),
            PassportRegistrationNumber = read.Text("passportRegistrationNumber"),
            OtherRegistrations = read.Text("otherRegistrations"),
            Notes = read.Text("notes"),
        }) : null,
        _ => read.Required("name") is { } name ? (name, new Customer
        {
            Name = name,
            Address = read.Text("address"),
            Email = read.Text("email"),
            Phone = read.Text("phone"),
            VatNumber = read.Text("vatNumber"),
        }) : null,
    };

    internal sealed record ImportRow(int Row, IReadOnlyDictionary<string, string?> Values);

    internal sealed record ImportRequest(IReadOnlyList<ImportRow> Rows, bool Save);

    internal sealed record ImportProblem(int Row, string Problem);

    internal sealed record ImportResult(int Adding, IReadOnlyList<int> AlreadyThere, IReadOnlyList<ImportProblem> Problems, bool Saved);
}
