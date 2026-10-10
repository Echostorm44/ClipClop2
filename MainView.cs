using System.Globalization;
using Cascade.UI;
using ClipClop2.History;
using ClipClop2.Ui;

namespace ClipClop2;

/// <summary>Which entries the type dropdown shows.</summary>
internal enum TypeFilter
{
    All,
    Text,
    Images,
    Files,
    Links,
    Emails,
    Colors,
}

/// <summary>
/// The clipboard history popup (Raycast layout): search and type filter on top, history list on the
/// left, preview and information on the right, hint footer at the bottom, Ctrl+K actions over it,
/// and the Settings screen in the same window.
/// </summary>
internal sealed partial class MainView : Component
{
    private const float RowHeight = 36f;
    private const int PageStep = 8;

    private enum Screen
    {
        History,
        Settings,
    }

    private static readonly IReadOnlyList<SelectOption<TypeFilter>> FilterOptions =
    [
        new(TypeFilter.All, "All Types"),
        new(TypeFilter.Text, "Text"),
        new(TypeFilter.Images, "Images"),
        new(TypeFilter.Files, "Files"),
        new(TypeFilter.Links, "Links"),
        new(TypeFilter.Emails, "Emails"),
        new(TypeFilter.Colors, "Colors"),
    ];

    private readonly NodeRef<TextInput> searchRef = new();
    private readonly NodeRef<ListView<ClipEntry>> historyListRef = new();
    private readonly Func<ClipEntry, IReadOnlyList<ContextMenuItem>> itemMenu;
    private readonly Func<ClipEntry, string> itemAccessibleName = AccessibleName;
    private readonly PreviewCache previews = new();

    private Screen screen = Screen.History;

    // History list. `visible` is rebuilt only when history, search or filter change, so the list
    // instance stays reference-stable between renders.
    private List<ClipEntry> visible = [];
    private bool historyStale = true;
    private ClipEntry? selected;
    private int selectedIndex = -1;
    private string search = "";
    private TypeFilter filter = TypeFilter.All;

    // Shift held: number hints on the first nine rows.
    private bool showHints;

    // The footer button under the pointer, if any.
    private FooterButtonId hoveredFooterButton;

    private enum FooterButtonId
    {
        None,
        Paste,
        Actions,
        Back,
    }

    // A paste waits for its key's release (the window then hides with no key still down), the way
    // the original ClipClop pasted on Enter's key-up.
    private Cascade.UI.Key pendingKey = Cascade.UI.Key.None;
    private Action? pendingAction;

    private readonly Func<KeyEvent, bool> onKeyDown;
    private readonly Action<KeyEvent> onKeyUp;

    public MainView()
    {
        onKeyDown = OnKeyDown;
        itemMenu = ItemMenu;
        actionRow = ActionRow;
        onKeyUp = e => { OnKeyUpCommit(e); };
        actions = BuildActions();
        previews.ThumbnailReady = Invalidate;
    }

    protected override Task OnMounted()
    {
        ClipClopApp.History.Changed += OnHistoryChanged;
        ClipClopApp.Opening += OnOpening;
        ClipClopApp.Hidden += OnHidden;
        Keyboard.ModifiersChanged += OnModifiersChanged;
        ClipClopApp.OnWindowReady();
        return Task.CompletedTask;
    }

    protected override void OnUnmounted()
    {
        ClipClopApp.History.Changed -= OnHistoryChanged;
        ClipClopApp.Opening -= OnOpening;
        ClipClopApp.Hidden -= OnHidden;
        Keyboard.ModifiersChanged -= OnModifiersChanged;
    }

    // ── App events ────────────────────────────────────────────────────

    // While hidden nothing renders: a copy only marks the list stale.
    private void OnHistoryChanged()
    {
        historyStale = true;
        if (App.Window.IsVisible)
        {
            RefreshVisible();
            Invalidate();
        }
    }

    private void OnOpening(bool settings)
    {
        screen = settings ? Screen.Settings : Screen.History;
        search = "";
        actionsOpen = false;
        hoveredFooterButton = FooterButtonId.None;
        showHints = false;
        pendingKey = Cascade.UI.Key.None;
        pendingAction = null;
        historyStale = true;
        RefreshVisible();
        SelectRow(visible.Count > 0 ? 0 : -1);
        if (screen == Screen.History)
        {
            FocusSearch();
        }
        Invalidate();
    }

    private void OnHidden()
    {
        previews.Trim();
    }

    private void OnModifiersChanged(ModifierKeys modifiers)
    {
        bool hints = modifiers == ModifierKeys.Shift && screen == Screen.History && !actionsOpen;
        if (hints != showHints)
        {
            showHints = hints;
            Invalidate();
        }
    }

    // ── History list state ───────────────────────────────────────────

    private void RefreshVisible()
    {
        if (!historyStale)
        {
            return;
        }
        historyStale = false;

        List<ClipEntry> all = ClipClopApp.History.Snapshot();
        string needle = search.Trim();
        var rows = new List<ClipEntry>(all.Count);
        foreach (ClipEntry entry in all)
        {
            if (Matches(entry, filter) && (needle.Length == 0 || entry.SearchText.Contains(needle, StringComparison.OrdinalIgnoreCase)))
            {
                rows.Add(entry);
            }
        }
        visible = rows;

        int index = selected is null ? -1 : visible.IndexOf(selected);
        SelectRow(index >= 0 ? index : (visible.Count > 0 ? 0 : -1));
    }

    private static bool Matches(ClipEntry entry, TypeFilter filter) => filter switch
    {
        TypeFilter.Text => entry.Kind == ClipKind.Text,
        TypeFilter.Images => entry.Kind == ClipKind.Image,
        TypeFilter.Files => entry.Kind == ClipKind.Files,
        TypeFilter.Links => entry.Kind == ClipKind.Link,
        TypeFilter.Emails => entry.Kind == ClipKind.Email,
        TypeFilter.Colors => entry.Kind == ClipKind.Color,
        _ => true,
    };

    private void SelectRow(int index)
    {
        selectedIndex = index;
        selected = index >= 0 && index < visible.Count ? visible[index] : null;
    }

    private void MoveSelection(int delta)
    {
        if (visible.Count == 0)
        {
            return;
        }
        SelectRow(Math.Clamp(selectedIndex + delta, 0, visible.Count - 1));
        AnnounceSelection();
        Invalidate();
    }

    // Focus stays in the search box while the arrow keys move the list selection, so a screen reader
    // would not hear the newly selected entry: say it. (Does nothing when no screen reader listens.)
    private void AnnounceSelection()
    {
        if (selected is not { } entry)
        {
            return;
        }
        Accessibility.Announce(string.Create(CultureInfo.CurrentCulture,
            $"{AccessibleName(entry)}, {selectedIndex + 1} of {visible.Count}"));
    }

    private static string AccessibleName(ClipEntry entry)
    {
        string pinned = entry.Pinned ? ", pinned" : "";
        return $"{entry.DisplayLine}, {Look.KindName(entry.Kind)}{pinned}";
    }

    private void SetSearch(string value)
    {
        search = value;
        historyStale = true;
        selected = null;
        RefreshVisible();
        Invalidate();
    }

    private void SetFilter(TypeFilter value)
    {
        filter = value;
        historyStale = true;
        RefreshVisible();
        Invalidate();
    }

    private void FocusSearch()
    {
        if (searchRef.Node is { } input)
        {
            FocusManager.RequestFocus(input);
        }
    }

    // ── Keyboard ──────────────────────────────────────────────────────

    private bool OnKeyDown(KeyEvent e)
    {
        if (screen == Screen.Settings)
        {
            return SettingsKeyDown(e);
        }
        if (actionsOpen)
        {
            return ActionsKeyDown(e);
        }

        ModifierKeys mods = e.Modifiers;
        switch (e.Key)
        {
            case Cascade.UI.Key.Up when mods == ModifierKeys.None:
                MoveSelection(-1);
                return true;
            case Cascade.UI.Key.Down when mods == ModifierKeys.None:
                MoveSelection(1);
                return true;
            case Cascade.UI.Key.PageUp when mods == ModifierKeys.None:
                MoveSelection(-PageStep);
                return true;
            case Cascade.UI.Key.PageDown when mods == ModifierKeys.None:
                MoveSelection(PageStep);
                return true;
            case Cascade.UI.Key.Escape when mods == ModifierKeys.None:
                if (search.Length > 0)
                {
                    SetSearch("");
                }
                else
                {
                    ClipClopApp.HidePopup();
                }
                return true;
            case Cascade.UI.Key.K when mods == ModifierKeys.Ctrl:
                OpenActions();
                return true;
            case Cascade.UI.Key.F10 when mods == ModifierKeys.Shift:
            case Cascade.UI.Key.Apps when mods == ModifierKeys.None:
                // Focus stays in the search box, so the selected row's menu is opened here.
                if (selected is not null)
                {
                    historyListRef.Node?.ShowContextMenu();
                }
                return true;
            case Cascade.UI.Key.Comma when mods == ModifierKeys.Ctrl:
                // Settings is Ctrl+S (shown in the actions menu); Ctrl+, is the Windows convention, kept too.
                OpenSettings();
                return true;
        }

        // Shift+1…9 pastes that row, counting from the top of the list as shown.
        if (mods == ModifierKeys.Shift && e.Key is >= Cascade.UI.Key.D1 and <= Cascade.UI.Key.D9)
        {
            int index = e.Key - Cascade.UI.Key.D1;
            if (!e.IsRepeat && index < visible.Count)
            {
                ClipEntry entry = visible[index];
                Defer(e.Key, () => { ClipClopApp.Clipboard.Paste(entry); });
            }
            return true;
        }

        return RunShortcut(e);
    }

    private bool OnKeyUpCommit(KeyEvent e)
    {
        if (pendingAction is null || e.Key != pendingKey)
        {
            return false;
        }
        Action run = pendingAction;
        pendingAction = null;
        pendingKey = Cascade.UI.Key.None;
        run();
        return true;
    }

    private void Defer(Cascade.UI.Key key, Action action)
    {
        pendingKey = key;
        pendingAction = action;
    }

    // ── Render ────────────────────────────────────────────────────────

    protected override Node Render()
    {
        Node content = screen == Screen.Settings ? SettingsScreen() : HistoryScreen();
        return new KeyHandler(content)
        {
            KeyDown = onKeyDown,
            KeyUp = onKeyUp,
        };
    }

    private Node HistoryScreen()
    {
        Node main = new Column(children:
            [
                SearchBar(),
                new Row(children:
                    [
                        HistoryList().Width(330).BorderRight(Look.Hairline, 1),
                        Preview().Expand(),
                    ]).Expand(),
                Footer(),
            ])
            .Background(Look.Background);

        // The overlay slot is always there: swapping the root between `main` and a Stack remounted the
        // whole screen on every Ctrl+K (the list rebuilt, its scroll lost) and the remounted search box,
        // an AutoFocus node first in document order, took focus back from the menu's search box.
        return new Stack(main, actionsOpen ? ActionsOverlay() : Node.Empty);
    }

    private Node SearchBar()
    {
        return new Row(
                spacing: 8,
                crossAxisAlignment: CrossAxisAlignment.Center,
                children:
                [
                    new TextInput(Bind(search, SetSearch), placeholder: "Type to filter entries…", icon: Look.Search)
                        .Ref(searchRef)
                        .AutoFocus()
                        .Expand(),
                    new Select<TypeFilter>(Bind(filter, SetFilter), FilterOptions, placeholder: "All Types")
                        .Width(170),
                ])
            .Padding(EdgeInsets.Symmetric(horizontal: 14, vertical: 10))
            .BorderBottom(Look.Hairline, 1);
    }

    private Node Footer()
    {
        return new Row(
                spacing: 8,
                crossAxisAlignment: CrossAxisAlignment.Center,
                children:
                [
                    new IconView(Look.Paste, size: 14).Color(Look.TextSecondary),
                    new Label("Clipboard History").FontSize(12).Color(Look.TextSecondary).Expand(),
                    FooterButton(FooterButtonId.Paste, "Paste", Look.TextPrimary, ["↵"], enabled: selected is not null,
                        () => { Trigger(PasteAction, null); }),
                    new Label("|").FontSize(12).Color(Look.TextDim),
                    FooterButton(FooterButtonId.Actions, "Actions", Look.TextSecondary, ["Ctrl", "K"], enabled: true,
                        () => { OpenActions(); }),
                ])
            .Padding(EdgeInsets.Only(left: 14, right: 8, top: 4, bottom: 4))
            .BorderTop(Look.Hairline, 1);
    }

    /// <summary>A footer hint that is also a button: the label and its keys, highlighted under the pointer.</summary>
    private Node FooterButton(FooterButtonId id, string label, ColorValue color, string[] keys, bool enabled, Action onClick)
    {
        var children = new Node[keys.Length + 1];
        children[0] = new Label(label).FontSize(12).Color(enabled ? color : Look.TextDim);
        for (int i = 0; i < keys.Length; i++)
        {
            children[i + 1] = Look.KeyCapLabel(keys[i]);
        }

        Node button = new Row(spacing: 6, crossAxisAlignment: CrossAxisAlignment.Center, children: children)
            .Padding(EdgeInsets.Symmetric(horizontal: 6, vertical: 0))
            .Height(26)
            .CornerRadius(6)
            .Background(enabled && hoveredFooterButton == id ? Look.ButtonHover : ColorValue.Transparent)
            .AccessibleRole(AccessibleRole.Button)
            .AccessibleLabel(label);
        if (!enabled)
        {
            return button;
        }
        return button
            .OnPointerEnter(() => { SetHoveredFooterButton(id); })
            .OnPointerLeave(() => { SetHoveredFooterButton(hoveredFooterButton == id ? FooterButtonId.None : hoveredFooterButton); })
            .OnTap(onClick);
    }

    private void SetHoveredFooterButton(FooterButtonId id)
    {
        if (hoveredFooterButton == id)
        {
            return;
        }
        hoveredFooterButton = id;
        Invalidate();
    }
}
