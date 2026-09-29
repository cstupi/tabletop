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

    public static IReadOnlyList<WorldVolume> Terrain(GameState state) => state.Board.Table.Terrain.SelectMany(t => t.WorldVolumes()).ToList();

    /// <summary>Terrain plus every model not in the given unit, as sight blockers (p.8: a unit ignores its own models; p.11: other units block).</summary>
    public static IReadOnlyList<WorldVolume> Blockers(GameState state, Guid exceptUnitId) =>
        Terrain(state).Concat(state.Projection.Models.Values.Where(m => m.UnitId != exceptUnitId && m.Location is OnTable)
            .Select(m => Bodies.AsBlockingVolume(m.Id, Placed(state, m.Id)!.Value))).ToList();

    public static bool CanSee(GameState state, Guid fromModel, Guid toModel)
    {
        var (from, to) = (Placed(state, fromModel), Placed(state, toModel));
        if (from is null || to is null) return false;
        var blockers = Blockers(state, state.Projection.Models[fromModel].UnitId).Where(v => v.Id != toModel && v.Id != fromModel).ToList();
        return LineOfSight.Check(from.Value, to.Value, blockers).Clear;
    }

    public static double Distance(GameState state, Guid a, Guid b) => Geometry.Distance.EdgeToEdge(Placed(state, a)!.Value, Placed(state, b)!.Value);
    public static double Vertical(GameState state, Guid a, Guid b) => Geometry.Distance.Vertical(Placed(state, a)!.Value, Placed(state, b)!.Value);

    public static double? UnitDistance(GameState state, Guid unitA, Guid unitB)
    {
        var pairs = (from a in Effects.Lookup.LivingModels(state, unitA) from b in Effects.Lookup.LivingModels(state, unitB) select Distance(state, a, b)).ToList();
        return pairs.Count > 0 ? pairs.Min() : null;
    }

    /// <summary>
    /// p.11's terrain half, counted and not judged: living models whose base centre is inside a volume declared `Cover`. The test
    /// is the base centre inside the volume, so this is `Containment.VolumesContaining` rather than a whole-base test, and it reads
    /// the same way for a single-model unit as for a squad. What fraction of the unit has to be in cover is the ruleset's number and
    /// lives in `GfRolls.InCover`.
    /// </summary>
    public static int ModelsInCoverTerrain(GameState state, Guid unitId)
    {
        var terrain = Terrain(state);
        return Effects.Lookup.LivingModels(state, unitId).Count(id =>
            Containment.VolumesContaining(Placed(state, id)!.Value, terrain).Any(v => terrain.First(t => t.Id == v).Volume.Declarations.HasFlag(TerrainDeclarations.Cover)));
    }

    /// <summary>
    /// p.11's sight half, counted and not judged: how many of the target unit's living models at least one of these models can
    /// see. `LineOfSight.VisibleModels` takes every observer, so the attacking unit sees a model when any of its models does, and
    /// no single vantage point has to be chosen. It is called once per target model rather than once for the unit, because the
    /// blockers must lose the target's own body and keep the unit's other models (p.11); the ray count is the same either way.
    /// </summary>
    public static int ModelsVisibleFrom(GameState state, Guid targetUnit, IReadOnlyList<Guid> fromModels)
    {
        var observers = fromModels.Select(m => Placed(state, m)).Where(p => p is not null).Select(p => p!.Value).ToList();
        if (observers.Count == 0) return 0;
        var blockers = Blockers(state, state.Projection.Models[fromModels[0]].UnitId);
        return Effects.Lookup.LivingModels(state, targetUnit)
            .Count(id => LineOfSight.VisibleModels(observers, [Placed(state, id)!.Value], blockers.Where(v => v.Id != id).ToList())[0]);
    }

    public static IReadOnlyList<string> TerrainUnder(GameState state, Guid unitId)
    {
        var terrain = Terrain(state);
        var flags = Effects.Lookup.LivingModels(state, unitId).SelectMany(id => Containment.VolumesContaining(Placed(state, id)!.Value, terrain)).Distinct()
            .Select(id => terrain.First(v => v.Id == id).Volume.Declarations).Aggregate(TerrainDeclarations.None, (acc, d) => acc | d);
        return Enum.GetValues<TerrainDeclarations>().Where(f => f != TerrainDeclarations.None && flags.HasFlag(f)).Select(f => Tabletop.Content.ContentJson.Serialize(f).Trim('"')).ToList();
    }

    public static Placement Settle(GameState state, Placement client) =>
        new(client.Position with { Z = Settling.SurfaceHeightAt(client.Position, Terrain(state)) }, client.Yaw);
}
