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
    readonly IReadOnlyList<WorldVolume> blockers;
    readonly HashSet<Guid> bodies;

    Sight(GameState state, IReadOnlyList<WorldVolume> blockers, HashSet<Guid> bodies) => (this.state, this.blockers, this.bodies) = (state, blockers, bodies);

    /// <summary>`alsoIgnoring` leaves a second unit's bodies out as well, for a question that must not count that unit as a blocker.</summary>
    public static Sight From(GameState state, Guid observerUnit, Guid? alsoIgnoring = null)
    {
        var terrain = Battlefield.Terrain(state).Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.BlocksLineOfSight));
        var models = state.Projection.Models.Values.Where(m => m.UnitId != observerUnit && m.UnitId != alsoIgnoring && m.Location is OnTable).ToList();
        var volumes = terrain.Concat(models.Select(m => Bodies.AsBlockingVolume(m.Id, Battlefield.Placed(state, m.Id)!.Value))).ToList();
        return new(state, volumes, models.Select(m => m.Id).ToHashSet());
    }

    public bool CanSee(Guid fromModel, Guid toModel) => Look(fromModel, toModel) is { Clear: true };

    /// <summary>
    /// One sight line measured: `Clear` is `LineOfSight.Check`, and `Screened` is `Cover.SightBlockersBetween` non-empty along a
    /// clear line, so it is false whenever `Clear` is. The target's own body is never its own blocker. Null when either model is off the table.
    /// </summary>
    public (bool Clear, bool Screened)? Look(Guid fromModel, Guid toModel)
    {
        var (from, to) = (Battlefield.Placed(state, fromModel), Battlefield.Placed(state, toModel));
        if (from is null || to is null) return null;
        var volumes = bodies.Contains(toModel) ? blockers.Where(v => v.Id != toModel).ToList() : blockers;
        if (!LineOfSight.Check(from.Value, to.Value, volumes).Clear) return (false, false);
        return (true, Cover.SightBlockersBetween(from.Value, to.Value, volumes).Count > 0);
    }
}
