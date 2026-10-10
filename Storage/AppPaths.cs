namespace ClipClop2.Storage;

/// <summary>Where ClipClop2 keeps its files. Everything lives under %LOCALAPPDATA%\ClipClop2.</summary>
internal static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipClop2");

    /// <summary>User settings (plain JSON; holds nothing sensitive).</summary>
    public static string SettingsFile { get; } = Path.Combine(Root, "settings.json");

    /// <summary>Pinned items, DPAPI-encrypted.</summary>
    public static string PinsFile { get; } = Path.Combine(Root, "pins.bin");

    /// <summary>
    /// Large unpinned payloads spilled out of RAM, each file DPAPI-encrypted. Wiped on start and exit;
    /// unpinned history is never meant to outlive the process.
    /// </summary>
    public static string CacheDirectory { get; } = Path.Combine(Root, "cache");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(CacheDirectory);
    }
}
