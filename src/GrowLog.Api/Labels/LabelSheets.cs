namespace GrowLog.Api.Labels;

/// <summary>The A4 sticker sheets GrowLog can print on, and where each label lands on them.</summary>
internal static class LabelSheets
{
    public const decimal PageWidthMm = 210m;
    public const decimal PageHeightMm = 297m;

    /// <summary>The usual A4 sheets: sizes from the makers' templates, which most own-brand sheets copy.</summary>
    public static readonly IReadOnlyList<LabelSheet> All =
    [
        new("65", "65 a sheet, 38 × 21 mm (like Avery L7651)", 5, 13, 38.1m, 21.2m, 2.5m, 0m),
        new("24", "24 a sheet, 64 × 34 mm (like Avery L7159)", 3, 8, 63.5m, 33.9m, 2.54m, 0m),
        new("21", "21 a sheet, 64 × 38 mm (like Avery L7160)", 3, 7, 63.5m, 38.1m, 2.54m, 0m),
        new("14", "14 a sheet, 99 × 38 mm (like Avery L7163)", 2, 7, 99.1m, 38.1m, 2.5m, 0m),
        new("8", "8 a sheet, 99 × 68 mm (like Avery L7165)", 2, 4, 99.1m, 67.7m, 2.5m, 0m),
    ];

    public static LabelSheet? Find(string id) => All.FirstOrDefault(sheet => string.Equals(sheet.Id, id, StringComparison.Ordinal));

    /// <summary>
    /// Where each of so many labels goes, page by page, from the sticker numbered <paramref name="startAt"/> on the
    /// first sheet (counting across, then down, from 1), so a part-used sheet can be finished first.
    /// </summary>
    public static List<List<Cell>> Lay(LabelSheet sheet, int count, int startAt)
    {
        var left = (PageWidthMm - (sheet.Columns * sheet.WidthMm) - ((sheet.Columns - 1) * sheet.ColumnGapMm)) / 2;
        var top = (PageHeightMm - (sheet.Rows * sheet.HeightMm) - ((sheet.Rows - 1) * sheet.RowGapMm)) / 2;
        var skip = Math.Clamp(startAt, 1, sheet.PerSheet) - 1;
        var pages = new List<List<Cell>>();

        for (var index = 0; index < count; index++)
        {
            var place = skip + index;
            var page = place / sheet.PerSheet;
            var spot = place % sheet.PerSheet;

            while (pages.Count <= page)
            {
                pages.Add([]);
            }

            var column = spot % sheet.Columns;
            var row = spot / sheet.Columns;
            pages[page].Add(new Cell(index, left + (column * (sheet.WidthMm + sheet.ColumnGapMm)), top + (row * (sheet.HeightMm + sheet.RowGapMm))));
        }

        return pages;
    }

    /// <param name="Index">Which label goes here.</param>
    /// <param name="X">From the left edge of the page, in millimetres.</param>
    /// <param name="Y">From the top edge of the page, in millimetres.</param>
    internal sealed record Cell(int Index, decimal X, decimal Y);
}
