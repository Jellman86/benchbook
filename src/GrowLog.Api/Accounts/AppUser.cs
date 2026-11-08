using Microsoft.AspNetCore.Identity;

namespace GrowLog.Api.Accounts;

/// <summary>A person who records work. UserName holds the name shown to them and to colleagues.</summary>
internal sealed class AppUser : IdentityUser<Guid>;
