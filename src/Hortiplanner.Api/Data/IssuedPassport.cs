namespace Hortiplanner.Api.Data;

/// <summary>
/// A UK plant passport the nursery issued for plants of one batch. Kept for at least three years with who the
/// plants went to, and never changed: a mistake is put right by issuing another. Every issue follows an
/// examination, recorded on the tray as work.
/// </summary>
internal sealed class IssuedPassport
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid BatchId { get; init; }

    /// <summary>The tray or pots the passported plants came from.</summary>
    public required Guid ContainerId { get; init; }

    public required int Quantity { get; init; }

    /// <summary>Botanical name: genus and species, or the genus if the species isn't known.</summary>
    public required string PartA { get; init; }

    /// <summary>The nursery's registration number, without a "GB" prefix.</summary>
    public required string PartB { get; init; }

    /// <summary>The traceability code, which leads back to the batch.</summary>
    public required string PartC { get; init; }

    /// <summary>Country of origin as a two-letter code, worked out at issue.</summary>
    public required string PartD { get; init; }

    /// <summary>The rule that gave Part D, in words, so it can be checked later.</summary>
    public required string OriginRule { get; init; }

    public bool? ActiveGrowthConfirmed { get; init; }

    public Guid? CustomerId { get; init; }

    /// <summary>The package it was issued for, when it covers a package of several batches with others.</summary>
    public Guid? PackageId { get; init; }

    /// <summary>Who the plants went to, as written at issue. A business name; kept when a customer's contact details are forgotten.</summary>
    public required string IssuedTo { get; init; }

    public required DateOnly IssuedOn { get; init; }

    /// <summary>The examination recorded on the tray before the passport was issued.</summary>
    public required Guid ExaminationEventId { get; init; }

    public required Guid IssuedBy { get; init; }

    public string? RequestFingerprint { get; init; }

    public DateTimeOffset CreatedAt { get; private set; }
}
