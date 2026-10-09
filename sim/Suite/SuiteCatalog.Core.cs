using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    private static partial IEnumerable<TestGame> Core() => [CoreGames.FourActions(), CoreGames.FiringLine(), CoreGames.ChargeAndStrikeBack(), CoreGames.MoraleBreak(), CoreGames.RoughGround(), CoreGames.CoverAndSight(), CoreGames.TransportRun(), CoreGames.Overkill(), CoreGames.HoldTheMarkers(), CoreGames.Deployment()];
}

/// <summary>
/// The core group's games (rulebook pp.4–12). The policy plays them; layouts, goals and seeds bring each rule into play, and dice fixes
/// set the rolls an exact check reads. Every expected value is a literal from the rule text, quoted with its page.
/// </summary>
file static class CoreGames
{
    const string Group = SuiteCatalog.CoreGroup;

    static ScenarioWeapon Rifle(int attacks = 1, string name = "Rifle") => new(name, new Dictionary<string, string> { ["Range"] = "24\"", ["Attacks"] = $"{attacks}" }, []);
    static ScenarioWeapon HandWeapon(int attacks = 1) => new("Hand Weapon", new Dictionary<string, string> { ["Attacks"] = $"{attacks}" }, []);

    /// <summary>Each model would rather stand at the nearest enemy model: the legal destination nearest it wins, so the armies close and fight.</summary>
    public static Point2? NearestEnemy(Guid modelId, PolicyView view)
    {
        if (view.PlacedOf(modelId) is not { } placed || view.TeamOf(view.ModelOf(modelId).UnitId) is not { } team) return null;
        var at = placed.Placement.Position.Horizontal;
        return view.Projection.Models.Values
            .Where(m => view.TeamOf(m.UnitId) is { } t && t != team)
            .Select(m => view.PlacedOf(m.Id)?.Placement.Position.Horizontal)
            .Where(p => p is not null)
            .MinBy(p => p!.Value.DistanceTo(at));
    }

    static TestGame Game(string name, Scenario scenario, IReadOnlyList<GameCheck> checks, IReadOnlyList<string>? covers = null, IReadOnlyList<DiceFix>? dice = null,
        IReadOnlyDictionary<string, Func<IReadOnlyList<GameEvent>, bool>>? evidence = null) =>
        new(name, Group, scenario, covers ?? [], dice ?? [], [Checks.Ended(), Checks.Invariants(), Checks.FiredAll(), .. checks], evidence);

    /// <summary>A check over the run read as frames, steps, dice and positions.</summary>
    static GameCheck Check(string name, Func<CoreLog, string?> failure, params string[] entries) =>
        new(name, run => failure(new CoreLog(run)), entries.Length == 0 ? null : entries);

    // ---- core-four-actions -------------------------------------------------------------------------------------------------------------

    /// <summary>p.7: the four actions, their moves, the activation order and the round-4 end under strict.</summary>
    public static TestGame FourActions()
    {
        const string name = "core-four-actions";
        var scenario = new ScenarioBuilder(name, seed: 1)
            .RoundCap(5).Table(48, 36)
            .Army("Red", 3, 3, weapons: [Rifle(), HandWeapon()])
            .Army("Blue", 3, 3, weapons: [Rifle(), HandWeapon()])
            .Seeking(NearestEnemy)
            .Build();
        return Game(name, scenario,
        [
            MoveAllowances(),
            Check("p.7 Hold: \"the models in the unit may not move\"", log =>
                log.Frames.Where(f => f.Sequence == "hold").SelectMany(f => f.Steps.SelectMany(s => Moved(s.Step).Select(m => (f, m))))
                    .FirstOrDefault(x => log.UnitOf(x.m.ModelId) == x.f.Actor) is { f: { } held } ? $"a hold frame {held.Id} moved its unit" : null),
            Check("p.7 Rush: \"Moves 12\" but can't shoot\"", log =>
                log.Frames.FirstOrDefault(f => f.Sequence == "shooting" && log.Frame(f.Parent)?.Sequence == "rush") is { } shot ? $"shooting {shot.Id} under a rush" : null),
            Check("p.7: \"Units may only take a Charge action if at least one model is within charge distance\" (12\")", log =>
                Require(log.Frames.Where(f => f.Sequence == "charge").SelectMany(f => f.Steps).Where(s => s.Step.Step == "charge-target" && s.Step.Values?.ContainsKey("charge_range") == true).ToList(),
                    "no charge was declared",
                    s => s.Step.Values!["charge_range"] <= 1200 ? null : $"a charge declared at {s.Step.Values["charge_range"] / 100.0}\"")),
            Check("p.7: \"the player picks a unit that has not been activated yet\"", log =>
                log.Activations.GroupBy(a => (a.Round, a.Unit)).FirstOrDefault(g => g.Count() > 1) is { } twice ? $"unit {twice.Key.Unit} activated {twice.Count()} times in round {twice.Key.Round}" : null),
            Check("p.7: \"On each new round the player that finished activating first on the last round gets to activate first\"", log =>
                Require(log.Rounds.Where(r => r.Round >= 2).ToList(), "no second round", r =>
                {
                    var last = log.Activations.Where(a => a.Round == r.Round - 1).GroupBy(a => a.Team).Select(g => (Team: g.Key, Last: g.Max(a => a.Seq))).OrderBy(x => x.Last).ToList();
                    return last.Count < 2 || r.Order[0] == last[0].Team ? null : $"round {r.Round} opened with {r.Order[0]}, but {last[0].Team} finished first in round {r.Round - 1}";
                })),
            FinalRound(Enforcement.Strict),
        ]);
    }

    /// <summary>
    /// p.7: "Advance - Moves 6"", "Rush - Moves 12"", "Charge - Moves 12" into melee"; p.9: the target unit's models "must move by up to 3"";
    /// p.10: "the other unit may move by up to 3"" when one was destroyed, else "the charging unit must move back by 1"". The offered allowance
    /// and every model's displacement on the table.
    /// </summary>
    static GameCheck MoveAllowances() => Check("p.7, p.9, p.10: move allowances (Advance 6\", Rush 12\", Charge 12\", close-in 3\", consolidation 3\" or 1\")", log =>
    {
        var seen = new HashSet<string>();
        foreach (var f in log.Frames)
            foreach (var s in f.Steps)
            {
                double? expected = (f.Sequence, s.Step.Step) switch
                {
                    ("advance", "move") => 6, ("rush", "move") => 12, ("charge", "move") => 12, ("charge", "defender-closes") => 3,
                    ("melee", "consolidate") => log.UnitDestroyedBefore(f.Actor!.Value, s.Seq) || log.UnitDestroyedBefore(f.Targets[0], s.Seq) ? 3 : 1,
                    _ => null,
                };
                if (expected is not { } allowance) continue;
                seen.Add(f.Sequence);
                foreach (var ask in s.Asks.Where(a => a.Question == "move"))
                    if (ask.Legal is MoveRegion { Containment: null } m && Math.Abs(m.Allowance - allowance) > 1e-9)
                        return $"{f.Sequence} {s.Step.Step} offered {m.Allowance}\", expected {allowance}\"";
                foreach (var move in Moved(s.Step))
                    if (move is { From: OnTable from, To: OnTable to } && from.Placement.Position.Horizontal.DistanceTo(to.Placement.Position.Horizontal) > allowance + 1e-6)
                        return $"{f.Sequence} {s.Step.Step} moved a model {from.Placement.Position.Horizontal.DistanceTo(to.Placement.Position.Horizontal):0.00}\", allowance {allowance}\"";
            }
        return new[] { "advance", "rush", "charge", "melee" }.Except(seen).ToList() is { Count: > 0 } missing ? $"no {string.Join(", ", missing)} move was made" : null;
    });

    /// <summary>p.7: "After 4 rounds have been played the game ends" — strict ends it, warn starts round 5 with a warning, silent starts it quietly.</summary>
    static GameCheck FinalRound(Enforcement enforcement) => Check($"p.7: \"After 4 rounds have been played the game ends\" ({enforcement.ToString().ToLowerInvariant()})", log =>
    {
        var fifth = log.Events.FirstOrDefault(e => e.Payload is RoundStarted { Round: 5 });
        var ended = log.Events.FirstOrDefault(e => e.Payload is GameEnded);
        if (!log.Events.Any(e => e.Payload is RoundStarted { Round: 4 })) return "round 4 never started";
        return enforcement switch
        {
            Enforcement.Strict => ended is null ? "no game_ended" : fifth is not null ? "round 5 started" : null,
            Enforcement.Warn => ended is not null ? "the game ended" : fifth is null ? "round 5 never started" : fifth.Warning is null ? "round 5 started with no warning" : null,
            _ => ended is not null ? "the game ended" : fifth is null ? "round 5 never started" : fifth.Warning is not null ? $"round 5 warned: {fifth.Warning}" : null,
        };
    });

    // ---- core-firing-line ------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// p.8 shooting: Red's squad carries two weapon types, so it may split its fire between two targets. The first hit roll is fixed to a 6
    /// and a 1 and the first defense roll to a 1.
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
            Check("p.5: \"a roll of 6 always counts as a success and a roll of 1 always counts as a fail\" (hit #0 fixed to 6, 1)", log =>
                log.Edited("hit", 0) is not { } dice ? "hit #0 was not edited"
                : dice[0] is { Raw: 6, Success: true } && dice[1] is { Raw: 1, Success: false } ? null : $"hit #0 reads {Faces(dice)}"),
            RollToBlock(),
            Check("p.8: \"all failed rolls cause one wound each\" (defense #0 fixed to 1)", log =>
                log.Rolls("defense") is not [var first, ..] ? "no defense roll"
                : log.Dice(first.Roll) is [{ Raw: 1, Success: false }, ..] && log.CasualtiesUnder(first.Frame) >= 1 ? null
                : $"defense #0 reads {Faces(log.Dice(first.Roll))} and removed {log.CasualtiesUnder(first.Frame)}"),
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
        ], dice: [new DiceFix("hit", 0, [6, 1]), new DiceFix("defense", 0, [1])]);
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

    // ---- core-charge-and-strike-back -------------------------------------------------------------------------------------------------

    /// <summary>
    /// p.9–10 melee: armies with only hand weapons close in and charge. The first hit roll of the game (a charge's) is fixed to a 6 and a 1.
    /// Round 4's end is under warn.
    /// </summary>
    public static TestGame ChargeAndStrikeBack()
    {
        const string name = "core-charge-and-strike-back";
        var scenario = new ScenarioBuilder(name, seed: 1)
            .Enforcing(Enforcement.Warn).RoundCap(5).Table(36, 30)
            .Army("Red", 4, 3, weapons: [HandWeapon(2)])
            .Army("Blue", 4, 3, weapons: [HandWeapon(2)])
            .Seeking(NearestEnemy)
            .Build();
        return Game(name, scenario,
        [
            Check("p.5: \"a roll of 6 always counts as a success and a roll of 1 always counts as a fail\" (melee hit #0 fixed to 6, 1)", log =>
                log.Edited("hit", 0) is not { } dice ? "hit #0 was not edited"
                : dice[0] is { Raw: 6, Success: true } && dice[1] is { Raw: 1, Success: false } ? null : $"hit #0 reads {Faces(dice)}"),
            RollToBlock(),
            MoveAllowances(),
            Check("p.9: \"Once all charging models have attacked, the defending unit may choose to strike back ... but doesn't have to\"", log =>
            {
                var struck = 0;
                foreach (var melee in log.Frames.Where(f => f.Sequence == "melee"))
                {
                    var answer = melee.Steps.FirstOrDefault(s => s.Step.Step == "strike-back")?.Step.Answer;
                    var back = log.Children(melee).Count(c => c.Sequence == "attack-sequence" && c.Actor == melee.Targets[0] && c.Targets[0] == melee.Actor);
                    if (answer is Chosen { OptionId: "strike" }) { if (back == 0) return $"melee {melee.Id}: the defender chose to strike back and did not"; struck++; }
                    else if (back > 0) return $"melee {melee.Id}: the defender struck back without choosing to";
                }
                return struck > 0 ? null : "no defender struck back";
            }),
            Check("p.9: \"After attacking in melee for the first time during a round ... units only hit on unmodified rolls of 6\"; p.10: Shaken units \"may strike back counting as fatigued\"", log =>
            {
                var fatigued = 0;
                foreach (var round in log.MeleeAttacks.GroupBy(a => (a.Round, a.Actor)))
                    foreach (var (attack, i) in round.Select((a, i) => (a, i)))
                    {
                        var rolls = attack.Steps.SelectMany(s => s.Step.Rolls).Where(r => r.Roll == "hit").ToList();
                        if (i == 0 && !log.HasCondition(attack.Actor!.Value, "shaken", attack.StartSeq))
                        {
                            if (rolls.Any(r => r.Threshold != 4)) return $"a unit's first melee of round {round.Key.Round} hit on {rolls.First(r => r.Threshold != 4).Threshold}+, not its Quality 4+";
                            continue;
                        }
                        fatigued++;
                        if (rolls.FirstOrDefault(r => r.Threshold != 6 || r.Modifiers.Count > 0 || log.Dice(r).Any(d => d.Success != (d.Raw == 6))) is { } bad)
                            return $"a fatigued melee in round {round.Key.Round} rolled {bad.Threshold}+ {Faces(log.Dice(bad))}";
                    }
                return fatigued > 0 ? null : "no unit attacked in melee twice in a round";
            }),
            Check("p.10: \"If one unit caused more wounds than the other ... the opposing unit must take a morale test\"; a tie tests neither", log =>
                Require(log.Frames.Where(f => f.Sequence == "melee" && f.Steps.Any(s => s.Step.Values?.ContainsKey("compared") == true)).ToList(), "no melee was resolved", melee =>
                {
                    var values = melee.Steps.First(s => s.Step.Values?.ContainsKey("compared") == true).Step.Values!;
                    var (charger, defender) = (values["charger_wounds"], values["defender_wounds"]);
                    var at = melee.Steps.First(s => s.Step.Values?.ContainsKey("compared") == true).Seq;
                    var destroyed = log.UnitDestroyedBefore(melee.Targets[0], at) ? melee.Targets[0] : log.UnitDestroyedBefore(melee.Actor!.Value, at) ? melee.Actor : null;
                    Guid? loser = destroyed ?? (charger > defender ? melee.Targets[0] : defender > charger ? melee.Actor : null);
                    var tested = log.Children(melee).Where(c => c.Sequence == "morale-test").Select(c => c.Actor).ToList();
                    // "units that are destroyed in melee always count as having lost, and their opponent doesn't have to take a morale test"
                    var expected = loser is { } l && l != destroyed ? [l] : new List<Guid?>();
                    return tested.SequenceEqual(expected) ? null : $"melee {melee.Id} ({charger} wounds to {defender}) tested {tested.Count} unit(s), expected {expected.Count}";
                })),
            MeleeMorale(),
            FinalRound(Enforcement.Warn),
        ], dice: [new DiceFix("hit", 0, [6, 1])]);
    }

    /// <summary>p.10 melee morale: passed, nothing; failed over half its starting size, Shaken; failed at half or less, Routs. The logged roll decides.</summary>
    static GameCheck MeleeMorale() => Check("p.10: melee morale \"failed ... over half ... Shaken\"; \"failed ... half or less ... Routs\"", log =>
        Require(log.MoraleTests.Where(t => t.Frame.Action == "melee").ToList(), "no melee morale test", t =>
        {
            var unit = t.Frame.Actor!.Value;
            var passed = t.Roll is { } roll && log.Dice(roll)[0].Success;
            var living = log.LivingModels(unit, t.Test.Seq);
            var shaken = t.Test.Step.Changes.OfType<ConditionSet>().Any(c => c is { Condition: "shaken", Present: true });
            var routed = Moved(t.Test.Step).Count(m => m.To is OnShelf);
            var (wantShaken, wantRouted) = passed ? (false, 0) : living * 2 <= log.StartingSize(unit) ? (false, living) : (true, 0);
            return (shaken, routed) == (wantShaken, wantRouted) ? null
                : $"a melee morale test ({(passed ? "passed" : "failed")}, {living} of {log.StartingSize(unit)} left) shook {shaken} and routed {routed}";
        }));

    // ---- core-morale-break -----------------------------------------------------------------------------------------------------------

    /// <summary>p.10 general morale, Shaken and idle activations: five-model squads shoot each other down. Round 4's end is under silent.</summary>
    public static TestGame MoraleBreak()
    {
        const string name = "core-morale-break";
        var scenario = new ScenarioBuilder(name, seed: 1)
            .Enforcing(Enforcement.Silent).RoundCap(5).Table(48, 36)
            .Army("Red", 3, 5, weapons: [Rifle(2)])
            .Army("Blue", 3, 5, weapons: [Rifle(2)])
            .Build();
        return Game(name, scenario,
        [
            Check("p.10: \"At the end of an activation in which a unit takes wounds that leave it with half or less of its starting size ... it must take a morale test\"", log =>
            {
                var tests = log.MoraleTests.Where(t => t.Frame.Action == "wounds").ToList();
                foreach (var t in tests)
                    if (log.LivingModels(t.Frame.Actor!.Value, t.Test.Seq) * 2 > log.StartingSize(t.Frame.Actor!.Value)) return $"a unit over half its size tested at seq {t.Test.Seq}";
                foreach (var turn in log.Turns)
                    foreach (var unit in log.Run.Final.Units.Keys)
                    {
                        var (before, after) = (log.LivingModels(unit, turn.Start), log.LivingModels(unit, turn.End));
                        if (after == before || after == 0 || after * 2 > log.StartingSize(unit)) continue;
                        if (!tests.Any(t => t.Frame.Actor == unit && t.Test.Seq > turn.Start && t.Test.Seq < turn.End)) return $"a unit left at {after} of {log.StartingSize(unit)} was not tested in the turn at seq {turn.Start}";
                    }
                return tests.Count > 0 ? null : "no general morale test";
            }),
            Check("p.10: \"If the test is passed, nothing happens. If the test is failed, the unit is Shaken\"; Shaken units \"always fail morale tests\"", log =>
            {
                var tests = log.MoraleTests.Where(t => t.Frame.Action == "wounds").ToList();
                foreach (var (frame, test, roll) in tests)
                {
                    var wasShaken = log.HasCondition(frame.Actor!.Value, "shaken", test.Seq);
                    if (wasShaken && roll is not null) return "a Shaken unit rolled its morale test";
                    var passed = roll is not null && log.Dice(roll)[0].Success;
                    var shaken = test.Step.Changes.OfType<ConditionSet>().Any(c => c is { Condition: "shaken", Present: true });
                    if (shaken == passed || Moved(test.Step).Any()) return $"a general morale test {(passed ? "passed" : "failed")} and {(shaken ? "shook" : "did not shake")} the unit";
                }
                var rolled = tests.Where(t => t.Roll is not null).Select(t => log.Dice(t.Roll!)[0].Success).ToList();
                return rolled.Contains(true) && rolled.Contains(false) ? null : "the logged morale rolls were not both a pass and a fail";
            }),
            ShakenIdles(),
            FinalRound(Enforcement.Silent),
        ]);
    }

    /// <summary>p.10: "Shaken units must stay idle"; idling "stops them being Shaken at the end of the activation".</summary>
    static GameCheck ShakenIdles() => Check("p.10: \"When activated, Shaken units must spend their activation being idle ... which stops them being Shaken at the end of the activation\"", log =>
    {
        var idled = 0;
        foreach (var e in log.Events)
        {
            if (e.Payload is not AskOpened { Ask: { Question: AskQuestions.Activate, Legal: Options options } }) continue;
            foreach (var group in options.Choices.GroupBy(c => c.Pieces[0]))
                if (log.HasCondition(group.Key, "shaken", e.Seq) && group.Any(c => !c.Id.EndsWith("/Idle")))
                    return $"a Shaken unit was offered {string.Join(", ", group.Select(c => c.Label))}";
        }
        foreach (var turn in log.Turns)
            foreach (var a in log.Activations.Where(a => a.Seq > turn.Start && a.Seq < turn.End && log.HasCondition(a.Unit, "shaken", a.Seq)))
            {
                idled++;
                if (log.HasCondition(a.Unit, "shaken", turn.End + 1)) return $"a Shaken unit was still Shaken after its idle activation at seq {a.Seq}";
            }
        return idled > 0 ? null : "no Shaken unit activated";
    });

    // ---- core-rough-ground -----------------------------------------------------------------------------------------------------------

    // 48" by 36": a difficult bog across the table in front of Blue's zone (y 13–17) and a dangerous minefield in front of Red's (y 19–23).
    const double BogSouth = 13, BogNorth = 17, MinesSouth = 19, MinesNorth = 23;

    /// <summary>p.11 difficult and dangerous terrain: each army must cross a band of it to reach the other.</summary>
    public static TestGame RoughGround()
    {
        const string name = "core-rough-ground";
        var scenario = new ScenarioBuilder(name, seed: 1)
            .RoundCap(4).Table(48, 36)
            .Army("Red", 3, 3, weapons: [Rifle(), HandWeapon()])
            .Army("Blue", 3, 3, weapons: [Rifle(), HandWeapon()])
            .Terrain("bog", new Point3(24, (BogSouth + BogNorth) / 2, 0), volumes:
                [new Volume(VolumeKind.Area, Footprint.Rect(48, BogNorth - BogSouth), new Point2(0, 0), new Yaw(0), 0, 1, TerrainDeclarations.Difficult, null)])
            .Terrain("minefield", new Point3(24, (MinesSouth + MinesNorth) / 2, 0), volumes:
                [new Volume(VolumeKind.Area, Footprint.Rect(48, MinesNorth - MinesSouth), new Point2(0, 0), new Yaw(0), 0, 1, TerrainDeclarations.Dangerous, null)])
            .Seeking(NearestEnemy)
            .Build();
        // A move whose start and end lie on opposite sides of a band, or end in it, went in or through it.
        static bool Crossed(Point2 from, Point2 to, double south, double north) =>
            (to.Y >= south && to.Y <= north) || (from.Y < south && to.Y > north) || (from.Y > north && to.Y < south) || (from.Y >= south && from.Y <= north);
        return Game(name, scenario,
        [
            Check("p.11: \"If any model in a unit moves in or through difficult terrain at any point of its move, then all models in the unit may not move more than 6\" for that movement\"", log =>
            {
                var capped = 0;
                foreach (var f in log.Frames.Where(f => f.Sequence is "rush" or "charge"))
                    foreach (var s in f.Steps.Where(s => s.Step.Step == "move"))
                    {
                        var moves = Moved(s.Step).Where(m => m is { From: OnTable, To: OnTable }).Select(m => (From: ((OnTable)m.From).Placement.Position.Horizontal, To: ((OnTable)m.To).Placement.Position.Horizontal)).ToList();
                        if (!moves.Any(m => Crossed(m.From, m.To, BogSouth, BogNorth))) continue;
                        capped++;
                        if (moves.FirstOrDefault(m => m.From.DistanceTo(m.To) > 6 + 1e-6) is { From: var from, To: var to } && from != to)
                            return $"a {f.Sequence} into the bog moved a model {from.DistanceTo(to):0.00}\" (seq {s.Seq})";
                    }
                return capped > 0 ? null : "no Rush or Charge went into the bog";
            }),
            Check("p.11: \"If a model moves in or through dangerous terrain ... it must take a dangerous terrain test\"; \"if the result is 1, then the unit takes one automatic wound\"", log =>
            {
                foreach (var f in log.Frames.Where(f => f.Sequence is "advance" or "rush" or "charge"))
                    foreach (var s in f.Steps.Where(s => s.Step.Step == "move"))
                        if (Moved(s.Step).Any(m => m is { From: OnTable from, To: OnTable to } && Crossed(from.Placement.Position.Horizontal, to.Placement.Position.Horizontal, MinesSouth, MinesNorth))
                            && !log.Children(f).Any(c => c.Sequence == "dangerous-terrain" && c.StartSeq > s.Seq))
                            return $"a {f.Sequence} into the minefield at seq {s.Seq} took no test";
                var tests = log.Frames.Where(f => f.Sequence == "dangerous-terrain").ToList();
                foreach (var t in tests)
                {
                    var ones = t.Steps.SelectMany(s => s.Step.Rolls).Where(r => r.Roll == "dangerous-terrain").Sum(r => log.Dice(r).Count(d => d.Raw == 1));
                    var removed = log.CasualtiesUnder(t);
                    if (removed != Math.Min(ones, log.LivingModels(t.Actor!.Value, t.StartSeq))) return $"a dangerous terrain test with {ones} one(s) removed {removed}";
                }
                var rolledOne = tests.Select(t => t.Steps.SelectMany(s => s.Step.Rolls).Any(r => r.Roll == "dangerous-terrain" && log.Dice(r).Any(d => d.Raw == 1))).ToList();
                return rolledOne.Contains(true) && rolledOne.Contains(false) ? null : "the logged dangerous terrain tests did not both roll a 1 and not";
            }),
        ]);
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
        var scenario = new ScenarioBuilder(name, seed: 1)
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
        ]);
    }

    /// <summary>Every line between the two bases crosses the wall's footprint: they stand on opposite sides of it, both within its width.</summary>
    static bool BehindWall(Point2 a, Point2 b)
    {
        bool Within(Point2 p) => p.X >= WallWest + ModelRadius && p.X <= WallEast - ModelRadius;
        return Within(a) && Within(b) && ((a.Y >= WallNorth + ModelRadius && b.Y <= WallSouth - ModelRadius) || (b.Y >= WallNorth + ModelRadius && a.Y <= WallSouth - ModelRadius));
    }

    // ---- core-transport-run ----------------------------------------------------------------------------------------------------------

    /// <summary>Red's infantry heads for its own transport; everything else heads for the nearest enemy.</summary>
    static Point2? ToTheTransport(Guid modelId, PolicyView view)
    {
        var unit = view.UnitOf(view.ModelOf(modelId).UnitId);
        if (view.TeamOf(unit.Id) is not { } team || unit.Definition.Models.Any(IsTransport) || !unit.Definition.Name.StartsWith("Red "))
            return NearestEnemy(modelId, view);
        return view.Projection.Models.Values.Where(m => IsTransport(m.Slot) && view.TeamOf(m.UnitId) == team)
            .Select(m => view.PlacedOf(m.Id)?.Placement.Position.Horizontal).FirstOrDefault(p => p is not null);
    }

    static bool IsTransport(ModelSlot slot) => slot.Properties.Any(p => p.Name == "Transport");

    /// <summary>p.15 Transport(X): Red's two single-model infantry units ride a Tough(3) transport that Blue's squads shoot at.</summary>
    public static TestGame TransportRun()
    {
        const string name = "core-transport-run";
        var scenario = new ScenarioBuilder(name, seed: 1)
            .RoundCap(4).Table(24, 36)
            .Army("Red", 3, 1, modelRules: [new ScenarioRule(1, "Transport", 6), new ScenarioRule(1, "Tough", 3)], weapons: [Rifle(), HandWeapon()],
                unitStats: new Dictionary<string, string> { ["Quality"] = "4+", ["Defense"] = "5+" })
            .Army("Blue", 3, 3, weapons: [Rifle(2), HandWeapon()])
            .Seeking(ToTheTransport)
            .Build();
        return Game(name, scenario,
        [
            Check("p.15: units \"must stay fully within 6\" of it when exiting\"", log =>
                Require(log.Exits().ToList(), "no unit disembarked", exit =>
                    exit.Within ? null : $"a model left the transport at seq {exit.Seq} and stood outside 6\" of it"), "Transport"),
            Check("p.15: \"Units inside/outside transports may not target other units outside/inside transports, but may target the transport itself\"", log =>
            {
                foreach (var attack in log.Frames.Where(f => f.Sequence == "attack-sequence" && f.Actor is not null))
                {
                    var at = log.LocationsBefore(attack.StartSeq);
                    bool Inside(Guid unit) => at.Any(kv => log.UnitOf(kv.Key) == unit && kv.Value is Inside);
                    var target = attack.Targets[0];
                    var isTransport = log.Run.Final.Units[target].Definition.Models.Any(IsTransport);
                    if (Inside(attack.Actor!.Value) != Inside(target) && !isTransport) return $"an attack at seq {attack.StartSeq} crossed a transport's hull";
                }
                return null;
            }, "Transport"),
            Check("p.15: \"units can't both embark/disembark as part of the same activation\"", log =>
            {
                foreach (var turn in log.Turns)
                {
                    var moves = log.Frames.Where(f => f.StartSeq > turn.Start && f.StartSeq < turn.End).SelectMany(f => f.Steps.Where(s => s.Step.Step == "move")).SelectMany(s => Moved(s.Step)).ToList();
                    var embarked = moves.Where(m => m is { From: OnTable, To: Inside }).Select(m => log.UnitOf(m.ModelId)).ToHashSet();
                    if (moves.FirstOrDefault(m => m is { From: Inside, To: OnTable } && embarked.Contains(log.UnitOf(m.ModelId))) is { } both) return $"a unit embarked and disembarked in the turn at seq {turn.Start}";
                }
                return log.Exits().Any() && log.Moves.Any(m => m.Move is { From: OnTable, To: Inside }) ? null : "no unit both embarked and, later, disembarked";
            }, "Transport"),
            Check("p.15: \"When a transport is destroyed, units inside must take a dangerous terrain test, are Shaken, and must be placed fully within 6\" of the transport before it's removed\"", log =>
                Require(log.Frames.Where(f => f.Sequence == "transport-destroyed").ToList(), "no transport was destroyed with anybody inside", wreck =>
                {
                    var unit = wreck.Actor!.Value;
                    if (!log.Children(wreck).Any(c => c.Sequence == "dangerous-terrain")) return "the occupants took no dangerous terrain test";
                    var place = wreck.Steps.FirstOrDefault(s => s.Step.Step == "place");
                    if (place is null) return "the occupants were never placed";
                    if (log.LivingModels(unit, place.Seq) > 0 && !place.Step.Changes.OfType<ConditionSet>().Any(c => c is { Condition: "shaken", Present: true } && c.Subject.Id == unit)) return "the occupants were not Shaken";
                    if (log.Exits().Any(e => e.Seq == place.Seq && !e.Within)) return "an occupant was placed outside 6\" of the wreck";
                    var removed = log.Moves.FirstOrDefault(m => m.Seq >= place.Seq && IsTransport(log.Run.Final.Models[m.Move.ModelId].Slot) && m.Move.To is OnShelf);
                    return removed.Move is null ? "the wreck was never removed" : null;
                }), "Transport"),
        ], covers: ["Transport"]);
    }

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
            Check("p.8: \"all weapons are fired simultaneously\" (wounds past the last model are lost and the shooting ends)", log =>
                Require(log.Frames.Where(f => f.Sequence == "shooting" && log.Children(f).Count(c => c.Sequence == "attack-sequence") == 2).ToList(), "no two-group shooting",
                    f => log.Events.Any(e => e.Payload is SequenceFinished x && x.FrameId == f.Id) ? null : $"shooting {f.Id} never finished")),
        ], dice: [new DiceFix("hit", 0, [6]), new DiceFix("defense", 0, [1]), new DiceFix("hit", 1, [6]), new DiceFix("defense", 1, [1])]);
    }

    // ---- core-hold-the-markers -------------------------------------------------------------------------------------------------------

    /// <summary>p.6 the mission: three markers across the middle, every unit heading for the nearest one its team does not hold.</summary>
    public static TestGame HoldTheMarkers()
    {
        const string name = "core-hold-the-markers";
        var scenario = new ScenarioBuilder(name, seed: 1)
            .RoundCap(5).Table(48, 36)
            .Army("Red", 3, 3, weapons: [Rifle(), HandWeapon()])
            .Army("Blue", 3, 3, weapons: [Rifle(), HandWeapon()])
            .Marker(MarkerKind.Objective, new Point3(12, 18, 0), label: "West")
            .Marker(MarkerKind.Objective, new Point3(24, 18, 0), label: "Centre")
            .Marker(MarkerKind.Objective, new Point3(36, 18, 0), label: "East")
            .Seeking(ObjectiveGoal.Nearest)
            .Expect(new Expectation.MostMarkersWin())
            .Build();
        return Game(name, scenario,
        [
            Check("p.6: \"if a unit is within 3\" of a marker whilst no enemies are, then it counts as being seized\"; \"if units from both sides contest a marker at the end of a round, then it becomes neutral\"; p.10: Shaken units \"can't seize or contest objectives\"", log =>
            {
                var (seized, contested) = (0, 0);
                var control = new Dictionary<Guid, Guid?>();
                foreach (var e in log.Events)
                {
                    if (e.Payload is not RoundEnded ended) continue;
                    var at = log.LocationsBefore(e.Seq);
                    foreach (var marker in log.Run.Final.Board.Table.Markers.Where(m => m.Kind == MarkerKind.Objective))
                    {
                        // Each team: certainly within 3" (a margin either side), certainly not, or too close to call. Shaken units count for no one.
                        var near = at.Where(kv => kv.Value is OnTable && log.TeamOf(log.UnitOf(kv.Key)) is not null && !log.HasCondition(log.UnitOf(kv.Key), "shaken", e.Seq))
                            .Select(kv => (Team: log.TeamOf(log.UnitOf(kv.Key))!.Value,
                                Edge: Distance.EdgeToMarker(new PlacedModel(log.Body(kv.Key), ((OnTable)kv.Value).Placement), marker.Position.Horizontal, marker.Diameter)))
                            .GroupBy(x => x.Team)
                            .ToDictionary(g => g.Key, g => g.Any(x => x.Edge <= 3 - 0.01) ? true : g.All(x => x.Edge > 3 + 0.01) ? false : (bool?)null);
                        var before = control.GetValueOrDefault(marker.Id);
                        var after = ended.Changes.OfType<ObjectiveControlSet>().LastOrDefault(c => c.MarkerId == marker.Id) is { } set ? set.TeamId : before;
                        control[marker.Id] = after;
                        if (near.Values.Any(v => v is null)) continue;
                        var present = near.Where(kv => kv.Value == true).Select(kv => kv.Key).ToList();
                        Guid? expected = present.Count switch { 0 => before, 1 => present[0], _ => null };
                        if (present.Count == 1) seized++;
                        if (present.Count > 1) contested++;
                        if (after != expected) return $"round {ended.Round} marker {marker.Label}: {present.Count} team(s) within 3\", control went to {(after is null ? "nobody" : "a team")} not {(expected is null ? "nobody" : "a team")}";
                    }
                }
                return seized > 0 && contested > 0 ? null : $"{seized} seizure(s) and {contested} contest(s) in the game; both are needed";
            }),
            FinalRound(Enforcement.Strict),
        ]);
    }

    // ---- core-deployment -------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// p.6 deployment: the roll-off is fixed so Blue (team 2) wins, deploys first and takes the first turn. Red's third unit is a Scout and
    /// Blue's third an Ambusher (p.13, p.14).
    /// </summary>
    public static TestGame Deployment()
    {
        const string name = "core-deployment";
        var scenario = new ScenarioBuilder(name, seed: 1)
            .RoundCap(3).Table(48, 36)
            .Army("Red", 3, 3, unitRules: [new ScenarioRule(3, "Scout")], weapons: [Rifle(), HandWeapon()])
            .Army("Blue", 3, 3, unitRules: [new ScenarioRule(3, "Ambush")], weapons: [Rifle(), HandWeapon()])
            .Build();
        string Army(CoreLog log, Guid unit) => log.Run.Final.Units[unit].Definition.Name.Split(' ')[0];
        return Game(name, scenario,
        [
            Check("p.4: \"The player with the highest result wins the roll-off\"; p.6: \"the winner must start deploying their army first\"; p.7: the deployment roll-off winner \"takes the first turn\" (roll-off fixed to 2 for Red, 5 for Blue)", log =>
            {
                if (log.Edited("roll-off", 0) is not [{ Raw: 2 }, { Raw: 5 }, ..]) return "the roll-off was not fixed";
                var deployed = log.Deployments.ToList();
                if (deployed.Count == 0 || Army(log, deployed[0].Unit) != "Blue") return "Blue did not deploy first";
                var blue = log.TeamOf(deployed[0].Unit);
                return log.Rounds.FirstOrDefault(r => r.Round == 1)?.Order[0] == blue ? null : "Blue did not take the first turn";
            }),
            Check("p.6: \"Then the players continue alternating in placing one unit each, until all units have been deployed\"", log =>
            {
                var order = log.Deployments.Where(d => d.Question == AskQuestions.Deploy).Select(d => Army(log, d.Unit)).ToList();
                for (var i = 1; i < order.Count; i++)
                    if (order[i] == order[i - 1] && order.Skip(i).Any(a => a != order[i])) return $"deployment order {string.Join(", ", order)} did not alternate";
                return order.Count >= 4 ? null : "too few deployments";
            }),
            Check("p.6: each unit is placed \"fully within 12\" of their table edge\" (Red y 24–36, Blue y 0–12)", log =>
                Require(log.Deployments.Where(d => d.Question == AskQuestions.Deploy).ToList(), "nobody deployed", d =>
                {
                    var (low, high) = Army(log, d.Unit) == "Red" ? (24.0, 36.0) : (0.0, 12.0);
                    return log.FirstPlacement(d.Unit).All(p => p.Y >= low + ModelRadius - 1e-6 && p.Y <= high - ModelRadius + 1e-6) ? null : $"{log.Run.Final.Units[d.Unit].Definition.Name} was placed outside its zone";
                })),
            Check("p.14 Scout: \"After all other units are deployed, may be deployed anywhere fully within 12\" of their deployment zone\" (Red y 12–36)", log =>
                Require(log.Deployments.Where(d => d.Question == AskQuestions.DeployScouts).ToList(), "no Scout was deployed", d =>
                    log.Deployments.Any(o => o.Question == AskQuestions.Deploy && o.Seq > d.Seq) ? "a Scout deployed before the other units"
                    : log.FirstPlacement(d.Unit).All(p => p.Y >= 12 + ModelRadius - 1e-6) ? null : "a Scout stood outside 12\" of its zone"), "Scout"),
            Check("p.13 Ambush: \"At the start of any round after the first, may be deployed anywhere over 9\" away from enemy units\"", log =>
                Require(log.Deployments.Where(d => d.Question == AskQuestions.DeployAmbusher).ToList(), "no Ambusher was deployed", d =>
                {
                    var round = log.Rounds.LastOrDefault(r => log.Events.Any(e => e.Payload == r && e.Seq < d.Seq))?.Round ?? 0;
                    if (round < 2) return $"an Ambusher deployed in round {round}";
                    var enemies = log.EnemiesOf(d.Unit).SelectMany(u => log.Placed(u, d.Seq)).ToList();
                    return log.FirstPlacement(d.Unit).All(p => enemies.All(q => p.DistanceTo(q) - 2 * ModelRadius > 9)) ? null : "an Ambusher deployed within 9\" of an enemy";
                }), "Ambush"),
        ], covers: ["Scout", "Ambush"], dice: [new DiceFix("roll-off", 0, [2, 5])],
        evidence: new Dictionary<string, Func<IReadOnlyList<GameEvent>, bool>>
        {
            ["Scout"] = log => Answered(log, AskQuestions.DeployScouts),
            ["Ambush"] = log => Answered(log, AskQuestions.DeployAmbusher),
        });
    }

    /// <summary>A unit was picked at an Ask of that question: the scheduler's evidence for Scout and Ambush, which no step records.</summary>
    static bool Answered(IReadOnlyList<GameEvent> log, string question)
    {
        var asks = log.Select(e => e.Payload).OfType<AskOpened>().Where(o => o.Ask.Question == question).Select(o => o.Ask.Id).ToHashSet();
        return log.Select(e => e.Payload).OfType<AskClosed>().Any(c => asks.Contains(c.AskId) && c.Answers.Any(a => a.Answer is SelectedPieces { PieceIds.Count: > 0 }));
    }

    // ---- helpers -----------------------------------------------------------------------------------------------------------------------

    static IEnumerable<ModelRelocation> Moved(StepCompleted step) => step.Changes.OfType<ModelsMoved>().SelectMany(m => m.Moves);

    static string Faces(IReadOnlyList<Die> dice) => $"[{string.Join(",", dice.Select(d => $"{d.Raw}{(d.Success ? "+" : "-")}"))}]";

    /// <summary>Every item passes, and there is at least one: a check with nothing to look at fails rather than passing vacuously.</summary>
    static string? Require<T>(IReadOnlyList<T> items, string none, Func<T, string?> failure) =>
        items.Count == 0 ? none : items.Select(failure).FirstOrDefault(f => f is not null);
}

/// <summary>A sequence frame of the effective log: its context, the frame it was started under, and its steps with the Asks each answered.</summary>
file sealed record CoreFrame(Guid Id, string Sequence, Guid? Actor, IReadOnlyList<Guid> Targets, string? Action, Guid? Parent, long StartSeq, int Round, List<CoreStep> Steps);

/// <summary>One step_completed and the Asks its frame opened since the previous step.</summary>
file sealed record CoreStep(StepCompleted Step, long Seq, IReadOnlyList<Ask> Asks);

/// <summary>An activation: the unit, its team, the round, and the seq of the action's record.</summary>
file sealed record CoreActivation(Guid Unit, Guid Team, int Round, long Seq);

/// <summary>
/// The effective log read the way the core checks need it: frames with their parents, every roll with its dice as edited or rerolled, the
/// activations, and model locations at any seq. A reading of the log, not of the rules.
/// </summary>
file sealed class CoreLog
{
    readonly Dictionary<Guid, CoreFrame> _frames = [];
    readonly Dictionary<Guid, Die[]> _dice = [];
    readonly List<(long Seq, ModelRelocation Move)> _moves = [];
    readonly List<(long Seq, Guid Unit, string Condition, bool Present)> _conditions = [];

    public SuiteGameRun Run { get; }
    public IReadOnlyList<GameEvent> Events { get; }
    public IReadOnlyList<CoreFrame> Frames { get; }
    public IReadOnlyList<CoreActivation> Activations { get; }
    public IReadOnlyList<RoundStarted> Rounds { get; }
    /// <summary>Each turn's turn_started and turn_ended seqs.</summary>
    public IReadOnlyList<(long Start, long End)> Turns { get; }

    public CoreLog(SuiteGameRun run)
    {
        Run = run;
        Events = EventApplier.EffectiveLog(run.EffectiveLog);
        var frames = new List<CoreFrame>();
        var stack = new List<Guid>();
        var pending = new Dictionary<Guid, List<Ask>>();
        var activations = new List<CoreActivation>();
        var round = 0;
        Guid? turn = null;
        foreach (var e in Events)
        {
            switch (e.Payload)
            {
                case RoundStarted r: round = r.Round; break;
                case TurnStarted t: turn = t.TeamId; break;
                case TurnEnded: turn = null; break;
                case SequenceStarted s:
                    var frame = new CoreFrame(s.FrameId, s.Sequence, s.Context.ActorUnitId, s.Context.TargetUnitIds, s.Context.Action, stack.Count > 0 ? stack[^1] : null, e.Seq, round, []);
                    frames.Add(frame); _frames[s.FrameId] = frame; stack.Add(s.FrameId);
                    break;
                case SequenceFinished f: stack.Remove(f.FrameId); break;
                case SequenceAbandoned a: if (stack.IndexOf(a.FrameId) is >= 0 and var i) stack.RemoveRange(i, stack.Count - i); break;
                case AskOpened { Ask.FrameId: { } owner } o: (pending.TryGetValue(owner, out var list) ? list : pending[owner] = []).Add(o.Ask); break;
                case StepCompleted c when _frames.TryGetValue(c.FrameId, out var owner):
                    owner.Steps.Add(new CoreStep(c, e.Seq, pending.Remove(c.FrameId, out var asks) ? asks : []));
                    foreach (var roll in c.Rolls) _dice[roll.Id] = [.. roll.Dice];
                    break;
                case DiceEdited d: Replace(d.RollId, d.Replaced); break;
                case DiceRerolled d: Replace(d.RollId, d.Replaced); break;
            }
            foreach (var change in ChangesOf(e.Payload))
            {
                if (change is ModelsMoved mm) _moves.AddRange(mm.Moves.Select(m => (e.Seq, m)));
                if (change is ConditionSet { Subject.Kind: SubjectKind.Unit } cs) _conditions.Add((e.Seq, cs.Subject.Id, cs.Condition, cs.Present));
                if (change is ActionRecorded a && turn is { } team) activations.Add(new CoreActivation(a.UnitId, team, round, e.Seq));
            }
        }
        Frames = frames;
        Activations = activations;
        var questions = Events.Select(e => e.Payload).OfType<AskOpened>().ToDictionary(o => o.Ask.Id, o => o.Ask.Question);
        Deployments = [.. Events.Where(e => e.Payload is AskClosed).SelectMany(e => ((AskClosed)e.Payload).Answers
            .Where(a => questions.GetValueOrDefault(((AskClosed)e.Payload).AskId) is AskQuestions.Deploy or AskQuestions.DeployScouts or AskQuestions.DeployAmbusher)
            .SelectMany(a => a.Answer is SelectedPieces p ? p.PieceIds.Select(u => (questions[((AskClosed)e.Payload).AskId], u, a.TeamId, e.Seq)) : []))];
        Rounds = Events.Select(e => e.Payload).OfType<RoundStarted>().ToList();
        Turns = Events.Where(e => e.Payload is TurnStarted)
            .Select(s => (s.Seq, Events.FirstOrDefault(e => e.Seq > s.Seq && e.Payload is TurnEnded)?.Seq ?? long.MaxValue - 1)).ToList();
    }

    void Replace(Guid roll, IReadOnlyList<DieReplacement> replaced)
    {
        if (!_dice.TryGetValue(roll, out var dice)) return;
        foreach (var r in replaced) dice[r.Index] = r.New;
    }

    static IEnumerable<StateChange> ChangesOf(EventPayload p) => p switch
    {
        StateChange c => [c],
        StepCompleted s => s.Changes,
        RoundStarted r => r.Changes,
        RoundEnded r => r.Changes,
        TurnEnded t => t.Changes,
        _ => [],
    };

    public CoreFrame? Frame(Guid? id) => id is { } i && _frames.TryGetValue(i, out var f) ? f : null;
    public IEnumerable<CoreFrame> Children(CoreFrame parent) => Frames.Where(f => f.Parent == parent.Id);
    public IEnumerable<CoreFrame> Descendants(CoreFrame parent) => Children(parent).SelectMany(c => (IEnumerable<CoreFrame>)[c, .. Descendants(c)]);

    /// <summary>A roll's dice as they stand: edits and rerolls applied.</summary>
    public IReadOnlyList<Die> Dice(RollRecord roll) => _dice.TryGetValue(roll.Id, out var d) ? d : roll.Dice;

    /// <summary>Every roll of that kind in log order, with the frame that made it.</summary>
    public IReadOnlyList<(RollRecord Roll, CoreFrame Frame)> Rolls(string kind) =>
        Frames.SelectMany(f => f.Steps.SelectMany(s => s.Step.Rolls.Where(r => r.Roll == kind).Select(r => (s.Seq, Roll: r, Frame: f))))
              .OrderBy(x => x.Seq).Select(x => (x.Roll, x.Frame)).ToList();

    /// <summary>The ordinal-th roll of that kind, when a dice fix edited it.</summary>
    public IReadOnlyList<Die>? Edited(string kind, int ordinal) =>
        Rolls(kind).ElementAtOrDefault(ordinal) is { Roll: { } roll } && Events.Any(e => e.Payload is DiceEdited d && d.RollId == roll.Id) ? Dice(roll) : null;

    public Guid UnitOf(Guid model) => Run.Final.Models[model].UnitId;

    /// <summary>Where every model stood before the event at that seq.</summary>
    public IReadOnlyDictionary<Guid, Location> LocationsBefore(long seq)
    {
        var at = Run.Final.Models.Keys.ToDictionary(m => m, _ => (Location)new OnShelf());
        foreach (var (s, move) in _moves) { if (s >= seq) break; at[move.ModelId] = move.To; }
        return at;
    }

    /// <summary>The unit's models on the table or inside a container before that seq.</summary>
    public int LivingModels(Guid unit, long seq) =>
        LocationsBefore(seq).Count(kv => UnitOf(kv.Key) == unit && kv.Value is not OnShelf);

    public bool UnitDestroyedBefore(Guid unit, long seq) => LivingModels(unit, seq) == 0;

    /// <summary>The unit's models' table positions before that seq.</summary>
    public IReadOnlyList<Point2> Placed(Guid unit, long seq) =>
        [.. LocationsBefore(seq).Where(kv => UnitOf(kv.Key) == unit && kv.Value is OnTable).Select(kv => ((OnTable)kv.Value).Placement.Position.Horizontal)];

    /// <summary>The team that deployed the unit.</summary>
    public Guid? TeamOf(Guid unit) => Deployments.FirstOrDefault(d => d.Unit == unit) is { Question: not null } d ? d.Team : null;

    /// <summary>Every unit picked at a deploy, deploy_scouts or deploy_ambusher Ask: the question, the unit, its team and the seq of the answer.</summary>
    public IReadOnlyList<(string Question, Guid Unit, Guid Team, long Seq)> Deployments { get; private set; } = [];

    /// <summary>Where the unit's models were first put on the table.</summary>
    public IReadOnlyList<Point2> FirstPlacement(Guid unit) =>
        [.. _moves.Where(m => UnitOf(m.Move.ModelId) == unit && m.Move is { From: OnShelf, To: OnTable }).GroupBy(m => m.Move.ModelId)
            .Select(g => ((OnTable)g.First().Move.To).Placement.Position.Horizontal)];

    /// <summary>Every model relocation, in log order.</summary>
    public IReadOnlyList<(long Seq, ModelRelocation Move)> Moves => _moves;

    /// <summary>
    /// Every model that left a container for the table: the seq, and whether its base stood fully within 6" of the container where the container
    /// stood (p.15), measured by Tabletop.Geometry.
    /// </summary>
    public IEnumerable<(long Seq, bool Within)> Exits()
    {
        foreach (var (seq, move) in _moves)
        {
            if (move is not { From: Inside from, To: OnTable to }) continue;
            if (LocationsBefore(seq)[from.ContainerId] is not OnTable container) { yield return (seq, false); continue; }
            yield return (seq, Containment.BaseFullyWithin(new PlacedModel(Body(move.ModelId), to.Placement), new PlacedModel(Body(from.ContainerId), container.Placement), 6));
        }
    }

    public ModelBody Body(Guid model) => new(Run.Final.Models[model].Slot.Base, Run.Final.Models[model].Slot.Height);

    /// <summary>The units of every other participant.</summary>
    public IEnumerable<Guid> EnemiesOf(Guid unit) =>
        Run.Final.Units.Values.Where(u => u.ParticipantId != Run.Final.Units[unit].ParticipantId).Select(u => u.Id);

    /// <summary>Whether the unit carried the condition before that seq.</summary>
    public bool HasCondition(Guid unit, string condition, long seq) =>
        _conditions.LastOrDefault(c => c.Seq < seq && c.Unit == unit && c.Condition == condition) is { Present: true };

    /// <summary>Every condition set on a unit, in log order.</summary>
    public IReadOnlyList<(long Seq, Guid Unit, string Condition, bool Present)> Conditions => _conditions;

    /// <summary>p.10's "starting size": the models the unit's definition holds.</summary>
    public int StartingSize(Guid unit) => Run.Final.Units[unit].Definition.Models.Count;

    /// <summary>The attack sequences a unit made in melee (under a melee frame), in log order.</summary>
    public IEnumerable<CoreFrame> MeleeAttacks => Frames.Where(f => f.Sequence == "attack-sequence" && Frame(f.Parent)?.Sequence == "melee");

    /// <summary>The morale tests, each with its logged roll (null when none was rolled) and the step that settled it.</summary>
    public IEnumerable<(CoreFrame Frame, CoreStep Test, RollRecord? Roll)> MoraleTests =>
        Frames.Where(f => f.Sequence == "morale-test").SelectMany(f => f.Steps.Where(s => s.Step.Step == "test").Select(s => (f, s, s.Step.Rolls.FirstOrDefault(r => r.Roll == "morale"))));

    /// <summary>Models of the frame's target sent to the shelf by this deal-wounds frame's own steps.</summary>
    public int CasualtiesIn(CoreFrame deal) => deal.Steps.SelectMany(s => s.Step.Changes.OfType<ModelsMoved>()).SelectMany(m => m.Moves).Count(m => m.To is OnShelf);

    /// <summary>Casualties under a frame: its own steps and every frame started beneath it.</summary>
    public int CasualtiesUnder(CoreFrame frame) => CasualtiesIn(frame) + Descendants(frame).Sum(CasualtiesIn);
}
