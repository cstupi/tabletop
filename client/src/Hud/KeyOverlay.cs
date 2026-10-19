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
        ("Q / E", "turn each held model or the held terrain piece 15° (Shift: 5°), or the held rows; otherwise orbit the camera"),
        ("Enter", "confirm the decision: commit, fire, remove"),
        ("C", "stop the move here (cast or pick), when the move box offers it"),
        ("Esc", "cancel, innermost first: a drag, the target assignments, the selection"),
        ("Shift (held)", "one model: click selects, drag moves"),
        ("Alt (while dragging)", "place freely: no sliding, stopping or allowance"),
        ("L (held)", "sight check on the hovered unit"),
        ("M", "measure tool: click, then click again (or drag) to fix the line"),
        ("Ctrl (held)", "ruler from the point under the cursor as Ctrl goes down to the cursor; letting go removes it"),
        ("H", "measure 2D (across the table) or 3D (through the height, with Δh); remembered"),
        ("P", "ping"),
        ("Tab / Shift+Tab", "next / previous of my units not yet activated"),
        ("Space", "glide the camera to the active unit, else the selected one"),
        ("R", "collapse or expand the roster"),
        ("V", "range rings for my selected unit"),
        ("B", "show or hide the model hitboxes: off, a model with a mesh draws only the mesh and its base ring; remembered"),
        ("Shift+B", "show or hide the terrain hitboxes: off, terrain with a mesh draws only the mesh; remembered"),
        ("Ctrl+Z", "undo the last action, whoever took it"),
        ("G", "open or close the log"),
        ("Delete / Backspace", "in setup: remove the selected terrain piece or marker"),
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
