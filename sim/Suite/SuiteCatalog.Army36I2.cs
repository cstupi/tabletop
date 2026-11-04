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
            static string Name((string Uid, string Army) book, string entry) => $"{Slug(book.Army)}-{Slug(entry)}";

            /// <summary>The base and its Enhanced rule on every model of the units.</summary>
            static IReadOnlyList<Tabletop.Sim.Scenarios.ScenarioRule> Both(int units, string baseRule) => Each(units, baseRule, "Enhanced " + baseRule);

            /// <summary>Extra attacks on successful unmodified 5-6: the base's 6 and the Enhanced rule's 5.</summary>
            static TestGame Attacks((string Uid, string Army) book, string baseRule) =>
                Game(book, Name(book, "Enhanced " + baseRule), Play.Shoots, ["Enhanced " + baseRule], Both(2, baseRule), [],
                    [ExtraAttacks("Enhanced " + baseRule, By("Enhanced " + baseRule), [5, 6], $"\"it gets extra attacks on successful unmodified hit results of 5-6 from {baseRule} (instead of only on 6)\"")]).In36();

            /// <summary>The opponent's -1 to hit, shooting from 4.5" (inside the base's bound), and the base's -1 not beside it.</summary>
            static TestGame Fogged((string Uid, string Army) book, string baseRule) =>
                Game(book, Name(book, "Enhanced " + baseRule), Play.Shot, ["Enhanced " + baseRule], Both(1, baseRule), [],
                    [Modifier("Enhanced " + baseRule, "hit", -1, $"\"enemies attacking them always get -1 to hit rolls from {baseRule}\""),
                     ModifierReplaced("Enhanced " + baseRule, baseRule, "\"(instead of only when shooting/charging from over 9\"/6\" away)\"")], units: 1, enemyUnits: 1, length: 26, anchors: Near, enemyAnchors: NearEnemy).In36();

            /// <summary>-8"/-4" to a min. of 6": charged at 8"; with 24" guns, shot from 14" and not from 18" (Apart).</summary>
            static IEnumerable<TestGame> Hidden((string Uid, string Army) book, string baseRule)
            {
                var entry = "Enhanced " + baseRule;
                var quote = $"\"enemy units shooting/charging it get -8\"/-4\" range to a min. of 6\" from {baseRule} (instead of only -4\"/-2\")\"";
                yield return Game(book, Name(book, entry), Play.Charged, [entry], Both(3, baseRule), [], [ChargeAt(entry, 8, quote + " (charging)")], enemyUnits: 4).In36();
                // Unit 2 stands 16.7" (edge to edge) from opponent unit 2, just past 16", and the opponents shoot it first whenever it is a legal target.
                yield return Aiming(Game(book, Name(book, entry) + "-shot", Play.Shot, [entry], Both(2, baseRule), [], [ShotWithin(entry, 16, quote + " (shooting, 24\" guns)")],
                    units: 2, enemyRange: 24, length: 26, anchors: Apart, enemyAnchors: ApartEnemy), 1, $"{book.Army} unit 2").In36();
            }

            /// <summary>An extra hit on an unmodified 6, shooting from 4.5" (inside the base's bound), once (the base's would be a second).</summary>
            static TestGame Hits((string Uid, string Army) book, string baseRule) =>
                Game(book, Name(book, "Enhanced " + baseRule), Play.Shoots, ["Enhanced " + baseRule], Both(1, baseRule), [],
                    [ExtraHits("Enhanced " + baseRule, By("Enhanced " + baseRule), [6], $"\"the target always takes hits on unmodified hit results of 6 from {baseRule}\" (shooting from 4.5\")")], units: 1, enemyUnits: 1, length: 26, anchors: Near, enemyAnchors: NearEnemy).In36();

            /// <summary>One extra wound per unmodified 1 to block, shooting from 4.5" (inside the base's bound), once (the base's would be a second).</summary>
            static TestGame Wounds((string Uid, string Army) book, string baseRule) =>
                Game(book, Name(book, "Enhanced " + baseRule), Play.Shoots, ["Enhanced " + baseRule], Both(1, baseRule), [],
                    [ExtraWounds("Enhanced " + baseRule, By("Enhanced " + baseRule), [1], $"\"enemies always take wounds on unmodified results of 1 to block from {baseRule}\" (shooting from 4.5\")")], units: 1, enemyUnits: 1, length: 26, anchors: Near, enemyAnchors: NearEnemy).In36();

            /// <summary>Forced re-rolls of successful unmodified 5-6 to block: the base's 6 and the Enhanced rule's 5.</summary>
            static TestGame Rerolled((string Uid, string Army) book, string baseRule) =>
                Game(book, Name(book, "Enhanced " + baseRule), Play.Shoots, ["Enhanced " + baseRule], Both(2, baseRule), [],
                    [Rerolls("Enhanced " + baseRule, By("Enhanced " + baseRule), [5, 6], successOnly: true,
                        $"\"enemies taking hits from it must re-roll successful unmodified Defense results of 5-6 from {baseRule}\"")]).In36();

            /// <summary>+2" Advance, +4" Rush and Charge: 8", 16", 16".</summary>
            static TestGame Faster((string Uid, string Army) book, string baseRule) =>
                Game(book, Name(book, "Enhanced " + baseRule), Play.Moves, ["Enhanced " + baseRule], Both(4, baseRule), [],
                    [Allowance("Enhanced " + baseRule, null, $"\"it moves +2\" on Advance and +4\" on Rush/Charge from {baseRule}\"", ("advance", 8), ("rush", 16), ("charge", 16))]).In36();

            /// <summary>Morale's game (two of three models fall to the first shooting, 3.6.0 p.10) shot from 4.5": +1, and the base's +1 not beside it.</summary>
            static TestGame Steady((string Uid, string Army) book, string baseRule) =>
                Game(book, Name(book, "Enhanced " + baseRule), Play.Shot, ["Enhanced " + baseRule], [R(1, baseRule), R(1, "Enhanced " + baseRule)],
                    [Fix("hit", 6, 6, 1), Fix("defense", 1, 1)],
                    [Modifier("Enhanced " + baseRule, "morale", 1, $"\"they always get +1 to Morale Test rolls from {baseRule}\" (shot from 4.5\")"),
                     ModifierReplaced("Enhanced " + baseRule, baseRule, "\"(instead of only when shot/charged from over 9\"/6\" away)\"")],
                    enemyModels: 1, enemyAttacks: 3, rounds: 2, units: 1, enemyUnits: 1, length: 26, anchors: Near, enemyAnchors: NearEnemy).In36();

            internal static IReadOnlyList<TestGame> Enhanced =>
            [
                Attacks(Books36.BloodBrothers, "Bloodborn"), Attacks(Books36.EternalDynasty, "Clan Warrior"), Attacks(Books36.SaurianStarhosts, "Primal"),
                Fogged(Books36.ChangeDisciples, "Changebound"), Fogged(Books36.MachineCults, "Machine-Fog"),
                .. Hidden(Books36.DarkBrothers, "Darkborn"), .. Hidden(Books36.VoidspaceStalkers, "Shadowborn"),
                Hits(Books36.BlessedSisters, "Devout"), Hits(Books36.OrcMarauders, "Ferocious"),
                Wounds(Books36.InfectedColonies, "Infected"), Wounds(Books36.WarDisciples, "Warbound"),
                Rerolled(Books36.GoblinReclaimers, "Mischievous"), Rerolled(Books36.Jackals, "Scrapper"),
                Faster(Books36.LustDisciples, "Lustbound"), Faster(Books36.RatmenClans, "Scurry"),
                Steady(Books36.AlienHives, "Hive Bond"), Steady(Books36.HumanDefenseForce, "Hold the Line"),
                // Opponents unit 1 fires an AP(3) Rifle, unit 2 an AP(1) Carbine. Against AP(1) Guardian's "to a min. of AP(0)" holds: AP(-1)
                // would be +1 to the defense roll.
                Game(Books36.CustodianBrothers, "custodian-brothers-enhanced-guardian", Play.Shot, ["Enhanced Guardian"], [R(1, "Guardian"), R(1, "Enhanced Guardian")],
                    FloorDice,
                    [ApGrant("Enhanced Guardian", Firing(On("Enhanced Guardian"), "Rifle"), 1, "\"hits they take count as having AP(-2) from Guardian (instead of only AP(-1))\" (against AP(3))"),
                     ApGrant("Enhanced Guardian", Firing(On("Enhanced Guardian"), "Carbine"), 0, "\"hits they take count as having AP(-2) from Guardian (instead of only AP(-1))\", Guardian's \"to a min. of AP(0)\" (against AP(1))")],
                    enemyWeapon: [R(0, "AP", 3)],
                    enemyCarriers: CarbineUnit(2, 1)).In36(),
                Game(Books36.DarkElfRaiders, "dark-elf-raiders-enhanced-harassing", Play.Shoots, ["Enhanced Harassing"], Both(2, "Harassing"), [],
                    [Reposition("Enhanced Harassing", 6, "\"they may move by up to 6\" from Harassing (instead of only 3\")\"")]).In36(),
                Game(Books36.HavocBrothers, "havoc-brothers-enhanced-havocbound", Play.Shoots, ["Enhanced Havocbound"], Both(2, "Havocbound"), [],
                    [ApGrant("Enhanced Havocbound", By("Enhanced Havocbound"), 1, "\"it always gets AP(+1) from Havocbound\"")]).In36(),
                // It charges from 4.5": AP(+1) all the same.
                Game(Books36.HavocBrothers, "havoc-brothers-enhanced-havocbound-within-6", Play.Charges, ["Enhanced Havocbound"], Both(1, "Havocbound"), [],
                    [ApGrant("Enhanced Havocbound", By("Enhanced Havocbound", when: When.Charging), 1,
                        "\"it always gets AP(+1) from Havocbound (instead of only when shooting/charging enemies over 9\"/6\" away)\" (charging from 4.5\")")],
                    units: 1, enemyUnits: 1, length: 26, anchors: Near, enemyAnchors: NearEnemy).In36(),
                Game(Books36.HighElfFleets, "high-elf-fleets-enhanced-highborn", Play.Shoots, ["Enhanced Highborn"], Both(2, "Highborn"), [],
                    [RangeBonus("Enhanced Highborn", By("Enhanced Highborn", when: When.Shooting), 12, 8, "\"it gets +8\"/+4\" range when shooting/charging from Highborn\" (shooting)")],
                    gunRange: 12, length: 26, anchors: Apart, enemyAnchors: Apart22Enemy).In36(),
                Game(Books36.HighElfFleets, "high-elf-fleets-enhanced-highborn-charge", Play.Charges, ["Enhanced Highborn"], Both(4, "Highborn"), [],
                    [Allowance("Enhanced Highborn", null, "\"it gets +8\"/+4\" range when shooting/charging from Highborn\" (charging)", ("charge", 16))]).In36(),
                // Carried's shot from 4.5": Knightborn would not roll inside 9"; Enhanced Knightborn does.
                Game(Books36.KnightBrothers, "knight-brothers-enhanced-knightborn", Play.Shot, ["Enhanced Knightborn"], [R(1, "Knightborn"), R(1, "Enhanced Knightborn")],
                    [Fix("hit", 6, 6, 6), Fix("defense", 1, 1, 1)],
                    [IgnoreWounds("Enhanced Knightborn", 6, "\"they always ignore shooting/charging wounds on rolls of 6+ from Knightborn\" (shot from 4.5\")"),
                     BaseReplaced("Enhanced Knightborn", "Knightborn", "\"(instead of only when shot/charged from over 9\"/6\" away)\"")],
                    enemyUnits: 1, enemyModels: 1, enemyAttacks: 3, rounds: 2, seed: 4, length: 26, anchors: Near, enemyAnchors: NearEnemy).In36(),
                Also(Carried(Books36.RobotLegions, "robot-legions-enhanced-self-repair", "Enhanced Self-Repair", [R(1, "Self-Repair"), R(1, "Enhanced Self-Repair")],
                    IgnoreWounds("Enhanced Self-Repair", 5, "\"they ignore wounds on rolls of 5-6 from Self-Repair\"")),
                    BaseReplaced("Enhanced Self-Repair", "Self-Repair", "\"from Self-Repair (instead of only on 6+)\"")).In36(),
                Game(Books36.DwarfGuilds, "dwarf-guilds-enhanced-sturdy", Play.Shot, ["Enhanced Sturdy"], Both(1, "Sturdy"), [Fix("hit", 6, 6, 6)],
                    [Modifier("Enhanced Sturdy", "defense", 1, "\"they always get +1 to Defense rolls from Sturdy\" (shot from 4.5\")"),
                     ModifierReplaced("Enhanced Sturdy", "Sturdy", "\"(instead of only when shot/charged from over 9\"/6\" away)\"")], units: 1, enemyUnits: 1, length: 26, anchors: Near, enemyAnchors: NearEnemy).In36(),
                Game(Books36.DaoUnion, "dao-union-enhanced-targeting-visor", Play.Shoots, ["Enhanced Targeting Visor"], Both(1, "Targeting Visor"), [],
                    [Modifier("Enhanced Targeting Visor", "hit", 1, "\"it always gets +1 to hit rolls from Targeting Visor\" (shooting from 4.5\")"),
                     ModifierReplaced("Enhanced Targeting Visor", "Targeting Visor", "\"(instead of only when shooting over 9\" away)\"")], units: 1, enemyUnits: 1, length: 26, anchors: Near, enemyAnchors: NearEnemy).In36(),
                Game(Books36.WatchBrothers, "watch-brothers-enhanced-watchborn", Play.Shoots, ["Enhanced Watchborn"], Both(1, "Watchborn"), [],
                    [Modifier("Enhanced Watchborn", "hit", 1, "\"it always gets either AP(+1) or +1 to hit rolls\" (shooting from 4.5\")"),
                     ModifierReplaced("Enhanced Watchborn", "Watchborn", "\"(instead of only when shooting/charging enemies over 9\"/6\" away)\"")],
                    more: s => { UsePicks(Books36.WatchBrothers.Army, 1, "Enhanced Watchborn", "Watchborn")(s); Choose("Enhanced Watchborn", "+1 to hit")(s); Choose("Watchborn", "+1 to hit")(s); }, units: 1, enemyUnits: 1, length: 26, anchors: Near, enemyAnchors: NearEnemy).In36(),
            ];
        }
    }
}
