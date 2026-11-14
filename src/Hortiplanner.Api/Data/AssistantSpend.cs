namespace Hortiplanner.Api.Data;

/// <summary>
/// What one question to Ask Hortiplanner cost, and who asked it, so each person can see what their questions cost the
/// nursery and the owner can see it for everyone. The question itself isn't kept.
/// </summary>
internal sealed class AssistantSpend
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid AskedBy { get; init; }

    public required DateTimeOffset AskedAt { get; init; }

    /// <summary>In US dollars, as OpenRouter charges.</summary>
    public required decimal Cost { get; init; }
}
