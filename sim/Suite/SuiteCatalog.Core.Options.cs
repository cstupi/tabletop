using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>
/// Core games under the optional rules (optional-rules spec): one game per option value that is not the default. The sight games fix every
/// unit's deployment (ScenarioArmy.Anchors) and every unit holds, so the lines they read stand as laid out all game.
/// </summary>
internal static partial class CoreGames
{
    static ScenarioWeapon LongRifle() => new("Long Rifle", new Dictionary<string, string> { ["Range"] = "36\"", ["Attacks"] = "1" }, []);

    /// <summary>Every activation is a Hold: nobody moves, and a unit with a gun shoots.</summary>
    static AnswerScript EveryoneHolds() =>
        AnswerScript.Build().Match(AskQuestions.Activate, (ask, _) => AskPicks.Option(ask, c => c.Label.EndsWith(": Hold"))).Build();

    /// <summary>
    /// Every unit Holds and shoots (the server's fixed option id), and each shooter named in aims puts every weapon group at its target there (when every
    /// group may take it).
    /// </summary>
    static AnswerScript EveryoneHoldsAiming(IReadOnlyDictionary<string, string> aims) =>
        AnswerScript.Build().Match(AskQuestions.Activate, (ask, _) => AskPicks.Option(ask, c => c.Label.EndsWith(": Hold")))
            .Match("shoot_or_not", (ask, _) => ask.Legal is Options o && o.Choices.Any(c => c.Id == "shoot") ? new Chosen("shoot") : null)
            .Match("declare_targets", (ask, p) => AskPicks.Aimed(ask, p, aims.GetValueOrDefault))
            .Build();

    static Guid UnitNamed(CoreLog log, string name) => log.Run.Final.Units.Values.First(u => u.Definition.Name == name).Id;

    /// <summary>Each target set a unit's shooting was offered, with the seq of the step that offered it.</summary>
    static IEnumerable<(long Seq, IReadOnlyList<Guid> Targets)> OfferedTargets(CoreLog log, Guid shooter) =>
        from f in log.Frames where f.Sequence == "shooting" && f.Actor == shooter
        from s in f.Steps from ask in s.Asks where ask.Legal is Assignment
        select (s.Seq, ((Assignment)ask.Legal).Targets);

    // ---- core-perimeter-sight ----------------------------------------------------------------------------------------------------------

    // 48" by 36". Red's shooter at (24, 34); Red's four-model screen in a row at y 26, centres 1.76" apart from x 21.36, so its middle gap spans
    // x 23.75–24.25; Blue 1 at (24, 6), every line to it through that gap (0.18" either side of x 24 at y 26); Blue 2 at (40, 6), its lines
    // passing the screen's east end at x 28.4 or more.
    const double ScreenY = 26;

    /// <summary>p.5: "Models can't see through solid obstacles, including the perimeter of other units (friendly or enemy)", played as each unit's perimeter.</summary>
    public static TestGame PerimeterSight()
    {
        const string name = "core-perimeter-sight";
        var scenario = new ScenarioBuilder(name, seed: 1)
            .RoundCap(5).Table(48, 36)
            .Options(new Dictionary<string, string> { ["unit_sight_blocking"] = "perimeter" })
            .Army("Red", 2, 1, weapons: [LongRifle()], sizes: new Dictionary<int, int> { [2] = 4 },
                models: [.. Enumerable.Range(1, 4).Select(m => new ScenarioModel(2, m, Weapons: [HandWeapon()]))],
                anchors: new Dictionary<int, Point2> { [1] = new(24, 34), [2] = new(24 - 2.64, ScreenY) })
            .Army("Blue", 2, 1, weapons: [HandWeapon()], anchors: new Dictionary<int, Point2> { [1] = new(24, 6), [2] = new(40, 6) })
            .Scripted(EveryoneHolds())
            .Build();
        return Game(name, scenario,
        [
            Check("p.5: \"Models can't see through solid obstacles, including the perimeter of other units (friendly or enemy)\" (unit_sight_blocking perimeter: Blue 1, seen only through the gap in Red's screen, is never a target; Blue 2 in the open is)", log =>
            {
                var (shooter, screen, hidden, open) = (UnitNamed(log, "Red unit 1"), UnitNamed(log, "Red unit 2"), UnitNamed(log, "Blue unit 1"), UnitNamed(log, "Blue unit 2"));
                var offers = OfferedTargets(log, shooter).ToList();
                if (offers.Count == 0) return "Red unit 1 was never offered a target";
                foreach (var (seq, targets) in offers)
                {
                    var wall = log.Placed(screen, seq);
                    if (wall.Count != 4 || wall.Any(p => Math.Abs(p.Y - ScreenY) > 1e-6)) return $"Red's screen is not in its row at seq {seq}";
                    if (log.Placed(hidden, seq) is not [{ X: var x }] || Math.Abs(x - 24) > 1e-6) return $"Blue 1 is not behind the gap at seq {seq}";
                    if (targets.Contains(hidden)) return $"Blue 1 was offered through the screen's gap at seq {seq}";
                }
                return offers.Any(o => o.Targets.Contains(open)) ? null : "Blue 2, in the open, was never offered";
            }),
            FinalRound(Enforcement.Strict),
        ]);
    }

    // ---- core-hybrid-sight -------------------------------------------------------------------------------------------------------------

    // 48" by 36". Red's Tough(12) shooter at (24, 34). Blue 1 (no Tough) at (12, 6) behind Blue 2, five models without Tough in a row at y 10
    // centred on x 13.71, where every line to Blue 1 crosses the middle model; Blue 3 (no Tough) at (36, 6) behind Blue 4, five Tough(12)
    // models in a row at y 10 centred on x 34.29.
    const double HybridRowY = 10;

    /// <summary>p.56 Hybrid True Line of Sight: a Large model sees through a unit of smaller size class, not through one of its own class
    /// in front of a smaller target.</summary>
    public static TestGame HybridSight()
    {
        const string name = "core-hybrid-sight";
        var row = 2 * 1.76;
        var scenario = new ScenarioBuilder(name, seed: 1)
            .RoundCap(5).Table(48, 36)
            .Options(new Dictionary<string, string> { ["line_of_sight"] = "hybrid_true" })
            .Army("Red", 1, 1, weapons: [LongRifle()], modelRules: [new ScenarioRule(1, "Tough", 12)], anchors: new Dictionary<int, Point2> { [1] = new(24, 34) })
            .Army("Blue", 4, 1, weapons: [HandWeapon()], modelRules: [new ScenarioRule(4, "Tough", 12)], sizes: new Dictionary<int, int> { [2] = 5, [4] = 5 },
                anchors: new Dictionary<int, Point2> { [1] = new(12, 6), [2] = new(13.714 - row, HybridRowY), [3] = new(36, 6), [4] = new(34.286 - row, HybridRowY) })
            .Scripted(EveryoneHolds())
            .Build();
        return Game(name, scenario,
        [
            Check("p.56: \"Models may draw line of sight through units of smaller size class\" (line_of_sight hybrid_true: the Tough(12) shooter is offered Blue 1 past Blue 2's five unharmed Small models)", log =>
            {
                var (shooter, smallTarget, smallScreen) = (UnitNamed(log, "Red unit 1"), UnitNamed(log, "Blue unit 1"), UnitNamed(log, "Blue unit 2"));
                var offers = OfferedTargets(log, shooter).Where(o => log.LivingModels(smallScreen, o.Seq) == 5).ToList();
                if (offers.Count == 0) return "Red unit 1 was never offered a target while Blue 2 stood whole";
                return offers.All(o => o.Targets.Contains(smallTarget)) ? null : "Blue 1 was not offered past the Small screen";
            }),
            Check("p.56: \"Models may draw line of sight through units of equal or lower size class, if the target is of greater size class than the unit in the way\" (Blue 3, Small, stays hidden behind Blue 4's Large models)", log =>
            {
                var (shooter, hidden, largeScreen) = (UnitNamed(log, "Red unit 1"), UnitNamed(log, "Blue unit 3"), UnitNamed(log, "Blue unit 4"));
                var offers = OfferedTargets(log, shooter).ToList();
                if (offers.Count == 0) return "Red unit 1 was never offered a target";
                foreach (var (seq, targets) in offers)
                {
                    if (log.LivingModels(largeScreen, seq) != 5) return $"Blue 4 lost a model by seq {seq}";
                    if (targets.Contains(hidden)) return $"Blue 3 was offered through Blue 4 at seq {seq}";
                }
                return null;
            }),
            FinalRound(Enforcement.Strict),
        ]);
    }

    // ---- core-melee-no-separation ------------------------------------------------------------------------------------------------------

    /// <summary>p.10's melee separation left out: armies with only hand weapons close in and charge, and stay in contact after each melee.</summary>
    public static TestGame MeleeNoSeparation()
    {
        const string name = "core-melee-no-separation";
        var scenario = new ScenarioBuilder(name, seed: 1)
            .RoundCap(5).Table(36, 30)
            .Options(new Dictionary<string, string> { ["melee_separation"] = "off" })
            .Army("Red", 4, 3, weapons: [HandWeapon(2)])
            .Army("Blue", 4, 3, weapons: [HandWeapon(2)])
            .Seeking(NearestEnemy)
            .Build();
        const double Base = 1.26;
        bool InContact(CoreLog log, Guid unit, long seq) =>
            log.EnemiesOf(unit).Any(e => log.Placed(e, seq).Any(p => log.Placed(unit, seq).Any(q => p.DistanceTo(q) - Base < 1 - 1e-6)));
        return Game(name, scenario,
        [
            Check("p.10: \"players may agree not to separate units by 1\" for simplicity\" (melee_separation off: when neither unit is destroyed, the consolidation moves nobody)", log =>
                Require(log.Frames.Where(f => f.Sequence == "melee").SelectMany(f => f.Steps.Where(s => s.Step.Step == "consolidate").Select(s => (f, s)))
                    .Where(x => !log.UnitDestroyedBefore(x.f.Actor!.Value, x.s.Seq) && !log.UnitDestroyedBefore(x.f.Targets[0], x.s.Seq)).ToList(),
                    "no melee ended with both units standing",
                    x => x.s.Asks.Any(a => a.Question == "move") || Moved(x.s.Step).Any() ? $"melee {x.f.Id}: the consolidation moved a unit" : null)),
            Check("p.10: \"units can still freely activate, move, shoot, charge, etc. despite being in contact\" (a unit in contact with an enemy takes a move action)", log =>
                log.Frames.Any(f => f.Sequence is "advance" or "rush" or "charge" && f.Actor is { } unit && InContact(log, unit, f.StartSeq))
                    ? null : "no unit in contact took a move action"),
            FinalRound(Enforcement.Strict),
        ]);
    }
}
