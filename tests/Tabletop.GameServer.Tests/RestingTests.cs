using Tabletop.Content;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Setup;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>Client UX spec → Physical collision → Server: a stored position is never inside terrain, whatever the client sent.</summary>
public class RestingTests
{
    static TerrainPlacement Piece(double x, double y, VolumeKind kind, Footprint f, double top) =>
        new(Guid.CreateVersion7(), VisualRef.OfBuiltin("hill"), new Point3(x, y, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(kind, f, new Point2(0, 0), new Yaw(0), 0, top, TerrainDeclarations.None, null))]);
    static TerrainPlacement Hill(double x, double y) => Piece(x, y, VolumeKind.Solid, Footprint.Rect(6, 6), 2);

    // Armies.State: team 1's first model at (-12, -18.5). The hill spans x -9..-3; a centre at -9.2 puts the base half over it.
    static Sim Game(out Unit unit) { unit = Unit("A", "4+", "4+", [], 2, []); return Sim.Gf(State([unit], [Unit("B", "4+", "4+", [], 1, [])], x2: 20, enforcement: Enforcement.Silent, terrain: [Hill(-6, -18.5)])); }
    static ModelRelocation To(Sim sim, Guid m, double x) => new(m, sim.Model(m).Location, new OnTable(new Placement(new Point3(x, -18.5, 0), new Yaw(0))));
    static double Z(Sim sim, Guid m) => ((OnTable)sim.Model(m).Location).Placement.Position.Z;

    [Fact]
    public void A_hand_move_sent_at_z0_over_a_hill_is_stored_on_it()
    {
        var sim = Game(out var u);
        Assert.IsType<Applied>(sim.Dispatch(new MoveModels([To(sim, u.Models[0].Id, -9.2)]), Samples.P1, Samples.Team1));
        Assert.Equal(2, Z(sim, u.Models[0].Id), 6);
    }

    [Fact]
    public void A_deployment_placement_is_stored_on_the_hill()
    {
        var sim = Game(out var u); var m = u.Models[0].Id;
        sim.Append([new ModelsMoved([new ModelRelocation(m, sim.Model(m).Location, new OnShelf())])]);
        sim.Dispatch(new MoveModels([new ModelRelocation(m, new OnShelf(), To(sim, m, -6).To)]), Samples.P1, Samples.Team1);
        Assert.Equal(2, Z(sim, m), 6);
    }

    [Fact]
    public void A_move_answer_sent_at_z0_is_stored_on_the_hill()
    {
        var sim = Game(out var u);
        sim.Start(GfNames.RushSeq, new SequenceContext(u.Id, [], [], GfNames.Rush));
        sim.Answer(Samples.Team1, new Destinations([To(sim, u.Models[0].Id, -6)]));
        Assert.Equal(2, Z(sim, u.Models[0].Id), 6);
    }

    [Fact]
    public void A_wood_added_or_moved_onto_a_hill_rests_on_it_and_the_hill_ignores_itself()
    {
        var hill = Hill(12, 12); var wood = Piece(30, 30, VolumeKind.Area, Footprint.Circle(3), 6);
        var d = new TableState(Guid.CreateVersion7(), null, new TableSize(72, 48), VisualRef.OfBuiltin("grass"), [hill], [], []);
        d = DraftEditor.Apply(d, new AddTerrain(wood), out _)!;
        Assert.Equal(0, d.Terrain.Single(t => t.Id == wood.Id).Position.Z);
        d = DraftEditor.Apply(d, new MoveTerrain(wood.Id, new Point3(13, 12, 0), new Yaw(0)), out _)!;
        Assert.Equal(2, d.Terrain.Single(t => t.Id == wood.Id).Position.Z, 6);
        d = DraftEditor.Apply(d, new MoveTerrain(hill.Id, new Point3(12.5, 12, 0), new Yaw(0)), out _)!;
        Assert.Equal(0, d.Terrain.Single(t => t.Id == hill.Id).Position.Z);           // an Area is never a floor for a piece
    }
}
