using Microsoft.AspNetCore.Identity;

namespace GrowLog.Api.Data;

/// <summary>A trade customer. Contact details are personal data and can be forgotten; invoices keep what they were issued with.</summary>
internal sealed class Customer : IAudited, IArchivable
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    [PersonalData]
    public string? Address { get; set; }

    [PersonalData]
    public string? Email { get; set; }

    [PersonalData]
    public string? Phone { get; set; }

    public string? VatNumber { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }
}
