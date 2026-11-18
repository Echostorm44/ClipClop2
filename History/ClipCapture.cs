using System.Buffers.Binary;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Cascade.UI;

namespace ClipClop2.History;

/// <summary>Turns a raw clipboard capture into a history entry: hash, kind and searchable text.</summary>
internal static partial class ClipCapture
{
    /// <summary>
    /// Formats left out of capture. "FileContents" makes Explorer render whole files (we keep only the
    /// paths); "DataObject" and "Ole Private Data" are pointers into the copying process — useless
    /// once it changes and different on every copy, so they would also defeat duplicate detection.
    /// The filter runs before data is requested, so excluded formats are never rendered.
    /// </summary>
    public static bool IncludeFormat(string name) => name is not ("FileContents" or "DataObject" or "Ole Private Data");

    public static ClipEntry? Create(ClipboardRawSnapshot snapshot, string sourceApp, DateTime copiedAt)
    {
        if (snapshot.Formats.Count == 0)
        {
            return null;
        }

        var formats = new List<ClipFormat>(snapshot.Formats.Count);
        foreach (ClipboardRawFormat raw in snapshot.Formats)
        {
            byte[] data = MemoryMarshal.TryGetArray(raw.Data, out ArraySegment<byte> segment)
                && segment.Offset == 0 && segment.Count == segment.Array!.Length
                ? segment.Array
                : raw.Data.ToArray();
            formats.Add(new ClipFormat(raw.Id, raw.Name, data));
        }

        if (Classify(formats) is not var (kind, searchText))
        {
            return null;
        }
        return new ClipEntry(kind, formats, IdentityHash(kind, formats, searchText), searchText, sourceApp, copiedAt);
    }

    /// <summary>
    /// What makes two copies the same history item: the text for text kinds, the paths for files,
    /// the image data for images. The formats around it (HTML, RTF, an app's private formats such as
    /// OneNote's) differ from copy to copy and from app to app, so they are left out: copying
    /// "Hi Bob" five times, from anywhere, keeps one entry.
    /// </summary>
    public static UInt128 IdentityHash(ClipKind kind, IReadOnlyList<ClipFormat> formats, string searchText)
    {
        var hasher = new XxHash128();
        Span<byte> buffer = stackalloc byte[4];
        buffer[0] = (byte)kind;
        hasher.Append(buffer[..1]);
        if (kind != ClipKind.Image)
        {
            hasher.Append(MemoryMarshal.AsBytes(searchText.AsSpan()));
            return hasher.GetCurrentHashAsUInt128();
        }
        foreach (ClipFormat format in formats)
        {
            if (!IsImageFormat(format.Name))
            {
                continue;
            }
            // Name and length too, so the same bytes under another format differ.
            byte[] data = format.ReadData();
            hasher.Append(MemoryMarshal.AsBytes(format.Name.AsSpan()));
            BinaryPrimitives.WriteInt32LittleEndian(buffer, data.Length);
            hasher.Append(buffer);
            hasher.Append(data);
        }
        return hasher.GetCurrentHashAsUInt128();
    }

    /// <summary>The formats that carry an image's pixels (what identifies an image entry).</summary>
    public static bool IsImageFormat(string name) => name is "PNG" or "CF_DIBV5" or "CF_DIB" or "CF_ENHMETAFILE";

    /// <summary>The entry kind and its searchable text, or null when nothing is displayable.</summary>
    internal static (ClipKind Kind, string SearchText)? Classify(IReadOnlyList<ClipFormat> formats)
    {
        if (FindData(formats, "CF_HDROP") is { } drop)
        {
            return (ClipKind.Files, string.Join('\n', ParseDropFiles(drop)));
        }

        if (FindData(formats, "CF_UNICODETEXT") is { } unicode)
        {
            string text = DecodeUnicode(unicode);
            if (text.Length > 0)
            {
                return (ClassifyText(text.Trim()), text);
            }
        }

        if (FindData(formats, "PNG") is not null || FindData(formats, "CF_DIBV5") is not null
            || FindData(formats, "CF_DIB") is not null || FindData(formats, "CF_ENHMETAFILE") is not null)
        {
            return (ClipKind.Image, "");
        }

        return null;
    }

    internal static ClipKind ClassifyText(string trimmed)
    {
        if (trimmed.Length > 2048 || trimmed.Contains('\n'))
        {
            return ClipKind.Text;
        }
        if (ColorPattern().IsMatch(trimmed))
        {
            return ClipKind.Color;
        }
        if (EmailPattern().IsMatch(trimmed))
        {
            return ClipKind.Email;
        }
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri) && !trimmed.Contains(' ')
            && uri.Scheme is "http" or "https" or "ftp" or "mailto")
        {
            return ClipKind.Link;
        }
        return ClipKind.Text;
    }

    internal static string DecodeUnicode(byte[] data)
    {
        ReadOnlySpan<char> chars = MemoryMarshal.Cast<byte, char>(data.AsSpan(0, data.Length & ~1));
        int end = chars.IndexOf('\0');
        return new string(end >= 0 ? chars[..end] : chars);
    }

    /// <summary>Paths in a DROPFILES block (CF_HDROP).</summary>
    internal static List<string> ParseDropFiles(byte[] data)
    {
        var paths = new List<string>();
        if (data.Length < 20)
        {
            return paths;
        }
        int offset = BinaryPrimitives.ReadInt32LittleEndian(data);
        bool wide = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(16)) != 0;
        if (offset < 20 || offset >= data.Length)
        {
            return paths;
        }

        string all = wide
            ? new string(MemoryMarshal.Cast<byte, char>(data.AsSpan(offset, (data.Length - offset) & ~1)))
            : Encoding.Default.GetString(data, offset, data.Length - offset);
        foreach (string path in all.Split('\0'))
        {
            if (path.Length == 0)
            {
                break; // the double null ends the list
            }
            paths.Add(path);
        }
        return paths;
    }

    private static byte[]? FindData(IReadOnlyList<ClipFormat> formats, string name)
    {
        foreach (ClipFormat format in formats)
        {
            if (format.Name == name)
            {
                return format.ReadData();
            }
        }
        return null;
    }

    [GeneratedRegex(@"^(#([0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})|(rgba?|hsla?)\(\s*[\d.%]+\s*(,\s*[\d.%]+\s*){2,3}\))$")]
    private static partial Regex ColorPattern();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();
}
