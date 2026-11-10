namespace GrowLog.Api.Data;

internal enum InvoiceKind
{
    Invoice,

    /// <summary>Cancels an invoice in full. Issued invoices are never edited.</summary>
    CreditNote,
}
