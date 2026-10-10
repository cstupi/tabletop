using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>Batch 1: hit modifiers, extra hits and attacks on 6s, and AP grants. The point-blank games stand the armies 2" apart (a 26" table).</summary>
    private static partial class ArmyGames
    {
        static (string Uid, string Army) BloodPrime => ("7ex2x15bpkmy1alv", "Blood Prime Brothers");
        static IReadOnlyList<ScenarioRule> Both(string rule) => [R(1, rule), R(2, rule)];

        public static IReadOnlyList<TestGame> HitAndAp =>
        [
            Game(Dao, "dao-union-good-shot", Play.Shoots, ["Good Shot"], Both("Good Shot"), [],
                [Modifier("Good Shot", "hit", 1, "\"This model gets +1 to hit rolls when shooting.\"")]),
            Game(Hives, "alien-hives-precise", Play.Shoots, ["Precise"], Both("Precise"), [],
                [Modifier("Precise", "hit", 1, "\"Gets +1 to hit when attacking.\"")]),
            Game(Sisters, "blessed-sisters-point-blank-surge", Play.Shoots, ["Point-Blank Surge"], Both("Point-Blank Surge"), [],
                [ExtraHits("Point-Blank Surge", Within(By("Point-Blank Surge", when: When.Shooting), 12), [6],
                    "\"When this model shoots at enemies within 12\", unmodified results of 6 to hit deal 1 extra hit\"")], length: 26),
            Game(ChangeDaemons, "wormhole-daemons-of-change-sergeant", Play.Shoots, ["Sergeant"], Both("Sergeant"), [],
                [ExtraHits("Sergeant", By("Sergeant"), [6], "\"When this model attacks, unmodified results of 6 to hit deal 1 extra hit\"")]),
            Game(Hives, "alien-hives-predator-fighter", Play.Charges, ["Predator Fighter"], All3("Predator Fighter"), [],
                [ExtraAttacks("Predator Fighter", By("Predator Fighter", when: When.Charging), [6], "\"For each unmodified roll of 6 to hit in melee, this model may roll +1 attack with that weapon\"")]),
            Game(Ratmen, "ratmen-clans-predator-shooter", Play.Shoots, ["Predator Shooter"], Both("Predator Shooter"), [],
                [ExtraAttacks("Predator Shooter", By("Predator Shooter", when: When.Shooting), [6], "\"For each unmodified roll of 6 to hit when shooting, this model may roll +1 attack with that weapon\"")]),
            Game(BloodPrime, "blood-prime-brothers-melee-slayer", Play.Charges, ["Melee Slayer"], All3("Melee Slayer"), [],
                [ApGrant("Melee Slayer", By("Melee Slayer", when: When.Charging), 2, "\"When this model charges, its weapons get AP(+2) if most models in the target have Tough(3) or higher\"")],
                enemy: [R(1, "Tough", 3), R(2, "Tough", 3)]),
            Game(Dao, "dao-union-ranged-slayer", Play.Shoots, ["Ranged Slayer"], Both("Ranged Slayer"), [],
                [ApGrant("Ranged Slayer", By("Ranged Slayer", when: When.Shooting), 2, "\"When this model shoots at enemies over 9\" away, its weapons get AP(+2) if most models in the target have Tough(3) or higher\"")]),
            Game(Sisters, "blessed-sisters-point-blank-piercing", Play.Shoots, ["Point-Blank Piercing"], Both("Point-Blank Piercing"), [],
                [ApGrant("Point-Blank Piercing", Within(By("Point-Blank Piercing", when: When.Shooting), 12), 1, "\"This model gets AP(+1) when shooting enemies within 12\"\"")], length: 26),
        ];
    }
}
