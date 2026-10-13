using Tabletop.Protocol;

namespace Tabletop.GameServer.Log;

/// <summary>Undo by replay: the initial state is the setup snapshot (Board), and the applier does the rest.</summary>
public static class Replay
{
    public static Projection To(Board board, GameInfo game, IReadOnlyList<GameEvent> log, long seq)
    {
        var applier = new EventApplier(board, game);
        applier.ApplyAll(log.Where(e => e.Seq <= seq));
        return applier.Projection;
    }

    /// <summary>0, or the seq of a live (not undone, not an Undone marker) event that the next live event is not an AskClosed after: the
    /// answer that closes an Ask and the close are one command, and between them the Ask stays answered but open with nothing to
    /// reopen it. The server refuses anything else (contracts plan → obligations on this plan).</summary>
    public static bool IsValidUndoTarget(EventApplier applier, long toSeq)
    {
        if (toSeq == 0) return true;
        var live = EventApplier.EffectiveLog(applier.Log);
        var at = live.ToList().FindIndex(e => e.Seq == toSeq);
        return at >= 0 && (at + 1 == live.Count || live[at + 1].Payload is not AskClosed);
    }
}
