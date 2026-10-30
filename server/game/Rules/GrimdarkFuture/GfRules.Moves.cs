using Tabletop.GameServer.Effects;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>Dispatch's answer judge (IGameRules.AnswerBreach): the move rules a MoveRegion answer can break that set membership cannot see; and its
/// hand-move judge (IGameRules.PlacementBreach).</summary>
public sealed partial class GrimdarkFutureRules
{
    public AnswerBreachFound? AnswerBreach(GameState state, Ask ask, AskAnswer answer) => ask.Legal switch
    {
        MoveRegion region when answer is Destinations moves =>
            GfMoves.NotOfferedBreach(state, region, moves) ?? GfMoves.PathBreach(state, region, moves)
            ?? GfMoves.MinimumBreach(state, region, moves) ?? GfMoves.DifficultCapBreach(state, ask, region, moves)
            ?? GfMoves.ContactBreach(state, region, moves) ?? GfMoves.CoherencyBreach(state, region, moves) ?? GfMoves.ChargeContactBreach(state, region, moves),
        MoveRegion region when answer is Passed => GfMoves.ChargeContactBreach(state, region, null),
        _ => PassBreach(ask, answer) ?? DeployInsideBreach(state, ask, answer),
    };

    /// <summary>
    /// p.15: "units inside Transports are deployed at the same time as the Transport". A deploy Ask lists units with every model on the shelf; one
    /// opened before a unit was put in a Transport still lists it, and naming it would spend the turn placing nothing.
    /// </summary>
    static AnswerBreachFound? DeployInsideBreach(GameState state, Ask ask, AskAnswer answer)
    {
        if (ask.FrameId is not null || ask.Question is not (AskQuestions.Deploy or AskQuestions.DeployScouts or AskQuestions.DeployAmbusher)) return null;
        if (answer is not SelectedPieces { PieceIds: var ids }) return null;
        foreach (var id in ids)
            if (state.Projection.Units.TryGetValue(id, out var unit) && Lookup.ContainerOf(state, id) is { } c && state.Projection.Models.TryGetValue(c, out var holder))
                return new(GfNames.DeployInsideRule, $"{unit.Definition.Name} is inside {state.Projection.Units[holder.UnitId].Definition.Name} and deploys with it (p.15)");
        return null;
    }

    public AnswerBreachFound? PlacementBreach(GameState state, IReadOnlyList<ModelRelocation> moves) => GfMoves.OverlapBreach(state, moves) ?? ZoneBreach(state, moves) ?? ScoutBreach(state, moves) ?? AmbushBreach(state, moves) ?? GfReserves.EdgeBreach(state, moves);
}
