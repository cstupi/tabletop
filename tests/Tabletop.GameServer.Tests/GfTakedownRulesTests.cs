using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.AppliedKindsTests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Takedown Shot and Takedown Strike (army-book rules spec → Readings, batch 3): the import gives the carrying model a weapon named after the rule with the
/// rule's profile and Limited (p.14 "may only be used once per game"). The engine lists the rule when that weapon attacks, and holds a fired Limited
/// weapon out of melee as it does out of shooting.
/// </summary>
public class GfTakedownRulesTests
{
    static Tabletop.Content.Weapon Strike() => Melee("Takedown Strike", 1, P("AP", 2), P("Deadly", 3), P("Reliable"), P("Takedown"), P("Limited"));

    [Fact]
    public void A_fired_limited_melee_weapon_is_left_out_of_later_melee()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Takedown Strike")], Melee("CCW", 1), Strike());
        var sim = Sim.Gf(State([a], [Unit("B", "4+", "4+", [], 1, [])], x1: -0.5, x2: 0.5), 4);
        var strike = a.Models[0].Weapons[1].Id;
        Assert.Equal([a.Models[0].Weapons[0].Id, strike], GfMelee.MeleeWeapons(sim.State, a.Id));
        var fired = Guid.CreateVersion7();
        sim.Append([new SequenceStarted(fired, GfNames.AttackSequence, new SequenceContext(a.Id, [], [strike], GfNames.Charge)),
            new StepCompleted(fired, "determine-attacks", null, [], []) { Values = new Dictionary<string, int> { ["attacks"] = 1, [$"limited_{strike:N}"] = 1 } },   // it attacked
            new SequenceFinished(fired)]);
        Assert.Equal([a.Models[0].Weapons[0].Id], GfMelee.MeleeWeapons(sim.State, a.Id));
    }

    [Fact]
    public void Melee_lists_the_rule_whose_weapon_strikes()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Takedown Strike")], Melee("CCW", 1), Strike());
        var b = Unit("B", "4+", "4+", [], 2, []);
        var sim = Sim.Gf(State([a], [b], x1: -0.5, x2: 0.5), 4);
        sim.Start(GfNames.Melee, new SequenceContext(a.Id, [b.Id], a.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Charge));
        sim.Answer(Samples.Team1, new Chosen("yes"));                                                    // "it may make X attacks"
        Assert.Contains("Takedown Strike", Applied(sim, "charger-attacks"));
    }

    [Fact]
    public void Shooting_lists_the_rule_whose_weapon_fires_and_not_a_weapon_that_does_not()
    {
        var shot = Gun("Takedown Shot", "24\"", 1, P("AP", 2), P("Deadly", 3), P("Reliable"), P("Takedown"), P("Limited"));
        var a = Unit("A", "4+", "4+", [], 1, [P("Takedown Shot")], Gun("Rifle", "24\"", 1), shot);
        var b = Unit("B", "4+", "4+", [], 2, []);
        var sim = Sim.Gf(State([a], [b], x1: -6, x2: 6), 4);
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id }));
        Assert.DoesNotContain("Takedown Shot", Applied(sim, "fire-groups"));

        var again = Sim.Gf(State([a], [Unit("B", "4+", "4+", [], 2, [])], x1: -6, x2: 6), 4);
        again.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        var target = again.State.Board.Armies[1].Snapshot.Units[0].Id;
        again.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = target, ["Takedown Shot"] = target }));
        Assert.Contains("Takedown Shot", Applied(again, "fire-groups"));
    }
}
