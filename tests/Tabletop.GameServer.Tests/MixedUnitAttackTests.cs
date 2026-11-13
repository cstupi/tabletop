using Tabletop.Content;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// A unit whose models carry different rules: a joined Hero, a Sergeant, an upgrade taken by some models. "This model …" rules act on the attacks
/// of the models that carry them and on nothing else, even when every model fires a weapon of the same name (one weapon group, p.8).
/// </summary>
public class MixedUnitAttackTests
{
    /// <summary>The squad with its first slot a joined Hero carrying `heroProps` (and Hero) and, when given, its own weapons in place of the squad's.</summary>
    static Unit Joined(Unit squad, PropertyInstance[] heroProps, params Weapon[] heroWeapons) =>
        squad with { Models = [squad.Models[0] with { Name = "Captain", Properties = [P("Hero"), .. heroProps],
            Weapons = heroWeapons.Length == 0 ? squad.Models[0].Weapons : heroWeapons.Select(w => w with { Id = Guid.CreateVersion7() }).ToList() }, .. squad.Models.Skip(1)] };

    static Sim Shoot(Unit shooters, Unit target, params int[] dice)
    {
        var sim = Sim.Gf36(State([shooters], [target], x1: -6, x2: 6), dice);
        sim.Start(GfNames.Shooting, new SequenceContext(shooters.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = target.Id }));
        return sim;
    }

    static Sim Charge(Unit chargers, Unit target, params int[] dice)
    {
        var sim = Sim.Gf36(State([chargers], [target], x1: -3, x2: 0), dice);
        sim.Start(GfNames.Melee, new SequenceContext(chargers.Id, [target.Id], chargers.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Charge));
        return sim;
    }

    static IReadOnlyList<RollRecord> Rolls(Sim sim, string step) => sim.Steps(step).SelectMany(s => s.Rolls).ToList();
    static int Ap(RollRecord r) => r.Modifiers.Where(m => m.Source == "AP").Sum(m => m.Amount);

    [Fact]
    public void A_squads_good_shot_holds_for_its_own_rifles_beside_a_joined_hero_without_it()
    {
        // Good Shot (all): "This model gets +1 to hit rolls when shooting." The Captain carries a Rifle and no Good Shot.
        var squad = Joined(Unit("Squad", "4+", "4+", [], 3, [P("Good Shot")], Gun("Rifle", "24\"", 1)), []);
        var sim = Shoot(squad, Unit("T", "4+", "6+", [], 5, []), 1);
        var hits = Rolls(sim, "roll-to-hit");
        Assert.Equal(2, hits.Count);                                                                   // one attack per carrier signature
        Assert.Contains(hits, r => r.Dice.Count == 2 && r.Modifiers.Any(m => m.Source == "Good Shot"));
        Assert.Contains(hits, r => r.Dice.Count == 1 && r.Modifiers.All(m => m.Source != "Good Shot"));
    }

    [Fact]
    public void A_heros_good_shot_holds_for_its_own_rifle_and_not_the_squads()
    {
        var squad = Joined(Unit("Squad", "4+", "4+", [], 3, [], Gun("Rifle", "24\"", 1)), [P("Good Shot")]);
        var hits = Rolls(Shoot(squad, Unit("T", "4+", "6+", [], 5, []), 1), "roll-to-hit");
        Assert.Contains(hits, r => r.Dice.Count == 1 && r.Modifiers.Any(m => m.Source == "Good Shot"));
        Assert.Contains(hits, r => r.Dice.Count == 2 && r.Modifiers.All(m => m.Source != "Good Shot"));
    }

    [Fact]
    public void A_heros_ap_rifle_gives_ap_to_its_own_hits_only()
    {
        // AP is the weapon's: "Targets get -X to Defense rolls when blocking hits from weapons with this special rule".
        var squad = Joined(Unit("Squad", "4+", "4+", [], 3, [], Gun("Rifle", "24\"", 1)), [], Gun("Rifle", "24\"", 1, P("AP", 1)));
        var blocks = Rolls(Shoot(squad, Unit("T", "4+", "2+", [], 5, []), 6), "roll-to-block");
        Assert.Contains(blocks, r => r.Dice.Count == 1 && Ap(r) == -1);
        Assert.Contains(blocks, r => r.Dice.Count == 2 && Ap(r) == 0);
    }

    [Fact]
    public void A_carrier_of_a_pick_entry_fires_with_the_squad()
    {
        // A Debuff acts through the grants its pick writes, so its carrier's attacks are the squad's: one attack, one pool.
        var squad = Unit("Squad", "4+", "4+", [], 3, [], Gun("Rifle", "24\"", 1));
        squad = squad with { Models = [squad.Models[0] with { Properties = [P("Piercing Debuff")] }, .. squad.Models.Skip(1)] };
        var hits = Rolls(Shoot(squad, Unit("T", "4+", "6+", [], 5, []), 1), "roll-to-hit");
        Assert.Equal(3, Assert.Single(hits).Dice.Count);
    }

    [Fact]
    public void A_sergeants_ap_reaches_its_own_hits_only()
    {
        // Sergeant (all): "This model gets AP(+1) when attacking."
        var squad = Unit("Squad", "4+", "4+", [], 3, [], Gun("Rifle", "24\"", 1));
        squad = squad with { Models = [squad.Models[0] with { Properties = [P("Sergeant")] }, .. squad.Models.Skip(1)] };
        var blocks = Rolls(Shoot(squad, Unit("T", "4+", "2+", [], 5, []), 6), "roll-to-block");
        Assert.Contains(blocks, r => r.Dice.Count == 1 && Ap(r) == -1);
        Assert.Contains(blocks, r => r.Dice.Count == 2 && Ap(r) == 0);
    }

    [Fact]
    public void Piercing_charge_holds_for_the_squad_beside_a_joined_hero_in_melee()
    {
        // Piercing Charge (all): "This model gets AP(+1) on its weapons when charging." The Captain strikes with the same CCW and no Piercing Charge.
        var squad = Joined(Unit("Squad", "4+", "4+", [], 3, [P("Piercing Charge")], Melee("CCW", 1)), []);
        var blocks = Rolls(Charge(squad, Unit("T", "4+", "2+", [], 5, []), 6), "roll-to-block");
        Assert.Contains(blocks, r => r.Dice.Count == 2 && Ap(r) == -1);
        Assert.Contains(blocks, r => r.Dice.Count == 1 && Ap(r) == 0);
    }

    [Fact]
    public void An_enhanced_rule_is_judged_over_the_models_attacking()
    {
        // Enhanced Mischievous: "If this model has Mischievous, …". The squad has both; the joined Captain has neither, and the squad's own
        // attack still holds Mischievous.
        var squad = Joined(Unit("Squad", "4+", "4+", [], 3, [P("Mischievous"), P("Enhanced Mischievous")], Melee("Claws", 1)), [], Melee("Sword", 1));
        var target = Unit("T", "4+", "4+", [], 1, []);
        var s = State([squad], [target]).Under(RuleSets.Gf360);
        var troops = squad.Models.Skip(1).Select(m => m.Id).ToList();
        var claws = squad.Models.Skip(1).SelectMany(m => m.Weapons).Select(w => w.Id).ToList();
        var live = LiveProperties.Settle(new Interaction(s, TestProperties.Core36, Party.Of(s, squad.Id, troops, claws), Party.Whole(s, target.Id), AttackKind.Melee, GfNames.Charge, 1, []));
        var enhanced = live.On(Side.Attacker).Single(p => p.Entry.Name == "Enhanced Mischievous");
        var row = enhanced.Effects.Single(e => e.Kind == EffectKind.ForceReroll);
        Assert.True(row.Conditions.Where(c => c.Kind == ConditionKind.HoldsProperty).All(c => Conditions.Holds(live.Interaction, Side.Attacker, c, enhanced, null, live)));
    }

    [Fact]
    public void Heavy_impact_gives_ap_1_to_its_own_models_impact_hits_only()
    {
        // Heavy Impact: "Counts as having Impact(X) with hits that have AP(1)." One model has Heavy Impact(1), the other Impact(1).
        var chargers = Unit("C", "4+", "4+", [], 2, [], Melee("CCW", 1));
        chargers = chargers with { Models = [chargers.Models[0] with { Properties = [P("Heavy Impact", 1)] }, chargers.Models[1] with { Properties = [P("Impact", 1)] }] };
        var sim = Charge(chargers, Unit("T", "4+", "2+", [], 5, []), 6);
        var impact = sim.Steps("roll-to-block").First().Rolls;
        Assert.Contains(impact, r => r.Dice.Count == 1 && Ap(r) == -1);
        Assert.Contains(impact, r => r.Dice.Count == 1 && Ap(r) == 0);
        Assert.Equal(2, impact.Sum(r => r.Dice.Count));
    }

    [Fact]
    public void Weapon_rules_do_not_act_on_impact_hits()
    {
        // 3.6.0 p.14 Impact: "roll X dice … For each 2+ the target takes 1 hit": no weapon makes them, so "AP(+1) on its weapons" and "hits from this
        // model's weapons" leave them alone.
        var chargers = Unit("C", "4+", "4+", [], 1, [P("Impact", 1), P("Piercing Charge"), P("Mischievous")], Melee("CCW", 1));
        var sim = Charge(chargers, Unit("T", "4+", "2+", [], 5, []), 6);
        var blocks = sim.Steps("roll-to-block").ToList();
        Assert.Equal(0, Ap(blocks[0].Rolls.Single()));                                                // Impact's hit
        Assert.DoesNotContain("Piercing Charge", blocks[0].Applied ?? []);
        Assert.DoesNotContain("Mischievous", blocks[0].Applied ?? []);
        Assert.Equal(-1, Ap(blocks[1].Rolls.Single()));                                               // the CCW's hit
    }

    static void Apply(GameState state, IEnumerable<StateChange> changes)
    {
        foreach (var c in changes) state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, c, null));
    }

    [Theory]
    [InlineData("Piercing Frenzy")]
    [InlineData("Piercing Growth")]
    public void A_piercing_token_reaches_only_the_models_with_the_rule(string rule)
    {
        // "For each token all models with this rule in this unit get AP(+1) on their weapons". The squad has the rule, the joined Captain does not.
        var squad = Joined(Unit("Squad", "4+", "4+", [], 3, [P(rule)], Melee("CCW", 1)), []);
        var target = Unit("T", "4+", "4+", [], 1, []);
        var s = State([squad], [target]).Under(RuleSets.Gf360);
        var props = TestProperties.Core36;
        Apply(s, GfMarkers.Place(props.Entry(rule)!, s.Projection.Units[squad.Id], squad.Id, 2));
        int? Ap(IEnumerable<ModelSlot> models)
        {
            var ids = models.Select(m => m.Id).ToList();
            var weapons = models.SelectMany(m => m.Weapons).Select(w => w.Id).ToList();
            return LiveProperties.Settle(new Interaction(s, props, Party.Of(s, squad.Id, ids, weapons), Party.Whole(s, target.Id), AttackKind.Melee, GfNames.Charge, 1, [])).Parameter(Side.Attacker, "AP");
        }
        Assert.Null(Ap(squad.Models.Take(1)));                                                        // the Captain's CCW
        Assert.True(Ap(squad.Models.Skip(1)) > 0);
    }

    [Fact]
    public void A_precision_token_on_a_heros_rule_does_not_reach_the_squad()
    {
        var squad = Joined(Unit("Squad", "4+", "4+", [], 3, [], Melee("CCW", 1)), [P("Precision Frenzy")]);
        var target = Unit("T", "4+", "4+", [], 1, []);
        var s = State([squad], [target]).Under(RuleSets.Gf360);
        var props = TestProperties.Core36;
        Apply(s, GfMarkers.Place(props.Entry("Precision Frenzy")!, s.Projection.Units[squad.Id], squad.Id, 1));
        int Hit(IEnumerable<ModelSlot> models)
        {
            var ids = models.Select(m => m.Id).ToList();
            var weapons = models.SelectMany(m => m.Weapons).Select(w => w.Id).ToList();
            var live = LiveProperties.Settle(new Interaction(s, props, Party.Of(s, squad.Id, ids, weapons), Party.Whole(s, target.Id), AttackKind.Melee, GfNames.Charge, 1, []));
            return live.Effects(Side.Attacker, EffectKind.ModifyRoll).Where(x => x.Effect.TargetRoll == GfNames.Hit).Sum(x => x.Effect.EffectiveAmount(x.Source.Parameter));
        }
        Assert.Equal(1, Hit(squad.Models.Take(1)));
        Assert.Equal(0, Hit(squad.Models.Skip(1)));
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void A_takedown_judges_the_defenders_rules_for_the_picked_model_alone(string rules)
    {
        // 3.6.0 p.15 Takedown: "resolved as if it was a unit of [1]"; the book's example: a Hero with Stealth in a unit without it gives the attacker -1 to hit.
        var sniper = Unit("Sniper", "4+", "4+", [], 1, [], Gun("Long Rifle", "30\"", 1, P("Takedown")));
        var squad = Joined(Unit("Squad", "4+", "6+", [], 3, []), [P("Stealth")]);
        int StealthOn(Guid picked)
        {
            var sim = Sim.Gf(rules, State([sniper], [squad], x1: -6, x2: 6), 1);
            sim.Start(GfNames.AttackSequence, new SequenceContext(sniper.Id, [squad.Id], sniper.Models[0].Weapons.Select(w => w.Id).ToList(), GfNames.Hold));
            sim.Answer(Samples.Team1, new SelectedPieces([picked]));
            return sim.Steps("roll-to-hit").Single().Rolls.Single().Modifiers.Where(m => m.Source == "Stealth").Sum(m => m.Amount);
        }
        Assert.Equal(-1, StealthOn(squad.Models[0].Id));
        Assert.Equal(0, StealthOn(squad.Models[1].Id));
    }
}
