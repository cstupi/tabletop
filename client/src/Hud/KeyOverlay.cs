using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Table;
using Tabletop.Client.Theming;
using Tabletop.Protocol;

namespace Tabletop.Client.Hud;

/// <summary>
/// Centre: the keys (UX redesign part 2 → Key overlay, KeyOverlayView): the open Ask's keys first, then Camera, Measure and Table, as key caps
/// in captioned groups. F1 or ? opens and closes it, as does <see cref="Request"/> (the decision card's ⌨); any other key closes it.
/// </summary>
public partial class KeyOverlay : PanelContainer
{
    /// <summary>Opens the overlay from a control (the decision card's ⌨).</summary>
    public static void Request() => Requested?.Invoke();
    static event Action? Requested;

    Session _s = null!;
    readonly HBoxContainer _columns = new();
    Action? _onRequest, _onTheme;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session;
        SetAnchorsAndOffsetsPreset(LayoutPreset.Center, LayoutPresetMode.Minsize);
        GrowHorizontal = GrowDirection.Both; GrowVertical = GrowDirection.Both;
        Visible = false; MouseFilter = MouseFilterEnum.Stop;
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 14); AddChild(box);
        var title = new HBoxContainer(); title.AddThemeConstantOverride("separation", 10); box.AddChild(title);
        title.AddChild(HudBits.Icon("key", 28, Ui.Accent));
        title.AddChild(new Label { Text = "KEYS", ThemeTypeVariation = Variation.Display, VerticalAlignment = VerticalAlignment.Center });
        _columns.AddThemeConstantOverride("separation", 40); box.AddChild(_columns);
        _onRequest = () => { if (IsInsideTree()) Open(); }; Requested += _onRequest;
        _onTheme = () => { if (Visible) Build(); }; Ui.Changed += _onTheme;
    }

    public override void _ExitTree() { Requested -= _onRequest; Ui.Changed -= _onTheme; }

    void Open() { Build(); Visible = true; }

    /// <summary>The groups for the Ask open now: the first column is the Ask's (or Camera), the rest stack in the second.</summary>
    void Build()
    {
        foreach (var c in _columns.GetChildren()) { _columns.RemoveChild(c); c.QueueFree(); }
        AskMode? mode = null; string? title = null; var stoppable = false;
        if (_s.Projection is { } p && _s.Game is { } game && !_s.IsWatching && AskViewModel.Mine(p, game, _s.MyTeamId, _s.Settings.MutedWindowKinds) is { } mine)
        {
            mode = mine.Mode; title = mine.Ask.Prompt;
            stoppable = mine.Ask.Legal is MoveRegion m && DecisionView.StopLabel(p, m) is not null;
        }
        var groups = KeyOverlayView.Of(mode, title, stoppable);
        var first = new VBoxContainer(); var rest = new VBoxContainer();
        first.AddThemeConstantOverride("separation", 18); rest.AddThemeConstantOverride("separation", 18);
        _columns.AddChild(first); _columns.AddChild(rest);
        for (var i = 0; i < groups.Count; i++) (i == 0 ? first : rest).AddChild(Group(groups[i], now: i == 0 && mode is not null));
    }

    static Control Group(KeyGroup g, bool now)
    {
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 6);
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 8); box.AddChild(head);
        head.AddChild(HudBits.Icon(g.Icon, 20, now ? Ui.Accent : Ui.DimText));
        head.AddChild(new Label { Text = g.Title.ToUpperInvariant(), ThemeTypeVariation = now ? Variation.Accent : Variation.Dim, VerticalAlignment = VerticalAlignment.Center });
        var grid = new GridContainer { Columns = 2 }; grid.AddThemeConstantOverride("h_separation", 14); grid.AddThemeConstantOverride("v_separation", 6); box.AddChild(grid);
        foreach (var row in g.Rows)
        {
            var keys = HudBits.KeyCaps(row.Keys); keys.Alignment = BoxContainer.AlignmentMode.End; grid.AddChild(keys);
            grid.AddChild(new Label { Text = row.Does, VerticalAlignment = VerticalAlignment.Center });
        }
        return box;
    }

    public override void _Input(InputEvent e)
    {
        if (IntentOf(e, GetViewport()) == Intent.KeyOverlay) { if (Visible) Visible = false; else Open(); GetViewport().SetInputAsHandled(); }
        else if (Visible && e is InputEventKey { Pressed: true, Echo: false }) { Visible = false; GetViewport().SetInputAsHandled(); }
    }

    /// <summary>A key press to its intent. HotKey carries the Godot names, so the keycode converts by name. Nothing while a
    /// text field has focus: a menu's number prompt or a spin box takes its own digits and Enter.</summary>
    public static Intent? IntentOf(InputEvent e, Viewport viewport) =>
        e is InputEventKey { Pressed: true, Echo: false } k && viewport.GuiGetFocusOwner() is not (LineEdit or TextEdit)
            && Enum.TryParse<HotKey>(k.Keycode.ToString(), out var key) && Enum.IsDefined(key) ? Hotkeys.Of(key, k.ShiftPressed, k.CtrlPressed) : null;
}
