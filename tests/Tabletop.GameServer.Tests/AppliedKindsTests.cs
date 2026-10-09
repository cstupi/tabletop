using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>StepCompleted.Applied, one test per effect kind: the entry whose row changed the step's result is listed; a step no entry changed lists none;
/// an entry present whose condition fails is not listed.</summary>
public class AppliedKindsTests
{
    internal static IReadOnlyList<string> Applied(Sim sim, string step) => sim.Steps(step).SelectMany(s => s.Applied ?? []).ToList();
    internal static void NoneApplied(Sim sim) => Assert.All(sim.Emitted.OfType<StepCompleted>(), s => Assert.True(s.Applied is null, $"{s.Step}: {string.Join(", ", s.Applied ?? [])}"));
    static SequenceContext Attack(Tabletop.Content.Unit a, Tabletop.Content.Unit t) => new(a.Id, [t.Id], a.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Hold);

    static Sim Shoot(Tabletop.Content.Unit a, Tabletop.Content.Unit t, double x, PropertySet? props = null, params int[] dice)
    {
        var sim = new Sim(State([a], [t], x1: -x, x2: x), GrimdarkFutureRules.Instance, props ?? TestProperties.Core, new ScriptedDice(dice));
        return sim;
    }

    static void GrantOn(Sim sim, PropertySet props, Guid unit, string spell) =>
        sim.Append([new GrantAdded(unit, new Grant(Guid.CreateVersion7(), props.Entry(spell)!.Effects[0].Id, spell, unit, 1))]);

    static EffectDocument Row(EffectKind kind, EffectSubject subject, EffectDuration duration, int? amount = null, string? entry = null, TargetTerrain? terrain = null, string? condition = null, string? roll = null, ConditionDocument[]? when = null, params EffectDocument[] children) =>
        new(subject, 10, kind, roll, null, entry, null, null, null, null, amount, false, null, null, null, duration, when ?? [], children, terrain, condition);
    static EntryDocument Spell(string name, TargetSide side, params EffectDocument[] rows) => new(name, CatalogKind.Spell, false, null, 1, null, $"{name}.", new SpellTargeting(side, TargetGrain.Unit, 1, 24), rows);
    static EntryDocument Rule(string name, params EffectDocument[] rows) => new(name, CatalogKind.Model, false, Aggregation.All, null, null, $"{name}.", null, rows);

    static readonly PropertySet Kinds = TestProperties.WithSpells(
        Spell("Sharpen", TargetSide.Friendly, Row(EffectKind.GrantProperty, EffectSubject.Self, EffectDuration.Once, amount: 1, entry: "AP")),
        Spell("Spikes", TargetSide.Enemy, Row(EffectKind.CountAsTerrain, EffectSubject.Self, EffectDuration.Once, terrain: TargetTerrain.Dangerous)),
        Spell("Fury", TargetSide.Enemy, Row(EffectKind.TakeTest, EffectSubject.Self, EffectDuration.Interaction, roll: "morale",
            children: [Row(EffectKind.SetCondition, EffectSubject.Self, EffectDuration.Interaction, condition: "fatigued")])),
        Spell("Hypnosis", TargetSide.Enemy, Row(EffectKind.TakeTest, EffectSubject.Self, EffectDuration.Interaction, roll: "morale",
            children: [Row(EffectKind.Move, EffectSubject.Self, EffectDuration.Interaction, amount: 6)])),
        Rule("Dampen", Row(EffectKind.ModifyParameter, EffectSubject.Opponent, EffectDuration.Interaction, amount: -1, entry: "AP")),
        Rule("Hop", Row(EffectKind.Move, EffectSubject.Self, EffectDuration.Interaction, amount: 3,
            when: [new ConditionDocument(ConditionKind.AtPoint, null, Comparison.Eq, null, [GfNames.ActivationStart])])));

    [Fact]
    public void Modify_roll_lists_stealth_beyond_nine_inches_and_nothing_within()
    {
        var far = Shoot(Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)), Unit("B", "4+", "4+", [], 1, [P("Stealth")]), 6, null, 4);
        far.Start(GfNames.AttackSequence, Attack(UnitOf(far, 0), UnitOf(far, 1)));
        Assert.Equal(["Stealth"], Applied(far, "roll-to-hit"));

        var near = Shoot(Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)), Unit("B", "4+", "4+", [], 1, [P("Stealth")]), 3, null, 4, 4);
        near.Start(GfNames.AttackSequence, Attack(UnitOf(near, 0), UnitOf(near, 1)));
        Assert.Single(near.Steps("roll-to-block"));
        NoneApplied(near);
    }

    [Fact]
    public void Modify_stat_lists_fast_on_the_move_it_lengthened()
    {
        foreach (var fast in new[] { true, false })
        {
            var a = Unit("A", "4+", "4+", [], 1, fast ? [P("Fast")] : []);
            var sim = new Sim(State([a], [Unit("B", "4+", "4+", [], 1, [])], x1: -20, x2: 20), GrimdarkFutureRules.Instance, TestProperties.Core, new ScriptedDice(4));
            sim.Start(GfNames.AdvanceSeq, new SequenceContext(a.Id, [], [], GfNames.Advance));
            Shift(sim, a.Models[0].Id, 5);
            if (fast) Assert.Contains("Fast", Applied(sim, "move")); else NoneApplied(sim);
        }
    }

    [Fact]
    public void Replace_stat_lists_reliable_where_the_attacks_are_counted_at_its_quality()
    {
        var sim = Shoot(Unit("A", "5+", "4+", [], 1, [], Gun("Rifle", "24\"", 1, P("Reliable"))), Unit("B", "4+", "4+", [], 1, []), 3, null, 2, 6);
        sim.Start(GfNames.AttackSequence, Attack(UnitOf(sim, 0), UnitOf(sim, 1)));
        Assert.Equal(2, sim.Steps("roll-to-hit").Single().Rolls.Single().Threshold);
        Assert.Contains("Reliable", Applied(sim, "determine-attacks"));
    }

    [Fact]
    public void Force_reroll_lists_bane_only_when_a_six_was_rerolled()
    {
        var six = Shoot(Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1, P("Bane"))), Unit("B", "4+", "4+", [], 1, []), 3, null, 5, 6, 1);
        six.Start(GfNames.AttackSequence, Attack(UnitOf(six, 0), UnitOf(six, 1)));
        Assert.Equal(["Bane"], Applied(six, "roll-to-block"));

        var five = Shoot(Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1, P("Bane"))), Unit("B", "4+", "4+", [], 1, []), 3, null, 5, 5);
        five.Start(GfNames.AttackSequence, Attack(UnitOf(five, 0), UnitOf(five, 1)));
        NoneApplied(five);
    }

    [Fact]
    public void Roll_and_override_result_list_regeneration_and_suppress_property_lists_bane_in_its_place()
    {
        var regen = Shoot(Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)), Unit("B", "4+", "4+", [], 1, [P("Regeneration")]), 3, null, 5, 1, 6);
        regen.Start(GfNames.AttackSequence, Attack(UnitOf(regen, 0), UnitOf(regen, 1)));
        Assert.Equal(["Regeneration"], Applied(regen, "assign"));

        var bane = Shoot(Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1, P("Bane"))), Unit("B", "4+", "4+", [], 1, [P("Regeneration")]), 3, null, 5, 1);
        bane.Start(GfNames.AttackSequence, Attack(UnitOf(bane, 0), UnitOf(bane, 1)));
        Assert.Equal(["Bane"], Applied(bane, "assign"));
        Assert.Empty(Applied(bane, "roll-to-block"));                                                     // the defense die was no six
    }

    [Fact]
    public void Add_hit_lists_surge_on_a_six_and_nothing_without_one()
    {
        var six = Shoot(Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1, P("Surge"))), Unit("B", "4+", "4+", [], 2, []), 3, null, 6, 6, 6);
        six.Start(GfNames.AttackSequence, Attack(UnitOf(six, 0), UnitOf(six, 1)));
        Assert.Equal(["Surge"], Applied(six, "roll-to-hit"));

        var five = Shoot(Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1, P("Surge"))), Unit("B", "4+", "4+", [], 2, []), 3, null, 5, 6);
        five.Start(GfNames.AttackSequence, Attack(UnitOf(five, 0), UnitOf(five, 1)));
        NoneApplied(five);
    }

    [Fact]
    public void Multiply_lists_deadly_on_the_wound_and_blast_only_when_the_cap_leaves_it_above_one()
    {
        var deadly = Shoot(Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1, P("Deadly", 3))), Unit("B", "4+", "4+", [], 1, [P("Tough", 3)]), 3, null, 5, 1);
        deadly.Start(GfNames.AttackSequence, Attack(UnitOf(deadly, 0), UnitOf(deadly, 1)));
        Assert.Contains("Deadly", Applied(deadly, "assign"));

        var pair = Shoot(Unit("A", "4+", "4+", [], 1, [], Gun("Mortar", "24\"", 1, P("Blast", 3))), Unit("B", "4+", "4+", [], 2, []), 3, null, 5, 6, 6);
        pair.Start(GfNames.AttackSequence, Attack(UnitOf(pair, 0), UnitOf(pair, 1)));
        Assert.Equal(["Blast"], Applied(pair, "roll-to-hit"));

        var single = Shoot(Unit("A", "4+", "4+", [], 1, [], Gun("Mortar", "24\"", 1, P("Blast", 3))), Unit("B", "4+", "4+", [], 1, []), 3, null, 5, 6);
        single.Start(GfNames.AttackSequence, Attack(UnitOf(single, 0), UnitOf(single, 1)));
        NoneApplied(single);
    }

    [Fact]
    public void Grant_property_lists_rending_on_the_six_and_the_ap_it_granted_on_the_block()
    {
        var sim = Shoot(Unit("A", "4+", "4+", [], 1, [], Gun("Saw", "24\"", 1, P("Rending"))), Unit("B", "4+", "2+", [], 1, []), 3, null, 6, 5);
        sim.Start(GfNames.AttackSequence, Attack(UnitOf(sim, 0), UnitOf(sim, 1)));
        Assert.Equal(["Rending"], Applied(sim, "roll-to-hit"));
        Assert.Equal(["AP"], Applied(sim, "roll-to-block"));
    }

    [Fact]
    public void A_pass_one_grant_lists_the_granted_rule_and_the_spell_that_granted_it()
    {
        var sim = Shoot(Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)), Unit("B", "4+", "4+", [], 1, []), 3, Kinds, 5, 4);
        GrantOn(sim, Kinds, UnitOf(sim, 0).Id, "Sharpen");
        sim.Start(GfNames.AttackSequence, Attack(UnitOf(sim, 0), UnitOf(sim, 1)));
        Assert.Equal(["AP", "Sharpen"], Applied(sim, "roll-to-block"));
    }

    [Fact]
    public void Modify_parameter_lists_the_rule_that_changed_the_parameter_with_the_rule_it_changed()
    {
        var sim = Shoot(Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1, P("AP", 2))), Unit("B", "4+", "4+", [], 1, [P("Dampen")]), 3, Kinds, 5, 3);
        sim.Start(GfNames.AttackSequence, Attack(UnitOf(sim, 0), UnitOf(sim, 1)));
        Assert.Equal(-1, sim.Steps("roll-to-block").Single().Rolls.Single().Modifiers.Single().Amount);
        Assert.Equal(["AP", "Dampen"], Applied(sim, "roll-to-block"));
    }

    [Fact]
    public void Suppress_modifiers_lists_unstoppable_and_not_the_stealth_it_removed()
    {
        var sim = Shoot(Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1, P("Unstoppable"))), Unit("B", "4+", "4+", [], 1, [P("Stealth")]), 6, null, 4, 6);
        sim.Start(GfNames.AttackSequence, Attack(UnitOf(sim, 0), UnitOf(sim, 1)));
        Assert.Equal(["Unstoppable"], Applied(sim, "roll-to-hit"));
    }

    [Fact]
    public void Count_as_terrain_lists_the_spell_at_the_test_it_caused()
    {
        var a = Unit("A", "4+", "4+", [], 3, []);
        var sim = new Sim(State([a], [Unit("B", "4+", "4+", [], 1, [])]), GrimdarkFutureRules.Instance, Kinds, new ScriptedDice(6, 6, 6));
        GrantOn(sim, Kinds, a.Id, "Spikes");
        sim.Start(GfNames.HoldSeq, new SequenceContext(a.Id, [], [], GfNames.Hold));
        Assert.Equal(["Spikes"], Applied(sim, "activation-terrain"));
    }

    [Fact]
    public void Take_test_and_set_condition_list_the_spell_when_the_test_fails()
    {
        foreach (var (die, failed) in new[] { (1, true), (6, false) })
        {
            var sim = Cast("Fury", 6, die);
            Assert.Contains("Fury", Applied(sim, "effects"));
            Assert.Equal(failed ? ["Fury"] : [], Applied(sim, "consequence"));
        }
    }

    [Fact]
    public void A_forced_move_lists_the_spell_that_moved_the_target()
    {
        var sim = Cast("Hypnosis", 6, 1);
        var target = sim.State.Board.Armies[1].Snapshot.Units[0].Models[0].Id;
        Shift(sim, target, 3);
        Assert.Equal(["Hypnosis"], Applied(sim, "consequence"));
    }

    [Fact]
    public void Move_and_place_rows_list_their_rule_on_the_reposition()
    {
        var hop = Unit("A", "4+", "4+", [], 1, [P("Hop")]);
        var sim = new Sim(State([hop], [Unit("B", "4+", "4+", [], 1, [])], x1: -20, x2: 20), GrimdarkFutureRules.Instance, Kinds, new ScriptedDice(4));
        sim.Start(GfNames.HoldSeq, new SequenceContext(hop.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Chosen($"Hop|{hop.Id:N}"));
        Shift(sim, hop.Models[0].Id, 2);
        Assert.Equal(["Hop"], Applied(sim, "reposition"));

        var tele = Unit("A", "4+", "4+", [], 1, [P("Teleport")]);
        var sim2 = new Sim(State([tele], [Unit("B", "4+", "4+", [], 1, [])], x1: -20, x2: 20), GrimdarkFutureRules.Instance, TestProperties.Core, new ScriptedDice(4));
        sim2.Start(GfNames.AdvanceSeq, new SequenceContext(tele.Id, [], [], GfNames.Advance));
        sim2.Answer(Samples.Team1, new Passed());
        sim2.Answer(Samples.Team1, new Chosen($"Teleport|{tele.Id:N}"));
        Shift(sim2, tele.Models[0].Id, 2.5);
        Assert.Equal(["Teleport"], Applied(sim2, "reposition"));
    }

    static Sim Cast(string spell, params int[] dice)
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 6)]); var t = Unit("T", "4+", "4+", [], 2, []);
        var sim = new Sim(State([wizard], [t], x1: -4, x2: 4), new WithSequences(GrimdarkFutureRules.Instance, WithSequences.Parent("test-parent", "w")), Kinds, new ScriptedDice(dice));
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 6)]);
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen($"{spell}|{wizard.Models[0].Id}"));
        sim.Answer(Samples.Team1, new Passed());
        sim.Answer(Samples.Team1, new SelectedPieces([t.Id]));
        return sim;
    }

    internal static Tabletop.Content.Unit UnitOf(Sim sim, int army) => sim.State.Board.Armies[army].Snapshot.Units[0];

    internal static void Shift(Sim sim, Guid model, double dx)
    {
        var at = ((OnTable)sim.Model(model).Location).Placement;
        var team = sim.State.Projection.OpenAsks.Values.Single(a => a.Ask.Question == "move").Ask.Teams[0];
        sim.Answer(team, new Destinations([new ModelRelocation(model, sim.Model(model).Location, new OnTable(at with { Position = at.Position with { X = at.Position.X + dx } }))]));
    }
}
