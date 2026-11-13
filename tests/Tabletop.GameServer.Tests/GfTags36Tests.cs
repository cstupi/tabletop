using Tabletop.Content;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// 3.6 Piercing Tag: "place X tokens on one enemy unit within 24" and in line of sight, or one token on X enemy units within 24" and in line of sight. When
/// attacking, friendly units may remove one token from their target before rolling to block to get AP(+1) on their weapons" (`one_marker`,
/// `countFromParameter`); 3.5.1's "may remove markers … to get +AP(Y) … where Y is the number of removed markers" (`spent_marker`). Mind Control: "Units may
/// only be moved once per round in this way" (`oncePerRound`).
/// </summary>
public class GfTags36Tests
{
    static readonly ConditionDocument BeforeAttacking = new(ConditionKind.AtPoint, null, Comparison.Eq, null, [GfNames.BeforeAttacking]);

    static EntryDocument Tag(string name, EffectDuration duration) => new(name, CatalogKind.Model, true, Aggregation.None, null, null, name + ".",
        new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 24, CountFromParameter: duration == EffectDuration.OneMarker),
        [new EffectDocument(EffectSubject.Opponent, 10, EffectKind.GrantProperty, null, null, "AP", null, null, null, null, 1, false, null, null, null, duration, [BeforeAttacking], [])]);
    static readonly EntryDocument OneTag = Tag("Test Tag", EffectDuration.OneMarker);
    static readonly EntryDocument SpentTag = Tag("Test Spent Tag", EffectDuration.SpentMarker);
    static readonly EntryDocument Control = new("Test Control", CatalogKind.Model, false, Aggregation.None, null, null, "Units may only be moved once per round in this way.",
        new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 18, OncePerRound: true),
        [new EffectDocument(EffectSubject.Self, 10, EffectKind.TakeTest, "morale", null, null, null, null, null, null, null, false, null, null, null, EffectDuration.Interaction, [BeforeAttacking],
            [new EffectDocument(EffectSubject.Self, 20, EffectKind.Move, null, null, null, null, null, null, null, 6, false, null, null, null, EffectDuration.Interaction, [], [])])]);

    static PropertySet With(PropertySetDocument core) => PropertiesJson.ToSet(core with { Entries = [.. core.Entries, OneTag, SpentTag, Control] }, Guid.CreateVersion7);
    static readonly PropertySet Props36 = With(TestProperties.CoreDocument36);
    static readonly PropertySet Props351 = With(TestProperties.CoreDocument);

    static Sim Sim36(GameState state, params int[] dice) => new(state.Under(RuleSets.Gf360), GrimdarkFutureRules.V360, Props36, new ScriptedDice(dice));

    [Theory]
    [InlineData(RuleSets.Gf360, "Test Tag", 3, -1, 2)]          // one token acts and goes
    [InlineData(RuleSets.Gf360, "Test Spent Tag", 2, 0, 2)]     // "friendly units may remove markers … when attacking": a rule's hits remove none (ArmyRuleAuditFinishSpotterTests)
    [InlineData(RuleSets.Gf351, "Test Spent Tag", 2, 0, 2)]
    public void One_token_acts_on_an_attack_and_goes_alone_while_spent_markers_act_only_when_removed(string rules, string tag, int placed, int ap, int left)
    {
        var s = Unit("S", "4+", "4+", [], 1, []); var e = Unit("E", "4+", "4+", [], 1, []);
        var state = State([s], [e]);
        var sim = rules == RuleSets.Gf360 ? Sim36(state, Enumerable.Repeat(1, 20).ToArray())
            : new Sim(state, GrimdarkFutureRules.V351, Props351, new ScriptedDice(Enumerable.Repeat(1, 20).ToArray()));
        var props = rules == RuleSets.Gf360 ? Props36 : Props351;
        sim.Append([.. GfMarkers.Place(props.Entry(tag)!, sim.Unit(e.Id), s.Id, placed)]);
        sim.Start(GfNames.RuleHitsSeq, new SequenceContext(s.Id, [e.Id], [], "Test|1"));
        var block = Assert.Single(sim.Emitted.OfType<StepCompleted>().SelectMany(c => c.Rolls), r => r.Roll == GfNames.DefenseRoll);
        Assert.Equal(ap, block.Modifiers.Where(m => m.Source == "AP").Sum(m => m.Amount));
        Assert.Equal(left, sim.Unit(e.Id).Grants.Count(g => g.EntryName == tag));
    }

    [Theory]
    [InlineData(1, new[] { 2, 0 })]   // "place X tokens on one enemy unit"
    [InlineData(2, new[] { 1, 1 })]   // "or one token on X enemy units"
    public void A_count_from_parameter_pick_places_x_tokens_on_one_unit_or_one_on_each_of_up_to_x(int picked, int[] tokens)
    {
        var t = Unit("T", "4+", "4+", [], 1, [P(OneTag.Name, 2)]);
        var e1 = Unit("E1", "4+", "4+", [], 1, []); var e2 = Unit("E2", "4+", "4+", [], 1, []);
        var sim = Sim36(State([t], [e1, e2], x1: -6, x2: 6), 4, 4, 4);
        sim.Start(GfNames.HoldSeq, new SequenceContext(t.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Chosen(GfPick.Key(OneTag.Name, t.Models[0].Id)));
        var pick = Assert.IsType<PieceSelection>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal((1, 2), (pick.Min, pick.Max));
        sim.Answer(Samples.Team1, new SelectedPieces(new[] { e1.Id, e2.Id }.Take(picked).ToList()));
        Assert.Equal(tokens, new[] { e1, e2 }.Select(u => sim.Unit(u.Id).Grants.Count(g => g.EntryName == OneTag.Name)));
    }

    static ModelRelocation Shifted(Sim sim, Guid model, double dx)
    {
        var at = ((OnTable)sim.Model(model).Location).Placement;
        return new ModelRelocation(model, sim.Model(model).Location, new OnTable(at with { Position = at.Position with { X = at.Position.X + dx } }));
    }

    [Theory]
    [InlineData(1, false)]   // failed and moved: no target again this round
    [InlineData(6, true)]    // passed: "a unit picked whose test passed was not moved and stays eligible"
    public void A_once_per_round_pick_leaves_out_a_unit_a_use_of_the_entry_moved_this_round(int moraleDie, bool stillOffered)
    {
        var a1 = Unit("A1", "4+", "4+", [], 1, [P(Control.Name)]); var a2 = Unit("A2", "4+", "4+", [], 1, [P(Control.Name)]);
        var e = Unit("E", "4+", "4+", [], 1, []); var f = Unit("F", "4+", "4+", [], 1, []);
        var sim = Sim36(State([a1, a2], [e, f]), moraleDie, moraleDie, 4, 4);
        foreach (var (u, x, y) in new[] { (a1, 20.0, 20.0), (a2, 20.0, 30.0), (e, 30.0, 20.0), (f, 30.0, 30.0) })
        {
            var m = u.Models[0].Id;
            sim.Append([new ModelsMoved([new ModelRelocation(m, sim.Model(m).Location, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))))])]);
        }
        sim.Start(GfNames.HoldSeq, new SequenceContext(a1.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Chosen(GfPick.Key(Control.Name, a1.Models[0].Id)));
        sim.Answer(Samples.Team1, new SelectedPieces([e.Id]));
        if (!stillOffered) sim.Answer(Samples.Team1, new Destinations([Shifted(sim, e.Models[0].Id, 2)]));
        var other = Props36.Entry(Control.Name)!;
        var targets = GfCasting.Targets(sim.State, a2.Models[0].Id, other.Targeting!, entry: other.Name);
        Assert.Equal(stillOffered, targets.Contains(e.Id));
        Assert.Contains(f.Id, targets);
        sim.Append([new RoundStarted(2, [], [])]);
        Assert.Contains(e.Id, GfCasting.Targets(sim.State, a2.Models[0].Id, other.Targeting!, entry: other.Name));   // the next round
    }
}
