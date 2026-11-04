using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>Batch 5 (army-book rules spec → Authored rules 5): hits and wounds rules deal in melee and on the move, and a once-per-game roll to ignore wounds.</summary>
    private static partial class ArmyGames
    {
        /// <summary>The rule and its number a rule-hits or rule-wounds frame carries ("&lt;rule&gt;|&lt;n&gt;").</summary>
        static (string Rule, int N)? Dealt(SequenceStarted frame) =>
            frame.Context.Action is { } k && k.LastIndexOf('|') is var cut and > 0 && int.TryParse(k[(cut + 1)..], out var n) ? (k[..cut], n) : null;

        /// <summary>
        /// Hits a wound sets off (Retaliate: "X hits per wound taken"; Deathstrike, Self-Destruct: "If this model is killed in melee, the attacking unit takes X
        /// hits"): under each deal-wounds frame, the entry's rule-hits frames total X for each wound (or each kill) its carriers took there, aimed at the
        /// frame's attacker, each landing the hits its number names. The wounds are read from every assign step of the frame.
        /// </summary>
        static GameCheck HitsOnWounds(string entry, int x, bool perKill, string quote) =>
            Exact(entry, quote, (run, log) =>
                from deal in log.Steps.Where(s => s.Frame.Sequence == "deal-wounds" && s.Step.Step == "retaliation").Select(s => s.Frame).DistinctBy(f => f.FrameId)
                // The frame's values as its retaliation step reads them: every assign step's, a later one overwriting a key (took_ is the frame's running
                // total per model; a frame whose wounds carry on to a further assign has several).
                let values = log.Steps.Where(s => s.Frame.FrameId == deal.FrameId && s.Step.Step == "assign").SelectMany(s => s.Step.Values ?? new Dictionary<string, int>())
                    .Aggregate(new Dictionary<string, int>(), (merged, kv) => { merged[kv.Key] = kv.Value; return merged; })
                let owed = values.Where(kv => kv.Key.StartsWith(perKill ? "killed_" : "took_", StringComparison.Ordinal)
                        && Guid.TryParseExact(kv.Key[(kv.Key.IndexOf('_') + 1)..], "N", out var m) && run.Final.Models[m].Slot.Properties.Any(p => p.Name == entry))
                    .Sum(kv => perKill ? x : kv.Value * x)
                let pushed = log.Steps.Where(s => s.Frame.Sequence == "rule-hits" && s.Step.Step == "hit" && s.Ancestors.FirstOrDefault()?.FrameId == deal.FrameId && Dealt(s.Frame) is { } d && d.Rule == entry).ToList()
                where owed > 0 || pushed.Count > 0
                select pushed.Sum(p => p.Value("hits")) != owed ? $"{pushed.Sum(p => p.Value("hits"))} hits for {owed} owed"
                    : pushed.Any(p => p.Frame.Context.TargetUnitIds.FirstOrDefault() != deal.Context.ActorUnitId) ? "hits at a unit that did not attack" : null);

        /// <summary>Self-Destruct's survivors: each melee's self-destruct step removes the surviving carriers and deals X hits per carrier to the other unit.</summary>
        static GameCheck SurvivorsDestruct(int x, string quote) =>
            Exact("Self-Destruct", quote, (run, log) =>
                from s in log.Steps.Where(s => s.Step.Step == "self-destruct" && s.Step.Changes.OfType<ModelsMoved>().Any())
                let killed = s.Step.Changes.OfType<ModelsMoved>().SelectMany(m => m.Moves).Select(m => m.ModelId).ToList()
                let melee = s.Frame
                let pushed = log.Steps.Where(p => p.Frame.Sequence == "rule-hits" && p.Step.Step == "hit" && p.Ancestors.FirstOrDefault()?.FrameId == melee.FrameId && Dealt(p.Frame) is { Rule: "Self-Destruct" }).ToList()
                select !killed.All(m => run.Final.Models[m].Slot.Properties.Any(p => p.Name == "Self-Destruct")) ? "removed a model without Self-Destruct"
                    : pushed.Sum(p => p.Value("hits")) != killed.Count * x ? $"{pushed.Sum(p => p.Value("hits"))} hits for {killed.Count} carriers" : null);

        /// <summary>Dice of wounds ("roll X dice. For each 6+ the target takes one wound"): each rule-wounds frame of the entry rolls X per carrier, judges 6+, and its
        /// deal-wounds assigns that many wounds.</summary>
        static GameCheck WoundDice(string entry, int x, string quote) =>
            Exact(entry, quote, (run, log) =>
                from s in log.Steps.Where(s => s.Frame.Sequence == "rule-wounds" && s.Step.Step == "roll" && Dealt(s.Frame) is { } d && d.Rule == entry)
                let roll = s.Rolls(entry).Single()
                let sixes = roll.Dice.Count(d => d.Raw >= 6)
                select roll.Dice.Count % x != 0 ? $"{roll.Dice.Count} dice for X {x}" : roll.Threshold != 6 ? $"threshold {roll.Threshold}"
                    : s.Value("wounds") != sixes ? $"{s.Value("wounds")} wounds from {Faces(roll.Dice)}" : null);

        /// <summary>"Once per game": the entry's dice are rolled in one deal-wounds frame per unit at most.</summary>
        static GameCheck OncePerGame(string entry, string quote) =>
            Exact(entry, quote, (run, log) =>
                from g in log.Steps.Where(s => s.Step.Step == "assign" && s.Rolls(entry).Any()).GroupBy(s => s.Frame.Context.TargetUnitIds.FirstOrDefault())
                let frames = g.Select(s => s.Frame.FrameId).Distinct().Count()
                select frames == 1 ? null : $"rolled in {frames} attacks on one unit");

        /// <summary>A crossing rule's use: every crossing_target Ask offers enemy units only, and each pick sets off the entry (its dice, or its weapon's attack).</summary>
        static GameCheck Crossing(string entry, string quote) =>
            Exact(entry, quote, (run, log) =>
                from a in log.Asks.Where(a => a.Ask.Question == "crossing_target")
                let mover = a.Frame?.Context.ActorUnitId
                select a.Ask.Legal is PieceSelection p && p.Eligible.All(u => run.Final.Units[u].ParticipantId != run.Final.Units[mover!.Value].ParticipantId) ? null : "offered a friendly unit");

        public static IReadOnlyList<TestGame> Batch5 =>
        [
            Game(Infected, "infected-colonies-deathstrike", Play.Charged, ["Deathstrike"], [R(1, "Deathstrike", 2), R(2, "Deathstrike", 2), R(3, "Deathstrike", 2)], [],
                [HitsOnWounds("Deathstrike", 2, perKill: true, "\"If this model is killed in melee, the attacking unit takes X hits\" (X 2)")], enemyUnits: 4),
            Game(BloodPrime, "blood-prime-brothers-retaliate", Play.Charged, ["Retaliate"], [R(1, "Retaliate", 1), R(1, "Tough", 3), R(2, "Retaliate", 1), R(2, "Tough", 3), R(3, "Retaliate", 1), R(3, "Tough", 3)], [],
                [HitsOnWounds("Retaliate", 1, perKill: false, "\"When this model takes wounds in melee, the attacker takes X hits per wound taken\" (X 1)")], enemyUnits: 4),
            // One round. Unit 1 activates first (the script) 4.5" from opponent unit 1 and charges it; the charge misses with every die, so the charged unit
            // strikes back whole with one hit, whose block fails: one Self-Destruct model is killed in melee and the other two survive it. Units 2 and 3 stand
            // beyond charge range of every opponent, so no other melee destroys the unit the survivors' hits are due to.
            Game(Ratmen, "ratmen-clans-self-destruct", Play.Charges, ["Self-Destruct"], [R(1, "Self-Destruct", 2), R(2, "Self-Destruct", 2), R(3, "Self-Destruct", 2)],
                [new DiceFix("hit", 0, [1, 1, 1], new RollKey(Actor: "Ratmen Clans unit 1")), new DiceFix("hit", 0, [6, 1, 1], new RollKey(Actor: "Opponents unit 1")),
                 new DiceFix("defense", 0, [1], new RollKey(Actor: "Opponents unit 1"))],
                [SurvivorsDestruct(2, "\"If this model survives melee, after both sides have finished attacking, it is immediately killed, and the enemy unit takes X hits\" (X 2)"),
                 HitsOnWounds("Self-Destruct", 2, perKill: true, "\"If this model is killed in melee, the attacking unit takes X hits\" (X 2)")], units: 3, rounds: 1,
                anchors: new Dictionary<int, Tabletop.Geometry.Point2> { [1] = new(8, 15), [2] = new(20, 25), [3] = new(30, 25) },
                enemyAnchors: new Dictionary<int, Tabletop.Geometry.Point2> { [1] = new(8, 10.5), [2] = new(30, 1) }),
            Game(Orcs, "orc-marauders-ravage", Play.Charges, ["Ravage"], [R(1, "Ravage", 2), R(2, "Ravage", 2), R(3, "Ravage", 2)], [],
                [WoundDice("Ravage", 2, "\"When it's this model's turn to attack in melee, roll X dice. For each 6+ the target takes one wound\" (X 2)")], units: 3),
            Game(Robots, "robot-legions-protection-feat", Play.Shot, ["Protection Feat"], [R(1, "Protection Feat"), R(1, "Tough", 6)], [],
                [IgnoreWounds("Protection Feat", 5, "\"roll one die per wound, and on a 5+ it is ignored\""), OncePerGame("Protection Feat", "\"Once per game, when this unit takes wounds\"")],
                enemyUnits: 2, enemyAttacks: 4, units: 1),
            // Crossing by construction (Through351): Tough(6) Flying units Rush every activation, each model making for a point 4" beyond the nearest enemy model.
            Through351(Game(Jackals, "jackals-crossing-attack", Play.ShotWhileMoving, ["Crossing Attack"],
                [R(1, "Crossing Attack", 3), R(1, "Flying"), R(1, "Tough", 6), R(2, "Crossing Attack", 3), R(2, "Flying"), R(2, "Tough", 6)], [],
                [Crossing("Crossing Attack", "\"when this model moves through enemy units, pick one of them\""),
                 WoundDice("Crossing Attack", 3, "\"roll X dice. For each 6+ the target takes one wound\" (X 3)")],
                length: 30, more: s => RushesThrough(s, "Jackals", 2))),
            Through351(Game(MachineCults, "machine-cults-strafing", Play.ShotWhileMoving, ["Strafing"], [R(1, "Flying"), R(1, "Tough", 6), R(2, "Flying"), R(2, "Tough", 6)], [],
                [Crossing("Strafing", "\"when this model moves through enemy units, pick one of them and attack it with this weapon as if it was shooting\"")],
                weapon: [R(0, "Strafing")], length: 30, more: s => RushesThrough(s, "Machine Cults", 2))),
        ];
    }
}
