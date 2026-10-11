using Tabletop.Content;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules;
using Tabletop.Geometry;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>Sight.Line: the pair's line as the rules measure it, with what blocks it.</summary>
public class SightLineTests
{
    /// <summary>
    /// Two models a side, observers at x -5, targets at x 5, each pair along y -18.5 and -17 (Armies.State). A 1"x1", 10" tall wall
    /// centred on (wallX, -18.5). At wallX 0 it stands across the y -18.5 line only: observer 1 to target 1 is blocked; the lines to or
    /// from y -17 cross x 0 at y -17.75 or above, clear of it. At wallX -3.5 it stands in front of observer 1, whose lines to both
    /// targets cross x -3.5 below y -18, while observer 2's cross above y -17.3.
    /// </summary>
    internal static (GameState State, Tabletop.Content.Unit Observers, Tabletop.Content.Unit Targets, Guid Wall) Table(double wallX, params Weapon[] weapons)
    {
        var wallVolume = Guid.CreateVersion7();
        var wall = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("wall"), new Point3(wallX, -18.5, 0), new Yaw(0), 1,
            [new TerrainVolume(wallVolume, new Volume(VolumeKind.Solid, Footprint.Rect(1, 1), new Point2(0, 0), new Yaw(0), 0, 10, TerrainDeclarations.BlocksLineOfSight, null))]);
        var observers = Unit("Observers", "4+", "4+", [], 2, [], weapons);
        var targets = Unit("Targets", "4+", "4+", [], 2, [P("Tough", 3)]);
        return (State([observers], [targets], x1: -5, x2: 5, terrain: [wall]), observers, targets, wallVolume);
    }

    [Fact]
    public void A_line_through_the_wall_is_blocked_by_it()
    {
        var (state, observers, targets, wall) = Table(0);
        var line = Sight.From(state, observers.Id).Line(observers.Models[0].Id, targets.Models[0].Id);
        Assert.NotNull(line);
        Assert.Equal((observers.Models[0].Id, targets.Models[0].Id, false, false), (line!.FromModelId, line.ToModelId, line.Clear, line.Screened));
        Assert.Contains(wall, line.BlockedBy);
    }

    [Fact]
    public void Every_line_agrees_with_the_rules_reading()
    {
        var (state, observers, targets, _) = Table(0);
        var sight = Sight.From(state, observers.Id);
        foreach (var from in observers.Models)
            foreach (var to in targets.Models)
            {
                var line = sight.Line(from.Id, to.Id)!;
                Assert.Equal(((bool, bool)?)(line.Clear, line.Screened), sight.Look(from.Id, to.Id));
                Assert.Equal(sight.CanSee(from.Id, to.Id), line.Clear);
                if (line.Clear) Assert.Empty(line.BlockedBy);
            }
        Assert.True(sight.Line(observers.Models[1].Id, targets.Models[1].Id)!.Clear);
    }

    [Fact]
    public void A_model_off_the_table_has_no_line()
    {
        var (state, observers, targets, _) = Table(0);
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new ModelsMoved([new ModelRelocation(targets.Models[0].Id, state.Projection.Models[targets.Models[0].Id].Location, new OnShelf())]), null));
        Assert.Null(Sight.From(state, observers.Id).Line(observers.Models[0].Id, targets.Models[0].Id));
    }
}
