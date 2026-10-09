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
        if (k.Echo || Translate(k.Keycode) is not { } key || Hotkeys.Of(key, k.ShiftPressed, k.CtrlPressed) is not { } intent) return;
        if (intent == Intent.Sight) { SetSight(k.Pressed); return; }
        if (k.Pressed && Act(intent)) GetViewport().SetInputAsHandled();
    }

    /// <summary>HotKey's members carry Godot's names. A key Hotkeys does not name is no key; IsDefined stops a numeric name parsing.</summary>
    static HotKey? Translate(Key key) => Enum.TryParse<HotKey>(key.ToString(), out var k) && Enum.IsDefined(k) ? k : null;

    void SetSight(bool held) { if (SightHeld == held) return; SightHeld = held; SightHeldChanged?.Invoke(); }

    /// <summary>L released while the window was elsewhere never arrives: losing focus lets go of it.</summary>
    public override void _Notification(int what) { if (what == NotificationApplicationFocusOut) SetSight(false); }

    /// <summary>True when the key was used. Option1–5, Q and E are the held unit's while one is held, the decision box's otherwise.</summary>
    bool Act(Intent intent)
    {
        switch (intent)
        {
            case >= Intent.Option1 and <= Intent.Option5 when HoldingModels: Arrange(intent - Intent.Option1 + 1); return true;
            case Intent.TurnLeft when HoldingModels: Turn(15); return true;           // positive yaw is counter-clockwise from above
            case Intent.TurnRight when HoldingModels: Turn(-15); return true;
            case Intent.TurnLeft or Intent.TurnRight: return false;
            case Intent.Cancel: Cancel(); return true;
            case Intent.Measure: Tool = Tool == TableTool.Measure ? TableTool.Select : TableTool.Measure; _measureFrom = null; return true;
            case Intent.Ping:
                var (at, target) = Pick(GetViewport().GetMousePosition());
                Observe(_session.SendPingAsync(new Ping(_session.ParticipantId ?? Guid.Empty, at, target is null ? null : IdOf(target))));
                return true;
            case Intent.NextUnit: Cycle(1); return true;
            case Intent.PreviousUnit: Cycle(-1); return true;
            case Intent.FocusCamera: FocusActive(); return true;
            case Intent.CameraTop or Intent.CameraLevel or Intent.SingleModel: return false;
            default: IntentPressed?.Invoke(intent); return true;
        }
    }

    /// <summary>Esc, innermost first: the drag (its models back where they started); the decision box has already taken an Esc it used for assignments; then the selection and the measurement.</summary>
    void Cancel()
    {
        if (_drag is { } drag)
        {
            foreach (var (id, at) in drag.Starts) { MoveNode(id, at); ModelDragged?.Invoke(id, at); }
            CancelDrag();
            return;
        }
        IntentPressed?.Invoke(Intent.Cancel);
        _measurement = null; _measureFrom = null; _ruler.Hide();
        ClearSelection();
    }

    /// <summary>Tab and Shift+Tab: my units on the table with no action recorded this round, in army order, after the selected one; select it and bring it to the centre.</summary>
    void Cycle(int step)
    {
        if (_session.Projection is not { } p || _session.Game is not { } game || _session.MyTeamId is not { } team) return;
        var mine = game.Participants.Where(x => x.TeamId == team).Select(x => x.Id).ToHashSet();
        var units = p.Units.Values
            .Where(u => mine.Contains(u.ParticipantId) && u.Actions.Count == 0 && p.Models.Values.Any(m => m.UnitId == u.Id && m.Location is OnTable))
            .Select(u => u.Id).ToList();
        if (units.Count == 0) return;
        var at = Selected is { } s ? units.IndexOf(s.PieceId) : -1;
        var next = at < 0 ? units[step > 0 ? 0 : units.Count - 1] : units[((at + step) % units.Count + units.Count) % units.Count];
        Select(next); Focus(next);
    }

    /// <summary>Space: the unit whose activation is running (the frame that carries an action), else the selected one.</summary>
    void FocusActive()
    {
        var active = _session.Projection is { } p ? HeaderView.ActionFrame(p)?.Context.ActorUnitId : null;
        if ((active ?? Selected?.PieceId) is { } unit) Focus(unit);
    }

    void Focus(Guid unitId)
    {
        if (_session.Projection is not { } p) return;
        var at = p.Models.Values.Where(m => m.UnitId == unitId).Select(m => m.Location).OfType<OnTable>().Select(o => o.Placement.Position).ToList();
        if (at.Count == 0) return;
        _view.Camera.Centre(WorldFrame.ToGodot(new Point3(at.Average(q => q.X), at.Average(q => q.Y), at.Average(q => q.Z))));
    }
}
