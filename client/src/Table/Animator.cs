using Godot;
using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Table;

/// <summary>The queue between the projection and the renderer. The projection is already current; this only paces what the table plays out.</summary>
public sealed class Animator : IDisposable
{
    readonly Session _s; readonly TableView _view; readonly TableInput _input; readonly AnimationQueue _queue = new();
    readonly Action<IReadOnlyList<GameEvent>> _onEvents; readonly Action _onBoard; readonly List<Tween> _tweens = new();
    bool _busy, _disposed; int _generation;
    /// <summary>Each event as it plays; with Animate off, a whole batch at once.</summary>
    public event Action<GameEvent>? Played;

    public Animator(Session s, TableView view, TableInput input)
    {
        _s = s; _view = view; _input = input;
        _onEvents = events => { _queue.Enqueue(events); Pump(); };
        _onBoard = Drop;
        s.EventsApplied += _onEvents; s.BoardChanged += _onBoard;
    }

    /// <summary>Session outlives the table: the subscriptions come off when the panels go.</summary>
    public void Dispose() { _disposed = true; _s.EventsApplied -= _onEvents; _s.BoardChanged -= _onBoard; }

    /// <summary>A new board (a reconnect, a replay's jump) makes what is queued stale: the glides in flight stop, nothing queued plays,
    /// and the table, already redrawn at the new state, stays as drawn.</summary>
    void Drop()
    {
        foreach (var t in _tweens) if (GodotObject.IsInstanceValid(t)) t.Kill();
        _tweens.Clear(); _queue.Clear(); _generation++; _busy = false;
        if (GodotObject.IsInstanceValid(_view)) _view.Redraw();
    }

    /// <summary>The last tween's end, or the fallback timer if a piece was freed mid-glide and its tween died with it; whichever comes first wins.</summary>
    void Done(int generation)
    {
        if (_disposed || generation != _generation) return;
        _generation++; _busy = false; _tweens.Clear();
        if (GodotObject.IsInstanceValid(_view)) Pump(glided: true);
    }

    /// <summary>
    /// The table redraws itself on each batch before this runs (it subscribes first), so the pump redraws only what that pass
    /// could not know: a glide that has ended (<paramref name="glided"/>), or the player's own drops whose holds it consumed.
    /// </summary>
    void Pump(bool glided = false)
    {
        if (_disposed || _busy || !GodotObject.IsInstanceValid(_view)) return;
        var redraw = glided;
        while (!_busy && _queue.Count > 0)
        foreach (var e in _queue.Take(_s.Settings.Animate))
        {
            Played?.Invoke(e);
            // The player's own drops are already on the table where they put them: consume the holds, and glide only the rest.
            var relocations = AnimationQueue.Relocations(e);
            var others = relocations.Where(r => !_input.ConsumeOwn(r)).ToList();
            if (others.Count != relocations.Count) redraw = true;
            if (!_s.Settings.Animate) continue;
            var glides = others.Where(r => r.To is OnTable).Select(r => (Relocation: r, Node: _view.PieceAt(r.ModelId)!))
                .Where(g => g.Node is not null && g.Node.IsInsideTree()).ToList();
            if (glides.Count == 0) continue;
            if (redraw) { _view.Redraw(); redraw = false; }   // first, so it does not put the gliding pieces back at their destinations
            // Every model the event moved glides at once; the next event waits for the last of them.
            _busy = true; var generation = _generation; var left = glides.Count;
            foreach (var (relocation, node) in glides)
            {
                // The table has already redrawn the piece at its destination, so the glide starts from where it was.
                if (relocation.From is OnTable from) node.Position = WorldFrame.ToGodot(from.Placement.Position);
                var tween = node.CreateTween(); _tweens.Add(tween); tween.TweenProperty(node, "position", WorldFrame.ToGodot(((OnTable)relocation.To).Placement.Position), 0.4);
                tween.TweenCallback(Callable.From(() => { if (--left == 0) Done(generation); }));
            }
            _view.GetTree().CreateTimer(0.6).Timeout += () => Done(generation);
            return;   // one event at a time while animating
        }
        if (redraw) _view.Redraw();
    }
}
