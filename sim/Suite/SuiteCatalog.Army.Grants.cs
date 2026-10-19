using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>Batch 1: "This model gets X in melee / when shooting" and the two weapon rules Ignores Cover and Ignores Regeneration.</summary>
    private static partial class ArmyGames
    {
        static (string Uid, string Army) ChangeDaemons => ("04z57ua0bwth37zh", "Wormhole Daemons of Change");
        static IReadOnlyList<ScenarioRule> All3(string rule) => [R(1, rule), R(2, rule), R(3, rule)];
        static IReadOnlyList<ScenarioRule> Each(int units, params string[] rules) => [.. Enumerable.Range(1, units).SelectMany(u => rules.Select(r => R(u, r)))];

        public static IReadOnlyList<TestGame> Grants =>
        [
            Game(Hives, "alien-hives-bane-in-melee", Play.Charges, ["Bane in Melee"], All3("Bane in Melee"), [],
                [Rerolls("Bane in Melee", By("Bane in Melee", when: When.Charging), [6], successOnly: false, "\"This model gets Bane in melee\": p.13 Bane, \"re-roll unmodified Defense results of 6\"")], seed: 2),
            Game(Robots, "robot-legions-rending-in-melee", Play.Charges, ["Rending in Melee"], All3("Rending in Melee"), [],
                [PerDieAp("Rending in Melee", By("Rending in Melee", when: When.Charging), 4, "\"This model gets Rending in melee\": p.14 Rending, \"on unmodified results of 6 to hit, those hits get AP(+4)\"")]),
            Game(Sisters, "blessed-sisters-shred-in-melee", Play.Charges, ["Shred in Melee"], All3("Shred in Melee"), [],
                [ExtraWounds("Shred in Melee", By("Shred in Melee", when: When.Charging), [1], "\"This model gets Shred in melee\": Shred, \"On unmodified results of 1 to block hits, this weapon deals 1 extra wound\"")]),
            Game(Custodians, "custodian-brothers-shred-when-shooting", Play.Shoots, ["Shred when Shooting"], [R(1, "Shred when Shooting"), R(2, "Shred when Shooting")], [],
                [ExtraWounds("Shred when Shooting", By("Shred when Shooting", when: When.Shooting), [1], "\"This model gets Shred when shooting\": Shred, \"On unmodified results of 1 to block hits, this weapon deals 1 extra wound\"")]),
            Game(Dwarves, "dwarf-guilds-surge-when-shooting", Play.Shoots, ["Surge when Shooting"], [R(1, "Surge when Shooting"), R(2, "Surge when Shooting")], [],
                [ExtraHits("Surge when Shooting", By("Surge when Shooting", when: When.Shooting), [6], "\"This model gets Surge when shooting\": p.14 Surge, \"On unmodified results of 6 to hit, this weapon deals 1 extra hit\"")]),
            // Melee Evasion (-1 to hit in melee) on the opponents is the negative modifier Unstoppable ignores.
            Game(Custodians, "custodian-brothers-unstoppable-in-melee", Play.Charges, ["Unstoppable in Melee"], All3("Unstoppable in Melee"), [],
                [NoPenalty("Unstoppable in Melee", By("Unstoppable in Melee", when: When.Charging), "\"This model gets Unstoppable in melee\": p.15 Unstoppable, \"ignores all negative modifiers to this weapon\"")],
                enemy: Each(2, "Melee Evasion")),
            Game(Lust, "lust-disciples-ignores-cover", Play.Shoots, ["Ignores Cover"], [], [],
                [NoCover("Ignores Cover", With("Ignores Cover"), "\"This weapon ignores cover.\"")], weapon: [R(0, "Ignores Cover")], units: 2, board: EnemyRuins),
            Game(Dao, "dao-union-ignores-cover-when-shooting", Play.Shoots, ["Ignores Cover when Shooting"], [R(1, "Ignores Cover when Shooting"), R(2, "Ignores Cover when Shooting")], [],
                [NoCover("Ignores Cover when Shooting", By("Ignores Cover when Shooting", when: When.Shooting), "\"This model gets Ignores Cover when shooting.\"")], board: EnemyRuins),
            // The opponents carry Regeneration ("roll one die for each. On a 5+ it is ignored"), which Ignores Regeneration leaves unrolled.
            Game(ChangeDaemons, "wormhole-daemons-of-change-ignores-regeneration", Play.Shoots, ["Ignores Regeneration"], [], [Fix("defense", 1, 1, 1)],
                [NoRoll("Ignores Regeneration", With("Ignores Regeneration"), "Regeneration", "\"This weapon ignores Regeneration.\"")],
                weapon: [R(0, "Ignores Regeneration")], units: 2, enemy: [R(1, "Tough", 3), R(2, "Tough", 3), R(1, "Regeneration"), R(2, "Regeneration")]),
            Game(Jackals, "jackals-ignores-regeneration-in-melee", Play.Charges, ["Ignores Regeneration in Melee"], All3("Ignores Regeneration in Melee"), [Fix("defense", 1)],
                [NoRoll("Ignores Regeneration in Melee", By("Ignores Regeneration in Melee", when: When.Charging), "Regeneration", "\"This model gets Ignores Regeneration in melee.\"")],
                enemy: Each(2, "Regeneration")),
        ];
    }
}
