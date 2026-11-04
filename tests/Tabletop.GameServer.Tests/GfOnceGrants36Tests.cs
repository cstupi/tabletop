using Tabletop.Content;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>"Next time the effect would apply" is the attack: a once grant read under an attack root is held to `attack-end`, so every row of
/// its entry applies across the attack (spec → Once grants released at the attack's end). A rule fix: 3.5.1 games move too.</summary>
public class GfOnceGrants36Tests
{
    static EffectDocument Row(EffectKind kind, EffectSubject subject = EffectSubject.Self, string? roll = null, string? entry = null, int? amount = null) =>
        new(subject, 10, kind, roll, null, entry, null, null, null, null, amount, false, null, null, null, EffectDuration.Once, [], []);
    static EntryDocument Spell(string name, TargetSide side, params EffectDocument[] rows) =>
        new(name, CatalogKind.Spell, false, null, 1, null, $"{name}.", new SpellTargeting(side, TargetGrain.Unit, 1, 18), rows);

    static readonly EntryDocument Thrust = Spell("Test Thrust", TargetSide.Friendly, Row(EffectKind.ModifyRoll, roll: GfNames.Hit, amount: 1), Row(EffectKind.GrantProperty, entry: "AP", amount: 1));
    static readonly EntryDocument Mark = Spell("Test Mark", TargetSide.Enemy, Row(EffectKind.GrantProperty, EffectSubject.Opponent, entry: "Bane"));
    static readonly EntryDocument Aim = Spell("Test Aim", TargetSide.Friendly, Row(EffectKind.ModifyRoll, roll: GfNames.Hit, amount: 1));
    static readonly EntryDocument Steady = Spell("Test Steady", TargetSide.Friendly, Row(EffectKind.ModifyRoll, roll: GfNames.Morale, amount: 1));
    static readonly PropertySet Props = TestProperties.WithSpells(Thrust, Mark, Aim, Steady);

    static Sim Game(string rules, GameState state, params int[] dice) =>
        new(state.Under(rules), rules == RuleSets.Gf360 ? GrimdarkFutureRules.V360 : GrimdarkFutureRules.V351, Props, new ScriptedDice(dice));

    /// <summary>Every once row of the spell as a grant on the unit, the shape the effects step writes.</summary>
    static IReadOnlyList<Guid> Grant(Sim sim, Guid unit, string spell, Guid caster)
    {
        var ids = new List<Guid>();
        foreach (var row in Props.Entry(spell)!.Effects.Where(e => e.Duration == EffectDuration.Once))
        {
            var id = Guid.CreateVersion7(); ids.Add(id);
            sim.Append([new GrantAdded(unit, new Tabletop.Protocol.Grant(id, row.Id, spell, caster, 1))]);
        }
        return ids;
    }

    static IReadOnlyList<Guid> Removed(Sim sim, string step) => sim.Steps(step).SelectMany(s => s.Changes.OfType<GrantRemoved>()).Select(g => g.GrantId).ToList();

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void A_two_row_once_grant_read_at_roll_to_hit_still_gives_its_ap_at_roll_to_block_and_goes_at_attack_end(string rules)
    {
        var shooter = Unit("Shooter", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)); var target = Unit("T", "4+", "4+", [], 1, []);
        var sim = Game(rules, State([shooter], [target], x1: -4, x2: 4), 3, 4);
        var grants = Grant(sim, shooter.Id, "Test Thrust", shooter.Id);
        sim.Start(GfNames.AttackSequence, new SequenceContext(shooter.Id, [target.Id], shooter.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Hold));
        var hit = sim.Steps("roll-to-hit").Single().Rolls.Single();
        Assert.Contains(hit.Modifiers, m => m.Source == "Test Thrust" && m.Amount == 1);
        Assert.True(hit.Dice[0].Success);                                                        // 3 +1
        Assert.Contains(sim.Steps("roll-to-block").Single().Rolls.Single().Modifiers, m => m.Source == "AP" && m.Amount == -1);
        Assert.Empty(Removed(sim, "roll-to-hit")); Assert.Empty(Removed(sim, "roll-to-block"));
        Assert.Equal(grants.Order(), Removed(sim, "attack-end").Order());
        Assert.Equal(2, sim.Emitted.OfType<StepCompleted>().SelectMany(s => s.Changes.OfType<GrantRemoved>()).Count());
        Assert.Empty(sim.Unit(shooter.Id).Grants);
        Assert.Empty(sim.State.Projection.Stack);
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void A_bane_grant_read_for_its_reroll_still_suppresses_regeneration_at_assign(string rules)
    {
        var shooter = Unit("Shooter", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)); var troll = Unit("Troll", "4+", "6+", [], 1, [P("Regeneration"), P("Tough", 6)]);
        var sim = Game(rules, State([shooter], [troll], x1: -6, x2: 6), 5, 6, 2, 6);            // hit; a 6 blocks, re-rolled to 2; a regeneration die would show 6
        var grant = Grant(sim, troll.Id, "Test Mark", shooter.Id).Single();
        sim.Start(GfNames.AttackSequence, new SequenceContext(shooter.Id, [troll.Id], shooter.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Hold));
        Assert.Single(sim.Emitted.OfType<DiceRerolled>());                                      // Bane's re-roll at roll-to-block
        Assert.Empty(sim.Steps("assign").SelectMany(s => s.Rolls));                                       // no regeneration roll: Bane is still live
        Assert.Equal(1, sim.Model(troll.Models[0].Id).Wounds);
        Assert.Equal([grant], Removed(sim, "attack-end"));
        Assert.Empty(sim.Unit(troll.Id).Grants);
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void A_once_morale_modifier_is_spent_by_the_morale_test_itself(string rules)
    {
        var unit = Unit("U", "4+", "4+", [], 2, []);
        var sim = Game(rules, State([unit], [Unit("E", "4+", "4+", [], 1, [])]), 3);
        var grant = Grant(sim, unit.Id, "Test Steady", unit.Id).Single();
        sim.Start(GfNames.MoraleTest, new SequenceContext(unit.Id, [], [], "wounds"));
        Assert.Contains(sim.Steps("test").Single().Rolls.Single().Modifiers, m => m.Source == "Test Steady");
        Assert.Equal([grant], Removed(sim, "test"));
        Assert.Empty(sim.Unit(unit.Id).Grants);
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void Two_fired_groups_give_the_second_group_nothing_from_a_once_grant_the_first_used(string rules)
    {
        var shooter = Unit("Shooter", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1), Gun("Pistol", "12\"", 1)); var target = Unit("T", "4+", "6+", [], 1, [P("Tough", 6)]);
        var sim = Game(rules, State([shooter], [target], x1: -4, x2: 4), 5, 5, 5, 5, 5, 5);
        var grant = Grant(sim, shooter.Id, "Test Aim", shooter.Id).Single();
        sim.Start(GfNames.Shooting, new SequenceContext(shooter.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = target.Id, ["Pistol"] = target.Id }));
        var hits = sim.Steps("roll-to-hit").Select(s => s.Rolls.Single().Modifiers.Count(m => m.Source == "Test Aim")).ToList();
        Assert.Equal([1, 0], hits);
        Assert.Equal([grant], Removed(sim, "attack-end"));
        Assert.Single(sim.Steps("attack-end"), s => s.Changes.OfType<GrantRemoved>().Any());
        Assert.Empty(sim.Unit(shooter.Id).Grants);
    }
}
