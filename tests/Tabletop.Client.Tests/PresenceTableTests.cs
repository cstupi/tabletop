using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class PresenceTableTests
{
    [Fact]
    public void Latest_wins_and_removal_ends_existence()
    {
        var t = new PresenceTable();
        var p = Guid.CreateVersion7();
        t.Set(new Presence(p, new Cursor(new Point3(1, 1, 0), null), null, [], null));
        t.Set(new Presence(p, new Cursor(new Point3(2, 2, 0), null), null, [], null));
        Assert.Equal(2, t.Of(p)!.Cursor!.Point.X);
        Assert.Single(t.All);
        t.Remove(p);
        Assert.Null(t.Of(p)); Assert.Empty(t.All);
    }
}
