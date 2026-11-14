using Microsoft.AspNetCore.DataProtection;

namespace Hortiplanner.Api.Assistant;

/// <summary>Keeps the owner's OpenRouter key encrypted at rest. The key never leaves the server.</summary>
internal sealed class AssistantKeys(IDataProtectionProvider protection)
{
    // The app's old name: saved secrets were protected with it and can only be read back with the same purpose.
    private readonly IDataProtector protector = protection.CreateProtector("GrowLog.Assistant.OpenRouterKey");

    public string Protect(string key) => protector.Protect(key);

    /// <summary>The key, or null if there's none or it can no longer be read (the server's keys changed).</summary>
    public string? Read(string? stored)
    {
        if (stored is null)
        {
            return null;
        }

        try
        {
            return protector.Unprotect(stored);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}
