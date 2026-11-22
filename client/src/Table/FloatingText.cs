using Godot; using Tabletop.Client.Core; using Tabletop.Client.Hud; using Tabletop.Client.Theming; using Tabletop.Protocol;
namespace Tabletop.Client.Table;

/// <summary>Results rising over a unit for 1.2 s, and with Animate on a red "-1" per wound over a wounded model's head (WoundNumbers), each
/// shown when the Animator plays the event that produced it. Bound before the Animator is made, so it hears each batch first. Processes only
/// while something floats. Its CanvasLayer inherits no theme, so it sets its own.</summary>
public partial class FloatingText : Control
{
    const float NumberRisePx = 110, NumberSpreadPx = 12, NumberLift = 0.3f;
    static readonly LabelSettings NumberLook = new()
    {
        FontSize = 40, FontColor = new Color(0.95f, 0.16f, 0.12f), OutlineSize = 10, OutlineColor = new Color(0.07f, 0.02f, 0.02f),
    };
    Session _s = null!; TableView _view = null!; readonly FloatingResults _results = new(); readonly WoundNumbers _numbers = new();
    readonly Dictionary<long, IReadOnlyList<FloatingResult>> _pending = new(); readonly List<(Vector3 At, Control Label, double Age)> _live = new();
    readonly Dictionary<long, IReadOnlyList<WoundNumber>> _pendingNumbers = new();
    readonly List<(Vector3 At, float Dx, Label Label, double Age)> _liveNumbers = new(); readonly Stack<Label> _spareNumbers = new();
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action? _onBoard, _onTheme;

    public void Bind(Session session, TableView view)
    {
        _s = session; _view = view; SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); MouseFilter = MouseFilterEnum.Ignore; SetProcess(false);
        if (session.Projection is { } p) _numbers.Seed(p);
        _onEvents = batch =>
        {
            if (_s.Projection is not { } q) return;
            foreach (var (seq, r) in _results.OnBatch(batch, q, _s.CatchingUp)) _pending[seq] = r;
            foreach (var (seq, n) in _numbers.OnBatch(batch, q, _s.CatchingUp, _s.MaxWounds)) _pendingNumbers[seq] = n;
            if (batch.Count == 0 || batch.Any(e => e.Payload is Undone)) ClearNumbers();
        };
        _onBoard = () => { _numbers.OnBoard(); _pending.Clear(); ClearNumbers(); };
        _onTheme = () => Theme = Ui.Theme;
        session.EventsApplied += _onEvents; session.BoardChanged += _onBoard; Ui.Changed += _onTheme; _onTheme();
    }
    public override void _ExitTree() { Ui.Changed -= _onTheme; if (_s is null) return; _s.EventsApplied -= _onEvents; _s.BoardChanged -= _onBoard; }

    public void Show(GameEvent e)
    {
        if (_pending.Remove(e.Seq, out var results))
        {
            var i = 0;
            foreach (var r in results.Where(r => r.At is not null))
            {
                var label = Roster.Chip(r.Text); label.MouseFilter = MouseFilterEnum.Ignore; AddChild(label);
                _live.Add((WorldFrame.ToGodot(r.At!.Value) + Vector3.Up * (2.2f + 0.6f * i++), label, 0));
            }
        }
        if (_pendingNumbers.Remove(e.Seq, out var numbers) && _s.Settings.Animate && _s.Settings.WoundNumbers)
            foreach (var n in numbers)
            {
                var label = _spareNumbers.Count > 0 ? _spareNumbers.Pop() : NewNumber();
                label.Visible = false;
                _liveNumbers.Add((WorldFrame.ToGodot(n.Head) + Vector3.Up * NumberLift, ((n.Index + 1) % 3 - 1) * NumberSpreadPx, label, -n.Delay));
            }
        SetProcess(_live.Count + _liveNumbers.Count > 0);
    }

    Label NewNumber()
    {
        var label = new Label { Text = WoundNumbers.Text, LabelSettings = NumberLook, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        AddChild(label);
        return label;
    }

    /// <summary>An undo, a replay's jump or a new board: what still floats goes, nothing waiting shows.</summary>
    void ClearNumbers()
    {
        _pendingNumbers.Clear();
        foreach (var n in _liveNumbers) { n.Label.Visible = false; _spareNumbers.Push(n.Label); }
        _liveNumbers.Clear();
    }

    public override void _Process(double delta)
    {
        var cam = _view.Camera;
        for (var i = _live.Count - 1; i >= 0; i--)
        {
            var (at, label, age) = _live[i]; age += delta;
            if (age >= FloatingResults.Seconds) { label.QueueFree(); _live.RemoveAt(i); continue; }
            _live[i] = (at, label, age);
            label.Visible = !cam.IsPositionBehind(at);
            label.Position = cam.UnprojectPosition(at + Vector3.Up * (float)age) - label.Size / 2;
            label.Modulate = label.Modulate with { A = (float)(1 - age / FloatingResults.Seconds) };
        }
        for (var i = _liveNumbers.Count - 1; i >= 0; i--)
        {
            var (at, dx, label, age) = _liveNumbers[i]; age += delta;
            if (age >= WoundNumbers.Seconds) { label.Visible = false; _spareNumbers.Push(label); _liveNumbers.RemoveAt(i); continue; }
            _liveNumbers[i] = (at, dx, label, age);
            if (age < 0) continue;
            var (rise, alpha) = WoundNumbers.Look(age);
            label.Visible = !cam.IsPositionBehind(at);
            label.Position = cam.UnprojectPosition(at) + new Vector2(dx, -NumberRisePx * (float)rise) - label.Size / 2;
            label.Modulate = new Color(1, 1, 1, (float)alpha);
        }
        if (_live.Count + _liveNumbers.Count == 0) SetProcess(false);
    }
}
