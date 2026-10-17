using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Tests;

/// <summary>A scenario's optional rules, unit sizes and deployment anchors (optional-rules plan → Sim and suite games).</summary>
public class ScenarioOptionsTests
{
    static Scenario Anchored() => new ScenarioBuilder("anchored", 1).Table(48, 36)
        .Options(new Dictionary<string, string> { ["melee_separation"] = "off" })
        .Army("Red", 2, 1, sizes: new Dictionary<int, int> { [2] = 4 }, anchors: new Dictionary<int, Point2> { [2] = new(21.36, 10) })
        .Army("Blue", 1, 1)
        .Build();

    [Fact]
    public void The_compiled_game_carries_the_optional_rules_and_each_units_size()
    {
        var compiled = ScenarioCompiler.Compile(Anchored());
        Assert.Equal("off", compiled.Info.OptionalRules["melee_separation"]);
        Assert.Equal([1, 4], compiled.Board.Armies[0].Snapshot.Units.Select(u => u.Models.Count));
        Assert.Empty(ScenarioCompiler.Compile(new ScenarioBuilder("plain", 1).Army("Red", 1, 1).Army("Blue", 1, 1).Build()).Info.OptionalRules);
    }

    [Fact]
    public void An_anchored_unit_deploys_in_a_row_from_its_anchor()
    {
        var compiled = ScenarioCompiler.Compile(Anchored());
        var projection = Projection.Initial(compiled.Board, compiled.Info);
        var unit = compiled.Board.Armies[0].Snapshot.Units[1];
        var moves = HandPlacement.IntoZone(projection, compiled, compiled.TeamIds[0], unit.Id, new Chooser(5));
        var at = moves.Select(m => ((OnTable)m.To).Placement.Position).ToList();
        Assert.Equal(new Point3(21.36, 10, 0), at[0]);
        Assert.All(at, p => Assert.Equal(10, p.Y, 9));
        Assert.Equal(1.76, at[1].X - at[0].X, 9);                                          // the widest base plus half an inch
    }

    [Fact]
    public void A_size_or_anchor_for_a_unit_the_army_does_not_have_is_refused() =>
        Assert.Throws<InvalidOperationException>(() => new ScenarioBuilder("bad", 1).Army("Red", 1, 1, anchors: new Dictionary<int, Point2> { [2] = new(0, 0) }).Army("Blue", 1, 1).Build());
}
