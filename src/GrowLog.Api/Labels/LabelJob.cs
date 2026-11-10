namespace GrowLog.Api.Labels;

/// <summary>One label ready to draw: its kind, what goes on it, the QR code's text if any, and its style.</summary>
internal sealed record LabelJob(LabelKind Kind, object Data, string? QrText, LabelContext Context);
