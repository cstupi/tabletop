# Task 7: Formation

**Wave:** 2 · **Depends on:** — · **Model:** sonnet

**Files:**
- Create: `client/src/Core/Formation.cs`
- Test: `tests/Tabletop.Client.Tests/FormationTests.cs` (new)

**Interfaces:**
- Consumes: `Tabletop.Geometry`: `Base` (A = length along the model's facing, B = width), `Point2.Rotate(Yaw)` (yaw 0 faces
  +Y, positive counter-clockwise from above), `Distance.EdgeToEdge(PlacedModel, PlacedModel)`, `Coherency.Check` (tests).
- Produces: `public static class Formation { public const double Gap = 0.5; public static IReadOnlyList<Point3> Arrange(IReadOnlyList<Base> bases, int rows, Point3 centre, Yaw facing); }`.
  One position per base, in input order; every Z is `centre.Z`. The caller (Task 17) gives each model the yaw `facing`.
  `Gap` is public for the tests.

Spec: *Table interaction → Formations*. "While a unit is held in a drag, 1–5 arranges it in that many rows around the
cursor, the models split as evenly as they go (10 models in 3 rows is 4-3-3). Rows run across the screen as the camera
sees it; Q/E turn the arrangement 15°. Bases sit with a fixed gap that keeps unit coherency (p.7)." Task 17 turns the
camera's view into `facing` and the Q/E turns; this task arranges.

Rulebook p.7, *Unit Coherency*: "All models in a unit must always stay within 1” of at least one other model, and must
stay within 9” of all other models (or as close as possible), forming an uninterrupted chain of models in 1” coherency
with each other."

How the gap keeps coherency:
- Neighbours in a row sit `Gap` = 0.5" apart, edge to edge, each spaced by its own width (`B`), so a mixed unit (a 60 mm
  leader among 25 mm models) keeps 0.5" too. A uniform cell sized to the widest base would put two 25 mm models 1.88"
  apart beside a 60 mm one.
- Each row steps back from the one in front until the closest pair across the two rows is exactly `Gap` apart, found by
  bisection on `Distance.EdgeToEdge`. A fixed row pitch fails for round bases offset half a model (a 4-row in front of a
  3-row): the diagonal pair is `0.118·d + 1.118·g` apart for diameter `d`, over 1" for 100 mm bases at `g` = 0.5.
- Rows chained in themselves and to each other make one uninterrupted chain. The 9" spread is the player's choice of
  rows (ten 32 mm models in one row span 17.1"); the spec asks only that the gap keeps coherency.

The split puts the extra models in the front rows: `n / rows`, plus one for each of the first `n % rows` rows. `rows`
clamps to `1..n`. The block is centred on `centre`: its extents across and along the facing, base edges included,
are halved about it.

- [ ] **Step 1: Write the failing tests**

Create `tests/Tabletop.Client.Tests/FormationTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Tabletop.Client.Tests --filter FormationTests`
Expected: compile error, `The name 'Formation' does not exist in the current context`.

- [ ] **Step 3: Implement**

Create `client/src/Core/Formation.cs`:

```csharp
using Tabletop.Geometry;

namespace Tabletop.Client.Core;

/// <summary>
/// A held unit arranged in rows around a point (spec: in-game UX → Formations). Rows run across the facing, the front row toward it; the
/// models split as evenly as they go, the extra ones in the front rows (10 in 3 rows is 4-3-3), in input order, front row first.
/// Neighbours in a row sit <see cref="Gap"/> apart edge to edge, and each row sits back from the one in front until its closest pair
/// across the two is <see cref="Gap"/> apart, so every model is within 1" of another and the rows form one chain (p.7, unit coherency).
/// </summary>
public static class Formation
{
    /// <summary>Edge-to-edge spacing, inside p.7's 1" with room for a dragged unit's rounding.</summary>
    public const double Gap = 0.5;

    public static IReadOnlyList<Point3> Arrange(IReadOnlyList<Base> bases, int rows, Point3 centre, Yaw facing)
    {
        var n = bases.Count;
        if (n == 0) return [];
        rows = Math.Clamp(rows, 1, n);
        // The local frame faces +Y (yaw 0): a row runs along X, the front row is at Y = 0 and later rows step back toward -Y.
        var local = new Point2[n];
        IReadOnlyList<int> ahead = [];
        var line = 0.0; var first = 0;
        for (var r = 0; r < rows; r++)
        {
            var row = Enumerable.Range(first, n / rows + (r < n % rows ? 1 : 0)).ToList();
            first += row.Count;
            var x = -(row.Sum(i => bases[i].B) + (row.Count - 1) * Gap) / 2;     // B is the width across the facing
            foreach (var i in row) { local[i] = new Point2(x + bases[i].B / 2, 0); x += bases[i].B + Gap; }
            if (r > 0) line = Behind(bases, local, ahead, row, line);
            foreach (var i in row) local[i] = local[i] with { Y = line };
            ahead = row;
        }
        var front = Enumerable.Range(0, n).Max(i => local[i].Y + bases[i].A / 2);  // A is the length along the facing
        var back = Enumerable.Range(0, n).Min(i => local[i].Y - bases[i].A / 2);
        var middle = new Point2(0, (front + back) / 2);
        return local.Select(p => (p - middle).Rotate(facing)).Select(p => new Point3(centre.X + p.X, centre.Y + p.Y, centre.Z)).ToList();
    }

    /// <summary>The line behind <paramref name="line"/> at which the closest pair across the two rows is <see cref="Gap"/> apart. The closest
    /// distance only grows as the row steps back, so a bisection between touching and a full row depth behind finds it.</summary>
    static double Behind(IReadOnlyList<Base> bases, Point2[] local, IReadOnlyList<int> ahead, IReadOnlyList<int> row, double line)
    {
        double Closest(double y) => ahead.Min(i => row.Min(j => Distance.EdgeToEdge(At(bases[i], local[i] with { Y = line }), At(bases[j], local[j] with { Y = y }))));
        double near = 0, far = ahead.Max(i => bases[i].A) / 2 + Gap + row.Max(j => bases[j].A) / 2;
        for (var k = 0; k < 40; k++) { var step = (near + far) / 2; if (Closest(line - step) < Gap) near = step; else far = step; }
        return line - far;
    }

    static PlacedModel At(Base b, Point2 p) => new(new ModelBody(b, 1), new Placement(new Point3(p.X, p.Y, 0), new Yaw(0)));
}
```

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test tests/Tabletop.Client.Tests --filter FormationTests && ./scripts/check-client`
Expected: 18 passed; `check-client` prints nothing.

- [ ] **Step 5: Commit**

```bash
git add client/src/Core/Formation.cs tests/Tabletop.Client.Tests/FormationTests.cs
git commit -m "Arrange a held unit in rows across a facing, one gap apart, in one coherent chain" -m "Co-Authored-By: Claude Sonnet <noreply@anthropic.com>"
```
