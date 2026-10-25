using Tabletop.GameServer.Interpreter;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public static partial class GfMoves
{
    /// <summary>
    /// p.9 CHARGE MOVES: "Charging models must move by up to 12” to get into base contact with an enemy model from the target unit, or as close as
    /// possible, whilst still maintaining unit coherency". The region's ContactWith is the target's models. When the charging unit could have
    /// reached base contact in coherency (ChargeFormation: one model on a path to contact, Movement.ContactDistance as GfMelee.ChargeTargets reads
    /// "a clear path to reach it", and the rest formed up on it, every move judged as the other breaches judge it), an answer, a pass included, in
    /// which no charging model ends within BaseContactSlack of a target model breaches it. When it could not (p.7: "if being within charge distance
    /// becomes impossible"), "as close as possible" is not judged. A stop (Destinations.Continues) is not the end of the move, and a disembark has
    /// no start on the table to measure from; neither is judged.
    /// </summary>
    public static AnswerBreachFound? ChargeContactBreach(GameState state, MoveRegion region, Destinations? answer)
    {
        if (region.ContactWith is not { Count: > 0 } contact || answer is { Continues: true } || region.Containment?.ExitingId is not null) return null;
        var p = state.Projection;
        var targets = contact.Where(p.Models.ContainsKey).Select(id => Battlefield.Placed(state, id)).OfType<PlacedModel>().ToList();
        var chargers = region.ModelIds.Where(p.Models.ContainsKey).Select(id => (Id: id, Start: Battlefield.Placed(state, id))).Where(c => c.Start is not null)
            .Select(c => (c.Id, Start: c.Start!.Value)).ToList();
        if (targets.Count == 0 || chargers.Count == 0) return null;
        var landed = (answer is null ? [] : GfAnswers.Moves(state, region, answer)).Where(m => m.To is OnTable).DistinctBy(m => m.ModelId)
            .ToDictionary(m => m.ModelId, m => Battlefield.At(state, m.ModelId, Battlefield.Settle(state, m.ModelId, ((OnTable)m.To).Placement)));
        if (chargers.Any(c => targets.Any(t => Distance.EdgeToEdge(landed.GetValueOrDefault(c.Id, c.Start), t) <= GfNames.BaseContactSlack))) return null;
        if (ChargeFormation(state, region, chargers, targets) is null) return null;
        var name = p.Units.GetValueOrDefault(p.Models[contact.First(p.Models.ContainsKey)].UnitId)?.Definition.Name ?? "the target";
        return new(GfNames.ChargeContactRule, $"no charging model reached base contact with {name}: charging models must move to get into base contact with an enemy model from the target unit (p.9)");
    }

    /// <summary>
    /// The charging unit in base contact and in coherency (UnitFormation.InContact), its moves judged against the Ask's own parameters: the listed
    /// impassable and difficult volumes and the cap, the models standing (none to go round when the region PassesModels), the impassable landing
    /// (none for an Aircraft), the table and p.7's coherency with p.11's 3" at different elevation. Null when none is found.
    /// </summary>
    static IReadOnlyDictionary<Guid, Point2>? ChargeFormation(GameState state, MoveRegion region, IReadOnlyList<(Guid Id, PlacedModel Start)> chargers,
        IReadOnlyList<PlacedModel> targets)
    {
        var p = state.Projection;
        var terrain = Battlefield.Terrain(state);
        var moving = region.ModelIds.ToHashSet();
        var standing = p.Models.Values.Where(x => !moving.Contains(x.Id) && x.Location is OnTable).Select(x => (x.UnitId, At: Battlefield.Placed(state, x.Id)!.Value)).ToList();
        var units = chargers.Select(c => p.Models[c.Id].UnitId).ToHashSet();
        var terms = new FormationTerms(region.Allowance, terrain.Where(v => region.ImpassableVolumeIds.Contains(v.Id)).ToList(),
            terrain.Where(v => region.DifficultVolumeIds.Contains(v.Id)).ToList(), region.DifficultCap,
            region.IgnoresTerrain ? [] : Declared(terrain, TerrainDeclarations.Impassable), terrain, state.Board.Table.Size, standing.Select(s => s.At).ToList(),
            region.PassesModels, GfNames.CoherencyChain, GfNames.CoherencyElevated, GfNames.CoherencySpread);
        return UnitFormation.InContact(chargers, standing.Where(s => units.Contains(s.UnitId)).Select(s => s.At).ToList(), targets, terms);
    }
}
