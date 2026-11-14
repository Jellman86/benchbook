using System.Globalization;
using System.Security.Claims;
using Hortiplanner.Api.Accounts;
using Hortiplanner.Api.Data;
using Hortiplanner.Api.Labels;
using Hortiplanner.Api.Settings;
using Microsoft.EntityFrameworkCore;

namespace Hortiplanner.Api.Selling;

/// <summary>
/// Trade invoices and credit notes. An invoice is issued once and never changed; taking plants from a
/// tray records a Sold event in the same transaction, and a credit note cancels both.
/// </summary>
internal static class InvoiceEndpoints
{
    private const int MostLines = 100;
    private const int LongestDescription = 300;
    private static readonly decimal[] UkVatRates = [0m, 5m, 20m];

    public static void MapInvoiceEndpoints(this IEndpointRouteBuilder app)
    {
        var invoices = app.MapGroup("/api/invoices").RequireAuthorization(AccountServices.OwnerPolicy).RequireFeature(NurseryFeature.Invoices);
        invoices.MapGet("/", ListAsync);
        invoices.MapPost("/", IssueAsync).Produces<InvoiceIssued>(StatusCodes.Status201Created).Produces<IssueRefused>(StatusCodes.Status409Conflict);
        invoices.MapPost("/{id:guid}/paid", SetPaidAsync);
        invoices.MapPost("/{id:guid}/credit", CreditAsync).Produces<InvoiceIssued>(StatusCodes.Status201Created);
        invoices.MapGet("/{id:guid}/pdf", PdfAsync).Produces(StatusCodes.Status200OK, contentType: "application/pdf");
    }

    private static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static string Money(decimal value) => string.Create(CultureInfo.InvariantCulture, $"£{value:N2}");

    private static async Task<List<InvoiceSummary>> ListAsync(HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        var invoices = await db.Invoices.AsNoTracking().Include(invoice => invoice.Lines).OrderByDescending(invoice => invoice.Number).ToListAsync(cancellationToken);
        var credited = invoices.Where(invoice => invoice.CreditsInvoiceId is not null).Select(invoice => invoice.CreditsInvoiceId!.Value).ToHashSet();

        return invoices.ConvertAll(invoice => new InvoiceSummary(
            invoice.Id,
            invoice.Number,
            invoice.Kind,
            invoice.CustomerName,
            invoice.IssuedOn,
            invoice.DueOn,
            invoice.PaidOn,
            InvoiceTotals.Of(invoice.Lines).Total,
            credited.Contains(invoice.Id),
            invoice.CreditsInvoiceId));
    }

    private static string? Problem(InvoiceRequest request, bool vatRegistered)
    {
        if (request.Lines.Count is 0 or > MostLines)
        {
            return "Lines";
        }

        foreach (var line in request.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Description) || line.Description.Length > LongestDescription || line.Quantity <= 0 || line.UnitPrice < 0)
            {
                return "Lines";
            }

            if ((line.ContainerId is null) != (line.BatchId is null))
            {
                return "Lines";
            }

            if (vatRegistered ? !UkVatRates.Contains(line.VatRate) : line.VatRate != 0)
            {
                return "VatRate";
            }
        }

        return null;
    }

    private static async Task<IResult> IssueAsync(InvoiceRequest request, ClaimsPrincipal user, HortiplannerDbContext db, EventStore store, TimeProvider clock, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var requestId = request.Id ?? Guid.CreateVersion7();
        await store.LockRequestAsync(requestId, cancellationToken);
        if (await db.Invoices.AsNoTracking().FirstOrDefaultAsync(invoice => invoice.Id == requestId, cancellationToken) is { } stored)
        {
            return stored.IssuedBy == UserId(user) && string.Equals(stored.RequestFingerprint, RequestFingerprint.For(request), StringComparison.Ordinal)
                ? TypedResults.Ok(new InvoiceIssued(stored.Id, stored.Number)) : TypedResults.Conflict(new IssueRefused("IdInUse"));
        }
        var settings = await db.GetNurserySettingsAsync(cancellationToken);

        if (requestId == Guid.Empty || Problem(request, settings.VatRegistered) is not null
            || await db.Customers.AsNoTracking().FirstOrDefaultAsync(customer => customer.Id == request.CustomerId && customer.ArchivedAt == null, cancellationToken) is not { } customer)
        {
            return TypedResults.BadRequest();
        }

        var issuedOn = request.IssuedOn ?? DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var number = await NextNumberAsync(db, cancellationToken);
        var sold = await TakeFromStockAsync(request.Lines, user, store, clock, cancellationToken);

        if (sold is { Result: not AppendResult.Stored })
        {
            return TypedResults.Conflict(new IssueRefused("NotEnoughStock"));
        }

        var invoice = new Invoice
        {
            Id = requestId,
            RequestFingerprint = RequestFingerprint.For(request),
            SellerName = settings.NurseryName ?? string.Empty,
            SellerAddress = settings.Address ?? string.Empty,
            SellerVatNumber = settings.VatNumber ?? string.Empty,
            SellerContact = string.Join(" · ", new[] { settings.Phone, settings.Email, settings.Website }.Where(part => !string.IsNullOrWhiteSpace(part))),
            Number = number,
            Kind = InvoiceKind.Invoice,
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            CustomerAddress = customer.Address,
            CustomerVatNumber = customer.VatNumber,
            IssuedOn = issuedOn,
            DueOn = issuedOn.AddDays(settings.PaymentTermsDays),
            VatCharged = settings.VatRegistered,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            SoldEventId = sold?.EventId,
            IssuedBy = UserId(user),
        };

        AddLines(invoice, request.Lines);

        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Created($"/api/invoices/{invoice.Id}", new InvoiceIssued(invoice.Id, invoice.Number));
    }

    private static void AddLines(Invoice invoice, IReadOnlyList<InvoiceLineRequest> lines)
    {
        foreach (var (line, position) in lines.Select((line, index) => (line, index + 1)))
        {
            invoice.Lines.Add(new InvoiceLine
            {
                Position = position,
                Description = line.Description.Trim(),
                Quantity = line.Quantity,
                UnitPrice = Math.Round(line.UnitPrice, 2),
                VatRate = line.VatRate,
                PlantId = line.PlantId,
                ContainerId = line.ContainerId,
                BatchId = line.BatchId,
            });
        }

    }

    /// <summary>Records the plants taken from trays as one Sold event, or nothing when no line names a tray.</summary>
    private static async Task<(Guid EventId, AppendResult Result)?> TakeFromStockAsync(
        IReadOnlyList<InvoiceLineRequest> lines,
        ClaimsPrincipal user,
        EventStore store,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var taken = lines
            .Where(line => line.ContainerId is not null && line.BatchId is not null)
            .GroupBy(line => (Container: line.ContainerId!.Value, Batch: line.BatchId!.Value))
            .ToList();

        if (taken.Count == 0)
        {
            return null;
        }

        var sold = new Event { Id = Guid.CreateVersion7(), Type = EventType.Sold, OccurredAt = clock.GetUtcNow(), RecordedBy = UserId(user) };

        foreach (var group in taken)
        {
            sold.Lines.Add(new EventLine { ContainerId = group.Key.Container, BatchId = group.Key.Batch, QuantityChange = -group.Sum(line => line.Quantity) });
        }

        return (sold.Id, await store.AppendAsync(sold, cancellationToken));
    }

    /// <summary>The next number with no gaps: the table is locked until this transaction ends.</summary>
    private static async Task<int> NextNumberAsync(HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE invoices IN EXCLUSIVE MODE", cancellationToken);
        return (await db.Invoices.MaxAsync(invoice => (int?)invoice.Number, cancellationToken) ?? 0) + 1;
    }

    private static async Task<IResult> SetPaidAsync(Guid id, PaidRequest request, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Invoices.FirstOrDefaultAsync(invoice => invoice.Id == id && invoice.Kind == InvoiceKind.Invoice, cancellationToken) is not { } invoice)
        {
            return TypedResults.NotFound();
        }

        invoice.PaidOn = request.PaidOn;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>Cancels an invoice in full with a credit note, putting any plants it took back in their trays.</summary>
    private static async Task<IResult> CreditAsync(Guid id, ClaimsPrincipal user, HortiplannerDbContext db, EventStore store, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (await db.Invoices.AsNoTracking().Include(invoice => invoice.Lines).FirstOrDefaultAsync(invoice => invoice.Id == id && invoice.Kind == InvoiceKind.Invoice, cancellationToken) is not { } original)
        {
            return TypedResults.NotFound();
        }

        if (await db.Invoices.AnyAsync(invoice => invoice.CreditsInvoiceId == id, cancellationToken))
        {
            return TypedResults.Conflict(new IssueRefused("AlreadyCredited"));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var number = await NextNumberAsync(db, cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM invoices WHERE id = {0} FOR UPDATE", [id], cancellationToken);
        if (await db.Invoices.AnyAsync(invoice => invoice.CreditsInvoiceId == id, cancellationToken))
        {
            return TypedResults.Conflict(new IssueRefused("AlreadyCredited"));
        }
        if (original.SoldEventId is { } soldId)
        {
            var putBack = new Event { Id = Guid.CreateVersion7(), Type = EventType.Corrected, OccurredAt = clock.GetUtcNow(), RecordedBy = UserId(user), CorrectsEventId = soldId };
            await store.AppendAsync(putBack, cancellationToken);
        }

        var credit = new Invoice
        {
            Number = number,
            Kind = InvoiceKind.CreditNote,
            SellerName = original.SellerName,
            SellerAddress = original.SellerAddress,
            SellerVatNumber = original.SellerVatNumber,
            SellerContact = original.SellerContact,
            CustomerId = original.CustomerId,
            CustomerName = original.CustomerName,
            CustomerAddress = original.CustomerAddress,
            CustomerVatNumber = original.CustomerVatNumber,
            IssuedOn = DateOnly.FromDateTime(clock.GetLocalNow().DateTime),
            CreditsInvoiceId = original.Id,
            VatCharged = original.VatCharged,
            IssuedBy = UserId(user),
        };

        foreach (var line in original.Lines.OrderBy(line => line.Position))
        {
            credit.Lines.Add(new InvoiceLine { Position = line.Position, Description = line.Description, Quantity = line.Quantity, UnitPrice = line.UnitPrice, VatRate = line.VatRate, PlantId = line.PlantId });
        }

        db.Invoices.Add(credit);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Created($"/api/invoices/{credit.Id}", new InvoiceIssued(credit.Id, credit.Number));
    }

    private static async Task<IResult> PdfAsync(Guid id, HortiplannerDbContext db, LabelPrinter printer, CancellationToken cancellationToken)
    {
        if (await db.Invoices.AsNoTracking().Include(invoice => invoice.Lines).FirstOrDefaultAsync(invoice => invoice.Id == id, cancellationToken) is not { } invoice)
        {
            return TypedResults.NotFound();
        }

        var settings = await db.GetNurserySettingsAsync(cancellationToken);
        var credited = invoice.CreditsInvoiceId is { } creditedId
            ? await db.Invoices.Where(other => other.Id == creditedId).Select(other => (int?)other.Number).FirstOrDefaultAsync(cancellationToken)
            : null;

        var data = InvoiceDocument.Describe(invoice, settings, credited);
        var context = await LabelSettings.LoadAsync(db, LabelKind.Invoice, cancellationToken);
        var pdf = await printer.RenderAsync(LabelKind.Invoice, data, qrText: null, context, copies: 1, cancellationToken);
        // Opened in the browser, ready to print or save.
        return TypedResults.File(pdf, "application/pdf");
    }

    internal sealed record InvoiceLineRequest(string Description, int Quantity, decimal UnitPrice, decimal VatRate, Guid? PlantId, Guid? ContainerId, Guid? BatchId);

    internal sealed record InvoiceRequest(Guid CustomerId, DateOnly? IssuedOn, string? Notes, IReadOnlyList<InvoiceLineRequest> Lines, Guid? Id = null);

    internal sealed record InvoiceIssued(Guid Id, int Number);

    internal sealed record IssueRefused(string Problem);

    internal sealed record PaidRequest(DateOnly? PaidOn);

    internal sealed record InvoiceSummary(
        Guid Id,
        int Number,
        InvoiceKind Kind,
        string CustomerName,
        DateOnly IssuedOn,
        DateOnly? DueOn,
        DateOnly? PaidOn,
        decimal Total,
        bool Credited,
        Guid? CreditsInvoiceId);

    internal static string Format(decimal value) => Money(value);
}
