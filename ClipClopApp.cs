using System.Reflection;
using Cascade.UI;
using ClipClop2.History;
using ClipClop2.Platform;
using ClipClop2.Storage;
using ClipClop2.Ui;

namespace ClipClop2;

/// <summary>
/// Wires the app together: settings, history, the clipboard monitor, the tray icon, the global
/// hotkey and the popup's show/hide. Everything here runs on the UI thread.
/// </summary>
internal static class ClipClopApp
{
    private const long BytesPerMb = 1024L * 1024L;

    // The product version (csproj <Version>), without the "+commit" build metadata the SDK appends.
    private static readonly string AppVersion =
        (typeof(ClipClopApp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "")
            .Split('+')[0];

    private static PayloadCache cache = null!;
    private static ClipboardService? clipboard;
    private static Hotkey? registeredHotkey;

    // The popup's window rect (physical pixels) as placed when it opened: a different rect when it
    // hides means the user moved it.
    private static Rect placedAt;

    public static Settings Settings { get; private set; } = new();

    public static HistoryStore History { get; private set; } = null!;

    public static ClipboardService Clipboard => clipboard ?? throw new InvalidOperationException("The popup is not mounted yet.");

    /// <summary>The popup is about to show; true when it should open on the Settings screen.</summary>
    public static event Action<bool>? Opening;

    /// <summary>The popup was hidden.</summary>
    public static event Action? Hidden;

    /// <summary>Why the hotkey could not be registered, or null when it is active.</summary>
    public static string? HotkeyProblem { get; private set; }

    public static void Run()
    {
        AppPaths.EnsureCreated();
        Settings = SettingsStore.Load();
        SyncStartupRegistration();

        // Unpinned history never outlives the process: clear what a crash may have left behind.
        cache = new PayloadCache(AppPaths.CacheDirectory);
        cache.Wipe();

        History = new HistoryStore(Settings.HistoryBudgetMb * BytesPerMb);
        List<ClipEntry> pins = PinStore.Load(AppPaths.PinsFile);
        foreach (ClipEntry pin in pins)
        {
            pin.Spill(cache); // large pinned payloads wait in the encrypted cache, not RAM
        }
        History.RestorePinned(pins);

        AppWindow window = App.Window;
        window.Chrome = WindowChrome.None;
        window.ShowOnStartup = false;
        window.ShowInTaskbar = false;
        window.HideOnClose = true;
        window.AlwaysOnTop = true;
        window.Resizable = true;
        window.MinimumSize = new Size(Settings.MinPopupWidth, Settings.MinPopupHeight);
        window.Deactivated += HidePopup;

        App.OnSecondInstanceLaunched(_ => ShowPopup());
        App.OnShutdown(Shutdown);

        App.Run<MainView>(config =>
        {
            config.Theme = new AppleTheme(ThemeMode.Dark);
            config.SingleInstance = true;
            config.WindowSize = new Size(Settings.PopupWidth, Settings.PopupHeight);
            // No GPU device: the popup is small and hidden most of the day. Measured 2026-10-10 on
            // an NVIDIA D3D12 driver: hidden 15 MB vs 63 MB (Task Manager), reopen frame 10–12 ms vs 3 ms.
            config.Gpu = GpuPreference.Cpu;
            config.Tray = new TrayIcon
            {
                Tooltip = "ClipClop",
                OnClick = TogglePopup,
                Menu = new TrayMenuDefinition
                {
                    Items =
                    [
                        TrayMenuItem.Action("&Open ClipClop", () => { ShowPopup(); }, Look.Paste),
                        TrayMenuItem.Action("&Settings…", () => { ShowPopup(settings: true); }, Look.Sliders),
                        TrayMenuItem.Separator(),
                        TrayMenuItem.Info($"Version {AppVersion}"),
                        TrayMenuItem.Separator(),
                        TrayMenuItem.Action("&Quit", () => { App.Exit(); }, Look.Power),
                    ],
                },
            };
        });
    }

    /// <summary>Starts the parts that need the native window: clipboard monitoring and the hotkey.</summary>
    public static void OnWindowReady()
    {
        clipboard ??= new ClipboardService(History, cache);
        if (!TryRegisterHotkey(Settings.Hotkey))
        {
            App.Tray?.ShowNotification(new TrayNotification
            {
                Title = "ClipClop shortcut unavailable",
                Body = $"{HotkeyProblem} Click to choose another shortcut.",
                OnClick = () => { ShowPopup(settings: true); },
            });
        }
    }

    public static void TogglePopup()
    {
        if (App.Window.IsVisible && App.Window.IsActive)
        {
            HidePopup();
            return;
        }
        ShowPopup();
    }

    public static void ShowPopup(bool settings = false)
    {
        Opening?.Invoke(settings);
        PlacePopup();
        App.Window.Activate();
    }

    public static void HidePopup()
    {
        if (!App.Window.IsVisible)
        {
            return;
        }
        RememberPlacement();
        App.Window.Hide();
        Hidden?.Invoke();
    }

    /// <summary>Forgets the popup's size and position: it opens at the default size, centered.</summary>
    public static void ResetPopupPlacement()
    {
        Settings.PopupWidth = Settings.DefaultPopupWidth;
        Settings.PopupHeight = Settings.DefaultPopupHeight;
        Settings.PopupX = null;
        Settings.PopupY = null;
        SettingsStore.Save(Settings);
        App.Window.SetSize(Settings.PopupWidth, Settings.PopupHeight);
        PlacePopup();
    }

    // Where the user dragged the popup, while that spot is still on a monitor (one may have been
    // unplugged since); otherwise centered on the monitor under the cursor.
    private static void PlacePopup()
    {
        if (Settings.PopupX is int x && Settings.PopupY is int y && IsOnAScreen(x, y, App.Window.ScreenBounds.Width))
        {
            App.Window.SetScreenPosition(x, y);
        }
        else
        {
            App.Window.CenterOn(App.Screens.AtCursor);
        }
        placedAt = App.Window.ScreenBounds;
    }

    // The middle of the popup's top edge (where the search box is) must land in a work area.
    private static bool IsOnAScreen(int x, int y, float width)
    {
        var top = new Point(x + width / 2, y + 1);
        foreach (ScreenInfo screen in App.Screens.All)
        {
            if (screen.WorkArea.Contains(top))
            {
                return true;
            }
        }
        return false;
    }

    // A move or resize the user made while the popup was open is kept for the next time.
    private static void RememberPlacement()
    {
        Rect client = App.Window.ClientBounds;
        Rect screen = App.Window.ScreenBounds;
        bool changed = false;
        if (MathF.Abs(client.Width - Settings.PopupWidth) >= 1 || MathF.Abs(client.Height - Settings.PopupHeight) >= 1)
        {
            Settings.PopupWidth = MathF.Round(client.Width);
            Settings.PopupHeight = MathF.Round(client.Height);
            changed = true;
        }
        if (screen.X != placedAt.X || screen.Y != placedAt.Y)
        {
            Settings.PopupX = (int)screen.X;
            Settings.PopupY = (int)screen.Y;
            changed = true;
        }
        if (changed)
        {
            SettingsStore.Save(Settings);
        }
    }

    /// <summary>Registers <paramref name="hotkey"/> in place of the current one. False (old one kept) when taken.</summary>
    public static bool TryRegisterHotkey(Hotkey hotkey)
    {
        if (registeredHotkey == hotkey)
        {
            return true;
        }
        Hotkey? previous = registeredHotkey;
        if (previous is { } old)
        {
            App.Hotkeys.Unregister(old);
            registeredHotkey = null;
        }

        try
        {
            App.Hotkeys.Register(hotkey, TogglePopup);
            registeredHotkey = hotkey;
            HotkeyProblem = null;
            return true;
        }
        catch (HotkeyConflictException)
        {
            HotkeyProblem = $"{hotkey} is in use by another app.";
            if (previous is { } restore)
            {
                App.Hotkeys.Register(restore, TogglePopup);
                registeredHotkey = restore;
            }
            return false;
        }
    }

    public static void SaveSettings()
    {
        SettingsStore.Save(Settings);
        History.SetBudget(Settings.HistoryBudgetMb * BytesPerMb);
        StartupRegistration.Set(Settings.StartWithWindows);
    }

    public static void SavePins()
    {
        PinStore.Save(AppPaths.PinsFile, History.PinnedEntries);
    }

    // Keep the Run key pointing at this executable (it may have moved since it was written).
    private static void SyncStartupRegistration()
    {
        if (Settings.StartWithWindows != StartupRegistration.IsEnabled())
        {
            StartupRegistration.Set(Settings.StartWithWindows);
        }
    }

    private static Task Shutdown()
    {
        clipboard?.Dispose();
        SavePins();
        cache.Wipe();
        return Task.CompletedTask;
    }
}
