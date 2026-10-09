namespace Tabletop.Client.Core;

/// <summary>
/// The Undo move button and Ctrl+Z (in-game UX spec → Moves): an Undo to the event before my last hand move, offered until any
/// later event lands, since an undo to that point would take the later event with it.
/// </summary>
public sealed class HandMoveUndo
{
    long? _moved;

    public long? UndoTo => _moved - 1;
    public IReadOnlyList<Guid> Models { get; private set; } = [];

    public void OnOwnMove(long movedSeq, IReadOnlyList<Guid> models) { _moved = movedSeq; Models = models.ToList(); }

    public void OnEvent(long seq)
    {
        if (_moved is { } moved && seq > moved) { _moved = null; Models = []; }
    }
}
