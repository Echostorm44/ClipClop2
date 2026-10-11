using System.Text.Json;
using System.Text.Json.Serialization;
using Cascade.UI;

namespace ClipClop2.Storage;

/// <summary>User-configurable settings, persisted to <see cref="AppPaths.SettingsFile"/>.</summary>
internal sealed class Settings
{
    public const int MinHistoryBudgetMb = 100;
    public const int DefaultHistoryBudgetMb = 300;

    /// <summary>Key of the show-popup hotkey. Default: the `~ key (VK_OEM_3).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Key>))]
    public Key HotkeyKey { get; set; } = Key.Backtick;

    /// <summary>Modifiers of the show-popup hotkey. Default: Ctrl.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ModifierKeys>))]
    public ModifierKeys HotkeyModifiers { get; set; } = ModifierKeys.Ctrl;

    [JsonIgnore]
    public Hotkey Hotkey
    {
        get => new(HotkeyModifiers, HotkeyKey);
        set
        {
            HotkeyModifiers = value.Modifiers;
            HotkeyKey = value.Key;
        }
    }

    public bool StartWithWindows { get; set; }

    /// <summary>Cap on everything unpinned history holds, RAM and temp cache together.</summary>
    public int HistoryBudgetMb { get; set; } = DefaultHistoryBudgetMb;

    public const float DefaultPopupWidth = 820;
    public const float DefaultPopupHeight = 520;
    public const float MinPopupWidth = 640;
    public const float MinPopupHeight = 400;

    /// <summary>Popup size in logical pixels, as the user last resized it.</summary>
    public float PopupWidth { get; set; } = DefaultPopupWidth;

    public float PopupHeight { get; set; } = DefaultPopupHeight;

    /// <summary>
    /// Where the user dragged the popup: its top-left corner in physical virtual-desktop pixels.
    /// Null (the default) centers it on the monitor under the cursor each time it opens.
    /// </summary>
    public int? PopupX { get; set; }

    public int? PopupY { get; set; }

    [JsonIgnore]
    public bool PopupPlacementCustomized =>
        PopupX is not null || PopupWidth != DefaultPopupWidth || PopupHeight != DefaultPopupHeight;

    /// <summary>Clamps values a hand-edited or older file might carry.</summary>
    public void Normalize()
    {
        HistoryBudgetMb = Math.Max(MinHistoryBudgetMb, HistoryBudgetMb);
        PopupWidth = float.IsFinite(PopupWidth) ? Math.Max(MinPopupWidth, PopupWidth) : DefaultPopupWidth;
        PopupHeight = float.IsFinite(PopupHeight) ? Math.Max(MinPopupHeight, PopupHeight) : DefaultPopupHeight;
        if (PopupX is null || PopupY is null)
        {
            PopupX = null;
            PopupY = null;
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Settings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext
{
}

internal static class SettingsStore
{
    public static Settings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                string json = File.ReadAllText(AppPaths.SettingsFile);
                if (json.Length > 0 && JsonSerializer.Deserialize(json, SettingsJsonContext.Default.Settings) is { } loaded)
                {
                    loaded.Normalize();
                    return loaded;
                }
            }
        }
        catch (JsonException)
        {
            // A corrupt file falls back to defaults; the next save replaces it.
        }
        catch (IOException)
        {
        }
        return new Settings();
    }

    public static void Save(Settings settings)
    {
        AppPaths.EnsureCreated();
        settings.Normalize();
        string json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.Settings);

        // Write-then-rename so a crash mid-write never leaves a truncated settings file.
        string temp = AppPaths.SettingsFile + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, AppPaths.SettingsFile, overwrite: true);
    }
}
