using Tabletop.Content;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Setup;
using Tabletop.Geometry;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>Held pieces lift over spec → Server: a placement rests on its open surface nearest the Z the client sent.</summary>
public class SettleNearLevelTests
{
    static TerrainPlacement Piece(double x, double y, Footprint f, double bottom, double top) =>
        new(Guid.CreateVersion7(), VisualRef.OfBuiltin("hill"), new Point3(x, y, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, f, new Point2(0, 0), new Yaw(0), bottom, top, TerrainDeclarations.None, null))]);
    static readonly TerrainPlacement Hill = Piece(20, 24, Footprint.Rect(6, 6), 0, 2);                 // x 17..23, top 2
    static readonly TerrainPlacement Deck = Piece(40, 24, Footprint.Rect(8, 4), 3, 4);                 // x 36..44, y 22..26, Z 3..4: room under it

    static (GameState State, Guid Model) Table()
    {
        var unit = Unit("A", "4+", "4+", [], 1, []);
        return (State([unit], [Unit("B", "4+", "4+", [], 1, [])], terrain: [Hill, Deck]), unit.Models[0].Id);
    }

    static double Settled(double x, double z) { var (state, m) = Table(); return Battlefield.Settle(state, m, new Placement(new Point3(x, 24, z), new Yaw(0))).Position.Z; }

    [Fact] public void Z_zero_on_a_hill_is_the_hills_top() => Assert.Equal(2, Settled(20, 0), 6);
    [Fact] public void Z_zero_under_a_bridge_with_room_stays_under_it() => Assert.Equal(0, Settled(40, 0));
    [Fact] public void Z_at_the_deck_top_stands_on_the_deck() => Assert.Equal(4, Settled(40, 4), 6);

    [Fact]
    public void A_terrain_edit_sent_under_a_bridge_stays_under_it()
    {
        var crate = Piece(10, 10, Footprint.Rect(2, 2), 0, 2);
        var d = new TableState(Guid.CreateVersion7(), null, new TableSize(72, 48), VisualRef.OfBuiltin("grass"), [Deck, crate], [], []);
        Assert.Equal(0, DraftEditor.Apply(d, new MoveTerrain(crate.Id, new Point3(40, 24, 0), new Yaw(0)), out _)!.Terrain.Single(t => t.Id == crate.Id).Position.Z);
        Assert.Equal(4, DraftEditor.Apply(d, new MoveTerrain(crate.Id, new Point3(40, 24, 4), new Yaw(0)), out _)!.Terrain.Single(t => t.Id == crate.Id).Position.Z, 6);
    }
}
