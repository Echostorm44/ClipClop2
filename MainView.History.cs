using System.Globalization;
using Cascade.UI;
using ClipClop2.History;
using ClipClop2.Ui;

namespace ClipClop2;

internal sealed partial class MainView
{
    // Text previews beyond this are cut: the pane shows the start; paste still uses everything.
    private const int MaxPreviewChars = 20_000;
    private const int MaxPreviewFiles = 50;
    private const int ThumbnailWidth = 960;
    private const int ThumbnailHeight = 640;

    private Node HistoryList()
    {
        return new ListView<ClipEntry>(
                visible,
                HistoryRow,
                SelectionMode.Single,
                onSelect: entry =>
                {
                    SelectRow(visible.IndexOf(entry));
                    Invalidate();
                },
                selected: Bind<ClipEntry>(selected!, v => { selected = v; })) // null = no selection; ListView handles it
            .ItemHeight(RowHeight)
            .Plain()
            .SelectionHighlight(false)
            .ScrollIntoView(selectedIndex)
            .OnActivate(entry => { ClipClopApp.Clipboard.Paste(entry); })
            // Typing always filters: clicking or right-clicking a row selects it and leaves focus in the
            // search box (the row's menu hands it back there when it closes).
            .FocusOnClick(false)
            .ItemAccessibleName(itemAccessibleName)
            .ItemContextMenu(itemMenu)
            .Ref(historyListRef)
            .EmptyState(new Center(new Label(search.Length > 0 ? "No matching entries" : "Nothing copied yet")
                .FontSize(13)
                .Color(Look.TextSecondary)))
            .Padding(EdgeInsets.All(6));
    }

    private Node HistoryRow(ClipEntry entry)
    {
        bool isSelected = ReferenceEquals(entry, selected);
        return new Row(
                spacing: 10,
                crossAxisAlignment: CrossAxisAlignment.Center,
                children:
                [
                    RowIcon(entry),
                    new Label(entry.DisplayLine)
                        .FontSize(13)
                        .Color(Look.TextPrimary)
                        .Wrap(TextWrap.NoWrap)
                        .Overflow(TextOverflow.Ellipsis)
                        .Expand(),
                    entry.Pinned ? new IconView(Look.Pin, size: 12).Color(Look.PinAccent) : Node.Empty,
                    HintCap(entry),
                ])
            .Padding(EdgeInsets.Symmetric(horizontal: 10, vertical: 0))
            .Height(RowHeight)
            .Background(isSelected ? Look.RowSelected : ColorValue.Transparent)
            .CornerRadius(8);
    }

    private static Node RowIcon(ClipEntry entry)
    {
        if (entry.Kind == ClipKind.Color && Look.ParseColor(entry.SearchText) is { } color)
        {
            return new Column().Size(14).Background(color).CornerRadius(4).Border(Look.Hairline, 1, 4);
        }
        return new IconView(Look.KindIcon(entry.Kind), size: 16).Color(Look.TextSecondary);
    }

    // The 1–9 shown next to the first nine rows while Shift is held.
    private Node HintCap(ClipEntry entry)
    {
        if (!showHints)
        {
            return Node.Empty;
        }
        int limit = Math.Min(9, visible.Count);
        for (int i = 0; i < limit; i++)
        {
            if (ReferenceEquals(visible[i], entry))
            {
                return new Label((i + 1).ToString(CultureInfo.InvariantCulture))
                    .FontSize(11)
                    .Bold()
                    .Color(Look.TextPrimary)
                    .Padding(EdgeInsets.Symmetric(horizontal: 6, vertical: 1))
                    .Background(Look.HintAccent)
                    .CornerRadius(4);
            }
        }
        return Node.Empty;
    }

    // ── Preview pane ──────────────────────────────────────────────────

    private Node Preview()
    {
        if (selected is not { } entry)
        {
            return new Center(new Label(visible.Count == 0 ? "" : "No entry selected").FontSize(13).Color(Look.TextSecondary));
        }

        return new Column(children:
            [
                PreviewContent(entry).Expand(),
                Information(entry).BorderTop(Look.Hairline, 1),
            ]);
    }

    private Node PreviewContent(ClipEntry entry)
    {
        return entry.Kind switch
        {
            ClipKind.Image => ImagePreview(entry),
            ClipKind.Files => FilesPreview(entry),
            ClipKind.Color => ColorPreview(entry),
            ClipKind.Link or ClipKind.Email => new Center(new Label(entry.SearchText.Trim())
                    .FontSize(15)
                    .Color(Look.TextPrimary)
                    .Wrap(TextWrap.Wrap)
                    .TextAlign(TextAlignment.Center))
                .Padding(EdgeInsets.All(24)),
            _ => TextPreview(entry),
        };
    }

    private static Node TextPreview(ClipEntry entry)
    {
        string text = entry.SearchText.Length > MaxPreviewChars
            ? string.Concat(entry.SearchText.AsSpan(0, MaxPreviewChars), "\n…")
            : entry.SearchText;
        return new ScrollView(new Label(text)
                .FontSize(12)
                .FontFamily(FontFamily.ForCategory(SystemFont.Monospace))
                .Color(Look.TextPrimary)
                .Wrap(TextWrap.Wrap)
                .Selectable(true)
                .Padding(EdgeInsets.All(16)));
    }

    private Node ImagePreview(ClipEntry entry)
    {
        ImageSource? image = previews.Thumbnail(entry, ThumbnailWidth, ThumbnailHeight, out bool failed);
        if (image is not null)
        {
            return new Image(image).Fit(ImageFit.Contain).AccessibleLabel(entry.DisplayLine).Padding(EdgeInsets.All(16));
        }
        return new Center(failed
            ? new IconView(Look.Image, size: 48).Color(Look.TextDim)
            : new Spinner());
    }

    private Node FilesPreview(ClipEntry entry)
    {
        string[] paths = entry.SearchText.Split('\n');
        int count = Math.Min(paths.Length, MaxPreviewFiles);
        var rows = new Node[count + (paths.Length > count ? 1 : 0)];
        for (int i = 0; i < count; i++)
        {
            rows[i] = FileRow(paths[i]);
        }
        if (paths.Length > count)
        {
            rows[count] = new Label($"+ {paths.Length - count} more").FontSize(12).Color(Look.TextSecondary);
        }
        return new ScrollView(new Column(spacing: 6, children: rows).Padding(EdgeInsets.All(16)));
    }

    private Node FileRow(string path)
    {
        ImageSource? icon = previews.FileIcon(path);
        bool exists = File.Exists(path) || Directory.Exists(path);
        return new Row(
            spacing: 10,
            crossAxisAlignment: CrossAxisAlignment.Center,
            children:
            [
                icon is not null ? new Image(icon).AccessibleLabel("").Size(24) : new IconView(Look.Files, size: 20).Color(Look.TextSecondary),
                new Column(children:
                    [
                        new Label(System.IO.Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } name ? name : path)
                            .FontSize(13)
                            .Color(exists ? Look.TextPrimary : Look.TextDim)
                            .Wrap(TextWrap.NoWrap)
                            .Overflow(TextOverflow.Ellipsis),
                        new Label(exists ? System.IO.Path.GetDirectoryName(path) ?? "" : "Moved or deleted")
                            .FontSize(11)
                            .Color(Look.TextSecondary)
                            .Wrap(TextWrap.NoWrap)
                            .Overflow(TextOverflow.Ellipsis),
                    ]).Expand(),
            ]);
    }

    private static Node ColorPreview(ClipEntry entry)
    {
        ColorValue? color = Look.ParseColor(entry.SearchText);
        return new Center(new Column(
            spacing: 12,
            crossAxisAlignment: CrossAxisAlignment.Center,
            children:
            [
                color is { } swatch
                    ? new Column().Size(120).Background(swatch).CornerRadius(16).Border(Look.Hairline, 1, 16)
                    : Node.Empty,
                new Label(entry.SearchText.Trim()).FontSize(15).Color(Look.TextPrimary),
            ]));
    }

    // ── Information table ─────────────────────────────────────────────

    private Node Information(ClipEntry entry)
    {
        PreviewCache.AppInfo app = previews.App(entry.SourceApp);
        var rows = new List<Node>(8)
        {
            new Label("Information").FontSize(12).Color(Look.TextSecondary).Padding(EdgeInsets.Symmetric(horizontal: 0, vertical: 8)),
            InfoRow("Source", new Row(
                spacing: 6,
                crossAxisAlignment: CrossAxisAlignment.Center,
                children:
                [
                    app.Icon is { } icon ? new Image(icon).AccessibleLabel("").Size(16) : Node.Empty,
                    InfoValue(app.Name),
                ])),
            InfoRow("Content type", InfoValue(Look.KindName(entry.Kind))),
        };

        switch (entry.Kind)
        {
            case ClipKind.Image:
                if (entry.ImageSize is { } size)
                {
                    rows.Add(InfoRow("Dimensions", InfoValue($"{size.Width}×{size.Height}")));
                }
                rows.Add(InfoRow("Size", InfoValue(FormatBytes(entry.SizeBytes))));
                break;
            case ClipKind.Files:
                rows.Add(InfoRow("Count", InfoValue(entry.SearchText.Split('\n').Length.ToString("N0", CultureInfo.CurrentCulture))));
                break;
            case ClipKind.Link:
                if (Uri.TryCreate(entry.SearchText.Trim(), UriKind.Absolute, out Uri? url))
                {
                    rows.Add(InfoRow("Host", InfoValue(url.Host)));
                }
                rows.Add(InfoRow("URL", InfoValue(entry.SearchText.Trim())));
                break;
            default:
                rows.Add(InfoRow("Characters", InfoValue(entry.SearchText.Length.ToString("N0", CultureInfo.CurrentCulture))));
                rows.Add(InfoRow("Words", InfoValue(CountWords(entry.SearchText).ToString("N0", CultureInfo.CurrentCulture))));
                break;
        }

        rows.Add(InfoRow("Formats", InfoValue(FormatList(entry))));
        rows.Add(InfoRow("Copied", InfoValue(FormatCopied(entry.CopiedAt))));

        return new Column(children: [.. rows]).Padding(EdgeInsets.Symmetric(horizontal: 16, vertical: 4));
    }

    private static Node InfoRow(string name, Node value)
    {
        return new Row(
                spacing: 12,
                crossAxisAlignment: CrossAxisAlignment.Center,
                children:
                [
                    new Label(name).FontSize(12).Color(Look.TextSecondary).Expand(),
                    value,
                ])
            .Height(30)
            .BorderBottom(Look.Hairline, 1);
    }

    private static Label InfoValue(string text)
    {
        return new Label(text).FontSize(12).Color(Look.TextPrimary).Wrap(TextWrap.NoWrap).Overflow(TextOverflow.Ellipsis);
    }

    private static int CountWords(string text)
    {
        int words = 0;
        bool inWord = false;
        foreach (char c in text)
        {
            bool letter = !char.IsWhiteSpace(c);
            if (letter && !inWord)
            {
                words++;
            }
            inWord = letter;
        }
        return words;
    }

    // The formats a paste restores, by friendly name ("Unicode Text, HTML, RTF").
    private static string FormatList(ClipEntry entry)
    {
        var names = new List<string>(entry.Formats.Count);
        foreach (ClipFormat format in entry.Formats)
        {
            string? name = format.Name switch
            {
                "CF_UNICODETEXT" => "Unicode Text",
                "CF_TEXT" or "CF_OEMTEXT" or "CF_LOCALE" => null, // synthesized from Unicode text
                "HTML Format" => "HTML",
                "Rich Text Format" => "RTF",
                "CF_HDROP" => "File List",
                "CF_DIB" or "CF_DIBV5" or "CF_BITMAP" => "Bitmap",
                "CF_ENHMETAFILE" => "Metafile",
                _ => format.Name.StartsWith("CF_", StringComparison.Ordinal) ? format.Name[3..] : format.Name,
            };
            if (name is not null && !names.Contains(name))
            {
                names.Add(name);
            }
        }
        return string.Join(", ", names);
    }

    private static string FormatCopied(DateTime when)
    {
        DateTime today = DateTime.Today;
        string time = when.ToString("t", CultureInfo.CurrentCulture);
        if (when.Date == today)
        {
            return $"Today at {time}";
        }
        if (when.Date == today.AddDays(-1))
        {
            return $"Yesterday at {time}";
        }
        return $"{when.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)} at {time}";
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):0.0} MB",
            >= 1024 => $"{bytes / 1024.0:0} KB",
            _ => $"{bytes} B",
        };
    }
}
