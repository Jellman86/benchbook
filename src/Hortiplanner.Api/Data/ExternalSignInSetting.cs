using Microsoft.AspNetCore.Identity;

namespace Hortiplanner.Api.Data;

/// <summary>The owner's choice of shared staff login. One row at most: until it exists, deployment settings apply.</summary>
internal sealed class ExternalSignInSetting : IAudited
{
    public int Id { get; init; } = 1;

    public bool Enabled { get; set; }

    public string? Authority { get; set; }

    public string? ClientId { get; set; }

    /// <summary>Encrypted with the application's data protection keys. Left out of the change history.</summary>
    [PersonalData]
    public string? ProtectedClientSecret { get; set; }

    public required string DisplayName { get; set; }

    public string? OwnerGroup { get; set; }
}
