using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>Between the projection and the renderer. The projection has already applied everything; this only paces what the table plays out.</summary>
public sealed class AnimationQueue
{
    readonly Queue<GameEvent> _pending = new();
    public int Count => _pending.Count;
    public void Enqueue(IReadOnlyList<GameEvent> events) { foreach (var e in events) _pending.Enqueue(e); }

    /// <summary>Not animating: everything, at once. Animating: one event per call, so the table plays them out in order.</summary>
    public IReadOnlyList<GameEvent> Take(bool animate)
    {
        if (!animate) { var all = _pending.ToList(); _pending.Clear(); return all; }
        return _pending.Count == 0 ? [] : [_pending.Dequeue()];
    }
}
