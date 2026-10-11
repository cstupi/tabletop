using Godot; using Tabletop.Client.Core; using Tabletop.Client.Hud; using Tabletop.Client.Theming; using Tabletop.Protocol;
namespace Tabletop.Client.Table;

/// <summary>Results rising over a unit for 1.2 s, each shown when the Animator plays the event that produced it. Bound before the Animator is made, so it hears each batch first.
/// Its CanvasLayer inherits no theme, so it sets its own.</summary>
public partial class FloatingText : Control
{
    Session _s = null!; TableView _view = null!; readonly FloatingResults _results = new();
    readonly Dictionary<long, IReadOnlyList<FloatingResult>> _pending = new(); readonly List<(Vector3 At, Control Label, double Age)> _live = new();
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action? _onBoard, _onTheme;

    public void Bind(Session session, TableView view)
    {
        _s = session; _view = view; SetAnchorsPreset(LayoutPreset.FullRect); MouseFilter = MouseFilterEnum.Ignore;
        if (session.Projection is { } p) _results.Seed(p);
        _onEvents = batch => { if (_s.Projection is { } q) foreach (var (seq, r) in _results.OnBatch(batch, q, _s.CatchingUp)) _pending[seq] = r; };
        _onBoard = () => { _results.OnBoard(); _pending.Clear(); };
        _onTheme = () => Theme = Ui.Theme;
        session.EventsApplied += _onEvents; session.BoardChanged += _onBoard; Ui.Changed += _onTheme; _onTheme();
    }
    public override void _ExitTree() { Ui.Changed -= _onTheme; if (_s is null) return; _s.EventsApplied -= _onEvents; _s.BoardChanged -= _onBoard; }

    public void Show(GameEvent e)
    {
        if (!_pending.Remove(e.Seq, out var results)) return;
        var i = 0;
        foreach (var r in results.Where(r => r.At is not null))
        {
            var label = Roster.Chip(r.Text); label.MouseFilter = MouseFilterEnum.Ignore; AddChild(label);
            _live.Add((WorldFrame.ToGodot(r.At!.Value) + Vector3.Up * (2.2f + 0.6f * i++), label, 0));
        }
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
    }
}
