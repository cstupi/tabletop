using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

public class TurnCycleTests
{
    readonly ScriptedStarter _starter = new();
    static TurnCycle Cycle => new(TestProperties.Core);

    /// <summary>Two teams of two three-model units, everything on the table, round one in progress, team 0's turn started.</summary>
    static TestGames.Built InRounds(out Guid team, out IReadOnlyList<Guid> units, Enforcement enforcement = Enforcement.Warn)
    {
        var g = TestGames.Build([TestArmies.Build("A", 2, 3), TestArmies.Build("B", 2, 3)], enforcement: enforcement);
        foreach (var u in g.State.Projection.Units.Keys.ToList()) TestGames.Apply(g, TestGames.Place(g.State, u));
        TestGames.Apply(g, new RoundStarted(1, g.Teams, []), new PhaseStarted(GfNames.Rounds), new TurnStarted(g.Teams[0]));
        team = g.Teams[0]; units = TeamUnits.Of(g.State, team).Select(u => u.Id).ToList();
        return g;
    }
    CycleStep Step(TestGames.Built g, Guid team, TurnCycle cycle) => cycle.Step(g.State, team, SchedulerLog.Of(g.State), _starter);
    static Ask Opened(CycleStep step) => ((AskOpened)Assert.IsType<CycleEmit>(step).Events.Single()).Ask;
    static void Answer(TestGames.Built g, Ask ask, Guid team, AskAnswer answer) => TestGames.Apply(g, new AskAnswered(ask.Id, team, answer, false), new AskClosed(ask.Id, [new TeamAnswer(team, answer)]));

    [Fact]
    public void A_turn_opens_an_activate_ask_then_waits_starts_the_sequence_waits_and_completes_when_it_finishes()
    {
        var g = InRounds(out var team, out var units);
        var ask = Opened(Step(g, team, Cycle));
        Assert.Equal((AskQuestions.Activate, true, null), (ask.Question, ask.AllowPass, ask.FrameId)); Assert.Equal([team], ask.Teams);
        Assert.Equal(8, Assert.IsType<Options>(ask.Legal).Choices.Count);                              // 2 units × 4 actions
        TestGames.Apply(g, new AskOpened(ask));
        Assert.IsType<CycleWaiting>(Step(g, team, Cycle));
        TestGames.Apply(g, TestGames.Place(g.State, units[0]));                                         // a hand move changes nothing
        Assert.IsType<CycleWaiting>(Step(g, team, Cycle));
        Answer(g, ask, team, new Chosen(ActivationOption.Id(units[0], "Advance")));
        var start = Assert.IsType<CycleEmit>(Step(g, team, Cycle)).Events;
        Assert.Equal(new ActionRecorded(units[0], "Advance"), start[0]);
        var frame = Assert.IsType<SequenceStarted>(start[1]);
        Assert.Equal((units[0], "Advance"), (frame.Context.ActorUnitId, frame.Context.Action));
        TestGames.Apply(g, start.ToArray());
        Assert.IsType<Running>(Cycle.Read(g.State, team, SchedulerLog.Of(g.State)));
        Assert.IsType<CycleWaiting>(Step(g, team, Cycle));
        TestGames.Apply(g, new SequenceStarted(Guid.CreateVersion7(), "shooting", new SequenceContext(units[0], [], [], null)));   // a nested frame
        Assert.IsType<CycleWaiting>(Step(g, team, Cycle));
        TestGames.Apply(g, new SequenceFinished(frame.FrameId));                                        // pops both
        var done = Assert.IsType<CycleComplete>(Step(g, team, Cycle));
        Assert.Equal((team, units[0], "Advance"), (done.Turn.TeamId, done.Turn.Activation!.UnitId, done.Turn.Activation.Action));
        Assert.Equal(g.State.Projection.LastSeq, done.Turn.ActivationEndedAtSeq);
    }

    [Fact]
    public void Passing_is_a_real_answer_and_so_is_an_answer_that_names_nothing()
    {
        var g = InRounds(out var team, out _);
        var ask = Opened(Step(g, team, Cycle)); TestGames.Apply(g, new AskOpened(ask));
        Answer(g, ask, team, new Passed());
        Assert.IsType<CyclePassed>(Step(g, team, Cycle));
        TestGames.Apply(g, new TurnStarted(team));
        var again = Opened(Step(g, team, Cycle)); TestGames.Apply(g, new AskOpened(again));
        Answer(g, again, team, new Chosen("not-an-option"));
        Assert.IsType<CyclePassed>(Step(g, team, Cycle));                                                 // under warn and silent: logged, then read as a pass
        var strict = InRounds(out var st, out _, Enforcement.Strict);                                      // the same logged answer, in a game since switched to strict
        var third = Opened(Step(strict, st, Cycle)); TestGames.Apply(strict, new AskOpened(third));
        Answer(strict, third, st, new Chosen("not-an-option"));
        Assert.IsType<CyclePassed>(Step(strict, st, Cycle));                                               // the reader does not consult the dial: a rebuilt state cannot depend on a setting a player may have changed since
    }

    [Fact]
    public void Idle_runs_the_idle_sequence_and_an_unknown_action_writes_the_record_and_completes_without_one()
    {
        var g = InRounds(out var team, out var units);
        TestGames.Apply(g, new ConditionSet(TestGames.UnitSubject(units[0]), GfNames.Shaken, true));
        var ask = Opened(Step(g, team, Cycle)); TestGames.Apply(g, new AskOpened(ask));
        Answer(g, ask, team, new Chosen(ActivationOption.Id(units[0], ActivationOption.Idle)));
        var start = Assert.IsType<CycleEmit>(Step(g, team, Cycle)).Events;
        Assert.Equal(new ActionRecorded(units[0], ActivationOption.Idle), start[0]);
        var idle = Assert.IsType<SequenceStarted>(start[1]);
        Assert.Equal((GfNames.IdleSeq, ActivationOption.Idle), (idle.Sequence, idle.Context.Action));
        TestGames.Apply(g, start.ToArray());
        Assert.IsType<CycleWaiting>(Step(g, team, Cycle));
        TestGames.Apply(g, new SequenceFinished(idle.FrameId));
        Assert.IsType<CycleComplete>(Step(g, team, Cycle));
        Assert.Equal(GfNames.IdleSeq, Assert.Single(_starter.Started).Sequence);
        TestGames.Apply(g, new TurnStarted(team));
        var second = Opened(Step(g, team, Cycle)); TestGames.Apply(g, new AskOpened(second));
        Answer(g, second, team, new Chosen(ActivationOption.Id(units[1], "Teleport")));                 // out of set, accepted under warn
        var recorded = Assert.IsType<CycleEmit>(Step(g, team, Cycle)).Events;
        Assert.Equal(new ActionRecorded(units[1], "Teleport"), Assert.Single(recorded));                 // the interpreter had no sequence for it
        TestGames.Apply(g, recorded.ToArray());
        Assert.IsType<CycleComplete>(Step(g, team, Cycle));
    }

    [Fact]
    public void The_end_of_turn_hook_runs_once_and_the_turn_completes_when_its_sequence_finishes()
    {
        var g = InRounds(out var team, out var units);
        var enemy = TeamUnits.Of(g.State, g.Teams[1])[0];
        var ask = Opened(Step(g, team, Cycle)); TestGames.Apply(g, new AskOpened(ask));
        Answer(g, ask, team, new Chosen(ActivationOption.Id(units[0], "Hold")));
        var start = Assert.IsType<CycleEmit>(Step(g, team, Cycle)).Events; TestGames.Apply(g, start.ToArray());
        var frame = ((SequenceStarted)start[1]).FrameId;
        var models = enemy.Definition.Models;
        TestGames.Apply(g, new WoundsThisActivationSet(enemy.Id, 2),
            new ModelsMoved([new ModelRelocation(models[0].Id, g.State.Projection.Models[models[0].Id].Location, new OnShelf()), new ModelRelocation(models[1].Id, g.State.Projection.Models[models[1].Id].Location, new OnShelf())]),
            new SequenceFinished(frame));
        var hook = Assert.IsType<CycleEmit>(Step(g, team, Cycle)).Events;
        var morale = Assert.IsType<SequenceStarted>(Assert.Single(hook));
        Assert.Equal((GfNames.MoraleTest, enemy.Id), (morale.Sequence, morale.Context.ActorUnitId));
        TestGames.Apply(g, hook.ToArray());
        Assert.IsType<CycleWaiting>(Step(g, team, Cycle));
        TestGames.Apply(g, new SequenceFinished(morale.FrameId));
        Assert.IsType<CycleComplete>(Step(g, team, Cycle));
        Assert.Equal(2, _starter.Started.Count);                                                          // the action, then the morale test — asked for once each
    }

    [Fact]
    public void An_abandoned_activation_still_counts_and_ends_the_turn()
    {
        var g = InRounds(out var team, out var units);
        var ask = Opened(Step(g, team, Cycle)); TestGames.Apply(g, new AskOpened(ask));
        Answer(g, ask, team, new Chosen(ActivationOption.Id(units[1], "Rush")));
        var start = Assert.IsType<CycleEmit>(Step(g, team, Cycle)).Events; TestGames.Apply(g, start.ToArray());
        TestGames.Apply(g, new SequenceAbandoned(((SequenceStarted)start[1]).FrameId));
        Assert.IsType<CycleComplete>(Step(g, team, Cycle));
        Assert.Contains("Rush", g.State.Projection.Units[units[1]].Actions);
    }

    [Fact]
    public void An_empty_legal_set_is_reported_and_deployment_waits_for_every_model()
    {
        var g = TestGames.Build([TestArmies.Build("A", 1, 2), TestArmies.Build("B", 1, 1)]);
        var team = g.Teams[0]; var unit = TeamUnits.Of(g.State, team)[0];
        TestGames.Apply(g, new RoundStarted(0, g.Teams, []), new PhaseStarted(GfNames.Deployment), new TurnStarted(team));
        var ask = Opened(Step(g, team, Cycle));
        Assert.Equal((AskQuestions.Deploy, false), (ask.Question, ask.AllowPass));
        TestGames.Apply(g, new AskOpened(ask));
        Answer(g, ask, team, new SelectedPieces([unit.Id]));
        Assert.IsType<CycleWaiting>(Step(g, team, Cycle));
        var m = unit.Definition.Models;
        TestGames.Apply(g, new ModelsMoved([new ModelRelocation(m[0].Id, new OnShelf(), new OnTable(new(new(2, 2, 0), new(0))))]));
        Assert.IsType<CycleWaiting>(Step(g, team, Cycle));
        TestGames.Apply(g, new ModelsMoved([new ModelRelocation(m[1].Id, new OnShelf(), new OnTable(new(new(4, 2, 0), new(0))))]));
        Assert.IsType<CycleComplete>(Step(g, team, Cycle));
        TestGames.Apply(g, new TurnEnded(team, false, []), new TurnStarted(team));
        Assert.IsType<CycleEmpty>(Step(g, team, Cycle));                                                // nothing left to deploy
    }
}
