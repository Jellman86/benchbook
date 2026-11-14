namespace Hortiplanner.Api.Data;

internal sealed class PurchaseLine : IAudited
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid PurchaseId { get; init; }

    public required string Description { get; init; }

    public int? Quantity { get; init; }

    public required decimal NetCost { get; init; }

    public required decimal Vat { get; init; }

    /// <summary>Whether the line was plants, which become batches, or sundries such as compost. Null on older lines.</summary>
    public bool? IsPlants { get; init; }
}
