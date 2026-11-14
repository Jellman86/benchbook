namespace Hortiplanner.Api.Data;

/// <summary>Plants of one batch from one tray in a package.</summary>
internal sealed class PackageLine
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid PackageId { get; init; }

    public required Guid BatchId { get; init; }

    public required Guid ContainerId { get; init; }

    public required int Quantity { get; init; }

    /// <summary>Plants already sold on this invoice line, and so no longer in the tray they came from.</summary>
    public Guid? InvoiceLineId { get; init; }
}
