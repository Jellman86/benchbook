namespace Hortiplanner.Api.Lookup;

/// <summary>Reads pictures from uploads and downloads, trusting only what the bytes say they are.</summary>
internal static class ImageContent
{
    public const int MaxBytes = 8 * 1024 * 1024;

    public static string? DetectType(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith<byte>([0xFF, 0xD8, 0xFF]))
        {
            return "image/jpeg";
        }

        if (content.StartsWith<byte>([0x89, 0x50, 0x4E, 0x47]))
        {
            return "image/png";
        }

        return content.Length >= 12 && content.StartsWith("RIFF"u8) && content[8..12].SequenceEqual("WEBP"u8)
            ? "image/webp"
            : null;
    }

    /// <summary>Gives the picture, or the status code that explains why there isn't one.</summary>
    public static async Task<(ImageFile? Image, int Refusal)> ReadAsync(Stream source, CancellationToken cancellationToken)
    {
        using var body = new MemoryStream();
        var buffer = new byte[81920];
        int read;

        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            body.Write(buffer, 0, read);

            if (body.Length > MaxBytes)
            {
                return (null, StatusCodes.Status413PayloadTooLarge);
            }
        }

        var content = body.ToArray();

        return DetectType(content) is { } contentType
            ? (new ImageFile(content, contentType), StatusCodes.Status200OK)
            : (null, StatusCodes.Status415UnsupportedMediaType);
    }
}
