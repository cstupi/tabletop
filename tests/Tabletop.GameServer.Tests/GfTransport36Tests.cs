using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>3.6.0 p.15 Transport(X): "If the Transport is unreachable (for example because it’s flying), then units can’t embark in it." 3.5.1 has no such sentence.</summary>
public class GfTransport36Tests
{
    static ModelRelocation At(Sim sim, Guid model, double x, double y) =>
        new(model, sim.Model(model).Location, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))));

    [Theory]
    [InlineData(RuleSets.Gf351, true)]
    [InlineData(RuleSets.Gf360, false)]
    public void An_aircraft_transport_is_not_in_reach_under_3_6_and_is_under_3_5_1(string rulesId, bool aircraftEnterable)
    {
        var jet = Unit("Jet", "4+", "4+", [], 1, [P("Transport", 11), P(GfNames.Aircraft)]);
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11)]);
        var riders = Unit("Riders", "4+", "4+", [], 1, []);
        var sim = Sim.Gf(rulesId, State([jet, bus, riders], [Unit("B", "4+", "4+", [], 1, [])], x1: -20, x2: 20));
        sim.Append([new ModelsMoved([At(sim, jet.Models[0].Id, 36, 20), At(sim, bus.Models[0].Id, 36, 28), At(sim, riders.Models[0].Id, 36, 24)])]);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(riders.Id, [], [], GfNames.Advance));
        var enterable = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Containment!.Enterable;
        Assert.Contains(bus.Models[0].Id, enterable);                                                     // a ground Transport stays reachable in both
        Assert.Equal(aircraftEnterable, enterable.Contains(jet.Models[0].Id));
    }
}
