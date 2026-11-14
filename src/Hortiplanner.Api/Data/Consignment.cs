namespace Hortiplanner.Api.Data;

/// <summary>A sale or movement of plants, made up of packages: the smallest units that travel together.</summary>
internal sealed class Consignment
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required int Number { get; init; }

    public required ConsignmentKind Kind { get; init; }

    /// <summary>Who or where the plants went, as written at the time: a business name, or a site.</summary>
    public required string SentTo { get; init; }

    public Guid? CustomerId { get; init; }

    public required DateOnly SentOn { get; init; }

    public string? Notes { get; init; }

    public required Guid CreatedBy { get; init; }

    public string? RequestFingerprint { get; init; }

    public DateTimeOffset CreatedAt { get; private set; }

    public ICollection<ConsignmentPackage> Packages { get; } = new List<ConsignmentPackage>();
}
