using Tabletop.GameServer.Rules;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Interpreter;

/// <summary>
/// What the ruleset's optional rules make of other units as sight blockers (optional-rules spec → Sight takes a policy). Bound to one state.
/// </summary>
public interface ISightPolicy
{
    /// <summary>True: a unit other than the observer's blocks with one perimeter (Bodies.Perimeter) keyed by its id; false: each model with its body.</summary>
    bool Perimeters { get; }
    /// <summary>True when the line from observer to target passes through blockingUnit as if it were not there.</summary>
    bool SeesThrough(Guid observerModel, Guid targetModel, Guid blockingUnit);

    /// <summary>Each model blocks with its body and no unit is seen through: the core game.</summary>
    static ISightPolicy PerModel { get; } = new PerModelPolicy();

    private sealed class PerModelPolicy : ISightPolicy
    {
        public bool Perimeters => false;
        public bool SeesThrough(Guid observerModel, Guid targetModel, Guid blockingUnit) => false;
    }
}

/// <summary>
/// Sight lines drawn from one unit's models, with the blocking volumes built once: the terrain that declares `BlocksLineOfSight`
/// and the body of every model on the table outside the observing unit (p.8: a unit ignores its own models; p.11: other units
/// block), with each other unit's perimeter in place of its bodies when the ruleset's ISightPolicy says so. Per pair the target's
/// own body, its unit's perimeter (its models block per model) and every unit the policy sees through are left out. A caller that
/// checks many pairs builds one `Sight` and asks it each pair, so no pair rebuilds the table.
/// </summary>
public sealed class Sight
{
    readonly GameState state;
    readonly SightBlockers blockers;
    readonly ISightPolicy policy;
    /// <summary>The unit of every model whose body is a blocker.</summary>
    readonly Dictionary<Guid, Guid> bodies;
    /// <summary>The units whose perimeter is a blocker, keyed by their own id.</summary>
    readonly HashSet<Guid> perimeters;

    Sight(GameState state, SightBlockers blockers, ISightPolicy policy, Dictionary<Guid, Guid> bodies, HashSet<Guid> perimeters) =>
        (this.state, this.blockers, this.policy, this.bodies, this.perimeters) = (state, blockers, policy, bodies, perimeters);

    /// <summary>`alsoIgnoring` leaves a second unit out as well (bodies and perimeter), for a question that must not count that unit as a blocker.</summary>
    public static Sight From(GameState state, Guid observerUnit, Guid? alsoIgnoring = null)
    {
        var policy = state.Rules.SightPolicy(state);
        var terrain = Battlefield.Terrain(state).Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.BlocksLineOfSight));
        var models = state.Projection.Models.Values.Where(m => m.UnitId != observerUnit && m.UnitId != alsoIgnoring && m.Location is OnTable).ToList();
        var volumes = terrain.Concat(models.Select(m => Bodies.AsBlockingVolume(m.Id, Battlefield.Placed(state, m.Id)!.Value))).ToList();
        var prisms = policy.Perimeters
            ? models.GroupBy(m => m.UnitId).Select(g => Bodies.Perimeter(g.Key, g.Select(m => Battlefield.Placed(state, m.Id)!.Value).ToList())).ToList()
            : [];
        return new(state, new SightBlockers(volumes, prisms), policy, models.ToDictionary(m => m.Id, m => m.UnitId), prisms.Select(p => p.Id).ToHashSet());
    }

    /// <summary>`Look`'s Clear, drawing lines only until one is clear.</summary>
    public bool CanSee(Guid fromModel, Guid toModel)
    {
        var (from, to) = (Battlefield.Placed(state, fromModel), Battlefield.Placed(state, toModel));
        return from is not null && to is not null && blockers.Check(from.Value, to.Value, Ignored(fromModel, toModel)).Clear;
    }

    /// <summary>
    /// One sight line measured: `Clear` is `LineOfSight.Check`, and `Screened` is `Cover.SightBlockersBetween` non-empty along a
    /// clear line, so it is false whenever `Clear` is. The target's own body is never its own blocker. Null when either model is off the table.
    /// </summary>
    public (bool Clear, bool Screened)? Look(Guid fromModel, Guid toModel) => Line(fromModel, toModel) is { } line ? (line.Clear, line.Screened) : null;

    /// <summary>
    /// The same measurement as a wire SightLine (in-game UX spec → Sight question): BlockedBy is LineOfSightResult.ObstructedBy, the
    /// terrain volume ids, model ids and perimeter unit ids in the way, empty on a clear line. Null when either model is off the table.
    /// </summary>
    public SightLine? Line(Guid fromModel, Guid toModel)
    {
        var (from, to) = (Battlefield.Placed(state, fromModel), Battlefield.Placed(state, toModel));
        if (from is null || to is null) return null;
        var reading = blockers.Measure(from.Value, to.Value, Ignored(fromModel, toModel));
        return reading.Clear
            ? new SightLine(fromModel, toModel, true, reading.Blockers.Count > 0, [])
            : new SightLine(fromModel, toModel, false, false, reading.Blockers);
    }

    /// <summary>The blockers this pair's line leaves out: the target's body, its unit's perimeter, the bodies a perimeter stands in for, and every unit seen through.</summary>
    Func<Guid, bool> Ignored(Guid fromModel, Guid toModel)
    {
        var targetUnit = state.Projection.Models[toModel].UnitId;
        var seen = new Dictionary<Guid, bool>();
        bool Through(Guid unit) => seen.TryGetValue(unit, out var through) ? through : seen[unit] = policy.SeesThrough(fromModel, toModel, unit);
        return id => id == toModel
            || (bodies.TryGetValue(id, out var unit)
                ? (unit != targetUnit && perimeters.Contains(unit)) || Through(unit)
                : perimeters.Contains(id) && (id == targetUnit || Through(id)));
    }
}
