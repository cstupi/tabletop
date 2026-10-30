using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.Sim.Suite;

/// <summary>
/// The 3.6 rules task F6b's engine work makes playable, one game per mechanism, grouped army:3.6:&lt;book uid&gt;: the morale test's range and
/// opponent (Hive Bond, Hold the Line); the rolled moves (Bounding, Dash, Rapid Blink, Wolfborn).
/// </summary>
public static partial class SuiteCatalog
{
    private static partial class ArmyGames
    {
        static readonly (string Uid, string Army) Hdf36 = ("z65fgu0l29i4lnlu", "Human Defense Force"), ChangeDaemons36 = ("04z57ua0bwth37zh", "Daemons of Change"),
            Custodians36 = ("ewbra8nv3nq3k27p", "Custodian Brothers"), Jesters36 = ("nyh41t82jugcdq8m", "Elven Jesters"), WolfBrothers36 = ("yxjboa8oma9bbdck", "Wolf Brothers");

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
        ];

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
