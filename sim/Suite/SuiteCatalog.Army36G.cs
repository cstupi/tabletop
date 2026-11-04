using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Sim.Suite;

/// <summary>
/// The 3.6 games of the morale test's range and opponent (Hive Bond, Hold the Line), the rolled moves (Bounding, Dash, Rapid Blink, Wolfborn),
/// the strike-turn rolls (Ravage, Regenerative Strength) and the placement's clearance (Fanatic, Vanguard), one game per mechanism, grouped army:3.6:&lt;book uid&gt;.
/// </summary>
public static partial class SuiteCatalog
{
    private static partial class ArmyGames
    {
        public static IReadOnlyList<TestGame> Army36G { get; } =
        [
            ShotNearAndFar(Books36.AlienHives, "alien-hives-hive-bond", "Hive Bond"),
            ShotNearAndFar(Books36.HumanDefenseForce, "human-defense-force-hold-the-line", "Hold the Line"),
            Pick(Books36.DaemonsOfChange, "daemons-of-change-bounding", Play.Moves, "Bounding",
                [MovedWithinRoll("Bounding", 1, "\"When this unit is activated, you may move all models with this rule in it by up to D3+1\"\"")], grants: false).In36(),
            Pick(Books36.CustodianBrothers, "custodian-brothers-dash", Play.Moves, "Dash",
                [MovedWithinRoll("Dash", 1, "\"Once per round, at the end of this unit's activation, you may move all models with this rule in it by up to D3+1\"\"")], grants: false).In36(),
            Pick(Books36.ElvenJesters, "elven-jesters-rapid-blink", Play.Moves, "Rapid Blink",
                [MovedWithinRoll("Rapid Blink", 0, "\"When this unit is activated, you may move all models with this rule in it by up to D3\"\"")], grants: false).In36(),
            Pick(Books36.WolfBrothers, "wolf-brothers-wolfborn", Play.Moves, "Wolfborn",
                [MovedWithinRoll("Wolfborn", 0, "\"When this unit is activated, you may move all models with this rule in it by up to D3\"\"")], grants: false).In36(),
            Game(Books36.RatmenClans, "ratmen-clans-ravage", Play.Charges, ["Ravage"], [R(1, "Ravage", 2), R(2, "Ravage", 2), R(3, "Ravage", 2)], [],
                [WoundDice("Ravage", 2, "\"roll X dice before attacking ... For each 6+ the target takes one wound\" (X 2)"), OncePerRound("Ravage", "\"Once per round, when it’s this model’s turn to strike in melee\""),
                 BeforeAttacking("Ravage", "\"roll X dice before attacking\"")],
                units: 3).In36(),
            // One Tough(6) opponent model of one attack: its first attack hits, the block fails and the Regeneration die ignores the wound, so the unit it struck
            // holds a token when it charges again.
            Game(Books36.DarkElfRaiders, "dark-elf-raiders-regenerative-strength", Play.ShotWhileMoving, ["Regenerative Strength"],
                [R(1, "Regenerative Strength"), R(1, "Regeneration"), R(1, "Tough", 3), R(2, "Regenerative Strength"), R(2, "Regeneration"), R(2, "Tough", 3)],
                [new DiceFix("hit", 0, [6], new RollKey(Actor: "Opponents unit 1")), new DiceFix("defense", 0, [1], new RollKey(Actor: "Opponents unit 1")),
                 new DiceFix("Regeneration", 0, [6], new RollKey(Actor: "Opponents unit 1"))],
                [StrengthDice("\"roll as many dice as tokens on it before attacking ... For each 4+ the target takes one wound\""),
                 OncePerRound("Regenerative Strength", "\"Once per round, when it’s this model’s turn to strike in melee\"")],
                enemy: [R(1, "Tough", 6)], enemyUnits: 1, enemyModels: 1, enemyAttacks: 1, length: 26,
                // The opponent shoots unit 1, the unit the script charges with.
                more: s => { Activates(c => c.Label == $"{Books36.DarkElfRaiders.Army} unit 1: Charge")(s); s.Match("declare_targets", (ask, p) => AllAt(ask, p, $"{Books36.DarkElfRaiders.Army} unit 1")); }).In36(),
            Batch3Game(Books36.SoulSnatcherCults, "soul-snatcher-cults-fanatic", Play.Moves, "Fanatic", [R(1, "Fanatic")], PlacedClear("Fanatic", Books36.SoulSnatcherCults.Army),
                units: 1, more: UsePicks(Books36.SoulSnatcherCults.Army, 1, "Fanatic")).In36(),
            Batch3Game(Books36.RatmenClans, "ratmen-clans-vanguard", Play.Moves, "Vanguard", [R(1, "Vanguard")], PlacedClear("Vanguard", Books36.RatmenClans.Army),
                units: 1, more: UsePicks(Books36.RatmenClans.Army, 1, "Vanguard")).In36(),
        ];

        /// <summary>Fanatic, Vanguard: every placement the rule's reposition frame makes is in deployment, within 9" (its Ask's allowance), and over 9" from
        /// every model of an enemy unit without Vanguard.</summary>
        static GameCheck PlacedClear(string entry, string army) =>
            Exact(entry, $"\"After this model is deployed, it may be placed anywhere fully within 9\" of its position, over 9\" away from non-Vanguard enemy units\"", (run, _) =>
            {
                var log = LogQueries.Of(run);
                var offered = log.Asks.Where(a => a.Ask.Question == "move" && a.Frame is { Sequence: "reposition" } f && f.Context.Action == entry)
                    .Select(a => a.Frame!.Round != 0 ? $"offered in round {a.Frame.Round}" : a.Ask.Legal is MoveRegion m && m.Allowance == 9 ? null : $"allowed {(a.Ask.Legal as MoveRegion)?.Allowance}\"");
                var placed = from s in log.Steps
                             where s.Frame.Sequence == "reposition" && s.Frame.Context.Action == entry
                             from mv in s.Step.Changes.OfType<ModelsMoved>().SelectMany(m => m.Moves)
                             where mv.To is OnTable
                             let gap = Gap(run, log, mv.ModelId, ((OnTable)mv.To).Placement.Position.Horizontal, s.Seq, u => !Named(run, army)(u) && !Holds(run, u, "Vanguard"))
                             select gap > 9 ? null : $"placed {gap:0.00}\" from a non-Vanguard enemy";
                return [.. offered, .. placed];
            });

        /// <summary>"before attacking": in each melee, the unit's rolls for the rule start before any of its attack-sequences there.</summary>
        static GameCheck BeforeAttacking(string entry, string quote) =>
            Exact(entry, quote, (run, _) =>
            {
                var log = LogQueries.Of(run);
                return from w in log.FramesOf("rule-wounds")
                       where Dealt(w.Start) is { } d && d.Rule == entry && w.Nearest("melee") is not null
                       let melee = w.Nearest("melee")!
                       let first = log.FramesOf("attack-sequence").Where(a => a.Context.ActorUnitId == w.Context.ActorUnitId && a.Nearest("melee")?.Id == melee.Id).Select(a => (long?)a.Seq).Min()
                       select first is { } f && f < w.Seq ? "rolled after the unit's attacks" : null;
            });

        /// <summary>"Once per round": at most one `rule-wounds` frame of the entry per unit and round.</summary>
        static GameCheck OncePerRound(string entry, string quote) =>
            Exact(entry, quote, (run, _) =>
                from g in Rounds(run).Where(e => e.Payload is SequenceStarted { Sequence: "rule-wounds" } s && Dealt(s) is { } d && d.Rule == entry)
                    .GroupBy(e => (((SequenceStarted)e.Payload).Context.ActorUnitId, e.Round))
                select g.Count() == 1 ? null : $"rolled {g.Count()} times in round {g.Key.Round}");

        /// <summary>Regenerative Strength's dice: as many as tokens on the unit's living models (each wound a model ignored), 4+ each a wound.</summary>
        static GameCheck StrengthDice(string quote) =>
            Exact("Regenerative Strength", quote, (run, log) =>
            {
                var ignored = new Dictionary<Guid, int>();
                var dead = new HashSet<Guid>();
                var verdicts = new List<string?>();
                foreach (var s in log.Steps)
                {
                    foreach (var mv in s.Step.Changes.OfType<ModelsMoved>().SelectMany(m => m.Moves)) if (mv.To is OnShelf) dead.Add(mv.ModelId); else dead.Remove(mv.ModelId);
                    if (s.Frame.Sequence == "rule-wounds" && s.Step.Step == "roll" && Dealt(s.Frame) is { Rule: "Regenerative Strength" } && s.Frame.Context.ActorUnitId is { } u)
                    {
                        var tokens = run.Final.Units[u].Definition.Models.Where(m => !dead.Contains(m.Id)).Sum(m => ignored.GetValueOrDefault(m.Id));
                        var roll = s.Rolls("Regenerative Strength").Single();
                        verdicts.Add(roll.Dice.Count != tokens ? $"{roll.Dice.Count} dice for {tokens} tokens" : roll.Threshold != 4 ? $"threshold {roll.Threshold}"
                            : s.Value("wounds") != roll.Dice.Count(d => d.Raw >= 4) ? $"{s.Value("wounds")} wounds from {Faces(roll.Dice)}" : null);
                    }
                    if (s.Step.Step == "assign")
                        foreach (var (k, v) in s.Step.Values ?? new Dictionary<string, int>())
                            if (k.StartsWith("ignored_", StringComparison.Ordinal) && Guid.TryParseExact(k["ignored_".Length..], "N", out var m)) ignored[m] = ignored.GetValueOrDefault(m) + v;
                }
                return verdicts;
            });

        /// <summary>A rolled move: every move Ask the rule's reposition frame opens is a move (not a placement) with the allowance its one die gives (D3, plus
        /// `plus` per die).</summary>
        static GameCheck MovedWithinRoll(string entry, int plus, string quote) =>
            Exact(entry, quote, (_, log) =>
                from a in log.Asks
                where a.Ask.Question == "move" && a.Frame is { Sequence: "reposition" } f && f.Context.Action == entry
                let roll = log.Steps.LastOrDefault(s => s.Frame.FrameId == a.Frame!.FrameId && s.Rolls(entry).Any())?.Rolls(entry).Single()
                let inches = roll?.Dice.Sum(d => (d.Raw + 1) / 2 + plus)
                select roll is null ? "no distance roll" : roll.Dice.Count != 1 ? $"{roll.Dice.Count} dice"
                    : a.Ask.Legal is not MoveRegion m ? "no move region" : m.PassesModels ? "a placement, not a move"
                    : m.Allowance == inches ? null : $"allowed {m.Allowance}\" after {Faces(roll.Dice)}");

        const string ShotFromOverNine = "\"they get +1 to Morale Test rolls when shot/charged from over 9\"/6\" away\"";

        /// <summary>Two units carrying the rule, each shot by its own one-model opponent whose 14" gun reaches only it (across the 2" between the zones of a
        /// 26" table): unit 1's from about 3", unit 2's from about 13". Each opponent's first volley hits twice and both blocks fail, so two of three models fall and the unit tests morale (p.10).</summary>
        static TestGame ShotNearAndFar((string Uid, string Army) book, string name, string entry)
        {
            static RollKey By(int unit) => new(Actor: $"Opponents unit {unit}");
            var game = Game(book, name, Play.Shot, [entry], [R(1, entry), R(2, entry)],
                [new DiceFix("hit", 0, [6, 6, 1], By(1)), new DiceFix("defense", 0, [1, 1], By(1)), new DiceFix("hit", 0, [6, 6, 1], By(2)), new DiceFix("defense", 0, [1, 1], By(2))],
                [MoraleByRange(entry, book.Army, ShotFromOverNine)],
                units: 2, enemyUnits: 2, enemyModels: 1, enemyAttacks: 3, enemyRange: 14, rounds: 2, length: 26,
                anchors: new Dictionary<int, Point2> { [1] = new(6, 15), [2] = new(30, 25) }, enemyAnchors: new Dictionary<int, Point2> { [1] = new(6, 11), [2] = new(30, 11) });
            return game.In36();
        }

        /// <summary>Every morale test of a unit carrying the rule after shooting has the rule's +1 exactly when the shot that caused it came from over 9" (base
        /// to base, where the shooter and the unit stood as the attack began); the game shows both a near and a far one.</summary>
        static GameCheck MoraleByRange(string entry, string army, string quote) =>
            Exact(entry, quote, (run, _) =>
            {
                var log = LogQueries.Of(run);
                var verdicts = new List<string?>();
                var seen = new HashSet<bool>();
                foreach (var test in log.FramesOf("morale-test").Where(f => f.Context.ActorUnitId is { } u && Holds(run, u, entry) && f.Context.Action == "wounds"))
                {
                    var unit = test.Context.ActorUnitId!.Value;
                    var shot = log.FramesOf("attack-sequence").LastOrDefault(f => f.Seq < test.Seq && f.Context.TargetUnitIds.Contains(unit));
                    var roll = test.Steps.SelectMany(s => s.Rolls("morale")).FirstOrDefault();
                    if (shot?.Context.ActorUnitId is not { } shooter || roll is null) continue;
                    var gap = log.LocationsBefore(shot.Seq).Where(kv => kv.Value is OnTable && run.Final.Models[kv.Key].UnitId == unit)
                        .Select(kv => Gap(run, log, kv.Key, ((OnTable)kv.Value).Placement.Position.Horizontal, shot.Seq, u => u == shooter)).DefaultIfEmpty(double.PositiveInfinity).Min();
                    var far = gap > 9;
                    seen.Add(far);
                    var bonus = roll.Roll.Modifiers.Where(m => m.Source == entry).Sum(m => m.Amount);
                    verdicts.Add(bonus == (far ? 1 : 0) ? null : $"{bonus:+0;-0;0} after a shot from {gap:0.00}\"");
                }
                if (!seen.Contains(false)) verdicts.Add("no test after a shot from 9\" or closer");
                if (!seen.Contains(true)) verdicts.Add("no test after a shot from over 9\"");
                return verdicts;
            });
    }
}
