using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>Dispatch's answer judge (IGameRules.AnswerBreach): the move rules a MoveRegion answer can break that set membership cannot see.</summary>
public sealed partial class GrimdarkFutureRules
{
    public AnswerBreachFound? AnswerBreach(GameState state, Ask ask, AskAnswer answer) =>
        ask.Legal is MoveRegion region && answer is Destinations moves
            ? GfMoves.MinimumBreach(state, region, moves) ?? GfMoves.DifficultCapBreach(state, ask, region, moves)
            : null;
}
