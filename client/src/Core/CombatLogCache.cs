using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// The combat log of one log state, built at most once and shared by its readers: the log panel (Entries), the dice tray
/// (Latest) and the undo button (Undo). Undo plans as if a pending undo's marker had landed, so each press steps back one
/// more; with no undo pending it reuses Entries, otherwise it builds over the log with that marker.
/// </summary>
public sealed class CombatLogState
{
    readonly IReadOnlyList<GameEvent> _log; readonly long? _pendingUndoTo;
    IReadOnlyList<LogEntry>? _entries; RollEntry? _latest; bool _latestDone; UndoPlan? _undo; bool _undoDone;

    internal CombatLogState(IReadOnlyList<GameEvent> log, long? pendingUndoTo, Names names) { _log = log; _pendingUndoTo = pendingUndoTo; Names = names; }

    public Names Names { get; }
    public IReadOnlyList<LogEntry> Entries => _entries ??= CombatLog.Build(_log, Names);

    public RollEntry? Latest
    {
        get { if (!_latestDone) { _latest = CombatLog.Latest(Entries); _latestDone = true; } return _latest; }
    }

    public UndoPlan? Undo
    {
        get
        {
            if (_undoDone) return _undo;
            if (_pendingUndoTo is not { } to) _undo = UndoPlan.Of(_log, Names, () => Entries);
            else
            {
                IReadOnlyList<GameEvent> pending = [.. _log, new GameEvent((_log.Count == 0 ? 0 : _log[^1].Seq) + 1, null, new Undone(to), null)];
                _undo = UndoPlan.Of(pending, Names);
            }
            _undoDone = true;
            return _undo;
        }
    }
}

/// <summary>Hands every reader the same CombatLogState until the log state changes: another log, another length or last seq,
/// another pending undo, another game record or another projection instance (a prediction, a rebuild after an Undone).</summary>
public sealed class CombatLogCache
{
    (IReadOnlyList<GameEvent> Log, int Count, long LastSeq, long? Pending, GameInfo Game, Projection? Projection, Guid? Me)? _key;
    CombatLogState? _state;

    /// <summary><paramref name="me"/>: the reader's participant row, so an edit of my own does not name me (<see cref="Names.Other"/>).</summary>
    public CombatLogState Of(IReadOnlyList<GameEvent> log, long? pendingUndoTo, GameInfo game, Projection? projection, Guid? me = null)
    {
        var lastSeq = log.Count == 0 ? 0 : log[^1].Seq;
        if (_state is not null && _key is { } k && ReferenceEquals(k.Log, log) && k.Count == log.Count && k.LastSeq == lastSeq
            && k.Pending == pendingUndoTo && ReferenceEquals(k.Game, game) && ReferenceEquals(k.Projection, projection) && k.Me == me)
            return _state;
        _key = (log, log.Count, lastSeq, pendingUndoTo, game, projection, me);
        return _state = new CombatLogState(log, pendingUndoTo, new Names(game, projection, me));
    }
}
