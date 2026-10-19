namespace Tabletop.Geometry.Tests;

public class BaseEdgeTests
{
    public static TheoryData<string> Shapes => new() { "circle", "oval", "rect" };
    static PlacedModel Of(string shape) => shape switch
    {
        "circle" => Fx.Model(3, 4, diameter: 1.26),
        "oval" => Fx.Model(Base.Oval(2.5, 1.5), 3, 4, yaw: 30),
        _ => Fx.Model(Base.Rect(2, 1), 3, 4, yaw: 70),
    };

    [Theory, MemberData(nameof(Shapes))]
    public void Measures_as_EdgeToEdge_and_bounds_the_base(string shape)
    {
        var m = Of(shape); var edge = new BaseEdge(m);
        Assert.Equal(new Point2(3, 4), edge.Centre);
        for (var k = 0; k < 72; k++)
            foreach (var r in new[] { 0.1, 0.6, 1, 2, 7 })
            {
                var dir = new Point2(Math.Cos(k * Math.PI / 36), Math.Sin(k * Math.PI / 36)); var q = edge.Centre + dir * r;
                var d = Distance.EdgeToEdge(m, q);
                Assert.Equal(d, edge.To(q), 9);
                Assert.True(d >= r - edge.Outer - 1e-9 && d <= Math.Max(0, r - edge.Inner) + 1e-9);                     // the base lies between the two circles
            }
    }

    [Fact]
    public void A_round_base_is_its_own_bound() { var e = new BaseEdge(Fx.Model(0, 0, diameter: 1)); Assert.Equal((0.5, 0.5), (e.Inner, e.Outer)); }
}
