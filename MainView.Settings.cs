using System.Globalization;
using Cascade.UI;
using ClipClop2.Storage;
using ClipClop2.Ui;

namespace ClipClop2;

internal sealed partial class MainView
{
    private const int MaxHistoryBudgetMb = 16 * 1024;

    private string? hotkeyMessage;

    private void OpenSettings()
    {
        screen = Screen.Settings;
        hoveredFooterButton = FooterButtonId.None;
        actionsOpen = false;
        showHints = false;
        hotkeyMessage = ClipClopApp.HotkeyProblem;
        FocusManager.ClearFocus();
        Invalidate();
    }

    private void CloseSettings()
    {
        screen = Screen.History;
        hoveredFooterButton = FooterButtonId.None;
        historyStale = true;
        RefreshVisible();
        FocusSearch();
        Invalidate();
    }

    private bool SettingsKeyDown(KeyEvent e)
    {
        if (e.Key == Cascade.UI.Key.Escape && e.Modifiers == ModifierKeys.None)
        {
            CloseSettings();
            return true;
        }
        return false;
    }

    private Node SettingsScreen()
    {
        Settings settings = ClipClopApp.Settings;
        long usedBytes = ClipClopApp.History.UnpinnedBytes;

        return new Column(children:
            [
                new Row(
                        spacing: 8,
                        crossAxisAlignment: CrossAxisAlignment.Center,
                        children:
                        [
                            new IconView(Look.Sliders, size: 16).Color(Look.TextSecondary),
                            new Label("Settings").FontSize(15).Bold().Color(Look.TextPrimary).Expand(),
                        ])
                    .Padding(EdgeInsets.Symmetric(horizontal: 16, vertical: 14))
                    .BorderBottom(Look.Hairline, 1)
                    .WindowDragArea(),

                new ScrollView(new Column(
                        spacing: 0,
                        children:
                        [
                            SettingRow(
                                "Open ClipClop",
                                "The global shortcut that shows this window.",
                                new HotkeyPicker
                                {
                                    Current = settings.Hotkey,
                                    OnChange = OnHotkeyChosen,
                                }),
                            hotkeyMessage is { } message
                                ? new Label(message).FontSize(12).Color(Look.Warning).Padding(EdgeInsets.Only(left: 16, bottom: 8))
                                : Node.Empty,
                            SettingRow(
                                "Start with Windows",
                                "Launch into the tray when you sign in.",
                                new Toggle(Bind(settings.StartWithWindows, v =>
                                {
                                    settings.StartWithWindows = v;
                                    ClipClopApp.SaveSettings();
                                    Invalidate();
                                }), label: "", description: "")),
                            SettingRow(
                                "History memory",
                                $"Unpinned history, RAM and temp cache together (minimum {Settings.MinHistoryBudgetMb} MB). In use: {usedBytes / (1024.0 * 1024.0):0.0} MB.",
                                new NumberInput<int>(
                                        Bind(settings.HistoryBudgetMb, v =>
                                        {
                                            settings.HistoryBudgetMb = Math.Clamp(v, Settings.MinHistoryBudgetMb, MaxHistoryBudgetMb);
                                            ClipClopApp.SaveSettings();
                                            Invalidate();
                                        }),
                                        min: Settings.MinHistoryBudgetMb,
                                        max: MaxHistoryBudgetMb,
                                        step: 50,
                                        format: null,
                                        placeholder: "",
                                        label: "")
                                    .Suffix(new Label("MB").FontSize(12).Color(Look.TextSecondary))
                                    .Width(140)),
                            SettingRow(
                                "Window",
                                settings.PopupPlacementCustomized
                                    ? "Opens at the size and place you left it. Drag the search bar or footer to move it; drag an edge to resize."
                                    : "Opens centered on the screen with the mouse. Drag the search bar or footer to move it; drag an edge to resize.",
                                new Button("Reset", () =>
                                    {
                                        ClipClopApp.ResetPopupPlacement();
                                        Invalidate();
                                    })
                                    .Disabled(!settings.PopupPlacementCustomized)),
                            SettingRow(
                                "Pinned items",
                                $"{ClipClopApp.History.PinnedEntries.Count.ToString("N0", CultureInfo.CurrentCulture)} pinned, saved encrypted for your Windows account.",
                                Node.Empty),
                        ]))
                    .Expand(),

                new Row(
                        spacing: 8,
                        crossAxisAlignment: CrossAxisAlignment.Center,
                        children:
                        [
                            new Label("Clipboard History").FontSize(12).Color(Look.TextSecondary).Expand(),
                            FooterButton(FooterButtonId.Back, "Back", Look.TextPrimary, ["Esc"], enabled: true,
                                () => { CloseSettings(); }),
                        ])
                    .Padding(EdgeInsets.Only(left: 14, right: 8, top: 4, bottom: 4))
                    .BorderTop(Look.Hairline, 1)
                    .WindowDragArea(),
            ])
            .Background(Look.Background);
    }

    private void OnHotkeyChosen(Hotkey hotkey)
    {
        if (ClipClopApp.TryRegisterHotkey(hotkey))
        {
            ClipClopApp.Settings.Hotkey = hotkey;
            ClipClopApp.SaveSettings();
            hotkeyMessage = null;
        }
        else
        {
            hotkeyMessage = ClipClopApp.HotkeyProblem;
        }
        Invalidate();
    }

    private static Node SettingRow(string title, string description, Node control)
    {
        return new Row(
                spacing: 16,
                crossAxisAlignment: CrossAxisAlignment.Center,
                children:
                [
                    new Column(
                        spacing: 2,
                        children:
                        [
                            new Label(title).FontSize(13).Color(Look.TextPrimary),
                            new Label(description).FontSize(12).Color(Look.TextSecondary).Wrap(TextWrap.Wrap),
                        ]).Expand(),
                    control,
                ])
            .Padding(EdgeInsets.Symmetric(horizontal: 16, vertical: 12))
            .BorderBottom(Look.Hairline, 1);
    }
}
