using Microsoft.AspNetCore.Identity;

namespace GrowLog.Api.Data;

internal sealed class Supplier : IAudited, IArchivable
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    [PersonalData]
    public string? ContactName { get; set; }

    [PersonalData]
    public string? Phone { get; set; }

    [PersonalData]
    public string? Email { get; set; }

    [PersonalData]
    public string? Address { get; set; }

    public string? AccountReference { get; set; }

    public string? Supplies { get; set; }

    public int? TypicalLeadTimeDays { get; set; }

    public string? PassportRegistrationNumber { get; set; }

    /// <summary>Other numbers the supplier is registered under, such as for seed marketing standards.</summary>
    public string? OtherRegistrations { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }
}
