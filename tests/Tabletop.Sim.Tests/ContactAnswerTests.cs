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
}
