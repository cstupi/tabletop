using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>
    /// The rules the import completes (army-book rules spec → Readings, batches 3 and 7): Takedown Shot and Takedown Strike, whose attack is a weapon the
    /// import adds to the carrying model with the rule's profile and Limited; the scenarios carry that weapon as an import gives it.
    /// </summary>
    private static partial class ArmyGames
    {
        /// <summary>The import's weapon for a Takedown rule: "one … attack … at Quality 2+ with AP(2), Deadly(3), and Takedown", once per game.</summary>
        static ScenarioWeapon TakedownWeapon(string rule, int? range) =>
            new(rule, range is { } r ? new Dictionary<string, string> { ["Range"] = $"{r}\"", ["Attacks"] = "1" } : new Dictionary<string, string> { ["Attacks"] = "1" },
                [R(0, "AP", 2), R(0, "Deadly", 3), R(0, "Reliable"), R(0, "Takedown"), R(0, "Limited")]);

        /// <summary>The rule's weapon attacked at most once a game, each time rolling to hit at 2+.</summary>
        static GameCheck OnceAtTwoPlus(string entry, string quote) =>
            Exact(entry, quote, (run, _) =>
            {
                var weapons = run.Final.Models.Values.SelectMany(m => m.Slot.Weapons).Where(w => w.Name == entry).Select(w => w.Id).ToHashSet();
                var frames = LogQueries.Of(run).FramesOf(GfNames.AttackSequence).Where(f => f.Context.WeaponIds.Any(weapons.Contains)).ToList();
                return frames.GroupBy(f => f.Context.WeaponIds.First(weapons.Contains)).Select(g => g.Count() > 1 ? $"attacked {g.Count()} times" : null)
                    .Concat(frames.SelectMany(f => f.Steps).SelectMany(s => s.Rolls("hit")).Select(r => r.Roll.Threshold == 2 ? null : $"hit at {r.Roll.Threshold}+"))
                    .ToList();
            });

        public static IReadOnlyList<TestGame> Batch9 =>
        [
            Game(Inquisition, "human-inquisition-takedown-shot", Play.Shoots, ["Takedown Shot"], [], [],
                [OnceAtTwoPlus("Takedown Shot", "\"Once per game, when this model shoots, it may make one extra attack against the target at Quality 2+\"")],
                carriers: [new ScenarioModel(1, 1, [R(1, "Takedown Shot")], Weapons: [Gun(1, [], 60), TakedownWeapon("Takedown Shot", 60), Blade(1)])], units: 1),
            Game(Ratmen, "ratmen-clans-takedown-strike", Play.Charges, ["Takedown Strike"], [], [],
                [OnceAtTwoPlus("Takedown Strike", "\"Once per game, when it's this model's turn to attack in melee, it may make one attack at Quality 2+\"")],
                carriers: [new ScenarioModel(1, 1, [R(1, "Takedown Strike")], Weapons: [Blade(1), TakedownWeapon("Takedown Strike", null)])], units: 1),
        ];
    }
}
