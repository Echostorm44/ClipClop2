using System.Diagnostics;
using Cascade.UI;
using ClipClop2.History;
using ClipClop2.Ui;

namespace ClipClop2;

internal sealed partial class MainView
{
    /// <summary>
    /// One entry in the Ctrl+K menu. <see cref="Keys"/> is its shortcut as shown; <see cref="Pastes"/>
    /// actions run on the key's release (see <see cref="Defer"/>).
    /// </summary>
    private sealed record ClipAction(
        string Title,
        Icon Icon,
        ModifierKeys Modifiers,
        Cascade.UI.Key Key,
        string[] Keys,
        bool Pastes,
        Func<ClipEntry?, bool> Applies,
        Action<ClipEntry?> Run,
        int ItemMenuGroup = NotInItemMenu,
        bool Destructive = false);

    // Right-click menu groups, separated by a line; screen-wide actions (Settings, Delete All) stay out.
    private const int NotInItemMenu = -1;
    private const int PasteGroup = 0;
    private const int OpenGroup = 1;
    private const int EditGroup = 2;

    private readonly IReadOnlyList<ClipAction> actions;

    // BuildActions lists Paste first: it is also the Enter action and the footer's Paste button.
    private ClipAction PasteAction => actions[0];
    private readonly NodeRef<TextInput> actionSearchRef = new();

    private bool actionsOpen;
    private string actionSearch = "";
    private List<ClipAction> actionMatches = [];
    private int actionIndex;

    private IReadOnlyList<ClipAction> BuildActions()
    {
        static bool Any(ClipEntry? e) => e is not null;

        return
        [
            new("Paste", Look.Paste, ModifierKeys.None, Cascade.UI.Key.Enter, ["↵"], Pastes: true, Any,
                e => { ClipClopApp.Clipboard.Paste(e!); },
                ItemMenuGroup: PasteGroup),
            new("Copy to Clipboard", Look.Copy, ModifierKeys.Ctrl, Cascade.UI.Key.Enter, ["Ctrl", "↵"], Pastes: false, Any,
                e =>
                {
                    ClipClopApp.Clipboard.CopyToClipboard(e!);
                    ClipClopApp.HidePopup();
                },
                ItemMenuGroup: PasteGroup),
            new("Paste and Keep Window Open", Look.Paste, ModifierKeys.Shift, Cascade.UI.Key.Enter, ["Shift", "↵"], Pastes: true, Any,
                e => { _ = ClipClopApp.Clipboard.PasteAndReturnAsync(e!); },
                ItemMenuGroup: PasteGroup),
            new("Paste as Plain Text", Look.PlainText, ModifierKeys.Ctrl | ModifierKeys.Shift, Cascade.UI.Key.V, ["Ctrl", "Shift", "V"], Pastes: true,
                e => e is { HasPlainText: true } && e.Formats.Count > 1,
                e => { ClipClopApp.Clipboard.Paste(e!, plainTextOnly: true); },
                ItemMenuGroup: PasteGroup),
            new("Open in Browser", Look.External, ModifierKeys.Ctrl, Cascade.UI.Key.O, ["Ctrl", "O"], Pastes: false,
                e => e?.Kind is ClipKind.Link,
                e => OpenAndHide(e!.SearchText.Trim()),
                ItemMenuGroup: OpenGroup),
            new("Compose Email", Look.Mail, ModifierKeys.Ctrl, Cascade.UI.Key.O, ["Ctrl", "O"], Pastes: false,
                e => e?.Kind is ClipKind.Email,
                e => OpenAndHide("mailto:" + e!.SearchText.Trim()),
                ItemMenuGroup: OpenGroup),
            new("Show in Explorer", Look.Folder, ModifierKeys.Ctrl, Cascade.UI.Key.O, ["Ctrl", "O"], Pastes: false,
                e => e?.Kind is ClipKind.Files,
                e => ShowInExplorer(e!),
                ItemMenuGroup: OpenGroup),
            new("Pin", Look.Pin, ModifierKeys.Ctrl, Cascade.UI.Key.P, ["Ctrl", "P"], Pastes: false,
                e => e is { Pinned: false },
                e =>
                {
                    ClipClopApp.History.Pin(e!);
                    ClipClopApp.SavePins();
                },
                ItemMenuGroup: EditGroup),
            new("Unpin", Look.Pin, ModifierKeys.Ctrl, Cascade.UI.Key.P, ["Ctrl", "P"], Pastes: false,
                e => e is { Pinned: true },
                e =>
                {
                    ClipClopApp.History.Unpin(e!);
                    ClipClopApp.SavePins();
                },
                ItemMenuGroup: EditGroup),
            new("Delete Entry", Look.Trash, ModifierKeys.Ctrl, Cascade.UI.Key.D, ["Ctrl", "D"], Pastes: false, Any,
                e =>
                {
                    bool wasPinned = e!.Pinned;
                    ClipClopApp.History.Remove(e);
                    if (wasPinned)
                    {
                        ClipClopApp.SavePins();
                    }
                },
                ItemMenuGroup: EditGroup, Destructive: true),
            new("Delete All Unpinned Entries", Look.Trash, ModifierKeys.Ctrl | ModifierKeys.Shift, Cascade.UI.Key.D, ["Ctrl", "Shift", "D"], Pastes: false,
                _ => ClipClopApp.History.Count > ClipClopApp.History.PinnedEntries.Count,
                _ => { ClipClopApp.History.ClearUnpinned(); }),
            new("Settings", Look.Sliders, ModifierKeys.Ctrl, Cascade.UI.Key.S, ["Ctrl", "S"], Pastes: false,
                _ => true,
                _ => OpenSettings()),
        ];
    }

    private static void OpenAndHide(string url)
    {
        App.OpenUrl(url);
        ClipClopApp.HidePopup();
    }

    private static void ShowInExplorer(ClipEntry entry)
    {
        string first = entry.SearchText.Split('\n')[0];
        using Process? _ = Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{first}\"") { UseShellExecute = false });
        ClipClopApp.HidePopup();
    }

    /// <summary>The selected entry's actions shortcut, if <paramref name="e"/> is one.</summary>
    private bool RunShortcut(KeyEvent e)
    {
        foreach (ClipAction action in actions)
        {
            if (action.Key == e.Key && action.Modifiers == e.Modifiers && action.Applies(selected))
            {
                Trigger(action, e);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// A history row's right-click (or Shift+F10) menu: the actions that apply to it, grouped as in
    /// the Ctrl+K menu, each with its shortcut.
    /// </summary>
    private IReadOnlyList<ContextMenuItem> ItemMenu(ClipEntry entry)
    {
        var items = new List<ContextMenuItem>(actions.Count + 2);
        int group = NotInItemMenu;
        foreach (ClipAction action in actions)
        {
            if (action.ItemMenuGroup == NotInItemMenu || !action.Applies(entry))
            {
                continue;
            }
            if (group != NotInItemMenu && action.ItemMenuGroup != group)
            {
                items.Add(ContextMenuItem.Separator());
            }
            group = action.ItemMenuGroup;
            items.Add(ContextMenuItem.Action(
                action.Title,
                () => { RunFromMenu(action, entry); },
                style: action.Destructive ? MenuItemStyle.Destructive : MenuItemStyle.Normal,
                icon: new IconView(action.Icon, size: 14).Color(Look.TextSecondary),
                shortcut: ShortcutText(action.Keys)));
        }
        return items;
    }

    private void RunFromMenu(ClipAction action, ClipEntry entry)
    {
        CloseActions();
        action.Run(entry);
        Invalidate();
    }

    private static string ShortcutText(string[] keys)
    {
        return string.Join('+', keys.Select(k => k == "↵" ? "Enter" : k));
    }

    private void Trigger(ClipAction action, KeyEvent? key)
    {
        ClipEntry? entry = selected;
        CloseActions();
        if (key is { } k && action.Pastes)
        {
            if (!k.IsRepeat)
            {
                Defer(k.Key, () => { action.Run(entry); });
            }
            return;
        }
        action.Run(entry);
        Invalidate();
    }

    // ── Menu state ────────────────────────────────────────────────────

    private void OpenActions()
    {
        actionsOpen = true;
        actionSearch = "";
        showHints = false;
        RefreshActionMatches();
        Invalidate();
    }

    private void CloseActions()
    {
        if (!actionsOpen)
        {
            return;
        }
        actionsOpen = false;
        FocusSearch();
        Invalidate();
    }

    private void RefreshActionMatches()
    {
        string needle = actionSearch.Trim();
        var matches = new List<ClipAction>(actions.Count);
        foreach (ClipAction action in actions)
        {
            if (action.Applies(selected) && (needle.Length == 0 || action.Title.Contains(needle, StringComparison.OrdinalIgnoreCase)))
            {
                matches.Add(action);
            }
        }
        actionMatches = matches;
        actionIndex = 0;
    }

    private bool ActionsKeyDown(KeyEvent e)
    {
        ModifierKeys mods = e.Modifiers;
        switch (e.Key)
        {
            case Cascade.UI.Key.Escape when mods == ModifierKeys.None:
            case Cascade.UI.Key.K when mods == ModifierKeys.Ctrl:
                CloseActions();
                return true;
            case Cascade.UI.Key.Up when mods == ModifierKeys.None:
                actionIndex = Math.Max(0, actionIndex - 1);
                Invalidate();
                return true;
            case Cascade.UI.Key.Down when mods == ModifierKeys.None:
                actionIndex = Math.Min(actionMatches.Count - 1, actionIndex + 1);
                Invalidate();
                return true;
            case Cascade.UI.Key.Enter when mods == ModifierKeys.None:
                if (actionIndex >= 0 && actionIndex < actionMatches.Count)
                {
                    Trigger(actionMatches[actionIndex], e);
                }
                return true;
        }
        return RunShortcut(e);
    }

    // ── Render ────────────────────────────────────────────────────────

    // A click outside the menu closes it; the menu sits above the footer's right end, like Raycast.
    private Node ActionsOverlay()
    {
        var rows = new Node[actionMatches.Count];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = ActionRow(actionMatches[i], i == actionIndex);
        }

        Node menu = new Column(children:
            [
                new Column(spacing: 2, children: rows.Length > 0 ? rows : [new Label("No matching actions").FontSize(12).Color(Look.TextSecondary).Padding(EdgeInsets.All(8))])
                    .Padding(EdgeInsets.All(6)),
                new TextInput(Bind(actionSearch, v =>
                    {
                        actionSearch = v;
                        RefreshActionMatches();
                        Invalidate();
                    }), placeholder: "Search for actions…")
                    .Ref(actionSearchRef)
                    .AutoFocus()
                    .Padding(EdgeInsets.All(8))
                    .BorderTop(Look.Hairline, 1),
            ])
            .Width(420)
            .Background(Look.Panel)
            .Border(Look.Hairline, 1, 10)
            .CornerRadius(10)
            .Margin(EdgeInsets.Only(right: 12, bottom: 44));

        return new Column(children:
            [
                new Spacer().OnTap(CloseActions).Expand(),
                new Row(children:
                    [
                        new Spacer().OnTap(CloseActions).Expand(),
                        menu,
                    ]),
            ]);
    }

    private Node ActionRow(ClipAction action, bool highlighted)
    {
        var keys = new Node[action.Keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            keys[i] = Look.KeyCapLabel(action.Keys[i], onHighlight: highlighted);
        }

        return new Row(
                spacing: 10,
                crossAxisAlignment: CrossAxisAlignment.Center,
                children:
                [
                    new IconView(action.Icon, size: 14).Color(Look.TextSecondary),
                    new Label(action.Title).FontSize(13).Color(Look.TextPrimary).Wrap(TextWrap.NoWrap).Overflow(TextOverflow.Ellipsis).Expand(),
                    new Row(spacing: 4, crossAxisAlignment: CrossAxisAlignment.Center, children: keys),
                ])
            .Padding(EdgeInsets.Symmetric(horizontal: 10, vertical: 0))
            .Height(32)
            .Background(highlighted ? Look.RowSelected : ColorValue.Transparent)
            .CornerRadius(6)
            .OnTap(() => { Trigger(action, null); });
    }
}
