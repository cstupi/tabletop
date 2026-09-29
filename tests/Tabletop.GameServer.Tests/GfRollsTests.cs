using Applied = Tabletop.GameServer.Dispatch.Applied;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

public class GfRollsTests
{
    static readonly PropertySet Core = TestProperties.Core;
    static readonly IGameRules Gf = GrimdarkFutureRules.Instance;

    [Fact]
    public void Roll_specs_are_per_roll_and_thresholds_are_functions()
    {
        var a = Unit("A", "3+", "5+", [], 1, [], Melee("Fist", 1)); var b = Unit("B", "4+", "2+", [], 1, []);
        var state = State([a], [b]);
        var live = LiveProperties.Settle(new Interaction(state, Core, Party.Whole(state, a.Id), Party.Whole(state, b.Id), AttackKind.Melee, "Charge", 1, []));
        Assert.Equal(3, Gf.RollSpec(GfNames.Hit).Threshold(live, Side.Attacker));
        Assert.Equal(2, Gf.RollSpec(GfNames.DefenseRoll).Threshold(live, Side.Defender));
        Assert.Equal(4, Gf.RollSpec(GfNames.Morale).Threshold(live, Side.Defender));
        Assert.Equal(4, Gf.RollSpec(GfNames.Casting).Threshold(live, Side.Attacker));
        Assert.Null(Gf.RollSpec(GfNames.RollOff).Threshold(live, Side.Attacker));
        Assert.Equal(new RollConvention(6, null), Gf.RollSpec(GfNames.Hit).Convention);
        Assert.Equal(GfNames.Hit, Gf.HitRoll);
        Assert.Equal(GfNames.ChargeSeq, Gf.ActionSequence(GfNames.Charge));
        Assert.Throws<UnknownSequenceException>(() => Gf.Sequence("nope"));
    }

    [Fact]
    public void Fatigue_makes_melee_hit_only_on_unmodified_sixes_and_cover_adds_one_to_defense_against_shooting()
    {
        var a = Unit("A", "3+", "5+", [], 1, [], Melee("Fist", 1, P("Thrust"))); var b = Unit("B", "4+", "4+", [], 1, []);
        var sim = Sim.Gf(State([a], [b]), 6, 5);
        sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, a.Id), GfNames.Fatigued, true)]);
        var frame = new FrameView(new Frame(Guid.CreateVersion7(), "x", new SequenceContext(a.Id, [b.Id], [], GfNames.Charge)), 0, new Dictionary<string, int>(), StepHistory.Empty, null);
        var ctx = new StepContext(sim.State, Core, Gf, frame, new Roller(new ScriptedDice(6, 5), sim.State.RngSeed, 0));
        var live = LiveProperties.Settle(new Interaction(sim.State, Core, Party.Of(sim.State, a.Id, a.Models.Select(m => m.Id), a.Models[0].Weapons.Select(w => w.Id)), Party.Whole(sim.State, b.Id), AttackKind.Melee, GfNames.Charge, 1, []));
        Assert.Equal(6, GfRolls.HitQuality(ctx, live, AttackKind.Melee, a.Models[0].Id));        // p.9 replaces the value, so fatigue collapses every Quality to one
        var plan = GfRolls.HitPlan(ctx, live, AttackKind.Melee, 6);
        Assert.Equal((6, 6, 0), (plan.RequiredRaw, plan.Threshold, plan.Modifiers.Count));       // Thrust's +1 does not help a fatigued unit
        Assert.Equal([true, false], ctx.Roll(plan, 2).Dice.Select(d => d.Success));
        var shot = LiveProperties.Settle(live.Interaction with { Attack = AttackKind.Shooting });
        Assert.Contains(GfRolls.DefensePlan(ctx, shot, inCover: true, defenderModels: 1).Modifiers, m => m.Source == "cover" && m.Amount == 1);
        Assert.DoesNotContain(GfRolls.DefensePlan(ctx, shot, inCover: false, defenderModels: 1).Modifiers, m => m.Source == "cover");
        Assert.DoesNotContain(GfRolls.DefensePlan(ctx, live, inCover: true, defenderModels: 1).Modifiers, m => m.Source == "cover");   // melee: no cover bonus
    }

    [Fact]
    public void A_joined_hero_hits_at_its_own_quality_blocks_on_the_units_until_it_is_alone_and_lends_its_quality_to_morale()
    {
        var squad = Unit("Squad", "5+", "5+", [], 2, [], Gun("Rifle", "24\"", 1));
        var joined = squad with { Models = [squad.Models[0] with { Name = "Captain", Stats = new Dictionary<string, string> { ["Quality"] = "3+", ["Defense"] = "2+" }, Properties = [P("Hero")] }, squad.Models[1]] };
        var (captain, trooper) = (joined.Models[0].Id, joined.Models[1].Id);
        var enemy = Unit("Enemy", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1));
        var state = State([joined], [enemy], x1: -6, x2: 6);
        var frame = new FrameView(new Frame(Guid.CreateVersion7(), "x", new SequenceContext(joined.Id, [enemy.Id], [], GfNames.Hold)), 0, new Dictionary<string, int>(), StepHistory.Empty, null);
        var ctx = new StepContext(state, Core, Gf, frame, new Roller(new ScriptedDice(4), state.RngSeed, 0));
        var shooting = LiveProperties.Settle(new Interaction(state, Core, Party.Of(state, joined.Id, joined.Models.Select(m => m.Id), joined.Models.SelectMany(m => m.Weapons.Select(w => w.Id))), Party.Whole(state, enemy.Id), AttackKind.Shooting, GfNames.Hold, 12, []));
        Assert.Equal(3, GfRolls.HitQuality(ctx, shooting, AttackKind.Shooting, captain));                 // p.4: the model's Quality value
        Assert.Equal(5, GfRolls.HitQuality(ctx, shooting, AttackKind.Shooting, trooper));
        Assert.Equal(3, GfRolls.HitPlan(ctx, shooting, AttackKind.Shooting, 3).Threshold);

        // p.14: the hero must use the unit's Defense until all other models have been killed.
        var incoming = LiveProperties.Settle(new Interaction(state, Core, Party.Whole(state, enemy.Id), Party.Whole(state, joined.Id), AttackKind.Shooting, GfNames.Hold, 12, []));
        Assert.Equal(5, GfRolls.DefensePlan(ctx, incoming, inCover: false, defenderModels: 2).Threshold);
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new ModelsMoved([new ModelRelocation(trooper, state.Projection.Models[trooper].Location, new OnShelf())]), null));
        var alone = LiveProperties.Settle(new Interaction(state, Core, Party.Whole(state, enemy.Id), Party.Whole(state, joined.Id), AttackKind.Shooting, GfNames.Hold, 12, []));
        Assert.Equal(2, GfRolls.DefensePlan(ctx, alone, inCover: false, defenderModels: 1).Threshold);
        Assert.Equal(5, GfRolls.DefensePlan(ctx, alone, inCover: false, defenderModels: 2).Threshold);   // the count fire-groups settled, not the live one

        // p.14: "may take morale tests on behalf of the unit", resolved always-when-it-helps.
        var morale = LiveProperties.Settle(new Interaction(state, Core, Party.Whole(state, joined.Id), null, null, GfNames.Hold, null, []));
        Assert.Equal(3, GfRolls.MoralePlan(ctx, morale).Threshold);
        var leaderless = State([squad], [enemy], x1: -6, x2: 6);
        var plain = LiveProperties.Settle(new Interaction(leaderless, Core, Party.Whole(leaderless, squad.Id), null, null, GfNames.Hold, null, []));
        Assert.Equal(5, GfRolls.MoralePlan(new StepContext(leaderless, Core, Gf, frame, new Roller(new ScriptedDice(4), leaderless.RngSeed, 0)), plain).Threshold);
    }

    [Fact]
    public void Round_start_clears_fatigue_and_grants_capped_tokens_and_turn_end_clears_wounds_this_activation()
    {
        var caster = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 2)]); var grunts = Unit("Grunts", "4+", "4+", [], 3, []);
        var state = State([caster], [grunts]);
        state.Applier.Apply(new GameEvent(2, null, new ConditionSet(new Subject(SubjectKind.Unit, grunts.Id), GfNames.Fatigued, true), null));
        state.Applier.Apply(new GameEvent(3, null, new SpellTokensSet(caster.Models[0].Id, 5), null));
        state.Applier.Apply(new GameEvent(4, null, new WoundsThisActivationSet(grunts.Id, 2), null));
        var round = Gf.OnRoundStarted(state, Core, 2);
        Assert.Contains(round, c => c is ConditionSet { Condition: GfNames.Fatigued, Present: false } cs && cs.Subject.Id == grunts.Id);
        Assert.Contains(round, c => c is SpellTokensSet { Tokens: 6 } t && t.ModelId == caster.Models[0].Id);
        var turn = Gf.OnTurnEnded(state, Core, new TurnContext(Samples.Team2, null, 0, 0));
        Assert.Contains(turn, c => c is WoundsThisActivationSet { Wounds: 0 } w && w.UnitId == grunts.Id);
        Assert.Empty(Gf.OnRoundStarted(state, Core, 0));                                                          // the phases before round one carry none
        state.Applier.Apply(new GameEvent(5, null, new ConditionSet(new Subject(SubjectKind.Unit, grunts.Id), GfNames.Shaken, true), null));
        var idle = Gf.OnTurnEnded(state, Core, new TurnContext(Samples.Team2, new Activation(grunts.Id, ActivationOption.Idle, false, []), 0, 0));
        Assert.Contains(idle, c => c is ConditionSet { Condition: GfNames.Shaken, Present: false } cs && cs.Subject.Id == grunts.Id);   // p.10: idling stops Shaken
        var moved = Gf.OnTurnEnded(state, Core, new TurnContext(Samples.Team2, new Activation(grunts.Id, GfNames.Advance, true, []), 0, 0));
        Assert.DoesNotContain(moved, c => c is ConditionSet { Condition: GfNames.Shaken, Present: false });                             // an ordinary Advance does not
    }

    [Fact]
    public void A_shaken_aircrafts_mandatory_advance_still_stops_it_being_shaken()
    {
        var jet = Unit("Jet", "4+", "4+", [P("Aircraft")], 1, []);
        var state = State([jet], [Unit("T", "4+", "4+", [], 1, [])]);
        state.Applier.Apply(new GameEvent(2, null, new ConditionSet(new Subject(SubjectKind.Unit, jet.Id), GfNames.Shaken, true), null));
        // p.13: an Aircraft may only use Advance actions, even if Shaken, and "their mandatory move doesn't break the staying idle requirement to stop being Shaken".
        var advanced = Gf.OnTurnEnded(state, Core, new TurnContext(Samples.Team1, new Activation(jet.Id, GfNames.Advance, true, []), 0, 0));
        Assert.Contains(advanced, c => c is ConditionSet { Condition: GfNames.Shaken, Present: false } cs && cs.Subject.Id == jet.Id);
    }

    [Fact]
    public void End_of_activation_tests_units_at_half_or_less_that_took_wounds_and_did_not_fight_a_melee()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)); var b = Unit("B", "4+", "4+", [], 4, []); var big = Unit("Big", "4+", "4+", [], 1, [P("Tough", 6)]);
        var state = State([a], [b, big]);
        void Kill(Guid model) => state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new ModelsMoved([new ModelRelocation(model, state.Projection.Models[model].Location, new OnShelf())]), null));
        Kill(b.Models[0].Id); Kill(b.Models[1].Id);
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new WoundsThisActivationSet(b.Id, 2), null));
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new WoundsSet(big.Models[0].Id, 3), null));
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new WoundsThisActivationSet(big.Id, 3), null));
        var tests = Gf.EndOfTurnSequences(state, Core, new TurnContext(Samples.Team1, new Activation(a.Id, GfNames.Hold, true, []), 0, 0));
        Assert.Equal(2, tests.Count);
        Assert.All(tests, t => Assert.Equal((GfNames.MoraleTest, "wounds"), (t.Sequence, t.Context.Action)));
        Assert.Equal(new HashSet<Guid> { b.Id, big.Id }, tests.Select(t => t.Context.ActorUnitId!.Value).ToHashSet());
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new WoundsThisActivationSet(b.Id, 1), null));
        Assert.True(GfHooks.AtHalfOrLess(state, Core, b.Id));
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null, new WoundsSet(big.Models[0].Id, 2), null));
        Assert.False(GfHooks.AtHalfOrLess(state, Core, big.Id));                                   // 4 of Tough(6) left
    }

    [Fact]
    public void Containment_settles_reach_without_measuring_and_carried_models_still_count()
    {
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11), P("Tough", 3)]);
        var riders = Unit("Riders", "4+", "4+", [], 4, []);
        var stowaways = Unit("Stowaways", "4+", "4+", [], 2, []);
        var outside = Unit("Outside", "4+", "4+", [], 1, []);
        var state = State([bus, riders], [stowaways, outside]);
        var container = bus.Models[0].Id;
        void Embark(Tabletop.Content.Unit u) => state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null,
            new ModelsMoved(u.Models.Select(m => new ModelRelocation(m.Id, state.Projection.Models[m.Id].Location, new Inside(container))).ToList()), null));
        Embark(riders); Embark(stowaways);
        Assert.Null(Battlefield.InsideReach(state, outside.Id, bus.Id));                           // both on the table: measure it
        Assert.False(Battlefield.InsideReach(state, outside.Id, riders.Id));                       // p.14: no sight into a transport
        Assert.False(Battlefield.InsideReach(state, riders.Id, outside.Id));                       // nor out of one
        Assert.True(Battlefield.InsideReach(state, riders.Id, stowaways.Id));                      // p.14: units inside can target other units inside
        Assert.True(Battlefield.InsideReach(state, riders.Id, bus.Id));                            // p.15: and the Transport itself
        // p.10 counts the models a unit still has, and being carried has cost it none.
        Assert.False(GfHooks.AtHalfOrLess(state, Core, riders.Id));
        state.Applier.Apply(new GameEvent(state.Projection.LastSeq + 1, null,
            new ModelsMoved(riders.Models.Take(2).Select(m => new ModelRelocation(m.Id, new Inside(container), new OnShelf())).ToList()), null));
        Assert.True(GfHooks.AtHalfOrLess(state, Core, riders.Id));                                 // two of four dead inside it
    }

    [Fact]
    public void The_roll_off_records_one_die_per_team_rerolls_ties_and_stops_at_a_gate_every_team_may_release()
    {
        var sim = Sim.Gf(State([Unit("A", "4+", "4+", [], 1, [])], [Unit("B", "4+", "4+", [], 1, [])]), 4, 4, 2, 5);
        sim.Append([new RoundStarted(0, [Samples.Team1, Samples.Team2], [])]);
        var events = sim.Start(GfNames.RollOffSeq, new SequenceContext(null, [], [], null));
        Assert.Equal(["step_completed", "ask_opened"], events.Select(EventKinds.Of));                // the frame stays open behind the gate
        var roll = events.OfType<StepCompleted>().Single();
        Assert.Equal(2, roll.Rolls.Count);                                                          // a tie, then the decider
        Assert.All(roll.Rolls, r => Assert.Null(r.Threshold));
        Assert.Equal([4, 4], roll.Rolls[0].Dice.Select(d => d.Raw)); Assert.Equal([2, 5], roll.Rolls[1].Dice.Select(d => d.Raw));
        Assert.Null(roll.Values);                                                                   // the rolling step settles nothing: `order` derives it
        var confirm = sim.AskFor(Samples.Team2);
        Assert.Equal((AskQuestions.Confirm, 2), (confirm.Question, confirm.Teams.Count));            // no actor, no parent: every team
        var rest = sim.Answer(Samples.Team2, new Confirmed());
        var order = rest.OfType<StepCompleted>().Single();
        Assert.Equal((2, 2, 1), (order.Values!["winner_ordinal"], order.Values["place_1"], order.Values["place_2"]));   // what the scheduler reads into round_started.Order
        Assert.Equal(4, DiceCursor.DrawnSoFar(sim.State));
    }

    /// <summary>
    /// The point of the gate: the frame is on the stack while it is open, so SetDice is legal
    /// against the roll-off's own record, and `order` derives the places from the edited dice rather than from a settled value.
    /// </summary>
    [Fact]
    public void Editing_the_roll_offs_dice_at_the_gate_changes_the_order()
    {
        var sim = Sim.Gf(State([Unit("A", "4+", "4+", [], 1, [])], [Unit("B", "4+", "4+", [], 1, [])]), 2, 5);
        sim.Append([new RoundStarted(0, [Samples.Team1, Samples.Team2], [])]);
        sim.Start(GfNames.RollOffSeq, new SequenceContext(null, [], [], null));
        var frame = sim.State.Projection.Stack.Single().Id;
        var record = sim.Steps(GfNames.RollOffRollStep).Single().Rolls.Single();
        Assert.IsType<Applied>(sim.Dispatch(new SetDice(frame, record.Id, [new DieValue(0, 6)]), Samples.P1, Samples.Team1));
        var order = sim.Answer(Samples.Team1, new Confirmed()).OfType<StepCompleted>().Single();
        Assert.Equal((1, 1, 2), (order.Values!["winner_ordinal"], order.Values["place_1"], order.Values["place_2"]));   // team 1's 2 edited to a 6 wins
        Assert.Equal(2, DiceCursor.DrawnSoFar(sim.State));                                          // an edit draws nothing
    }

    [Fact]
    public void The_battlefield_reads_range_sight_and_cover_from_geometry()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)); var b = Unit("B", "4+", "4+", [], 2, []);
        var state = State([a], [b], x1: -6, x2: 6);
        Assert.True(Battlefield.CanSee(state, a.Models[0].Id, b.Models[0].Id));
        Assert.InRange(Battlefield.Distance(state, a.Models[0].Id, b.Models[0].Id), 10.0, 12.0);   // 12" centre to centre, less the two bases
        Assert.Equal((0, 2), (Battlefield.ModelsInCoverTerrain(state, b.Id, wholeBase: false), Battlefield.ModelsVisibleFrom(state, b.Id, [a.Models[0].Id])));
        Assert.False(GfRolls.InCover(state, b.Id, [a.Models[0].Id]));                                 // nobody in terrain, both models seen: neither clause of p.11's majority
        Assert.Empty(Battlefield.TerrainUnder(state, b.Id));
    }

    /// <summary>A Cover area spanning x -5..5 and y -30..edgeY, so a model in the x = 0 column is fully inside when its base clears edgeY.</summary>
    static Tabletop.Content.TerrainPlacement CoverUpTo(double edgeY) =>
        new(Guid.CreateVersion7(), Tabletop.Content.VisualRef.OfBuiltin("forest"), new Tabletop.Geometry.Point3(0, (edgeY - 30) / 2, 0), new Tabletop.Geometry.Yaw(0), 1.0,
            [new Tabletop.Content.TerrainVolume(Guid.CreateVersion7(), new Tabletop.Geometry.Volume(Tabletop.Geometry.VolumeKind.Area, Tabletop.Geometry.Footprint.Rect(10, edgeY + 30), new Tabletop.Geometry.Point2(0, 0), new Tabletop.Geometry.Yaw(0), 0, 3, Tabletop.Geometry.TerrainDeclarations.Cover, null))]);

    // Models of a unit sit at y = -18.5, -17, -15.5, -14 with a 0.63 radius, so an edge at -15.2 has three centres inside and two whole bases.
    [Theory]
    [InlineData(-15.2, 3, 2, false)]   // third model straddles: centre in, base not fully in
    [InlineData(-13.8, 4, 3, true)]
    [InlineData(-13.0, 4, 4, true)]
    [InlineData(-17.5, 1, 1, false)]
    public void Cover_terrain_is_counted_by_centre_or_by_whole_base_and_in_cover_needs_a_strict_majority(double edgeY, int byCentre, int byBase, bool inCover)
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)); var b = Unit("B", "4+", "4+", [], 4, []);
        var state = State([a], [b], x1: -12, x2: 0, terrain: [CoverUpTo(edgeY)]);
        Assert.Equal((byCentre, byBase), (Battlefield.ModelsInCoverTerrain(state, b.Id, wholeBase: false), Battlefield.ModelsInCoverTerrain(state, b.Id, wholeBase: true)));
        Assert.Equal(inCover, GfRolls.InCover(state, b.Id, [a.Models[0].Id]));                     // a multi-model unit is judged by whole bases: 2 of 4 is not a majority, 3 of 4 is
    }

    [Fact]
    public void A_single_model_straddling_cover_terrain_is_in_cover_by_its_centre()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)); var b = Unit("B", "4+", "4+", [], 1, []);
        var state = State([a], [b], x1: -12, x2: 4.9, terrain: [CoverUpTo(-13)]);                 // x = 4.9: centre inside x -5..5, base past it
        Assert.Equal((1, 0), (Battlefield.ModelsInCoverTerrain(state, b.Id, wholeBase: false), Battlefield.ModelsInCoverTerrain(state, b.Id, wholeBase: true)));
        Assert.True(GfRolls.InCover(state, b.Id, [a.Models[0].Id]));                               // p.11: single-model units need only be "mostly inside"
    }
}
