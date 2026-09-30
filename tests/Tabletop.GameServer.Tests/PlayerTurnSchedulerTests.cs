using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

public class PlayerTurnSchedulerTests
{
    static SchedulerHarness Game(int unitsA = 2, int unitsB = 1) =>
        new([TestArmies.Build("A", unitsA, 2), TestArmies.Build("B", unitsB, 2)], scheduler: p => new PlayerTurnScheduler(p), shape: SchedulerShape.PlayerTurn);

    [Fact]
    public void Deployment_still_alternates_one_unit_a_turn_and_the_winner_opens_round_one()
    {
        var h = Game(); h.Dice.Enqueue(1); h.Dice.Enqueue(6); h.Run(); h.ConfirmRollOff();
        Assert.Equal(AskQuestions.Deploy, h.Open.Question); Assert.Equal(h.Teams[1], h.Open.Teams.Single());
        h.Answer(1, new SelectedPieces([h.Units(1)[0]])); h.Deploy(h.Units(1)[0]);
        Assert.Equal(h.Teams[0], h.Open.Teams.Single());                                                  // alternation, not "B deploys everything"
        h.SkipDeployment();
        Assert.Equal((1, h.Teams[1], AskQuestions.ActOrEndPhase, true), (h.P.Scheduler.Round, h.P.Scheduler.ActiveTeamId, h.Open.Question, h.Open.AllowPass));
    }

    [Fact]
    public void A_team_activates_every_unit_in_a_row_then_its_record_clears_at_turn_ended_and_the_other_team_goes()
    {
        var h = Game(); h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.SkipDeployment();
        var a = h.Units(0); var b = h.Units(1);
        var seq = h.P.LastSeq; h.Answer(0, new Chosen(TestGames.Activate(a[0])));
        Assert.Equal(["ask_answered", "ask_closed", "action_recorded", "sequence_started", "sequence_finished", "ask_opened"], h.Kinds(seq));   // same turn, next Ask
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);
        Assert.Equal([a[1]], Assert.IsType<Options>(h.Open.Legal).Choices.Select(c => c.Pieces[0]).Distinct());
        seq = h.P.LastSeq; h.Answer(0, new Chosen(TestGames.Activate(a[1])));
        Assert.Equal(["ask_answered", "ask_closed", "action_recorded", "sequence_started", "sequence_finished", "turn_ended", "turn_started", "ask_opened"], h.Kinds(seq));
        var ended = (TurnEnded)h.Log.First(e => e.Seq > seq && e.Payload is TurnEnded).Payload;
        Assert.False(ended.Passed);
        Assert.Equal([new ActionRecordCleared(a[0]), new ActionRecordCleared(a[1])], ended.Changes.OrderBy(c => a.ToList().IndexOf(((ActionRecordCleared)c).UnitId)).ToList());
        Assert.All(a, u => Assert.Empty(h.P.Units[u].Actions));
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);
        seq = h.P.LastSeq; h.Answer(1, new Chosen(TestGames.Activate(b[0])));
        Assert.Equal(["ask_answered", "ask_closed", "action_recorded", "sequence_started", "sequence_finished", "turn_ended", "ask_opened"], h.Kinds(seq));
        Assert.Equal(AskQuestions.EndRound, h.Open.Question); Assert.Equal(h.Teams, h.Open.Teams);                   // both shapes stop at the boundary and both ask every team
        seq = h.P.LastSeq; h.EndRound();
        Assert.Equal(["ask_answered", "ask_closed", "round_started", "turn_started", "ask_opened"], h.Kinds(seq));
        var round2 = (RoundStarted)h.Log.First(e => e.Seq > seq && e.Payload is RoundStarted).Payload;
        Assert.Equal((2, 0), (round2.Round, round2.Changes.Count));                                        // nothing to clear at the round: the turns cleared
        Assert.Equal([h.Teams[0], h.Teams[1]], round2.Order);                                              // A's turn ended first
        h.Rehydrate(); Assert.Empty(h.Run());
    }

    [Fact]
    public void Passing_ends_the_turn_with_units_left_and_the_team_is_not_asked_again_this_round()
    {
        var h = Game(); h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.SkipDeployment();
        var a = h.Units(0);
        h.Answer(0, new Chosen(TestGames.Activate(a[0])));
        var seq = h.P.LastSeq; h.Answer(0, new Passed());
        Assert.Equal(["ask_answered", "ask_closed", "turn_ended", "turn_started", "ask_opened"], h.Kinds(seq));
        var ended = (TurnEnded)h.Log.First(e => e.Seq > seq && e.Payload is TurnEnded).Payload;
        Assert.True(ended.Passed); Assert.Single(ended.Changes);                                            // a[0]'s record cleared; a[1] never activated
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);
        h.Dispatch(new ClearActionRecord(a[0]));
        h.Answer(1, new Chosen(TestGames.Activate(h.Units(1)[0])));
        Assert.Equal((1, AskQuestions.EndRound), (h.P.Scheduler.Round, h.Open.Question));                 // A was not re-asked in round one; the round waits for a player
        h.EndRound();
        Assert.Equal(2, h.P.Scheduler.Round);
    }

    [Fact]
    public void A_team_with_nothing_to_do_is_skipped_and_a_round_with_no_turns_waits_to_be_ended()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 1, 1), null], scheduler: p => new PlayerTurnScheduler(p), shape: SchedulerShape.PlayerTurn);
        h.Dice.Enqueue(1); h.Dice.Enqueue(6); h.SkipDeployment();
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);                                             // B (the winner) has nothing: skipped
        h.Answer(0, new Chosen(TestGames.Activate(h.Units(0)[0])));
        Assert.Equal(AskQuestions.EndRound, h.Open.Question);
        h.EndRound(team: 1);                                                                              // the team that never had a turn may still end the round
        Assert.Equal((2, h.Teams[0]), (h.P.Scheduler.Round, h.P.Scheduler.ActiveTeamId));
        var a0 = h.Units(0)[0];
        h.Dispatch(new MoveModels(h.P.Units[a0].Definition.Models.Select(m => new ModelRelocation(m.Id, h.P.Models[m.Id].Location, new OnShelf())).ToList()), team: 0);
        h.Answer(0, new Passed());
        h.EndRound();
        Assert.Equal((3, (Guid?)null, AskQuestions.EndRound), (h.P.Scheduler.Round, h.P.Scheduler.ActiveTeamId, h.Open.Question));   // nobody can act: it waits here, it does not count up
        Assert.Empty(h.Run());
    }

    [Fact]
    public void Three_teams_of_one_each_take_one_turn_a_round_in_the_deployment_order()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 2, 1), TestArmies.Build("B", 1, 1), TestArmies.Build("C", 1, 1)],
            scheduler: p => new PlayerTurnScheduler(p), shape: SchedulerShape.PlayerTurn);
        foreach (var d in new[] { 3, 2, 1 }) h.Dice.Enqueue(d);                                // deployment A, B, C; p.50 gives round one that same order
        h.SkipDeployment();
        Assert.Equal([h.Teams[0], h.Teams[1], h.Teams[2]], h.P.Scheduler.Order);
        var a = h.Units(0);
        h.Answer(0, new Chosen(TestGames.Activate(a[0])));
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);                                             // A holds the turn for its second unit
        h.Answer(0, new Chosen(TestGames.Activate(a[1])));
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);
        h.Answer(1, new Chosen(TestGames.Activate(h.Units(1)[0])));
        Assert.Equal(h.Teams[2], h.P.Scheduler.ActiveTeamId);
        h.Answer(2, new Chosen(TestGames.Activate(h.Units(2)[0])));
        Assert.Equal(AskQuestions.EndRound, h.Open.Question); h.EndRound(team: 2);
        Assert.Equal((2, h.Teams[0]), (h.P.Scheduler.Round, h.P.Scheduler.ActiveTeamId));
        Assert.Equal([h.Teams[0], h.Teams[1], h.Teams[2]], h.P.Scheduler.Order);                          // finished-first repeats the order here: under player-turn a team finishes when its units run out
        Assert.Equal(1, h.Log.Count(e => e.Payload is SequenceStarted s && s.Sequence == GfNames.RollOffSeq));   // p.50: the only roll-off was for deployment
        h.Rehydrate(); Assert.Empty(h.Run());
    }
}
