using System.Buffers.Binary;
using System.IO.Compression;

namespace GrowLog.Api.Printing;

/// <summary>
/// A label as printer dots: each one black or white, eight to a byte, most significant bit first, a set bit
/// black. A thermal printer has no greys, so anything darker than mid-grey prints.
/// </summary>
internal sealed class LabelBitmap
{
    private LabelBitmap(int width, int height, byte[] dots)
    {
        Width = width;
        Height = height;
        Dots = dots;
    }

    public int Width { get; }

    public int Height { get; }

    public int BytesPerRow => (Width + 7) / 8;

    /// <summary>Rows of <see cref="BytesPerRow"/> bytes, top first.</summary>
    public byte[] Dots { get; }

    /// <summary>
    /// Reads the 8-bit, non-interlaced PNG that Typst writes, laying any transparency over white paper.
    /// It is a small reader for that one case, so GrowLog needs no image library.
    /// </summary>
    public static LabelBitmap FromPng(byte[] png)
    {
        ReadOnlySpan<byte> signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

        if (png.Length < 8 || !png.AsSpan(0, 8).SequenceEqual(signature))
        {
            throw new InvalidDataException("Not a PNG.");
        }

        var (width, height, channels, palette, compressed) = ReadChunks(png);
        var stride = width * channels;
        var raw = new byte[(stride + 1) * height];
        using (compressed)
        using (var inflate = new ZLibStream(compressed, CompressionMode.Decompress))
        {
            inflate.ReadExactly(raw);
        }

        var bitmap = new LabelBitmap(width, height, new byte[((width + 7) / 8) * height]);
        var previous = new byte[stride];
        var row = new byte[stride];

        for (var y = 0; y < height; y++)
        {
            var filter = raw[y * (stride + 1)];
            raw.AsSpan(y * (stride + 1) + 1, stride).CopyTo(row);
            Unfilter(filter, row, previous, channels);

            for (var x = 0; x < width; x++)
            {
                if (Brightness(row.AsSpan(x * channels, channels), palette) < 128)
                {
                    bitmap.Dots[y * bitmap.BytesPerRow + x / 8] |= (byte)(0x80 >> (x % 8));
                }
            }

            (previous, row) = (row, previous);
        }

        return bitmap;
    }

    private static (int Width, int Height, int Channels, byte[]? Palette, MemoryStream Compressed) ReadChunks(byte[] png)
    {
        int width = 0, height = 0, channels = 0;
        byte[]? palette = null;
        var compressed = new MemoryStream();

        for (var at = 8; at + 8 <= png.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            var type = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
            var body = png.AsSpan(at + 8, length);

            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(body);
                    height = BinaryPrimitives.ReadInt32BigEndian(body[4..]);

                    if (body[8] != 8 || body[12] != 0)
                    {
                        throw new InvalidDataException("Only 8-bit, non-interlaced PNGs are read.");
                    }

                    channels = body[9] switch
                    {
                        0 => 1, // grey
                        2 => 3, // red, green, blue
                        3 => 1, // palette
                        4 => 2, // grey and alpha
                        6 => 4, // red, green, blue and alpha
                        _ => throw new InvalidDataException("Unknown PNG colour type."),
                    };

                    if (body[9] == 3)
                    {
                        palette = [];
                    }

                    break;
                case "PLTE":
                    palette = body.ToArray();
                    break;
                case "IDAT":
                    compressed.Write(body);
                    break;
            }

            at += 12 + length;
        }


        if (width <= 0 || height <= 0 || channels == 0)
        {
            throw new InvalidDataException("The PNG has no picture.");
        }

        compressed.Position = 0;
        return (width, height, channels, palette, compressed);
    }

    private static void Unfilter(byte filter, byte[] row, byte[] previous, int step)
    {
        for (var i = 0; i < row.Length; i++)
        {
            int left = i >= step ? row[i - step] : 0;
            int up = previous[i];
            int corner = i >= step ? previous[i - step] : 0;

            row[i] = (byte)(row[i] + filter switch
            {
                0 => 0,
                1 => left,
                2 => up,
                3 => (left + up) / 2,
                4 => Paeth(left, up, corner),
                _ => throw new InvalidDataException("Unknown PNG filter."),
            });
        }
    }

    private static int Paeth(int left, int up, int corner)
    {
        var estimate = left + up - corner;
        var fromLeft = Math.Abs(estimate - left);
        var fromUp = Math.Abs(estimate - up);
        var fromCorner = Math.Abs(estimate - corner);
        return fromLeft <= fromUp && fromLeft <= fromCorner ? left : fromUp <= fromCorner ? up : corner;
    }

    /// <summary>How light a dot looks on white paper, from 0 (black) to 255 (white).</summary>
    private static int Brightness(ReadOnlySpan<byte> pixel, byte[]? palette)
    {
        if (palette is not null)
        {
            var index = pixel[0] * 3;
            return index + 2 < palette.Length ? Grey(palette[index], palette[index + 1], palette[index + 2]) : 255;
        }

        var (grey, alpha) = pixel.Length switch
        {
            1 => (pixel[0], 255),
            2 => (pixel[0], pixel[1]),
            3 => (Grey(pixel[0], pixel[1], pixel[2]), 255),
            _ => (Grey(pixel[0], pixel[1], pixel[2]), pixel[3]),
        };

        return (grey * alpha + 255 * (255 - alpha)) / 255;
    }

    private static int Grey(int red, int green, int blue) => (red * 299 + green * 587 + blue * 114) / 1000;
}
