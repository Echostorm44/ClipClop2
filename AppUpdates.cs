using Cascade.UI;
using Cascade.UI.Installer.Update;

namespace ClipClop2;

/// <summary>
/// Updates from GitHub Releases. Checks at startup and again when the popup opens once the last
/// check is six hours old: no timer, so the tray app stays idle. Nothing is downloaded until the
/// user asks (tray menu, the notification, or Settings); then the update shim swaps the files and
/// relaunches. Everything here runs on the UI thread.
/// </summary>
internal static class AppUpdates
{
    private const string ManifestUrl = "https://github.com/Echostorm44/ClipClop2/releases/latest/download/manifest.json";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(15);

    private static DateTime nextCheck = DateTime.MinValue;
    private static bool checking;
    private static string? notifiedVersion;

    /// <summary>The newer version on offer, or null when up to date or not yet checked.</summary>
    public static string? AvailableVersion { get; private set; }

    /// <summary>When the last check succeeded (local time), or null before one has.</summary>
    public static DateTime? LastChecked { get; private set; }

    /// <summary>Downloading or handing off to the shim.</summary>
    public static bool Installing { get; private set; }

    /// <summary>Why the last install attempt failed, or null.</summary>
    public static string? Problem { get; private set; }

    /// <summary>Whether this copy can update itself: only an installed copy has the shim beside it.</summary>
    public static bool Supported => Updater.IsConfigured;

    /// <summary>The update state changed (for the Settings screen).</summary>
    public static event Action? Changed;

    public static void Configure(string currentVersion)
    {
        string installDir = AppContext.BaseDirectory;
        if (!File.Exists(System.IO.Path.Combine(installDir, "cascade-update.exe")))
        {
            return; // a dev build: nothing to swap files with
        }
        // CLIPCLOP_UPDATE_MANIFEST points at another manifest: a way to try an update locally before
        // it is released.
        string manifestUrl = Environment.GetEnvironmentVariable("CLIPCLOP_UPDATE_MANIFEST") is { Length: > 0 } custom
            ? custom
            : ManifestUrl;
        try
        {
            Updater.Configure(
                new UpdateConfig
                {
                    ManifestUrl = manifestUrl,
                    CheckOnStartup = true,
                    CheckInterval = CheckInterval,
                    AutoDownload = false,
                    Channel = "stable",
                },
                currentVersion,
                "win-x64",
                installDir,
                Environment.ProcessPath ?? System.IO.Path.Combine(installDir, "ClipClop2.exe"));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ClipClop] Updater unavailable: {ex.Message}");
        }
    }

    /// <summary>The app is up: defuse the shim's crash rollback and run the first check.</summary>
    public static void Started()
    {
        if (!Updater.IsConfigured)
        {
            return;
        }
        Updater.MarkHealthy();
        _ = CheckIfDueAsync();
    }

    /// <summary>A check is in flight.</summary>
    public static bool Checking => checking;

    /// <summary>Checks now, whenever the last check was ("Check now" in Settings).</summary>
    public static Task CheckNowAsync()
    {
        nextCheck = DateTime.MinValue;
        return CheckIfDueAsync();
    }

    public static async Task CheckIfDueAsync()
    {
        if (!Updater.IsConfigured || checking || Installing || DateTime.UtcNow < nextCheck)
        {
            return;
        }
        checking = true;
        Changed?.Invoke();
        try
        {
            UpdateCheckResult result = await Task.Run(() => Updater.CheckNowAsync()).ConfigureAwait(true);
            nextCheck = DateTime.UtcNow + CheckInterval;
            LastChecked = DateTime.Now;
            AvailableVersion = result.IsAvailable ? result.Version : null;
            if (AvailableVersion is { } version && version != notifiedVersion)
            {
                notifiedVersion = version;
                App.Tray?.ShowNotification(new TrayNotification
                {
                    Title = "ClipClop update available",
                    Body = $"Version {version} is ready. Click to update and restart ClipClop.",
                    OnClick = () => { _ = InstallAsync(); },
                });
            }
        }
        catch (Exception ex)
        {
            // Offline or GitHub unreachable: invisible, try again a little later.
            nextCheck = DateTime.UtcNow + RetryAfterFailure;
            Console.Error.WriteLine($"[ClipClop] Update check failed: {ex.Message}");
        }
        finally
        {
            checking = false;
            Changed?.Invoke();
        }
    }

    /// <summary>Downloads the update and restarts into it. Shutdown still saves pins and wipes the cache.</summary>
    public static async Task InstallAsync()
    {
        if (!Updater.IsConfigured || AvailableVersion is null || Installing)
        {
            return;
        }
        Installing = true;
        Problem = null;
        Changed?.Invoke();
        try
        {
            await Task.Run(() => Updater.DownloadAsync()).ConfigureAwait(true);
            Updater.ApplyAndRestart();
        }
        catch (Exception ex)
        {
            Installing = false;
            Problem = "The update could not be installed. Try again later.";
            Console.Error.WriteLine($"[ClipClop] Update failed: {ex}");
            Changed?.Invoke();
        }
    }
}
