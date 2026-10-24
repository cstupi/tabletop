using Tabletop.GameServer.Rules;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Interpreter;

/// <summary>Geometry questions over the projection. Every answer is a measurement; what it means is the ruleset's.</summary>
public static class Battlefield
{
    public static PlacedModel? Placed(GameState state, Guid modelId) =>
        state.Projection.Models[modelId].Location is OnTable t ? At(state, modelId, t.Placement) : null;

    /// <summary>The model's body at a placement it does not hold yet — how a destination is judged before it is applied, and the only way to place an occupant, which has no position of its own.</summary>
    public static PlacedModel At(GameState state, Guid modelId, Placement placement) =>
        new(new ModelBody(state.Projection.Models[modelId].Slot.Base, state.Projection.Models[modelId].Slot.Height), placement);

    /// <summary>
    /// What containment settles about one unit reaching another, before anything is measured (p.15: a unit inside a container and a
    /// unit outside it cannot reach each other; occupants of one container reach each other and the container model itself).
    /// False: one is inside and the other cannot see in or out. True: both are inside the same container, or the reaching unit is
    /// inside and the other owns the container — they are in the same box, so there is no distance and no sight line to take.
    /// Null: neither is inside anything, so measure it the usual way.
    /// </summary>
    public static bool? InsideReach(GameState state, Guid fromUnit, Guid toUnit)
    {
        var (from, to) = (Effects.Lookup.ContainerOf(state, fromUnit), Effects.Lookup.ContainerOf(state, toUnit));
        if (from is null && to is null) return null;
        if (from is not { } container) return false;                                              // outside looking in
        return to == container || state.Projection.Models[container].UnitId == toUnit;
    }

    /// <summary>The table's terrain as world volumes, built once per table: a table is immutable, and an edit in setup makes a new one.</summary>
    public static IReadOnlyList<WorldVolume> Terrain(GameState state) =>
        Volumes.GetValue(state.Board.Table, t => t.Terrain.SelectMany(p => p.WorldVolumes()).ToList().AsReadOnly());
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Tabletop.Content.TableState, IReadOnlyList<WorldVolume>> Volumes = new();

    /// <summary>One pair, one sight line. A caller checking many pairs builds a <see cref="Sight"/> once and asks it instead.</summary>
    public static bool CanSee(GameState state, Guid fromModel, Guid toModel) =>
        Sight.From(state, state.Projection.Models[fromModel].UnitId).CanSee(fromModel, toModel);

    public static double Distance(GameState state, Guid a, Guid b) => Geometry.Distance.EdgeToEdge(Placed(state, a)!.Value, Placed(state, b)!.Value);
    public static double Vertical(GameState state, Guid a, Guid b) => Geometry.Distance.Vertical(Placed(state, a)!.Value, Placed(state, b)!.Value);

    public static double? UnitDistance(GameState state, Guid unitA, Guid unitB)
    {
        var pairs = (from a in Effects.Lookup.LivingModels(state, unitA) from b in Effects.Lookup.LivingModels(state, unitB) select Distance(state, a, b)).ToList();
        return pairs.Count > 0 ? pairs.Min() : null;
    }

    /// <summary>
    /// p.11's terrain test, per model and not judged: the living models inside a volume declared `Cover`. Which test a model has to
    /// pass is the ruleset's call, because the book words it differently by unit size: `wholeBase` takes models whose whole base is
    /// inside (`Cover.InsideCoverVolumes`, the multi-model wording "fully inside"), otherwise a model is taken when its base centre is
    /// inside (`Containment.VolumesContaining`, standing for the single-model wording "mostly inside"). The majority is the ruleset's.
    /// </summary>
    public static IReadOnlyList<Guid> ModelsInCoverTerrain(GameState state, Guid unitId, bool wholeBase)
    {
        var terrain = Terrain(state);
        return Effects.Lookup.LivingModels(state, unitId).Where(id =>
            wholeBase
                ? Cover.InsideCoverVolumes(Placed(state, id)!.Value, terrain).Count > 0
                : Containment.VolumesContaining(Placed(state, id)!.Value, terrain).Any(v => terrain.First(t => t.Id == v).Volume.Declarations.HasFlag(TerrainDeclarations.Cover))).ToList();
    }

    /// <summary>
    /// p.11's sight test, per model and not judged: the target unit's living models that are "behind a sight blocker" from these
    /// attacking models. A model is when every attacking model with a clear line to it has a blocker along that line
    /// (`Cover.SightBlockersBetween` non-empty while `LineOfSight.Check` is clear), which includes no attacking model seeing it at all.
    /// The blockers are terrain and every unit but the two: p.11's other units "are sight blockers that can provide other units
    /// with cover", so a model does not take cover behind its own unit, and the attackers ignore their own (p.8).
    /// </summary>
    public static IReadOnlyList<Guid> ModelsBehindSightBlockers(GameState state, Guid targetUnit, IReadOnlyList<Guid> fromModels)
    {
        var living = Effects.Lookup.LivingModels(state, targetUnit);
        if (fromModels.Count == 0) return living;
        var sight = Sight.From(state, state.Projection.Models[fromModels[0]].UnitId, alsoIgnoring: targetUnit);
        return living.Where(id => fromModels.All(o => sight.Look(o, id) is not { Clear: true, Screened: false })).ToList();
    }

    const double NearDistance = 1 + 1e-6;

    public static IReadOnlyList<string> TerrainUnder(GameState state, Guid unitId)
    {
        var terrain = Terrain(state);
        var living = Effects.Lookup.LivingModels(state, unitId);
        var flags = living.SelectMany(id => Containment.VolumesContaining(Placed(state, id)!.Value, terrain)).Distinct()
            .Select(id => terrain.First(v => v.Id == id).Volume.Declarations).Aggregate(TerrainDeclarations.None, (acc, d) => acc | d);
        var names = Enum.GetValues<TerrainDeclarations>().Where(f => f != TerrainDeclarations.None && flags.HasFlag(f)).Select(f => Tabletop.Content.ContentJson.Serialize(f).Trim('"')).ToList();
        // Grounded Reinforcement: "most of them within 1" of terrain"; p.11: a volume declaring no terrain type is open terrain, which no rule affects.
        var declared = terrain.Where(v => v.Volume.Declarations != TerrainDeclarations.None).ToList();
        if (living.Count > 0 && living.Count(id => declared.Any(v => Geometry.Distance.EdgeToEdge(Placed(state, id)!.Value, v) <= NearDistance)) * 2 > living.Count)
            names.Add("near");
        return names;
    }

    /// <summary>
    /// Client UX spec → Physical collision; held pieces lift over spec → Server: rests the model over its whole base on its open surface nearest the
    /// client's Z (Collision.RestNear). A Z of 0 on a hill or a crate gets its top, since 0 is not open there; a model sent at table level under a
    /// bridge with room stays under it.
    /// </summary>
    public static Placement Settle(GameState state, Guid modelId, Placement client) =>
        new(client.Position with { Z = Collision.RestNear(At(state, modelId, client), Terrain(state), client.Position.Z) }, client.Yaw);
}
