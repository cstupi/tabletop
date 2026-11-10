using Tabletop.Content;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Geometry;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// The built-in ruined corner as the repo catalog declares it, centred at (0, -20) (a 6" square, 5" tall): 3.6.0 p.12 Dense "Units may draw
/// line of sight into and out of dense terrain, but not through it"; 3.5.1 p.12 "see into and out of ... but not through them"; owner
/// ruling: ruins are read so. The shooter and the target stand in one row along y -20.
/// </summary>
public class DenseRuinSightTests
{
    static BuiltinTerrain RuinedCorner()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Tabletop.sln"))) d = d.Parent;
        return BuiltinTerrain.FromCatalog(File.ReadAllText(Path.Combine(d!.FullName, "client/content/builtin/catalog.json")))["ruined-corner"];
    }

    static (Sim Sim, Unit Shooter, Unit Target) Table(string rulesId, double shooterX, double targetX)
    {
        var ruin = RuinedCorner();
        var piece = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin(ruin.Mesh), new Point3(0, -20, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), ruin.Volume)]);
        var (shooter, target) = (Unit("Shooter", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)), Unit("Target", "4+", "4+", [], 1, []));
        return (Sim.Gf(rulesId, State([shooter], [target], x1: shooterX, x2: targetX, terrain: [piece])), shooter, target);
    }

    static bool Sees(Sim sim, Unit shooter, Unit target) => Sight.From(sim.State, shooter.Id).CanSee(shooter.Models[0].Id, target.Models[0].Id);

    [Theory]
    [InlineData(RuleSets.Gf351)]
    [InlineData(RuleSets.Gf360)]
    public void A_line_through_the_ruined_corner_is_blocked_and_one_into_or_out_of_it_is_not(string rulesId)
    {
        var (through, s, t) = Table(rulesId, -8, 8);
        Assert.False(Sees(through, s, t));
        Assert.Empty(through.State.Rules.Shooters(through.State, through.Engine.Properties, s.Id, t.Id));

        var (into, s2, t2) = Table(rulesId, -8, 1);
        Assert.True(Sees(into, s2, t2));
        Assert.NotEmpty(into.State.Rules.Shooters(into.State, into.Engine.Properties, s2.Id, t2.Id));

        var (outOf, s3, t3) = Table(rulesId, -1, 8);
        Assert.True(Sees(outOf, s3, t3));
        Assert.NotEmpty(outOf.State.Rules.Shooters(outOf.State, outOf.Engine.Properties, s3.Id, t3.Id));
    }
}
