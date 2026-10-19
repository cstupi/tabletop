using Tabletop.Content;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Setup;
using Tabletop.Geometry;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>A wall turned in setup stands where its turned footprint is, for sight and for resting, on the server.</summary>
public class TurnedTerrainTests
{
    /// <summary>A 1" by 10" wall, 3" tall, blocking sight, at (36, 24): unturned x 35.5..36.5, y 19..29; turned 90° x 31..41, y 23.5..24.5.</summary>
    static readonly TerrainPlacement Wall = new(Guid.CreateVersion7(), VisualRef.OfBuiltin("wall-or-fence"), new Point3(36, 24, 0), new Yaw(0), 1,
        [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Rect(1, 10), new Point2(0, 0), new Yaw(0), 0, 3, TerrainDeclarations.BlocksLineOfSight, null))]);

    static IReadOnlyList<TerrainPlacement> Turned(double yaw)
    {
        var draft = new TableState(Guid.CreateVersion7(), null, new TableSize(72, 48), VisualRef.OfBuiltin("grass"), [Wall], [], []);
        var turned = DraftEditor.Apply(draft, new MoveTerrain(Wall.Id, Wall.Position, new Yaw(yaw)), out var missing);
        Assert.Null(missing);
        return turned!.Terrain;
    }

    /// <summary>An observer at (33, 20) and a target at (33, 28): the line between crosses the turned wall and passes clear of the unturned one.</summary>
    static (GameState State, Guid Observer, Guid Target) Table(double yaw)
    {
        var (observer, target) = (Unit("Observer", "4+", "4+", [], 1, []), Unit("Target", "4+", "4+", [], 1, []));
        var state = State([observer], [target], terrain: Turned(yaw));
        ModelRelocation Put(Unit u, double y) => new(u.Models[0].Id, state.Projection.Models[u.Models[0].Id].Location, new OnTable(new Placement(new Point3(33, y, 0), new Yaw(0))));
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new ModelsMoved([Put(observer, 20), Put(target, 28)]), null));
        return (state, observer.Models[0].Id, target.Models[0].Id);
    }

    [Fact]
    public void The_draft_keeps_the_turn()
    {
        Assert.Equal(new Yaw(90), Turned(90).Single().Yaw);
    }

    [Fact]
    public void Sight_is_blocked_by_the_turned_footprint_only()
    {
        var (unturned, o0, t0) = Table(0);
        Assert.True(Battlefield.CanSee(unturned, o0, t0));
        var (turned, o1, t1) = Table(90);
        Assert.False(Battlefield.CanSee(turned, o1, t1));
    }

    [Fact]
    public void A_model_rests_on_the_turned_footprint_only()
    {
        var (unturned, o0, _) = Table(0);
        Assert.Equal(0, Battlefield.Settle(unturned, o0, new Placement(new Point3(40, 24, 0), new Yaw(0))).Position.Z);
        var (turned, o1, _) = Table(90);
        Assert.Equal(3, Battlefield.Settle(turned, o1, new Placement(new Point3(40, 24, 0), new Yaw(0))).Position.Z);
    }
    [Fact]
    public void The_world_volumes_are_built_once_per_table()
    {
        var (state, _, _) = Table(90);
        var volumes = Battlefield.Terrain(state);
        Assert.Same(volumes, Battlefield.Terrain(state));
        Assert.Equal(state.Board.Table.Terrain.SelectMany(t => t.WorldVolumes()), volumes);
        Assert.NotSame(volumes, Battlefield.Terrain(Table(0).State));
    }
}
