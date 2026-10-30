using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>
    /// 3.6 hand part, batch H (weapon and attack rules): the 3.6 twins of the 3.5.1 games whose rules 3.6 reprints with "on its/their weapons", "token"
    /// for "marker" or "this model" for "units where all models have this rule", rows unchanged. Each plays its 3.5.1 game's scenario on a 3.6 book that
    /// prints the rule, its checks quoting the 3.6 entry and the 3.6.0 core rule it grants. The games 3.6 changes are SuiteCatalog.Army36H2.cs.
    /// </summary>
    private static partial class ArmyGames
    {
        const string BaneH = "3.6.0 p.13 Bane, \"the target must re-roll unmodified Defense results of 6\"";
        const string RendingH = "3.6.0 p.14 Rending, \"on unmodified results of 6 to hit, those hits get AP(+4)\"";
        const string ShredH = "3.6.0 p.15 Shred, \"For each unmodified result of 1 to block hits, this weapon deals 1 extra wound\"";
        const string UnstoppableH = "3.6.0 p.15 Unstoppable, \"ignores all negative modifiers to this weapon which are not from this model's special rules\"";
        const string FrenzyH = "\"Place one token on this unit when it fully destroys an enemy unit, up to a max. of two tokens. For each token";
        const string GrowthH = "\"Place one token on this unit at the beginning of each round if it's on the table and isn't Shaken, up to a max. of four tokens. For every two tokens";
        const string SpotterH = "\"Once per activation, before attacking, pick one enemy unit within 30\" and in line of sight of this model and roll one die, on a 4+ place a token on it";

        /// <summary>
        /// The game with every unit's first model deployed on a line across its zone (units 10" apart from x 8), the rule's army at y ruleY and the opponents
        /// at enemyY: the distance a pick's range or a range bonus needs, whatever the seed's hand placement.
        /// </summary>
        static TestGame AnchoredH(TestGame game, double ruleY, double enemyY)
        {
            static IReadOnlyDictionary<int, Tabletop.Geometry.Point2> Line(int units, double y) =>
                Enumerable.Range(1, units).ToDictionary(k => k, k => new Tabletop.Geometry.Point2(8 + 10 * (k - 1), y));
            var armies = game.Scenario.Armies;
            return game with { Scenario = game.Scenario with { Armies = [armies[0] with { Anchors = Line(armies[0].Units, ruleY) }, armies[1] with { Anchors = Line(armies[1].Units, enemyY) }] } };
        }

        /// <summary>Six attacks for every model of the rule's units, the Aura on each unit's first model: every melee rolls many dice, so the faces a rule reads come up.</summary>
        static IReadOnlyList<ScenarioModel> HeavyH(int units, string? aura = null) =>
            [.. Enumerable.Range(1, units).SelectMany(u => Enumerable.Range(1, 3).Select(m => new ScenarioModel(u, m, aura is not null && m == 1 ? [R(u, aura)] : [], Weapons: [Blade(6)])))];

        static IReadOnlyList<ScenarioRule> Tough6H => [R(1, "Tough", 6), R(2, "Tough", 6)];

        /// <summary>A Batch6 marker check under 3.6's word: its name says tokens.</summary>
        static GameCheck TokensH(GameCheck check) => check with { Name = check.Name.Replace("marker", "token", StringComparison.Ordinal) };

        public static IReadOnlyList<TestGame> Army36H =>
        [
            // Bane and Shred in melee: six attacks a model at Tough(6) opponents that last (HeavyH), so the defense faces they read come up whatever the seed.
            // The picks and range bonuses stand at the distance they need (AnchoredH).
            AnchoredH(Pick(Books36.SaurianStarhosts, "saurian-starhosts-bane-mark", Play.Shoots, "Bane Mark",
                [Rerolls("Bane Mark", Granted("Bane Mark", onTarget: true), [6], successOnly: false, MarkText + "s Bane on their weapons when attacking against once\": " + BaneH)]), 15, 11).In36(),
            Game(Books36.BattleBrothers, "battle-brothers-bane-in-melee-aura", Play.Charges, ["Bane in Melee Aura"], [], [],
                [Rerolls("Bane in Melee Aura", Carrying("Bane in Melee Aura", When.Charging), [6], successOnly: false, "\"This model and its unit get Bane on its weapons in melee.\": " + BaneH)],
                carriers: HeavyH(3, "Bane in Melee Aura"), units: 3, enemy: Tough6H).In36(),
            Game(Books36.ChangeDisciples, "change-disciples-bane", Play.Shoots, ["Bane when Shooting"], Both("Bane when Shooting"), [],
                [Rerolls("Bane when Shooting", By("Bane when Shooting", when: When.Shooting), [6], successOnly: false, "\"This model gets Bane on its weapons when shooting.\": " + BaneH)]).In36(),
            Shooting(Books36.BattleBrothers, "battle-brothers-bane-when-shooting-aura", "Bane when Shooting Aura",
                Rerolls("Bane when Shooting Aura", Carrying("Bane when Shooting Aura", When.Shooting), [6], successOnly: false, "\"This model and its unit get Bane on its weapons when shooting.\": " + BaneH)).In36(),

            // Hit modifiers and moves the 3.6 text gives "this model"; the rows keep aggregation all, so every model of the units carries the rule.
            Game(Books36.SoulSnatcherCults, "soul-snatcher-cults-grounded-precision", Play.Shoots, ["Grounded Precision"], Both("Grounded Precision"), [],
                [Modifier("Grounded Precision", "hit", 1, "\"If most models in this unit are within 1\" of terrain, this model gets +1 to hit rolls when attacking.\"")], board: Ruins).In36(),
            Game(Books36.HumanInquisition, "human-inquisition-hit-and-run", Play.Shoots, ["Hit & Run"], Both("Hit & Run"), [],
                [Reposition("Hit & Run", 3, "\"Once per round, this model may move by up to 3\" after shooting or being in melee.\"")]).In36(),
            Game(Books36.DaemonsOfLust, "daemons-of-lust-hit-and-run-fighter", Play.Charges, ["Hit & Run Fighter"], All3("Hit & Run Fighter"), [Fix("hit", 6)],
                [Reposition("Hit & Run Fighter", 3, "\"Once per round, this model may move by up to 3\" after being in melee.\"")]).In36(),
            Game(Books36.InfectedColonies, "infected-colonies-hit-and-run-shooter", Play.Shoots, ["Hit & Run Shooter"], Both("Hit & Run Shooter"), [],
                [Reposition("Hit & Run Shooter", 3, "\"Once per round, this model may move by up to 3\" after shooting.\"")]).In36(),

            // Increased Shooting Range: a 12" rifle, so every shot that lands beyond 12" is one the +6" reached.
            AnchoredH(Game(Books36.BlessedSisters, "blessed-sisters-increased-shooting-range", Play.Shoots, ["Increased Shooting Range"], Each(2, "Increased Shooting Range"), [],
                [RangeBonus("Increased Shooting Range", By("Increased Shooting Range", when: When.Shooting), 12, 6, "\"This model gets +6\" range on its weapons when shooting.\"")], gunRange: 12), 25, 10).In36(),
            AnchoredH(Game(Books36.AlienHives, "alien-hives-increased-shooting-range-aura", Play.Shoots, ["Increased Shooting Range Aura"], [], [],
                [RangeBonus("Increased Shooting Range Aura", Carrying("Increased Shooting Range Aura", When.Shooting), 12, 6, "\"This model and its unit get +6\" range on its weapons when shooting.\"")],
                carriers: Carriers("Increased Shooting Range Aura", 2), units: 2, gunRange: 12), 25, 10).In36(),
            AnchoredH(Pick(Books36.SoulSnatcherCults, "soul-snatcher-cults-increased-shooting-range-buff", Play.Shoots, "Increased Shooting Range Buff",
                [RangeBonus("Increased Shooting Range Buff", Granted("Increased Shooting Range Buff"), 12, 6, BuffText + "+6\" range on its weapons when shooting once\"")],
                gunRange: 12, length: 30), 19, 4).In36(),
            AnchoredH(Pick(Books36.EternalDynasty, "eternal-dynasty-increased-shooting-range-mark", Play.Shoots, "Increased Shooting Range Mark",
                [RangeBonus("Increased Shooting Range Mark", Granted("Increased Shooting Range Mark", onTarget: true), 12, 6, MarkText + " +6\" range on their weapons when shooting against once\"")],
                gunRange: 12, length: 30), 19, 4).In36(),

            Game(Books36.DarkElfRaiders, "dark-elf-raiders-lacerate", Play.Shoots, ["Lacerate"], [], [],
                [Rerolls("Lacerate", With("Lacerate"), [6], successOnly: false, "\"When attacking, the target must re-roll unmodified Defense results of 6.\"")], weapon: [R(0, "Lacerate")], units: 2).In36(),
            Game(Books36.BloodPrimeBrothers, "blood-prime-brothers-melee-slayer", Play.Charges, ["Melee Slayer"], All3("Melee Slayer"), [],
                [ApGrant("Melee Slayer", By("Melee Slayer", when: When.Charging), 2, "\"When charging, this model's weapons get AP(+2) if most models in the target have Tough(3) or higher.\"")],
                enemy: [R(1, "Tough", 3), R(2, "Tough", 3)]).In36(),

            // Piercing
            AnchoredH(Pick(Books36.MachineCults, "machine-cults-piercing-debuff", Play.Shot, "Piercing Debuff",
                [BlocksUnder("Piercing Debuff", Granted("Piercing Debuff"), 0, DebuffText + "loses AP(1) on its weapons when attacking once\" (of AP(1))")], enemyWeapon: [R(0, "AP", 1)]), 15, 11).In36(),
            Melee(Books36.OrcMarauders, "orc-marauders-piercing-fighter-aura", "Piercing Fighter Aura",
                ApGrant("Piercing Fighter Aura", Carrying("Piercing Fighter Aura", When.Charging), 1, "\"This model and its unit get AP(+1) on its weapons in melee.\"")).In36(),
            Game(Books36.DarkElfRaiders, "dark-elf-raiders-piercing-frenzy", Play.Moves, ["Piercing Frenzy"], Both("Piercing Frenzy"), [],
                [TokensH(PerMarker("Piercing Frenzy", "defense", -1, onTarget: false, FrenzyH + " all models with this rule in this unit get AP(+1) on their weapons\"", ap: true))],
                enemyUnits: 4, enemyModels: 1).In36(),
            Game(Books36.AlienHives, "alien-hives-piercing-growth", Play.Shoots, ["Piercing Growth"], Both("Piercing Growth"), [],
                [TokensH(PerMarker("Piercing Growth", "defense", -1, onTarget: false, GrowthH + ", models with this rule in this unit get AP(+1) on their weapons\"", perTwo: true, ap: true))]).In36(),
            Shooting(Books36.DaemonsOfWar, "daemons-of-war-piercing-shooter-aura", "Piercing Shooter Aura",
                ApGrant("Piercing Shooter Aura", Carrying("Piercing Shooter Aura", When.Shooting), 1, "\"This model and its unit get AP(+1) on its weapons when shooting.\"")).In36(),
            AnchoredH(Pick(Books36.DaoUnion, "dao-union-piercing-shooting-mark", Play.Shoots, "Piercing Shooting Mark",
                [BlocksUnder("Piercing Shooting Mark", Granted("Piercing Shooting Mark", onTarget: true), 1, MarkText + "s AP(+1) on their weapons when shooting against once\"")]), 15, 11).In36(),
            AnchoredH(Marking(Books36.HighElfFleets, "Piercing Spotter", 0,
                [TokensH(PerMarker("Piercing Spotter", "defense", -1, onTarget: true, SpotterH + ". When attacking, friendly units may remove tokens … to get +AP(X) on their weapons\"", ap: true)),
                 TokensH(SpentTogether("Piercing Spotter", SpotterH + "\": the tokens removed when used"))], length: 30) with { Dice = [RollOff, new DiceFix("Piercing Spotter", 0, [6])] }, 19, 11).In36(),

            // Precision
            Game(Books36.DwarfGuilds, "dwarf-guilds-precision-frenzy", Play.Moves, ["Precision Frenzy"], Both("Precision Frenzy"), [],
                [TokensH(PerMarker("Precision Frenzy", "hit", 1, onTarget: false, FrenzyH + " all models with this rule in this unit get +1 to hit rolls\""))], enemyUnits: 4, enemyModels: 1).In36(),
            Game(Books36.InfectedColonies, "infected-colonies-precision-growth", Play.Shoots, ["Precision Growth"], Both("Precision Growth"), [],
                [TokensH(PerMarker("Precision Growth", "hit", 1, onTarget: false, GrowthH + ", models with this rule in this unit get +1 to hit rolls\"", perTwo: true))]).In36(),
            AnchoredH(Marking(Books36.DaoUnion, "Precision Spotter", 0,
                [TokensH(PerMarker("Precision Spotter", "hit", 1, onTarget: true, SpotterH + ". Friendly units may remove tokens … to get +X to hit rolls\"")),
                 TokensH(SpentTogether("Precision Spotter", SpotterH + "\": the tokens removed when used"))], length: 30) with { Dice = [RollOff, new DiceFix("Precision Spotter", 0, [6])] }, 19, 11).In36(),

            // Rending
            Game(Books36.RobotLegions, "robot-legions-rending-in-melee", Play.Charges, ["Rending in Melee"], All3("Rending in Melee"), [],
                [PerDieAp("Rending in Melee", By("Rending in Melee", when: When.Charging), 4, "\"This model gets Rending on its weapons in melee.\": " + RendingH)]).In36(),
            Melee(Books36.RebelGuerrillas, "rebel-guerrillas-rending-in-melee-aura", "Rending in Melee Aura",
                PerDieAp("Rending in Melee Aura", Carrying("Rending in Melee Aura", When.Charging), 4, "\"This model and its unit get Rending on its weapons in melee.\": " + RendingH), seed: 1).In36(),
            Shooting(Books36.MachineCults, "machine-cults-rending-when-shooting-aura", "Rending when Shooting Aura",
                PerDieAp("Rending when Shooting Aura", Carrying("Rending when Shooting Aura", When.Shooting), 4, "\"This model and its unit get Rending on its weapons when shooting.\": " + RendingH)).In36(),

            // Shred
            AnchoredH(Pick(Books36.CustodianBrothers, "custodian-brothers-shred-mark", Play.Shoots, "Shred Mark",
                [ExtraWounds("Shred Mark", Granted("Shred Mark", onTarget: true), [1], MarkText + "s Shred on their weapons when attacking against once\": " + ShredH)]), 15, 11).In36(),
            Game(Books36.BlessedSisters, "blessed-sisters-shred-in-melee", Play.Charges, ["Shred in Melee"], All3("Shred in Melee"), [],
                [ExtraWounds("Shred in Melee", By("Shred in Melee", when: When.Charging), [1], "\"This model gets Shred on its weapons in melee.\": " + ShredH)],
                enemy: Tough6H, carriers: HeavyH(3)).In36(),
            Game(Books36.BlessedSisters, "blessed-sisters-shred-in-melee-aura", Play.Charges, ["Shred in Melee Aura"], [], [],
                [ExtraWounds("Shred in Melee Aura", Carrying("Shred in Melee Aura", When.Charging), [1], "\"This model and its unit get Shred on its weapons in melee.\": " + ShredH)],
                carriers: HeavyH(3, "Shred in Melee Aura"), units: 3, enemy: Tough6H).In36(),
            Game(Books36.CustodianBrothers, "custodian-brothers-shred-when-shooting", Play.Shoots, ["Shred when Shooting"], Both("Shred when Shooting"), [],
                [ExtraWounds("Shred when Shooting", By("Shred when Shooting", when: When.Shooting), [1], "\"This model gets Shred on its weapons when shooting.\": " + ShredH)]).In36(),
            Shooting(Books36.CustodianBrothers, "custodian-brothers-shred-when-shooting-aura", "Shred when Shooting Aura",
                ExtraWounds("Shred when Shooting Aura", Carrying("Shred when Shooting Aura", When.Shooting), [1], "\"This model and its unit get Shred on its weapons when shooting.\": " + ShredH)).In36(),

            // Thrust and Unstoppable: the opponents' Melee Evasion and Stealth are the negative modifiers, none from the holders' own rules.
            Game(Books36.InfectedColonies, "infected-colonies-thrust-in-melee-aura", Play.Charges, ["Thrust in Melee Aura"], [], [],
                [GrantedModifier("Thrust in Melee Aura", Carrying("Thrust in Melee Aura", When.Charging), "Thrust", "hit", 1, "\"This model and its unit get Thrust on its weapons in melee.\": 3.6.0 p.15 Thrust, \"When charging, gets +1 to hit rolls\""),
                 ApGrant("Thrust in Melee Aura", Carrying("Thrust in Melee Aura", When.Charging), 1, "\"This model and its unit get Thrust on its weapons in melee.\": 3.6.0 p.15 Thrust, \"and AP(+1) in melee\"")],
                carriers: Carriers("Thrust in Melee Aura", 3), units: 3).In36(),
            Game(Books36.CustodianBrothers, "custodian-brothers-unstoppable-in-melee", Play.Charges, ["Unstoppable in Melee"], All3("Unstoppable in Melee"), [],
                [NoPenalty("Unstoppable in Melee", By("Unstoppable in Melee", when: When.Charging), "\"This model gets Unstoppable on its weapons in melee.\": " + UnstoppableH)],
                enemy: Each(2, "Melee Evasion")).In36(),
            Melee(Books36.CustodianBrothers, "custodian-brothers-unstoppable-in-melee-aura", "Unstoppable in Melee Aura",
                NoPenalty("Unstoppable in Melee Aura", Carrying("Unstoppable in Melee Aura", When.Charging), "\"This model and its unit get Unstoppable on its weapons in melee.\": " + UnstoppableH),
                enemy: Each(2, "Melee Evasion")).In36(),
            Shooting(Books36.RobotLegions, "robot-legions-unstoppable-when-shooting-aura", "Unstoppable when Shooting Aura",
                NoPenalty("Unstoppable when Shooting Aura", Carrying("Unstoppable when Shooting Aura", When.Shooting), "\"This model and its unit get Unstoppable on its weapons when shooting.\": " + UnstoppableH),
                enemy: [.. Each(2, "Stealth"), R(1, "Tough", 3), R(2, "Tough", 3)]).In36(),
        ];
    }
}
