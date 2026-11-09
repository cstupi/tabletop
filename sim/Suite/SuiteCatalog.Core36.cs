using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>
/// Core games under Grimdark Future 3.6.0 alone (Edition = Gf360, named &lt;name&gt;@3.6): one per core rule whose 3.6.0 text changed, each
/// built by construction (sim/AGENTS.md → Writing a suite game that holds) and every check quoting the 3.6.0 sentence and page.
/// </summary>
internal static partial class CoreGames
{
    public static IEnumerable<TestGame> All36() =>
    [
        ChargeAndStrikeBack36().In36(),
        MoraleBreak36().In36(),
        Ruins36().In36(),
        RoughGround36().In36(),
        Deployment36().In36(),
        SideBattle().BothEditions(),
    ];

    // ---- core-charge-and-strike-back@3.6 -----------------------------------------------------------------------------------------------

    /// <summary>
    /// 3.6.0 p.9 who can strike and fatigue. Blue unit 1 (four models in a row along y 6, 1.76" apart) holds; Red unit 1 and Red unit 2 (one
    /// Tough(6) model each) charge it in turn, each landing straight above an end model, and Blue passes its close-in, so its second and third
    /// models stand 0.90" and 2.48" from the charger. Blue strikes back with a Rending blade twice in the round: its first strike back's dice
    /// are fixed to 6s (Rending acts), its second, fatigued, to 6s too (they hit and trigger nothing). Red's own strikes are fixed to miss and its
    /// defense dice to block, so no melee is lost, nobody routs or consolidates into the row, and the row stands.
    /// </summary>
    static TestGame ChargeAndStrikeBack36()
    {
        const string name = "core-charge-and-strike-back";
        var landing = new Dictionary<string, Point2> { ["Red unit 1"] = new(10, 6 + Scripts36.Touch), ["Red unit 2"] = new(10 + 3 * 1.76, 6 + Scripts36.Touch) };
        var script = AnswerScript.Build()
            .Match(AskQuestions.Activate, (ask, _) => AskPicks.Activate(ask, "Red unit 1", "Charge") ?? AskPicks.Activate(ask, "Red unit 2", "Charge")
                ?? AskPicks.Activate(ask, "Blue unit 1", "Hold"))
            .Match("charge_target", (ask, p) => AskPicks.Pieces(ask, p, "Blue unit 1"))
            .Match("strike_back", (ask, _) => new Chosen("strike"))
            .Match("move", Scripts36.Charging(landing))
            .Build();
        var scenario = new ScenarioBuilder(name, seed: 1)
            .RoundCap(1).Table(36, 24).Scripted(script)
            .Army("Red", 2, 1, modelRules: [new(1, "Tough", 6), new(2, "Tough", 6)], weapons: [HandWeapon()],
                anchors: new Dictionary<int, Point2> { [1] = new(10, 16), [2] = new(16, 16) })
            .Army("Blue", 1, 4, weapons: [new ScenarioWeapon("Rending Blade", new Dictionary<string, string> { ["Attacks"] = "1" }, [new(0, "Rending")])],
                anchors: new Dictionary<int, Point2> { [1] = new(10, 6) })
            .Build();
        return Game(name, scenario,
        [
            ChargeMoves("3.6.0 p.9"),
            Check("3.6.0 p.9: \"All models that are within 3” of an enemy model from the target unit (horizontally or vertically), may attack it\"", log =>
            {
                var far = 0;
                var failure = Require(log.MeleeAttacks.ToList(), "no melee attack", attack =>
                {
                    var determine = attack.Steps.FirstOrDefault(s => s.Step.Step == "determine-attacks");
                    if (determine is null) return null;
                    var gaps = log.Run.Final.Models.Values.Where(m => m.UnitId == attack.Actor).Select(m => GapTo(log, m.Id, attack.Targets[0], determine.Seq)).OfType<double>().ToList();
                    var within = gaps.Count(g => g <= 3 + 1e-6);
                    far += gaps.Count(g => g > 2 + 1e-6 && g <= 3 + 1e-6);
                    var eligible = determine.Step.Values?.GetValueOrDefault("eligible_models") ?? -1;
                    return eligible == within ? null : $"{log.Run.Final.Units[attack.Actor!.Value].Definition.Name} had {within} model(s) within 3\" ({string.Join(", ", gaps.Select(g => $"{g:0.00}\""))}) but {eligible} struck";
                });
                return failure ?? (far > 0 ? null : "no model 2-3\" from the enemy struck");
            }),
            Check("3.6.0 p.9: \"After a melee where they attacked, units hit on unmodified rolls of 6 in melee until the end of that round, and those hits don’t count as rolls of 6 for special rules\" (Rending: \"on unmodified results of 6 to hit, those hits get AP(+4)\")", log =>
            {
                var strikes = log.MeleeAttacks.Where(a => log.Run.Final.Units[a.Actor!.Value].Definition.Name == "Blue unit 1").ToList();
                if (strikes.Count != 2) return $"Blue unit 1 struck {strikes.Count} time(s), expected 2";
                string? Read(CoreFrame strike, bool fatigued)
                {
                    var hit = strike.Steps.SelectMany(s => s.Step.Rolls).FirstOrDefault(r => r.Roll == "hit");
                    if (hit is null || log.Dice(hit).Count == 0 || log.Dice(hit).Any(d => d.Raw != 6)) return $"strike {(fatigued ? 2 : 1)}: hit dice {(hit is null ? "none" : Faces(log.Dice(hit)))}, expected all 6s";
                    if (fatigued && (hit.Threshold != 6 || hit.Modifiers.Count > 0)) return $"the fatigued strike rolled at {hit.Threshold}+ with {hit.Modifiers.Count} modifier(s)";
                    if (log.Dice(hit).Count(d => d.Success) != log.Dice(hit).Count) return $"strike {(fatigued ? 2 : 1)}: a 6 missed";
                    var defense = log.Descendants(strike).SelectMany(f => f.Steps).SelectMany(s => s.Step.Rolls).Where(r => r.Roll == "defense").ToList();
                    var rending = defense.Sum(r => r.Modifiers.Any(m => m.Source == "AP" && m.Amount == -4) ? log.Dice(r).Count : 0);
                    var dice = defense.Sum(r => log.Dice(r).Count);
                    if (dice != log.Dice(hit).Count) return $"strike {(fatigued ? 2 : 1)}: {dice} defense dice for {log.Dice(hit).Count} hits";
                    var expected = fatigued ? 0 : dice;
                    return rending == expected ? null : $"strike {(fatigued ? 2 : 1)}: {rending} of {dice} defense dice at AP(4), expected {expected}";
                }
                return Read(strikes[0], fatigued: false) ?? Read(strikes[1], fatigued: true);
            }),
        ], dice:
        [
            new DiceFix("hit", 0, [1], new RollKey(Actor: "Red unit 1")),
            new DiceFix("hit", 0, [1], new RollKey(Actor: "Red unit 2")),
            new DiceFix("hit", 0, [6, 6, 6], new RollKey(Actor: "Blue unit 1", Target: "Red unit 1")),
            new DiceFix("hit", 0, [6, 6, 6], new RollKey(Actor: "Blue unit 1", Target: "Red unit 2")),
            new DiceFix("defense", 0, [6, 6, 6], new RollKey(Actor: "Blue unit 1", Target: "Red unit 1")),
            new DiceFix("defense", 0, [6, 6, 6], new RollKey(Actor: "Blue unit 1", Target: "Red unit 2")),
        ]);
    }

    // ---- core-morale-break@3.6 ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// 3.6.0 p.10 melee morale. Red unit 1 (one Tough(3) model) charges Blue unit 1 (one Tough(3) model) straight across a Dangerous strip
    /// (y 9–11, the table's width); its Dangerous Terrain Test is fixed to one 1, and each side's one melee attack to one hit and one
    /// unblocked wound. Red took two wounds in its activation and Blue one, so Red loses (3.5.1's wounds-caused count is a tie).
    /// </summary>
    static TestGame MoraleBreak36()
    {
        const string name = "core-morale-break";
        var script = AnswerScript.Build()
            .Match(AskQuestions.Activate, (ask, _) => AskPicks.Activate(ask, "Red unit 1", "Charge") ?? AskPicks.Activate(ask, "Blue unit 1", "Hold"))
            .Match("charge_target", (ask, p) => AskPicks.Pieces(ask, p, "Blue unit 1"))
            .Match("strike_back", (ask, _) => new Chosen("strike"))
            .Match("move", Scripts36.Charging(new Dictionary<string, Point2> { ["Red unit 1"] = new(18, 6 + Scripts36.Touch) }))
            .Build();
        var scenario = new ScenarioBuilder(name, seed: 1)
            .RoundCap(1).Table(36, 24).Scripted(script)
            .Army("Red", 1, 1, modelRules: [new(1, "Tough", 3)], weapons: [HandWeapon()], anchors: new Dictionary<int, Point2> { [1] = new(18, 16) })
            .Army("Blue", 1, 1, modelRules: [new(1, "Tough", 3)], weapons: [HandWeapon()], anchors: new Dictionary<int, Point2> { [1] = new(18, 6) })
            .Terrain("minefield", new Point3(18, 10, 0), volumes:
                [new Volume(VolumeKind.Area, Footprint.Rect(36, 2), new Point2(0, 0), new Yaw(0), 0, 1, TerrainDeclarations.Dangerous, null)])
            .Build();
        return Game(name, scenario,
        [
            Check("3.6.0 p.10: \"compare the number of wounds each unit took during this activation instead. The unit with the highest total loses, and must take a Morale Test\"", log =>
            {
                var q = LogQueries.Of(log.Run);
                var lostToTheCrossing = 0;
                var failure = Require(log.Frames.Where(f => f.Sequence == "melee" && f.Steps.Any(s => s.Step.Step == "compare")).ToList(), "no melee was resolved", melee =>
                {
                    var compare = melee.Steps.First(s => s.Step.Step == "compare");
                    var activation = log.Frame(melee.Parent) is { } p ? Ancestors(log, p).LastOrDefault() ?? p : melee;
                    var (charger, defender) = (melee.Actor!.Value, melee.Targets[0]);
                    var (chargerTook, defenderTook) = (q.WoundsTaken(charger, activation.StartSeq, compare.Seq), q.WoundsTaken(defender, activation.StartSeq, compare.Seq));
                    var values = compare.Step.Values!;
                    if (values["charger_wounds"] != chargerTook || values["defender_wounds"] != defenderTook)
                        return $"compare counted {values["charger_wounds"]} and {values["defender_wounds"]}; the units took {chargerTook} and {defenderTook} this activation";
                    Guid? loser = chargerTook > defenderTook ? charger : defenderTook > chargerTook ? defender : null;
                    var tested = log.Children(melee).Where(c => c.Sequence == "morale-test").Select(c => c.Actor).ToList();
                    if (!tested.SequenceEqual(loser is { } l ? [l] : new List<Guid?>())) return $"took {chargerTook} to {defenderTook}: {tested.Count} unit(s) tested";
                    var crossing = log.Frames.Where(f => f.Sequence == "dangerous-terrain-36" && f.Actor == charger && f.StartSeq > activation.StartSeq && f.StartSeq < melee.StartSeq)
                        .Sum(f => q.WoundsTaken(charger, f.StartSeq, f.Steps.LastOrDefault()?.Seq + 1 ?? f.StartSeq));
                    if (loser == charger && crossing > 0 && chargerTook - crossing <= defenderTook) lostToTheCrossing++;
                    return null;
                });
                return failure ?? (lostToTheCrossing > 0 ? null : "no charger lost by a wound from its charge move's Dangerous Terrain Test");
            }),
        ], dice:
        [
            new DiceFix("dangerous-terrain", 0, [1, 6, 6], new RollKey(Actor: "Red unit 1")),
            new DiceFix("hit", 0, [4], new RollKey(Actor: "Red unit 1")),
            new DiceFix("defense", 0, [1], new RollKey(Actor: "Red unit 1")),
            new DiceFix("hit", 0, [4], new RollKey(Actor: "Blue unit 1")),
            new DiceFix("defense", 0, [1], new RollKey(Actor: "Blue unit 1")),
        ]);
    }

    // ---- core-ruins@3.6 ----------------------------------------------------------------------------------------------------------------

    // 36" by 24": ruins across the whole table (y 10–14), Cover + Unstable, so no path goes round them.
    const double Ruins36South = 10, Ruins36North = 14;

    /// <summary>
    /// 3.6.0 p.11 "Ruins - Cover + Unstable" and p.12 Unstable and Dangerous terrain. Red unit 1 (three Fast models) Advances 7.9" straight
    /// into the ruins; Red unit 2 (one model) charges Blue unit 1, which stands in the ruins, 4.74" straight in; Blue holds and passes its
    /// close-in.
    /// </summary>
    static TestGame Ruins36()
    {
        const string name = "core-ruins";
        var charge = Scripts36.Charging(new Dictionary<string, Point2> { ["Red unit 2"] = new(28, 11 + Scripts36.Touch) });
        var script = AnswerScript.Build()
            .Match(AskQuestions.Activate, (ask, _) => AskPicks.Activate(ask, "Red unit 1", "Advance") ?? AskPicks.Activate(ask, "Red unit 2", "Charge")
                ?? AskPicks.Activate(ask, "Blue unit 1", "Hold"))
            .Match("charge_target", (ask, p) => AskPicks.Pieces(ask, p, "Blue unit 1"))
            .Match("strike_back", (ask, _) => new Chosen("strike"))
            .Match("move", (ask, p) => charge(ask, p)
                ?? (ask.Legal is MoveRegion { ContactWith: null, Containment: null } region && Scripts36.UnitOf(p, region) == "Red unit 1"
                    && p.Stack.FirstOrDefault(f => f.Id == ask.FrameId)?.Sequence == "advance"
                    ? new Destinations([.. region.ModelIds.Select(id => Scripts36.MoveBy(p, id, 0, -7.9))]) : null))
            .Build();
        var scenario = new ScenarioBuilder(name, seed: 1)
            .RoundCap(1).Table(36, 24).Scripted(script)
            .Army("Red", 2, 3, modelRules: [new(1, "Fast")], weapons: [HandWeapon()], sizes: new Dictionary<int, int> { [2] = 1 },
                anchors: new Dictionary<int, Point2> { [1] = new(2, 18), [2] = new(28, 17) })
            .Army("Blue", 1, 1, modelRules: [new(1, "Tough", 6)], weapons: [HandWeapon()], anchors: new Dictionary<int, Point2> { [1] = new(28, 11) })
            .Terrain("ruins", new Point3(18, (Ruins36South + Ruins36North) / 2, 0), volumes:
                [new Volume(VolumeKind.Area, Footprint.Rect(36, Ruins36North - Ruins36South), new Point2(0, 0), new Yaw(0), 0, 4,
                    TerrainDeclarations.Cover | TerrainDeclarations.DangerousOnRushCharge, null)])
            .Build();
        // A model whose straight move starts or ends in the band, or spans it, went into or through it.
        static bool Into(ModelRelocation m) => m is { From: OnTable from, To: OnTable to }
            && Math.Max(from.Placement.Position.Y, to.Placement.Position.Y) >= Ruins36South && Math.Min(from.Placement.Position.Y, to.Placement.Position.Y) <= Ruins36North;
        static double Length(ModelRelocation m) => m is { From: OnTable from, To: OnTable to } ? from.Placement.Position.Horizontal.DistanceTo(to.Placement.Position.Horizontal) : 0;
        return Game(name, scenario,
        [
            Check("3.6.0 p.12: \"If any model in a unit moves over 6” when moving into or through unstable terrain, then it counts as Dangerous terrain\" (an Advance of 7.9\" tests, a Charge of 4.74\" does not)", log =>
            {
                var (over, under) = (0, 0);
                foreach (var f in log.Frames.Where(f => f.Sequence is "advance" or "rush" or "charge"))
                {
                    var moves = f.Steps.Where(s => s.Step.Step == "move").SelectMany(s => Moved(s.Step)).Where(m => log.UnitOf(m.ModelId) == f.Actor).ToList();
                    if (!moves.Any(Into)) continue;
                    var longest = moves.Max(Length);
                    var tested = log.Children(f).Any(c => c.Sequence == "dangerous-terrain-36");
                    if (longest > 6 + 1e-6) { over++; if (!tested) return $"{f.Sequence} {f.Id}: a move of {longest:0.00}\" into the ruins took no Dangerous Terrain Test"; }
                    else { under++; if (tested) return $"{f.Sequence} {f.Id}: a move of {longest:0.00}\" into the ruins took a Dangerous Terrain Test"; }
                }
                return over > 0 && under > 0 ? null : $"{over} move(s) over 6\" and {under} of 6\" or less into the ruins; both are needed";
            }),
            Check("3.6.0 p.12: \"the entire unit must take a Dangerous Terrain Test ... roll as many dice as the number of wounds it would take to fully destroy the unit\" (three one-wound models: one test of 3 dice)", log =>
                Require(log.Frames.Where(f => f.Sequence == "advance" && log.Children(f).Any(c => c.Sequence == "dangerous-terrain-36")).ToList(), "no Advance took a Dangerous Terrain Test", f =>
                {
                    var tests = log.Children(f).Where(c => c.Sequence is "dangerous-terrain-36" or "dangerous-terrain").ToList();
                    var dice = tests.SelectMany(t => t.Steps).SelectMany(s => s.Step.Rolls).Where(r => r.Roll == "dangerous-terrain").Sum(r => log.Dice(r).Count);
                    return tests.Count == 1 && dice == 3 ? null : $"{tests.Count} test(s) of {dice} dice in all";
                })),
        ]);
    }

    // ---- core-rough-ground@3.6 ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// 3.6.0 p.12 Dangerous terrain. Red unit 1 is a Tough(3) model and two one-wound models in a row along y 18 (x 2, 3.76, 5.52); it
    /// Advances 4" straight down, and only its third model's path crosses a minefield (x 5–7, y 12–16). The unit's test is fixed to two 1s.
    /// </summary>
    static TestGame RoughGround36()
    {
        const string name = "core-rough-ground";
        var script = AnswerScript.Build()
            .Match(AskQuestions.Activate, (ask, _) => AskPicks.Activate(ask, "Red unit 1", "Advance") ?? AskPicks.Activate(ask, "Blue unit 1", "Hold"))
            .Match("move", (ask, p) => ask.Legal is MoveRegion { ContactWith: null, Containment: null } region && Scripts36.UnitOf(p, region) == "Red unit 1"
                ? new Destinations([.. region.ModelIds.Select(id => Scripts36.MoveBy(p, id, 0, -4))]) : null)
            .Build();
        var scenario = new ScenarioBuilder(name, seed: 1)
            .RoundCap(1).Table(36, 24).Scripted(script)
            .Army("Red", 1, 3, weapons: [HandWeapon()], models: [new ScenarioModel(1, 1, [new(0, "Tough", 3)])], anchors: new Dictionary<int, Point2> { [1] = new(2, 18) })
            .Army("Blue", 1, 1, weapons: [HandWeapon()], anchors: new Dictionary<int, Point2> { [1] = new(30, 4) })
            .Terrain("minefield", new Point3(6, 14, 0), volumes:
                [new Volume(VolumeKind.Area, Footprint.Rect(2, 4), new Point2(0, 0), new Yaw(0), 0, 1, TerrainDeclarations.Dangerous, null)])
            .Build();
        return Game(name, scenario,
        [
            Check("3.6.0 p.12: \"If any model in a unit moves into or through Dangerous terrain ... the entire unit must take a Dangerous Terrain Test. To take a Dangerous Terrain Test, roll as many dice as the number of wounds it would take to fully destroy the unit, and for each result is 1 the unit takes one wound\" (Tough(3) and two one-wound models: 5 dice)", log =>
            {
                var q = LogQueries.Of(log.Run);
                return Require(log.Frames.Where(f => f.Sequence is "dangerous-terrain-36" or "dangerous-terrain").ToList(), "no Dangerous Terrain Test", t =>
                {
                    if (t.Sequence != "dangerous-terrain-36") return $"a {t.Sequence} frame, not the unit's test";
                    var dice = t.Steps.SelectMany(s => s.Step.Rolls).Where(r => r.Roll == "dangerous-terrain").SelectMany(r => log.Dice(r)).ToList();
                    var took = q.WoundsTaken(t.Actor!.Value, t.StartSeq, t.Steps.Last().Seq + 1);
                    return dice.Count == 5 && took == dice.Count(d => d.Raw == 1) ? null : $"{dice.Count} dice ({Faces(dice)}), {took} wound(s) taken";
                });
            }),
        ], dice: [new DiceFix("dangerous-terrain", 0, [1, 1, 6, 6, 6], new RollKey(Actor: "Red unit 1"))]);
    }

    // ---- core-deployment@3.6 -----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// 3.6.0 p.15 Scout on a 48" by 36" table (Red's zone y 24–36, Blue's y 0–12; the sim places a Scout where its army anchors it). Blue
    /// units 1 and 2 deploy in rows from (2, 10) and (18, 10), Red unit 2 from (20, 26). Red unit 1 scouts from (30.4, 15.7): its base 8.93" out of its
    /// zone, 9.29" from Blue unit 2. Blue unit 3 scouts from (32.5, 20.32), 8.95" out of its zone, over 9" from
    /// Red unit 2 and within 9" of Red unit 1, a Scout.
    /// </summary>
    static TestGame Deployment36()
    {
        const string name = "core-deployment";
        var scenario = new ScenarioBuilder(name, seed: 1)
            .RoundCap(1).Table(48, 36)
            .Army("Red", 2, 3, unitRules: [new ScenarioRule(1, "Scout")], weapons: [Rifle(), HandWeapon()], setAside: [1],
                anchors: new Dictionary<int, Point2> { [1] = new(30.4, 15.7), [2] = new(20, 26) })
            .Army("Blue", 3, 3, unitRules: [new ScenarioRule(3, "Scout")], weapons: [Rifle(), HandWeapon()], setAside: [3],
                anchors: new Dictionary<int, Point2> { [1] = new(2, 10), [2] = new(18, 10), [3] = new(32.5, 20.32) })
            .Build();
        bool Scout(CoreLog log, Guid unit) => log.Run.Final.Units[unit].Definition.Properties.Any(p => p.Name == "Scout");
        return Game(name, scenario,
        [
            Check("3.6.0 p.15 Scout: \"After all other units are deployed, may be deployed anywhere fully within 9” of their deployment zone\" (Red y 15–36, Blue y 0–21)", log =>
                Require(log.Deployments.Where(d => d.Question == AskQuestions.DeployScouts).ToList(), "no Scout was deployed", d =>
                {
                    if (log.Deployments.Any(o => o.Question == AskQuestions.Deploy && o.Seq > d.Seq)) return "a Scout deployed before the other units";
                    var red = log.Run.Final.Units[d.Unit].Definition.Name.StartsWith("Red", StringComparison.Ordinal);
                    return log.FirstPlacement(d.Unit).All(p => red ? p.Y >= 15 + ModelRadius - 1e-6 : p.Y <= 21 - ModelRadius + 1e-6) ? null
                        : $"{log.Run.Final.Units[d.Unit].Definition.Name} stood beyond 9\" of its zone";
                }), "Scout"),
            Check("3.6.0 p.15 Scout: \"over 9” away from non-Scout enemy units\"", log =>
                Require(log.Deployments.Where(d => d.Question == AskQuestions.DeployScouts).ToList(), "no Scout was deployed", d =>
                {
                    var enemies = log.EnemiesOf(d.Unit).Where(u => !Scout(log, u)).SelectMany(u => log.Placed(u, d.Seq + 1)).ToList();
                    if (enemies.Count == 0) return "no non-Scout enemy stood on the table";
                    var gap = log.FirstPlacement(d.Unit).Min(p => enemies.Min(q => p.DistanceTo(q) - 2 * ModelRadius));
                    return gap > 9 ? null : $"{log.Run.Final.Units[d.Unit].Definition.Name} stood {gap:0.00}\" from a non-Scout enemy";
                }) ?? (log.Deployments.Where(d => d.Question == AskQuestions.DeployScouts).Any(d => log.EnemiesOf(d.Unit).Where(u => Scout(log, u))
                    .SelectMany(u => log.FirstPlacement(u)).Any(q => log.FirstPlacement(d.Unit).Any(p => p.DistanceTo(q) - 2 * ModelRadius <= 9)))
                    ? null : "no Scout stood within 9\" of an enemy Scout"), "Scout"),
        ], covers: ["Scout"], evidence: new Dictionary<string, Func<IReadOnlyList<GameEvent>, bool>> { ["Scout"] = log => Answered(log, AskQuestions.DeployScouts) });
    }

    // ---- core-side-battle (both editions) ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Side Battle deployment on a 72" by 48" table, five units a side placed by the sim. p.22 draws zone A as a triangle in the bottom-left
    /// corner, legs 30" up the short edge and 54" along the long; 3.6.0 p.21 draws 36" and 48" (rules-diff M1, measured on its 6" grid). Zone B
    /// is the mirror in the top-right corner.
    /// </summary>
    static TestGame SideBattle()
    {
        const string name = "core-side-battle";
        var scenario = new ScenarioBuilder(name, seed: 1)
            .RoundCap(1).Table(72, 48).Zones(ZoneStyles.SideBattle)
            .Army("Red", 5, 3, weapons: [Rifle(), HandWeapon()])
            .Army("Blue", 5, 3, weapons: [Rifle(), HandWeapon()])
            .Build();
        // (short leg, long leg) as each edition's diagram draws them.
        static (double Short, double Long) Legs(string rules) => rules == RuleSets.Gf360 ? (36, 48) : (30, 54);
        static Point2[] Triangle(string rules, bool red)
        {
            var (s, l) = Legs(rules);
            return red ? [new(0, 0), new(l, 0), new(0, s)] : [new(72, 48), new(72 - l, 48), new(72, 48 - s)];
        }
        // The base's centre inside the triangle and at least its radius from every side.
        static bool FullyWithin(Point2[] t, Point2 p, double r)
        {
            for (var i = 0; i < 3; i++)
            {
                var (a, b, c) = (t[i], t[(i + 1) % 3], t[(i + 2) % 3]);
                double Side(Point2 q) => ((b.X - a.X) * (q.Y - a.Y) - (b.Y - a.Y) * (q.X - a.X)) / a.DistanceTo(b);
                if (Side(p) * Math.Sign(Side(c)) < r - 1e-6) return false;
            }
            return true;
        }
        return Game(name, scenario,
        [
            Check("p.22 / 3.6.0 p.21 Side Battle: zone A's legs are 30\" by 54\" (3.5.1) and 36\" by 48\" (3.6.0), zone B its mirror", log =>
            {
                var rules = log.Run.Game.Edition;
                var legs = Tabletop.GameServer.Rules.GameRulesRegistry.Resolve(rules).Zones;
                foreach (var zone in log.Run.Final.Board.Table.Zones)
                {
                    var drawn = ZoneStyles.Resolve(zone.Shape, log.Run.Final.Board.Table.Size, legs).Single().Vertices;
                    var expected = Triangle(rules, zone.TeamOrdinal == 1);
                    if (drawn.Count != 3 || expected.Any(v => !drawn.Any(d => d.DistanceTo(v) < 1e-6)))
                        return $"team {zone.TeamOrdinal}'s zone is {string.Join(" ", drawn.Select(d => $"({d.X:0.##},{d.Y:0.##})"))}, expected {string.Join(" ", expected.Select(d => $"({d.X:0.##},{d.Y:0.##})"))}";
                }
                return null;
            }),
            Check("p.6: units are deployed \"fully within\" their deployment zone; the zone is p.22's / 3.6.0 p.21's Side Battle triangle", log =>
                Require(log.Deployments.Where(d => d.Question == AskQuestions.Deploy).ToList(), "nobody deployed", d =>
                {
                    var red = log.Run.Final.Units[d.Unit].Definition.Name.StartsWith("Red", StringComparison.Ordinal);
                    var zone = Triangle(log.Run.Game.Edition, red);
                    return log.FirstPlacement(d.Unit).All(p => FullyWithin(zone, p, ModelRadius)) ? null
                        : $"{log.Run.Final.Units[d.Unit].Definition.Name} was placed outside its zone";
                })),
        ]);
    }

    static IEnumerable<CoreFrame> Ancestors(CoreLog log, CoreFrame frame)
    {
        for (var f = log.Frame(frame.Parent); f is not null; f = log.Frame(f.Parent)) yield return f;
    }

    /// <summary>The model's closest base-to-base gap to a model of the unit, both on the table just before that log position; null when either is not.</summary>
    static double? GapTo(CoreLog log, Guid model, Guid unit, long seq)
    {
        var at = log.LocationsBefore(seq);
        PlacedModel? Placed(Guid id) => at.GetValueOrDefault(id) is OnTable t ? new PlacedModel(log.Body(id), t.Placement) : null;
        if (Placed(model) is not { } mine) return null;
        var gaps = log.Run.Final.Models.Values.Where(m => m.UnitId == unit).Select(m => Placed(m.Id)).OfType<PlacedModel>().Select(t => Distance.EdgeToEdge(mine, t)).ToList();
        return gaps.Count == 0 ? null : gaps.Min();
    }
}

/// <summary>What the 3.6.0 games' scripts share: placing a model by hand, and reading a move Ask's unit.</summary>
static class Scripts36
{
    /// <summary>Base contact plus a hair: a charge landing this far from its target is drawn flush (GfMoves.ChargeLanding) and never overlaps.</summary>
    public const double Touch = 1.26 + 0.001;

    /// <summary>The game runs under Grimdark Future 3.6.0 alone: named &lt;name&gt;@3.6 (SuiteCatalog.Expand).</summary>
    public static TestGame In36(this TestGame game) => game with { Edition = RuleSets.Gf360 };

    /// <summary>The model moved on the table to that point, its height and facing kept.</summary>
    public static ModelRelocation MoveTo(Projection p, Guid model, Point2 at)
    {
        var placement = ((OnTable)p.Models[model].Location).Placement;
        return new ModelRelocation(model, p.Models[model].Location, new OnTable(placement with { Position = placement.Position with { X = at.X, Y = at.Y } }));
    }

    /// <summary>
    /// A move pick: a one-model charging unit named in <paramref name="landing"/> lands at its point (drawn flush, GfMoves.ChargeLanding),
    /// and a charged unit passes its 3" close-in, so the models stand where they were placed. Anything else falls to the policy.
    /// </summary>
    public static Func<Ask, Projection, AskAnswer?> Charging(IReadOnlyDictionary<string, Point2> landing) => (ask, p) =>
        ask.Legal is MoveRegion { ContactWith.Count: > 0 } region && UnitOf(p, region) is { } unit && landing.TryGetValue(unit, out var at)
            ? new Destinations([MoveTo(p, region.ModelIds[0], at)])
            : ask.Prompt.StartsWith("Move up to 3\" into contact", StringComparison.Ordinal) ? new Passed() : null;

    /// <summary>The model moved on the table by (dx, dy).</summary>
    public static ModelRelocation MoveBy(Projection p, Guid model, double dx, double dy)
    {
        var at = ((OnTable)p.Models[model].Location).Placement.Position;
        return MoveTo(p, model, new Point2(at.X + dx, at.Y + dy));
    }

    /// <summary>The unit every model of the move belongs to, by name; null when they are of several or none.</summary>
    public static string? UnitOf(Projection p, MoveRegion region) =>
        region.ModelIds.Select(id => AskPicks.UnitNameOf(p, id)).Distinct().ToList() is [var only] ? only : null;
}
