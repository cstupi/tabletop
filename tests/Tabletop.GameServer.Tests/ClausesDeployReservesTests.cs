using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Rules.GrimdarkFuture.GfNames;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Clauses, 3.6.0 army rules:
/// Reinforcement: "[F1] When a unit where all models have this rule is Shaken, [F2] you may remove it from the table as destroyed. [F3] At the beginning of the
/// next round after a unit where all models have this rule is destroyed, [F4] after Ambushers have been deployed, [F5] you may place a new copy of it [F6] fully
/// within 9" of any table edge, [F7] over 9" away from enemy units. [F8] Units that deploy via Reinforcement can't seize or contest objectives on the round
/// they deploy, and [F9] this rule doesn't apply to the new copy of the unit."
/// Spawn: "[W1] Once per game, [W2] when this model is activated, [W3] you may place [W4] a new unit of X fully within 6" of it. [W5] Units placed via Spawn
/// can't seize or contest objectives on the round they are placed."
/// Split: "[L1] When this unit is fully destroyed, [L2] you may place [L3] a new unit of X fully within 6" of it [L4] before removing the last model.
/// [L5] Units placed via Split can't seize or contest objectives on the round they are placed."
/// </summary>
public class ClausesDeployReservesTests
{
    static void Stand(Sim sim, params (Guid Model, double X, double Y)[] at) =>
        sim.Append([new ModelsMoved(at.Select(a => new ModelRelocation(a.Model, sim.Model(a.Model).Location, new OnTable(new Placement(new Point3(a.X, a.Y, 0), new Yaw(0))))).ToList())]);

    static void Drain(Sim sim, Guid team)
    {
        for (var i = 0; i < 20 && sim.AskForOrNull(team) is { } ask; i++) sim.Answer(team, ask.AllowPass ? new Passed() : new Confirmed());
    }

    /// <summary>
    /// A: unit 1 plain, unit 2 Ambush (set aside), unit 3 Reinforcement on every model, unit 4 its copy; B: one unit. Round one: the Reinforcement unit is
    /// destroyed. Round two starts: [F4] the Ambusher Ask comes first, then [F3][F5] the copy's Ask; [F8] the copy, placed alone by a marker 4" from A's
    /// table edge, does not seize it at round two's end, and does at round three's.
    /// </summary>
    [Fact]
    public void The_copy_comes_after_the_Ambushers_at_the_next_round_and_holds_no_objective_that_round()
    {
        var a = TestArmies.Build("A", 4, 1, unitRules: [(2, Ambush, null)], modelRules: [(3, GfReserves.Reinforcement, null)]);
        a = a with { Units = [.. a.Units.Take(3), a.Units[3] with { Name = a.Units[2].Name, Reserve = new UnitReserve(GfReserves.Reinforcement, a.Units[2].Id) }] };
        var marker = new Marker(Guid.CreateVersion7(), MarkerKind.Objective, new Point3(60, 44, 0), new Yaw(0), 3, null, VisualRef.OfBuiltin("objective-ring"));
        var h = new ClausesDeploy36([a, TestArmies.Build("B", 1, 1)], real: true, markers: [marker]);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.Run();
        var au = h.Units(0); var b = h.Units(1)[0];
        h.SetAside(au[1], 0);
        h.Place(au[0], 20, 44); h.Place(b, 40, 4); h.Place(au[2], 30, 44);
        var rats = h.P.Units[au[2]].Definition.Models[0].Id;
        h.Sim.Append([new ModelsMoved([new ModelRelocation(rats, h.P.Models[rats].Location, new OnShelf())])]);   // destroyed in round one
        PlayRound(h);
        Assert.Equal((2, AskQuestions.DeployAmbusher), (h.P.Scheduler.Round, h.Open.Question));
        h.Answer(0, new SelectedPieces([au[1]])); h.Dispatch(h.At(au[1], 10, 30), 0);
        Assert.Equal(AskQuestions.DeployReinforcement, h.Open.Question);
        Assert.True(h.Open.AllowPass);
        Assert.Equal([au[3]], Assert.IsType<PieceSelection>(h.Open.Legal).Eligible);
        h.Answer(0, new SelectedPieces([au[3]])); h.Dispatch(h.At(au[3], 60, 45), 0);
        PlayRound(h);
        Assert.DoesNotContain(marker.Id, h.P.Control.Keys);                                                  // [F8] round two: not seized
        Assert.Equal(3, h.P.Scheduler.Round);
        PlayRound(h);
        Assert.Equal(h.Teams[0], h.P.Control[marker.Id]);
    }

    /// <summary>Every activate Ask answered with its first option, then the round ended.</summary>
    static void PlayRound(ClausesDeploy36 h)
    {
        for (var i = 0; i < 20 && h.Open.Question == AskQuestions.Activate; i++)
            h.Answer(h.TeamOf(h.Open), new Chosen(Assert.IsType<Options>(h.Open.Legal).Choices.First(c => h.Sim.Alive(c.Pieces[0]) > 0).Id));
        Assert.Equal(AskQuestions.EndRound, h.Open.Question);
        h.Answer(0, new Confirmed());
    }

    /// <summary>[F9] a copy that carries the rule (its original's entry) is never offered removal when Shaken: nothing waits for it.</summary>
    [Fact]
    public void The_copy_is_not_offered_removal_when_it_is_Shaken()
    {
        var rats = Unit("Rats", "4+", "4+", [], 2, [P(GfReserves.Reinforcement)]);
        var copy = Unit("Rats", "4+", "4+", [], 2, [P(GfReserves.Reinforcement)]) with { Reserve = new UnitReserve(GfReserves.Reinforcement, rats.Id) };
        var sim = Sim.Gf36(State([rats, copy], [Unit("E", "4+", "4+", [], 1, [])]), 1);
        sim.Append([new ModelsMoved([.. rats.Models.Select(m => new ModelRelocation(m.Id, sim.Model(m.Id).Location, new OnShelf()))])]);
        Stand(sim, (copy.Models[0].Id, 4, 30), (copy.Models[1].Id, 6, 30));
        sim.Start(MoraleTest, new SequenceContext(copy.Id, [], [], "wounds"));
        Assert.Contains(Shaken, sim.Unit(copy.Id).Conditions);
        Assert.Null(sim.AskForOrNull(Samples.Team1));
    }

    [Fact]
    public void Spawn_declined_is_offered_again_at_the_next_activation_and_a_Shaken_spawner_is_offered_nothing()
    {
        var lord = Unit("Lord", "4+", "4+", [], 1, [P(GfReserves.Spawn)]);
        var swarm = Unit("Swarm", "5+", "6+", [], 2, []) with { Reserve = new UnitReserve(GfReserves.Spawn, lord.Models[0].Id) };
        var sim = Sim.Gf36(State([lord, swarm], [Unit("E", "4+", "4+", [], 1, [])], x1: 30, x2: 60), 4);
        bool Offered() => sim.AskForOrNull(Samples.Team1) is { Question: AskQuestions.React, Legal: Options o } && o.Choices.Any(c => c.Id.StartsWith("Spawn|", StringComparison.Ordinal));
        sim.Append([new ActionRecorded(lord.Id, Hold)]);
        sim.Start(HoldSeq, new SequenceContext(lord.Id, [], [], Hold));
        Assert.True(Offered());                                                                              // [W2]
        Drain(sim, Samples.Team1);                                                                           // [W3] declined
        Assert.All(swarm.Models, m => Assert.IsType<OnShelf>(sim.Model(m.Id).Location));
        sim.Append([new ActionRecorded(lord.Id, Hold)]);
        sim.Start(HoldSeq, new SequenceContext(lord.Id, [], [], Hold));
        Assert.True(Offered());                                                                              // [W1] a declined offer spent nothing
        Drain(sim, Samples.Team1);
        sim.Append([new ConditionSet(new Subject(SubjectKind.Unit, lord.Id), Shaken, true), new ActionRecorded(lord.Id, Tabletop.GameServer.Scheduler.ActivationOption.Idle)]);
        sim.Start(IdleSeq, new SequenceContext(lord.Id, [], [], Tabletop.GameServer.Scheduler.ActivationOption.Idle));
        Assert.False(Offered());                                                                             // p.10: idle, no active special rules
    }

    /// <summary>[L2] the place Ask may be passed: the reserve stays off the table and [L4] the last model is removed.</summary>
    [Fact]
    public void Split_declined_removes_the_last_model_and_places_nothing()
    {
        var rifle = Unit("R", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1));
        var horror = Unit("Horror", "4+", "6+", [], 1, [P(GfReserves.Split)]);
        var lesser = Unit("Lesser", "5+", "6+", [], 2, []) with { Reserve = new UnitReserve(GfReserves.Split, horror.Id) };
        var sim = Sim.Gf36(State([rifle], [horror, lesser]), 5, 1);
        Stand(sim, (rifle.Models[0].Id, 20, 24), (horror.Models[0].Id, 30, 24));
        sim.Start(AttackSequence, new SequenceContext(rifle.Id, [horror.Id], [rifle.Models[0].Weapons[0].Id], Hold));
        var ask = sim.AskFor(Samples.Team2);
        Assert.True(ask.AllowPass);
        sim.Answer(Samples.Team2, new Passed());
        Assert.IsType<OnShelf>(sim.Model(horror.Models[0].Id).Location);
        Assert.All(lesser.Models, m => Assert.IsType<OnShelf>(sim.Model(m.Id).Location));
    }

    /// <summary>[L5] the unit Split places in round two, alone by the marker, does not seize it at that round's end.</summary>
    [Fact]
    public void A_unit_placed_by_Split_holds_no_objective_on_the_round_it_is_placed()
    {
        var rifle = Unit("R", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1));
        var horror = Unit("Horror", "4+", "6+", [], 1, [P(GfReserves.Split)]);
        var lesser = Unit("Lesser", "5+", "6+", [], 2, []) with { Reserve = new UnitReserve(GfReserves.Split, horror.Id) };
        var sim = Sim.Gf36(State([rifle], [horror, lesser]), 5, 1);
        sim.Append([new RoundStarted(2, [Samples.Team1, Samples.Team2], [])]);
        Stand(sim, (rifle.Models[0].Id, 36, 4), (horror.Models[0].Id, 36, 27));
        sim.Start(AttackSequence, new SequenceContext(rifle.Id, [horror.Id], [rifle.Models[0].Weapons[0].Id], Hold));
        sim.Answer(Samples.Team2, new Destinations([.. lesser.Models.Select((m, i) => new ModelRelocation(m.Id, new OnShelf(), new OnTable(new Placement(new Point3(35 + 2 * i, 24.5, 0), new Yaw(0)))))]));
        Assert.All(lesser.Models, m => Assert.IsType<OnTable>(sim.Model(m.Id).Location));
        Assert.Empty(GfObjectives.Seize(sim.State, TestProperties.Core36));                                  // Team 2's Lesser alone within 3": nothing seized
        sim.Append([new RoundStarted(3, [Samples.Team1, Samples.Team2], [])]);
        Assert.Equal(Samples.Team2, Assert.IsType<ObjectiveControlSet>(Assert.Single(GfObjectives.Seize(sim.State, TestProperties.Core36))).TeamId);
    }

    /// <summary>[L1] with p.10 (3.6.0): a unit that Routs "counts as destroyed", so Split places its unit, anchored on the last model, which then goes.</summary>
    [Fact]
    public void A_routed_Split_unit_places_its_new_unit_before_the_last_model_is_removed()
    {
        var horror = Unit("Horror", "4+", "6+", [], 4, [P(GfReserves.Split)]);
        var lesser = Unit("Lesser", "5+", "6+", [], 2, []) with { Reserve = new UnitReserve(GfReserves.Split, horror.Id) };
        var sim = Sim.Gf36(State([Unit("E", "4+", "4+", [], 1, [])], [horror, lesser]), 1);
        Stand(sim, (horror.Models[2].Id, 30, 24), (horror.Models[3].Id, 32, 24));
        sim.Append([new ModelsMoved([.. horror.Models.Take(2).Select(m => new ModelRelocation(m.Id, sim.Model(m.Id).Location, new OnShelf()))])]);   // half left
        sim.Start(MoraleTest, new SequenceContext(horror.Id, [], [], "melee"));                              // the 1 fails: a Rout
        var ask = sim.AskFor(Samples.Team2);
        Assert.Equal("place", ask.Question);
        Assert.Equal(1, sim.Alive(horror.Id));                                                               // [L4] the last model stays while it is placed
        sim.Answer(Samples.Team2, new Destinations([new ModelRelocation(lesser.Models[0].Id, new OnShelf(), new OnTable(new Placement(new Point3(30, 28, 0), new Yaw(0)))),
            new ModelRelocation(lesser.Models[1].Id, new OnShelf(), new OnTable(new Placement(new Point3(32, 28, 0), new Yaw(0))))]));
        Assert.Equal(0, sim.Alive(horror.Id));
        Assert.Equal(2, sim.Alive(lesser.Id));
        Assert.Equal(1, sim.Steps("consequence").Last().Values!["routed"]);
    }

    /// <summary>[F3] with p.10 (3.6.0): a Reinforcement unit that Routs is destroyed, so its copy is offered at the next round's start.</summary>
    [Fact]
    public void A_routed_Reinforcement_unit_is_destroyed_and_its_copy_is_offered_next_round()
    {
        var rats = Unit("Rats", "4+", "4+", [], 2, [P(GfReserves.Reinforcement)]);
        var copy = Unit("Rats", "4+", "4+", [], 2, [P(GfReserves.Reinforcement)]) with { Reserve = new UnitReserve(GfReserves.Reinforcement, rats.Id) };
        var sim = Sim.Gf36(State([rats, copy], [Unit("E", "4+", "4+", [], 1, [])]), 1);
        sim.Append([new RoundStarted(1, [Samples.Team1, Samples.Team2], []),
            new ModelsMoved([new ModelRelocation(rats.Models[0].Id, sim.Model(rats.Models[0].Id).Location, new OnShelf())])]);
        sim.Start(MoraleTest, new SequenceContext(rats.Id, [], [], "melee"));
        Assert.Equal(0, sim.Alive(rats.Id));                                                                 // routed
        sim.Append([new RoundStarted(2, [Samples.Team1, Samples.Team2], [])]);
        Assert.Equal([copy.Id], GfReserves.Reinforcements(sim.State, Samples.Team1));
    }
}
