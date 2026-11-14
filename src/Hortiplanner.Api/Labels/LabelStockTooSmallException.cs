namespace Hortiplanner.Api.Labels;

internal sealed class LabelStockTooSmallException() : Exception("The label stock is too small for this passport. Choose a larger label.");
