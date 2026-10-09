using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// opr-sequences.md → Dice edits: a face's decision is settled by the step after the roll, so an edit made while the frame waits at
/// the boundary between them changes the outcome. Every game here runs with the wait setting on, so each boundary is a confirm gate.
/// </summary>
public class DiceEditTests
{
    static EffectDocument Row(EffectKind kind, string? roll, string? entry, int? amount) =>
        new(EffectSubject.Self, 10, kind, roll, null, entry, null, null, null, null, amount, false, null, null, null, EffectDuration.Once, [], []);
    static readonly EntryDocument Fireball = new("Fireball", CatalogKind.Spell, false, null, 1, null, "3 hits.",
        new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 24), [Row(EffectKind.AddHit, null, null, 3) with { Duration = EffectDuration.Interaction }]);
    static readonly EntryDocument LoudBolt = new("Loud Bolt", CatalogKind.Spell, false, null, 1, null, "2 hits, roll for 6s, Surge.",
        new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 24),
        [Row(EffectKind.AddHit, GfNames.Hit, null, 2) with { Duration = EffectDuration.Interaction, Children = [Row(EffectKind.GrantProperty, null, "Surge", null) with { Duration = EffectDuration.Interaction }] }]);
    /// <summary>One spell leaving two grants: +1 to hit (read when the roll is planned) and Surge (read on the faces).</summary>
    static readonly EntryDocument Zeal = new("Zeal", CatalogKind.Spell, false, null, 1, null, "+1 to hit and Surge once.",
        new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 1, 24), [Row(EffectKind.ModifyRoll, GfNames.Hit, null, 1), Row(EffectKind.GrantProperty, null, "Surge", null) with { Ordinal = 20 }]);
    static readonly PropertySet Props = TestProperties.WithSpells(Fireball, LoudBolt, Zeal);

    static Sim Game(IReadOnlyList<Tabletop.Content.Unit> mine, IReadOnlyList<Tabletop.Content.Unit> theirs, params int[] dice) =>
        new(State(mine, theirs, x1: -4, x2: 4, waits: true), new WithSequences(GrimdarkFutureRules.Instance, WithSequences.Parent("test-parent", "w")), Props, new ScriptedDice(dice));

    /// <summary>Release the one open step-boundary gate, whoever holds it.</summary>
    static void Pass(Sim sim)
    {
        var gate = sim.State.Projection.OpenAsks.Values.Single(a => a.Ask.Question == AskQuestions.Confirm).Ask;
        sim.Answer(gate.Teams[0], new Confirmed());
    }

    static void Edit(Sim sim, string step, params int[] faces)
    {
        var record = sim.Steps(step).Last().Rolls[0];
        var frame = sim.State.Projection.Stack.Single(f => f.Steps.Any(s => s.Rolls.Any(r => r.Id == record.Id))).Id;
        Assert.IsType<Dispatch.Applied>(sim.Dispatch(new SetDice(frame, record.Id, faces.Select((f, i) => new DieValue(i, f)).ToList()), Samples.P1, Samples.Team1));
    }

    /// <summary>Pick the spell, pass the gate, spend nothing, pass the gate: the casting roll is next, then the gate before `result`.</summary>
    static Sim Cast(string spell, Tabletop.Content.Unit target, params int[] dice)
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 3)]);
        var sim = Game([wizard], [target], dice);
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 3)]);
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen($"{spell}|{wizard.Models[0].Id}"));
        Pass(sim);
        sim.Answer(Samples.Team1, new Passed());
        Pass(sim);
        Assert.Single(sim.Steps("roll"));
        Assert.Empty(sim.Steps("result"));
        return sim;
    }

    [Fact]
    public void A_cast_edited_from_a_four_to_a_one_fails_and_ends_casting()
    {
        var sim = Cast("Fireball", Unit("T", "4+", "4+", [], 3, []), 4);
        Edit(sim, "roll", 1);
        Pass(sim);
        Assert.Equal(0, sim.Steps("result").Single().Values!["passed"]);
        Assert.Empty(sim.Steps("targets"));
        Assert.DoesNotContain(sim.State.Projection.Stack, f => f.Sequence == GfNames.CastingSeq);
    }

    [Fact]
    public void A_failed_cast_edited_to_a_five_passes_and_asks_for_targets()
    {
        var target = Unit("T", "4+", "4+", [], 3, []);
        var sim = Cast("Fireball", target, 2);
        Assert.Contains(sim.State.Projection.Stack, f => f.Sequence == GfNames.CastingSeq);   // a failed roll no longer ends the frame
        Edit(sim, "roll", 5);
        Pass(sim);
        Assert.Equal(1, sim.Steps("result").Single().Values!["passed"]);
        Pass(sim);
        Assert.Contains(target.Id, Assert.IsType<PieceSelection>(sim.AskFor(Samples.Team1).Legal).Eligible);
    }

    static SequenceContext Shoot(Tabletop.Content.Unit a, Tabletop.Content.Unit t) => new(a.Id, [t.Id], a.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Hold);

    /// <summary>Start the attack and pass the gates up to the one after roll-to-hit.</summary>
    static Sim Attack(Tabletop.Content.Unit shooter, Tabletop.Content.Unit target, params int[] dice)
    {
        var sim = Game([shooter], [target], dice);
        sim.Start(GfNames.AttackSequence, Shoot(shooter, target));
        Pass(sim);   // before pick-target-model
        Pass(sim);   // before roll-to-hit
        Assert.Single(sim.Steps("roll-to-hit"));
        Assert.Empty(sim.Steps("score-hits"));
        return sim;
    }

    [Fact]
    public void A_surge_hit_die_edited_to_a_six_scores_an_extra_hit()
    {
        var shooter = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 2, P("Surge")));
        var sim = Attack(shooter, Unit("B", "4+", "6+", [], 5, []), 3, 4, 1, 1, 1);
        Edit(sim, "roll-to-hit", 6, 4);
        Pass(sim);
        var score = sim.Steps("score-hits").Single();
        Assert.Equal(1, score.Values!["extra_hits"]);
        Assert.Contains("Surge", score.Applied!);
        Assert.DoesNotContain("Surge", sim.Steps("roll-to-hit").Single().Applied ?? []);
        Pass(sim);   // before resolve; resolve-hits rolls its blocks at once
        Assert.Equal(3, sim.Steps("roll-to-block").Single().Rolls.Sum(r => r.Dice.Count));    // two hits and the surge
    }

    [Fact]
    public void A_surge_six_edited_away_scores_no_extra_hit()
    {
        var shooter = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 2, P("Surge")));
        var sim = Attack(shooter, Unit("B", "4+", "6+", [], 5, []), 6, 4, 1, 1);
        Edit(sim, "roll-to-hit", 5);
        Pass(sim);
        Assert.Equal(0, sim.Steps("score-hits").Single().Values!.GetValueOrDefault("extra_hits"));
        Assert.Null(sim.Steps("score-hits").Single().Applied);
    }

    [Fact]
    public void A_rending_hit_die_edited_to_a_six_is_granted_ap_four()
    {
        var shooter = Unit("A", "4+", "4+", [], 1, [], Gun("Saw", "24\"", 2, P("Rending")));
        var sim = Attack(shooter, Unit("B", "4+", "2+", [], 5, []), 5, 4, 5, 5);
        Edit(sim, "roll-to-hit", 6);
        Pass(sim);
        Assert.Equal(1, sim.Steps("score-hits").Single().Values!["granted_AP_4"]);
    }

    [Fact]
    public void A_spell_granting_a_modifier_and_a_per_die_rule_keeps_the_per_die_grant_for_score_hits_then_spends_both()
    {
        var shooter = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1));
        var target = Unit("B", "4+", "6+", [], 5, []);
        var sim = Game([shooter], [target], 6, 1, 1);
        foreach (var effect in Props.Entry("Zeal")!.Effects)
            sim.Append([new GrantAdded(shooter.Id, new Grant(Guid.CreateVersion7(), effect.Id, "Zeal", shooter.Id, 1))]);
        sim.Start(GfNames.AttackSequence, Shoot(shooter, target));
        Pass(sim); Pass(sim);
        Assert.Contains(sim.Steps("roll-to-hit").Single().Rolls[0].Modifiers, m => m.Source == "Zeal" && m.Amount == 1);
        Assert.Equal(2, sim.Unit(shooter.Id).Grants.Count);                                     // left for score-hits, which reads the Surge row
        Pass(sim);
        Assert.Equal(1, sim.Steps("score-hits").Single().Values!["extra_hits"]);
        Assert.Empty(sim.Unit(shooter.Id).Grants);
    }

    [Fact]
    public void A_spell_hit_die_edited_to_a_six_surges()
    {
        var target = Unit("T", "4+", "6+", [], 5, []);
        var sim = Cast("Loud Bolt", target, 5, 2, 2, 1, 1, 1);
        Pass(sim);   // result
        Pass(sim);   // before targets
        sim.Answer(Samples.Team1, new SelectedPieces([target.Id]));
        Pass(sim);   // before effects
        Assert.Single(sim.Steps("hit"));
        Edit(sim, "hit", 6);
        Pass(sim);
        Assert.Equal(1, sim.Steps("score-hits").Single().Values!["extra_hits"]);
    }

    static Sim Morale(Tabletop.Content.Unit u, int dead, params int[] dice)
    {
        var sim = Game([Unit("A", "4+", "4+", [], 1, [])], [u], dice);
        sim.Append(u.Models.Take(dead).Select(m => (EventPayload)new ModelsMoved([new ModelRelocation(m.Id, sim.Model(m.Id).Location, new OnShelf())])));
        sim.Start(GfNames.MoraleTest, new SequenceContext(u.Id, [], [], "wounds"));
        Assert.Single(sim.Steps("test"));
        return sim;
    }

    [Fact]
    public void A_passed_morale_die_edited_to_a_one_shakes_the_unit()
    {
        var u = Unit("U", "4+", "4+", [], 4, []);
        var sim = Morale(u, 2, 5);
        Edit(sim, "test", 1);
        Pass(sim);   // override: no override roll
        Pass(sim);
        Assert.Equal(0, sim.Steps("consequence").Single().Values!["passed"]);
        Assert.Contains(GfNames.Shaken, sim.Unit(u.Id).Conditions);
    }

    [Fact]
    public void A_failed_morale_die_edited_to_a_pass_rolls_no_fearless_die_and_does_nothing()
    {
        var u = Unit("U", "4+", "4+", [], 4, [P("Fearless")]);
        var sim = Morale(u, 2, 2, 6);
        Edit(sim, "test", 4);
        Pass(sim);
        Assert.Empty(sim.Steps("override").Single().Rolls);
        Pass(sim);
        Assert.Equal(1, sim.Steps("consequence").Single().Values!["passed"]);
        Assert.DoesNotContain(GfNames.Shaken, sim.Unit(u.Id).Conditions);
    }

    [Fact]
    public void A_failed_fearless_die_edited_to_a_four_passes_the_test()
    {
        var u = Unit("U", "4+", "4+", [], 4, [P("Fearless")]);
        var sim = Morale(u, 2, 1, 2);
        Pass(sim);
        Assert.Equal("Fearless", sim.Steps("override").Single().Rolls.Single().Roll);
        Edit(sim, "override", 4);
        Pass(sim);
        Assert.Equal(1, sim.Steps("consequence").Single().Values!["passed"]);
        Assert.DoesNotContain(GfNames.Shaken, sim.Unit(u.Id).Conditions);
    }
}
