namespace GrowLog.Api.Data;

internal sealed class Supplier
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    public string? ContactName { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Address { get; set; }

    public string? AccountReference { get; set; }

    public string? Supplies { get; set; }

    public int? TypicalLeadTimeDays { get; set; }

    public string? PassportRegistrationNumber { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }
}
