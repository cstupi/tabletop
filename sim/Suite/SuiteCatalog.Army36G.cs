using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.Sim.Suite;

/// <summary>
/// The 3.6 rules task F6b's engine work makes playable, one game per mechanism, grouped army:3.6:&lt;book uid&gt;: the morale test's range and
/// opponent (Hive Bond, Hold the Line); the rolled moves (Bounding, Dash, Rapid Blink, Wolfborn); the strike-turn rolls (Ravage, Regenerative Strength).
/// </summary>
public static partial class SuiteCatalog
{
    private static partial class ArmyGames
    {
        static readonly (string Uid, string Army) Hdf36 = ("z65fgu0l29i4lnlu", "Human Defense Force"), ChangeDaemons36 = ("04z57ua0bwth37zh", "Daemons of Change"),
            Custodians36 = ("ewbra8nv3nq3k27p", "Custodian Brothers"), Jesters36 = ("nyh41t82jugcdq8m", "Elven Jesters"), WolfBrothers36 = ("yxjboa8oma9bbdck", "Wolf Brothers"),
            DarkElves36 = ("q11la9v8h1heu9ja", "Dark Elf Raiders");

        public static IReadOnlyList<TestGame> Army36G { get; } =
        [
            // Both sides hold in their zones, over 9" apart: every morale test the shooting forces is "shot from over 9" away".
            In36(Morale(Hives36, "alien-hives-hive-bond", "Hive Bond", [R(1, "Hive Bond")], 1, ShotFromOverNine), Hives36),
            In36(Morale(Hdf36, "human-defense-force-hold-the-line", "Hold the Line", [R(1, "Hold the Line")], 1, ShotFromOverNine), Hdf36),
            In36(Pick(ChangeDaemons36, "daemons-of-change-bounding", Play.Moves, "Bounding",
                [MovedWithinRoll("Bounding", 1, "\"When this unit is activated, you may move all models with this rule in it by up to D3+1\"\"")], grants: false), ChangeDaemons36),
            In36(Pick(Custodians36, "custodian-brothers-dash", Play.Moves, "Dash",
                [MovedWithinRoll("Dash", 1, "\"Once per round, at the end of this unit's activation, you may move all models with this rule in it by up to D3+1\"\"")], grants: false), Custodians36),
            In36(Pick(Jesters36, "elven-jesters-rapid-blink", Play.Moves, "Rapid Blink",
                [MovedWithinRoll("Rapid Blink", 0, "\"When this unit is activated, you may move all models with this rule in it by up to D3\"\"")], grants: false), Jesters36),
            In36(Pick(WolfBrothers36, "wolf-brothers-wolfborn", Play.Moves, "Wolfborn",
                [MovedWithinRoll("Wolfborn", 0, "\"When this unit is activated, you may move all models with this rule in it by up to D3\"\"")], grants: false), WolfBrothers36),
            In36(Game(Ratmen36, "ratmen-clans-ravage", Play.Charges, ["Ravage"], [R(1, "Ravage", 2), R(2, "Ravage", 2), R(3, "Ravage", 2)], [],
                [WoundDice("Ravage", 2, "\"roll X dice before attacking ... For each 6+ the target takes one wound\" (X 2)"), OncePerRound("Ravage", "\"Once per round, when it’s this model’s turn to strike in melee\"")],
                units: 3), Ratmen36),
            // One Tough(6) opponent model of one attack: its first attack hits, the block fails and the Regeneration die ignores the wound, so the unit it struck
            // holds a token when it charges again.
            In36(Game(DarkElves36, "dark-elf-raiders-regenerative-strength", Play.ShotWhileMoving, ["Regenerative Strength"],
                [R(1, "Regenerative Strength"), R(1, "Regeneration"), R(1, "Tough", 3), R(2, "Regenerative Strength"), R(2, "Regeneration"), R(2, "Tough", 3)],
                [new DiceFix("hit", 0, [6], new RollKey(Actor: "Opponents unit 1")), new DiceFix("defense", 0, [1], new RollKey(Actor: "Opponents unit 1")),
                 new DiceFix("Regeneration", 0, [6], new RollKey(Actor: "Opponents unit 1"))],
                [StrengthDice("\"roll as many dice as tokens on it before attacking ... For each 4+ the target takes one wound\""),
                 OncePerRound("Regenerative Strength", "\"Once per round, when it’s this model’s turn to strike in melee\"")],
                enemy: [R(1, "Tough", 6)], enemyUnits: 1, enemyModels: 1, enemyAttacks: 1, length: 26, more: Activates(c => c.Label.EndsWith(": Charge", StringComparison.Ordinal))), DarkElves36),
        ];

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
    }
}
