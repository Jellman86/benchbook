using Hortiplanner.Api.Data;

namespace Hortiplanner.Api.Accounts;

/// <summary>What the nursery's chosen sign-in level allows.</summary>
internal static class SignInRules
{
    public const string TeamName = "Nursery team";

    /// <summary>Whether someone can sign in just by tapping their name. Never the owner.</summary>
    public static bool CanTapName(SignInLevel level, bool chosenForThem, bool isOwner) =>
        !isOwner && level switch
        {
            SignInLevel.Open or SignInLevel.Names => true,
            SignInLevel.Personal => chosenForThem,
            _ => false,
        };

    /// <summary>Whether a person can be set up to sign in by tapping their name.</summary>
    public static bool AllowsNameOnly(SignInLevel level) => level != SignInLevel.Strict;

    public static bool BenchNeedsPin(SignInLevel level) => level is SignInLevel.Personal or SignInLevel.Strict;
}
