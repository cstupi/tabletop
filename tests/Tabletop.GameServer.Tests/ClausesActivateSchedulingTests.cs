using Tabletop.Content;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Clause by clause, 3.6.0 army rules that decide who activates (army-rules.part.json ruleText).
///
/// Coordinate: "At the end of this unit's activation, another friendly unit within 12" that hasn't activated yet may be activated immediately. May not be
/// used if this unit was activated via Coordinate."
///   C1 at the end of this unit's activation; C2 another friendly unit (not itself, not an enemy); C3 within 12"; C4 that hasn't activated yet;
///   C5 may (declining leaves the unit unactivated); C6 activated immediately (its action runs); C7 not if this unit was activated via Coordinate;
///   mixed: a joined Hero holding it (aggregation any); a Shaken holder uses no active rule (p.10); a Shaken friendly unit hasn't activated yet.
///
/// Delayed Action: "Once per round, if your opponent has more units left to activate than you, then this model's unit may pass its turn instead of
/// activating (may still be activated later)."
///   D1 once per round (and again next round); D2 opponent has more units left (strictly more); D3 this model's unit (a joined Hero carrying it);
///   D4 may pass its turn instead of activating (no action recorded); D5 may still be activated later.
///
/// Inquisitorial Agent / Martial Prowess: "Once per game, if all models in this unit have this rule, it may be activated even if it had already activated
/// this round (stops being Fatigued when activated again). Only up to one third of the units in the army with this rule at the beginning of the game
/// (rounding up) may use it in a single round."
///   I1 once per game; I2 all models in this unit have it; I3 may activate again after activating this round; I4 stops being Fatigued;
///   I5 at most ceil(holders / 3) units per round; I6 holders counted at the beginning of the game (a destroyed holder still counts).
/// </summary>
public class ClausesActivateSchedulingTests
{
    static readonly PropertySet Core = TestProperties.Core36;
    static readonly GrimdarkFutureRules Rules = GrimdarkFutureRules.V360;

    static GameState Game(IReadOnlyList<Unit> mine, IReadOnlyList<Unit> theirs) => State(mine, theirs).Under(RuleSets.Gf360);

    static void Apply(GameState s, params EventPayload[] events)
    {
        foreach (var e in events) s.Applier.Apply(new GameEvent(s.Projection.LastSeq + 1, null, e, null));
    }

    static void Activated(GameState s, params Guid[] units) => Apply(s, [.. units.Select(u => (EventPayload)new ActionRecorded(u, GfNames.Hold))]);

    static void NewRound(GameState s, int round) =>
        Apply(s, new RoundStarted(round, [Samples.Team1, Samples.Team2], [.. s.Projection.Units.Keys.Select(u => (StateChange)new ActionRecordCleared(u))]));

    static IReadOnlyList<string> Turn(GameState s, Guid team) => Rules.LegalTurn(s, Core, team) is { Legal: Options o } ? o.Choices.Select(c => c.Id).ToList() : [];

    static Unit WithHero(Unit squad, params PropertyInstance[] heroRules) =>
        squad with { Models = [.. squad.Models.Take(squad.Models.Count - 1), squad.Models[^1] with { Properties = [P("Hero"), .. heroRules] }] };

    static Unit E(string n = "E") => Unit(n, "4+", "4+", [], 1, []);

    // ---- Coordinate ------------------------------------------------------------------------------------------------------------------

    /// <summary>Team 1's units stand 12" apart along the table: unit 0 and unit 1 within 12" base to base, unit 0 and unit 2 not.</summary>
    static (Unit A, Unit B, Unit C, GameState S) Coordinators(PropertyInstance[] aRules, PropertyInstance[] bRules)
    {
        var a = Unit("A", "4+", "4+", [], 1, aRules); var b = Unit("B", "4+", "4+", [], 1, bRules); var c = Unit("C", "4+", "4+", [], 1, []);
        return (a, b, c, Game([a, b, c], [E()]));
    }

    [Fact]
    public void Coordinate_offers_another_friendly_unit_within_12_that_has_not_activated_and_runs_its_action()
    {
        var (a, b, c, s) = Coordinators([P("Coordinate")], []);
        Activated(s, a.Id);
        var seqs = GfActivationEnd.Sequences(s, Core, new Activation(a.Id, GfNames.Hold, true, []));            // C1
        var coordinate = Assert.Single(seqs, q => q.Sequence == GfNames.CoordinateSeq);
        var sim = Sim.Gf36(s);
        sim.Start(GfNames.CoordinateSeq, coordinate.Context);
        var ask = sim.AskFor(Samples.Team1);
        Assert.True(ask.AllowPass);                                                                               // C5 "may"
        var units = ((Options)ask.Legal).Choices.Select(o => ActivationOption.TryParse(o.Id, out var u, out _) ? u : Guid.Empty).Distinct().ToList();
        Assert.Equal([b.Id], units);                                                                              // C2 not A itself, C3 not C at 24"
        sim.Answer(Samples.Team1, new Chosen(ActivationOption.Id(b.Id, GfNames.Hold)));
        Assert.Equal([GfNames.Hold], sim.Unit(b.Id).Actions);                                                    // C6
        Assert.Contains(sim.Emitted.OfType<SequenceStarted>(), q => q.Sequence == GfNames.HoldSeq && q.Context.ActorUnitId == b.Id);
        Assert.Empty(sim.Unit(c.Id).Actions);
    }

    [Fact]
    public void Coordinate_does_not_offer_a_unit_that_already_activated_this_round()
    {
        var (a, b, _, s) = Coordinators([P("Coordinate")], []);
        Activated(s, b.Id, a.Id);                                                                                 // C4
        Assert.Empty(GfActivationEnd.Coordinated(s, Core, a.Id));
        Assert.DoesNotContain(GfActivationEnd.Sequences(s, Core, new Activation(a.Id, GfNames.Hold, true, [])), q => q.Sequence == GfNames.CoordinateSeq);
    }

    [Fact]
    public void Declining_coordinate_leaves_the_other_unit_unactivated()
    {
        var (a, b, _, s) = Coordinators([P("Coordinate")], []);
        Activated(s, a.Id);
        var sim = Sim.Gf36(s);
        sim.Start(GfNames.CoordinateSeq, new SequenceContext(a.Id, [], [], GfNames.Coordinate));
        sim.Answer(Samples.Team1, new Passed());
        Assert.Empty(sim.Unit(b.Id).Actions);
        Assert.Contains(ActivationOption.Id(b.Id, GfNames.Hold), Turn(sim.State, Samples.Team1));
    }

    [Fact]
    public void A_unit_activated_via_coordinate_does_not_coordinate_in_turn()
    {
        // B holds Coordinate too, and C stands within 12" of B: activated by itself B would offer C.
        var (a, b, c, s) = Coordinators([P("Coordinate")], [P("Coordinate")]);
        Assert.Equal([c.Id], GfActivationEnd.Coordinated(s, Core, b.Id).Select(u => u.Id).Where(u => u != a.Id));
        Activated(s, a.Id);
        var sim = Sim.Gf36(s);
        sim.Start(GfNames.CoordinateSeq, new SequenceContext(a.Id, [], [], GfNames.Coordinate));
        sim.Answer(Samples.Team1, new Chosen(ActivationOption.Id(b.Id, GfNames.Hold)));
        Assert.Null(sim.AskForOrNull(Samples.Team1));                                                             // C7: no second Coordinate Ask
        Assert.Empty(sim.State.Projection.Stack);
        Assert.Single(sim.Emitted.OfType<SequenceStarted>(), q => q.Sequence == GfNames.CoordinateSeq);
        Assert.Empty(sim.Unit(c.Id).Actions);
    }

    [Fact]
    public void A_joined_hero_holding_coordinate_lets_its_unit_coordinate_and_a_shaken_holder_does_not()
    {
        var squad = WithHero(Unit("A", "4+", "4+", [], 2, []), P("Coordinate"));
        var b = Unit("B", "4+", "4+", [], 1, []);
        var s = Game([squad, b], [E()]);
        Activated(s, squad.Id);
        Assert.Equal([b.Id], GfActivationEnd.Coordinated(s, Core, squad.Id).Select(u => u.Id));
        Apply(s, new ConditionSet(new Subject(SubjectKind.Unit, squad.Id), GfNames.Shaken, true));             // p.10: no active special rules
        Assert.Empty(GfActivationEnd.Coordinated(s, Core, squad.Id));
    }

    [Fact]
    public void Coordinate_may_activate_a_shaken_friendly_unit_which_spends_it_idle()
    {
        var (a, b, _, s) = Coordinators([P("Coordinate")], []);
        Apply(s, new ConditionSet(new Subject(SubjectKind.Unit, b.Id), GfNames.Shaken, true));
        Activated(s, a.Id);
        Assert.Equal([b.Id], GfActivationEnd.Coordinated(s, Core, a.Id).Select(u => u.Id));
        var sim = Sim.Gf36(s);
        sim.Start(GfNames.CoordinateSeq, new SequenceContext(a.Id, [], [], GfNames.Coordinate));
        var ask = sim.AskFor(Samples.Team1);
        Assert.Equal([ActivationOption.Id(b.Id, ActivationOption.Idle)], ((Options)ask.Legal).Choices.Select(o => o.Id));   // p.10: idle only
        sim.Answer(Samples.Team1, new Chosen(ActivationOption.Id(b.Id, ActivationOption.Idle)));
        Assert.Contains(sim.Emitted.OfType<SequenceStarted>(), q => q.Sequence == GfNames.IdleSeq && q.Context.ActorUnitId == b.Id);
        Assert.Empty(sim.State.Projection.Stack);
        Assert.DoesNotContain(GfNames.Shaken, sim.Unit(b.Id).Conditions);                                       // "stops them from being Shaken"
    }

    // ---- Delayed Action --------------------------------------------------------------------------------------------------------------

    static string DelayOf(Guid unit) => ActivationOption.Id(unit, GfNames.Delay);

    static void Delayed(GameState s, Guid unit) => Apply(s, new AskClosed(Guid.CreateVersion7(), [new TeamAnswer(Samples.Team1, new Chosen(DelayOf(unit)))]));

    [Fact]
    public void Delayed_action_needs_strictly_more_enemy_units_left_to_activate()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Delayed Action")]);
        var more = Game([a], [E("E1"), E("E2")]);
        Assert.Contains(DelayOf(a.Id), Turn(more, Samples.Team1));                                               // D2
        var level = Game([a], [E("E1")]);
        Assert.DoesNotContain(DelayOf(a.Id), Turn(level, Samples.Team1));
        var spent = Game([a], [E("E1"), E("E2")]);
        Activated(spent, spent.Projection.Units.Values.First(u => u.Definition.Name == "E1").Id);               // E1 activated: 1 left each
        Assert.DoesNotContain(DelayOf(a.Id), Turn(spent, Samples.Team1));
    }

    [Fact]
    public void Delayed_action_passes_the_turn_without_activating_and_the_unit_activates_later()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Delayed Action")]);
        var s = Game([a], [E("E1"), E("E2"), E("E3")]);
        var act = Rules.ReadAnswer(s, Samples.Team1, new Chosen(DelayOf(a.Id)))!;
        Assert.False(act.RunsSequence);                                                                           // D4: no action
        Assert.Empty(act.Changes);
        Delayed(s, a.Id);
        Assert.Empty(s.Projection.Units[a.Id].Actions);
        var options = Turn(s, Samples.Team1);
        Assert.Contains(ActivationOption.Id(a.Id, GfNames.Hold), options);                                        // D5
        Assert.DoesNotContain(DelayOf(a.Id), options);                                                           // D1: once per round
        NewRound(s, 2);
        Assert.Contains(DelayOf(a.Id), Turn(s, Samples.Team1));                                                  // D1: the next round again
    }

    [Fact]
    public void Delayed_action_on_a_joined_hero_passes_for_its_whole_unit()
    {
        var squad = WithHero(Unit("A", "4+", "4+", [], 3, []), P("Delayed Action"));                             // D3 "this model's unit"
        var s = Game([squad], [E("E1"), E("E2")]);
        Assert.Contains(DelayOf(squad.Id), Turn(s, Samples.Team1));
    }

    // ---- Inquisitorial Agent, Martial Prowess ----------------------------------------------------------------------------------------

    static string Again(Guid unit) => ActivationOption.Id(unit, GfNames.Hold);

    [Theory]
    [InlineData("Inquisitorial Agent")]
    [InlineData("Martial Prowess")]
    public void A_second_activation_is_offered_after_the_first_clears_fatigue_and_is_once_per_game(string rule)
    {
        var a = Unit("A", "4+", "4+", [], 1, [P(rule)]);
        var s = Game([a], [E()]);
        NewRound(s, 1);
        Assert.Contains(Again(a.Id), Turn(s, Samples.Team1));                                                    // fresh: its first activation
        Activated(s, a.Id);
        Apply(s, new ConditionSet(new Subject(SubjectKind.Unit, a.Id), GfNames.Fatigued, true));
        Assert.Contains(Again(a.Id), Turn(s, Samples.Team1));                                                    // I3
        var act = Rules.ReadAnswer(s, Samples.Team1, new Chosen(Again(a.Id)))!;
        Assert.Contains(new ConditionSet(new Subject(SubjectKind.Unit, a.Id), GfNames.Fatigued, false), act.Changes);   // I4
        Activated(s, a.Id);
        Assert.Empty(Turn(s, Samples.Team1));                                                                    // not a third time
        NewRound(s, 2);
        Activated(s, a.Id);
        Assert.Empty(Turn(s, Samples.Team1));                                                                    // I1: once per game, not once per round
    }

    [Theory]
    [InlineData("Inquisitorial Agent")]
    [InlineData("Martial Prowess")]
    public void A_unit_where_not_all_models_have_it_is_not_offered_a_second_activation(string rule)
    {
        var withHero = WithHero(Unit("A", "4+", "4+", [], 3, [P(rule)]));                                        // I2: a joined Hero without it
        var some = Unit("B", "4+", "4+", [], 2, []);
        some = some with { Models = [some.Models[0] with { Properties = [P(rule)] }, some.Models[1]] };
        var s = Game([withHero, some], [E()]);
        NewRound(s, 1);
        Activated(s, withHero.Id, some.Id);
        Assert.Empty(Turn(s, Samples.Team1));
    }

    [Theory]
    [InlineData("Inquisitorial Agent", 4, 2)]
    [InlineData("Martial Prowess", 4, 2)]
    [InlineData("Inquisitorial Agent", 3, 1)]
    [InlineData("Inquisitorial Agent", 1, 1)]
    public void Up_to_a_third_of_the_holders_rounding_up_activate_again_in_one_round(string rule, int holders, int perRound)
    {
        var units = Enumerable.Range(1, holders).Select(i => Unit($"A{i}", "4+", "4+", [], 1, [P(rule)])).ToList();
        var s = Game(units, [E()]);
        NewRound(s, 1);
        Activated(s, [.. units.Select(u => u.Id)]);
        for (var i = 0; i < perRound; i++)
        {
            Assert.Contains(Again(units[i].Id), Turn(s, Samples.Team1));
            Activated(s, units[i].Id);
        }
        Assert.Empty(Turn(s, Samples.Team1));                                                                    // I5
        if (holders == perRound) return;
        NewRound(s, 2);
        Activated(s, [.. units.Select(u => u.Id)]);
        var offered = Turn(s, Samples.Team1);
        Assert.Contains(Again(units[perRound].Id), offered);                                                     // a new round's share
        Assert.DoesNotContain(Again(units[0].Id), offered);                                                      // I1
    }

    [Fact]
    public void A_destroyed_holder_still_counts_toward_the_third()
    {
        // I6 "with this rule at the beginning of the game": four holders, one destroyed, still two a round.
        var units = Enumerable.Range(1, 4).Select(i => Unit($"A{i}", "4+", "4+", [], 1, [P("Inquisitorial Agent")])).ToList();
        var s = Game(units, [E()]);
        var dead = units[3].Models[0].Id;
        Apply(s, new ModelsMoved([new ModelRelocation(dead, s.Projection.Models[dead].Location, new OnShelf())]));
        NewRound(s, 1);
        Activated(s, units[0].Id, units[1].Id, units[2].Id);
        Activated(s, units[0].Id);
        Assert.Contains(Again(units[1].Id), Turn(s, Samples.Team1));
        Activated(s, units[1].Id);
        Assert.Empty(Turn(s, Samples.Team1));
    }
}
