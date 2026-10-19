using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class AnimationQueueTests
{
    static ModelRelocation Move(double x) =>
        new(Guid.CreateVersion7(), new OnTable(new Placement(new Point3(0, 0, 0), new Yaw(0))), new OnTable(new Placement(new Point3(x, 0, 0), new Yaw(0))));

    [Fact]
    public void Every_model_of_a_multi_model_move_is_relocated()
    {
        var moves = new[] { Move(1), Move(2), Move(3) };
        Assert.Equal(moves, AnimationQueue.Relocations(new GameEvent(1, null, new ModelsMoved(moves), null)));
    }

    [Fact]
    public void A_step_relocates_every_model_of_each_of_its_moves_in_order()
    {
        var (a, b, c) = (Move(1), Move(2), Move(3));
        var step = new StepCompleted(Guid.CreateVersion7(), "move", null, [], [new ModelsMoved([a, b]), new ScoreSet(Guid.CreateVersion7(), 1), new ModelsMoved([c])]);
        Assert.Equal([a, b, c], AnimationQueue.Relocations(new GameEvent(1, null, step, null)));
    }

    [Fact]
    public void Clear_drops_everything_queued()
    {
        var q = new AnimationQueue();
        q.Enqueue([new GameEvent(1, null, new ScoreSet(Guid.CreateVersion7(), 1), null), new GameEvent(2, null, new ScoreSet(Guid.CreateVersion7(), 2), null)]);
        q.Clear();
        Assert.Equal(0, q.Count);
        Assert.Empty(q.Take(animate: true));
    }

    [Fact]
    public void Other_events_relocate_nothing() =>
        Assert.Empty(AnimationQueue.Relocations(new GameEvent(1, null, new ScoreSet(Guid.CreateVersion7(), 1), null)));
}
