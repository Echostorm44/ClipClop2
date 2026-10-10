using System.Diagnostics;
using Cascade.UI;
using ClipClop2.History;

namespace ClipClop2.Ui;

/// <summary>
/// Everything the popup decodes for display: source-app names and icons, file icons and image
/// thumbnails. Produced on first view (never at capture), bounded, and dropped when the popup hides
/// so the tray-resident app holds no decoded images while idle.
/// </summary>
internal sealed class PreviewCache
{
    private const int MaxThumbnails = 4;
    private const int MaxFileIcons = 64;
    private const int IconPixels = 32;

    public readonly record struct AppInfo(string Name, ImageSource? Icon);

    private readonly Dictionary<string, AppInfo> apps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ImageSource?> fileIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(ClipEntry Entry, ImageSource? Image)> thumbnails = [];
    private readonly HashSet<ClipEntry> decoding = [];

    /// <summary>Called on the UI thread when a thumbnail finishes decoding.</summary>
    public Action? ThumbnailReady { get; set; }

    public AppInfo App(string exePath)
    {
        if (exePath.Length == 0)
        {
            return new AppInfo("Unknown", null);
        }
        if (!apps.TryGetValue(exePath, out AppInfo info))
        {
            info = new AppInfo(AppName(exePath), ImageSource.FromFileIcon(exePath, IconPixels));
            apps[exePath] = info;
        }
        return info;
    }

    public ImageSource? FileIcon(string path)
    {
        if (fileIcons.TryGetValue(path, out ImageSource? icon))
        {
            return icon;
        }
        if (fileIcons.Count >= MaxFileIcons)
        {
            DisposeAll(fileIcons.Values);
            fileIcons.Clear();
        }
        icon = ImageSource.FromFileIcon(path, IconPixels);
        fileIcons[path] = icon;
        return icon;
    }

    /// <summary>
    /// The entry's thumbnail if decoded; otherwise starts decoding it off the UI thread and returns
    /// null (<see cref="ThumbnailReady"/> fires when done). <paramref name="failed"/> is true when the
    /// image cannot be decoded at all.
    /// </summary>
    public ImageSource? Thumbnail(ClipEntry entry, int maxWidth, int maxHeight, out bool failed)
    {
        failed = false;
        for (int i = 0; i < thumbnails.Count; i++)
        {
            if (ReferenceEquals(thumbnails[i].Entry, entry))
            {
                failed = thumbnails[i].Image is null;
                return thumbnails[i].Image;
            }
        }
        if (decoding.Add(entry))
        {
            _ = DecodeAsync(entry, maxWidth, maxHeight);
        }
        return null;
    }

    private async Task DecodeAsync(ClipEntry entry, int maxWidth, int maxHeight)
    {
        ImageSource? image = await Task.Run(() => ClipImages.DecodeThumbnail(entry, maxWidth, maxHeight)).ConfigureAwait(false);
        Dispatcher.Post(() =>
        {
            decoding.Remove(entry);
            if (thumbnails.Count >= MaxThumbnails)
            {
                thumbnails[0].Image?.Dispose();
                thumbnails.RemoveAt(0);
            }
            thumbnails.Add((entry, image));
            ThumbnailReady?.Invoke();
        });
    }

    /// <summary>Drops decoded images (the popup hid). Names stay; they are tiny.</summary>
    public void Trim()
    {
        foreach ((ClipEntry _, ImageSource? image) in thumbnails)
        {
            image?.Dispose();
        }
        thumbnails.Clear();
        DisposeAll(fileIcons.Values);
        fileIcons.Clear();
    }

    private static void DisposeAll(IEnumerable<ImageSource?> images)
    {
        foreach (ImageSource? image in images)
        {
            image?.Dispose();
        }
    }

    // The product name an app shows itself as ("Microsoft Edge"), else its file name.
    private static string AppName(string exePath)
    {
        try
        {
            string? description = FileVersionInfo.GetVersionInfo(exePath).FileDescription?.Trim();
            if (!string.IsNullOrEmpty(description))
            {
                return description;
            }
        }
        catch (FileNotFoundException)
        {
        }
        return System.IO.Path.GetFileNameWithoutExtension(exePath);
    }
}
