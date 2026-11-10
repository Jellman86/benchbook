using System.Globalization;
using System.Text;

namespace GrowLog.Api.Printing;

internal static class PrinterCommands
{
    /// <summary>The label as one picture, then how many copies, so the printer does the repeating.</summary>
    public static byte[] For(PrinterLanguage language, LabelBitmap label, int copies, int dotsPerInch) => language switch
    {
        PrinterLanguage.Zpl => Zpl(label, copies),
        PrinterLanguage.Tspl => Tspl(label, copies, dotsPerInch),
        _ => throw new ArgumentOutOfRangeException(nameof(language)),
    };

    private static byte[] Zpl(LabelBitmap label, int copies)
    {
        var total = label.Dots.Length;
        var command = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"^XA^PW{label.Width}^LL{label.Height}^LH0,0^FO0,0")
            .Append(CultureInfo.InvariantCulture, $"^GFA,{total},{total},{label.BytesPerRow},")
            .Append(Convert.ToHexString(label.Dots))
            .Append(CultureInfo.InvariantCulture, $"^FS^PQ{copies}^XZ");
        return Encoding.ASCII.GetBytes(command.ToString());
    }

    /// <summary>TSPL's bitmap marks white dots, the opposite of ours, so each byte is flipped.</summary>
    private static byte[] Tspl(LabelBitmap label, int copies, int dotsPerInch)
    {
        static string Millimetres(int dots, int dotsPerInch) =>
            (dots * 25.4 / dotsPerInch).ToString("0.#", CultureInfo.InvariantCulture);

        using var command = new MemoryStream();
        var start = string.Create(
            CultureInfo.InvariantCulture,
            $"SIZE {Millimetres(label.Width, dotsPerInch)} mm,{Millimetres(label.Height, dotsPerInch)} mm\r\nGAP 2 mm,0 mm\r\nDIRECTION 1\r\nCLS\r\nBITMAP 0,0,{label.BytesPerRow},{label.Height},0,");
        command.Write(Encoding.ASCII.GetBytes(start));

        foreach (var dots in label.Dots)
        {
            command.WriteByte((byte)~dots);
        }

        command.Write(Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"\r\nPRINT 1,{copies}\r\n")));
        return command.ToArray();
    }
}
