using System.Security.Claims;
using Hortiplanner.Api.Accounts;
using Hortiplanner.Api.Data;
using Hortiplanner.Api.Settings;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hortiplanner.Api.Buying;

/// <summary>Money out. For the owner.</summary>
internal static class PurchaseEndpoints
{
    private const int MaxReceiptBytes = 10 * 1024 * 1024;

    public static void MapPurchaseEndpoints(this IEndpointRouteBuilder app)
    {
        var purchases = app.MapGroup("/api/purchases").RequireAuthorization(AccountServices.OwnerPolicy).RequireFeature(NurseryFeature.Purchases);
        purchases.MapGet("/", ListAsync);
        purchases.MapPost("/", AddAsync).Produces<PurchaseCreated>(StatusCodes.Status201Created);
        purchases.MapPost("/{id:guid}/paid", SetPaidAsync);
        purchases.MapPost("/{id:guid}/void", VoidAsync);
        purchases.MapPost("/{id:guid}/receipts", AddReceiptAsync).Produces<PurchaseCreated>(StatusCodes.Status201Created);
        purchases.MapGet("/receipts/{id:guid}", GetReceiptAsync);
        purchases.MapPost("/read-invoice", ReadInvoiceAsync).Produces<InvoiceDraft>();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsMoney(decimal value) => value >= 0 && value == decimal.Round(value, 2);

    private static bool IsValid(LineRequest line) =>
        Clean(line.Description) is not null
        && IsMoney(line.NetCost)
        && IsMoney(line.Vat)
        && line.Quantity is null or > 0;

    private static DateOnly LatestAllowedDate => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

    private static async Task<List<PurchaseResponse>> ListAsync(HortiplannerDbContext db, bool includeVoided = false, CancellationToken cancellationToken = default)
    {
        var purchases = await db.Purchases
            .AsNoTracking()
            .Include(purchase => purchase.Lines)
            .Where(purchase => includeVoided || purchase.VoidedAt == null)
            .OrderByDescending(purchase => purchase.PurchasedOn)
            .ThenByDescending(purchase => purchase.Id)
            .ToListAsync(cancellationToken);

        var supplierNames = await db.Suppliers.AsNoTracking().ToDictionaryAsync(supplier => supplier.Id, supplier => supplier.Name, cancellationToken);

        var receipts = (await db.PurchaseReceipts.AsNoTracking().OrderBy(receipt => receipt.Id).Select(receipt => new { receipt.Id, receipt.PurchaseId }).ToListAsync(cancellationToken))
            .ToLookup(receipt => receipt.PurchaseId, receipt => receipt.Id);

        var batchCounts = await db.Batches.AsNoTracking()
            .Where(batch => batch.PurchaseLineId != null)
            .GroupBy(batch => batch.PurchaseLineId!.Value)
            .ToDictionaryAsync(group => group.Key, group => group.Count(), cancellationToken);

        return purchases.ConvertAll(purchase => new PurchaseResponse(
            purchase.Id,
            purchase.SupplierId,
            supplierNames[purchase.SupplierId],
            purchase.PurchasedOn,
            purchase.Reference,
            purchase.Notes,
            purchase.PaidOn,
            purchase.VoidedAt is not null,
            purchase.Lines.Sum(line => line.NetCost),
            purchase.Lines.Sum(line => line.Vat),
            purchase.Lines.OrderBy(line => line.Id).Select(line => new LineResponse(line.Id, line.Description, line.Quantity, line.NetCost, line.Vat, batchCounts.GetValueOrDefault(line.Id), line.IsPlants)).ToList(),
            receipts[purchase.Id].ToList()));
    }

    private static async Task<IResult> AddAsync(PurchaseRequest request, ClaimsPrincipal user, HortiplannerDbContext db, EventStore store, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var id = request.Id ?? Guid.CreateVersion7();
        await store.LockRequestAsync(id, cancellationToken);
        var person = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (await db.Purchases.AsNoTracking().FirstOrDefaultAsync(purchase => purchase.Id == id, cancellationToken) is { } stored)
        {
            return stored.RecordedBy == person && string.Equals(stored.RequestFingerprint, RequestFingerprint.For(request), StringComparison.Ordinal)
                ? TypedResults.Ok(new PurchaseCreated(stored.Id)) : TypedResults.Conflict();
        }
        if (id == Guid.Empty || request.Lines.Count == 0 || !request.Lines.All(IsValid) || request.PurchasedOn > LatestAllowedDate)
        {
            return TypedResults.BadRequest();
        }

        var purchase = new Purchase
        {
            Id = id,
            RequestFingerprint = RequestFingerprint.For(request),
            SupplierId = request.SupplierId,
            PurchasedOn = request.PurchasedOn,
            Reference = Clean(request.Reference),
            Notes = Clean(request.Notes),
            RecordedBy = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
        };

        foreach (var line in request.Lines)
        {
            purchase.Lines.Add(new PurchaseLine { Description = line.Description.Trim(), Quantity = line.Quantity, NetCost = line.NetCost, Vat = line.Vat, IsPlants = line.IsPlants });
        }

        db.Purchases.Add(purchase);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return TypedResults.Created($"/api/purchases/{purchase.Id}", new PurchaseCreated(purchase.Id));
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            return TypedResults.BadRequest();
        }
    }

    private static async Task<IResult> SetPaidAsync(Guid id, PaidRequest request, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        if (request.PaidOn > LatestAllowedDate)
        {
            return TypedResults.BadRequest();
        }

        if (await db.Purchases.FindAsync([id], cancellationToken) is not { VoidedAt: null } purchase)
        {
            return TypedResults.NotFound();
        }

        purchase.PaidOn = request.PaidOn;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> VoidAsync(Guid id, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Purchases.FindAsync([id], cancellationToken) is not { } purchase)
        {
            return TypedResults.NotFound();
        }

        purchase.VoidedAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>Goes by the file's own first bytes, so a mislabelled file can't be served as something else.</summary>
    private static string? DetectContentType(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith<byte>([0xFF, 0xD8, 0xFF]))
        {
            return "image/jpeg";
        }

        if (content.StartsWith<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }

        return content.StartsWith("%PDF-"u8) ? "application/pdf" : null;
    }

    /// <summary>Suggests a purchase from a supplier's PDF invoice, for a person to check before saving.</summary>
    private static async Task<IResult> ReadInvoiceAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);
        var content = buffer.ToArray();

        if (content.Length is 0 or > MaxReceiptBytes || !content.AsSpan().StartsWith("%PDF-"u8))
        {
            return TypedResults.BadRequest();
        }

        try
        {
            return TypedResults.Ok(InvoiceReader.Read(content));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A damaged or unusual PDF: the person types the purchase in instead.
            return TypedResults.UnprocessableEntity();
        }
    }

    private static async Task<IResult> AddReceiptAsync(Guid id, HttpRequest request, ClaimsPrincipal user, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Purchases.AnyAsync(purchase => purchase.Id == id && purchase.VoidedAt == null, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        using var body = new MemoryStream();
        var buffer = new byte[81920];
        int read;

        while ((read = await request.Body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            body.Write(buffer, 0, read);

            if (body.Length > MaxReceiptBytes)
            {
                return TypedResults.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }
        }

        var content = body.ToArray();

        if (DetectContentType(content) is not { } contentType)
        {
            return TypedResults.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        var receipt = new PurchaseReceipt
        {
            PurchaseId = id,
            ContentType = contentType,
            Content = content,
            UploadedBy = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
        };

        db.PurchaseReceipts.Add(receipt);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/purchases/receipts/{receipt.Id}", new PurchaseCreated(receipt.Id));
    }

    private static async Task<IResult> GetReceiptAsync(Guid id, HttpContext context, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        if (await db.PurchaseReceipts.AsNoTracking().FirstOrDefaultAsync(receipt => receipt.Id == id, cancellationToken) is not { } receipt)
        {
            return TypedResults.NotFound();
        }

        context.Response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.File(receipt.Content, receipt.ContentType);
    }

    internal sealed record LineRequest(string Description, int? Quantity, decimal NetCost, decimal Vat, bool? IsPlants = null);

    internal sealed record PurchaseRequest(Guid SupplierId, DateOnly PurchasedOn, string? Reference, string? Notes, IReadOnlyList<LineRequest> Lines, Guid? Id = null);

    internal sealed record PurchaseCreated(Guid Id);

    internal sealed record PaidRequest(DateOnly? PaidOn);

    internal sealed record LineResponse(Guid Id, string Description, int? Quantity, decimal NetCost, decimal Vat, int BatchCount, bool? IsPlants);

    internal sealed record PurchaseResponse(
        Guid Id,
        Guid SupplierId,
        string SupplierName,
        DateOnly PurchasedOn,
        string? Reference,
        string? Notes,
        DateOnly? PaidOn,
        bool Voided,
        decimal NetTotal,
        decimal VatTotal,
        IReadOnlyList<LineResponse> Lines,
        IReadOnlyList<Guid> ReceiptIds);
}
