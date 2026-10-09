using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class MarkerLocksTests
{
    static readonly GameInfo Game = Samples.Game;
    static readonly Marker PlayObjective = new(Guid.CreateVersion7(), MarkerKind.Objective, new Point3(5, 5, 0), new Yaw(0), 3, null, VisualRef.OfBuiltin("objective-ring"));
    static Projection P()
    {
        var a = new EventApplier(Samples.Board, Game);
        a.ApplyAll(new EventPayload[] { new MarkerPlaced(Samples.Token), new MarkerPlaced(PlayObjective) }.Select((x, i) => new GameEvent(i + 1, Samples.P1, x, null)));
        return a.Projection;
    }
    static Guid SetupObjective(Projection p) => p.Board.Table.Markers.First(m => m.Kind == MarkerKind.Objective).Id;

    [Theory]
    [InlineData(GameStatus.Playing)]
    [InlineData(GameStatus.Ended)]
    public void Every_objective_is_locked_from_play_on(GameStatus status)
    {
        var locks = new MarkerLocks(); var p = P();
        Assert.True(locks.IsLocked(status, p, SetupObjective(p)));
        Assert.True(locks.IsLocked(status, p, PlayObjective.Id));
        Assert.False(locks.MayDrag(status, p, PlayObjective.Id));
    }

    [Theory]
    [InlineData(GameStatus.Lobby)]
    [InlineData(GameStatus.Setup)]
    public void Setup_moves_markers_freely(GameStatus status)
    {
        var locks = new MarkerLocks(); var p = P();
        Assert.False(locks.IsLocked(status, p, SetupObjective(p)));
        Assert.True(locks.MayDrag(status, null, SetupObjective(p)));   // setup draws the draft: no projection
    }

    [Fact]
    public void Tokens_and_unknown_ids_are_free()
    {
        var locks = new MarkerLocks(); var p = P();
        Assert.True(locks.MayDrag(GameStatus.Playing, p, Samples.Token.Id));
        Assert.True(locks.MayDrag(GameStatus.Playing, p, Guid.CreateVersion7()));
        Assert.True(locks.MayDrag(GameStatus.Playing, p, Samples.Model));
        Assert.Null(locks.ItemFor(GameStatus.Playing, p, Samples.Token.Id));
        Assert.Null(locks.ItemFor(GameStatus.Playing, p, Guid.CreateVersion7()));
        Assert.Null(locks.ItemFor(GameStatus.Setup, p, PlayObjective.Id));
    }

    [Fact]
    public void Unlock_frees_one_objective_until_it_is_locked_again()
    {
        var locks = new MarkerLocks(); var p = P(); var changed = 0; locks.Changed += () => changed++;
        var unlock = locks.ItemFor(GameStatus.Playing, p, PlayObjective.Id)!;
        Assert.Equal("Unlock", unlock.Label);
        Assert.Equal(MenuInput.None, unlock.Input);
        Assert.Null(unlock.Build(null));                                 // a local action: nothing on the wire
        Assert.True(locks.Chosen(unlock));
        Assert.Equal(1, changed);
        Assert.True(locks.MayDrag(GameStatus.Playing, p, PlayObjective.Id));
        Assert.True(locks.IsLocked(GameStatus.Playing, p, SetupObjective(p)));   // the other stays locked
        var lockItem = locks.ItemFor(GameStatus.Playing, p, PlayObjective.Id)!;
        Assert.Equal("Lock", lockItem.Label);
        Assert.True(locks.Chosen(lockItem));
        Assert.False(locks.MayDrag(GameStatus.Playing, p, PlayObjective.Id));
        Assert.Equal(2, changed);
        Assert.False(locks.Chosen(new MenuItem("Unlock", MenuInput.None, _ => null)));   // not one of its items
    }

    [Fact]
    public void The_marker_menu_carries_the_lock_item_last()
    {
        var locks = new MarkerLocks(); var p = P(); var setup = SetupObjective(p);
        Assert.Equal(["Give to Team 1", "Give to Team 2", "Make neutral", "Unlock"], MenuModel.ForMarker(Game, p, setup, locks).Select(i => i.Label));
        Assert.Equal(["Remove"], MenuModel.ForMarker(Game, p, Samples.Token.Id, locks).Select(i => i.Label));
        locks.Chosen(MenuModel.ForMarker(Game, p, setup, locks)[^1]);
        Assert.Equal("Lock", MenuModel.ForMarker(Game, p, setup, locks)[^1].Label);
    }

    [Fact]
    public void A_moved_setup_objective_is_still_not_removable()
    {
        var board = Samples.Board; var a = new EventApplier(board, Game);
        var setup = board.Table.Markers.First(m => m.Kind == MarkerKind.Objective).Id;
        a.Apply(new GameEvent(1, Samples.P1, new MarkerMoved(setup, new Point3(9, 9, 0), new Yaw(0)), null));
        Assert.DoesNotContain("Remove", MenuModel.ForMarker(Game, a.Projection, setup).Select(i => i.Label));
    }
}
