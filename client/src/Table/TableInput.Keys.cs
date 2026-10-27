using Godot;
using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Table;

/// <summary>
/// Keys (spec → Hotkeys). Hotkeys.Of names the intent; the table acts on its own (rows and turns while holding, Esc's drag,
/// M, P, L, Tab, Space) and raises IntentPressed for the rest. Home and End are OrbitCamera's.
/// </summary>
public partial class TableInput
{
    void OnKey(InputEventKey k)
    {
        if (k.Echo || (k.Pressed && TextFocused()) || Translate(k.Keycode) is not { } key || Hotkeys.Of(key, k.ShiftPressed, k.CtrlPressed) is not { } intent) return;
        if (intent == Intent.Sight) { SetSight(k.Pressed); return; }
        if (intent == Intent.MeasureHeld) { SetMeasureHeld(k.Pressed); return; }
        if (k.Pressed && Act(intent)) GetViewport().SetInputAsHandled();
    }

    /// <summary>A text field in the window's own viewport (the terrain card's notes, the setup toolbar's fields) has the key: the
    /// table's viewport hears it too, so a typed space, letter or Enter never reaches a hotkey. So does a selectable text holding a
    /// selection (the log's rows), so the Ctrl of its Ctrl+C starts no ruler. Releases still pass.</summary>
    bool TextFocused() => GetTree().Root.GuiGetFocusOwner() switch
    {
        LineEdit or TextEdit or SpinBox => true,
        RichTextLabel { SelectionEnabled: true } t => t.GetSelectedText().Length > 0,
        _ => false,
    };

    /// <summary>HotKey's members carry Godot's names. A key Hotkeys does not name is no key; IsDefined stops a numeric name parsing.</summary>
    static HotKey? Translate(Key key) => Enum.TryParse<HotKey>(key.ToString(), out var k) && Enum.IsDefined(k) ? k : null;

    void SetSight(bool held) { if (SightHeld == held) return; SightHeld = held; SightHeldChanged?.Invoke(); }

    /// <summary>Ctrl down anchors the ruler under the cursor and it follows the cursor; letting go takes the line away, here and for
    /// everyone else. Not mid-drag: the drag owns the release.</summary>
    void SetMeasureHeld(bool held)
    {
        if (_measureHeld == held || (held && _drag is not null)) return;
        _measureHeld = held;
        if (held) StartTapeAtCursor(); else { _tape.Clear(); ShowTape(); }
    }

    /// <summary>L or Ctrl released while the window was elsewhere never arrives: losing focus lets go of it.</summary>
    public override void _Notification(int what) { if (what == NotificationApplicationFocusOut) { SetSight(false); SetMeasureHeld(false); } }

    /// <summary>True when the key was used. Option1–5 are the held models' while any are held, Q and E and PageDown/PageUp the held models' or terrain piece's; the decision box's and the camera's otherwise.</summary>
    bool Act(Intent intent)
    {
        switch (intent)
        {
            case >= Intent.Option1 and <= Intent.Option5 when HoldingModels: Arrange(intent - Intent.Option1 + 1); return true;
            case Intent.TurnLeft or Intent.TurnRight when HoldingTurnable: return true;      // _Process turns while the key is held
            case Intent.TurnLeft or Intent.TurnRight: return false;
            case Intent.Lower or Intent.Raise when HoldingTurnable: StepLevel(intent == Intent.Raise); return true;
            case Intent.Lower or Intent.Raise: return false;
            case Intent.Cancel: Cancel(); return true;
            case Intent.Measure: Tool = Tool == TableTool.Measure ? TableTool.Select : TableTool.Measure; if (!_measureHeld) { _tape.Clear(); ShowTape(); } return true;
            case Intent.MeasureMode: ToggleMeasureMode(); return true;
            case Intent.Ping:
                var (at, target) = Pick(GetViewport().GetMousePosition());
                Observe(_session.SendPingAsync(new Ping(_session.ParticipantId ?? Guid.Empty, at, target is null ? null : IdOf(target))));
                return true;
            case Intent.NextUnit: Cycle(1); return true;
            case Intent.PreviousUnit: Cycle(-1); return true;
            case Intent.FocusCamera: FocusActive(); return true;
            case Intent.CameraTop or Intent.CameraLevel or Intent.SingleModel or Intent.CameraForward or Intent.CameraBack or Intent.CameraLeft or Intent.CameraRight: return false;   // the camera polls WASD
            default: IntentPressed?.Invoke(intent); return true;
        }
    }

    /// <summary>Esc, innermost first: the drag (its models back where they were picked up, facing as they did); the decision box has already taken an Esc it used for assignments; then the selection and the measurement.</summary>
    void Cancel()
    {
        if (_drag is { } drag)
        {
            foreach (var (id, at) in drag.Grabs) { MoveNode(id, at.Position); Face(id, at.Yaw); }
            ModelsDragged?.Invoke(drag.Grabs.Select(kv => (kv.Key, kv.Value.Position)).ToList());
            CancelDrag();
            return;
        }
        IntentPressed?.Invoke(Intent.Cancel);
        _tape.Clear(); ShowTape();
        ClearSelection();
    }

    /// <summary>Tab and Shift+Tab: my units on the table with no action recorded this round, in army order, after the selected one; select it and
    /// bring it to the centre. Past the last comes no selection (SelectionView.Cycle), which widens the decision box to every unit's options.</summary>
    void Cycle(int step)
    {
        if (_session.Projection is not { } p || _session.Game is not { } game || _session.MyTeamId is not { } team) return;
        var mine = game.Participants.Where(x => x.TeamId == team).Select(x => x.Id).ToHashSet();
        var units = p.Units.Values
            .Where(u => mine.Contains(u.ParticipantId) && u.Actions.Count == 0 && p.Models.Values.Any(m => m.UnitId == u.Id && m.Location is OnTable))
            .Select(u => u.Id).ToList();
        if (units.Count == 0) return;
        if (SelectionView.Cycle(units, Selected?.PieceId, step) is { } next) { Select(next); Focus(next); }
        else ClearSelection();
    }

    /// <summary>Space: the unit whose activation is running (the frame that carries an action), else the selected one.</summary>
    void FocusActive()
    {
        var active = _session.Projection is { } p ? HeaderView.ActionFrame(p)?.Context.ActorUnitId : null;
        if ((active ?? Selected?.PieceId) is { } unit) Focus(unit);
    }

    public void Focus(Guid unitId) { if (_session.Projection is { } p && CameraFocus.Centroid(p, unitId) is { } c) _view.Camera.Centre(WorldFrame.ToGodot(c), byPlayer: true); }
}
