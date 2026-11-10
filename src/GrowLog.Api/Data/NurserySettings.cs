namespace GrowLog.Api.Data;

/// <summary>Settings for the whole nursery. There is only ever one row.</summary>
internal sealed class NurserySettings
{
    public const int SingleId = 1;

    public int Id { get; init; } = SingleId;

    /// <summary>
    /// Whether staff can sign in just by tapping their name. Only safe where GrowLog can't be reached from
    /// outside the nursery, so the owner turns it on knowingly.
    /// </summary>
    public bool AllowNameOnlySignIn { get; set; }
}
