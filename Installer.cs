using Cascade.UI.Installer;
using Microsoft.Win32;

namespace ClipClop2;

/// <summary>
/// The install/uninstall declaration, consumed by <c>cascade package --installer</c> to produce the
/// single-file setup exe. A per-user install (no admin): the app lands under
/// %LOCALAPPDATA%\Programs\ClipClop2, kept apart from the settings and pins in %LOCALAPPDATA%\ClipClop2,
/// with a Start Menu shortcut. Everything it writes is recorded in the install manifest, so the bundled
/// uninstaller removes it completely.
/// </summary>
// Must be PUBLIC: `cascade package --installer` discovers it by the public modifier and the
// generated wizard instantiates it from a separate assembly.
[Installer]
public sealed class ClipClopInstaller : CascadeInstaller
{
    // Written by the app itself (Platform/StartupRegistration.cs) when Start with Windows is on.
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "ClipClop2";

    public override InstallerConfig Configure() => new()
    {
        AppId         = "5B5BBEAD-36E4-4B39-BA56-CD4F0CF87BBA",
        AppName       = "ClipClop2",
        Version       = ResolveVersion(),
        Publisher     = "Echostorm",
        Description   = "A fast, private clipboard history that lives in the tray.",
        InstallDir    = InstallDir.LocalAppData(@"Programs\ClipClop2"),
        Output        = "ClipClop2-Setup",
        RequiresAdmin = false,
        Shortcuts =
        [
            new Shortcut { Name = "ClipClop", TargetPath = "ClipClop2.exe", Location = ShortcutLocation.StartMenu },
        ],
    };

    public override IReadOnlyList<InstallFile> Files =>
    [
        InstallFile.Directory("publish/*", dest: Dir.App, recursive: true),
    ];

    /// <summary>
    /// The app writes its own Start with Windows entry, so the install manifest doesn't know about it.
    /// Remove it here, or Windows would try to launch a deleted exe at every sign-in.
    /// </summary>
    public override Task OnUninstallAsync(InstallContext ctx)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(RunValueName, throwOnMissingValue: false);
        return Task.CompletedTask;
    }

    /// <summary>
    /// The app assembly's version as <c>major.minor.patch</c>; falls back to 1.0.0 when unset.
    /// CI stamps the tag version into the build.
    /// </summary>
    private static string ResolveVersion()
    {
        Version? v = typeof(ClipClopInstaller).Assembly.GetName().Version;
        return v is null ? "1.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
