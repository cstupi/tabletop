using Tabletop.Protocol;

namespace Tabletop.Sim.Suite;

/// <summary>
/// 3.6 army games, batch I: the Enhanced variants of the *born/*bound and kindred rules. Each holder carries the base rule and its Enhanced rule;
/// the Enhanced rule suppresses the base and plays the stronger value (a check that counts the effect fails if both act), and where the base
/// keeps its own 6 (the extra attacks, the forced re-rolls) the check reads the 5-6 of both together.
/// </summary>
public static partial class SuiteCatalog
{
    private static partial class ArmyGames
    {
        private static partial class Born36
        {
            static readonly (string Uid, string Army)
                BloodBrothers = ("xnnqhh1775kvmz2r", "Blood Brothers"), ChangeE = ("r6hr29338u4micfw", "Change Disciples"), Dynasty = ("vux1Y5vvULmaxZ8P", "Eternal Dynasty"),
                DarkE = ("xp5zwh73lg1uaym4", "Dark Brothers"), Sisters = ("7oi8zeiqfamiur21", "Blessed Sisters"), Orcs = ("1wj1ysgxpuuz9bc7", "Orc Marauders"),
                Custodians = ("ewbra8nv3nq3k27p", "Custodian Brothers"), DarkElves = ("q11la9v8h1heu9ja", "Dark Elf Raiders"), HavocE = ("7o6om21wxlvvy3hq", "Havoc Brothers"),
                HighElvesE = ("7i7blhft75q9zfdc", "High Elf Fleets"), Hives = ("w7qor7b2kuifcyvk", "Alien Hives"), Hdf = ("z65fgu0l29i4lnlu", "Human Defense Force"),
                Infected = ("dnthspt7c0klhmt8", "Infected Colonies"), Knights = ("w70ha3o85pa7nigq", "Knight Brothers"), LustE = ("drqw1iswxmuugp3d", "Lust Disciples"),
                MachineCults = ("7el7k3hgy5pb9o9i", "Machine Cults"), Goblins = ("k5T0f2XIfpmPO28h", "Goblin Reclaimers"), Saurians = ("U6hwlur14RpnInZr", "Saurian Starhosts"),
                Jackals = ("BKi_hJaJflN8ZorH", "Jackals"), Ratmen = ("hk70l4d471plza00", "Ratmen Clans"), Robots = ("4k5amkxoybdiqotm", "Robot Legions"),
                VoidE = ("zpaY1LFqVk-ZJdvW", "Voidspace Stalkers"), Dwarves = ("fk1mkbp8apvltu0z", "Dwarf Guilds"), Dao = ("z8205ez2boggzs22", "DAO Union"),
                WarE = ("31xjrm9ivdimkjxp", "War Disciples"), WatchE = ("rvvb3kdn2x2pqkki", "Watch Brothers");

            static string Name((string Uid, string Army) book, string entry) => $"{Slug(book.Army)}-{Slug(entry)}";

            /// <summary>The base and its Enhanced rule on every model of the units.</summary>
            static IReadOnlyList<Tabletop.Sim.Scenarios.ScenarioRule> Both(int units, string baseRule) => Each(units, baseRule, "Enhanced " + baseRule);

            /// <summary>Extra attacks on successful unmodified 5-6: the base's 6 and the Enhanced rule's 5.</summary>
            static TestGame Attacks((string Uid, string Army) book, string baseRule) =>
                In36(Game(book, Name(book, "Enhanced " + baseRule), Play.Shoots, ["Enhanced " + baseRule], Both(2, baseRule), [],
                    [ExtraAttacks("Enhanced " + baseRule, By("Enhanced " + baseRule), [5, 6], $"\"it gets extra attacks on successful unmodified hit results of 5-6 from {baseRule} (instead of only on 6)\"")]));

            /// <summary>The opponent's -1 to hit, shooting from 4.5" (inside the base's bound), and the base's -1 not beside it.</summary>
            static TestGame Fogged((string Uid, string Army) book, string baseRule) =>
                In36(Game(book, Name(book, "Enhanced " + baseRule), Play.Shot, ["Enhanced " + baseRule], Both(1, baseRule), [],
                    [Modifier("Enhanced " + baseRule, "hit", -1, $"\"enemies attacking them always get -1 to hit rolls from {baseRule}\""),
                     ModifierReplaced("Enhanced " + baseRule, baseRule, "\"(instead of only when shooting/charging from over 9\"/6\" away)\"")], units: 1, enemyUnits: 1, length: 26, anchors: Near, enemyAnchors: NearEnemy));

            /// <summary>-8"/-4" to a min. of 6": charged at 8"; with 24" guns, shot from 14" and not from 18" (Apart).</summary>
            static IEnumerable<TestGame> Hidden((string Uid, string Army) book, string baseRule)
            {
                var entry = "Enhanced " + baseRule;
                var quote = $"\"enemy units shooting/charging it get -8\"/-4\" range to a min. of 6\" from {baseRule} (instead of only -4\"/-2\")\"";
                yield return In36(Game(book, Name(book, entry), Play.Charged, [entry], Both(3, baseRule), [], [ChargeAt(entry, 8, quote + " (charging)")], enemyUnits: 4));
                yield return In36(Game(book, Name(book, entry) + "-shot", Play.Shot, [entry], Both(2, baseRule), [], [ShotWithin(entry, 16, quote + " (shooting, 24\" guns)")],
                    units: 2, enemyRange: 24, length: 26, anchors: Apart, enemyAnchors: ApartEnemy));
            }

            /// <summary>An extra hit on an unmodified 6, shooting from 4.5" (inside the base's bound), once (the base's would be a second).</summary>
            static TestGame Hits((string Uid, string Army) book, string baseRule) =>
                In36(Game(book, Name(book, "Enhanced " + baseRule), Play.Shoots, ["Enhanced " + baseRule], Both(1, baseRule), [],
                    [ExtraHits("Enhanced " + baseRule, By("Enhanced " + baseRule), [6], $"\"the target always takes hits on unmodified hit results of 6 from {baseRule}\" (shooting from 4.5\")")], units: 1, enemyUnits: 1, length: 26, anchors: Near, enemyAnchors: NearEnemy));

            /// <summary>One extra wound per unmodified 1 to block, shooting from 4.5" (inside the base's bound), once (the base's would be a second).</summary>
            static TestGame Wounds((string Uid, string Army) book, string baseRule) =>
                In36(Game(book, Name(book, "Enhanced " + baseRule), Play.Shoots, ["Enhanced " + baseRule], Both(1, baseRule), [],
                    [ExtraWounds("Enhanced " + baseRule, By("Enhanced " + baseRule), [1], $"\"enemies always take wounds on unmodified results of 1 to block from {baseRule}\" (shooting from 4.5\")")], units: 1, enemyUnits: 1, length: 26, anchors: Near, enemyAnchors: NearEnemy));

            /// <summary>Forced re-rolls of successful unmodified 5-6 to block: the base's 6 and the Enhanced rule's 5.</summary>
            static TestGame Rerolled((string Uid, string Army) book, string baseRule) =>
                In36(Game(book, Name(book, "Enhanced " + baseRule), Play.Shoots, ["Enhanced " + baseRule], Both(2, baseRule), [],
                    [Rerolls("Enhanced " + baseRule, By("Enhanced " + baseRule), [5, 6], successOnly: true,
                        $"\"enemies taking hits from it must re-roll successful unmodified Defense results of 5-6 from {baseRule}\"")]));

            /// <summary>+2" Advance, +4" Rush and Charge: 8", 16", 16".</summary>
            static TestGame Faster((string Uid, string Army) book, string baseRule) =>
                In36(Game(book, Name(book, "Enhanced " + baseRule), Play.Moves, ["Enhanced " + baseRule], Both(4, baseRule), [],
                    [Allowance("Enhanced " + baseRule, null, $"\"it moves +2\" on Advance and +4\" on Rush/Charge from {baseRule}\"", ("advance", 8), ("rush", 16), ("charge", 16))]));

            /// <summary>Morale's game (two of three models fall to the first shooting, 3.6.0 p.10) shot from 4.5": +1, and the base's +1 not beside it.</summary>
            static TestGame Steady((string Uid, string Army) book, string baseRule) =>
                In36(Game(book, Name(book, "Enhanced " + baseRule), Play.Shot, ["Enhanced " + baseRule], [R(1, baseRule), R(1, "Enhanced " + baseRule)],
                    [Fix("hit", 6, 6, 1), Fix("defense", 1, 1)],
                    [Modifier("Enhanced " + baseRule, "morale", 1, $"\"they always get +1 to Morale Test rolls from {baseRule}\" (shot from 4.5\")"),
                     ModifierReplaced("Enhanced " + baseRule, baseRule, "\"(instead of only when shot/charged from over 9\"/6\" away)\"")],
                    enemyModels: 1, enemyAttacks: 3, rounds: 2, units: 1, enemyUnits: 1, length: 26, anchors: Near, enemyAnchors: NearEnemy));

            internal static IReadOnlyList<TestGame> Enhanced =>
            [
                Attacks(BloodBrothers, "Bloodborn"), Attacks(Dynasty, "Clan Warrior"), Attacks(Saurians, "Primal"),
                Fogged(ChangeE, "Changebound"), Fogged(MachineCults, "Machine-Fog"),
                .. Hidden(DarkE, "Darkborn"), .. Hidden(VoidE, "Shadowborn"),
                Hits(Sisters, "Devout"), Hits(Orcs, "Ferocious"),
                Wounds(Infected, "Infected"), Wounds(WarE, "Warbound"),
                Rerolled(Goblins, "Mischievous"), Rerolled(Jackals, "Scrapper"),
                Faster(LustE, "Lustbound"), Faster(Ratmen, "Scurry"),
                Steady(Hives, "Hive Bond"), Steady(Hdf, "Hold the Line"),
                In36(Game(Custodians, "custodian-brothers-enhanced-guardian", Play.Shot, ["Enhanced Guardian"], [R(1, "Guardian"), R(1, "Enhanced Guardian")], [Fix("hit", 6, 6, 6)],
                    [ApGrant("Enhanced Guardian", On("Enhanced Guardian"), 1, "\"hits they take count as having AP(-2) from Guardian (instead of only AP(-1))\" (against AP(3))")],
                    enemyWeapon: [R(0, "AP", 3)])),
                In36(Game(DarkElves, "dark-elf-raiders-enhanced-harassing", Play.Shoots, ["Enhanced Harassing"], Both(2, "Harassing"), [],
                    [Reposition("Enhanced Harassing", 6, "\"they may move by up to 6\" from Harassing (instead of only 3\")\"")])),
                In36(Game(HavocE, "havoc-brothers-enhanced-havocbound", Play.Shoots, ["Enhanced Havocbound"], Both(2, "Havocbound"), [],
                    [ApGrant("Enhanced Havocbound", By("Enhanced Havocbound"), 1, "\"it always gets AP(+1) from Havocbound\"")])),
                // It charges from 4.5": AP(+1) all the same.
                In36(Game(HavocE, "havoc-brothers-enhanced-havocbound-within-6", Play.Charges, ["Enhanced Havocbound"], Both(1, "Havocbound"), [],
                    [ApGrant("Enhanced Havocbound", By("Enhanced Havocbound", when: When.Charging), 1,
                        "\"it always gets AP(+1) from Havocbound (instead of only when shooting/charging enemies over 9\"/6\" away)\" (charging from 4.5\")")],
                    units: 1, enemyUnits: 1, length: 26, anchors: Near, enemyAnchors: NearEnemy)),
                In36(Game(HighElvesE, "high-elf-fleets-enhanced-highborn", Play.Shoots, ["Enhanced Highborn"], Both(2, "Highborn"), [],
                    [RangeBonus("Enhanced Highborn", By("Enhanced Highborn", when: When.Shooting), 12, 8, "\"it gets +8\"/+4\" range when shooting/charging from Highborn\" (shooting)")],
                    gunRange: 12, length: 26, anchors: Apart, enemyAnchors: Apart22Enemy)),
                In36(Game(HighElvesE, "high-elf-fleets-enhanced-highborn-charge", Play.Charges, ["Enhanced Highborn"], Both(4, "Highborn"), [],
                    [Allowance("Enhanced Highborn", null, "\"it gets +8\"/+4\" range when shooting/charging from Highborn\" (charging)", ("charge", 16))])),
                // Carried's shot from 4.5": Knightborn would not roll inside 9"; Enhanced Knightborn does.
                In36(Game(Knights, "knight-brothers-enhanced-knightborn", Play.Shot, ["Enhanced Knightborn"], [R(1, "Knightborn"), R(1, "Enhanced Knightborn")],
                    [Fix("hit", 6, 6, 6), Fix("defense", 1, 1, 1)],
                    [IgnoreWounds("Enhanced Knightborn", 6, "\"they always ignore shooting/charging wounds on rolls of 6+ from Knightborn\" (shot from 4.5\")"),
                     BaseReplaced("Enhanced Knightborn", "Knightborn", "\"(instead of only when shot/charged from over 9\"/6\" away)\"")],
                    enemyUnits: 1, enemyModels: 1, enemyAttacks: 3, rounds: 2, seed: 4, length: 26, anchors: Near, enemyAnchors: NearEnemy)),
                In36(Also(Carried(Robots, "robot-legions-enhanced-self-repair", "Enhanced Self-Repair", [R(1, "Self-Repair"), R(1, "Enhanced Self-Repair")],
                    IgnoreWounds("Enhanced Self-Repair", 5, "\"they ignore wounds on rolls of 5-6 from Self-Repair\"")),
                    BaseReplaced("Enhanced Self-Repair", "Self-Repair", "\"from Self-Repair (instead of only on 6+)\""))),
                In36(Game(Dwarves, "dwarf-guilds-enhanced-sturdy", Play.Shot, ["Enhanced Sturdy"], Both(1, "Sturdy"), [Fix("hit", 6, 6, 6)],
                    [Modifier("Enhanced Sturdy", "defense", 1, "\"they always get +1 to Defense rolls from Sturdy\" (shot from 4.5\")"),
                     ModifierReplaced("Enhanced Sturdy", "Sturdy", "\"(instead of only when shot/charged from over 9\"/6\" away)\"")], units: 1, enemyUnits: 1, length: 26, anchors: Near, enemyAnchors: NearEnemy)),
                In36(Game(Dao, "dao-union-enhanced-targeting-visor", Play.Shoots, ["Enhanced Targeting Visor"], Both(1, "Targeting Visor"), [],
                    [Modifier("Enhanced Targeting Visor", "hit", 1, "\"it always gets +1 to hit rolls from Targeting Visor\" (shooting from 4.5\")"),
                     ModifierReplaced("Enhanced Targeting Visor", "Targeting Visor", "\"(instead of only when shooting over 9\" away)\"")], units: 1, enemyUnits: 1, length: 26, anchors: Near, enemyAnchors: NearEnemy)),
                In36(Game(WatchE, "watch-brothers-enhanced-watchborn", Play.Shoots, ["Enhanced Watchborn"], Both(1, "Watchborn"), [],
                    [Modifier("Enhanced Watchborn", "hit", 1, "\"it always gets either AP(+1) or +1 to hit rolls\" (shooting from 4.5\")"),
                     ModifierReplaced("Enhanced Watchborn", "Watchborn", "\"(instead of only when shooting/charging enemies over 9\"/6\" away)\"")],
                    more: s => { UsePicks(WatchE.Army, 1, "Enhanced Watchborn", "Watchborn")(s); Choose("Enhanced Watchborn", "+1 to hit")(s); Choose("Watchborn", "+1 to hit")(s); }, units: 1, enemyUnits: 1, length: 26, anchors: Near, enemyAnchors: NearEnemy)),
            ];
        }
    }
}
