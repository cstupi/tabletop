using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>Dispatch's answer judge (IGameRules.AnswerBreach): the move rules a MoveRegion answer can break that set membership cannot see; and its
/// hand-move judge (IGameRules.PlacementBreach).</summary>
public sealed partial class GrimdarkFutureRules
{
    public AnswerBreachFound? AnswerBreach(GameState state, Ask ask, AskAnswer answer) =>
        ask.Legal is MoveRegion region && answer is Destinations moves
            ? GfMoves.NotOfferedBreach(state, region, moves) ?? GfMoves.PathBreach(state, region, moves)
              ?? GfMoves.MinimumBreach(state, region, moves) ?? GfMoves.DifficultCapBreach(state, ask, region, moves)
              ?? GfMoves.ContactBreach(state, region, moves)
            : null;

    public AnswerBreachFound? PlacementBreach(GameState state, IReadOnlyList<ModelRelocation> moves) => GfMoves.OverlapBreach(state, moves);
}
