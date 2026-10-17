using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>Batch 7 (army-book rules spec → Authored rules 7): who casts and with what, a Buff's extended reach, Reanimation.</summary>
public class Batch7CastingTests
{
    static readonly PropertySet Core = TestProperties.Core;

    static void Apply(Rules.GameState state, params StateChange[] changes)
    {
        foreach (var c in changes) state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, c, null));
    }

    [Fact]
    public void A_caster_group_has_one_caster_of_x_the_number_of_its_models_and_its_tokens_pass_on()
    {
        var u = Unit("G", "4+", "4+", [], 3, [P("Caster Group")]);
        var state = State([u], [Unit("E", "4+", "4+", [], 1, [])]);
        Assert.Equal((3, (int?)null), (GfSpellSupport.CasterValue(state, u.Models[0].Id)!.Value, GfSpellSupport.CasterValue(state, u.Models[1].Id)));
        Apply(state, [.. GfSpellSupport.RoundStartTokens(state)]);
        Assert.Equal(3, state.Projection.Models[u.Models[0].Id].SpellTokens);
        Apply(state, [.. GfSpellSupport.RoundStartTokens(state)]);
        Assert.Equal(3, state.Projection.Models[u.Models[0].Id].SpellTokens);                         // unspent tokens are lost, not added to
        Apply(state, new ModelsMoved([new ModelRelocation(u.Models[0].Id, state.Projection.Models[u.Models[0].Id].Location, new OnShelf())]));
        Assert.Equal(2, GfSpellSupport.CasterValue(state, u.Models[1].Id));
    }

    [Fact]
    public void A_caster_may_pay_from_a_friendly_accumulator_of_another_unit_within_12()
    {
        var wizard = Unit("W", "4+", "4+", [], 1, [P("Caster", 1)]);
        var acc = Unit("A", "4+", "4+", [], 1, [P("Spell Accumulator", 3)]);
        var state = State([wizard, acc], [Unit("E", "4+", "4+", [], 1, [])]);
        Apply(state, [.. GfSpellSupport.RoundStartTokens(state)]);
        Assert.Equal(3, state.Projection.Models[acc.Models[0].Id].SpellTokens);
        Assert.Equal(4, GfSpellSupport.Available(state, wizard.Models[0].Id));                       // 12" apart along the row
        var paid = GfSpellSupport.Pay(state, wizard.Models[0].Id, 3);
        Assert.Equal([new SpellTokensSet(wizard.Models[0].Id, 0), new SpellTokensSet(acc.Models[0].Id, 1)], paid);
        Apply(state, new ConditionSet(new Subject(SubjectKind.Unit, acc.Id), GfNames.Shaken, true));
        Assert.Equal(1, GfSpellSupport.Available(state, wizard.Models[0].Id));                       // "only if this unit isn't Shaken"
    }

    [Fact]
    public void A_cast_from_a_conduit_is_offered_and_rolls_one_better()
    {
        var wizard = InBook(Unit("W", "4+", "4+", [], 1, [P("Caster", 3)]), "4k5amkxoybdiqotm");
        var conduit = Unit("C", "4+", "4+", [], 1, [P("Spell Conduit")]);
        var sim = new Sim(State([wizard, conduit], [Unit("E", "4+", "4+", [], 1, [])]), GrimdarkFutureRules.Instance, Core, new ScriptedDice([4, 4, 4, 4]));
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 3)]);
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        var spells = (Options)sim.AskFor(Samples.Team1).Legal;
        sim.Answer(Samples.Team1, new Chosen(spells.Choices[0].Id));
        var from = sim.AskFor(Samples.Team1);
        Assert.Equal("cast_from", from.Question);
        sim.Answer(Samples.Team1, new Chosen($"{conduit.Models[0].Id:N}"));
        if (sim.AskForOrNull(Samples.Team1) is { Question: AskQuestions.SpendTokens }) sim.Answer(Samples.Team1, new Passed());
        var roll = sim.Steps("roll").Single().Rolls.Single();
        Assert.Contains(roll.Modifiers, m => m.Source == "Spell Conduit" && m.Amount == 1);
    }

    [Fact]
    public void A_forged_cast_from_id_casts_from_the_caster_with_no_bonus()
    {
        var wizard = InBook(Unit("W", "4+", "4+", [], 1, [P("Caster", 3)]), "4k5amkxoybdiqotm");
        var conduit = Unit("C", "4+", "4+", [], 1, [P("Spell Conduit")]);
        var sim = new Sim(State([wizard, conduit], [Unit("E", "4+", "4+", [], 1, [])]), GrimdarkFutureRules.Instance, Core, new ScriptedDice([4, 4, 4, 4]));
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 3)]);
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen(((Options)sim.AskFor(Samples.Team1).Legal).Choices[0].Id));
        Assert.Equal("cast_from", sim.AskFor(Samples.Team1).Question);
        sim.Answer(Samples.Team1, new Chosen($"{Guid.CreateVersion7():N}"));                       // accepted under warn, outside the Ask
        if (sim.AskForOrNull(Samples.Team1) is { Question: AskQuestions.SpendTokens }) sim.Answer(Samples.Team1, new Passed());
        var roll = sim.Steps("roll").Single().Rolls.Single();
        Assert.DoesNotContain(roll.Modifiers, m => m.Source == "Spell Conduit");
        Assert.DoesNotContain("Spell Conduit", sim.Steps("origin").Single().Applied ?? []);
    }

    [Fact]
    public void Extended_buff_range_lets_a_hero_buff_a_unit_with_it_within_24()
    {
        var hero = Unit("H", "4+", "4+", [], 1, [P("Hero"), P("Extended Buff Range"), P("Courage Buff")]);
        var mid = Unit("M", "4+", "4+", [], 1, []);
        var far = Unit("F", "4+", "4+", [], 1, [P("Extended Buff Range")]);
        var state = State([hero, mid, far], [Unit("E", "4+", "4+", [], 1, [])]);
        var targets = GfCasting.Targets(state, hero.Models[0].Id, Core.Entry("Courage Buff")!.Targeting!);
        Assert.Contains(far.Id, targets);                                                               // 24" along the row
        Assert.DoesNotContain(far.Id, GfCasting.Targets(state, hero.Models[0].Id, Core.Entry("Courage Buff")!.Targeting! with { LineOfSight = null }));
    }

    [Fact]
    public void Reanimation_rolls_a_die_per_killed_model_and_wound_and_restores_one_per_5_plus()
    {
        var u = Unit("R", "4+", "4+", [], 3, [P("Reanimation")]);
        var state = State([u], [Unit("E", "4+", "4+", [], 1, [])]);
        Apply(state, new ModelsMoved([new ModelRelocation(u.Models[2].Id, state.Projection.Models[u.Models[2].Id].Location, new OnShelf())]));
        var sim = new Sim(state, GrimdarkFutureRules.Instance, Core, new ScriptedDice([5, 4, 4, 4]));
        sim.Append([new ActionRecorded(u.Id, GfNames.Hold)]);
        sim.Start(GfNames.HoldSeq, new SequenceContext(u.Id, [], [], GfNames.Hold));
        var roll = sim.Emitted.OfType<StepCompleted>().SelectMany(s => s.Rolls).Single(r => r.Roll == "Reanimation");
        Assert.Equal((1, 5), (roll.Dice.Count, roll.Threshold));
        Assert.IsType<OnTable>(sim.Model(u.Models[2].Id).Location);
    }
}
