using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

/// <summary>A refused move or placement commit comes back where the player dropped it.</summary>
public class CommitHoldTests
{
    static readonly Guid Ask = Guid.CreateVersion7(), A = Guid.CreateVersion7(), B = Guid.CreateVersion7(), Cmd = Guid.CreateVersion7(), Other = Guid.CreateVersion7();
    static OnTable At(double x, double y, double yaw = 0) => new(new Placement(new Point3(x, y, 0), new Yaw(yaw)));

    [Fact]
    public void A_refusal_of_the_commit_hands_back_its_ghosts_and_the_models_it_moved()
    {
        var hold = new CommitHold();
        hold.Remember(Ask, [new(A, At(0, 0), At(5, 0, 90)), new(B, At(2, 0), At(2, 0))]);
        hold.Sent(Cmd);
        Assert.Null(hold.Refused(Other));
        var held = hold.Refused(Cmd)!;
        Assert.Equal(Ask, held.Key);
        Assert.Equal(new Point3(5, 0, 0), held.At[A]);
        Assert.Equal(new Yaw(90), held.Yaws[A]);
        Assert.Equal([A], held.Moved);
        Assert.Null(hold.Refused(Cmd));                                                   // once
    }

    [Fact]
    public void A_placement_from_the_shelf_moves_every_model()
    {
        var hold = new CommitHold();
        hold.Remember(A, [new(A, new OnShelf(), At(5, 0)), new(B, new OnShelf(), At(6, 0))]);
        hold.Sent(Cmd);
        Assert.Equal([A, B], hold.Refused(Cmd)!.Moved);
    }

    [Fact]
    public void A_stop_before_moving_marks_every_model()
    {
        var hold = new CommitHold();
        hold.Remember(Ask, [new(A, At(0, 0), At(0, 0)), new(B, At(2, 0), At(2, 0))]);
        hold.Sent(Cmd);
        Assert.Equal([A, B], hold.Refused(Cmd)!.Moved);
    }

    [Fact]
    public void Another_send_after_the_commit_drops_it()
    {
        var hold = new CommitHold();
        hold.Remember(Ask, [new(A, At(0, 0), At(5, 0))]);
        hold.Sent(Cmd);
        hold.Sent(Other);                                                                 // a later answer that remembered nothing
        Assert.Null(hold.Refused(Other));
        Assert.Null(hold.Refused(Cmd));
    }
}
