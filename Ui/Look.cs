using Cascade.UI;
using ClipClop2.History;

namespace ClipClop2.Ui;

/// <summary>Palette (Raycast-like dark) and icons (Lucide, 24×24 view box).</summary>
internal static class Look
{
    public static readonly ColorValue Background = new("#1E1E20");
    public static readonly ColorValue Panel = new("#262628");
    public static readonly ColorValue Hairline = new("#2E2E31");
    public static readonly ColorValue RowSelected = new("#3A3A3D");
    public static readonly ColorValue ButtonHover = new("#2C2C2F");
    public static readonly ColorValue KeyCap = new("#3A3A3D");
    public static readonly ColorValue KeyCapOnHighlight = new("#505054");
    public static readonly ColorValue TextPrimary = new("#F2F2F2");
    public static readonly ColorValue TextSecondary = new("#9A9AA0");
    public static readonly ColorValue TextDim = new("#6C6C70");
    public static readonly ColorValue PinAccent = new("#FF9F0A");
    public static readonly ColorValue HintAccent = new("#0A84FF");
    public static readonly ColorValue Warning = new("#FF9F0A");

    private static readonly Size ViewBox = new(24, 24);

    public static readonly Icon Search = new(["m21 21-4.34-4.34", "M11 17a6 6 0 1 0 0-12 6 6 0 0 0 0 12z"], ViewBox, 16f, "Search");

    public static readonly Icon Text = new(["M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z", "M14 2v4a2 2 0 0 0 2 2h4", "M10 9H8", "M16 13H8", "M16 17H8"], ViewBox, 16f, "Text");

    public static readonly Icon Link = new(["M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71", "M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71"], ViewBox, 16f, "Link");

    public static readonly Icon Mail = new(["m22 7-8.991 5.727a2 2 0 0 1-2.009 0L2 7", "M4 4h16a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z"], ViewBox, 16f, "Email");

    public static readonly Icon Image = new(["M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z", "M9 11a2 2 0 1 0 0-4 2 2 0 0 0 0 4z", "m21 15-3.086-3.086a2 2 0 0 0-2.828 0L6 21"], ViewBox, 16f, "Image");

    public static readonly Icon Files = new(["M20 7h-3a2 2 0 0 1-2-2V2", "M9 18a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h7l4 4v10a2 2 0 0 1-2 2Z", "M3 7.6v12.8A1.6 1.6 0 0 0 4.6 22h9.8"], ViewBox, 16f, "Files");

    public static readonly Icon Pin = new(["M12 17v5", "M9 10.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24V16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-.76a2 2 0 0 0-1.11-1.79l-1.78-.9A2 2 0 0 1 15 10.76V7a1 1 0 0 1 1-1 2 2 0 0 0 0-4H8a2 2 0 0 0 0 4 1 1 0 0 1 1 1z"], ViewBox, 16f, "Pin");

    public static readonly Icon Paste = new(["M15 2H9a1 1 0 0 0-1 1v2c0 .6.4 1 1 1h6c.6 0 1-.4 1-1V3c0-.6-.4-1-1-1Z", "M8 4H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2M16 4h2a2 2 0 0 1 2 2v2M11 14h10", "m17 10 4 4-4 4"], ViewBox, 16f, "Paste");

    public static readonly Icon Copy = new(["M10 8h10a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H10a2 2 0 0 1-2-2V10a2 2 0 0 1 2-2z", "M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"], ViewBox, 16f, "Copy");

    public static readonly Icon PlainText = new(["M4 7V4h16v3", "M9 20h6", "M12 4v16"], ViewBox, 16f, "Plain Text");

    public static readonly Icon External = new(["M15 3h6v6", "M10 14 21 3", "M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"], ViewBox, 16f, "Open");

    public static readonly Icon Folder = new(["M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z"], ViewBox, 16f, "Folder");

    public static readonly Icon Trash = new(["M3 6h18", "M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6", "M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2", "M10 11v6", "M14 11v6"], ViewBox, 16f, "Delete");

    public static readonly Icon Sliders = new(["M21 4h-7", "M10 4H3", "M21 12h-9", "M8 12H3", "M21 20h-5", "M12 20H3", "M14 2v4", "M8 10v4", "M16 18v4"], ViewBox, 16f, "Settings");

    public static Icon KindIcon(ClipKind kind) => kind switch
    {
        ClipKind.Link => Link,
        ClipKind.Email => Mail,
        ClipKind.Image => Image,
        ClipKind.Files => Files,
        _ => Text,
    };

    public static string KindName(ClipKind kind) => kind switch
    {
        ClipKind.Link => "Link",
        ClipKind.Email => "Email",
        ClipKind.Color => "Color",
        ClipKind.Image => "Image",
        ClipKind.Files => "Files",
        _ => "Text",
    };

    /// <summary>A key cap ("Ctrl", "K", "↵"); <paramref name="onHighlight"/> for one on a selected row, which is KeyCap-coloured.</summary>
    public static Node KeyCapLabel(string key, bool onHighlight = false)
    {
        return new Label(key)
            .FontSize(11)
            .Color(TextPrimary)
            .Padding(EdgeInsets.Symmetric(horizontal: 5, vertical: 1))
            .Background(onHighlight ? KeyCapOnHighlight : KeyCap)
            .CornerRadius(4);
    }

    /// <summary>The colour a Color entry names (#hex or rgb()/rgba()), or null.</summary>
    public static ColorValue? ParseColor(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.StartsWith('#'))
        {
            try
            {
                return new ColorValue(trimmed);
            }
            catch (FormatException)
            {
                return null;
            }
        }
        if (!trimmed.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        int open = trimmed.IndexOf('(');
        int close = trimmed.LastIndexOf(')');
        if (open < 0 || close <= open)
        {
            return null;
        }
        string[] parts = trimmed[(open + 1)..close].Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 3
            || !float.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out float r)
            || !float.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out float g)
            || !float.TryParse(parts[2], System.Globalization.CultureInfo.InvariantCulture, out float b))
        {
            return null;
        }
        float a = 1f;
        if (parts.Length > 3 && !float.TryParse(parts[3], System.Globalization.CultureInfo.InvariantCulture, out a))
        {
            return null;
        }
        return ColorValue.FromRgba(Math.Clamp(r / 255f, 0f, 1f), Math.Clamp(g / 255f, 0f, 1f), Math.Clamp(b / 255f, 0f, 1f), Math.Clamp(a, 0f, 1f));
    }
}
