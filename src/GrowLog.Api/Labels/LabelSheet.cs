namespace GrowLog.Api.Labels;

/// <summary>
/// A sheet of A4 stickers: how many stickers across and down, their size, and the gaps between them. The grid sits
/// in the middle of the page, as on the common sheets.
/// </summary>
/// <param name="Id">A short name for the sheet, such as "21".</param>
/// <param name="Name">What it's called on screen, with the sheets it matches.</param>
internal sealed record LabelSheet(string Id, string Name, int Columns, int Rows, decimal WidthMm, decimal HeightMm, decimal ColumnGapMm, decimal RowGapMm)
{
    public int PerSheet => Columns * Rows;
}
