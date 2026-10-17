using Tabletop.Client.Core; using Tabletop.Geometry;
namespace Tabletop.Client.Tests;

public class GhostStashTests
{
    static readonly Guid Ask = Guid.CreateVersion7(), M1 = Guid.CreateVersion7(), M2 = Guid.CreateVersion7();

    [Fact]
    public void Ghosts_kept_across_a_reconnect_come_back_once_for_the_same_key_with_their_facings()
    {
        var stash = new GhostStash();
        stash.Keep(Ask, new Dictionary<Guid, Point3> { [M1] = new(1, 2, 0), [M2] = new(3, 4, 0) }, new Dictionary<Guid, Yaw> { [M1] = new(90) });
        Assert.Null(stash.Take(Guid.CreateVersion7()));                  // another Ask: the stash is spent
        stash.Keep(Ask, new Dictionary<Guid, Point3> { [M1] = new(1, 2, 0), [M2] = new(3, 4, 0) }, new Dictionary<Guid, Yaw> { [M1] = new(90) });
        var back = stash.Take(Ask)!;
        Assert.Equal(new KeptGhost(new Point3(1, 2, 0), new Yaw(90)), back[M1]);
        Assert.Null(back[M2].Yaw);                                       // never turned: the facing the render gives it
        Assert.Null(stash.Take(Ask));
    }
}
