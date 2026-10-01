using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

/// <summary>Undo through the real dispatcher into and past the roll-off: the scheduler reads only the effective log, so what stands is what it acts on.</summary>
public class RollOffUndoTests
{
    static SchedulerHarness Rolled()
    {
        var h = new SchedulerHarness([TestArmies.Build("A", 1, 1), TestArmies.Build("B", 1, 1)]);
        h.Dice.Enqueue(2); h.Dice.Enqueue(5); h.Run(); h.ConfirmRollOff();
        return h;
    }

    static IReadOnlyList<GameEvent> Effective(SchedulerHarness h) => EventApplier.EffectiveLog(h.Log);
    static int RollOffs(SchedulerHarness h) => Effective(h).Count(e => e.Payload is SequenceStarted s && s.Sequence == GfNames.RollOffSeq);

    [Fact]
    public void Undoing_the_whole_roll_off_starts_a_fresh_one_at_the_next_advance()
    {
        var h = Rolled();
        Assert.Equal(1, h.P.Scheduler.Order.Count - 1);                                                    // the order is settled: round zero has begun
        h.Dice.Enqueue(6); h.Dice.Enqueue(1);
        h.Dispatch(new Undo(0));
        Assert.Equal(1, RollOffs(h));                                                                      // one live roll-off: the new one
        Assert.Single(h.P.Stack);
        Assert.Equal(AskQuestions.Confirm, h.Open.Question); Assert.NotNull(h.Open.FrameId);               // waiting at its gate again
        Assert.Empty(h.P.Scheduler.Order);
    }

    [Fact]
    public void Undoing_only_the_tail_of_the_roll_off_leaves_its_frame_standing_and_starts_no_second_roll_off()
    {
        var h = Rolled();
        var order = h.Log.Last(e => e.Payload is StepCompleted s && s.Step == GfNames.RollOffOrderStep);
        var finished = h.Log.Last(e => e.Payload is SequenceFinished);
        Assert.True(finished.Seq > order.Seq);
        h.Append([new Undone(order.Seq - 1)]);                                                        // the Undone the dispatcher would log, without the Run that follows a Dispatch
        Assert.DoesNotContain(Effective(h), e => e.Payload is SequenceFinished);
        Assert.Single(h.P.Stack);                                                                          // the frame stands open
        h.Run();
        Assert.Equal(1, RollOffs(h));                                                                      // it finished again; no second roll-off started
        Assert.Equal(2, h.P.Scheduler.Order.Count);
        h.Rehydrate(); Assert.Empty(h.Run());
    }
}
