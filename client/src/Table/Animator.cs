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
            if (_s.Settings.Animate && e.Payload is StepCompleted { Rolls.Count: > 0 } step)
            {
                var actor = _s.Projection?.Stack.FirstOrDefault(f => f.Id == step.FrameId)?.Context.ActorUnitId;
                var anchor = actor is { } a ? _view.Pieces.OfType<PieceNode>().FirstOrDefault(p => p.UnitId == a) : null;
                if (anchor is not null && anchor.IsInsideTree())
                {
                    // The whole readout of a roll, which is all the client ever shows of one — no die is drawn:
                    // the faces, which dice passed in brackets, the threshold, and every modifier by source.
                    // No die is placed on the table and nothing tumbles; Godot physics is not in the scene.
                    // One block per roll on the step, stacked, because a step may hold several: OPR's roll-to-hit rolls one
                    // pool per Quality value the attacking models bring, since p.4 tests each attack against "the model's
                    // Quality value" and a joined hero brings its own. The threshold on each block is what tells them apart.
                    var text = string.Join("\n", step.Rolls.Select(roll =>
                    {
                        var faces = string.Join(" ", roll.Dice.Select(d => d.Success ? $"[{d.Raw}]" : $"{d.Raw}"));
                        var mods = roll.Modifiers.Count == 0 ? "" : "\n" + string.Join(", ", roll.Modifiers.Select(m => $"{m.Amount:+#;-#;0} {m.Source}"));
                        return $"{roll.Roll}{(roll.Threshold is { } t ? $" {t}+" : "")}\n{faces}{mods}";
                    }));
                    var label = new Label3D { Text = text, Position = new Vector3(0, 3, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 64, PixelSize = 0.01f };
                    anchor.AddChild(label); anchor.GetTree().CreateTimer(1.5).Timeout += () => { if (GodotObject.IsInstanceValid(label)) label.QueueFree(); };
                }
            }
        }
        _view.Redraw();
    }
}
