using Tabletop.GameServer.Rules;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Interpreter;

/// <summary>
/// Sight lines drawn from one unit's models, with the blocking volumes built once: the terrain that declares `BlocksLineOfSight`
/// and the body of every model on the table outside the observing unit (p.8: a unit ignores its own models; p.11: other units
/// block). A caller that checks many pairs builds one `Sight` and asks it each pair, so no pair rebuilds the table.
/// </summary>
public sealed class Sight
{
    readonly GameState state;
    readonly SightBlockers blockers;
    readonly HashSet<Guid> bodies;

    Sight(GameState state, SightBlockers blockers, HashSet<Guid> bodies) => (this.state, this.blockers, this.bodies) = (state, blockers, bodies);

    /// <summary>`alsoIgnoring` leaves a second unit's bodies out as well, for a question that must not count that unit as a blocker.</summary>
    public static Sight From(GameState state, Guid observerUnit, Guid? alsoIgnoring = null)
    {
        var terrain = Battlefield.Terrain(state).Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.BlocksLineOfSight));
        var models = state.Projection.Models.Values.Where(m => m.UnitId != observerUnit && m.UnitId != alsoIgnoring && m.Location is OnTable).ToList();
        var volumes = terrain.Concat(models.Select(m => Bodies.AsBlockingVolume(m.Id, Battlefield.Placed(state, m.Id)!.Value))).ToList();
        return new(state, new SightBlockers(volumes), models.Select(m => m.Id).ToHashSet());
    }

    /// <summary>`Look`'s Clear, drawing lines only until one is clear.</summary>
    public bool CanSee(Guid fromModel, Guid toModel)
    {
        var (from, to) = (Battlefield.Placed(state, fromModel), Battlefield.Placed(state, toModel));
        return from is not null && to is not null && blockers.Check(from.Value, to.Value, Ignored(toModel)).Clear;
    }

    /// <summary>
    /// One sight line measured: `Clear` is `LineOfSight.Check`, and `Screened` is `Cover.SightBlockersBetween` non-empty along a
    /// clear line, so it is false whenever `Clear` is. The target's own body is never its own blocker. Null when either model is off the table.
    /// </summary>
    public (bool Clear, bool Screened)? Look(Guid fromModel, Guid toModel) => Line(fromModel, toModel) is { } line ? (line.Clear, line.Screened) : null;

    /// <summary>
    /// The same measurement as a wire SightLine (in-game UX spec → Sight question): BlockedBy is LineOfSightResult.ObstructedBy, the
    /// terrain volume ids and model ids in the way, empty on a clear line. Null when either model is off the table.
    /// </summary>
    public SightLine? Line(Guid fromModel, Guid toModel)
    {
        var (from, to) = (Battlefield.Placed(state, fromModel), Battlefield.Placed(state, toModel));
        if (from is null || to is null) return null;
        var reading = blockers.Measure(from.Value, to.Value, Ignored(toModel));
        return reading.Clear
            ? new SightLine(fromModel, toModel, true, reading.Blockers.Count > 0, [])
            : new SightLine(fromModel, toModel, false, false, reading.Blockers);
    }

    Guid? Ignored(Guid toModel) => bodies.Contains(toModel) ? toModel : null;
}
