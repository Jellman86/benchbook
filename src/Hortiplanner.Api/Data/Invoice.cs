namespace Hortiplanner.Api.Data;

/// <summary>
/// A trade invoice or credit note, numbered without gaps. Once issued it is never changed: a mistake
/// is put right with a credit note. It keeps the customer's name and address as they were.
/// </summary>
internal sealed class Invoice
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required int Number { get; init; }

    public required InvoiceKind Kind { get; init; }

    public required Guid CustomerId { get; init; }

    public required string CustomerName { get; init; }

    public string? SellerName { get; init; }

    public string? SellerAddress { get; init; }

    public string? SellerVatNumber { get; init; }

    public string? SellerContact { get; init; }

    public string? CustomerAddress { get; init; }

    public string? CustomerVatNumber { get; init; }

    public required DateOnly IssuedOn { get; init; }

    public DateOnly? DueOn { get; init; }

    /// <summary>The invoice a credit note cancels.</summary>
    public Guid? CreditsInvoiceId { get; init; }

    /// <summary>Whether VAT was charged, as the nursery was registered for it at the time.</summary>
    public required bool VatCharged { get; init; }

    public string? Notes { get; init; }

    /// <summary>The stock taken for this invoice, recorded as a Sold event.</summary>
    public Guid? SoldEventId { get; init; }

    /// <summary>Null while it is still owed.</summary>
    public DateOnly? PaidOn { get; set; }

    public required Guid IssuedBy { get; init; }

    public string? RequestFingerprint { get; init; }

    public DateTimeOffset CreatedAt { get; private set; }

    public ICollection<InvoiceLine> Lines { get; } = new List<InvoiceLine>();
}
