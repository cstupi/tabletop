using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Rules.GrimdarkFuture.GfNames;

namespace Tabletop.GameServer.Tests;

public class RoundBoundaryTests
{

    [Fact]
    public void Ambushers_are_asked_at_the_start_of_round_two_and_may_activate_that_round()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 2, 1, unitRules: [(2, Ambush, null)]), TestArmies.Build("B", 1, 1)]);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.Run();
        var a = h.Units(0); var b = h.Units(1);
        h.Dispatch(new SetCondition(TestGames.UnitSubject(a[1]), SetAside, true));
        h.SkipDeployment();
        h.Answer(0, new Chosen(TestGames.Activate(a[0]))); var seq = h.P.LastSeq; h.Answer(1, new Chosen(TestGames.Activate(b[0])));
        Assert.Equal(["ask_answered", "ask_closed", "action_recorded", "sequence_started", "sequence_finished", "turn_ended", "ask_opened"], h.Kinds(seq));
        Assert.Equal(AskQuestions.EndRound, h.Open.Question);                                              // the round waits to be ended before anything else happens
        seq = h.P.LastSeq; h.EndRound();
        Assert.Equal(["ask_answered", "ask_closed", "round_started", "ask_opened"], h.Kinds(seq));          // the boundary Ask sits before round_started, so every hook still lands in the new round
        Assert.Equal((2, (Guid?)null, AskQuestions.DeployAmbusher), (h.P.Scheduler.Round, h.P.Scheduler.ActiveTeamId, h.Open.Question));   // a boundary Ask, not a turn
        Assert.Equal([h.Teams[0]], h.Open.Teams);                                                          // A finished first and is asked first
        h.Answer(0, new SelectedPieces([a[1]]));
        Assert.Empty(h.P.OpenAsks); Assert.Null(h.P.Scheduler.ActiveTeamId);                                // waiting for the models
        seq = h.P.LastSeq; h.Deploy(a[1]);
        Assert.Equal(["models_moved", "turn_started", "ask_opened"], h.Kinds(seq));                       // A has no more Ambushers, B never had one: the hook is done
        Assert.Contains(Assert.IsType<Options>(h.Open.Legal).Choices, c => c.Pieces[0] == a[1]);           // deploying wrote nothing to the record
        Assert.Empty(h.P.Units[a[1]].Actions);
        h.Rehydrate(); Assert.Empty(h.Run());
    }

    [Fact]
    public void Round_start_changes_ride_on_round_started_in_order_record_clears_then_fatigue_then_tokens()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 1, 1, modelRules: [(1, Caster, 2)]), TestArmies.Build("B", 1, 1)]);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.SkipDeployment();
        var a = h.Units(0); var b = h.Units(1); var caster = h.P.Units[a[0]].Definition.Models[0].Id;
        h.Dispatch(new SetCondition(TestGames.UnitSubject(b[0]), Fatigued, true));
        h.Answer(0, new Chosen(TestGames.Activate(a[0]))); var seq = h.P.LastSeq; h.Answer(1, new Chosen(TestGames.Activate(b[0])));
        Assert.Contains(Fatigued, h.P.Units[b[0]].Conditions);                                    // the boundary Ask is open and no hook has run: fatigue still belongs to round one
        Assert.Equal(2, h.P.Models[caster].SpellTokens);                                          // round one's own boundary granted Caster(2); round two has granted nothing yet
        h.EndRound();
        var round = (RoundStarted)h.Log.First(e => e.Seq > seq && e.Payload is RoundStarted).Payload;
        Assert.Equal([typeof(ActionRecordCleared), typeof(ActionRecordCleared), typeof(ConditionSet), typeof(SpellTokensSet)], round.Changes.Select(c => c.GetType()));
        Assert.Equal(4, h.P.Models[caster].SpellTokens); Assert.Empty(h.P.Units[b[0]].Conditions);             // two from round one, two more from round two (capped at 6)
        Assert.Empty(((RoundStarted)h.Log.First(e => e.Payload is RoundStarted).Payload).Changes);   // round 0 carried none
    }

    [Fact]
    public void Free_for_all_deploys_by_roll_off_then_orders_rounds_by_who_finished_first()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 1, 1), TestArmies.Build("B", 2, 1), TestArmies.Build("C", 1, 1)]);
        foreach (var d in new[] { 1, 2, 3 }) h.Dice.Enqueue(d);                               // deployment: C, B, A
        h.SkipDeployment();
        Assert.Equal(1, h.Log.Count(e => e.Payload is SequenceStarted s && s.Sequence == GfNames.RollOffSeq));   // p.50: one roll-off, and it settles deployment order
        Assert.Equal([h.Teams[2], h.Teams[1], h.Teams[0]], h.P.Scheduler.Order);              // round one follows the deployment order
        for (var i = 0; i < 4; i++)                                                                        // four activations end round one
        {
            var team = h.Teams.ToList().IndexOf(h.Open.Teams[0]);
            h.Answer(team, new Chosen(TestGames.Activate(((Options)h.Open.Legal).Choices[0].Pieces[0])));
        }
        Assert.Equal((1, AskQuestions.EndRound), (h.P.Scheduler.Round, h.Open.Question));
        h.EndRound(team: 1);
        Assert.Equal(2, h.P.Scheduler.Round);
        Assert.Equal(1, h.Log.Count(e => e.Payload is SequenceStarted s && s.Sequence == GfNames.RollOffSeq));   // no roll-off at a round boundary, so no confirm gate either
        Assert.Equal([h.Teams[2], h.Teams[0], h.Teams[1]], h.P.Scheduler.Order);              // C activated first and once, A once, B twice and last
        Assert.Equal(h.Teams[2], h.P.Scheduler.ActiveTeamId);
        h.Answer(2, new Chosen(TestGames.Activate(h.Units(2)[0]))); h.Answer(0, new Chosen(TestGames.Activate(h.Units(0)[0])));
        h.Answer(1, new Chosen(TestGames.Activate(h.Units(1)[0])));
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);                                             // C and A are finished: B goes twice running
        h.Rehydrate(); Assert.Empty(h.Run());
    }
}
