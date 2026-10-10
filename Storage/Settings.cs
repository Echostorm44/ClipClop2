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

    /// <summary>Clamps values a hand-edited or older file might carry.</summary>
    public void Normalize()
    {
        HistoryBudgetMb = Math.Max(MinHistoryBudgetMb, HistoryBudgetMb);
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
