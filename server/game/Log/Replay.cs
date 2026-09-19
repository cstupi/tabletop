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

    /// <summary>0, or the seq of a live (not undone, not an Undone marker) event. The server refuses anything else (contracts plan → obligations on this plan).</summary>
    public static bool IsValidUndoTarget(EventApplier applier, long toSeq) =>
        toSeq == 0 || EventApplier.EffectiveLog(applier.Log).Any(e => e.Seq == toSeq);
}
