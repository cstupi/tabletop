using Tabletop.Geometry;

namespace Tabletop.Content.Tests;

public class TableSidesTests
{
    static readonly TableSize Opr = new(72, 48);

    internal static IReadOnlyList<DeploymentZone> Zones(string style, int count = 0) =>
        ZoneStyles.ShapesOf(style).Take(count == 0 ? int.MaxValue : count).Select((s, i) => new DeploymentZone(Guid.NewGuid(), s, i + 1)).ToList();

    static TerrainPlacement Piece(string name, double x, double y) =>
        new(Guid.NewGuid(), VisualRef.OfBuiltin(name), new Point3(x, y, 0), new Yaw(0), 1, [new TerrainVolume(Guid.NewGuid(), RepoCatalog.Terrain[name].Volume)]);

    [Fact]
    public void Frontline_anchors_are_the_long_edge_bands()
    {
        var a = TableSides.Anchors(Opr, Zones("frontline"));
        Assert.Equal(new Point2(36, 42), a[1]);
        Assert.Equal(new Point2(36, 6), a[2]);
        Assert.Equal(a, TableSides.Anchors(Opr, []));
    }

    [Fact]
    public void Free_for_all_has_four_corner_anchors()
    {
        var a = TableSides.Anchors(Opr, Zones("free-for-all"));
        Assert.Equal(4, a.Count);
        Assert.Equal(new Point2(12, 42), a[1]);
        Assert.Equal(new Point2(60, 42), a[2]);
        Assert.Equal(new Point2(60, 6), a[3]);
        Assert.Equal(new Point2(12, 6), a[4]);
    }

    [Fact]
    public void The_centre_line_is_a_tie()
    {
        var a = TableSides.Anchors(Opr, Zones("frontline"));
        Assert.Null(TableSides.SideOf(a, new Point2(10, 24)));
        Assert.Equal(1, TableSides.SideOf(a, new Point2(10, 30)));
        Assert.Equal(2, TableSides.SideOf(a, new Point2(10, 18)));
    }

    [Fact]
    public void Shares_split_a_tied_piece()
    {
        var shares = TableSides.Shares(Opr, Zones("frontline"), [Piece("large-forest", 36, 24)]);
        Assert.Equal(new TerrainShare(0.5, 0.5, 0.5), shares[1]);
        Assert.Equal(shares[1], shares[2]);
    }
}
