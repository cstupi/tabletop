using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>3.6 hand part, batch J, second half: Machine-Fog to Versatile Reach.</summary>
public static partial class SuiteCatalog
{
    private static partial class ArmyGames
    {
        static readonly (string Uid, string Army)
            MachineCults36J = ("7el7k3hgy5pb9o9i", "Machine Cults"), War36J = ("31xjrm9ivdimkjxp", "War Disciples"),
            BattleBrothers36 = ("78qp9l5alslt6yj8", "Battle Brothers"), Robots36J = ("4k5amkxoybdiqotm", "Robot Legions"),
            Change36J = ("r6hr29338u4micfw", "Change Disciples"), Ratmen36J = ("hk70l4d471plza00", "Ratmen Clans"),
            ChangeDaemons36 = ("04z57ua0bwth37zh", "Daemons of Change"), Jackals36 = ("BKi_hJaJflN8ZorH", "Jackals"),
            Dao36J = ("z8205ez2boggzs22", "DAO Union"), HighElves36J = ("7i7blhft75q9zfdc", "High Elf Fleets"),
            Dynasty36 = ("vux1Y5vvULmaxZ8P", "Eternal Dynasty");

        const string Ignore36 = "\"When a unit where most models have this rule takes wounds, roll one die for each. On a 6+ it is ignored\"";

        /// <summary>Carried's shot (three wounds) with the rule on two of the three models.</summary>
        static TestGame Carried36((string Uid, string Army) book, string name, string entry, GameCheck check) =>
            In36(Game(book, name, Play.Shot, [entry], [], [Fix("hit", 6, 6, 6), Fix("defense", 1, 1, 1)], [check],
                units: 1, enemyUnits: 1, enemyModels: 1, enemyAttacks: 3, rounds: 2, seed: 4, carriers: Most(1, entry)));

        static IReadOnlyList<TestGame> Army36J2 =>
        [
            In36(Game(MachineCults36J, "machine-cults-machine-fog", Play.Shot, ["Machine-Fog"], [], [],
                [Modifier("Machine-Fog", "hit", -1, "\"If most models in this unit have this rule, enemy units shooting/charging them from over 9\"/6\" away get -1 to hit rolls\"")],
                units: 2, carriers: Most(2, "Machine-Fog"))),
            In36(Game(War36J, "war-disciples-melee-evasion", Play.Charges, ["Melee Evasion"], [], [Fix("hit", 1)],
                [Modifier("Melee Evasion", "hit", -1, "\"Enemies get -1 to hit rolls in melee when attacking units where most models have this rule\"", s => s.Nearest(MeleeSequence) is not null)],
                units: 3, carriers: Most(3, "Melee Evasion"))),
            // p.7's 12" charge less 2". Tough(12) keeps the carriers standing, so most models of each unit keep the rule.
            In36(Game(BattleBrothers36, "battle-brothers-melee-shrouding", Play.Charged, ["Melee Shrouding"], [R(1, "Tough", 12), R(2, "Tough", 12), R(3, "Tough", 12)], [],
                [ChargeAt("Melee Shrouding", 10, "\"When a unit where most models have this rule is targeted by an enemy unit for charging, that enemy unit gets -2\" range to a min. of 6\"\"")],
                units: 3, carriers: Most(3, "Melee Shrouding"), enemyUnits: 4)),
            Carried36(Inquisition36, "human-inquisition-protected", "Protected", IgnoreWounds("Protected", 6, Ignore36)),
            In36(Game(Robots36J, "robot-legions-protection-feat", Play.Shot, ["Protection Feat"], [R(1, "Tough", 6)], [],
                [IgnoreWounds("Protection Feat", 5, "\"when this unit takes wounds and most of its models have this rule, you may use this rule and roll one die per wound, and on a 5+ it is ignored\""),
                 OncePerGame("Protection Feat", "\"Once per game, when this unit takes wounds\"")],
                enemyUnits: 2, enemyAttacks: 4, units: 1, carriers: Most(1, "Protection Feat"))),
            // 24" guns less 4"; a 30" table puts the zones 6" apart, so the shrouded guns reach.
            In36(Game(Orcs36, "orc-marauders-ranged-shrouding", Play.Shot, ["Ranged Shrouding"], [R(1, "Tough", 12), R(2, "Tough", 12)], [],
                [ShotWithin("Ranged Shrouding", 20, "\"When a unit where most models have this rule is targeted by an enemy unit for shooting, that enemy unit gets -4\" range to a min. of 6\"\" (24\" guns)")],
                units: 2, carriers: Most(2, "Ranged Shrouding"), enemyRange: 24, length: 30)),
            In36(Game(Change36J, "change-disciples-screened", Play.Shot, ["Screened"], [], [],
                [Modifier("Screened", "hit", -1, "\"If most models in this unit have this rule, enemy units shooting/charging them from over 9\"/6\" away get -1 to hit rolls\"")],
                units: 2, carriers: Most(2, "Screened"))),
            In36(Game(Ratmen36J, "ratmen-clans-scurry", Play.Moves, ["Scurry"], [R(1, "Scurry"), R(2, "Scurry"), R(3, "Scurry"), R(4, "Scurry")], [],
                [Allowance("Scurry", null, "\"Moves +1\" when using Advance, and +2\" when using Rush/Charge\"", ("advance", 7), ("rush", 14), ("charge", 14))])),
            Carried36(Robots36J, "robot-legions-self-repair", "Self-Repair", IgnoreWounds("Self-Repair", 6, Ignore36)),
            In36(Game(ChangeDaemons36, "daemons-of-change-sergeant", Play.Shoots, ["Sergeant"], [R(1, "Sergeant"), R(2, "Sergeant")], [],
                [ApGrant("Sergeant", By("Sergeant"), 1, "\"This model gets AP(+1) when attacking\"")])),
            // The volley's targets are Tough(3), where the 3.5.1 Shatter's AP(+2) would hold on every die.
            In36(Game(Jackals36, "jackals-shatter", Play.Shoots, ["Shatter"], [], [],
                [PerDieAp("Shatter", With("Shatter"), 3, "\"For each unmodified result of 6 to hit, those hits get AP(+3)\"")], weapon: [R(0, "Shatter")], units: 2)),
            In36(Game(Jackals36, "jackals-shielded", Play.Shot, ["Shielded"], [], [Fix("hit", 6, 6, 6)],
                [Modifier("Shielded", "defense", 1, "\"Units where most models have this rule get +1 to Defense rolls against hits that are not from spells\"")],
                units: 1, carriers: Most(1, "Shielded"))),
            In36(Pick(Dwarves36, "dwarf-guilds-speed-debuff", Play.Charged, "Speed Debuff",
                [GrantedAllowance("Speed Debuff", DebuffText.Replace("enemy unit", "enemy model") + "moves -2\" when using Advance actions and -4\" when using Rush/Charge actions once\"", ("advance", 4), ("rush", 8), ("charge", 8)),
                 PicksModels("Speed Debuff", "\"pick one enemy model within 18\" in line of sight\"")])),
            In36(Game(Orcs36, "orc-marauders-speed-feat", Play.Moves, ["Speed Feat"], [R(1, "Speed Feat"), R(2, "Speed Feat")], [],
                [ChosenAllowance("Speed Feat", "Speed Feat", 1, "\"Once per game, when this unit moves, you may use this rule so that all models with this rule in it move +2\" when using Advance actions and +4\" when using Rush/Charge actions\"", ("advance", 8), ("rush", 16), ("charge", 16))],
                more: s => { UsePicks(Orcs36.Army, 1, "Speed Feat")(s); Choose("Speed Feat", "Speed Feat")(s); })),
            Rallying36(War36J, "Steadfast", "\"If a unit where most models have this rule is Shaken at the beginning of the round, roll one die. On a 4+, it stops being Shaken.\""),
            In36(Game(Dwarves36, "dwarf-guilds-sturdy", Play.Shot, ["Sturdy"], [], [Fix("hit", 6, 6, 6)],
                [Modifier("Sturdy", "defense", 1, "\"If most models in this unit have this rule, they get +1 to Defense rolls when shot/charged from over 9\"/6\" away\"")],
                units: 2, carriers: Most(2, "Sturdy"))),
            // Shoots: every shot crosses at least 12".
            In36(Game(Dao36J, "dao-union-targeting-visor", Play.Shoots, ["Targeting Visor"], [R(1, "Targeting Visor"), R(2, "Targeting Visor")], [],
                [Modifier("Targeting Visor", "hit", 1, "\"When shooting at enemies over 9\" away, this model gets +1 to hit rolls\"")])),
            In36(Pick(HighElves36J, "high-elf-fleets-unwieldy-debuff", Play.Charged, "Unwieldy Debuff",
                [StrikesLast("Unwieldy Debuff", DebuffText + "gets Unwieldy on its weapons in melee once\": Unwieldy, \"Strikes last when charging\"")])),
            In36(Game(Dynasty36, "eternal-dynasty-vengeance", Play.Exchange, ["Vengeance"], [R(1, "Vengeance"), R(2, "Vengeance"), R(3, "Vengeance")], [],
                [PerMarker("Vengeance", "hit", 1, onTarget: true, "\"place as many Vengeance tokens on the unit that destroyed it as models with this rule … Friendly units get +X to hit rolls when attacking that unit\"")],
                models: 1, enemyAttacks: 4, enemyUnits: 1, units: 3)),
            In36(Game(BattleBrothers36, "battle-brothers-versatile-reach", Play.Charges, ["Versatile Reach"], [R(1, "Versatile Reach"), R(2, "Versatile Reach")], [],
                [ChosenAllowance("Versatile Reach", "Versatile Reach: +2\" charge", null, "\"pick one effect: until the end of the activation, all models with this rule in this unit either get +4\" range when shooting or move +2\" when charging\"", ("charge", 14))],
                more: s => { UsePicks(BattleBrothers36.Army, 1, "Versatile Reach")(s); Choose("Versatile Reach", "charge")(s); })),
        ];
    }
}
