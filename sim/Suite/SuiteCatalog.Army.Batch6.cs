using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>Batch 6 (army-book rules spec → Authored rules 6): round-start rallies and markers on units.</summary>
    private static partial class ArmyGames
    {
        const string Rally = "\"If a unit where all models have this rule is Shaken at the beginning of the round, roll one die. On a 4+, it stops being Shaken.\"";
        const string FrenzyText = "\"Place one marker on this unit when it fully destroys an enemy unit, up to a max. of two markers. For each marker";
        const string GrowthText = "\"place one marker on it at the beginning of each round if it's on the table and isn't Shaken, up to a max. of four markers. For every two markers";
        const string SpotterText = "\"pick one enemy unit within 30\" and in line of sight of this model and roll one die, on a 4+ place a marker on it";

        /// <summary>The rule's two units start the game Shaken (set from the unit menu before the first answer), so round one's start rolls for both.</summary>
        static TestGame Rallying((string Uid, string Army) book, string entry) =>
            Game(book, $"{Slug(book.Army)}-{Slug(entry)}", Play.Shot, [entry], [R(1, entry), R(2, entry)], [], [Rallies(entry, Rally)], units: 2,
                conditions: [(1, "shaken"), (2, "shaken")]);

        static string Slug(string s) => s.ToLowerInvariant().Replace(' ', '-');

        /// <summary>A pick entry with an X on model 1 of unit 1, every use taken.</summary>
        static TestGame Marking((string Uid, string Army) book, string entry, int x, IReadOnlyList<GameCheck> checks, double length = 26) =>
            Game(book, $"{Slug(book.Army)}-{Slug(entry)}", Play.Shoots, [entry], [], [], checks, carriers: [new ScenarioModel(1, 1, [R(1, entry, x)])], units: 2,
                enemyUnits: 1, length: length, more: UsePicks(book.Army, 1, entry));

        /// <summary>Fortified Growth: "For every two markers, enemies attacking this unit get AP(-1) to a min. of AP(0)": against AP(1), blocks of a unit with two or
        /// more markers roll under no AP, with fewer under AP(1).</summary>
        static GameCheck ApFloor(string quote) =>
            Exact("Fortified Growth", quote, (run, _) =>
                from x in Marked(run, "Fortified Growth")
                from r in x.Step.Rolls.Where(r => r.Roll == "defense")
                let held = x.Held.GetValueOrDefault(x.Frame.Context.TargetUnitIds.FirstOrDefault())
                where held >= 2
                let ap = r.Modifiers.Where(m => m.Source == "AP").Sum(m => m.Amount)
                select ap == 0 ? null : $"AP {ap} with {held} markers");

        public static IReadOnlyList<TestGame> Batch6 =>
        [
            Rallying(War, "Steadfast"),
            Rallying(BattleBrothers, "Battleborn"),
            Rallying(TitanLords, "Honor Code"),
            Game(WarDaemons, "wormhole-daemons-of-war-defensive-frenzy", Play.Moves, ["Defensive Frenzy"], [R(1, "Defensive Frenzy"), R(2, "Defensive Frenzy")], [],
                [PerMarker("Defensive Frenzy", "defense", 1, onTarget: true, "\"If all models in this unit have this rule, place one marker on this unit when it fully destroys an enemy unit … For each marker this unit gets +1 to Defense rolls\"")],
                enemyUnits: 4, enemyModels: 1),
            Game(DarkElves, "dark-elf-raiders-piercing-frenzy", Play.Moves, ["Piercing Frenzy"], [R(1, "Piercing Frenzy"), R(2, "Piercing Frenzy")], [],
                [PerMarker("Piercing Frenzy", "defense", -1, onTarget: false, FrenzyText + " all models with this rule in this unit get AP(+1)\"", ap: true)], enemyUnits: 4, enemyModels: 1),
            Game(Dwarves, "dwarf-guilds-precision-frenzy", Play.Moves, ["Precision Frenzy"], [R(1, "Precision Frenzy"), R(2, "Precision Frenzy")], [],
                [PerMarker("Precision Frenzy", "hit", 1, onTarget: false, FrenzyText + " all models with this rule in this unit get +1 to hit rolls\"")], enemyUnits: 4, enemyModels: 1),
            Game(PlagueDaemons, "wormhole-daemons-of-plague-fortified-growth", Play.Shot, ["Fortified Growth"], [R(1, "Fortified Growth"), R(1, "Tough", 6)], [],
                [ApFloor(GrowthText + ", enemies attacking this unit get AP(-1) to a min. of AP(0)\" (against AP(1))"), GrowsEachRound("Fortified Growth", GrowthText + "\"")],
                enemyWeapon: [R(0, "AP", 1)], units: 1),
            Game(Inquisition, "human-inquisition-defensive-growth", Play.Shot, ["Defensive Growth"], [R(1, "Defensive Growth"), R(1, "Tough", 6)], [],
                [PerMarker("Defensive Growth", "defense", 1, onTarget: true, GrowthText + ", models with this rule in this unit get +1 to Defense rolls\"", perTwo: true)], units: 1),
            Game(Hives, "alien-hives-piercing-growth", Play.Shoots, ["Piercing Growth"], [R(1, "Piercing Growth"), R(2, "Piercing Growth")], [],
                [PerMarker("Piercing Growth", "defense", -1, onTarget: false, GrowthText + ", models with this rule in this unit get AP(+1) on their weapons\"", perTwo: true, ap: true)]),
            Game(Infected, "infected-colonies-precision-growth", Play.Shoots, ["Precision Growth"], [R(1, "Precision Growth"), R(2, "Precision Growth")], [],
                [PerMarker("Precision Growth", "hit", 1, onTarget: false, GrowthText + ", models with this rule in this unit get +1 to hit rolls\"", perTwo: true)]),
            Marking(HighElves, "Piercing Spotter", 0, [PerMarker("Piercing Spotter", "defense", -1, onTarget: true, SpotterText + ". When attacking, friendly units may remove markers … to get +AP(X)\"", ap: true),
                SpentTogether("Piercing Spotter", SpotterText + "\": the markers removed when used")], length: 30),
            Marking(Dao, "Precision Spotter", 0, [PerMarker("Precision Spotter", "hit", 1, onTarget: true, SpotterText + ". Friendly units may remove markers … to get +X to hit rolls\""),
                SpentTogether("Precision Spotter", SpotterText + "\": the markers removed when used")], length: 30),
            Marking(Guerrillas, "Piercing Tag", 2, [PerMarker("Piercing Tag", "defense", -1, onTarget: true, "\"place X markers on it. When attacking, friendly units may remove markers … to get +AP(Y)\" (X 2)", ap: true),
                SpentTogether("Piercing Tag", "\"may remove markers from their target before rolling to block\"")]),
            Marking(Jackals, "Precision Tag", 2, [PerMarker("Precision Tag", "hit", 1, onTarget: true, "\"place X markers on it. When attacking, friendly units may remove markers … to get +Y to hit rolls\" (X 2)"),
                SpentTogether("Precision Tag", "\"may remove markers from their target before rolling to hit\"")]),
            Marking(Custodians, "Piercing Target", 2, [PerMarker("Piercing Target", "defense", -1, onTarget: true, "\"place X markers on it. Friendly units get +AP(X) on their weapons when attacking it\" (X 2)", ap: true)]),
            Marking(Sisters, "Precision Target", 2, [PerMarker("Precision Target", "hit", 1, onTarget: true, "\"place X markers on it. Friendly units get +X to hit rolls when attacking it\" (X 2)")]),
            Game(Dynasty, "eternal-dynasty-vengeance", Play.Exchange, ["Vengeance"], [R(1, "Vengeance"), R(2, "Vengeance"), R(3, "Vengeance")], [],
                [PerMarker("Vengeance", "hit", 1, onTarget: true, "\"place as many Vengeance markers on the unit that destroyed it as models with this rule … Friendly units get +X to hit rolls when attacking that unit\"")],
                models: 1, enemyAttacks: 4, enemyUnits: 1, units: 3),
            Game(DarkElves, "dark-elf-raiders-regenerative-strength", Play.ShotWhileMoving, ["Regenerative Strength"],
                [R(1, "Regenerative Strength"), R(1, "Regeneration"), R(1, "Tough", 3), R(2, "Regenerative Strength"), R(2, "Regeneration"), R(2, "Tough", 3)], [],
                [StrengthAttacks("\"Place one marker on this model when it ignores a wound. When in melee, pick one of its weapons to get +X attacks\"")], enemyAttacks: 4, length: 26, more: Activates(c => c.Label.EndsWith(": Charge", StringComparison.Ordinal))),
        ];
    }
}
