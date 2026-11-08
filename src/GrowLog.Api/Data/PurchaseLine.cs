namespace GrowLog.Api.Data;

internal sealed class PurchaseLine : IAudited
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid PurchaseId { get; init; }

    public required string Description { get; init; }

    public int? Quantity { get; init; }

    public required decimal NetCost { get; init; }

    public required decimal Vat { get; init; }
}
