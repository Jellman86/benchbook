namespace Hortiplanner.Api.Data;

/// <summary>
/// How much signing in the nursery wants, chosen by the owner. The owner's own account always needs a
/// password or passkey, whatever is chosen, so the nursery's settings stay protected.
/// </summary>
internal enum SignInLevel
{
    /// <summary>No signing in: devices go straight in, and work is recorded as the nursery team.</summary>
    Open,

    /// <summary>Staff tap their name, with nothing to type, at a bench too.</summary>
    Names,

    /// <summary>Each person signs in the way the owner set for them; a bench needs a PIN.</summary>
    Personal,

    /// <summary>No tapping a name: a password, passkey or staff login, and a PIN at a bench.</summary>
    Strict,
}
