namespace Hortiplanner.Api.Buying;

/// <summary>One item line read from an invoice: what it was, how many, and its cost before VAT.</summary>
internal sealed record DraftLine(string Description, int? Quantity, decimal NetCost);
