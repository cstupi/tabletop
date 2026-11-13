using Tabletop.Content;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>Batch 4 (army-book rules spec → Authored rules 4): rules that act when a unit activates, at its activation's end, or on who may activate.</summary>
public class Batch4ActivationTests
{
    static readonly PropertySet Core = TestProperties.Core;

    static Sim Acting(Tabletop.Content.Unit actor, IReadOnlyList<Tabletop.Content.Unit> enemies, string sequence = GfNames.HoldSeq, string action = GfNames.Hold,
        int[]? dice = null, double apart = 24, IReadOnlyList<Tabletop.Content.Unit>? friends = null)
    {
        var sim = new Sim(State([actor, .. friends ?? []], enemies, x1: -apart / 2, x2: apart / 2), GrimdarkFutureRules.Instance, Core, new ScriptedDice(dice ?? [4, 4, 4, 4, 4, 4]));
        sim.Append([new ActionRecorded(actor.Id, action)]);
        sim.Start(sequence, new SequenceContext(actor.Id, [], [], action));
        return sim;
    }

    static IReadOnlyList<AskOption> Offered(Sim sim) =>
        sim.AskForOrNull(Samples.Team1) is { Question: AskQuestions.React, Legal: Options o } ? o.Choices : [];

    [Fact]
    public void Bounding_is_offered_as_the_unit_activates_and_places_within_d3_plus_1()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Bounding")]);
        var sim = Acting(a, [Unit("E", "4+", "4+", [], 1, [])]);
        var use = Assert.Single(Offered(sim));
        Assert.Equal("Bounding: place within D3+1\"", use.Label);
        sim.Answer(Samples.Team1, new Chosen(use.Id));
        Assert.Equal(3.0, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);   // a 4 is D3 2, plus 1
    }

    [Fact]
    public void Dash_opens_the_activation_end_window_once_per_round()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Dash")]);
        var state = State([a], [Unit("E", "4+", "4+", [], 1, [])]);
        var act = new Activation(a.Id, GfNames.Hold, true, []);
        Assert.Contains(GfActivationEnd.Sequences(state, Core, act), s => s.Sequence == GfNames.ActivationEndSeq);
        var sim = new Sim(state, GrimdarkFutureRules.Instance, Core, new ScriptedDice([2, 2]));
        sim.Start(GfNames.ActivationEndSeq, new SequenceContext(a.Id, [], [], GfNames.Hold));
        Assert.Equal("Dash: place within D3+1\"", Assert.Single(Offered(sim)).Label);
        Assert.Empty(GfActivationEnd.Sequences(state, Core, new Activation(a.Id, GfNames.Delay, false, [])));
    }

    [Fact]
    public void A_choice_sets_one_condition_its_rows_read_and_the_next_activation_clears_it()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Versatile Attack")], Gun("Gun", "24\"", 1));
        var e = Unit("E", "4+", "4+", [], 1, []);
        var sim = Acting(a, [e]);
        var use = Assert.Single(Offered(sim));
        Assert.Equal("Versatile Attack: pick an effect", use.Label);
        sim.Answer(Samples.Team1, new Chosen(use.Id));
        var ask = sim.AskFor(Samples.Team1);
        Assert.Equal(new[] { "Versatile Attack: AP(+1)", "Versatile Attack: +1 to hit" }, ((Options)ask.Legal).Choices.Select(c => c.Id));
        sim.Answer(Samples.Team1, new Chosen("Versatile Attack: AP(+1)"));
        Assert.Contains("Versatile Attack: AP(+1)", sim.Unit(a.Id).Conditions);
        var live = LiveProperties.Settle(new Interaction(sim.State, Core, Party.Whole(sim.State, a.Id), Party.Whole(sim.State, e.Id), AttackKind.Shooting, GfNames.Hold, 20, []));
        Assert.Equal(1, live.Parameter(Side.Attacker, "AP"));
        Assert.Null(LiveProperties.Settle(new Interaction(sim.State, Core, Party.Whole(sim.State, a.Id), Party.Whole(sim.State, e.Id), AttackKind.Shooting, GfNames.Hold, 6, [])).Parameter(Side.Attacker, "AP"));
        Assert.Empty(Offered(sim));                                                        // once per activation
        while (sim.State.Projection.Stack.Count > 0 && sim.AskForOrNull(Samples.Team1) is { } open) sim.Answer(Samples.Team1, open.AllowPass ? new Passed() : new Chosen("pass"));
        var again = new Sim(sim.State, GrimdarkFutureRules.Instance, Core, new ScriptedDice([4]));
        again.Start(GfNames.HoldSeq, new SequenceContext(a.Id, [], [], GfNames.Hold));
        Assert.DoesNotContain("Versatile Attack: AP(+1)", again.Unit(a.Id).Conditions);
    }

    [Fact]
    public void Speed_feat_is_chosen_once_per_game_and_lengthens_the_move()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Speed Feat")]);
        var sim = Acting(a, [Unit("E", "4+", "4+", [], 1, [])], GfNames.AdvanceSeq, GfNames.Advance);
        sim.Answer(Samples.Team1, new Chosen(Assert.Single(Offered(sim)).Id));
        sim.Answer(Samples.Team1, new Chosen("Speed Feat"));
        Assert.Equal(8.0, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Allowance);
        var live = LiveProperties.Settle(new Interaction(sim.State, Core, Party.Whole(sim.State, a.Id), null, null, GfNames.Advance, null, [], GfNames.ActivationStart));
        Assert.DoesNotContain(live.AtPoint(Side.Attacker, GfNames.ActivationStart), x => x.Source.Entry.Name == "Speed Feat");
    }

    [Fact]
    public void Delayed_action_is_offered_while_the_opponent_has_more_units_left_and_passes_the_turn_unrecorded()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Delayed Action")]);
        var state = State([a], [Unit("E1", "4+", "4+", [], 1, []), Unit("E2", "4+", "4+", [], 1, [])]);
        var options = ((Options)GrimdarkFutureRules.Instance.LegalTurn(state, Core, Samples.Team1)!.Legal).Choices;
        var delay = Assert.Single(options, o => o.Id == ActivationOption.Id(a.Id, GfNames.Delay));
        var act = GrimdarkFutureRules.Instance.ReadAnswer(state, Samples.Team1, new Chosen(delay.Id))!;
        Assert.False(act.RunsSequence);
        Assert.Empty(act.Changes);
        var level = State([a], [Unit("E1", "4+", "4+", [], 1, [])]);
        Assert.DoesNotContain(((Options)GrimdarkFutureRules.Instance.LegalTurn(level, Core, Samples.Team1)!.Legal).Choices, o => o.Id.EndsWith("/" + GfNames.Delay));
    }

    [Fact]
    public void Inquisitorial_agent_may_activate_a_second_time_once_and_stops_being_fatigued()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Inquisitorial Agent")]);
        var state = State([a], [Unit("E", "4+", "4+", [], 1, [])]);
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new RoundStarted(1, [Samples.Team1, Samples.Team2], []), null));
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new ActionRecorded(a.Id, GfNames.Hold), null));
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new ConditionSet(new Subject(SubjectKind.Unit, a.Id), GfNames.Fatigued, true), null));
        var options = ((Options)GrimdarkFutureRules.Instance.LegalTurn(state, Core, Samples.Team1)!.Legal).Choices;
        var again = Assert.Single(options, o => o.Id == ActivationOption.Id(a.Id, GfNames.Hold));
        var act = GrimdarkFutureRules.Instance.ReadAnswer(state, Samples.Team1, new Chosen(again.Id))!;
        Assert.Contains(new ConditionSet(new Subject(SubjectKind.Unit, a.Id), GfNames.Fatigued, false), act.Changes);
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new ActionRecorded(a.Id, GfNames.Hold), null));
        Assert.Null(GrimdarkFutureRules.Instance.LegalTurn(state, Core, Samples.Team1));
    }

    [Fact]
    public void Instinctive_must_attack_the_closest_valid_target_with_plus_one_to_hit()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Instinctive")], Gun("Gun", "48\"", 1));
        var near = Unit("Near", "4+", "4+", [], 1, []); var far = Unit("Far", "4+", "4+", [], 1, []);
        var state = State([a], [near, far]);
        var (actions, note) = GrimdarkFutureRules.ActivationChoices(state, Core, state.Projection.Units[a.Id], ["Hold", "Advance", "Rush", "Charge"]);
        Assert.Equal(["Hold", "Advance"], actions);
        Assert.Equal(GrimdarkFutureRules.InstinctiveNote, note);
        var sim = new Sim(state, GrimdarkFutureRules.Instance, Core, new ScriptedDice(Enumerable.Repeat(4, 20).ToArray()));
        sim.Append([new ActionRecorded(a.Id, GfNames.Hold)]);
        sim.Start(GfNames.HoldSeq, new SequenceContext(a.Id, [], [], GfNames.Hold));
        Assert.Contains(GfNames.InstinctiveCondition, sim.Unit(a.Id).Conditions);
        var declare = sim.AskFor(Samples.Team1);                                            // no shoot_or_not: the shot is taken
        Assert.Equal("declare_targets", declare.Question);
        Assert.Equal([near.Id], ((Assignment)declare.Legal).Targets);   // Near (unit 1 of team 2) stands nearer than Far
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void Instinctive_in_charge_distance_may_charge_or_shoot_but_not_rush(string rules)
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Instinctive")], Gun("Gun", "48\"", 1));
        var state = State([a], [Unit("E", "4+", "4+", [], 1, [])], x1: -4, x2: 4).Under(rules);
        var props = rules == RuleSets.Gf360 ? TestProperties.Core36 : Core;
        var (actions, note) = GrimdarkFutureRules.ActivationChoices(state, props, state.Projection.Units[a.Id], ["Hold", "Advance", "Rush", "Charge"]);
        Assert.Equal(["Hold", "Advance", "Charge"], actions);
        Assert.Equal(GrimdarkFutureRules.InstinctiveNote, note);
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void Instinctive_with_only_a_charge_open_must_charge(string rules)
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Instinctive")], Melee("Blade", 1));
        var state = State([a], [Unit("E", "4+", "4+", [], 1, [])], x1: -4, x2: 4).Under(rules);
        var props = rules == RuleSets.Gf360 ? TestProperties.Core36 : Core;
        var (actions, _) = GrimdarkFutureRules.ActivationChoices(state, props, state.Projection.Units[a.Id], ["Hold", "Advance", "Rush", "Charge"]);
        Assert.Equal(["Charge"], actions);
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void Instinctive_holds_for_a_unit_a_model_without_it_has_joined_and_adds_one_to_its_carriers_hits_only(string rules)
    {
        // A joined Hero carries its own rules on its own slot: the mob's models still hold Instinctive ("When this model is activated").
        var mob = Unit("Mob", "5+", "5+", [], 3, [P("Instinctive")], Gun("Scrap Gun", "48\"", 1));
        var hero = mob.Models[^1] with { Properties = [], Weapons = [Gun("Boss Pistol", "48\"", 1)] };
        var a = mob with { Models = [.. mob.Models.Take(2), hero] };
        var e = Unit("E", "4+", "4+", [], 1, []);
        var state = State([a], [e]).Under(rules);
        var props = rules == RuleSets.Gf360 ? TestProperties.Core36 : Core;
        var (actions, note) = GrimdarkFutureRules.ActivationChoices(state, props, state.Projection.Units[a.Id], ["Hold", "Advance", "Rush", "Charge"]);
        Assert.Equal(["Hold", "Advance"], actions);
        Assert.Equal(GrimdarkFutureRules.InstinctiveNote, note);
        Assert.NotNull(GrimdarkFutureRules.InstinctiveAttack(state, props, state.Projection.Units[a.Id]));

        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new ConditionSet(new Subject(SubjectKind.Unit, a.Id), GfNames.InstinctiveCondition, true), null));
        int Bonus(Weapon w) => LiveProperties.Settle(GfAttack.AttackInteraction(state, props, null, a.Id, e.Id, [w.Id], AttackKind.Shooting, GfNames.Hold))
            .Has(Side.Attacker, GfNames.Instinctive) ? 1 : 0;
        Assert.Equal(1, Bonus(a.Models[0].Weapons[0]));
        Assert.Equal(0, Bonus(hero.Weapons[0]));
    }

    [Fact]
    public void Coordinate_activates_another_friendly_unit_within_12_that_has_not_activated()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Coordinate")]); var b = Unit("B", "4+", "4+", [], 1, []);
        var state = State([a, b], [Unit("E", "4+", "4+", [], 1, [])]);
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new ActionRecorded(a.Id, GfNames.Hold), null));
        var seqs = GfActivationEnd.Sequences(state, Core, new Activation(a.Id, GfNames.Hold, true, []));
        Assert.Equal(GfNames.CoordinateSeq, seqs[0].Sequence);
        var sim = new Sim(state, GrimdarkFutureRules.Instance, Core, new ScriptedDice([4, 4, 4]));
        sim.Start(GfNames.CoordinateSeq, seqs[0].Context);
        var ask = sim.AskFor(Samples.Team1);
        Assert.All(((Options)ask.Legal).Choices, c => Assert.StartsWith($"{b.Id:N}/", c.Id));
        sim.Answer(Samples.Team1, new Chosen(ActivationOption.Id(b.Id, GfNames.Hold)));
        Assert.Equal([GfNames.Hold], sim.Unit(b.Id).Actions);
        Assert.Contains(sim.Emitted.OfType<SequenceStarted>(), s => s.Sequence == GfNames.HoldSeq && s.Context.ActorUnitId == b.Id);
    }

    [Fact]
    public void Re_position_artillery_moves_a_friendly_artillery_model_within_6_by_up_to_9()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Re-Position Artillery")]);
        var gun = Unit("Gun", "4+", "4+", [], 1, [P("Artillery")]);
        var sim = Acting(a, [Unit("E", "4+", "4+", [], 1, [])], friends: [gun]);
        // A's model at y = -18.5, the gun's at y = -6.5: 12" apart, out of reach.
        Assert.Empty(Offered(sim));
        var near = Unit("A2", "4+", "4+", [], 2, []);
        var carrier = near with { Models = [near.Models[0] with { Properties = [P("Re-Position Artillery")] }, near.Models[1] with { Properties = [P("Artillery")] }] };
        var sim2 = Acting(carrier, [Unit("E", "4+", "4+", [], 1, [])]);
        sim2.Answer(Samples.Team1, new Chosen(Assert.Single(Offered(sim2)).Id));
        sim2.Answer(Samples.Team1, new SelectedPieces([carrier.Models[1].Id]));
        var move = sim2.AskFor(Samples.Team1);
        Assert.Equal(9.0, Assert.IsType<MoveRegion>(move.Legal).Allowance);
    }
}
