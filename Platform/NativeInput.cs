using System.Runtime.InteropServices;

namespace ClipClop2.Platform;

/// <summary>Synthesized keyboard input for pasting into the window under ClipClop.</summary>
internal static partial class NativeInput
{
    private const byte VkShift = 0x10;
    private const byte VkControl = 0x11;
    private const byte VkV = 0x56;
    private const uint KeyEventKeyUp = 0x0002;

    /// <summary>
    /// Sends Ctrl+V with keybd_event, the way the original ClipClop pastes (proven against real apps).
    /// When the paste comes from a Shift+digit press, Shift is physically down; a synthetic Shift-up
    /// comes first so the target sees Ctrl+V rather than Ctrl+Shift+V (often "paste as plain text").
    /// </summary>
    public static void SendPaste()
    {
        if (GetAsyncKeyState(VkShift) < 0)
        {
            keybd_event(VkShift, 0, KeyEventKeyUp, 0);
        }
        keybd_event(VkControl, 0, 0, 0);
        keybd_event(VkV, 0, 0, 0);
        keybd_event(VkV, 0, KeyEventKeyUp, 0);
        keybd_event(VkControl, 0, KeyEventKeyUp, 0);
    }

    [LibraryImport("user32")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void keybd_event(byte vk, byte scan, uint flags, nuint extraInfo);

    [LibraryImport("user32")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial short GetAsyncKeyState(int vk);
}
