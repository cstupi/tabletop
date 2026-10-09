using Godot;
using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Table;

/// <summary>The queue between the projection and the renderer. The projection is already current; this only paces what the table plays out.</summary>
public sealed class Animator : IDisposable
{
    readonly Session _s; readonly TableView _view; readonly TableInput _input; readonly AnimationQueue _queue = new();
    readonly Action<IReadOnlyList<GameEvent>> _onEvents;
    bool _busy, _disposed; int _generation;

    public Animator(Session s, TableView view, TableInput input)
    {
        _s = s; _view = view; _input = input;
        _onEvents = events => { _queue.Enqueue(events); Pump(); };
        s.EventsApplied += _onEvents;
    }

    /// <summary>Session outlives the table: the subscription comes off when the panels go.</summary>
    public void Dispose() { _disposed = true; _s.EventsApplied -= _onEvents; }

    /// <summary>The tween's end, or the fallback timer if the piece was freed mid-glide and the tween died with it; whichever comes first wins.</summary>
    void Done(int generation)
    {
        if (_disposed || generation != _generation) return;
        _generation++; _busy = false;
        if (GodotObject.IsInstanceValid(_view)) Pump();
    }

    void Pump()
    {
        if (_disposed || _busy || !GodotObject.IsInstanceValid(_view)) return;
        while (!_busy && _queue.Count > 0)
        foreach (var e in _queue.Take(_s.Settings.Animate))
        {
            List<ModelsMoved> moves = e.Payload switch { ModelsMoved m => [m], StepCompleted s => [.. s.Changes.OfType<ModelsMoved>()], _ => [] };
            // The player's own drops are already on the table where they put them: consume the holds, and glide only the rest.
            foreach (var relocation in moves.SelectMany(m => m.Moves).Where(r => !_input.ConsumeOwn(r)).ToList())
                if (_s.Settings.Animate && relocation.To is OnTable to && _view.PieceAt(relocation.ModelId) is { } node && node.IsInsideTree())
                {
                    // The table has already redrawn the piece at its destination, so the glide starts from where it was.
                    if (relocation.From is OnTable from) node.Position = WorldFrame.ToGodot(from.Placement.Position);
                    var target = WorldFrame.ToGodot(to.Placement.Position);
                    _busy = true; var generation = _generation;
                    var tween = node.CreateTween(); tween.TweenProperty(node, "position", target, 0.4); tween.TweenCallback(Callable.From(() => { if (GodotObject.IsInstanceValid(_view)) _view.Redraw(); Done(generation); }));
                    _view.GetTree().CreateTimer(0.6).Timeout += () => Done(generation);
                    return;   // one at a time while animating
                }
        }
        _view.Redraw();
    }
}
