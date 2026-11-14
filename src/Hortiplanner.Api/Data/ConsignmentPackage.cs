namespace Hortiplanner.Api.Data;

/// <summary>One package in a consignment: what a passport is attached to, and what it covers.</summary>
internal sealed class ConsignmentPackage
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid ConsignmentId { get; init; }

    /// <summary>Its number within the consignment, from 1.</summary>
    public required int Number { get; init; }

    public ICollection<PackageLine> Lines { get; } = new List<PackageLine>();
}
