using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Content.Tests;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

/// <summary>A disembark move Ask (p.15 "must stay fully within 6" of it when exiting"): where the passengers' ghosts start, and the mark a ghost
/// outside the exit reach carries.</summary>
public class DisembarkStartTests
{
    static ModelSlot Slot(int ordinal, Base b, double height) => new(Guid.CreateVersion7(), ordinal, $"m{ordinal}", new Dictionary<string, string>(), [], b, height, null, []);

    // Five infantry and one cavalry on an oval base, inside a Light APC on a 5" by 3" base.
    static readonly IReadOnlyList<ModelSlot> Riders = [.. Enumerable.Range(1, 5).Select(i => Slot(i, Base.Circle(1.26), 1.5)), Slot(6, Base.Oval(2.36, 1.38), 2)];
    static readonly ModelSlot Apc = Slot(1, Base.Rect(5, 3), 2.5);
    static readonly Unit Squad = new(Guid.CreateVersion7(), 1, "Veterans", new Dictionary<string, string>(), [], Riders);
    static readonly Unit Transport = new(Guid.CreateVersion7(), 2, "Light APC", new Dictionary<string, string>(), [], [Apc]);
    static readonly TableSize Size = new(72, 48);

    static Projection Inside(double x, double y, double yaw = 0)
    {
        var army = new Army(Guid.CreateVersion7(), "A", "grimdark-future", "x", [Squad, Transport]);
        var a = new EventApplier(new Board(TableStateTests.Sample(), [new ArmyInPlay(Samples.P1, ArmySnapshot.Of(army, Guid.CreateVersion7()))]), Samples.Game);
        EventPayload moved = new ModelsMoved([new ModelRelocation(Apc.Id, new OnShelf(), new OnTable(new Placement(new Point3(x, y, 0), new Yaw(yaw)))),
            .. Riders.Select(r => new ModelRelocation(r.Id, new OnShelf(), new Inside(Apc.Id)))]);
        a.ApplyAll([new GameEvent(1, Samples.P1, moved, null)]);
        return a.Projection;
    }

    static MoveRegion Exit(IReadOnlyList<Guid>? impassable = null) =>
        new([.. Riders.Select(r => r.Id)], 6, impassable ?? [], [], null, new MoveContainment([], Apc.Id, 6), Separation: 1);

    static PlacedModel ApcAt(Projection p) => new(new ModelBody(Apc.Base, Apc.Height), ((OnTable)p.Models[Apc.Id].Location).Placement);
    static PlacedModel Body(Projection p, ModelRelocation r) => new(new ModelBody(p.Models[r.ModelId].Slot.Base, p.Models[r.ModelId].Slot.Height), ((OnTable)r.To).Placement);

    [Theory]
    [InlineData(36, 24, 0)]
    [InlineData(36, 24, 37)]
    [InlineData(2.6, 24, 90)]      // against the left edge: half the ring is off the table
    [InlineData(3, 2, 0)]          // in a corner
    public void Every_passenger_starts_on_the_table_around_the_transport_clear_of_it_inside_the_exit_reach(double x, double y, double yaw)
    {
        var p = Inside(x, y, yaw); var legal = Exit();
        var start = MoveRuler.ExitStart(p, legal, [], Size);
        Assert.Equal(legal.ModelIds, start.Select(r => r.ModelId));
        Assert.All(start, r => Assert.IsType<Inside>(r.From));
        var apc = ApcAt(p);
        var bodies = start.Select(r => Body(p, r)).ToList();
        foreach (var b in bodies)
        {
            Assert.True(Distance.BodyToBody(b, apc) >= 1 - 1e-6, $"{b.Placement.Position} within the Ask's 1\" of the transport");
            Assert.True(Containment.BaseFullyWithin(b, apc, 6), $"{b.Placement.Position} not fully within 6\" of the transport");
            Assert.True(Table.ContainsBase(Size, b), $"{b.Placement.Position} off the table");
        }
        for (var i = 0; i < bodies.Count; i++) for (var j = i + 1; j < bodies.Count; j++) Assert.True(Distance.BodyToBody(bodies[i], bodies[j]) > 0, "two ghosts overlap");
    }

    [Fact]
    public void A_ring_spot_in_impassable_terrain_is_skipped()
    {
        var p = Inside(36, 24);
        // A wall over the transport's whole east side, from 1" past its edge outward.
        var wall = new WorldVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Rect(8, 20), new Point2(0, 0), new Yaw(0), 0, 3, TerrainDeclarations.Impassable, null),
            new TerrainTransform(new Point3(36 + 1.5 + 1 + 4, 24, 0), new Yaw(0), 1));
        var legal = Exit([wall.Id]);
        var start = MoveRuler.ExitStart(p, legal, [wall], Size);
        Assert.Equal(legal.ModelIds.Count, start.Count);
        Assert.All(start, r => Assert.NotNull(MoveRuler.ExitYawAt(new ModelBody(p.Models[r.ModelId].Slot.Base, 1.5), ApcAt(p), 6, ((OnTable)r.To).Placement.Position.Horizontal, [wall], Size)));
    }

    [Fact]
    public void A_ghost_outside_the_exit_reach_is_marked_and_one_inside_is_not()
    {
        var p = Inside(36, 24); var legal = Exit();
        var start = MoveRuler.ExitStart(p, legal, [], Size).ToDictionary(r => r.ModelId, r => ((OnTable)r.To).Placement);
        Assert.Empty(MoveRuler.ExitWords(p, legal, start, [], Size, _ => "Light APC"));
        var far = new Dictionary<Guid, Placement>(start) { [Riders[0].Id] = new Placement(new Point3(36, 24 + 1.5 + 7, 0), new Yaw(0)) };
        var words = MoveRuler.ExitWords(p, legal, far, [], Size, _ => "Light APC");
        Assert.Equal("beyond 6\" of Light APC", Assert.Single(words, kv => kv.Key == Riders[0].Id).Value);
        Assert.Equal("Beyond 6\"", MarkWords.Short(words[Riders[0].Id]));
        Assert.Equal("m1 would be placed beyond 6\" of Light APC", ContactCheck.Sentence(words[Riders[0].Id], "m1"));
        var edge = Inside(3, 24);
        var off = new Dictionary<Guid, Placement> { [Riders[0].Id] = new Placement(new Point3(0.2, 24, 0), new Yaw(0)) };   // within 6" but over the edge
        Assert.Equal("would leave the table", MoveRuler.ExitWords(edge, legal, off, [], Size, _ => "Light APC")[Riders[0].Id]);
    }
}
