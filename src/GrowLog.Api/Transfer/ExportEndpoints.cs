using System.Data;
using System.IO.Compression;
using System.Text;
using GrowLog.Api.Accounts;
using GrowLog.Api.Catalogue;
using GrowLog.Api.Data;
using GrowLog.Api.Production;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Transfer;

/// <summary>
/// Everything the nursery has recorded, in one download: a spreadsheet file for each kind of record, so the
/// records can be kept, checked or taken elsewhere. Photos stay in the backups. Owner only, as it names customers.
/// </summary>
internal static class ExportEndpoints
{
    public static void MapExportEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/export", ExportAsync).RequireAuthorization(AccountServices.OwnerPolicy).Produces(StatusCodes.Status200OK, contentType: "application/zip");

    private sealed record Sheet(string Name, IReadOnlyList<string> Header, IEnumerable<object?[]> Rows);

    /// <summary>Names to put in place of ids, loaded once for every sheet.</summary>
    private sealed record Names(
        Dictionary<Guid, (string CommonName, string? Variety)> Plants,
        Dictionary<Guid, string> Suppliers,
        Dictionary<Guid, string> Batches,
        Dictionary<Guid, string> Containers,
        Dictionary<Guid, string> Mothers,
        Dictionary<Guid, string?> People,
        Dictionary<Guid, string> Places)
    {
        public static async Task<Names> LoadAsync(GrowLogDbContext db, CancellationToken cancellationToken) => new(
            await db.Plants.AsNoTracking().ToDictionaryAsync(plant => plant.Id, plant => (plant.CommonName, plant.Variety), cancellationToken),
            await db.Suppliers.AsNoTracking().ToDictionaryAsync(supplier => supplier.Id, supplier => supplier.Name, cancellationToken),
            await db.Batches.AsNoTracking().ToDictionaryAsync(batch => batch.Id, batch => batch.TraceabilityCode, cancellationToken),
            await db.Containers.AsNoTracking().ToDictionaryAsync(container => container.Id, container => container.LabelCode, cancellationToken),
            await db.MotherPlants.AsNoTracking().ToDictionaryAsync(mother => mother.Id, mother => mother.Name, cancellationToken),
            await db.Users.AsNoTracking().ToDictionaryAsync(user => user.Id, user => user.UserName, cancellationToken),
            await PlacePaths.LoadAsync(db, cancellationToken));

        public string? Supplier(Guid? id) => id is { } key ? Suppliers.GetValueOrDefault(key) : null;

        public string? Batch(Guid? id) => id is { } key ? Batches.GetValueOrDefault(key) : null;
    }

    private static async Task<IResult> ExportAsync(GrowLogDbContext db, EventStore store, TimeProvider clock, CancellationToken cancellationToken)
    {
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var names = await Names.LoadAsync(db, cancellationToken);
        Sheet[] sheets =
        [
            await PlantsAsync(db, cancellationToken),
            await SuppliersAsync(db, cancellationToken),
            await CustomersAsync(db, cancellationToken),
            await PlacesAsync(db, names.Places, cancellationToken),
            await BatchesAsync(db, names, cancellationToken),
            await TraysAsync(db, store, cancellationToken),
            await WorkAsync(db, names, cancellationToken),
            await AdjustmentsAsync(db, cancellationToken),
            await LegacyCountsAsync(db, cancellationToken),
            await PurchasesAsync(db, names, cancellationToken),
            await InvoicesAsync(db, names, cancellationToken),
            await TillAsync(db, cancellationToken),
            await PassportsIssuedAsync(db, names, cancellationToken),
            await PassportsReceivedAsync(db, names, cancellationToken),
            await ConsignmentsAsync(db, names, cancellationToken),
            await JobsAsync(db, cancellationToken),
            await MothersAsync(db, names, cancellationToken),
        ];

        using var zipped = new MemoryStream();

        using (var archive = new ZipArchive(zipped, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var sheet in sheets)
            {
                var entry = archive.CreateEntry($"{sheet.Name}.csv", CompressionLevel.Optimal);
                await using var stream = await entry.OpenAsync(cancellationToken);
                // A byte-order mark, so Excel reads names with accents properly.
                var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Csv.Write(sheet.Header, sheet.Rows))).ToArray();
                await stream.WriteAsync(bytes, cancellationToken);
            }
        }

        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        return TypedResults.File(zipped.ToArray(), "application/zip", $"growlog-records-{today:yyyy-MM-dd}.zip");
    }

    private static async Task<Sheet> PlantsAsync(GrowLogDbContext db, CancellationToken cancellationToken) =>
        new("plants", ["Common name", "Variety", "Botanical name", "Category", "Position", "Watering", "Hardiness", "Xylella host", "Protected variety", "No longer used"],
            (await db.Plants.AsNoTracking().OrderBy(plant => plant.CommonName).ThenBy(plant => plant.Variety).ToListAsync(cancellationToken))
                .Select(plant => new object?[] { plant.CommonName, plant.Variety, plant.BotanicalName, plant.Category, plant.Position, plant.Watering, plant.Hardiness, plant.XylellaHost, plant.ProtectedVariety, plant.ArchivedAt != null }));

    private static async Task<Sheet> SuppliersAsync(GrowLogDbContext db, CancellationToken cancellationToken) =>
        new("suppliers", ["Name", "Contact", "Phone", "Email", "Address", "Account reference", "Supplies", "Lead time (days)", "Passport registration", "Other registrations", "Notes", "No longer used"],
            (await db.Suppliers.AsNoTracking().OrderBy(supplier => supplier.Name).ToListAsync(cancellationToken))
                .Select(s => new object?[] { s.Name, s.ContactName, s.Phone, s.Email, s.Address, s.AccountReference, s.Supplies, s.TypicalLeadTimeDays, s.PassportRegistrationNumber, s.OtherRegistrations, s.Notes, s.ArchivedAt != null }));

    private static async Task<Sheet> CustomersAsync(GrowLogDbContext db, CancellationToken cancellationToken) =>
        new("customers", ["Name", "Address", "Email", "Phone", "VAT number", "No longer used"],
            (await db.Customers.AsNoTracking().OrderBy(customer => customer.Name).ToListAsync(cancellationToken))
                .Select(c => new object?[] { c.Name, c.Address, c.Email, c.Phone, c.VatNumber, c.ArchivedAt != null }));

    private static async Task<Sheet> PlacesAsync(GrowLogDbContext db, Dictionary<Guid, string> paths, CancellationToken cancellationToken) =>
        new("places", ["Place", "Kind", "Label", "No longer used"],
            (await db.Locations.AsNoTracking().ToListAsync(cancellationToken))
                .OrderBy(place => paths.GetValueOrDefault(place.Id, place.Name), StringComparer.OrdinalIgnoreCase)
                .Select(place => new object?[] { paths.GetValueOrDefault(place.Id, place.Name), place.Kind, place.LabelCode, place.ArchivedAt != null }));

    private static async Task<Sheet> BatchesAsync(GrowLogDbContext db, Names names, CancellationToken cancellationToken) =>
        new("batches", ["Batch", "Plant", "Variety", "How it started", "Lot or reference", "Supplier", "Country of origin", "Grown from batch", "Mother plant", "Cuttings round", "Seed sown as", "Seed amount", "Packet share", "Packet says", "Sow by", "Recorded"],
            (await db.Batches.AsNoTracking().OrderBy(batch => batch.TraceabilityCode).ToListAsync(cancellationToken))
                .Select(batch => new object?[]
                {
                    batch.TraceabilityCode,
                    names.Plants[batch.PlantId].CommonName,
                    names.Plants[batch.PlantId].Variety,
                    batch.Source,
                    batch.SourceReference,
                    names.Supplier(batch.SupplierId),
                    batch.OriginCountry,
                    names.Batch(batch.ParentBatchId),
                    batch.MotherPlantId is { } mother ? names.Mothers.GetValueOrDefault(mother) : batch.MotherPlant,
                    batch.CuttingsRound,
                    batch.SownAs,
                    batch.SownAmount,
                    batch.PacketShare,
                    batch.PacketSays,
                    batch.SowBy,
                    batch.CreatedAt,
                }));

    private static async Task<Sheet> TraysAsync(GrowLogDbContext db, EventStore store, CancellationToken cancellationToken) =>
        new("trays-now", ["Tray or pot", "Batch", "Plant", "Variety", "How many", "Not counted yet", "Stage", "Since", "Place", "Pot size"],
            (await ProductionEndpoints.ListAsync(db, store, cancellationToken))
                .OrderBy(row => row.LabelCode, StringComparer.Ordinal)
                .Select(row => new object?[] { row.LabelCode, row.TraceabilityCode, row.CommonName, row.Variety, row.NotCounted ? null : row.Quantity, row.NotCounted, row.Stage, row.StageSince, row.LocationName, row.PotSizeName }));

    /// <summary>Every piece of work, one row for each tray and batch it touched, oldest first. Undone work is marked, not left out.</summary>
    private static async Task<Sheet> WorkAsync(GrowLogDbContext db, Names names, CancellationToken cancellationToken)
    {
        var undone = (await db.Events.Where(e => e.CorrectsEventId != null).Select(e => e.CorrectsEventId!.Value).ToListAsync(cancellationToken)).ToHashSet();
        var events = await db.Events.AsNoTracking().Include(e => e.Lines).OrderBy(e => e.OccurredAt).ThenBy(e => e.RecordedAt).ToListAsync(cancellationToken);

        return new("work", ["When", "Recorded", "What", "Tray or pot", "Batch", "Change in plants", "Place", "By", "Details", "Undone or corrected", "Corrects", "Work id", "Line effective time"],
            events.SelectMany(e =>
            {
                object?[] Row(Guid? container, Guid? batch, int? change, DateTimeOffset? effectiveAt) =>
                [
                    e.OccurredAt, e.RecordedAt, e.Type,
                    container is { } c ? names.Containers.GetValueOrDefault(c) : null,
                    names.Batch(batch),
                    change,
                    e.LocationId is { } place ? names.Places.GetValueOrDefault(place) : null,
                    names.People.GetValueOrDefault(e.RecordedBy),
                    string.Equals(e.Payload, "{}", StringComparison.Ordinal) ? null : e.Payload,
                    undone.Contains(e.Id),
                    e.CorrectsEventId,
                    e.Id,
                    effectiveAt,
                ];

                return e.Lines.Count == 0
                    ? [Row(e.ContainerId, null, null, null)]
                    : e.Lines.Select(line => Row(line.ContainerId, line.BatchId, line.QuantityChange, line.EffectiveAt ?? e.OccurredAt));
            }));
    }

    private static async Task<Sheet> AdjustmentsAsync(GrowLogDbContext db, CancellationToken cancellationToken) =>
        new("count-adjustments", ["Work id", "Count work id", "Container id", "Batch id", "Change in plants"],
            (await db.CountAdjustments.AsNoTracking().ToListAsync(cancellationToken))
                .Select(adjustment => new object?[] { adjustment.EventId, adjustment.CountEventId, adjustment.ContainerId, adjustment.BatchId, adjustment.QuantityChange }));

    private static async Task<Sheet> LegacyCountsAsync(GrowLogDbContext db, CancellationToken cancellationToken) =>
        new("legacy-counts", ["Count work id", "Container id", "Batch id", "Counted"],
            (await db.Set<LegacyCount>().AsNoTracking().ToListAsync(cancellationToken))
                .Select(count => new object?[] { count.EventId, count.ContainerId, count.BatchId, count.Counted }));

    private static async Task<Sheet> PurchasesAsync(GrowLogDbContext db, Names names, CancellationToken cancellationToken) =>
        new("purchases", ["Bought", "Supplier", "Reference", "Item", "Quantity", "Cost before VAT", "VAT", "Paid", "Cancelled", "Notes"],
            (await db.Purchases.AsNoTracking().Include(purchase => purchase.Lines).OrderBy(purchase => purchase.PurchasedOn).ToListAsync(cancellationToken))
                .SelectMany(purchase => purchase.Lines.Select(line => new object?[]
                {
                    purchase.PurchasedOn, names.Supplier(purchase.SupplierId), purchase.Reference, line.Description, line.Quantity, line.NetCost, line.Vat, purchase.PaidOn, purchase.VoidedAt != null, purchase.Notes,
                })));

    private static async Task<Sheet> InvoicesAsync(GrowLogDbContext db, Names names, CancellationToken cancellationToken) =>
        new("invoices", ["Number", "Kind", "Issued", "Due", "Paid", "Customer", "Item", "Quantity", "Price each", "VAT rate", "Batch", "Notes"],
            (await db.Invoices.AsNoTracking().Include(invoice => invoice.Lines).OrderBy(invoice => invoice.Number).ToListAsync(cancellationToken))
                .SelectMany(invoice => invoice.Lines.OrderBy(line => line.Position).Select(line => new object?[]
                {
                    invoice.Number, invoice.Kind, invoice.IssuedOn, invoice.DueOn, invoice.PaidOn, invoice.CustomerName, line.Description, line.Quantity, line.UnitPrice, line.VatRate, names.Batch(line.BatchId), invoice.Notes,
                })));

    private static async Task<Sheet> TillAsync(GrowLogDbContext db, CancellationToken cancellationToken) =>
        new("till-takings", ["Day", "Amount", "Notes", "Cancelled"],
            (await db.TillTakings.AsNoTracking().OrderBy(taking => taking.TakenOn).ToListAsync(cancellationToken))
                .Select(taking => new object?[] { taking.TakenOn, taking.Amount, taking.Notes, taking.VoidedAt != null }));

    private static async Task<Sheet> PassportsIssuedAsync(GrowLogDbContext db, Names names, CancellationToken cancellationToken) =>
        new("passports-issued", ["Issued", "To", "Plants", "A", "B", "C", "D", "Rule for D", "Tray or pot", "By"],
            (await db.IssuedPassports.AsNoTracking().OrderBy(passport => passport.IssuedOn).ThenBy(passport => passport.CreatedAt).ToListAsync(cancellationToken))
                .Select(passport => new object?[]
                {
                    passport.IssuedOn, passport.IssuedTo, passport.Quantity, passport.PartA, passport.PartB, passport.PartC, passport.PartD, passport.OriginRule,
                    names.Containers.GetValueOrDefault(passport.ContainerId), names.People.GetValueOrDefault(passport.IssuedBy),
                }));

    private static async Task<Sheet> PassportsReceivedAsync(GrowLogDbContext db, Names names, CancellationToken cancellationToken) =>
        new("passports-received", ["Received", "From", "Batch", "A", "B", "C", "D", "E", "Status"],
            (await db.Set<ReceivedPassport>().AsNoTracking().OrderBy(passport => passport.ReceivedOn).ToListAsync(cancellationToken))
                .Select(passport => new object?[]
                {
                    passport.ReceivedOn, names.Supplier(passport.SupplierId), names.Batch(passport.BatchId), passport.PartA, passport.PartB, passport.PartC, passport.PartD, passport.PartE, passport.Status,
                }));

    private static async Task<Sheet> ConsignmentsAsync(GrowLogDbContext db, Names names, CancellationToken cancellationToken)
    {
        var consignments = await db.Consignments.AsNoTracking().Include(c => c.Packages).ThenInclude(package => package.Lines).OrderBy(c => c.Number).ToListAsync(cancellationToken);

        return new("consignments", ["Number", "How", "To", "Sent", "Package", "Tray or pot", "Batch", "Plants", "Notes"],
            consignments.SelectMany(c => c.Packages.OrderBy(package => package.Number).SelectMany(package => package.Lines.Select(line => new object?[]
            {
                c.Number, c.Kind, c.SentTo, c.SentOn, package.Number, names.Containers.GetValueOrDefault(line.ContainerId), names.Batch(line.BatchId), line.Quantity, c.Notes,
            }))));
    }

    private static async Task<Sheet> JobsAsync(GrowLogDbContext db, CancellationToken cancellationToken)
    {
        var lastDone = await db.JobsDone.GroupBy(done => done.JobId).Select(group => new { group.Key, Last = group.Max(done => done.DoneOn) }).ToDictionaryAsync(row => row.Key, row => row.Last, cancellationToken);

        return new("jobs", ["Job", "Every (days)", "Next due", "Last done", "Notes", "Stopped"],
            (await db.Jobs.AsNoTracking().OrderBy(job => job.Title).ToListAsync(cancellationToken))
                .Select(job => new object?[] { job.Title, job.EveryDays, job.NextDue, lastDone.TryGetValue(job.Id, out var last) ? last : null, job.Notes, job.ArchivedAt != null }));
    }

    private static async Task<Sheet> MothersAsync(GrowLogDbContext db, Names names, CancellationToken cancellationToken)
    {
        var taken = await db.Batches.Where(batch => batch.MotherPlantId != null).GroupBy(batch => batch.MotherPlantId!.Value).Select(group => new { group.Key, Count = group.Count() }).ToDictionaryAsync(row => row.Key, row => row.Count, cancellationToken);

        return new("mother-plants", ["Name", "Plant", "Cuttings batches taken", "Notes", "No longer used"],
            (await db.MotherPlants.AsNoTracking().OrderBy(mother => mother.Name).ToListAsync(cancellationToken))
                .Select(mother => new object?[] { mother.Name, names.Plants[mother.PlantId].CommonName, taken.GetValueOrDefault(mother.Id), mother.Notes, mother.ArchivedAt != null }));
    }
}
