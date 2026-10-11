using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Table;

namespace Tabletop.Client.Hud;

/// <summary>Centre: every hotkey and what it does. F1 or ? opens and closes it; Esc closes it.</summary>
public partial class KeyOverlay : PanelContainer
{
    static readonly (string Key, string Does)[] Rows =
    [
        ("1–9", "pick the numbered option in the decision box"),
        ("1–5, while holding a unit", "arrange it in that many rows"),
        ("W A S D", "move the camera across the table"),
        ("Q / E", "turn the held unit; otherwise orbit the camera"),
        ("Enter", "confirm the decision: commit, fire, remove"),
        ("C", "stop the move here (cast or pick), when the move box offers it"),
        ("Esc", "cancel, innermost first: a drag, the target assignments, the selection"),
        ("Shift (held)", "one model: click selects, drag moves"),
        ("Alt (while dragging)", "place freely"),
        ("L (held)", "sight check on the hovered unit"),
        ("M", "measure tool"),
        ("Ctrl (held)", "measure: click and drag; letting go removes the line"),
        ("P", "ping"),
        ("Tab / Shift+Tab", "next / previous of my units not yet activated"),
        ("Space", "glide the camera to the active unit, else the selected one"),
        ("R", "collapse or expand the roster"),
        ("V", "range rings for my selected unit"),
        ("Ctrl+Z", "undo the last action, whoever took it"),
        ("G", "open or close the log"),
        ("Home / End", "camera top-down / level"),
        ("F1 or ?", "this overlay"),
    ];

    public void Bind(Session session, TableView view, TableInput input)
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.Center, LayoutPresetMode.Minsize);
        GrowHorizontal = GrowDirection.Both; GrowVertical = GrowDirection.Both;
        Visible = false;
        var grid = new GridContainer { Columns = 2 }; AddChild(grid);
        foreach (var (key, does) in Rows) { grid.AddChild(new Label { Text = key }); grid.AddChild(new Label { Text = does }); }
    }

    public override void _Input(InputEvent e)
    {
        switch (IntentOf(e, GetViewport()))
        {
            case Intent.KeyOverlay: Visible = !Visible; GetViewport().SetInputAsHandled(); break;
            case Intent.Cancel when Visible: Visible = false; GetViewport().SetInputAsHandled(); break;
        }
    }

    /// <summary>A key press to its intent. HotKey carries the Godot names, so the keycode converts by name. Nothing while a
    /// text field has focus: a menu's number prompt or a spin box takes its own digits and Enter.</summary>
    public static Intent? IntentOf(InputEvent e, Viewport viewport) =>
        e is InputEventKey { Pressed: true, Echo: false } k && viewport.GuiGetFocusOwner() is not (LineEdit or TextEdit)
            && Enum.TryParse<HotKey>(k.Keycode.ToString(), out var key) && Enum.IsDefined(key) ? Hotkeys.Of(key, k.ShiftPressed, k.CtrlPressed) : null;
}
