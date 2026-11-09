namespace GrowLog.Api.Accounts;

internal sealed record ExternalSignInConfiguration(
    bool Enabled,
    string? Authority,
    string? ClientId,
    string? ClientSecret,
    string DisplayName,
    string? OwnerGroup)
{
    public bool IsOn => Enabled && IsSecureAddress(Authority) && !string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(ClientSecret);

    public static bool IsSecureAddress(string? address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal);
}
