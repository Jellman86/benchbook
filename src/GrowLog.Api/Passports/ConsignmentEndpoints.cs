using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.Json;
using GrowLog.Api.Accounts;
using GrowLog.Api.Data;
using GrowLog.Api.Settings;
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

    /// <summary>A consignment can be made ready up to a fortnight before it goes.</summary>
    private const int MostDaysAhead = 14;

    public static void MapConsignmentEndpoints(this IEndpointRouteBuilder app)
    {
        var consignments = app.MapGroup("/api/consignments").RequireAuthorization(AccountServices.OwnerPolicy).RequireFeature(NurseryFeature.Passports);
        consignments.MapGet("/", ListAsync);
        consignments.MapGet("/invoiced", InvoicedAsync);
        consignments.MapGet("/{id:guid}", GetAsync).Produces<ConsignmentDetails>();
        consignments.MapPost("/", AddAsync).Produces<ConsignmentAdded>(StatusCodes.Status201Created);
        consignments.MapDelete("/{id:guid}", RemoveAsync).Produces<PassportRefused>(StatusCodes.Status409Conflict);
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
        var invoiceLineIds = consignment.Packages.SelectMany(package => package.Lines).Select(line => line.InvoiceLineId).OfType<Guid>().ToList();
        var invoiceNumbers = await db.InvoiceLines
            .Where(line => invoiceLineIds.Contains(line.Id))
            .Join(db.Invoices, line => line.InvoiceId, invoice => invoice.Id, (line, invoice) => new { line.Id, invoice.Number })
            .ToDictionaryAsync(row => row.Id, row => row.Number, cancellationToken);

        var (needed, why) = PassportNeeded(consignment.Kind);

        var propagating = new HashSet<(Guid BatchId, Guid ContainerId)>();
        foreach (var line in consignment.Packages.SelectMany(package => package.Lines))
        {
            var batch = await db.Batches.AsNoTracking().SingleAsync(batch => batch.Id == line.BatchId, cancellationToken);
            if (await PassportEndpoints.PropagatingAsync(batch, line.ContainerId, db, cancellationToken))
            {
                propagating.Add((line.BatchId, line.ContainerId));
            }
        }
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
                    line.Quantity,
                    line.InvoiceLineId is { } invoiceLine ? invoiceNumbers.GetValueOrDefault(invoiceLine) : null,
                    propagating.Contains((line.BatchId, line.ContainerId))))],
                Combined(passports.Where(passport => passport.PackageId == package.Id).ToList())))]);
    }

    /// <summary>
    /// One passport for a whole package: part B once, then a line for each batch with its own botanical name (A),
    /// code (C) and origin (D), so each code and origin stays with its plant. The same batch from two trays is one
    /// line. Null until it's issued.
    /// </summary>
    internal static PackagePassport? Combined(List<IssuedPassport> passports) =>
        passports.Count == 0
            ? null
            : new PackagePassport(
                passports[0].PartB,
                [.. passports
                    .GroupBy(passport => (passport.PartA, passport.PartC, passport.PartD, passport.OriginRule))
                    .Select(group => new PackagePassportLine(group.Key.PartA, group.Key.PartC, group.Key.PartD, group.Key.OriginRule, group.Sum(passport => passport.Quantity)))
                    .OrderBy(line => line.PartC, StringComparer.Ordinal)],
                passports.Min(passport => passport.IssuedOn));

    private static async Task<IResult> AddAsync(ConsignmentRequest request, ClaimsPrincipal user, GrowLogDbContext db, EventStore store, TimeProvider clock, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await store.LockRequestAsync(request.Id ?? Guid.CreateVersion7(), cancellationToken);
        // Sent again after a reply was lost: the consignment made the first time.
        if (request.Id is { } given && await db.Consignments.AsNoTracking().FirstOrDefaultAsync(item => item.Id == given, cancellationToken) is { } already)
        {
            return already.CreatedBy == Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!)
                && string.Equals(already.RequestFingerprint, RequestFingerprint.For(request), StringComparison.Ordinal)
                ? TypedResults.Ok(new ConsignmentAdded(already.Id, already.Number)) : TypedResults.Conflict(new PassportRefused("IdInUse"));
        }

        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var sentTo = request.CustomerId is { } customerId
            ? await db.Customers.Where(customer => customer.Id == customerId).Select(customer => customer.Name).FirstOrDefaultAsync(cancellationToken)
            : Clean(request.SentTo);
        var lines = request.Packages.SelectMany(package => package.Lines).ToList();

        if (!IsValid(request, sentTo, lines, today))
        {
            return TypedResults.BadRequest();
        }

        if (await ResolveInvoicedAsync(request.Packages, db, cancellationToken) is not { } packages)
        {
            return TypedResults.BadRequest();
        }

        if (!await InTraysAsync([.. packages.SelectMany(package => package).Where(line => line.InvoiceLineId is null)], store, cancellationToken))
        {
            return TypedResults.Conflict(new PassportRefused("TooMany"));
        }

        await db.Database.ExecuteSqlRawAsync("LOCK TABLE consignments IN EXCLUSIVE MODE", cancellationToken);
        var number = (await db.Consignments.MaxAsync(item => (int?)item.Number, cancellationToken) ?? 0) + 1;

        if (!await InvoicedLeftAsync([.. packages.SelectMany(package => package).Where(line => line.InvoiceLineId is not null)], db, cancellationToken))
        {
            return TypedResults.Conflict(new PassportRefused("TooMany"));
        }

        var consignment = new Consignment
        {
            Id = request.Id ?? Guid.CreateVersion7(),
            RequestFingerprint = RequestFingerprint.For(request),
            Number = number,
            Kind = request.Kind,
            SentTo = sentTo,
            CustomerId = request.CustomerId,
            SentOn = request.SentOn ?? today,
            Notes = Clean(request.Notes),
            CreatedBy = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
        };

        AddPackages(consignment, packages);

        db.Consignments.Add(consignment);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Created($"/api/consignments/{consignment.Id}", new ConsignmentAdded(consignment.Id, number));
    }

    /// <summary>Plants on invoices that can still go in a consignment: on an invoice not cancelled, from a tray.</summary>
    private static IQueryable<InvoiceLine> Consignable(GrowLogDbContext db) =>
        db.InvoiceLines.Where(line => line.BatchId != null
            && line.ContainerId != null
            && db.Invoices.Any(invoice => invoice.Id == line.InvoiceId
                && invoice.Kind == InvoiceKind.Invoice
                && !db.Invoices.Any(credit => credit.CreditsInvoiceId == invoice.Id)));

    /// <summary>
    /// Plants already invoiced and so gone from their trays, with how many of each line aren't in a consignment yet:
    /// a consignment, and its passports, can still be made for them.
    /// </summary>
    private static async Task<List<InvoicedPlants>> InvoicedAsync(GrowLogDbContext db, CancellationToken cancellationToken)
    {
        var lines = await Consignable(db)
            .Join(db.Invoices, line => line.InvoiceId, invoice => invoice.Id, (line, invoice) => new { Line = line, invoice.Number, invoice.CustomerId, invoice.CustomerName, invoice.IssuedOn })
            .Join(db.Batches, row => row.Line.BatchId, batch => batch.Id, (row, batch) => new { row.Line, row.Number, row.CustomerId, row.CustomerName, row.IssuedOn, batch.TraceabilityCode, batch.PlantId })
            .Join(db.Plants, row => row.PlantId, plant => plant.Id, (row, plant) => new { row.Line, row.Number, row.CustomerId, row.CustomerName, row.IssuedOn, row.TraceabilityCode, plant.CommonName, plant.Variety })
            .Join(db.Containers, row => row.Line.ContainerId, container => container.Id, (row, container) => new { row, container.LabelCode })
            .Select(item => new
            {
                item.row,
                item.LabelCode,
                Sent = db.Set<PackageLine>().Where(sent => sent.InvoiceLineId == item.row.Line.Id).Sum(sent => (int?)sent.Quantity) ?? 0,
            })
            .ToListAsync(cancellationToken);

        return [.. lines
            .Where(item => item.row.Line.Quantity > item.Sent)
            .OrderByDescending(item => item.row.Number)
            .Select(item => new InvoicedPlants(
                item.row.Line.Id,
                item.row.Number,
                item.row.CustomerId,
                item.row.CustomerName,
                item.row.IssuedOn,
                item.row.Line.BatchId!.Value,
                item.row.Line.ContainerId!.Value,
                item.LabelCode,
                item.row.TraceabilityCode,
                item.row.Variety is { } variety ? $"{item.row.CommonName} '{variety}'" : item.row.CommonName,
                item.row.Line.Quantity - item.Sent))];
    }

    /// <summary>
    /// The packages with each invoiced line's tray and batch taken from its invoice line, not from what was sent;
    /// null if one isn't on an invoice that can go.
    /// </summary>
    private static async Task<List<List<ConsignmentLineRequest>>?> ResolveInvoicedAsync(IReadOnlyList<PackageRequest> packages, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        var ids = packages.SelectMany(package => package.Lines).Select(line => line.InvoiceLineId).OfType<Guid>().Distinct().ToList();
        var invoiced = await Consignable(db).Where(line => ids.Contains(line.Id)).ToDictionaryAsync(line => line.Id, cancellationToken);

        if (invoiced.Count != ids.Count)
        {
            return null;
        }

        return [.. packages.Select(package => package.Lines
            .Select(line => line.InvoiceLineId is { } id ? line with { ContainerId = invoiced[id].ContainerId!.Value, BatchId = invoiced[id].BatchId!.Value } : line)
            .ToList())];
    }

    /// <summary>Whether every line not from an invoice is plants that are in that tray now.</summary>
    private static async Task<bool> InTraysAsync(List<ConsignmentLineRequest> lines, EventStore store, CancellationToken cancellationToken)
    {
        foreach (var container in lines.GroupBy(line => line.ContainerId))
        {
            var contents = (await store.GetContainerStateAsync(container.Key, cancellationToken)).Contents;

            if (container.GroupBy(line => line.BatchId).Any(batch => (contents.FirstOrDefault(item => item.BatchId == batch.Key)?.Quantity ?? 0) < batch.Sum(line => line.Quantity)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether each invoice line has that many left that aren't in a consignment already. Checked under the lock.</summary>
    private static async Task<bool> InvoicedLeftAsync(List<ConsignmentLineRequest> lines, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        foreach (var line in lines.GroupBy(line => line.InvoiceLineId!.Value))
        {
            var invoiced = await db.InvoiceLines.Where(item => item.Id == line.Key).Select(item => item.Quantity).FirstAsync(cancellationToken);
            var sent = await db.Set<PackageLine>().Where(item => item.InvoiceLineId == line.Key).SumAsync(item => (int?)item.Quantity, cancellationToken) ?? 0;

            if (line.Sum(item => item.Quantity) > invoiced - sent)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValid(ConsignmentRequest request, [NotNullWhen(true)] string? sentTo, List<ConsignmentLineRequest> lines, DateOnly today) =>
        sentTo is { Length: <= LongestText }
        && request.Packages.Count > 0
        && request.Packages.All(package => package.Lines.Count > 0)
        && lines.Count <= MostLines
        && lines.All(line => line.Quantity > 0)
        && (request.SentOn ?? today) <= today.AddDays(MostDaysAhead)
        && (request.Notes?.Length ?? 0) <= LongestText * 5;

    private static void AddPackages(Consignment consignment, List<List<ConsignmentLineRequest>> packages)
    {
        foreach (var (package, index) in packages.Select((package, index) => (package, index)))
        {
            var made = new ConsignmentPackage { Number = index + 1 };

            foreach (var line in package)
            {
                made.Lines.Add(new PackageLine { BatchId = line.BatchId, ContainerId = line.ContainerId, Quantity = line.Quantity, InvoiceLineId = line.InvoiceLineId });
            }

            consignment.Packages.Add(made);
        }
    }

    /// <summary>
    /// A consignment recorded by mistake, taken away while none of its passports has been issued. Once one has, it
    /// stays: an issued passport is kept for three years with the consignment it went with.
    /// </summary>
    private static async Task<IResult> RemoveAsync(Guid id, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Consignments.Include(item => item.Packages).ThenInclude(package => package.Lines).FirstOrDefaultAsync(item => item.Id == id, cancellationToken) is not { } consignment)
        {
            return TypedResults.NotFound();
        }

        var packages = consignment.Packages.Select(package => package.Id).ToList();

        if (await db.IssuedPassports.AnyAsync(passport => passport.PackageId != null && packages.Contains(passport.PackageId.Value), cancellationToken))
        {
            return TypedResults.Conflict(new PassportRefused("Issued"));
        }

        db.RemoveRange(consignment.Packages.SelectMany(package => package.Lines));
        db.RemoveRange(consignment.Packages);
        db.Consignments.Remove(consignment);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
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

        var issued = await db.IssuedPassports.Where(passport => passport.PackageId == packageId).ToListAsync(cancellationToken);
        if (issued.Count > 0)
        {
            return issued.All(passport => passport.IssuedBy == Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!)
                && string.Equals(passport.RequestFingerprint, RequestFingerprint.For(request), StringComparison.Ordinal))
                ? TypedResults.Ok() : TypedResults.Conflict(new PassportRefused("IdInUse"));
        }
        await LockInvoicesAsync(package, db, cancellationToken);
        await store.LockAsync(package.Lines.Select(line => line.ContainerId), cancellationToken);

        var (drafts, refused) = await DraftAllAsync(package, request.ActiveGrowthConfirmed, db, store, clock, cancellationToken);

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
        db.IssuedPassports.AddRange(drafts.Select(item => PassportLine(item.Line, item.Draft, consignment, packageId, today, examinations[item.Line.Id], userId, request)));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Created($"/api/consignments/{id}", new { packageId });
    }

    private static IssuedPassport PassportLine(PackageLine line, PassportEndpoints.IssueDraft draft, Consignment consignment, Guid packageId, DateOnly today, Guid examinationId, Guid userId, PackagePassportRequest request) => new()
    {
        RequestFingerprint = RequestFingerprint.For(request),
        ActiveGrowthConfirmed = request.ActiveGrowthConfirmed,
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

    private static async Task LockInvoicesAsync(ConsignmentPackage package, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        var lineIds = package.Lines.Select(line => line.InvoiceLineId).OfType<Guid>().ToList();
        var invoiceIds = await db.InvoiceLines.Where(line => lineIds.Contains(line.Id)).Select(line => line.InvoiceId).Distinct().ToArrayAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM invoices WHERE id = ANY({0}) ORDER BY id FOR UPDATE", [invoiceIds], cancellationToken);
    }

    /// <summary>Each line's passport as it would be issued now, or why the package can't have one.</summary>
    private static async Task<(List<(PackageLine Line, PassportEndpoints.IssueDraft Draft)> Drafts, string? Refused)> DraftAllAsync(
        ConsignmentPackage package,
        bool activeGrowthConfirmed,
        GrowLogDbContext db,
        EventStore store,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var drafts = new List<(PackageLine Line, PassportEndpoints.IssueDraft Draft)>();

        var invoiceIds = package.Lines.Where(line => line.InvoiceLineId != null).Select(line => line.InvoiceLineId!.Value).Distinct().ToList();
        if (await Consignable(db).CountAsync(line => invoiceIds.Contains(line.Id), cancellationToken) != invoiceIds.Count)
        {
            return ([], "InvoiceCancelled");
        }
        foreach (var line in package.Lines)
        {
            var sold = line.InvoiceLineId is not null;
            var draft = await PassportEndpoints.MakeDraftAsync(line.BatchId, line.ContainerId, db, store, clock, cancellationToken, sold, activeGrowthConfirmed);

            if (draft is null || draft.Problems.Count > 0 || (!sold && draft.Available < package.Lines.Where(other => other.InvoiceLineId == null && other.BatchId == line.BatchId && other.ContainerId == line.ContainerId).Sum(other => other.Quantity)))
            {
                return ([], draft is { Problems.Count: 0 } ? "TooMany" : "CannotIssue");
            }

            drafts.Add((line, draft));
        }

        return (drafts, null);
    }

    /// <summary>
    /// The examination before the issue, recorded once on each tray in the package, and which examination each line
    /// rests on: every line from a tray rests on that tray's examination.
    /// </summary>
    private static async Task<Dictionary<Guid, Guid>?> ExamineAsync(ConsignmentPackage package, PackagePassportRequest request, Guid userId, EventStore store, TimeProvider clock, CancellationToken cancellationToken)
    {
        var examinations = new Dictionary<Guid, Guid>();

        // One examination for each tray, however many of its batches are in the package.
        foreach (var tray in package.Lines.GroupBy(line => line.ContainerId))
        {
            var examined = new Event
            {
                Id = Guid.CreateVersion7(),
                Type = EventType.Examined,
                OccurredAt = clock.GetUtcNow(),
                RecordedBy = userId,
                ContainerId = tray.Key,
                Payload = JsonSerializer.Serialize(new { findings = Clean(request.Findings) ?? "No pests or diseases seen", forPassport = true }),
            };

            if (await store.AppendAsync(examined, cancellationToken) != AppendResult.Stored)
            {
                return null;
            }

            foreach (var line in tray)
            {
                examinations[line.Id] = examined.Id;
            }
        }

        return examinations;
    }

    /// <param name="InvoiceLineId">Plants already invoiced: their tray and batch come from the invoice line.</param>
    internal sealed record ConsignmentLineRequest(Guid ContainerId, Guid BatchId, int Quantity, Guid? InvoiceLineId = null);

    internal sealed record InvoicedPlants(
        Guid InvoiceLineId,
        int InvoiceNumber,
        Guid CustomerId,
        string CustomerName,
        DateOnly IssuedOn,
        Guid BatchId,
        Guid ContainerId,
        string LabelCode,
        string TraceabilityCode,
        string PlantName,
        int Left);

    internal sealed record PackageRequest(IReadOnlyList<ConsignmentLineRequest> Lines);

    /// <param name="SentTo">Who or where it went, when it isn't one of the owner's customers.</param>
    /// <param name="SentOn">The day it went, if not today.</param>
    /// <param name="Id">Made by the device, so sending it again after a lost reply finds the same consignment.</param>
    internal sealed record ConsignmentRequest(ConsignmentKind Kind, string? SentTo, Guid? CustomerId, DateOnly? SentOn, string? Notes, IReadOnlyList<PackageRequest> Packages, Guid? Id = null);

    internal sealed record ConsignmentAdded(Guid Id, int Number);

    internal sealed record PackagePassportRequest(bool Examined, string? Findings, bool ActiveGrowthConfirmed = false);

    internal sealed record PassportRefused(string Problem);

    internal sealed record ConsignmentSummary(Guid Id, int Number, ConsignmentKind Kind, string SentTo, DateOnly SentOn, int Packages, bool PassportNeeded, int PackagesPassported);

    /// <param name="InvoiceNumber">The invoice the plants were sold on, when they'd left the tray before going.</param>
    internal sealed record PackageLineDetails(Guid BatchId, string TraceabilityCode, string PlantName, string LabelCode, int Quantity, int? InvoiceNumber = null, bool PropagatingMaterial = false);

    /// <param name="Rules">How each batch's country of origin was worked out.</param>
    internal sealed record PackagePassportLine(string PartA, string PartC, string PartD, string Rule, int Quantity);

    internal sealed record PackagePassport(string PartB, IReadOnlyList<PackagePassportLine> Lines, DateOnly IssuedOn);

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
