using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>Core games: melee (p.9–10) and morale (p.10).</summary>
internal static partial class CoreGames
{
    // ---- core-charge-and-strike-back -------------------------------------------------------------------------------------------------

    /// <summary>
    /// p.9–10 melee: armies with only hand weapons close in and charge. The first hit roll of the game (a charge's) is fixed to test the Quality threshold.
    /// Round 4's end is under warn, and both players continue at the game_over Ask. Red wins the roll-off (fixed); Red's units charge whenever they may and
    /// Blue's Hold, every defender strikes back. Red 1 and Red 2 deploy within charge range of Blue 1 and both charge it in round 1, so Blue 1 strikes back
    /// twice in the round. Red 1's charge (the game's first hit roll) scores one hit, which Blue 1 blocks; Blue 1's strike back hits and wounds, so Red 1
    /// lost the melee and tests morale. Red's Defense 2+ keeps Red 1 standing to test; Blue 1's five models outlast Red 2's charge, so it strikes back again.
    /// Red 3 stands 8" from Blue 2 (five models, so it outlasts the charge) and charges it in round 1 too; Blue 2 declines to strike back (p.9: "but
    /// doesn't have to"), Blue 1 always strikes.
    /// </summary>
    public static TestGame ChargeAndStrikeBack()
    {
        const string name = "core-charge-and-strike-back";
        var script = AnswerScript.Build()
            .Match(AskQuestions.GameOver, (ask, _) => AskPicks.Labeled(ask, "Continue"))
            .Match(AskQuestions.Activate, (ask, p) => Enumerable.Range(1, 4).Select(k => AskPicks.Activate(ask, $"Red unit {k}", "Charge")).FirstOrDefault(a => a is not null)
                ?? AskPicks.Activate(ask, "Red unit 4", p.Scheduler.Round == 1 ? "Advance" : "Rush")
                ?? AskPicks.Option(ask, c => c.Label.StartsWith("Blue ", StringComparison.Ordinal) && c.Label.EndsWith(": Hold", StringComparison.Ordinal)))
            .Match("charge_target", (ask, p) => p.Scheduler.Round != 1 ? null
                : AskPicks.Pieces(ask, p, ask.FrameId is { } f && p.Stack.FirstOrDefault(x => x.Id == f)?.Context.ActorUnitId is { } c && AskPicks.UnitNameOf(p, c) == "Red unit 3" ? "Blue unit 2" : "Blue unit 1"))
            .Match("strike_back", (ask, p) => ask.Legal is Options o && o.Choices.FirstOrDefault(c => c.Id == "strike") is { } strike
                ? new Chosen(strike.Pieces is [var defender] && AskPicks.UnitNameOf(p, defender) == "Blue unit 2" ? "pass" : "strike") : null)
            .Build();
        var scenario = new ScenarioBuilder(name, seed: 1)
            .Enforcing(Enforcement.Warn).RoundCap(5).Table(36, 30)
            .Army("Red", 4, 3, weapons: [HandWeapon(2)], unitStats: new Dictionary<string, string> { ["Quality"] = "4+", ["Defense"] = "2+" },
                anchors: new Dictionary<int, Point2> { [1] = new(6, 19), [2] = new(16, 19), [3] = new(25, 19), [4] = new(31, 29) })
            .Army("Blue", 4, 3, weapons: [HandWeapon(2)], sizes: new Dictionary<int, int> { [1] = 5, [2] = 5 },
                anchors: new Dictionary<int, Point2> { [1] = new(8, 11), [2] = new(26, 11), [3] = new(14, 1), [4] = new(30, 1) })
            .Seeking(NearestEnemy)
            .Scripted(script)
            .Build();
        return Game(name, scenario,
        [
            ScoresAgainstThreshold(),
            FixedRoll("p.9: \"take as many Quality tests as attacks. Each successful roll counts as a hit\"; p.4: \"Quality value or higher\" (melee hit #0 fixed to 4, 3 at Quality 4+: one defense die for those two)",
                "hit", 0, "4+,3-", (log, r) =>
                {
                    var rest = log.Dice(r.Roll).Skip(2).Count(d => d.Success);
                    return DefenseDiceAfter(log, r.Frame) == 1 + rest ? null : $"{DefenseDiceAfter(log, r.Frame)} defense dice for {1 + rest} hits";
                }),
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
        ], dice:
        [
            new DiceFix("roll-off", 0, [6, 1]), new DiceFix("hit", 0, [4, 3, 1, 1, 1, 1]), new DiceFix("defense", 0, [6]),
            new DiceFix("hit", 0, [6], new RollKey(Actor: "Blue unit 1")), new DiceFix("defense", 0, [1], new RollKey(Actor: "Blue unit 1")),
        ]);
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

    /// <summary>
    /// p.10 general morale, Shaken and idle activations: five-model squads shoot each other down. Round 4's end is under silent. Every unit Holds and
    /// Red k and Blue k shoot each other. Red wins the roll-off (fixed); Red 1's first volley is fixed to three hits and three failed blocks, so Blue 1 is
    /// left at two of five and tests, fixed to fail; Blue 2's first volley misses with every die, so Red 2 shoots whole, and its fixed volley leaves Blue 2
    /// at two of five, whose test is fixed to pass. Blue 1, Shaken, activates idle in round 2.
    /// </summary>
    public static TestGame MoraleBreak()
    {
        const string name = "core-morale-break";
        var scenario = new ScenarioBuilder(name, seed: 3)
            .Enforcing(Enforcement.Silent).RoundCap(5).Table(48, 36)
            .Army("Red", 2, 5, weapons: [Rifle(2)], anchors: new Dictionary<int, Point2> { [1] = new(8, 25), [2] = new(30, 25) })
            .Army("Blue", 2, 5, weapons: [Rifle(2)], anchors: new Dictionary<int, Point2> { [1] = new(8, 11), [2] = new(30, 11) })
            .Scripted(EveryoneHoldsAiming(new Dictionary<string, string> { ["Red unit 1"] = "Blue unit 1", ["Red unit 2"] = "Blue unit 2", ["Blue unit 1"] = "Red unit 1", ["Blue unit 2"] = "Red unit 2" }))
            .Build();
        int[] ThreeHits = [6, 6, 6, 1, 1, 1, 1, 1, 1, 1];
        IReadOnlyList<DiceFix> dice =
        [
            new("roll-off", 0, [6, 1]),
            new("hit", 0, ThreeHits, new RollKey(Actor: "Red unit 1")), new("defense", 0, [1, 1, 1], new RollKey(Actor: "Red unit 1")),
            new("hit", 0, [1, 1, 1, 1, 1, 1, 1, 1, 1, 1], new RollKey(Actor: "Blue unit 2")),
            new("hit", 0, ThreeHits, new RollKey(Actor: "Red unit 2")), new("defense", 0, [1, 1, 1], new RollKey(Actor: "Red unit 2")),
            new("morale", 0, [1], new RollKey(Action: "wounds")), new("morale", 1, [6], new RollKey(Action: "wounds")),
        ];
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
        ], dice: dice);
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
}
