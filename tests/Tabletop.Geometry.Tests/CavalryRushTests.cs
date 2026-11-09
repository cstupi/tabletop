using System.Diagnostics;
using Xunit.Abstractions;

namespace Tabletop.Geometry.Tests;

/// <summary>
/// Oval bases among many bodies and surfaces (CavalryRushScene): each rider's reach area, and routes and clamps across the table, are what the
/// implementation before MoveShape's closed-form Interval and coarse bounds and MoveSpace's per-body rests answered, bit for bit.
/// </summary>
public class CavalryRushTests(ITestOutputHelper output)
{
    static readonly Dictionary<string, string> Expected = new()
    {
        ["0"] = "16464 0D68A397C083FDA93CEFB731A678AF48 83DBBB7729D11E8B65021743ACEE4443 C34116DC5BCE714567466D80070D6CE7",
        ["1"] = "16590 31EFF812B7BFF00B6E5AE5A3D4205D91 94056EAB404B1D6BA609729A5141A285 AFD3935A9C9A3C0010730113D0A8DF32",
        ["2"] = "16755 2E1DC5E83DBB2A08B5519042E5AF1A20 05145CB9BC15D32BB95532B48EC2BD99 03BD0DE55708CDB933429F0D447C2F50",
        ["3"] = "17895 21C93BDB67A0BE565A1F244151F49057 E4C8007718BEB70A0E604DAEAE4B893A 49EE463A6C4E66A3B47061C31BAFA9F2",
        ["4"] = "19527 D083CD50930651F33A95A83019F7B555 BF06C0E374C9411F32853876D023DA7D F07293DE4D609BE06ABA434C6B0BD3D9",
    };

    static string Digest(IEnumerable<Point2> points) => ReferenceScene.Digest(points.SelectMany(p => new[] { p.X, p.Y }));

    static IEnumerable<Point2> Targets()
    {
        for (var x = 14.0; x <= 50; x += 3.1)
            for (var y = 1.0; y <= 24; y += 2.3)
                yield return new Point2(x, y);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Each_riders_area_routes_and_clamps_are_what_they_were(int rider)
    {
        var reach = CavalryRushScene.Reach(rider);
        var triangles = reach.Triangles();
        var routes = Targets().SelectMany(t => reach.Route(t).Points.Append(new Point2(reach.Route(t).Distance, reach.Route(t).EntersDifficult ? 1 : 0)));
        var clamps = Targets().Select(t => reach.Clamp(t));
        var actual = $"{triangles.Count} {Digest(triangles)} {Digest(routes)} {Digest(clamps)}";
        output.WriteLine($"[\"{rider}\"] = \"{actual}\",");
        Assert.Equal(Expected[rider.ToString()], actual);
    }

    /// <summary>About 0.7 s in a debug build and 70 ms optimized; the budget leaves room for a loaded machine.</summary>
    [Fact]
    public void The_leads_area_builds_within_three_seconds_in_a_debug_build()
    {
        CavalryRushScene.Reach(1).Triangles();                                                  // warm
        var watch = Stopwatch.StartNew();
        var area = CavalryRushScene.Reach(0).Triangles();
        output.WriteLine($"{area.Count / 3} triangles, {watch.ElapsedMilliseconds} ms");
        Assert.InRange(watch.ElapsedMilliseconds, 0, 3000);
    }
}
