using Tabletop.GameServer.Interpreter;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static partial class GfMoves
{
    /// <summary>
    /// p.7 UNIT COHERENCY: "All models in a unit must always stay within 1” of at least one other model, and must stay within 9” of all other
    /// models (or as close as possible), forming an uninterrupted chain of models in 1” coherency with each other"; p.11: "a coherency of 3” is
    /// allowed to models at different elevation" (Coherency.Check). Each unit with a model in the region is judged with all its models on the table
    /// after the answer (read as the steps apply it, GfAnswers.Moves): the ones it moves where they land, the rest where they stand. A breach only
    /// when the unit was in coherency before the answer and is not after, so a unit casualties already broke may still move as it can; a unit with
    /// no model on the table before (a disembark, a reserve placed from the shelf) counts as in coherency. A stop part-way (Destinations.Continues)
    /// is not the end of the move and is not judged.
    /// </summary>
    public static AnswerBreachFound? CoherencyBreach(GameState state, MoveRegion region, Destinations answer)
    {
        if (answer.Continues) return null;
        var p = state.Projection;
        var moves = GfAnswers.Moves(state, region, answer).Where(m => p.Models.ContainsKey(m.ModelId)).DistinctBy(m => m.ModelId).ToDictionary(m => m.ModelId);
        foreach (var unit in region.ModelIds.Where(p.Models.ContainsKey).Select(id => p.Models[id].UnitId).Distinct())
        {
            var members = p.Models.Values.Where(x => x.UnitId == unit).Select(x => x.Id).ToList();
            var before = members.Select(id => Battlefield.Placed(state, id)).OfType<PlacedModel>().ToList();
            if (!Coherent(before)) continue;
            var after = members.Select(id => moves.TryGetValue(id, out var m)
                ? m.To is OnTable to ? Battlefield.At(state, id, Battlefield.Settle(state, id, to.Placement)) : (PlacedModel?)null
                : Battlefield.Placed(state, id)).OfType<PlacedModel>().ToList();
            if (Coherent(after)) continue;
            var name = p.Units.GetValueOrDefault(unit)?.Definition.Name ?? "the unit";
            return new(GfNames.MoveCoherencyRule, $"{name} would end out of coherency: all models in a unit must stay within {GfNames.CoherencyChain:0.#}\" of at least one other model and within {GfNames.CoherencySpread:0.#}\" of all other models (p.7)");
        }
        return null;
    }

    static bool Coherent(IReadOnlyList<PlacedModel> models) =>
        Coherency.Check(models, GfNames.CoherencyChain, GfNames.CoherencyElevated, GfNames.CoherencySpread).Coherent;
}
