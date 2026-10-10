using Microsoft.Win32;

namespace ClipClop2.Platform;

/// <summary>
/// Start with Windows via HKCU\Software\Microsoft\Windows\CurrentVersion\Run, the per-user key
/// installers use. No admin rights needed; the value points at this executable.
/// </summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClipClop2";

    // Launched at sign-in, the app should go straight to the tray.
    public const string StartupArgument = "--startup";

    public static bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string command && command.Contains(ExecutablePath(), StringComparison.OrdinalIgnoreCase);
    }

    public static void Set(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{ExecutablePath()}\" {StartupArgument}");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static string ExecutablePath() =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "ClipClop2.exe");
}
