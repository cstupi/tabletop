using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class PendingDropsTests
{
    static readonly Guid A = Guid.CreateVersion7(), B = Guid.CreateVersion7(), Cmd = Guid.CreateVersion7();
    static OnTable At(double x, double y) => new(new Placement(new Point3(x, y, 0), new Yaw(0)));
    static ModelRelocation Move(Guid id, double x, double y) => new(id, At(0, 0), At(x, y));

    [Fact]
    public void A_dropped_model_is_held_until_its_move_arrives()
    {
        var d = new PendingDrops(); d.Add(Cmd, [Move(A, 3, 4), Move(B, 5, 6)]);
        Assert.True(d.Holds(A)); Assert.True(d.Holds(B));
        Assert.True(d.Consume(Move(A, 3, 4)));      // our own move: no animation
        Assert.False(d.Holds(A)); Assert.True(d.Holds(B));
    }

    [Fact]
    public void A_move_to_somewhere_else_releases_the_model_but_is_not_ours()
    {
        var d = new PendingDrops(); d.Add(Cmd, [Move(A, 3, 4)]);
        Assert.False(d.Consume(Move(A, 9, 9)));
        Assert.False(d.Holds(A));
    }

    [Fact]
    public void A_move_of_an_unheld_model_is_not_ours()
    {
        var d = new PendingDrops(); Assert.False(d.Consume(Move(A, 1, 1)));
    }

    [Fact]
    public void A_refusal_releases_exactly_that_commands_models()
    {
        var other = Guid.CreateVersion7(); var d = new PendingDrops();
        d.Add(Cmd, [Move(A, 1, 1)]); d.Add(other, [Move(B, 2, 2)]);
        Assert.Equal([A], d.Refuse(Cmd));
        Assert.False(d.Holds(A)); Assert.True(d.Holds(B));
        Assert.Empty(d.Refuse(Guid.CreateVersion7()));
    }

    [Fact]
    public void A_later_drop_of_the_same_model_replaces_the_earlier_one()
    {
        var d = new PendingDrops(); var second = Guid.CreateVersion7();
        d.Add(Cmd, [Move(A, 1, 1)]); d.Add(second, [Move(A, 2, 2)]);
        Assert.Empty(d.Refuse(Cmd));                 // the first command no longer holds anything
        Assert.True(d.Consume(Move(A, 2, 2)));
    }
}
