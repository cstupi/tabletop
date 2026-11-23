namespace Tabletop.Geometry.Tests;

/// <summary>
/// The Rush of three Hover Bikers (75 × 46 mm ovals, 16", Flying: through models, no cap binding) of the dev game "batchplay 1123", seq 284: they
/// stand 1.6" from the 72 × 48 table's far edge, so the table edge cuts each 16" disc near its middle. The area is every disc less what the edge
/// takes, and a move of exactly 16" lands on its boundary (the base centre's; the base's far half lies past it).
/// </summary>
public class HoverBikersRushTests
{
    static readonly TableSize Table = new(72, 48);
    static readonly ModelBody Bike = new(Base.Oval(2.952755905511811, 1.8110236220472442), 4.429133858267717);
    static readonly Point2[] Starts = [new(50.85171212624377, 46.423622047244095), new(52.862735748291016, 46.423622047244095), new(54.87375937033826, 46.423622047244095)];
    static readonly Point2[] Ends = [new(53.5355382057799, 30.650319362809178), new(55.54656236963969, 30.650319454998545), new(57.55758653349947, 30.650319547187927)];

    static List<MoveReach> Reaches() =>
        [.. Starts.Select(s => MoveReach.Build(new PlacedModel(Bike, new Placement(new Point3(s.X, s.Y, 0), new Yaw(0))), 16, [], [], null, Table))];

    static double Area(IReadOnlyList<Point2> t)
    {
        var sum = 0.0;
        for (var i = 0; i + 2 < t.Count; i += 3) sum += Math.Abs((t[i + 1] - t[i]).Cross(t[i + 2] - t[i])) / 2;
        return sum;
    }

    /// <summary>The area of a disc of radius r whose centre is d inside a straight edge (d < r): the part on the centre's side.</summary>
    static double CutDisc(double r, double d) => r * r * (Math.PI - Math.Acos(d / r)) + d * Math.Sqrt(r * r - d * d);

    [Fact] public void Each_area_is_the_disc_less_what_the_table_edge_takes()
    {
        foreach (var reach in Reaches())
        {
            var area = reach.Bands().Clear;
            // The base centre stops where the oval meets the edge: at yaw 0 its length lies along y.
            var edge = area.Max(p => p.Y);
            Assert.Equal(48 - 2.952755905511811 / 2, edge, 2);
            Assert.Equal(CutDisc(16, edge - reach.Origin.Y), Area(area), 0);
        }
    }

    [Fact] public void The_units_area_is_every_models_area_and_each_full_move_lands_on_it()
    {
        var reaches = Reaches();
        var union = MoveReach.Union(reaches);
        Assert.Empty(union.Difficult);
        Assert.Empty(union.Dangerous);
        Assert.Equal(Starts.Min(s => s.X) - 16, union.Clear.Min(p => p.X), 1);
        Assert.Equal(Starts.Max(s => s.X) + 16, union.Clear.Max(p => p.X), 1);
        Assert.Equal(Starts[0].Y - 16, union.Clear.Min(p => p.Y), 1);
        Assert.True(Area(union.Clear) > Area(reaches[0].Bands().Clear) + 2 * 2 * 16);
        for (var k = 0; k < 3; k++)
        {
            Assert.True(reaches[k].Reaches(Ends[k]));
            Assert.Equal(16, Starts[k].DistanceTo(Ends[k]), 6);
            Assert.False(reaches[k].Reaches(Ends[k] + (Ends[k] - Starts[k]) * 0.01));
        }
    }
}
