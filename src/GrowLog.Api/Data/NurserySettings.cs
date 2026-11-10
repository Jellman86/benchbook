namespace GrowLog.Api.Data;

/// <summary>Settings for the whole nursery. There is only ever one row.</summary>
internal sealed class NurserySettings
{
    public const int SingleId = 1;

    public int Id { get; init; } = SingleId;

    /// <summary>How much signing in the nursery wants.</summary>
    public SignInLevel SignInLevel { get; set; } = SignInLevel.Personal;
}
