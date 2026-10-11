using Tabletop.Content;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;
using Refused = Tabletop.GameServer.Dispatch.Refused;

namespace Tabletop.GameServer.Tests;

/// <summary>p.7: "Models may never be within 1” of models from other units, unless they are taking a Charge action, and may never move through other models".</summary>
public class GfContactTests
{
    // A's one model at (0, -18.5), B's at (x2, -18.5); bases 1.26".
    static Sim Game(Enforcement e, out Unit a, out Unit b, double x2 = 3, params PropertyInstance[] rules)
    {
        a = Unit("A", "4+", "4+", rules, 1, [], Melee("Claws", 1)); b = Unit("B", "4+", "6+", [], 1, []);
        return Sim.Gf(State([a], [b], x1: 0, x2: x2, enforcement: e), 6, 6, 6, 6, 6, 6);
    }
    static Sim Advancing(Enforcement e, out Unit a, out Unit b, double x2 = 3, params PropertyInstance[] rules)
    { var sim = Game(e, out a, out b, x2, rules); sim.Start(GfNames.AdvanceSeq, new SequenceContext(a.Id, [], [], GfNames.Advance)); return sim; }
    static MoveRegion Region(Sim sim) => Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
    static ModelRelocation To(Sim sim, Guid m, double x) => new(m, sim.Model(m).Location, new OnTable(new Placement(new Point3(x, -18.5, 0), new Yaw(0))));

    [Fact]
    public void An_advance_keeps_one_inch_a_charge_does_not_and_a_flyer_passes_models()
    {
        var advance = Advancing(Enforcement.Strict, out _, out _);
        Assert.Equal(((double?)1, false), (Region(advance).Separation, Region(advance).PassesModels));
        var charge = Game(Enforcement.Strict, out var c, out var target, x2: 12);
        charge.Start(GfNames.ChargeSeq, new SequenceContext(c.Id, [], [], GfNames.Charge));
        charge.Answer(Samples.Team1, new SelectedPieces([target.Id]));
        Assert.Null(Region(charge).Separation);
        Assert.True(Region(Advancing(Enforcement.Strict, out _, out _, 3, P("Flying"))).PassesModels);
    }
}
