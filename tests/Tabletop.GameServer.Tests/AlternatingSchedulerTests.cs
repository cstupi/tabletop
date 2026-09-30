using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

public class AlternatingSchedulerTests
{
    static SchedulerHarness TwoTeams(int unitsA = 2, int unitsB = 1) => new([TestArmies.Build("A", unitsA, 2), TestArmies.Build("B", unitsB, 2)]);

    [Fact]
    public void A_fresh_game_rolls_off_then_alternates_deployment_skipping_a_team_with_nothing_left_then_starts_round_one()
    {
        var h = TwoTeams(); h.Dice.Enqueue(1); h.Dice.Enqueue(6);
        h.Run();
        Assert.Equal(["sequence_started", "step_completed", "ask_opened"], h.Kinds(0));                   // the roll-off waits at its confirm gate
        Assert.Equal((AskQuestions.Confirm, 0, (Guid?)null), (h.Open.Question, h.P.Scheduler.Round, h.P.Scheduler.ActiveTeamId));
        var gate = h.P.LastSeq; h.ConfirmRollOff(team: 1);
        Assert.Equal(["ask_answered", "ask_closed", "step_completed", "sequence_finished", "round_started", "phase_started", "turn_started", "ask_opened"], h.Kinds(gate));
        Assert.Equal((0, "deployment", h.Teams[1]), (h.P.Scheduler.Round, h.P.Scheduler.Phase, h.P.Scheduler.ActiveTeamId));    // B rolled 6
        Assert.Equal([h.Teams[1], h.Teams[0]], h.P.Scheduler.Order);
        var b = h.Units(1)[0]; var a = h.Units(0);
        h.Answer(1, new SelectedPieces([b]));
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);                                              // waiting for the models
        var seq = h.P.LastSeq; h.Deploy(b);
        Assert.Equal(["models_moved", "turn_ended", "turn_started", "ask_opened"], h.Kinds(seq));
        Assert.Equal(h.Teams[0], h.Open.Teams.Single());
        h.Answer(0, new SelectedPieces([a[0]])); h.Deploy(a[0]);
        Assert.Equal(h.Teams[0], h.Open.Teams.Single());                                                   // B has nothing left: skipped
        h.Answer(0, new SelectedPieces([a[1]])); seq = h.P.LastSeq; h.Deploy(a[1]);
        Assert.Equal(["models_moved", "turn_ended", "phase_started", "round_started", "phase_started", "turn_started", "ask_opened"], h.Kinds(seq));
        Assert.Equal((1, SchedulerBase.RoundsPhase, h.Teams[1]), (h.P.Scheduler.Round, h.P.Scheduler.Phase, h.P.Scheduler.ActiveTeamId));  // the winner takes round one's first turn
        Assert.Equal(AskQuestions.Activate, h.Open.Question);
        h.Rehydrate(); Assert.Empty(h.Run());
    }

    [Fact]
    public void Turns_alternate_one_activation_each_the_round_ends_when_everyone_is_finished_and_records_clear_at_the_next_round()
    {
        var h = TwoTeams(); h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.SkipDeployment();
        var a = h.Units(0); var b = h.Units(1);
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);
        var seq = h.P.LastSeq; h.Answer(0, new Chosen(TestGames.Activate(a[0], "Advance")));
        Assert.Equal(["ask_answered", "ask_closed", "action_recorded", "sequence_started", "sequence_finished", "turn_ended", "turn_started", "ask_opened"], h.Kinds(seq));
        Assert.Equal((a[0], "Advance", "advance"), h.Log.Select(e => e.Payload).OfType<SequenceStarted>().Select(s => (s.Context.ActorUnitId, s.Context.Action, s.Sequence)).Single(s => s.Action is not null));   // the roll-off is the other frame
        Assert.Contains("Advance", h.P.Units[a[0]].Actions);
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);
        h.Answer(1, new Chosen(TestGames.Activate(b[0])));
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);
        Assert.Single(Assert.IsType<Options>(h.Open.Legal).Choices.Select(c => c.Pieces[0]).Distinct());   // only a[1] remains
        seq = h.P.LastSeq; h.Answer(0, new Chosen(TestGames.Activate(a[1])));
        Assert.Equal(["ask_answered", "ask_closed", "action_recorded", "sequence_started", "sequence_finished", "turn_ended", "ask_opened"], h.Kinds(seq));
        Assert.Equal((AskQuestions.EndRound, false, 1), (h.Open.Question, h.Open.AllowPass, h.P.Scheduler.Round));   // the round waits to be ended
        Assert.Equal(h.Teams, h.Open.Teams);                                                               // every team that has a participant; any one of them may answer
        Assert.Empty(h.Run());
        seq = h.P.LastSeq; h.EndRound(team: 1);                                                            // the team that did not finish first ends it: any player, any team
        Assert.Equal(["ask_answered", "ask_closed", "round_started", "turn_started", "ask_opened"], h.Kinds(seq));
        var round2 = (RoundStarted)h.Log.First(e => e.Seq > seq && e.Payload is RoundStarted).Payload;
        Assert.Equal((2, 3), (round2.Round, round2.Changes.Count(c => c is ActionRecordCleared)));
        Assert.Equal([h.Teams[1], h.Teams[0]], round2.Order);                                              // B finished first
        Assert.All(h.P.Units.Values, u => Assert.Empty(u.Actions));
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);
        h.Dispatch(new Undo(seq));                                                                 // undo across the boundary: the answer is a logged fact, so replay un-ends the round
        Assert.Equal((1, AskQuestions.EndRound), (h.P.Scheduler.Round, h.Open.Question));
        Assert.All(h.P.Units.Values, u => Assert.NotEmpty(u.Actions));                             // and round two's record clearing goes with it
        h.EndRound();
        Assert.Equal(2, h.P.Scheduler.Round);
        h.Rehydrate(); Assert.Empty(h.Run());
    }

    [Fact]
    public void A_pass_stands_for_the_round_and_a_cleared_record_re_enters_a_team_that_did_not_pass()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 3, 2), TestArmies.Build("B", 2, 2)]);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.SkipDeployment();
        var a = h.Units(0); var b = h.Units(1);
        h.Answer(0, new Chosen(TestGames.Activate(a[0]))); h.Answer(1, new Chosen(TestGames.Activate(b[0]))); h.Answer(0, new Chosen(TestGames.Activate(a[1])));
        var seq = h.P.LastSeq; h.Answer(1, new Passed());
        Assert.Equal(["ask_answered", "ask_closed", "turn_ended", "turn_started", "ask_opened"], h.Kinds(seq));
        Assert.True(((TurnEnded)h.Log.First(e => e.Seq > seq && e.Payload is TurnEnded).Payload).Passed);
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);
        h.Dispatch(new ClearActionRecord(b[0]));                                                          // a menu edit while B has passed
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId); Assert.Single(h.P.OpenAsks);
        h.Answer(0, new Chosen(TestGames.Activate(a[2])));
        Assert.Equal((AskQuestions.EndRound, 1), (h.Open.Question, h.P.Scheduler.Round));                 // B was not re-asked: the pass stood, and the round waits
        h.EndRound();
        Assert.Equal(2, h.P.Scheduler.Round);
        Assert.Equal([h.Teams[1], h.Teams[0]], h.P.Scheduler.Order);                                      // B finished (passed) before A's last turn
        h.Answer(1, new Chosen(TestGames.Activate(b[0]))); h.Answer(0, new Chosen(TestGames.Activate(a[0])));
        h.Answer(1, new Chosen(TestGames.Activate(b[1]))); h.Answer(0, new Chosen(TestGames.Activate(a[1])));
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);                                             // B's legal set is empty: A goes again
        h.Dispatch(new ClearActionRecord(b[0]));
        h.Answer(0, new Chosen(TestGames.Activate(a[2])));
        Assert.Equal((2, h.Teams[1]), (h.P.Scheduler.Round, h.P.Scheduler.ActiveTeamId));                 // B re-entered at its slot
        h.Rehydrate(); Assert.Empty(h.Run());
    }

    [Fact]
    public void Hand_moves_never_end_a_turn_and_undo_reopens_what_it_undid()
    {
        var h = TwoTeams(); h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.SkipDeployment();
        var a = h.Units(0);
        var seq = h.P.LastSeq;
        h.Dispatch(TestGames.Move(h.State, a[0]), team: 0);
        Assert.Equal(["models_moved"], h.Kinds(seq));
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);
        var before = h.P.LastSeq;
        h.Answer(0, new Passed());
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);
        h.Dispatch(new Undo(before));
        Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);
        Assert.Equal(AskQuestions.Activate, h.Open.Question); Assert.Equal(h.Teams[0], h.Open.Teams.Single());
        Assert.DoesNotContain(h.Teams[0], h.P.Scheduler.Passed);
    }

    [Fact]
    public void Set_active_team_and_set_round_from_the_menu_are_followed()
    {
        var h = TwoTeams(); h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.SkipDeployment();
        h.Dispatch(new SetActiveTeam(h.Teams[1]));
        Assert.Equal(h.Teams[1], h.Open.Teams.Single());                                                  // a fresh Ask for the team the menu named
        Assert.Single(h.P.OpenAsks);
        h.Dispatch(new SetRound(4));
        Assert.Equal((4, h.Teams[0]), (h.P.Scheduler.Round, h.P.Scheduler.ActiveTeamId));                  // the boundary hook ran (nothing to do) and Order[0] took the first turn
        Assert.Single(h.P.OpenAsks);                                                                       // B's superseded Ask closed
    }

    [Fact]
    public void Under_strict_an_out_of_set_answer_is_refused_and_the_turn_stands()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 2, 2), TestArmies.Build("B", 1, 2)], enforcement: Enforcement.Strict);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.SkipDeployment();
        var a = h.Units(0); var ask = h.Open; var seq = h.P.LastSeq;
        Assert.Throws<InvalidOperationException>(() => h.Answer(0, new Chosen(TestGames.Activate(a[0], "Teleport"))));   // OutsideLegalSet under strict: refused by the dispatcher
        Assert.Equal(seq, h.P.LastSeq);                                                                        // nothing logged, so the scheduler never saw it
        Assert.Equal((ask.Id, h.Teams[0]), (h.Open.Id, h.P.Scheduler.ActiveTeamId!.Value));                     // the same Ask stands
        h.Answer(0, new Chosen(TestGames.Activate(a[0])));
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);
    }

    /// <summary>
    /// An answer the ruleset cannot read takes the same three positions as an out-of-set one (ruling 3), because it is
    /// decided in the same place. The id here is both — which is what a real game produces, since a legal set only ever offers well-formed
    /// ids for units the log has. What the readability clause adds on its own is a legal set offering an id its own ruleset cannot read.
    /// </summary>
    [Theory]
    [InlineData(Enforcement.Strict, false)] [InlineData(Enforcement.Warn, true)] [InlineData(Enforcement.Silent, true)]
    public void An_unreadable_answer_follows_the_enforcement_dial(Enforcement enforcement, bool applied)
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 2, 2), TestArmies.Build("B", 1, 2)], enforcement: enforcement);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.SkipDeployment();
        var ask = h.Open; var seq = h.P.LastSeq;
        if (!applied)
        {
            Assert.Throws<InvalidOperationException>(() => h.Answer(0, new Chosen("not-an-option")));
            Assert.Equal(seq, h.P.LastSeq);                                                                    // refused before anything was written
            Assert.Equal((ask.Id, h.Teams[0]), (h.Open.Id, h.P.Scheduler.ActiveTeamId!.Value));                 // the same Ask stands and the turn is still A's: nothing is waiting on it
            Assert.Throws<InvalidOperationException>(() => h.Answer(0, new Chosen("not-an-option")));           // and again, as often as the client likes
            h.Answer(0, new Chosen(TestGames.Activate(h.Units(0)[0])));                                                   // a readable answer moves the game on
            Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);
            return;
        }
        h.Answer(0, new Chosen("not-an-option"));
        Assert.True(((AskAnswered)h.Log.First(e => e.Seq > seq && e.Payload is AskAnswered).Payload).OutsideLegalSet);   // the warn warning rides on this; the string itself is the dispatcher's, pinned by CommandDispatcherTests
        var ended = (TurnEnded)h.Log.First(e => e.Seq > seq && e.Payload is TurnEnded).Payload;
        Assert.Equal((h.Teams[0], true), (ended.TeamId, ended.Passed));                                        // read as a pass, which stands for the round
        Assert.Equal(h.Teams[1], h.P.Scheduler.ActiveTeamId);
        h.Answer(1, new Chosen(TestGames.Activate(h.Units(1)[0])));
        Assert.Equal((AskQuestions.EndRound, 1), (h.Open.Question, h.P.Scheduler.Round));                      // A passed and B is done: the round waits for a player, not for A's turn
    }

    [Fact]
    public void Teams_with_nothing_are_skipped_spectators_are_never_asked_and_a_round_nobody_can_act_in_waits_to_be_ended()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 1, 1), null], spectators: 1);
        h.Dice.Enqueue(3); h.Dice.Enqueue(5);
        h.Run(); h.ConfirmRollOff();
        Assert.Equal(h.Teams[0], h.Open.Teams.Single());                                                  // B won the roll-off but has nothing to deploy
        h.SkipDeployment();
        var a0 = h.Units(0)[0];
        h.Answer(0, new Chosen(TestGames.Activate(a0)));
        Assert.Equal((AskQuestions.EndRound, 1), (h.Open.Question, h.P.Scheduler.Round));                  // B never had a turn, so it is not asked to act — but it is asked to end the round
        h.EndRound(team: 1);
        Assert.Equal(2, h.P.Scheduler.Round);
        h.Dispatch(new RecordAction(a0, "Hold"));                                                          // marked from the menu: the open Ask stands
        Assert.Empty(h.Run()); Assert.Equal(h.Teams[0], h.P.Scheduler.ActiveTeamId);
        h.Dispatch(new MoveModels(h.P.Units[a0].Definition.Models.Select(m => new ModelRelocation(m.Id, h.P.Models[m.Id].Location, new OnShelf())).ToList()), team: 0);
        h.Answer(0, new Passed());
        h.EndRound();
        Assert.Equal((3, (Guid?)null, AskQuestions.EndRound), (h.P.Scheduler.Round, h.P.Scheduler.ActiveTeamId, h.Open.Question));   // round 3 began; nobody can act, so it waits at its own boundary
        Assert.Empty(h.Run());
        var asked = h.Log.Select(e => e.Payload).OfType<AskOpened>().Select(o => o.Ask).ToList();
        Assert.All(asked.Where(a => a.FrameId is null && a.Question != AskQuestions.EndRound), a => Assert.Equal([h.Teams[0]], a.Teams));   // only A is ever asked to deploy or activate
        Assert.All(asked.Where(a => a.FrameId is null && a.Question == AskQuestions.EndRound), a => Assert.Equal(h.Teams, a.Teams));        // both teams may end a round; the spectator has no team and is in no Ask
        var empty = new SchedulerHarness([null, null]); empty.Run(); empty.ConfirmRollOff();
        Assert.Equal((1, AskQuestions.EndRound), (empty.P.Scheduler.Round, empty.Open.Question));          // round one began and stopped at its boundary
        Assert.Empty(empty.Run());
        empty.EndRound();
        Assert.Equal(2, empty.P.Scheduler.Round);                                                          // rounds count up only when a player says so
    }
}
