using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// The Undo move button and Ctrl+Z (in-game UX spec → Moves): an Undo to the live event before my last hand move, offered until
/// any later event lands, since an undo to that point would take the later event with it. The target is the last event of the
/// effective log below the move (0 when none): the server accepts only a live seq, and an Undone marker or an undone event is not one.
/// </summary>
public sealed class HandMoveUndo
{
    long? _moved;
    long _to;

    public long? UndoTo => _moved is null ? null : _to;
    public IReadOnlyList<Guid> Models { get; private set; } = [];

    public void OnOwnMove(IReadOnlyList<GameEvent> log, long movedSeq, IReadOnlyList<Guid> models)
    {
        _moved = movedSeq;
        _to = EventApplier.EffectiveLog(log).LastOrDefault(e => e.Seq < movedSeq)?.Seq ?? 0;
        Models = models.ToList();
    }

    public void OnEvent(long seq)
    {
        if (_moved is { } moved && seq > moved) { _moved = null; Models = []; }
    }
}
