using Tabletop.Client.Core;
using Tabletop.Geometry;

namespace Tabletop.Client.Tests;

public class FormationTests
{
    static readonly Base Mm25 = Base.Circle(25 / 25.4), Mm32 = Base.Circle(32 / 25.4), Mm60 = Base.Circle(60 / 25.4), Mm100 = Base.Circle(100 / 25.4);
    static readonly Point3 Centre = new(30, 20, 1.5);
    static IReadOnlyList<Base> Many(Base b, int n) => Enumerable.Repeat(b, n).ToList();
    static IReadOnlyList<PlacedModel> Placed(IReadOnlyList<Base> bases, IReadOnlyList<Point3> at, Yaw facing) =>
        bases.Select((b, i) => new PlacedModel(new ModelBody(b, 1), new Placement(at[i], facing))).ToList();
    /// <summary>Row sizes front to back: models grouped by their distance along the facing, the front (largest) first.</summary>
    static IReadOnlyList<int> RowSizes(IReadOnlyList<Point3> at, Yaw facing) =>
        at.GroupBy(p => Math.Round(new Point2(p.X - Centre.X, p.Y - Centre.Y).Rotate(new Yaw(-facing.Degrees)).Y, 3))
          .OrderByDescending(g => g.Key).Select(g => g.Count()).ToList();

    [Fact]
    public void Ten_models_in_three_rows_split_four_three_three_front_row_first()
    {
        var at = Formation.Arrange(Many(Mm32, 10), 3, Centre, new Yaw(0));
        Assert.Equal([4, 3, 3], RowSizes(at, new Yaw(0)));
        Assert.All(at.Take(4), p => Assert.Equal(at[0].Y, p.Y, 6));       // input order: the first four are the front row
        Assert.True(at[0].Y > at[4].Y);                                   // yaw 0 faces +Y: the front row is the furthest +Y
    }

    [Fact]
    public void Rows_run_across_the_facing()
    {
        var facing = new Yaw(90);                                         // faces -X
        var at = Formation.Arrange(Many(Mm32, 10), 3, Centre, facing);
        Assert.Equal([4, 3, 3], RowSizes(at, facing));
        Assert.All(at.Take(4), p => Assert.Equal(at[0].X, p.X, 6));       // a row runs along Y
        Assert.True(at[0].X < at[4].X);                                   // the front row is toward -X
    }

    [Fact]
    public void The_formation_is_centred_on_the_point_at_its_height()
    {
        var at = Formation.Arrange(Many(Mm32, 10), 3, Centre, new Yaw(0));
        var r = Mm32.A / 2;
        Assert.Equal(Centre.X, (at.Min(p => p.X) - r + at.Max(p => p.X) + r) / 2, 6);
        Assert.Equal(Centre.Y, (at.Min(p => p.Y) - r + at.Max(p => p.Y) + r) / 2, 6);
        Assert.All(at, p => Assert.Equal(Centre.Z, p.Z));
    }

    [Fact]
    public void Neighbours_in_a_row_sit_one_gap_apart()
    {
        var bases = Many(Mm32, 5);
        var placed = Placed(bases, Formation.Arrange(bases, 1, Centre, new Yaw(30)), new Yaw(30));
        for (var i = 0; i + 1 < placed.Count; i++) Assert.Equal(Formation.Gap, Distance.EdgeToEdge(placed[i], placed[i + 1]), 6);
    }

    [Fact]
    public void Rows_clamp_to_one_and_to_the_model_count()
    {
        Assert.Equal([3], RowSizes(Formation.Arrange(Many(Mm25, 3), 0, Centre, new Yaw(0)), new Yaw(0)));
        Assert.Equal([1, 1, 1], RowSizes(Formation.Arrange(Many(Mm25, 3), 5, Centre, new Yaw(0)), new Yaw(0)));
        Assert.Empty(Formation.Arrange([], 3, Centre, new Yaw(0)));
    }

    public static TheoryData<string, int, double> Units => new()
    {
        { "10x32", 1, 0 }, { "10x32", 2, 0 }, { "10x32", 3, 37 }, { "10x32", 4, 0 }, { "10x32", 5, 210 },
        { "leader60+9x25", 2, 15 }, { "leader60+9x25", 3, 15 }, { "leader60+9x25", 5, 15 }, { "9x25+leader60", 3, 15 },
        { "5x100", 2, 0 }, { "5x100", 3, 45 }, { "6xoval", 2, 90 }, { "6xoval", 3, 12 },
    };

    static IReadOnlyList<Base> UnitOf(string name) => name switch
    {
        "10x32" => Many(Mm32, 10),
        "leader60+9x25" => [Mm60, .. Many(Mm25, 9)],
        "9x25+leader60" => [.. Many(Mm25, 9), Mm60],
        "5x100" => Many(Mm100, 5),
        "6xoval" => Many(Base.Oval(75 / 25.4, 42 / 25.4), 6),
        _ => throw new ArgumentException(name),
    };

    // p.7: "All models in a unit must always stay within 1” of at least one other model … forming an uninterrupted chain of
    // models in 1” coherency with each other." The 9" spread depends on how many rows the player picks, so it is not asserted.
    [Theory, MemberData(nameof(Units))]
    public void Every_formation_is_one_coherent_chain_with_no_bases_closer_than_the_gap(string unit, int rows, double yaw)
    {
        var bases = UnitOf(unit);
        var placed = Placed(bases, Formation.Arrange(bases, rows, Centre, new Yaw(yaw)), new Yaw(yaw));
        Assert.True(Coherency.Check(placed, 1, 1, double.MaxValue).Coherent);
        for (var i = 0; i < placed.Count; i++)
            for (var j = i + 1; j < placed.Count; j++)
                Assert.True(Distance.EdgeToEdge(placed[i], placed[j]) >= Formation.Gap - 1e-6, $"{i} and {j} are closer than the gap");
    }
}
