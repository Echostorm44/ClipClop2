using Cascade.UI;
using ClipClop2.Platform;
using ClipClop2.Storage;

namespace ClipClop2.History;

/// <summary>
/// Feeds clipboard changes into history and puts entries back on paste. Event-driven only
/// (WM_CLIPBOARDUPDATE): nothing runs while the clipboard is idle.
/// </summary>
internal sealed class ClipboardService : IDisposable
{
    private readonly HistoryStore history;
    private readonly PayloadCache cache;

    public ClipboardService(HistoryStore history, PayloadCache cache)
    {
        this.history = history;
        this.cache = cache;
        Clipboard.Changed += OnChanged;
        Clipboard.StartMonitoring();
    }

    public void Dispose()
    {
        Clipboard.Changed -= OnChanged;
        Clipboard.StopMonitoring();
    }

    private void OnChanged(ClipboardChangedEventArgs args)
    {
        // Our own pastes and content a password manager marks private are never recorded.
        if (args.Source == ClipboardSource.ThisApp || args.IsExcludedFromHistory)
        {
            return;
        }

        if (Clipboard.CaptureRaw(ClipCapture.IncludeFormat) is not { } snapshot)
        {
            return;
        }

        // Resolve the source now: the copying process may exit before anyone looks.
        string source = args.SourceApp?.ExecutablePath ?? "";
        if (ClipCapture.Create(snapshot, source, DateTime.Now) is not { } entry)
        {
            return;
        }

        ClipEntry kept = history.Add(entry);
        if (ReferenceEquals(kept, entry))
        {
            entry.Spill(cache);
        }
    }

    /// <summary>
    /// Makes the entry the clipboard content (every captured format, or plain text only), hides the
    /// popup and pastes into the window that had focus before it.
    /// </summary>
    public bool Paste(ClipEntry entry, bool plainTextOnly = false)
    {
        if (!CopyToClipboard(entry, plainTextOnly))
        {
            return false;
        }
        ClipClopApp.HidePopup();
        NativeInput.SendPaste();
        return true;
    }

    /// <summary>
    /// Pastes into the window under ClipClop, then brings the popup back. The window has to hide for
    /// the target to be foreground when Ctrl+V is synthesized; the short wait lets the target's input
    /// queue take the keystrokes before we reactivate (a one-off delay, not a timer).
    /// </summary>
    public async Task PasteAndReturnAsync(ClipEntry entry)
    {
        if (!Paste(entry))
        {
            return;
        }
        await Task.Delay(ReturnDelay).ConfigureAwait(true);
        App.Window.Activate();
    }

    private static readonly TimeSpan ReturnDelay = TimeSpan.FromMilliseconds(150);

    /// <summary>Puts the entry on the clipboard without pasting.</summary>
    public bool CopyToClipboard(ClipEntry entry, bool plainTextOnly = false)
    {
        var formats = new List<ClipboardRawFormat>(entry.Formats.Count);
        foreach (ClipFormat format in entry.Formats)
        {
            if (plainTextOnly && format.Name != "CF_UNICODETEXT")
            {
                continue;
            }
            formats.Add(new ClipboardRawFormat(format.Id, format.Name, format.ReadData()));
        }
        if (formats.Count == 0 || !Clipboard.WriteRaw(new ClipboardRawSnapshot(formats)))
        {
            return false;
        }
        history.Promote(entry);
        return true;
    }
}
