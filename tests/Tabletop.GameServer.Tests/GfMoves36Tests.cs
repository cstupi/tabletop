using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// 3.6.0 p.14 Flying: "May move through units and terrain, and ignores the effects of Difficult and Dangerous terrain." Steep counts as Difficult
/// and Unstable as Dangerous (3.6.0 p.12), so the same Flying flags in TerrainRules cover them; the engine's reading is the same in both editions.
/// </summary>
public class GfMoves36Tests
{
    static TerrainPlacement Volume(double x, double y, double width, double length, double height, TerrainDeclarations declarations) => new(Guid.CreateVersion7(),
        VisualRef.OfBuiltin("ruins"), new Point3(x, y, 0), new Yaw(0), 1,
        [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(width, length), new Point2(0, 0), new Yaw(0), 0, height, declarations, null))]);

    static Destinations By(Sim sim, Tabletop.Content.Unit u, double dx) => new(u.Models.Select(m => sim.Model(m.Id)).Select(m => new ModelRelocation(m.Id, m.Location,
        new OnTable(new Placement(((OnTable)m.Location).Placement.Position with { X = ((OnTable)m.Location).Placement.Position.X + dx }, new Yaw(0))))).ToList());

    static readonly int[] Fours = Enumerable.Repeat(4, 30).ToArray();

    static Tabletop.Content.Unit Mover(bool flying) => Unit("A", "4+", "4+", flying ? [P("Flying")] : [], 1, []);

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void A_flying_unit_ignores_steep_and_unstable_under_3_6(string rules)
    {
        // Steep: a hill at x -9..-3; a Rush of 11" onto it is over the 6" cap for a walker, not for a flyer.
        foreach (var (flying, breach) in new[] { (false, true), (true, false) })
        {
            var unit = Mover(flying);
            var sim = Sim.Gf(rules, State([unit], [Unit("B", "4+", "4+", [], 1, [])], x1: -12, x2: 20, terrain: [Volume(-6, -20, 6, 30, 2, TerrainDeclarations.Steep)]), Fours);
            sim.Start(GfNames.RushSeq, new SequenceContext(unit.Id, [], [], GfNames.Rush));
            var ask = sim.AskFor(Samples.Team1);
            var region = Assert.IsType<MoveRegion>(ask.Legal);
            Assert.Equal(breach, GfMoves.DifficultCapBreach(sim.State, ask, region, By(sim, unit, 11)) != null);
        }

        // Unstable: a Rush of 7" through a ruin at x -10..-6, no way round it. 3.6.0 p.12 tests a walker for a move over 6"; 3.5.1 tests it on any Rush.
        foreach (var flying in new[] { false, true })
        {
            var unit = Mover(flying);
            var sim = Sim.Gf(rules, State([unit], [Unit("B", "4+", "4+", [], 1, [])], x1: -12, x2: 20,
                terrain: [Volume(-8, -20, 4, 30, 4, TerrainDeclarations.Cover | TerrainDeclarations.DangerousOnRushCharge)]), Fours);
            sim.Start(GfNames.RushSeq, new SequenceContext(unit.Id, [], [], GfNames.Rush));
            sim.Answer(Samples.Team1, By(sim, unit, 7));
            Assert.Equal(flying ? 0 : 1, sim.Steps("test").Count);
        }
    }

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void A_flying_unit_activated_in_dangerous_terrain_tests_under_neither_edition(string rules)
    {
        // TerrainRules.IgnoresDangerous covers the activation test too, the same in both editions; a walker activated there tests.
        foreach (var flying in new[] { false, true })
        {
            var unit = Mover(flying);
            var sim = Sim.Gf(rules, State([unit], [Unit("B", "4+", "4+", [], 1, [])], x1: -12, x2: 20, terrain: [Volume(-12, -20, 4, 4, 4, TerrainDeclarations.Dangerous)]), Fours);
            sim.Start(GfNames.RushSeq, new SequenceContext(unit.Id, [], [], GfNames.Rush));
            Assert.Equal(flying ? 0 : 1, sim.Steps("test").Count);
        }
    }
}
