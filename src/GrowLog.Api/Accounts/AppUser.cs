using Microsoft.AspNetCore.Identity;

namespace GrowLog.Api.Accounts;

/// <summary>A person who records work. UserName holds the name shown to them and to colleagues.</summary>
internal sealed class AppUser : IdentityUser<Guid>
{
    /// <summary>Hash of the short PIN used at a bench station.</summary>
    public string? PinHash { get; set; }

    /// <summary>Set when someone leaves. They are kept so that older records still name them.</summary>
    public DateTimeOffset? ArchivedAt { get; set; }
}
