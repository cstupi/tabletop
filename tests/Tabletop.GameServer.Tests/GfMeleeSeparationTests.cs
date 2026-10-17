using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Melee separation off (optional-rules spec; p.10: "players may agree not to separate units by 1" for simplicity, as long as they remember that
/// units can still freely activate, move, shoot, charge, etc. despite being in contact").
/// </summary>
public class GfMeleeSeparationTests
{
    static Sim Off(Sim sim) { sim.State.Info = sim.State.Info with { OptionalRules = new Dictionary<string, string> { ["melee_separation"] = "off" } }; return sim; }

    static Sim Melee(bool separation, out Unit charger, out Unit defender)
    {
        charger = Unit("Charger", "4+", "4+", [], 1, [], Armies.Melee("Claw", 1));
        defender = Unit("Pikes", "4+", "4+", [], 1, [], Armies.Melee("Pike", 1, P("Counter")));
        var sim = Sim.Gf(State([charger], [defender], x1: -1.3, x2: 0), 1, 1);                  // Counter strike misses; charger misses; nobody strikes back
        if (!separation) Off(sim);
        sim.Start(GfNames.Melee, new SequenceContext(charger.Id, [defender.Id], charger.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Charge));
        return sim;
    }

    [Fact]
    public void With_both_units_alive_the_charger_stays_in_contact()
    {
        var on = Melee(separation: true, out _, out _);
        Assert.Equal(1.0, Assert.IsType<MoveRegion>(on.AskFor(Samples.Team1).Legal).Allowance);     // p.10: "the charging unit must move back by 1""
        var off = Melee(separation: false, out var charger, out _);
        Assert.Null(off.AskForOrNull(Samples.Team1));
        Assert.Empty(off.Steps("consolidate").Single().Changes.OfType<ModelsMoved>());
        Assert.Equal(-1.3, ((OnTable)off.Model(charger.Models[0].Id).Location).Placement.Position.X, 6);
    }

    /// <summary>A at x 0 is within 1" of B at x 1.3 (0.04" apart) as its Advance starts; C at x -2.6 is 1.34" away. All at y -18.5.</summary>
    static (Sim Sim, Guid Mover) InContact(bool separation)
    {
        var (a, b, c) = (Unit("A", "4+", "4+", [], 1, [], Armies.Melee("Claw", 1)), Unit("B", "4+", "4+", [], 1, []), Unit("C", "4+", "4+", [], 1, []));
        var sim = Sim.Gf(State([a], [b, c], x1: 0, x2: 1.3, enforcement: Enforcement.Strict), 4);
        sim.Append([new ModelsMoved([new ModelRelocation(c.Models[0].Id, sim.Model(c.Models[0].Id).Location, new OnTable(new Placement(new Point3(-2.6, -18.5, 0), new Yaw(0))))])]);
        if (!separation) Off(sim);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(a.Id, [], [], GfNames.Advance));
        return (sim, a.Models[0].Id);
    }

    static string? Breach(Sim sim, Guid model, double x, double y) => GrimdarkFutureRules.Instance.AnswerBreach(sim.State, sim.AskFor(Samples.Team1),
        new Destinations([new ModelRelocation(model, sim.Model(model).Location, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))))]))?.Rule;

    [Fact]
    public void A_unit_in_contact_moves_and_stays_within_an_inch_of_that_enemy_but_not_of_another()
    {
        var (on, a) = InContact(separation: true);
        Assert.Equal(GfNames.MoveSeparationRule, Breach(on, a, 0, -17.5));                       // 0.38" from B
        var (off, m) = InContact(separation: false);
        Assert.Null(Breach(off, m, 0, -17.5));
        Assert.Equal(GfNames.MoveSeparationRule, Breach(off, m, -0.5, -18.5));                    // 0.54" from B, but 0.84" from C, which it was not in contact with
    }

    [Fact]
    public void A_unit_in_contact_may_charge_the_unit_it_touches()
    {
        var (a, b) = (Unit("A", "4+", "4+", [], 1, [], Armies.Melee("Claw", 1)), Unit("B", "4+", "4+", [], 1, []));
        var sim = Off(Sim.Gf(State([a], [b], x1: 0, x2: 1.26), 4));
        Assert.Contains(b.Id, GfMelee.ChargeTargets(sim.State, TestProperties.Core, a.Id));
    }
}
