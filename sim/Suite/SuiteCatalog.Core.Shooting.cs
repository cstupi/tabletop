using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>Core games: shooting (p.8) and cover and sight (p.11).</summary>
internal static partial class CoreGames
{
    // ---- core-firing-line ------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// p.8 shooting: Red's squad carries two weapon types, so it may split its fire between two targets. The first hit roll is fixed to test
    /// the Quality threshold and the automatic 6 and 1; the first defense roll to test the Defense threshold.
    /// </summary>
    public static TestGame FiringLine()
    {
        const string name = "core-firing-line";
        var scenario = new ScenarioBuilder(name, seed: 1)
            .RoundCap(5).Table(48, 36)
            .Army("Red", 2, 5, weapons: [Rifle(2), Rifle(2, "Heavy Rifle"), HandWeapon()])
            .Army("Blue", 3, 3, weapons: [Rifle(2), HandWeapon()])
            .Seeking(NearestEnemy)
            .Build();
        return Game(name, scenario,
        [
            ScoresAgainstThreshold(),
            FixedRoll("p.4: \"If you score the model's Quality value or higher, then it counts as a success\"; p.5: 6 always a success, 1 always a fail; p.8: \"For every hit ... roll one die\" (hit #0 fixed to 6, 1, 4, 3, 2, 2 at Quality 4+: exactly 2 hits)",
                "hit", 0, "6+,1-,4+,3-,2-,2-", (log, r) => DefenseDiceAfter(log, r.Frame) == 2 ? null : $"{DefenseDiceAfter(log, r.Frame)} defense dice for 2 hits"),
            FixedRoll("p.8: \"trying to score the target's Defense value\"; \"all failed rolls cause one wound each\"; \"For each wound ... remove one model\" (defense #0 fixed to 4, 3 at Defense 4+: exactly 1 casualty)",
                "defense", 0, "4+,3-", (log, r) => log.CasualtiesUnder(r.Frame) is 1 ? null : $"defense #0 removed {log.CasualtiesUnder(r.Frame)} models, expected 1"),
            RollToBlock(),
            Check("p.8: \"you may fire only at up to two different targets, and all weapons from the same group must fire at the same target\"", log =>
            {
                var shootings = log.Frames.Where(f => f.Sequence == "shooting").ToList();
                foreach (var f in shootings)
                {
                    var declare = f.Steps.FirstOrDefault(s => s.Step.Step == "declare-targets");
                    if (declare?.Asks.FirstOrDefault(a => a.Question == "declare_targets") is { Legal: Assignment { MaxDistinctTargets: not 2 } a }) return $"declare_targets allowed {a.MaxDistinctTargets} targets";
                    if (declare?.Step.Answer is not Assigned assigned) continue;
                    var fired = log.Children(f).Where(c => c.Sequence == "attack-sequence").Select(c => c.Targets[0]).ToHashSet();
                    if (!fired.SetEquals(assigned.Targets.Values)) return $"shooting {f.Id} declared {assigned.Targets.Values.Distinct().Count()} targets and fired at {fired.Count}";
                }
                return shootings.Any(f => log.Children(f).Where(c => c.Sequence == "attack-sequence").Select(c => c.Targets[0]).Distinct().Count() == 2)
                    ? null : "no unit split its fire between two targets";
            }),
            FinalRound(Enforcement.Strict),
        ], dice: [new DiceFix("hit", 0, [6, 1, 4, 3, 2, 2]), new DiceFix("defense", 0, [4, 3])]);
    }

    /// <summary>
    /// p.8 and p.9's Roll to Block and Remove Casualties: "For every hit ... the defending player must roll one die", "all failed rolls cause one
    /// wound each", "For each wound ... remove one model". Every model here has one wound, so casualties are the failures, capped by the models left.
    /// </summary>
    static GameCheck RollToBlock() => Check("p.8: \"For every hit ... roll one die\"; \"all failed rolls cause one wound each\"; \"For each wound ... remove one model\"", log =>
    {
        var attacks = log.Frames.Where(f => f.Sequence == "attack-sequence").ToList();
        if (attacks.Count == 0) return "nobody attacked";
        foreach (var attack in attacks)
        {
            var hits = attack.Steps.SelectMany(s => s.Step.Rolls).Where(r => r.Roll == "hit").Sum(r => log.Dice(r).Count(d => d.Success));
            var resolve = log.Children(attack).FirstOrDefault(c => c.Sequence == "resolve-hits");
            var defense = resolve?.Steps.SelectMany(s => s.Step.Rolls).Where(r => r.Roll == "defense").ToList() ?? [];
            if (defense.Sum(r => r.Dice.Count) != hits) return $"attack {attack.Id}: {hits} hits, {defense.Sum(r => r.Dice.Count)} defense dice";
            if (resolve is null) continue;
            var failures = defense.Sum(r => log.Dice(r).Count(d => !d.Success));
            var deal = log.Children(resolve).FirstOrDefault(c => c.Sequence == "deal-wounds");
            var living = log.LivingModels(attack.Targets[0], resolve.StartSeq);
            var removed = deal is null ? 0 : log.CasualtiesIn(deal);
            if (removed != Math.Min(failures, living)) return $"attack {attack.Id}: {failures} failed blocks against {living} models removed {removed}";
        }
        return null;
    });

    // ---- core-overkill -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// p.8 "all weapons are fired simultaneously": Red's squad fires two weapon groups at Blue's lone model. The first group's hit and block
    /// are fixed to kill it, and the second group's to hit and fail to block, so the second group's wounds land on a unit that is gone.
    /// </summary>
    public static TestGame Overkill()
    {
        const string name = "core-overkill";
        ScenarioWeapon Long(string weapon) => new(weapon, new Dictionary<string, string> { ["Range"] = "36\"", ["Attacks"] = "2" }, []);
        var scenario = new ScenarioBuilder(name, seed: 7)
            .RoundCap(2).Table(24, 48)
            .Army("Red", 1, 3, weapons: [Long("Rifle"), Long("Heavy Rifle")])
            .Army("Blue", 1, 1, weapons: [HandWeapon()])
            .Build();
        return Game(name, scenario,
        [
            Check("p.8: \"all weapons are fired simultaneously\" (the second group's wounds find no model left, remove nothing, and the shooting ends)", log =>
                Require(log.Frames.Where(f => f.Sequence == "shooting" && log.Children(f).Count(c => c.Sequence == "attack-sequence") == 2).ToList(), "no two-group shooting", f =>
                {
                    var second = log.Children(f).Where(c => c.Sequence == "attack-sequence").Last();
                    if (log.CasualtiesUnder(second) > 0) return $"the second group removed {log.CasualtiesUnder(second)} models from a destroyed unit";
                    return log.Events.Any(e => e.Payload is SequenceFinished x && x.FrameId == f.Id) ? null : $"shooting {f.Id} never finished";
                })),
        ], dice: [new DiceFix("hit", 0, [6]), new DiceFix("defense", 0, [1]), new DiceFix("hit", 1, [6]), new DiceFix("defense", 1, [1])]);
    }

    // ---- core-cover-and-sight --------------------------------------------------------------------------------------------------------

    // 48" by 36": a wall in the middle east (x 20–44, y 17–19, 4" tall) and a wood inside Blue's zone in the west.
    const double WallWest = 20, WallEast = 44, WallSouth = 17, WallNorth = 19, ModelRadius = 1.26 / 2;
    static readonly Point2 WoodCentre = new(8, 6);
    const double WoodRadius = 6;

    /// <summary>p.11 blocking and cover terrain: Red shoots across the middle, past a wall, at Blue units that may stand in a wood.</summary>
    public static TestGame CoverAndSight()
    {
        const string name = "core-cover-and-sight";
        var scenario = new ScenarioBuilder(name, seed: 73)
            .RoundCap(4).Table(48, 36)
            .Army("Red", 3, 3, weapons: [Rifle(2), HandWeapon()])
            .Army("Blue", 3, 3, weapons: [Rifle(2), HandWeapon()])
            .Terrain("wall", new Point3((WallWest + WallEast) / 2, (WallSouth + WallNorth) / 2, 0), volumes:
                [new Volume(VolumeKind.Solid, Footprint.Rect(WallEast - WallWest, WallNorth - WallSouth), new Point2(0, 0), new Yaw(0), 0, 4,
                    TerrainDeclarations.Impassable | TerrainDeclarations.BlocksLineOfSight, null)])
            .Terrain("wood", new Point3(WoodCentre.X, WoodCentre.Y, 0), volumes:
                [new Volume(VolumeKind.Area, Footprint.Circle(WoodRadius), new Point2(0, 0), new Yaw(0), 0, 3, TerrainDeclarations.Cover, null)])
            .Build();
        return Game(name, scenario,
        [
            Check("p.11: \"Units may not draw line of sight through blocking terrain\" (no unit is a valid target whose every model is behind the wall from every shooter)", log =>
            {
                var blocked = 0;
                foreach (var f in log.Frames.Where(f => f.Sequence == "shooting"))
                    foreach (var s in f.Steps)
                        foreach (var ask in s.Asks.Where(a => a.Legal is Assignment))
                        {
                            var shooters = log.Placed(f.Actor!.Value, s.Seq);
                            foreach (var enemy in log.EnemiesOf(f.Actor!.Value))
                            {
                                var targets = log.Placed(enemy, s.Seq);
                                if (targets.Count == 0 || !shooters.All(a => targets.All(t => BehindWall(a, t)))) continue;
                                blocked++;
                                if (((Assignment)ask.Legal).Targets.Contains(enemy)) return $"a unit wholly behind the wall was offered as a target at seq {s.Seq}";
                            }
                        }
                return blocked > 0 ? null : "no unit was ever wholly hidden behind the wall from a shooter";
            }),
            Check("p.11: units in cover \"get +1 to Defense rolls when blocking hits from shooting attacks\"", log =>
            {
                var covered = 0;
                foreach (var attack in log.Frames.Where(f => f.Sequence == "attack-sequence"))
                {
                    var shooting = log.Frame(attack.Parent)?.Sequence == "shooting";
                    var defense = log.Children(attack).Where(c => c.Sequence == "resolve-hits").SelectMany(c => c.Steps).SelectMany(s => s.Step.Rolls).Where(r => r.Roll == "defense").ToList();
                    var cover = defense.SelectMany(r => r.Modifiers).Where(m => m.Source == "cover").ToList();
                    if (cover.Any(m => m.Amount != 1)) return $"a cover modifier of {cover.First(m => m.Amount != 1).Amount}";
                    if (!shooting && cover.Count > 0) return "a melee defense roll had cover";
                    var models = log.Placed(attack.Targets[0], attack.StartSeq);
                    // "the majority of models in a unit are fully inside a piece of cover terrain": every base well inside the wood is.
                    if (!shooting || defense.Count == 0 || models.Count(p => p.DistanceTo(WoodCentre) <= WoodRadius - ModelRadius) * 2 <= models.Count) continue;
                    covered++;
                    if (defense.Any(r => !r.Modifiers.Any(m => m.Source == "cover"))) return $"a unit in the wood blocked without cover at seq {attack.StartSeq}";
                }
                return covered > 0 ? null : "no unit was shot at in the wood";
            }),
            ScoresAgainstThreshold(),
            FixedRoll("p.11: cover gives \"+1 to Defense rolls when blocking hits from shooting attacks\"; p.5: \"the new value counts as the final result\" (defense #2, in cover, fixed to 3, 2 at Defense 4+: the 3 blocks, the 2 wounds, exactly 1 casualty)",
                "defense", 2, "3+,2-", (log, r) =>
                    !r.Roll.Modifiers.Any(m => m is { Source: "cover", Amount: 1 }) ? "defense #2 had no cover"
                    : log.CasualtiesUnder(r.Frame) is 1 ? null : $"defense #2 removed {log.CasualtiesUnder(r.Frame)} models, expected 1"),
        ], dice: [new DiceFix("defense", 2, [3, 2])]);
    }

    // ---- core-fence-cover ------------------------------------------------------------------------------------------------------------

    // 48" by 36": the built-in wall-or-fence (solid, 6" by 0.5", 1.5" tall, cover and difficult, not blocking sight) across x 13–19, y 9.25–9.75.
    // Red's two shooters hold at (14, 34) and (18, 34). Blue 1 stands behind the fence at (16, 8.57), every line to it through the fence;
    // Blue 2 beside its east end at (20.5, 8.57), every line to it crossing y 9.75 east of x 19.5.
    const double FenceY = 9.5;

    /// <summary>p.11 "Terrain features that models can hide in or behind ... count as cover terrain"; p.18 "Wall or Fence: Counts as Cover".</summary>
    public static TestGame FenceCover()
    {
        const string name = "core-fence-cover";
        var below = FenceY - 0.25 - ModelRadius - 0.05;
        var scenario = new ScenarioBuilder(name, seed: 1)
            .RoundCap(5).Table(48, 36)
            .Army("Red", 2, 1, weapons: [new ScenarioWeapon("Long Rifle", new Dictionary<string, string> { ["Range"] = "36\"", ["Attacks"] = "3" }, [])],
                anchors: new Dictionary<int, Point2> { [1] = new(14, 34), [2] = new(18, 34) })
            .Army("Blue", 2, 1, weapons: [HandWeapon()], modelRules: [new ScenarioRule(1, "Tough", 12), new ScenarioRule(2, "Tough", 12)],
                anchors: new Dictionary<int, Point2> { [1] = new(16, below), [2] = new(20.5, below) })
            .Terrain("wall", new Point3(16, FenceY, 0), volumes:
                [new Volume(VolumeKind.Solid, Footprint.Rect(6, 0.5), new Point2(0, 0), new Yaw(0), 0, 1.5, TerrainDeclarations.Cover | TerrainDeclarations.Difficult, null)])
            .Scripted(EveryoneHolds())
            .Build();
        string? Shots(CoreLog log, string unit, bool cover)
        {
            var target = UnitNamed(log, unit);
            var rolls = (from attack in log.Frames where attack.Sequence == "attack-sequence" && attack.Targets[0] == target && log.Frame(attack.Parent)?.Sequence == "shooting"
                         from c in log.Children(attack) where c.Sequence == "resolve-hits"
                         from st in c.Steps from r in st.Step.Rolls where r.Roll == "defense" select (attack.StartSeq, r)).ToList();
            if (rolls.Count == 0) return $"{unit} was never shot";
            foreach (var (seq, r) in rolls)
                if (r.Modifiers.Any(m => m is { Source: "cover", Amount: 1 }) != cover) return $"{unit} blocked {(cover ? "without" : "with")} cover at seq {seq}";
            return null;
        }
        return Game(name, scenario,
        [
            Check("p.11: \"Terrain features that models can hide in or behind ... count as cover terrain\"; p.18: \"Wall or Fence: Counts as Cover\" (Blue 1, behind the fence from both shooters, blocks with +1 every time)",
                log => Shots(log, "Blue unit 1", cover: true)),
            Check("p.11: cover is terrain models are \"in or behind\" (Blue 2, beside the fence and not behind it from either shooter, never blocks with cover)",
                log => Shots(log, "Blue unit 2", cover: false)),
        ]);
    }

    /// <summary>Every line between the two bases crosses the wall's footprint: they stand on opposite sides of it, both within its width.</summary>
    static bool BehindWall(Point2 a, Point2 b)
    {
        bool Within(Point2 p) => p.X >= WallWest + ModelRadius && p.X <= WallEast - ModelRadius;
        return Within(a) && Within(b) && ((a.Y >= WallNorth + ModelRadius && b.Y <= WallSouth - ModelRadius) || (b.Y >= WallNorth + ModelRadius && a.Y <= WallSouth - ModelRadius));
    }
}
