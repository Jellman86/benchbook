namespace GrowLog.Api.Labels;

/// <summary>The style chosen for a label, with the nursery's name and logo for it to show.</summary>
internal sealed record LabelContext(LabelStyle Style, string? NurseryName, byte[]? Logo, string? LogoContentType);
