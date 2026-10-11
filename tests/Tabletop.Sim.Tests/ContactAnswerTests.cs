using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;

namespace Tabletop.Sim.Tests;

/// <summary>Every sim scenario defaults to strict: a sampled move the server judges a contact breach would be refused and the run would stick.</summary>
public class ContactAnswerTests
{
    [Fact]
    public void Sampled_moves_never_breach_contact()
    {
        // Five A models in a 1.5" column at x 30, B's three at x 33: most bearings cross B, land on it or end within 1" of it.
        var a = Armies.Unit("A", "4+", "4+", [], 5, []); var b = Armies.Unit("B", "4+", "4+", [], 3, []);
        var state = Armies.State([a], [b]);
        var placed = a.Models.Select((m, i) => (m.Id, X: 30.0, Y: 20 + 1.5 * i)).Concat(b.Models.Select((m, i) => (m.Id, X: 33.0, Y: 21 + 1.5 * i)))
            .Select(p => new ModelRelocation(p.Id, state.Projection.Models[p.Id].Location, new OnTable(new Placement(new Point3(p.X, p.Y, 0), new Yaw(0))))).ToList();
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new ModelsMoved(placed), null));
        var region = new MoveRegion(a.Models.Select(m => m.Id).ToList(), 6, [], [], null, Separation: 1);
        var ask = new Ask(Guid.CreateVersion7(), "move", "Move", [Guid.CreateVersion7()], region, false, false, null, Guid.CreateVersion7());
        for (var seed = 0ul; seed < 25; seed++)
        {
            var answer = Assert.IsType<Destinations>(new AnswerPolicy(new Chooser(seed)).Answer(ask, new PolicyView(state.Projection, state.Board)));
            Assert.Null(GfMoves.ContactBreach(state, region, answer));
        }
    }

    [Fact]
    public void Capped_resamples_never_breach_contact()
    {
        // A's first model fenced 1.04" from B's three on every side but its partner's, the second in a difficult patch under a 1" cap:
        // the first slips out between B's models, is drawn again within the cap once the second enters the patch, and finds room only
        // where the second may have landed.
        var a = Armies.Unit("A", "4+", "4+", [], 2, []); var b = Armies.Unit("B", "4+", "4+", [], 3, []);
        var patch = new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(2, 1.4), new Point2(0, 0), new Yaw(0), 0, 1, TerrainDeclarations.Difficult, null));
        var state = Armies.State([a], [b], terrain: [new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("patch"), new Point3(30, 21.8, 0), new Yaw(0), 1, [patch])]);
        (double X, double Y)[] at = [(30, 20), (30, 21.3), (27.7, 20), (32.3, 20), (30, 17.7)];
        var placed = a.Models.Concat(b.Models).Select((m, i) => new ModelRelocation(m.Id, state.Projection.Models[m.Id].Location,
            new OnTable(new Placement(new Point3(at[i].X, at[i].Y, 0), new Yaw(0))))).ToList();
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new ModelsMoved(placed), null));
        var region = new MoveRegion(a.Models.Select(m => m.Id).ToList(), 6, [], [patch.Id], 1, Separation: 1);
        var ask = new Ask(Guid.CreateVersion7(), "move", "Move", [Guid.CreateVersion7()], region, false, false, null, Guid.CreateVersion7());
        for (var seed = 0ul; seed < 200; seed++)
        {
            var answer = Assert.IsType<Destinations>(new AnswerPolicy(new Chooser(seed)).Answer(ask, new PolicyView(state.Projection, state.Board)));
            Assert.Null(GfMoves.ContactBreach(state, region, answer));
        }
    }
}
