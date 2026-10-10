using System.Buffers.Binary;
using Cascade.UI;
using SharpImage.Core;
using SharpImage.Formats;
using SharpImage.Transform;
using SharpImageFrame = SharpImage.Image.ImageFrame;

namespace ClipClop2.History;

/// <summary>
/// Display-side reading of entries: the list line, image sizes from headers, and preview thumbnails.
/// The original bytes are never re-encoded; thumbnails are decoded copies for display only.
/// </summary>
internal static class ClipImages
{
    private const int MaxLineLength = 240;

    public static string DisplayLine(ClipEntry entry)
    {
        switch (entry.Kind)
        {
            case ClipKind.Image:
                return entry.ImageSize is { } size ? $"Image ({size.Width}×{size.Height})" : "Image";
            case ClipKind.Files:
                string[] paths = entry.SearchText.Split('\n');
                string first = System.IO.Path.GetFileName(paths[0].TrimEnd('\\'));
                return paths.Length == 1 ? first : $"{first} + {paths.Length - 1} more";
            default:
                return FirstLine(entry.SearchText);
        }
    }

    /// <summary>The first non-blank line, trimmed and capped.</summary>
    public static string FirstLine(string text)
    {
        ReadOnlySpan<char> rest = text;
        while (rest.Length > 0)
        {
            int end = rest.IndexOfAny('\r', '\n');
            ReadOnlySpan<char> line = (end < 0 ? rest : rest[..end]).Trim();
            if (line.Length > 0)
            {
                return line.Length > MaxLineLength ? string.Concat(line[..MaxLineLength], "…") : line.ToString();
            }
            if (end < 0)
            {
                break;
            }
            rest = rest[(end + 1)..];
        }
        return "";
    }

    public static (int Width, int Height)? ReadSize(ClipEntry entry)
    {
        if (entry.Find("PNG") is { } png)
        {
            byte[] data = png.ReadData();
            // Signature (8), IHDR length + type (8), then width and height big-endian.
            if (data.Length >= 24)
            {
                return (BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(20)));
            }
        }
        if ((entry.Find("CF_DIBV5") ?? entry.Find("CF_DIB")) is { } dib)
        {
            byte[] data = dib.ReadData();
            if (data.Length >= 12)
            {
                return (BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4)), Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8))));
            }
        }
        return null;
    }

    /// <summary>
    /// Decodes the entry's image and scales it to fit the box (never up). Prefers PNG (it carries
    /// alpha faithfully), then the DIB formats. Null for formats SharpImage cannot read (e.g. EMF).
    /// </summary>
    public static ImageSource? DecodeThumbnail(ClipEntry entry, int maxWidth, int maxHeight)
    {
        byte[]? encoded = null;
        ImageFileFormat format = ImageFileFormat.Unknown;
        if (entry.Find("PNG") is { } png)
        {
            encoded = png.ReadData();
            format = ImageFileFormat.Png;
        }
        else if ((entry.Find("CF_DIBV5") ?? entry.Find("CF_DIB")) is { } dib)
        {
            encoded = WrapDibAsBmp(dib.ReadData());
            format = ImageFileFormat.Bmp;
        }
        if (encoded is null)
        {
            return null;
        }

        try
        {
            using SharpImageFrame frame = FormatRegistry.Decode(encoded, format);
            if (frame.Columns <= maxWidth && frame.Rows <= maxHeight)
            {
                return ToImageSource(frame);
            }
            using SharpImageFrame scaled = Resize.Thumbnail(frame, maxWidth, maxHeight);
            return ToImageSource(scaled);
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or FormatException or EndOfStreamException or ArgumentException)
        {
            return null;
        }
    }

    // A clipboard DIB is a BMP file without its 14-byte file header. Pixels start after the header,
    // the bit-field masks a v3 header with BI_BITFIELDS carries, and any palette.
    private static byte[] WrapDibAsBmp(byte[] dib)
    {
        int headerSize = dib.Length >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(dib) : 0;
        int extra = 0;
        if (headerSize == 40 && dib.Length >= 20)
        {
            uint compression = BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(16));
            extra = compression switch { 3 => 12, 6 => 16, _ => 0 };
        }
        if (dib.Length >= 36)
        {
            int bits = BinaryPrimitives.ReadUInt16LittleEndian(dib.AsSpan(14));
            uint colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(32));
            if (bits <= 8)
            {
                extra += 4 * (int)(colorsUsed > 0 ? colorsUsed : 1u << bits);
            }
        }

        byte[] file = new byte[14 + dib.Length];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(2), file.Length);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(10), 14 + headerSize + extra);
        dib.CopyTo(file, 14);
        return file;
    }

    private static ImageSource ToImageSource(SharpImageFrame frame)
    {
        int width = (int)frame.Columns;
        int height = (int)frame.Rows;
        int channels = frame.NumberOfChannels;
        bool alpha = frame.HasAlpha;
        byte[] rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<ushort> row = frame.GetPixelRow(y);
            int dst = y * width * 4;
            for (int x = 0; x < width; x++, dst += 4)
            {
                int src = x * channels;
                ushort r = row[src];
                rgba[dst] = Quantum.ScaleToByte(r);
                rgba[dst + 1] = Quantum.ScaleToByte(channels > 1 ? row[src + 1] : r);
                rgba[dst + 2] = Quantum.ScaleToByte(channels > 2 ? row[src + 2] : r);
                rgba[dst + 3] = alpha ? Quantum.ScaleToByte(row[src + channels - 1]) : (byte)255;
            }
        }
        return ImageSource.FromBytes(rgba, width, height);
    }
}
