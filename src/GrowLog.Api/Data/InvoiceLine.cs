namespace GrowLog.Api.Data;

internal sealed class InvoiceLine
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid InvoiceId { get; init; }

    /// <summary>Where the line comes on the invoice, from 1.</summary>
    public required int Position { get; init; }

    public required string Description { get; init; }

    public required int Quantity { get; init; }

    /// <summary>Price for one, before VAT.</summary>
    public required decimal UnitPrice { get; init; }

    /// <summary>VAT rate as a percentage, such as 20 or 0. Zero when no VAT was charged.</summary>
    public required decimal VatRate { get; init; }

    public Guid? PlantId { get; init; }

    /// <summary>The tray the plants came from, when they were taken from stock.</summary>
    public Guid? ContainerId { get; init; }

    public Guid? BatchId { get; init; }
}
