using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Content.Tests;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class GhostLookTests
{
    static readonly Guid Mesh = Guid.CreateVersion7();
    static ModelSlot Slot(int ordinal, Base b, double height, Guid? asset) => new(Guid.CreateVersion7(), ordinal, $"m{ordinal}", new Dictionary<string, string>(), [], b, height, asset, []);

    // A unit of two models, one with an uploaded mesh and one without, and a transport.
    static readonly ModelSlot Meshed = Slot(1, Base.Circle(1.26), 1.5, Mesh), Bare = Slot(2, Base.Oval(2.36, 1.38), 2, null), Truck = Slot(1, Base.Rect(4, 6), 3, null);
    static readonly Unit Squad = new(Guid.CreateVersion7(), 1, "Squad", new Dictionary<string, string>(), [], [Meshed, Bare]);
    static readonly Unit Transport = new(Guid.CreateVersion7(), 2, "Truck", new Dictionary<string, string>(), [], [Truck]);

    static Projection Play(params EventPayload[] history)
    {
        var army = new Army(Guid.CreateVersion7(), "A", "grimdark-future", "x", [Squad, Transport]);
        var a = new EventApplier(new Board(TableStateTests.Sample(), [new ArmyInPlay(Samples.P1, ArmySnapshot.Of(army, Guid.CreateVersion7()))]), Samples.Game);
        a.ApplyAll(history.Select((p, i) => new GameEvent(i + 1, Samples.P1, p, null)));
        return a.Projection;
    }

    static Placement At(double x, double y, double yaw) => new(new Point3(x, y, 0), new Yaw(yaw));

    [Fact]
    public void A_deploy_ghost_draws_the_slots_own_mesh_and_body_at_its_placement_and_facing()
    {
        var p = Play();
        var looks = GhostLooks.Placing(p, [new ModelRelocation(Meshed.Id, new OnShelf(), new OnTable(At(10, 12, 45))), new ModelRelocation(Bare.Id, new OnShelf(), new OnTable(At(12, 12, 0)))]);
        Assert.Equal([new GhostLook(Meshed.Id, Squad.Id, Meshed.Base, 1.5, Mesh, At(10, 12, 45)), new GhostLook(Bare.Id, Squad.Id, Bare.Base, 2, null, At(12, 12, 0))], looks);
    }

    [Fact]
    public void A_relocation_off_the_table_or_of_an_unknown_model_draws_no_ghost()
    {
        var p = Play();
        Assert.Empty(GhostLooks.Placing(p, [new ModelRelocation(Meshed.Id, new OnShelf(), new Inside(Truck.Id)), new ModelRelocation(Guid.CreateVersion7(), new OnShelf(), new OnTable(At(1, 1, 0)))]));
    }

    [Fact]
    public void Another_players_proposed_model_draws_that_models_mesh_where_and_as_they_turned_it()
    {
        var p = Play();
        var look = GhostLooks.Proposed(p, new PresencePlacement(Meshed.Id, new Point3(5, 6, 0), new Yaw(30), PlacementKind.Proposed))!;
        Assert.Equal(Mesh, look.AssetId);
        Assert.Equal(At(5, 6, 30), look.At);
        Assert.Null(GhostLooks.Proposed(p, new PresencePlacement(Guid.CreateVersion7(), new Point3(5, 6, 0), new Yaw(0), PlacementKind.Proposed)));
        Assert.Null(GhostLooks.Proposed(null, new PresencePlacement(Meshed.Id, new Point3(5, 6, 0), new Yaw(0), PlacementKind.Proposed)));
    }
}
