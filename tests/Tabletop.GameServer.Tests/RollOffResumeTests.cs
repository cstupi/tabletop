using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Roll-off result spec → No gate: a game whose log stands at the roll-off's old every-team Confirm (started, rolled, unfinished) resumes by
/// running the ranking over the dice already rolled. The gate is closed unanswered, since the wait setting is off and nothing asks for it now.
/// </summary>
public class RollOffResumeTests
{
    /// <summary>The log as the interpreter wrote it while every roll-off stopped at its gate: A rolled 2, B rolled 5.</summary>
    static (SchedulerHarness H, Guid Gate) AtTheOldGate()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 1, 1), TestArmies.Build("B", 1, 1)]);
        var frame = Guid.CreateVersion7();
        var roll = new RollRecord(Guid.CreateVersion7(), GfNames.RollOff, null, [], [new Die(2, 2, false), new Die(5, 5, false)]);
        var gate = new Ask(Guid.CreateVersion7(), AskQuestions.Confirm, $"Continue: {GfNames.RollOffOrderStep}", h.Teams, new Confirmation(), false, false, null, frame);
        h.Append([
            new SequenceStarted(frame, GfNames.RollOffSeq, new SequenceContext(null, [], [], null)),
            new StepCompleted(frame, GfNames.RollOffRollStep, null, [roll], []),
            new AskOpened(gate)]);
        return (h, gate.Id);
    }

    [Fact]
    public void A_log_at_the_old_gate_closes_it_unanswered_and_ranks_the_dice_already_rolled()
    {
        var (h, gate) = AtTheOldGate();
        var at = h.P.LastSeq;
        h.Rehydrate();                                                                                     // the game is loaded from that log
        h.Run();
        Assert.Equal(["ask_closed", "step_completed", "sequence_finished", "round_started", "phase_started", "turn_started", "ask_opened"], h.Kinds(at));
        var closed = h.Log.Select(e => e.Payload).OfType<AskClosed>().Single();
        Assert.Equal((gate, 0), (closed.AskId, closed.Answers.Count));
        Assert.Equal(2, DiceCursor.DrawnSoFar(h.State));                                                   // nothing re-rolled
        Assert.Equal([h.Teams[1], h.Teams[0]], h.P.Scheduler.Order);                                       // B's 5 beats A's 2
        Assert.Equal((AskQuestions.Deploy, h.Teams[1]), (h.Open.Question, h.Open.Teams.Single()));
        h.Rehydrate(); Assert.Empty(h.Run());
    }
}
