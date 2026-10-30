using Tabletop.Content;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>3.6.0 p.13 Assault: "Counts as both a ranged weapon and a melee weapon." 3.5.1 has no such rule: a weapon with a Range is ranged only.</summary>
public class GfAssault36Tests
{
    static Tabletop.Content.Unit Raiders() => Unit("Raiders", "4+", "4+", [], 1, [], Gun("Pistol", "12\"", 1, P(GfNames.Assault)));
    static Tabletop.Content.Unit Target() => Unit("Target", "4+", "4+", [], 1, []);
    static Guid Pistol(Tabletop.Content.Unit u) => u.Models[0].Weapons[0].Id;

    static Sim Shoot(string rules, Tabletop.Content.Unit raiders, Tabletop.Content.Unit target)
    {
        var sim = Sim.Gf(rules, State([raiders], [target], x1: -3.5, x2: 0));
        sim.Start(GfNames.Shooting, new SequenceContext(raiders.Id, [], [], GfNames.Hold));
        if (sim.AskForOrNull(Samples.Team1) is not null) sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Pistol"] = target.Id }));
        return sim;
    }

    static Sim Fight(string rules, Tabletop.Content.Unit raiders, Tabletop.Content.Unit target, double x1)
    {
        var sim = Sim.Gf(rules, State([raiders], [target], x1: x1, x2: 0));
        sim.Start(GfNames.Melee, new SequenceContext(raiders.Id, [target.Id], GfMelee.MeleeWeapons(sim.State, raiders.Id), GfNames.Charge));   // as charge's fight step pushes it
        return sim;
    }

    static bool Attacked(Sim sim, Guid unit, Guid weapon) =>
        sim.Emitted.OfType<SequenceStarted>().Any(s => s.Sequence == GfNames.AttackSequence && s.Context.ActorUnitId == unit && s.Context.WeaponIds.Contains(weapon));

    [Fact]
    public void An_assault_weapon_shoots_in_the_shooting_phase_and_strikes_in_melee_under_3_6()
    {
        var raiders = Raiders();
        var target = Target();
        var shot = Shoot(RuleSets.Gf360, raiders, target);
        Assert.True(Attacked(shot, raiders.Id, Pistol(raiders)));
        Assert.Single(shot.Steps("roll-to-hit"));

        raiders = Raiders();
        target = Target();
        var melee = Fight(RuleSets.Gf360, raiders, target, -3);
        Assert.True(Attacked(melee, raiders.Id, Pistol(raiders)));
        Assert.Equal(1, melee.Steps("determine-attacks").First().Values!["eligible_models"]);
        Assert.False(melee.Steps("determine-attacks").First().Values!.Keys.Any(k => k.StartsWith("aim_")));   // no shooting aims: p.9 reach decides who strikes
        Assert.Equal(0, melee.Steps("determine-attacks").First().Values!["in_cover"]);
        var hit = melee.Steps("roll-to-hit").First().Rolls.Single(r => r.Roll == GfNames.Hit);
        Assert.Equal(4, hit.Threshold);                                                                 // a Quality test, as any melee strike
        Assert.Empty(hit.Modifiers);
        Assert.All(melee.Emitted.OfType<StepCompleted>().SelectMany(s => s.Rolls).Where(r => r.Roll == GfNames.DefenseRoll), r => Assert.Null(r.Cover));
    }

    [Fact]
    public void A_fatigued_unit_strikes_with_an_assault_weapon_only_on_sixes_under_3_6()
    {
        var raiders = Raiders();
        var target = Target();
        var sim = Sim.Gf36(State([raiders], [target], x1: -3, x2: 0));
        sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, raiders.Id), GfNames.Fatigued, true)]);
        sim.Start(GfNames.Melee, new SequenceContext(raiders.Id, [target.Id], GfMelee.MeleeWeapons(sim.State, raiders.Id), GfNames.Charge));
        Assert.Equal(6, sim.Steps("roll-to-hit").First().Rolls.Single(r => r.Roll == GfNames.Hit).RequiredRaw);   // p.9 fatigue is a melee rule
    }

    [Fact]
    public void Under_3_5_1_an_assault_named_weapon_with_a_range_is_ranged_only()
    {
        var raiders = Raiders();
        var target = Target();
        Assert.True(Attacked(Shoot(RuleSets.Gf351, raiders, target), raiders.Id, Pistol(raiders)));

        raiders = Raiders();
        target = Target();
        var melee = Fight(RuleSets.Gf351, raiders, target, -3);                                         // 0.48" edge to edge: within 3.5.1's 2" reach
        Assert.False(Attacked(melee, raiders.Id, Pistol(raiders)));
    }
}
