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
    /// after the answer (read as the steps apply it, GfAnswers.Moves): the ones it moves where they land, the rest where they stand. A unit in
    /// coherency before the answer must be in it after; a unit with no model on the table before (a disembark, a reserve placed from the shelf)
    /// counts as in coherency. A unit out of coherency before (casualties, a hand move) is held to p.7's "If a model is not in coherency with its
    /// unit at the beginning of its activation, then you must use an action so that the model gets back into coherency": when this move can bring
    /// it back (UnitFormation.Regroup finds a landing for each model outside the chain) it must end in coherency; when it cannot ("or as close as
    /// possible") and a model can end nearer the chain (UnitFormation.CanClose), the unit must end nearer coherency than it began
    /// (Coherency.Shortfall). A charge is not held to this: its landings are ChargeContactBreach's. A stop part-way (Destinations.Continues) is
    /// not the end of the move and is not judged.
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
            var after = members.Select(id => moves.TryGetValue(id, out var m)
                ? m.To is OnTable to ? Battlefield.At(state, id, Battlefield.Settle(state, id, to.Placement)) : (PlacedModel?)null
                : Battlefield.Placed(state, id)).OfType<PlacedModel>().ToList();
            if (Coherent(after)) continue;
            var name = p.Units.GetValueOrDefault(unit)?.Definition.Name ?? "the unit";
            if (!Coherent(before))
            {
                if (region.ContactWith is { Count: > 0 } || before.Count == 0) continue;
                var (movers, held, terms) = Regrouping(state, region, unit);
                if (UnitFormation.Regroup(movers, held, terms) is not null)
                    return new(GfNames.MoveCoherencyRule, $"{name} is out of coherency and this move can bring it back: it must end in coherency");
                if (UnitFormation.CanClose(movers, held, terms) && Shortfall(after) >= Shortfall(before) - 1e-6)
                    return new(GfNames.MoveCoherencyRule, $"{name} is out of coherency: its models must end this move closer together");
                continue;
            }
            return new(GfNames.MoveCoherencyRule, $"{name} would end out of coherency: all models in a unit must stay within {GfNames.CoherencyChain:0.#}\" of at least one other model and within {GfNames.CoherencySpread:0.#}\" of all other models");
        }
        return null;
    }

    static double Shortfall(IReadOnlyList<PlacedModel> models) =>
        Coherency.Shortfall(models, GfNames.CoherencyChain, GfNames.CoherencyElevated, GfNames.CoherencySpread);

    /// <summary>The unit's models the region moves (where they stand), its models it does not, and the move's terms as ChargeFormation reads them
    /// off the Ask: the listed impassable and difficult volumes and the cap, the other models standing, the impassable landing, the table.</summary>
    static (IReadOnlyList<(Guid Id, PlacedModel Start)> Movers, IReadOnlyList<PlacedModel> Held, FormationTerms Terms) Regrouping(GameState state, MoveRegion region, Guid unit)
    {
        var p = state.Projection;
        var terrain = Battlefield.Terrain(state);
        var moving = region.ModelIds.ToHashSet();
        var movers = region.ModelIds.Where(id => p.Models.TryGetValue(id, out var m) && m.UnitId == unit && Battlefield.Placed(state, id) is not null)
            .Select(id => (id, Battlefield.Placed(state, id)!.Value)).ToList();
        var standing = p.Models.Values.Where(x => !moving.Contains(x.Id) && x.Location is OnTable).Select(x => (x.UnitId, At: Battlefield.Placed(state, x.Id)!.Value)).ToList();
        var terms = new FormationTerms(region.Allowance, terrain.Where(v => region.ImpassableVolumeIds.Contains(v.Id)).ToList(),
            terrain.Where(v => region.DifficultVolumeIds.Contains(v.Id)).ToList(), region.DifficultCap,
            region.IgnoresTerrain ? [] : Declared(terrain, TerrainDeclarations.Impassable), terrain, state.Board.Table.Size, standing.Select(s => s.At).ToList(),
            region.PassesModels, GfNames.CoherencyChain, GfNames.CoherencyElevated, GfNames.CoherencySpread,
            region.SteepVolumeIds is { Count: > 0 } steep ? terrain.Where(v => steep.Contains(v.Id)).ToList() : null,
            standing.Where(s => s.UnitId != unit).Select(s => s.At).ToList(), region.Separation ?? 0);
        return (movers, standing.Where(s => s.UnitId == unit).Select(s => s.At).ToList(), terms);
    }

    static bool Coherent(IReadOnlyList<PlacedModel> models) =>
        Coherency.Check(models, GfNames.CoherencyChain, GfNames.CoherencyElevated, GfNames.CoherencySpread).Coherent;
}
