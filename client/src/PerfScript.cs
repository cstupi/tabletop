using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Screens;
using Tabletop.Client.Table;

namespace Tabletop;

/// <summary>
/// A development build's scripted session for <see cref="PerfProbe"/> (<c>TABLETOP_PERF_SCRIPT=play</c>): once the table stands and its
/// meshes have loaded, real input events through the window, each phase named in the probe's bench column. The game must open on my
/// team's activation Ask with a unit whose options are Hold, Advance, Rush (1, 2, 3). Phases: idle; a hover sweep over every unit of
/// another team (the pointer moving a little every frame, as a hand does); T and the sweep again; T off; a click on my acting unit; V
/// and the sweep with the rings on; the camera panned with D held; 2 (Advance); the move's area building; a drag of my unit toward the
/// nearest unit of another team; Enter (the move); the sweep under the shooting Ask; a click on the nearest unit of another team and
/// Enter (target and volley); a rest. <c>TABLETOP_PERF_SHOTS=1</c> photographs each phase. A release build never reads it.
/// </summary>
public partial class PerfScript : Node
{
    public static string Phase { get; private set; } = "";
    public static string? Name => BuildInfo.Current.IsRelease ? null : System.Environment.GetEnvironmentVariable("TABLETOP_PERF_SCRIPT") is { Length: > 0 } s ? s : null;

    const double PerUnit = 0.8, Settle = 4, DragSeconds = 3;
    readonly Queue<(string Name, double Seconds, Action? Start)> _phases = new();
    GameScreen? _screen; double _ready = -1, _clock, _phaseEnd; List<Guid> _sweep = []; int _sweepAt; double _sweepNext;
    readonly RandomNumberGenerator _rng = new() { Seed = 7 };
    Vector2? _pointer; double _shotAt;
    (Vector2 From, Vector2 To, double Start)? _drag;
    static bool Shots => System.Environment.GetEnvironmentVariable("TABLETOP_PERF_SHOTS") == "1";

    public override void _Ready()
    {
        ProcessPriority = int.MinValue + 1;
        _phases.Enqueue(("idle", 4, null));
        _phases.Enqueue(("hover", -1, () => Sweep(Enemies())));
        _phases.Enqueue(("t-on", 2, () => { Key(Godot.Key.T); _pointer = EmptyTable(); }));
        _phases.Enqueue(("hover-t", -1, () => Sweep(Enemies())));
        _phases.Enqueue(("t-off", 2, () => { Key(Godot.Key.T); _pointer = EmptyTable(); }));
        _phases.Enqueue(("select", 1.5, () => Click(Mine())));
        _phases.Enqueue(("v-on", 3, () => { Key(Godot.Key.V); _pointer = EmptyTable(); }));
        _phases.Enqueue(("hover-v", -1, () => Sweep(Enemies())));
        _phases.Enqueue(("pan-v", 2, () => { _pointer = EmptyTable(); Hold(Godot.Key.D, true); }));
        _phases.Enqueue(("advance", 4, () => { Hold(Godot.Key.D, false); Key(Godot.Key.Key2); }));
        _phases.Enqueue(("drag", DragSeconds + 1, StartDrag));
        _phases.Enqueue(("move", 4, () => { _pointer = EmptyTable(); Key(Godot.Key.Enter); }));
        _phases.Enqueue(("hover-ask", -1, () => Sweep(Enemies())));
        _phases.Enqueue(("target", 2, () => Click(Nearest())));
        _phases.Enqueue(("volley", 8, () => { _pointer = EmptyTable(); Key(Godot.Key.Enter); }));
        _phases.Enqueue(("rest", 3, null));
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        _screen ??= GetTree().Root.FindChildren("*", "", true, false).OfType<GameScreen>().FirstOrDefault();
        if (_screen is null || !IsInstanceValid(_screen) || Main.Instance.Session.Projection is null) return;
        if (_ready < 0)
        {
            if (!_screen.Table.SlabReady || _screen.Table.MeshProgress is { } m && m.Done < m.Total) return;
            _ready = _clock + Settle; return;
        }
        if (_clock < _ready) return;
        if (_clock >= _phaseEnd && (_sweep.Count == 0 || _sweepAt >= _sweep.Count && _clock >= _sweepNext)) Next();
        if (_shotAt > 0 && _clock >= _shotAt && PerfProbe.OutPath is { } path) { _shotAt = 0; GetViewport().GetTexture().GetImage().SavePng($"{path}.{Phase}.png"); }
        if (_sweep.Count > 0 && _sweepAt < _sweep.Count && _clock >= _sweepNext) { _pointer = At(_sweep[_sweepAt]); _sweepAt++; _sweepNext = _clock + PerUnit; }
        if (_drag is { } d) { Dragging(d); return; }
        if (_pointer is { } p) Motion(p + new Vector2(_rng.RandfRange(-3, 3), _rng.RandfRange(-3, 3)));
    }

    void Next()
    {
        _sweep = []; _sweepAt = 0;
        if (!_phases.TryDequeue(out var phase)) { Phase = "done"; GetTree().Quit(); return; }
        Phase = phase.Name; _phaseEnd = _clock + Math.Max(0, phase.Seconds); _sweepNext = _clock;
        phase.Start?.Invoke();
        if (Shots) _shotAt = _clock + 0.6;
        GD.Print($"perf script: {phase.Name} at {_clock:0.0} s");
    }

    void Sweep(List<Guid> units) { _sweep = units; _sweepAt = 0; _sweepNext = _clock; }

    /// <summary>A press on my acting unit's first model, the pointer carried a third of the way to the nearest unit of another team over
    /// <see cref="DragSeconds"/>, then the release.</summary>
    void StartDrag()
    {
        if (Mine() is not { } me || At(me) is not { } from || Nearest() is not { } them || At(them) is not { } to) { GD.Print("perf script: nothing to drag"); return; }
        _pointer = null; Motion(from, true);
        Button(from, true);
        _drag = (from, from + (to - from) / 3, _clock);
    }

    void Dragging((Vector2 From, Vector2 To, double Start) d)
    {
        var t = Math.Clamp((_clock - d.Start) / DragSeconds, 0, 1);
        var at = d.From.Lerp(d.To, (float)t);
        Motion(at, true);
        if (t < 1) return;
        Button(at, false); _drag = null; _pointer = null;
    }

    List<Guid> UnitsWhere(Func<Guid?, bool> team)
    {
        var s = Main.Instance.Session;
        if (s.Projection is not { } p || s.Game is not { } g) return [];
        return _screen!.Table.Pieces.OfType<PieceNode>().Select(n => n.UnitId).Distinct()
            .Where(u => p.Units.TryGetValue(u, out var unit) && team(g.Participants.FirstOrDefault(x => x.Id == unit.ParticipantId)?.TeamId)).ToList();
    }
    List<Guid> Enemies() => UnitsWhere(t => t is { } id && !Main.Instance.Session.MyTeamIds.Contains(id));
    Guid? Mine()
    {
        var s = Main.Instance.Session;
        var actor = s.Projection?.Stack.LastOrDefault(f => f.Context.ActorUnitId is not null)?.Context.ActorUnitId;
        return actor ?? UnitsWhere(t => t is { } id && s.MyTeamIds.Contains(id)).FirstOrDefault();
    }
    Guid? Nearest()
    {
        if (Mine() is not { } me || Centre(me) is not { } from) return null;
        return Enemies().OrderBy(u => Centre(u)?.DistanceTo(from) ?? float.MaxValue).FirstOrDefault();
    }

    Vector3? Centre(Guid unit)
    {
        var nodes = _screen!.Table.Pieces.OfType<PieceNode>().Where(n => n.UnitId == unit).ToList();
        return nodes.Count == 0 ? null : nodes.Aggregate(Vector3.Zero, (a, n) => a + n.Bounds.GetCenter()) / nodes.Count;
    }

    /// <summary>A unit's first model's centre in window pixels (the table's container stretches its viewport 1:1).</summary>
    Vector2? At(Guid unit)
    {
        var node = _screen!.Table.Pieces.OfType<PieceNode>().FirstOrDefault(n => n.UnitId == unit);
        if (node is null || _screen.Table.Camera.IsPositionBehind(node.Bounds.GetCenter())) return null;
        return Box().Position + _screen.Table.Camera.UnprojectPosition(node.Bounds.GetCenter());
    }
    Rect2 Box() => _screen!.FindChildren("*", "SubViewportContainer", true, false).OfType<SubViewportContainer>().First().GetGlobalRect();
    Vector2 EmptyTable() => Box().Position + new Vector2(Box().Size.X * 0.5f, Box().Size.Y * 0.08f);

    static void Motion(Vector2 at, bool left = false) =>
        Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, ButtonMask = left ? MouseButtonMask.Left : 0 });
    static void Button(Vector2 at, bool down) =>
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down, Position = at, GlobalPosition = at, ButtonMask = down ? MouseButtonMask.Left : 0 });
    void Click(Guid? unit)
    {
        if (unit is not { } u || At(u) is not { } at) { GD.Print("perf script: nothing to click"); return; }
        _pointer = at; Motion(at);
        Button(at, true); Button(at, false);
    }
    static void Key(Key k) { Hold(k, true); Hold(k, false); }
    static void Hold(Key k, bool down) => Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = down });
}
