namespace Hortiplanner.Api.Buying;

/// <summary>What a supplier's invoice seems to say. Suggestions only: a person checks them before saving.</summary>
internal sealed record InvoiceDraft(string? Reference, DateOnly? Date, IReadOnlyList<DraftLine> Lines);
