using System.Security.Claims;
using System.Text.Json;
using GrowLog.Api.Accounts;
using GrowLog.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Passports;

/// <summary>
/// Consignments: sales and movements of plants in packages. GrowLog says whether they need a plant passport, and
/// issues one for each package covering every batch in it, after the plants have been examined. Recording a
/// consignment doesn't take plants from the trays; an invoice line taking them from a tray does that.
/// </summary>
internal static class ConsignmentEndpoints
{
    private const int LongestText = 200;
    private const int MostLines = 100;

    public static void MapConsignmentEndpoints(this IEndpointRouteBuilder app)
    {
        var consignments = app.MapGroup("/api/consignments").RequireAuthorization(AccountServices.OwnerPolicy);
        consignments.MapGet("/", ListAsync);
        consignments.MapGet("/{id:guid}", GetAsync).Produces<ConsignmentDetails>();
        consignments.MapPost("/", AddAsync).Produces<ConsignmentAdded>(StatusCodes.Status201Created);
        consignments.MapPost("/{id:guid}/packages/{packageId:guid}/passport", IssueAsync).Produces<PassportRefused>(StatusCodes.Status409Conflict);
    }

    /// <summary>Whether plants going this way need a UK plant passport, and why, in words.</summary>
    internal static (bool Needed, string Why) PassportNeeded(ConsignmentKind kind) => kind switch
    {
        ConsignmentKind.TradeSale => (true, "Plants moved to another business need a UK plant passport."),
        ConsignmentKind.DistanceSale => (true, "Plants sold at a distance (post, courier or online) need a UK plant passport, even to a home gardener."),
        ConsignmentKind.OwnSite => (false, "Moving plants between your own sites usually needs no passport. If the sites are far apart, check with your plant health inspector."),
        _ => (false, "Plants sold in person to someone who will grow them themselves need no passport."),
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task<List<ConsignmentSummary>> ListAsync(GrowLogDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.Consignments
            .AsNoTracking()
            .OrderByDescending(consignment => consignment.Number)
            .Select(consignment => new
            {
                consignment.Id,
                consignment.Number,
                consignment.Kind,
                consignment.SentTo,
                consignment.SentOn,
                Packages = consignment.Packages.Count,
                Passported = consignment.Packages.Count(package => db.IssuedPassports.Any(passport => passport.PackageId == package.Id)),
            })
            .ToListAsync(cancellationToken);

        return rows.ConvertAll(row => new ConsignmentSummary(row.Id, row.Number, row.Kind, row.SentTo, row.SentOn, row.Packages, PassportNeeded(row.Kind).Needed, row.Passported));
    }

    private static async Task<IResult> GetAsync(Guid id, GrowLogDbContext db, CancellationToken cancellationToken) =>
        await DescribeAsync(id, db, cancellationToken) is { } details ? TypedResults.Ok(details) : TypedResults.NotFound();

    private static async Task<ConsignmentDetails?> DescribeAsync(Guid id, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Consignments.AsNoTracking().Include(item => item.Packages).ThenInclude(package => package.Lines).FirstOrDefaultAsync(item => item.Id == id, cancellationToken) is not { } consignment)
        {
            return null;
        }

        var batchIds = consignment.Packages.SelectMany(package => package.Lines).Select(line => line.BatchId).Distinct().ToList();
        var containerIds = consignment.Packages.SelectMany(package => package.Lines).Select(line => line.ContainerId).Distinct().ToList();
        var packageIds = consignment.Packages.Select(package => package.Id).ToList();

        var batches = await db.Batches
            .Where(batch => batchIds.Contains(batch.Id))
            .Join(db.Plants, batch => batch.PlantId, plant => plant.Id, (batch, plant) => new { batch.Id, batch.TraceabilityCode, plant.CommonName, plant.Variety })
            .ToDictionaryAsync(batch => batch.Id, cancellationToken);
        var labels = await db.Containers.Where(container => containerIds.Contains(container.Id)).ToDictionaryAsync(container => container.Id, container => container.LabelCode, cancellationToken);
        var passports = await db.IssuedPassports.AsNoTracking().Where(passport => passport.PackageId != null && packageIds.Contains(passport.PackageId.Value)).ToListAsync(cancellationToken);

        var (needed, why) = PassportNeeded(consignment.Kind);

        return new ConsignmentDetails(
            consignment.Id,
            consignment.Number,
            consignment.Kind,
            consignment.SentTo,
            consignment.SentOn,
            consignment.Notes,
            needed,
            why,
            [.. consignment.Packages.OrderBy(package => package.Number).Select(package => new PackageDetails(
                package.Id,
                package.Number,
                [.. package.Lines.Select(line => new PackageLineDetails(
                    line.BatchId,
                    batches[line.BatchId].TraceabilityCode,
                    batches[line.BatchId].Variety is { } variety ? $"{batches[line.BatchId].CommonName} '{variety}'" : batches[line.BatchId].CommonName,
                    labels[line.ContainerId],
                    line.Quantity))],
                Combined(passports.Where(passport => passport.PackageId == package.Id).ToList())))]);
    }

    /// <summary>
    /// One passport for a whole package: every species in part A and every batch's code in part C, so each code
    /// leads back to a batch in the package. Null until it's issued.
    /// </summary>
    internal static PackagePassport? Combined(List<IssuedPassport> unordered)
    {
        var passports = unordered.OrderBy(passport => passport.PartC, StringComparer.Ordinal).ToList();

        return passports.Count == 0
            ? null
            : new PackagePassport(
                string.Join("; ", passports.Select(passport => passport.PartA).Distinct(StringComparer.Ordinal)),
                passports[0].PartB,
                string.Join(", ", passports.Select(passport => passport.PartC).Distinct(StringComparer.Ordinal)),
                string.Join(", ", passports.Select(passport => passport.PartD).Distinct(StringComparer.Ordinal)),
                [.. passports.Select(passport => $"{passport.PartC}: {passport.OriginRule}")],
                passports.Min(passport => passport.IssuedOn));
    }

    private static async Task<IResult> AddAsync(ConsignmentRequest request, ClaimsPrincipal user, GrowLogDbContext db, EventStore store, TimeProvider clock, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var sentTo = request.CustomerId is { } customerId
            ? await db.Customers.Where(customer => customer.Id == customerId).Select(customer => customer.Name).FirstOrDefaultAsync(cancellationToken)
            : Clean(request.SentTo);
        var lines = request.Packages.SelectMany(package => package.Lines).ToList();

        if (sentTo is not { Length: <= LongestText }
            || request.Packages.Count == 0
            || request.Packages.Any(package => package.Lines.Count == 0)
            || lines.Count > MostLines
            || lines.Any(line => line.Quantity <= 0)
            || (request.SentOn ?? today) > today
            || (request.Notes?.Length ?? 0) > LongestText * 5)
        {
            return TypedResults.BadRequest();
        }

        // Every line is plants that are in that tray now.
        foreach (var container in lines.GroupBy(line => line.ContainerId))
        {
            var contents = (await store.GetContainerStateAsync(container.Key, cancellationToken)).Contents;

            if (container.GroupBy(line => line.BatchId).Any(batch => (contents.FirstOrDefault(item => item.BatchId == batch.Key)?.Quantity ?? 0) < batch.Sum(line => line.Quantity)))
            {
                return TypedResults.Conflict(new PassportRefused("TooMany"));
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE consignments IN EXCLUSIVE MODE", cancellationToken);
        var number = (await db.Consignments.MaxAsync(item => (int?)item.Number, cancellationToken) ?? 0) + 1;

        var consignment = new Consignment
        {
            Number = number,
            Kind = request.Kind,
            SentTo = sentTo,
            CustomerId = request.CustomerId,
            SentOn = request.SentOn ?? today,
            Notes = Clean(request.Notes),
            CreatedBy = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
        };

        foreach (var (package, index) in request.Packages.Select((package, index) => (package, index)))
        {
            var made = new ConsignmentPackage { Number = index + 1 };

            foreach (var line in package.Lines)
            {
                made.Lines.Add(new PackageLine { BatchId = line.BatchId, ContainerId = line.ContainerId, Quantity = line.Quantity });
            }

            consignment.Packages.Add(made);
        }

        db.Consignments.Add(consignment);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Created($"/api/consignments/{consignment.Id}", new ConsignmentAdded(consignment.Id, number));
    }

    /// <summary>
    /// The passport for one package, after its plants are examined: a passport line for each batch in it, all
    /// belonging to the package. Issued once; asking again finds the one issued.
    /// </summary>
    private static async Task<IResult> IssueAsync(Guid id, Guid packageId, PackagePassportRequest request, ClaimsPrincipal user, GrowLogDbContext db, EventStore store, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!request.Examined)
        {
            return TypedResults.Conflict(new PassportRefused("NotExamined"));
        }

        if ((request.Findings?.Length ?? 0) > LongestText)
        {
            return TypedResults.BadRequest();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM consignment_packages WHERE id = {0} FOR UPDATE", [packageId], cancellationToken);

        var consignment = await db.Consignments.AsNoTracking().Include(item => item.Packages).ThenInclude(package => package.Lines).FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (consignment?.Packages.FirstOrDefault(package => package.Id == packageId) is not { } package)
        {
            return TypedResults.NotFound();
        }

        if (await db.IssuedPassports.AnyAsync(passport => passport.PackageId == packageId, cancellationToken))
        {
            return TypedResults.Ok();
        }

        var (drafts, refused) = await DraftAllAsync(package, db, store, clock, cancellationToken);

        if (refused is not null)
        {
            return TypedResults.Conflict(new PassportRefused(refused));
        }

        var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var examinations = await ExamineAsync(package, request, userId, store, clock, cancellationToken);

        if (examinations is null)
        {
            return TypedResults.Conflict(new PassportRefused("CannotIssue"));
        }

        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        db.IssuedPassports.AddRange(drafts.Select(item => PassportLine(item.Line, item.Draft, consignment, packageId, today, examinations[item.Line.Id], userId)));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Created($"/api/consignments/{id}", new { packageId });
    }

    private static IssuedPassport PassportLine(PackageLine line, PassportEndpoints.IssueDraft draft, Consignment consignment, Guid packageId, DateOnly today, Guid examinationId, Guid userId) => new()
    {
        BatchId = line.BatchId,
        ContainerId = line.ContainerId,
        Quantity = line.Quantity,
        PartA = draft.PartA!,
        PartB = draft.PartB!,
        PartC = draft.PartC,
        PartD = draft.PartD,
        OriginRule = draft.OriginRule,
        CustomerId = consignment.CustomerId,
        PackageId = packageId,
        IssuedTo = consignment.SentTo,
        IssuedOn = today,
        ExaminationEventId = examinationId,
        IssuedBy = userId,
    };

    /// <summary>Each line's passport as it would be issued now, or why the package can't have one.</summary>
    private static async Task<(List<(PackageLine Line, PassportEndpoints.IssueDraft Draft)> Drafts, string? Refused)> DraftAllAsync(
        ConsignmentPackage package,
        GrowLogDbContext db,
        EventStore store,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var drafts = new List<(PackageLine Line, PassportEndpoints.IssueDraft Draft)>();

        foreach (var line in package.Lines)
        {
            var draft = await PassportEndpoints.MakeDraftAsync(line.BatchId, line.ContainerId, db, store, clock, cancellationToken);

            if (draft is null || draft.Problems.Count > 0 || draft.Available < line.Quantity)
            {
                return ([], draft is { Problems.Count: 0 } ? "TooMany" : "CannotIssue");
            }

            drafts.Add((line, draft));
        }

        return (drafts, null);
    }

    /// <summary>
    /// The examination before the issue, recorded on each tray in the package, and which examination each line
    /// rests on. Each passport line keeps its own examination, as a passport issued alone does.
    /// </summary>
    private static async Task<Dictionary<Guid, Guid>?> ExamineAsync(ConsignmentPackage package, PackagePassportRequest request, Guid userId, EventStore store, TimeProvider clock, CancellationToken cancellationToken)
    {
        var examinations = new Dictionary<Guid, Guid>();

        foreach (var line in package.Lines)
        {
            var examined = new Event
            {
                Id = Guid.CreateVersion7(),
                Type = EventType.Examined,
                OccurredAt = clock.GetUtcNow(),
                RecordedBy = userId,
                ContainerId = line.ContainerId,
                Payload = JsonSerializer.Serialize(new { findings = Clean(request.Findings) ?? "No pests or diseases seen", forPassport = true }),
            };

            if (await store.AppendAsync(examined, cancellationToken) != AppendResult.Stored)
            {
                return null;
            }

            examinations[line.Id] = examined.Id;
        }

        return examinations;
    }

    internal sealed record ConsignmentLineRequest(Guid ContainerId, Guid BatchId, int Quantity);

    internal sealed record PackageRequest(IReadOnlyList<ConsignmentLineRequest> Lines);

    /// <param name="SentTo">Who or where it went, when it isn't one of the owner's customers.</param>
    /// <param name="SentOn">The day it went, if not today.</param>
    internal sealed record ConsignmentRequest(ConsignmentKind Kind, string? SentTo, Guid? CustomerId, DateOnly? SentOn, string? Notes, IReadOnlyList<PackageRequest> Packages);

    internal sealed record ConsignmentAdded(Guid Id, int Number);

    internal sealed record PackagePassportRequest(bool Examined, string? Findings);

    internal sealed record PassportRefused(string Problem);

    internal sealed record ConsignmentSummary(Guid Id, int Number, ConsignmentKind Kind, string SentTo, DateOnly SentOn, int Packages, bool PassportNeeded, int PackagesPassported);

    internal sealed record PackageLineDetails(Guid BatchId, string TraceabilityCode, string PlantName, string LabelCode, int Quantity);

    /// <param name="Rules">How each batch's country of origin was worked out.</param>
    internal sealed record PackagePassport(string PartA, string PartB, string PartC, string PartD, IReadOnlyList<string> Rules, DateOnly IssuedOn);

    internal sealed record PackageDetails(Guid Id, int Number, IReadOnlyList<PackageLineDetails> Lines, PackagePassport? Passport);

    internal sealed record ConsignmentDetails(
        Guid Id,
        int Number,
        ConsignmentKind Kind,
        string SentTo,
        DateOnly SentOn,
        string? Notes,
        bool PassportNeeded,
        string Why,
        IReadOnlyList<PackageDetails> Packages);
}
