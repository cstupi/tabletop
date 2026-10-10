using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class GfPickTests
{
    static readonly ConditionDocument BeforeAttacking = new(ConditionKind.AtPoint, null, Comparison.Eq, null, [GfNames.BeforeAttacking]);
    static EffectDocument Grant(string entry, EffectSubject subject) =>
        new(subject, 5, EffectKind.GrantProperty, null, null, entry, null, null, null, null, null, false, null, null, null, EffectDuration.Once, [BeforeAttacking], []);
    static readonly EntryDocument TestBuff = new("Test Buff", CatalogKind.Model, false, Aggregation.None, null, null, "Test buff.",
        new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 1, 12, LineOfSight: false), [Grant("Stealth", EffectSubject.Self)]);
    static readonly EntryDocument TestMark = new("Test Mark", CatalogKind.Model, false, Aggregation.None, null, null, "Test mark.",
        new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 18), [Grant("Furious", EffectSubject.Opponent)]);
    static readonly PropertySet Props = TestProperties.WithSpells(TestBuff, TestMark);

    /// <summary>Team 1's units at x = -12 (unit i at y = 12i - 20 + 1.5 per model), team 2's at x = +12: 24" apart, so nothing of team 2 is within 18".</summary>
    static Sim Holding(Tabletop.Content.Unit actor, IReadOnlyList<Tabletop.Content.Unit> friends, IReadOnlyList<Tabletop.Content.Unit> enemies)
    {
        var sim = new Sim(State([actor, .. friends], enemies), GrimdarkFutureRules.Instance, Props, new Dice.ScriptedDice([4, 4, 4, 4]));
        sim.Start(GfNames.HoldSeq, new SequenceContext(actor.Id, [], [], GfNames.Hold));
        return sim;
    }

    static IReadOnlyList<string> Offered(Sim sim) =>
        sim.AskForOrNull(Samples.Team1) is { Question: AskQuestions.React, Legal: Options o } ? o.Choices.Select(c => c.Id).ToList() : [];

    [Fact]
    public void Before_attacking_offers_a_use_per_carrying_model()
    {
        var a = Unit("A", "4+", "4+", [], 2, [P("Test Buff")]);
        var sim = Holding(a, [], [Unit("E", "4+", "4+", [], 1, [])]);
        Assert.Equal(new[] { GfPick.Key("Test Buff", a.Models[0].Id), GfPick.Key("Test Buff", a.Models[1].Id) }.Order(), Offered(sim).Order());
        Assert.Equal("Test Buff (A 1): pick a target", ((Options)sim.AskFor(Samples.Team1).Legal).Choices.First(c => c.Id == GfPick.Key("Test Buff", a.Models[0].Id)).Label);
    }

    [Fact]
    public void A_shaken_unit_is_offered_no_use()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Test Buff")]);
        var sim = new Sim(State([a], [Unit("E", "4+", "4+", [], 1, [])]), GrimdarkFutureRules.Instance, Props, new Dice.ScriptedDice([4]));
        sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, a.Id), GfNames.Shaken, true)]);
        sim.Start(GfNames.HoldSeq, new SequenceContext(a.Id, [], [], GfNames.Hold));
        Assert.Empty(Offered(sim));
    }

    [Fact]
    public void Using_it_asks_for_a_target_in_range_and_grants_each_once_row_to_the_picked_unit()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Test Buff")]); var b = Unit("B", "4+", "4+", [], 1, []);
        var sim = Holding(a, [b], [Unit("E", "4+", "4+", [], 1, [])]);
        sim.Answer(Samples.Team1, new Chosen(GfPick.Key("Test Buff", a.Models[0].Id)));
        var ask = sim.AskFor(Samples.Team1);
        Assert.Equal(GfNames.RuleTarget, ask.Question);
        Assert.True(ask.AllowPass);
        Assert.Equal(new[] { a.Id, b.Id }.Order(), Assert.IsType<PieceSelection>(ask.Legal).Eligible.Order());   // friendly units within 12", the enemy is not
        sim.Answer(Samples.Team1, new SelectedPieces([b.Id]));
        var grant = Assert.Single(sim.Unit(b.Id).Grants);
        Assert.Equal((Props.Entry("Test Buff")!.Effects.Single().Id, "Test Buff", a.Id, 1), (grant.EffectId, grant.EntryName, grant.CasterUnitId, grant.RemainingUses));
        Assert.Contains("Test Buff", sim.Steps(GfNames.RuleTargetStep).Single().Applied!);
        Assert.Equal(1, GfPick.Uses(sim.State, a.Id, GfPick.Key("Test Buff", a.Models[0].Id)));
    }

    [Fact]
    public void A_model_uses_its_pick_once_per_activation_and_another_carrier_still_may()
    {
        var a = Unit("A", "4+", "4+", [], 2, [P("Test Buff")]);
        var sim = Holding(a, [], [Unit("E", "4+", "4+", [], 1, [])]);
        sim.Answer(Samples.Team1, new Chosen(GfPick.Key("Test Buff", a.Models[0].Id)));
        sim.Answer(Samples.Team1, new SelectedPieces([a.Id]));
        Assert.Equal([GfPick.Key("Test Buff", a.Models[1].Id)], Offered(sim));
    }

    [Fact]
    public void Passing_the_target_uses_nothing_and_the_pick_is_offered_again()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Test Buff")]);
        var sim = Holding(a, [], [Unit("E", "4+", "4+", [], 1, [])]);
        var key = GfPick.Key("Test Buff", a.Models[0].Id);
        sim.Answer(Samples.Team1, new Chosen(key));
        sim.Answer(Samples.Team1, new Passed());
        Assert.Empty(sim.Unit(a.Id).Grants);
        Assert.Equal(0, GfPick.Uses(sim.State, a.Id, key));
        Assert.Equal([key], Offered(sim));
    }

    [Fact]
    public void No_use_is_offered_when_no_unit_is_in_range()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Test Mark")]);
        var sim = Holding(a, [], [Unit("E", "4+", "4+", [], 1, [])]);
        Assert.Empty(Offered(sim));
    }

    [Fact]
    public void A_target_already_holding_the_grants_gets_none_and_the_use_counts()
    {
        var a = Unit("A", "4+", "4+", [], 2, [P("Test Buff")]);
        var sim = Holding(a, [], [Unit("E", "4+", "4+", [], 1, [])]);
        foreach (var m in a.Models)
        {
            sim.Answer(Samples.Team1, new Chosen(GfPick.Key("Test Buff", m.Id)));
            sim.Answer(Samples.Team1, new SelectedPieces([a.Id]));
        }
        Assert.Single(sim.Unit(a.Id).Grants);
        Assert.Equal(1, GfPick.Uses(sim.State, a.Id, GfPick.Key("Test Buff", a.Models[1].Id)));
    }

    [Fact]
    public void A_key_splits_at_its_last_bar_so_an_entry_name_may_hold_one()
    {
        var m = Guid.CreateVersion7();
        Assert.Equal(("Hit | Run Buff", m), GfPick.Parse(GfPick.Key("Hit | Run Buff", m)));
        Assert.Null(GfPick.Parse("no model"));
    }
}
