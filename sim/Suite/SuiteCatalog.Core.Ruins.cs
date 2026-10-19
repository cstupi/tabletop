using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

internal static partial class CoreGames
{
    // ---- core-ruins --------------------------------------------------------------------------------------------------------------------

    // 48" by 36": a band of ruins across the middle of the table (y 15–21), Cover and Dangerous when using Rush/Charge.
    const double RuinsSouth = 15, RuinsNorth = 21;

    /// <summary>p.12 "Ruins - Cover + Dangerous when using rush/charge" with p.11's dangerous terrain test: each army must cross the ruins to reach the other.</summary>
    public static TestGame Ruins()
    {
        const string name = "core-ruins";
        var scenario = new ScenarioBuilder(name, seed: 1)
            .RoundCap(4).Table(48, 36)
            .Army("Red", 3, 3, weapons: [Rifle(), HandWeapon()])
            .Army("Blue", 3, 3, weapons: [Rifle(), HandWeapon()])
            .Terrain("ruins", new Point3(24, (RuinsSouth + RuinsNorth) / 2, 0), volumes:
                [new Volume(VolumeKind.Area, Footprint.Rect(48, RuinsNorth - RuinsSouth), new Point2(0, 0), new Yaw(0), 0, 4,
                    TerrainDeclarations.Cover | TerrainDeclarations.DangerousOnRushCharge, null)])
            .Seeking(NearestEnemy)
            .Build();
        // A model whose base centre starts or ends in the band, or whose move spans it, went in or through it.
        static bool Crossed(ModelRelocation m) => m is { From: OnTable from, To: OnTable to }
            && Spans(from.Placement.Position.Horizontal.Y, to.Placement.Position.Horizontal.Y);
        static bool Spans(double a, double b) => Math.Max(a, b) >= RuinsSouth && Math.Min(a, b) <= RuinsNorth;
        static string? Tested(CoreLog log, string sequence, bool owed)
        {
            var crossings = 0;
            foreach (var f in log.Frames.Where(f => f.Sequence == sequence))
            {
                var tests = log.Children(f).Count(c => c.Sequence == "dangerous-terrain");
                if (!owed && tests > 0) return $"{sequence} frame {f.Id} took {tests} dangerous terrain test(s)";
                foreach (var s in f.Steps.Where(s => s.Step.Step == "move" && Moved(s.Step).Any(Crossed)))
                {
                    crossings++;
                    if (owed && !log.Children(f).Any(c => c.Sequence == "dangerous-terrain" && c.StartSeq > s.Seq))
                        return $"a {sequence} through the ruins at seq {s.Seq} took no test";
                }
            }
            return crossings > 0 ? null : $"no {sequence} went into the ruins";
        }
        return Game(name, scenario,
        [
            Check("p.12: \"Ruins - Cover + Dangerous when using rush/charge\"; p.11: \"If a model moves in or through dangerous terrain ... it must take a dangerous terrain test\" (Rush)",
                log => Tested(log, "rush", owed: true)),
            Check("p.12: \"Ruins - Cover + Dangerous when using rush/charge\"; p.11: \"If a model moves in or through dangerous terrain ... it must take a dangerous terrain test\" (Charge)",
                log => Tested(log, "charge", owed: true)),
            Check("p.12: \"Ruins - Cover + Dangerous when using rush/charge\": an Advance in or through ruins takes no dangerous terrain test",
                log => Tested(log, "advance", owed: false)),
        ]);
    }
}
