using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using static Tabletop.GameServer.Rules.GrimdarkFuture.GfNames;

namespace Tabletop.GameServer.Tests;

public class DeploymentTests
{

    [Fact]
    public void Scouts_deploy_after_every_other_unit_starting_with_the_roll_off_winner()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 2, 1, unitRules: [(2, Scout, null)]), TestArmies.Build("B", 2, 1, unitRules: [(1, Scout, null)])]);
        h.Dice.Enqueue(2); h.Dice.Enqueue(5);                                    // B wins
        h.Run(); h.ConfirmRollOff();                                             // the roll-off's gate, released with the dice as rolled
        var a = h.Units(0); var b = h.Units(1);
        h.Dispatch(new SetCondition(TestGames.UnitSubject(a[1]), SetAside, true)); h.Dispatch(new SetCondition(TestGames.UnitSubject(b[0]), SetAside, true));   // set aside from the menu
        Assert.Equal([b[0], b[1]], Assert.IsType<PieceSelection>(h.Open.Legal).Eligible);                 // B's Ask opened before the set-aside and stands as logged; B picks b[1]
        h.Answer(1, new SelectedPieces([b[1]])); h.Deploy(b[1]);
        Assert.Equal(h.Teams[0], h.Open.Teams.Single());
        Assert.Equal([a[0]], Assert.IsType<PieceSelection>(h.Open.Legal).Eligible);                       // A's Ask opened after: the set-aside Scout is not offered
        h.Answer(0, new SelectedPieces([a[0]])); var seq = h.P.LastSeq; h.Deploy(a[0]);
        Assert.Equal(["models_moved", "turn_ended", "phase_started", "turn_started", "ask_opened"], h.Kinds(seq));
        Assert.Equal((Scouts, h.Teams[1], AskQuestions.DeployScouts), (h.P.Scheduler.Phase, h.P.Scheduler.ActiveTeamId, h.Open.Question));   // the winner, not the next in rotation
        Assert.Equal([b[0]], Assert.IsType<PieceSelection>(h.Open.Legal).Eligible);
        h.Answer(1, new SelectedPieces([b[0]])); h.Deploy(b[0]);
        Assert.Equal((h.Teams[0], AskQuestions.DeployScouts), (h.P.Scheduler.ActiveTeamId, h.Open.Question));
        h.Answer(0, new SelectedPieces([a[1]])); seq = h.P.LastSeq; h.Deploy(a[1]);
        Assert.Equal(["models_moved", "turn_ended", "round_started", "phase_started", "turn_started", "ask_opened"], h.Kinds(seq));
        Assert.Equal((1, h.Teams[1]), (h.P.Scheduler.Round, h.P.Scheduler.ActiveTeamId));
        Assert.All(h.P.Units.Values, u => Assert.Empty(u.Actions));
        h.Rehydrate(); Assert.Empty(h.Run());
    }

    [Fact]
    public void The_roll_off_waits_at_a_confirm_gate_to_every_team_and_a_die_edited_there_decides_the_order()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 1, 1), TestArmies.Build("B", 1, 1)]);
        h.Dice.Enqueue(2); h.Dice.Enqueue(5);
        h.Run();
        Assert.Equal(["sequence_started", "step_completed", "ask_opened"], h.Kinds(0));                    // rolled, then held: no order and no round_started yet
        Assert.Equal((AskQuestions.Confirm, 0, (Guid?)null), (h.Open.Question, h.P.Scheduler.Round, h.P.Scheduler.ActiveTeamId));
        Assert.Equal(h.Teams, h.Open.Teams); Assert.False(h.Open.Sealed);                                  // every team, unsealed: no acting unit has a better claim to release it
        Assert.NotNull(h.Open.FrameId);                                                                    // the interpreter's Ask, so StaleAsks never touches it
        Assert.Single(h.P.Stack);
        var rolled = h.Log.Select(e => e.Payload).OfType<StepCompleted>().Single();
        Assert.Equal(GfNames.RollOffRollStep, rolled.Step); Assert.Null(rolled.Values);                    // the rolling step carries dice and no ranking
        var seq = h.P.LastSeq;
        h.Dispatch(new SetDice(rolled.FrameId, rolled.Rolls[0].Id, [new DieValue(0, 6)]), team: 0);        // A's 2 edited to a 6 while the frame is live and the roll is in a completed step
        Assert.Equal(["dice_edited"], h.Kinds(seq));
        Assert.Equal(0, h.P.Scheduler.Round);                                                              // the edit changed no order, because no order has been derived
        seq = h.P.LastSeq;
        h.ConfirmRollOff(team: 1);                                                                         // B releases A's edit: any team may
        Assert.Equal(["ask_answered", "ask_closed", "step_completed", "sequence_finished", "round_started", "phase_started", "turn_started", "ask_opened"], h.Kinds(seq));
        var order = h.Log.Select(e => e.Payload).OfType<StepCompleted>().Last();
        Assert.Equal((GfNames.RollOffOrderStep, 1, 1, 2), (order.Step, order.Values!["winner_ordinal"], order.Values["place_1"], order.Values["place_2"]));
        Assert.Equal([h.Teams[0], h.Teams[1]], h.P.Scheduler.Order);                                       // the edited die decided it: A first, where the raw dice had B
        Assert.Empty(h.P.Stack);
        h.Rehydrate(); Assert.Empty(h.Run());                                                              // and the same order comes back out of the log, with no die re-rolled
    }

    [Fact]
    public void A_set_aside_unit_that_is_neither_scout_nor_ambusher_is_simply_never_offered()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 2, 1), TestArmies.Build("B", 1, 1)]);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.Run(); h.ConfirmRollOff();
        var a = h.Units(0);
        h.Dispatch(new SetCondition(TestGames.UnitSubject(a[1]), SetAside, true));
        h.Answer(0, new SelectedPieces([a[0]])); h.Deploy(a[0]);
        h.Answer(1, new SelectedPieces([h.Units(1)[0]])); h.Deploy(h.Units(1)[0]);
        Assert.Equal(1, h.P.Scheduler.Round);                                                             // deployment ended without a[1]
        Assert.All(h.P.Units[a[1]].Definition.Models, m => Assert.IsType<OnShelf>(h.P.Models[m.Id].Location));
        h.Dispatch(new SetCondition(TestGames.UnitSubject(a[1]), SetAside, false)); h.Dispatch(TestGames.Move(h.State, a[1]), team: 0);   // the menu puts it on the table later
        Assert.DoesNotContain(Assert.IsType<Options>(h.Open.Legal).Choices, c => c.Pieces[0] == a[1]);     // A's open Ask predates the move and stands as logged
        h.Answer(0, new Passed()); h.Answer(1, new Passed()); h.EndRound();                               // both passed, then a player ended the round
        Assert.Equal((2, h.Teams[0]), (h.P.Scheduler.Round, h.P.Scheduler.ActiveTeamId));                 // A passed first, so A goes first
        Assert.Contains(Assert.IsType<Options>(h.Open.Legal).Choices, c => c.Pieces[0] == a[1]);           // the fresh set reads the table as it is
    }
}
