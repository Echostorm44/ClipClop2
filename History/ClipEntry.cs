using ClipClop2.Storage;

namespace ClipClop2.History;

/// <summary>What a history entry is, for icons, filtering and the preview pane.</summary>
internal enum ClipKind
{
    Text,
    Link,
    Email,
    Color,
    Image,
    Files,
}

/// <summary>
/// One clipboard format captured from a copy: the format id, its name (ids of registered formats
/// differ between sessions, so the name is what identifies it on restore) and the bytes — in RAM,
/// or for large payloads in an encrypted cache file (<see cref="PayloadCache"/>).
/// </summary>
internal sealed class ClipFormat
{
    private byte[]? data;
    private string? cachePath;

    public ClipFormat(uint id, string name, byte[] data)
    {
        Id = id;
        Name = name;
        this.data = data;
        Length = data.Length;
    }

    public uint Id { get; }

    public string Name { get; }

    /// <summary>Payload size in bytes, wherever it lives.</summary>
    public int Length { get; }

    public bool IsCached => cachePath is not null;

    /// <summary>The bytes (decrypted from the cache when spilled).</summary>
    public byte[] ReadData() => data ?? PayloadCache.Read(cachePath!);

    /// <summary>Moves a large payload out of RAM into the cache.</summary>
    public void Spill(PayloadCache cache)
    {
        if (data is null || data.Length <= PayloadCache.SpillThresholdBytes)
        {
            return;
        }
        cachePath = cache.Write(data);
        data = null;
    }

    /// <summary>Brings a spilled payload back into RAM (pinning: pins persist on their own).</summary>
    public void Load()
    {
        if (cachePath is null)
        {
            return;
        }
        data = PayloadCache.Read(cachePath);
        DeleteCache();
    }

    public void DeleteCache()
    {
        if (cachePath is not null)
        {
            PayloadCache.TryDelete(cachePath);
            cachePath = null;
        }
    }
}

/// <summary>
/// One history entry. Long-lived and few (hundreds), so a class; the byte payloads are what
/// dominate memory and they are counted in <see cref="SizeBytes"/>.
/// </summary>
internal sealed class ClipEntry
{
    public ClipEntry(ClipKind kind, IReadOnlyList<ClipFormat> formats, UInt128 contentHash, string searchText, string sourceApp, DateTime copiedAt)
    {
        Kind = kind;
        Formats = formats;
        ContentHash = contentHash;
        SearchText = searchText;
        SourceApp = sourceApp;
        CopiedAt = copiedAt;

        long bytes = searchText.Length * sizeof(char);
        foreach (ClipFormat format in formats)
        {
            bytes += format.Length;
        }
        SizeBytes = bytes;
    }

    public ClipKind Kind { get; }

    /// <summary>Every format restored on paste, in the order the source offered them.</summary>
    public IReadOnlyList<ClipFormat> Formats { get; }

    /// <summary>Hash over every captured format; equal hashes are confirmed byte-for-byte before merging.</summary>
    public UInt128 ContentHash { get; }

    /// <summary>The plain text searched by the filter box (file paths for files, empty for images).</summary>
    public string SearchText { get; }

    /// <summary>The source app's executable path (empty when unknown).</summary>
    public string SourceApp { get; }

    /// <summary>When this content was last copied (re-copying moves the entry and updates this).</summary>
    public DateTime CopiedAt { get; internal set; }

    public bool Pinned { get; internal set; }

    /// <summary>Everything this entry holds, RAM and cache together; what the history budget counts.</summary>
    public long SizeBytes { get; }

    private string? displayLine;
    private (int Width, int Height)? imageSize;
    private bool imageSizeRead;

    /// <summary>One line for the history list. Built on first display and kept.</summary>
    public string DisplayLine => displayLine ??= ClipImages.DisplayLine(this);

    /// <summary>Pixel size of an image entry, read from its PNG or DIB header on first use.</summary>
    public (int Width, int Height)? ImageSize
    {
        get
        {
            if (!imageSizeRead)
            {
                imageSize = Kind == ClipKind.Image ? ClipImages.ReadSize(this) : null;
                imageSizeRead = true;
            }
            return imageSize;
        }
    }

    public bool HasPlainText => Find("CF_UNICODETEXT") is not null;

    public ClipFormat? Find(string name)
    {
        foreach (ClipFormat format in Formats)
        {
            if (format.Name == name)
            {
                return format;
            }
        }
        return null;
    }

    public bool HasSameContent(ClipEntry other)
    {
        if (ContentHash != other.ContentHash || Formats.Count != other.Formats.Count)
        {
            return false;
        }
        for (int i = 0; i < Formats.Count; i++)
        {
            if (Formats[i].Name != other.Formats[i].Name || Formats[i].Length != other.Formats[i].Length)
            {
                return false;
            }
        }
        // Equal 128-bit hashes and shapes: confirm byte for byte (reads spilled payloads, rarely).
        for (int i = 0; i < Formats.Count; i++)
        {
            if (!Formats[i].ReadData().AsSpan().SequenceEqual(other.Formats[i].ReadData()))
            {
                return false;
            }
        }
        return true;
    }

    public void Spill(PayloadCache cache)
    {
        foreach (ClipFormat format in Formats)
        {
            format.Spill(cache);
        }
    }

    /// <summary>Releases cache files (the entry left history).</summary>
    public void Release()
    {
        foreach (ClipFormat format in Formats)
        {
            format.DeleteCache();
        }
    }
}
