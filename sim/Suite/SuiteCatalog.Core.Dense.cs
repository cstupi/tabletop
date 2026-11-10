using Tabletop.Geometry;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

internal static partial class CoreGames
{
    // ---- core-dense-ruins --------------------------------------------------------------------------------------------------------------

    // 48" by 36": the built-in ruined corner (area, 6" square, 5" tall; Cover, Unstable, Sight blocking and Dense) at (16, 8) in Blue's zone, x 13–19,
    // y 5–11. Red 1 holds at (16, 32), Blue 1 at (16, 2) behind the ruin, every line between them through it; Blue 2 stands in the ruin at
    // (14, 9), clear of that line. Every unit Holds and shoots, with 36" rifles and Tough(12) so nothing falls.

    /// <summary>
    /// 3.6.0 p.12 Dense "Units may draw line of sight into and out of dense terrain, but not through it to targets on the other side"; 3.5.1
    /// p.12 "Units can see into and out of forests, but not through them"; the built-in ruins are read so (owner ruling).
    /// </summary>
    public static TestGame DenseRuins()
    {
        const string name = "core-dense-ruins";
        var longRifle = new ScenarioWeapon("Long Rifle", new Dictionary<string, string> { ["Range"] = "36\"", ["Attacks"] = "1" }, []);
        var scenario = new ScenarioBuilder(name, seed: 1)
            .RoundCap(2).Table(48, 36)
            .Army("Red", 1, 1, weapons: [longRifle], modelRules: [new ScenarioRule(1, "Tough", 12)],
                anchors: new Dictionary<int, Point2> { [1] = new(16, 32) })
            .Army("Blue", 2, 1, weapons: [longRifle], modelRules: [new ScenarioRule(1, "Tough", 12), new ScenarioRule(2, "Tough", 12)],
                anchors: new Dictionary<int, Point2> { [1] = new(16, 2), [2] = new(14, 9) })
            .Terrain("ruined-corner", new Point3(16, 8, 0), volumes:
                [new Volume(VolumeKind.Area, Footprint.Rect(6, 6), new Point2(0, 0), new Yaw(0), 0, 5,
                    TerrainDeclarations.BlocksLineOfSight | TerrainDeclarations.SeeInAndOut | TerrainDeclarations.Cover | TerrainDeclarations.DangerousOnRushCharge, null)])
            .Scripted(EveryoneHoldsAiming(new Dictionary<string, string> { ["Red unit 1"] = "Blue unit 2", ["Blue unit 2"] = "Red unit 1" }))
            .Build();
        string? NeverOffered(CoreLog log, string shooter, string target)
        {
            var (s, t) = (UnitNamed(log, shooter), UnitNamed(log, target));
            var offers = OfferedTargets(log, s).ToList();
            if (offers.Count == 0) return $"{shooter} was never offered targets";
            return offers.FirstOrDefault(o => o.Targets.Contains(t)) is { Targets: not null } hit ? $"{shooter} was offered {target} through the ruin at seq {hit.Seq}" : null;
        }
        string? Shot(CoreLog log, string shooter, string target)
        {
            var (s, t) = (UnitNamed(log, shooter), UnitNamed(log, target));
            return log.Frames.Any(f => f.Sequence == "attack-sequence" && f.Actor == s && f.Targets.Contains(t)) ? null : $"{shooter} never shot {target}";
        }
        return Game(name, scenario,
        [
            Check("3.6.0 p.12 Dense, 3.5.1 p.12 see into and out of: \"Units may draw line of sight into and out of dense terrain, but not through it to targets on the other side\" (Red 1 is never offered Blue 1, behind the ruin)",
                log => NeverOffered(log, "Red unit 1", "Blue unit 1")),
            Check("3.6.0 p.12 Dense, 3.5.1 p.12 see into and out of: \"Units may draw line of sight into ... dense terrain\" (Red 1 shoots Blue 2, in the ruin)",
                log => Shot(log, "Red unit 1", "Blue unit 2")),
            Check("3.6.0 p.12 Dense, 3.5.1 p.12 see into and out of: \"Units may draw line of sight ... out of dense terrain\" (Blue 2, in the ruin, shoots Red 1)",
                log => Shot(log, "Blue unit 2", "Red unit 1")),
        ]);
    }
}
