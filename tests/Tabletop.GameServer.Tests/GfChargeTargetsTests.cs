using Tabletop.Content;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>ChargeTargets against the path-first expression it replaced, over randomised layouts with impassable walls.</summary>
public class GfChargeTargetsTests
{
    static TerrainPlacement Wall(double x, double y, double yaw) => new(Guid.CreateVersion7(), VisualRef.OfBuiltin("wall"), new Point3(x, y, 0), new Yaw(yaw), 1,
        [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Rect(1, 10), new Point2(0, 0), new Yaw(0), 0, 4, TerrainDeclarations.Impassable, null))]);

    /// <summary>The expression before the distance test moved first: every model pair's path walked, then the distance compared.</summary>
    static List<Guid> PathFirst(GameState state, PropertySet props, Guid unit)
    {
        var live = LiveProperties.Settle(new Interaction(state, props, Party.Whole(state, unit), null, AttackKind.Melee, GfNames.Charge, null, []));
        var distance = StatReader.ActionDistance(live, Side.Attacker, GfNames.Charge);
        var impassable = live.Has(Side.Attacker, "Flying") ? [] : Battlefield.Terrain(state).Where(v => v.Volume.Declarations.HasFlag(TerrainDeclarations.Impassable)).ToList();
        var team = Teams.Of(state, unit);
        return state.Projection.Units.Values.Where(u => Teams.Of(state, u.Id) != team && !Lookup.UnitHas(state, props, u.Id, GfNames.Aircraft)).Select(u => u.Id)
            .Where(t => Lookup.LivingModels(state, unit).Any(m => Lookup.LivingModels(state, t).Any(e =>
            {
                if (Battlefield.Placed(state, m) is not { } from || Battlefield.Placed(state, e) is not { } to) return false;
                var check = Movement.Check(from, to.Placement.Position.Horizontal, distance, impassable, [], null, state.Board.Table.Size);
                return Battlefield.Distance(state, m, e) <= distance && !check.CrossesImpassable;
            }))).ToList();
    }

    [Fact]
    public void Charge_targets_match_the_path_first_expression_on_random_layouts()
    {
        var random = new Random(20261011);
        var compared = 0;
        var nonEmpty = 0;
        for (var layout = 0; layout < 60; layout++)
        {
            Unit Random(string name) => Unit(name, "4+", "4+", random.Next(4) == 0 ? [P("Flying")] : [], random.Next(1, 6), [], Melee("Fist", 1));
            var team1 = Enumerable.Range(0, random.Next(1, 5)).Select(i => Random($"A{i}")).ToList();
            var team2 = Enumerable.Range(0, random.Next(1, 5)).Select(i => Random($"B{i}")).ToList();
            var walls = Enumerable.Range(0, random.Next(0, 6)).Select(_ => Wall(6 + random.NextDouble() * 60, 6 + random.NextDouble() * 36, random.NextDouble() * 360)).ToList();
            var sim = Sim.Gf(State(team1, team2, terrain: walls), 1);
            var moves = new List<ModelRelocation>();
            foreach (var u in team1.Concat(team2))
            {
                var (cx, cy) = (4 + random.NextDouble() * 64, 4 + random.NextDouble() * 40);
                foreach (var m in u.Models)
                    moves.Add(new ModelRelocation(m.Id, sim.Model(m.Id).Location, random.Next(20) == 0 ? new OnShelf()
                        : new OnTable(new Placement(new Point3(cx + random.NextDouble() * 4, cy + random.NextDouble() * 4, 0), new Yaw(0)))));
            }
            sim.Append([new ModelsMoved(moves)]);
            foreach (var u in team1.Concat(team2))
            {
                var expected = PathFirst(sim.State, TestProperties.Core, u.Id);
                Assert.Equal(expected, GfMelee.ChargeTargets(sim.State, TestProperties.Core, u.Id));
                compared++;
                if (expected.Count > 0) nonEmpty++;
            }
        }
        Assert.InRange(nonEmpty, compared / 10, compared - compared / 10);   // the layouts exercise both answers
    }
}
