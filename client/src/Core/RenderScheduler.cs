namespace Tabletop.Client.Core;

/// <summary>Which Session changes a coalesced render covers; a part re-renders when any change it reads is set.</summary>
[Flags]
public enum RenderCause { None = 0, Events = 1, Game = 2, Board = 4, Facts = 8 }

/// <summary>
/// One render per dispatcher turn for the parts that redraw from Session's current state (performance review → finding 4).
/// Session marks each EventsApplied, GameChanged, BoardChanged and FactsChanged; the first mark posts one Flush through the
/// dispatcher, and every mark before it runs folds into it. On Godot the post is a deferred call, so the batches already
/// queued for this frame land first and the parts render once, in subscription order, before the frame ends.
/// </summary>
public sealed class RenderScheduler(IDispatcher dispatcher)
{
    RenderCause _pending;

    /// <summary>Raised once per flush with every cause marked since the last one.</summary>
    public event Action<RenderCause>? Render;

    public void Mark(RenderCause cause)
    {
        if (cause == RenderCause.None) return;
        var idle = _pending == RenderCause.None;
        _pending |= cause;
        if (idle) dispatcher.Post(Flush);
    }

    void Flush()
    {
        var causes = _pending;
        _pending = RenderCause.None;    // a mark raised by a render posts its own flush
        if (causes != RenderCause.None) Render?.Invoke(causes);
    }
}
