using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>Batch 2's pick entries that act on their target when used (army-book rules spec → the pick): a heal, a morale test, dice that deal hits.</summary>
public class GfPickActivesTests
{
    static Sim Holding(Tabletop.Content.Unit actor, IReadOnlyList<Tabletop.Content.Unit> enemies, int[] dice, double apart = 12, IReadOnlyList<Tabletop.Content.Unit>? friends = null)
    {
        var sim = new Sim(State([actor, .. friends ?? []], enemies, x1: -apart / 2, x2: apart / 2), GrimdarkFutureRules.Instance, TestProperties.Core, new ScriptedDice(dice));
        sim.Start(GfNames.HoldSeq, new SequenceContext(actor.Id, [], [], GfNames.Hold));
        return sim;
    }

    static IReadOnlyList<string> Offered(Sim sim) =>
        sim.AskForOrNull(Samples.Team1) is { Question: AskQuestions.React, Legal: Options o } ? o.Choices.Select(c => c.Id).ToList() : [];

    [Fact]
    public void Mend_picks_a_friendly_model_with_tough_within_3_and_removes_d3_wounds()
    {
        var a = Unit("A", "4+", "4+", [], 2, [P("Tough", 3)]);
        a = a with { Models = [a.Models[0] with { Properties = [P("Tough", 3), P("Mend")] }, a.Models[1]] };
        var plain = Unit("C", "4+", "4+", [], 1, []);
        var sim = Holding(a, [Unit("E", "4+", "4+", [], 1, [])], [4, 4, 4, 4], friends: [plain]);
        sim.Append([new WoundsSet(a.Models[1].Id, 2)]);
        sim.Answer(Samples.Team1, new Chosen(GfPick.Key("Mend", a.Models[0].Id)));
        var ask = sim.AskFor(Samples.Team1);
        Assert.Equal(new[] { a.Models[0].Id, a.Models[1].Id }.Order(), Assert.IsType<PieceSelection>(ask.Legal).Eligible.Order());   // models with Tough within 3"
        sim.Answer(Samples.Team1, new SelectedPieces([a.Models[1].Id]));
        Assert.Equal(0, sim.Model(a.Models[1].Id).Wounds);                                // D3 on a 4 is 2 (p.4: halved, rounding up)
        Assert.Contains("Mend", sim.Steps(GfNames.RuleEffectsStep).Single(s => s.Applied is not null).Applied!);
    }

    [Fact]
    public void Fatigue_debuff_tests_the_picked_enemy_and_fatigues_it_on_a_failure()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Fatigue Debuff")]); var e = Unit("E", "4+", "4+", [], 1, []);
        var sim = Holding(a, [e], [1, 1, 1, 1]);
        sim.Answer(Samples.Team1, new Chosen(GfPick.Key("Fatigue Debuff", a.Models[0].Id)));
        sim.Answer(Samples.Team1, new SelectedPieces([e.Id]));
        Assert.Contains(GfNames.Fatigued, sim.Unit(e.Id).Conditions);
        Assert.Equal(1, GfPick.Uses(sim.State, a.Id, GfPick.Key("Fatigue Debuff", a.Models[0].Id)));
    }

    [Fact]
    public void A_storm_rolls_three_dice_and_each_2_plus_deals_three_hits_to_a_unit_picked_for_it_once_per_game()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Storm of War")]);
        var e = Unit("E", "4+", "4+", [], 5, []);
        var sim = Holding(a, [e], [2, 1, 5, .. Enumerable.Repeat(6, 20)], apart: 10);
        var key = GfPick.Key("Storm of War", a.Models[0].Id);
        sim.Answer(Samples.Team1, new Chosen(key));
        var first = sim.AskFor(Samples.Team1);
        Assert.Equal(GfNames.RuleTarget, first.Question);
        Assert.Contains("1 of 2", first.Prompt);                                           // two dice of three scored 2+
        sim.Answer(Samples.Team1, new SelectedPieces([e.Id]));
        Assert.Contains("2 of 2", sim.AskFor(Samples.Team1).Prompt);
        sim.Answer(Samples.Team1, new SelectedPieces([e.Id]));                            // the same unit again: "for each 2+ one enemy unit"
        var blocks = sim.Emitted.OfType<StepCompleted>().SelectMany(s => s.Rolls).Where(r => r.Roll == GfNames.DefenseRoll).ToList();
        Assert.Equal(6, blocks.Sum(r => r.Dice.Count));                                   // 3 hits per success
        Assert.All(blocks, r => Assert.Contains(r.Modifiers, m => m.Source == "AP" && m.Amount == -1));
        Assert.Equal(1, GfPick.GameUses(sim.State, key));
        Assert.DoesNotContain(GfPick.Offers(sim.State, TestProperties.Core, a.Id), o => o.Entry.Name == "Storm of War");
    }

    [Fact]
    public void A_storm_whose_dice_all_fail_picks_nothing_and_is_used()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Storm of Change")]); var e = Unit("E", "4+", "4+", [], 1, []);
        var sim = Holding(a, [e], [1, 1, 1, 4, 4, 4], apart: 10);
        var key = GfPick.Key("Storm of Change", a.Models[0].Id);
        sim.Answer(Samples.Team1, new Chosen(key));
        Assert.NotEqual(GfNames.RuleTarget, sim.AskForOrNull(Samples.Team1)?.Question);
        Assert.Equal(1, GfPick.GameUses(sim.State, key));
    }

    [Fact]
    public void Casting_debuff_offers_only_enemy_units_with_a_caster()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Casting Debuff")]);
        var caster = Unit("C", "4+", "4+", [], 1, [P("Caster", 1)]); var plain = Unit("E", "4+", "4+", [], 1, []);
        var sim = Holding(a, [caster, plain], [4, 4]);
        sim.Answer(Samples.Team1, new Chosen(GfPick.Key("Casting Debuff", a.Models[0].Id)));
        Assert.Equal([caster.Id], Assert.IsType<PieceSelection>(sim.AskFor(Samples.Team1).Legal).Eligible);
    }

    [Fact]
    public void A_pick_entry_held_as_a_unit_property_is_offered_by_every_living_model()
    {
        var a = Unit("A", "4+", "4+", [P("Courage Buff")], 2, []);
        var sim = Holding(a, [Unit("E", "4+", "4+", [], 1, [])], [4]);
        Assert.Equal(a.Models.Select(m => GfPick.Key("Courage Buff", m.Id)).Order(), Offered(sim).Order());
    }
}
