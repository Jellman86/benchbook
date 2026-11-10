namespace GrowLog.Api.Printing;

/// <summary>
/// What a label printer understands. Between them these cover most desktop thermal printers: ZPL for
/// Zebra and the many printers that copy it, TSPL for TSC and the many that copy that.
/// </summary>
internal enum PrinterLanguage
{
    Zpl,
    Tspl,
}
