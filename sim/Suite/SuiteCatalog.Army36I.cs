using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>
/// 3.6 army games, batch I: the *born/*bound rules (here) and their Enhanced variants (SuiteCatalog.Army36I2.cs), one game or more per entry,
/// grouped army:3.6:&lt;book uid&gt; by a 3.6 book that prints the rule. Where 3.6 made a rule "most models" the rule sits on two of each unit's three
/// models (3.5.1's "all models" does not hold there); where it bounded a rule by range a game stands inside the bound and shows the rule idle.
/// </summary>
public static partial class SuiteCatalog
{
    private static partial class ArmyGames
    {
        public static IReadOnlyList<TestGame> Army36I => [.. Born36.Base, .. Born36.Enhanced];

        private static partial class Born36
        {
            const string Shrouded = "\"that enemy unit gets -4\"/-2\" range to a min. of 6\"\"";
            const string WatchbornText = "\"pick one effect: until the end of the activation, when shooting/charging enemies over 9\"/6\" away, all models with this rule in it either get AP(+1) or get +1 to hit rolls\"";

            /// <summary>The rule on models 1 and 2 of each of the units' three: "most models", not all.</summary>
            internal static IReadOnlyList<ScenarioModel> Most(string entry, int units) =>
                [.. Enumerable.Range(1, units).SelectMany(u => new[] { new ScenarioModel(u, 1, [R(u, entry)]), new ScenarioModel(u, 2, [R(u, entry)]) })];

            /// <summary>Tough(9) on the rule's units, so no model falls and "most models" holds all game.</summary>
            internal static IReadOnlyList<ScenarioRule> Lasting(int units) => [.. Enumerable.Range(1, units).Select(u => R(u, "Tough", 9))];

            /// <summary>The rule's one unit 4.5" from the opponents' one: every attack between them is from 6" or less.</summary>
            internal static readonly IReadOnlyDictionary<int, Point2> Near = new Dictionary<int, Point2> { [1] = new(16, 15) }, NearEnemy = new Dictionary<int, Point2> { [1] = new(16, 10.5) };

            /// <summary>
            /// The rule's unit 1 14" from the opponents' unit 1 and its unit 2 18" from their unit 2 (every other pair over 24"): with 24" guns
            /// under -8", or 12" guns under +4", unit 1's pair may shoot and unit 2's may not.
            /// </summary>
            internal static readonly IReadOnlyDictionary<int, Point2> Apart = new Dictionary<int, Point2> { [1] = new(8, 24), [2] = new(28, 24) },
                ApartEnemy = new Dictionary<int, Point2> { [1] = new(8, 10), [2] = new(28, 6) };

            /// <summary>Apart with the opponents' unit 2 22" from the rule's unit 2: under +8" on 12" guns, unit 1's pair may shoot and unit 2's may not.</summary>
            internal static readonly IReadOnlyDictionary<int, Point2> Apart22Enemy = new Dictionary<int, Point2> { [1] = new(8, 10), [2] = new(28, 2) };

            /// <summary>Each attack-sequence step completed: the evidence of a game whose rule stays idle by its bound.</summary>
            internal static bool Attacked(IReadOnlyList<GameEvent> log) => log.Any(e => e.Payload is StepCompleted { Step: "roll-to-block" });

            internal static IReadOnlyList<TestGame> Base =>
            [
                // The two units start Shaken, the rule on two of each unit's three models.
                Game(Books36.BattleBrothers, "battle-brothers-battleborn", Play.Shot, ["Battleborn"], [], [],
                    [Rallies("Battleborn", "\"If a unit where most models have this rule is Shaken at the beginning of the round, roll one die. On a 4+, it stops being Shaken.\"")],
                    units: 2, carriers: Most("Battleborn", 2), conditions: [(1, "shaken"), (2, "shaken")]).In36(),
                // Both sides hold in their deployment zones, so every shot is from over 9".
                Game(Books36.ChangeDisciples, "change-disciples-changebound", Play.Shot, ["Changebound"], [], [],
                    [Modifier("Changebound", "hit", -1, "\"If most models in this unit have this rule, enemy units shooting/charging them from over 9\"/6\" away get -1 to hit rolls\"")],
                    units: 2, carriers: Most("Changebound", 2)).In36(),
                Game(Books36.DarkBrothers, "dark-brothers-darkborn", Play.Charged, ["Darkborn"], Lasting(3), [],
                    [ChargeAt("Darkborn", 10, Shrouded + " (charging)")], enemyUnits: 4, carriers: Most("Darkborn", 3)).In36(),
                Game(Books36.DarkBrothers, "dark-brothers-darkborn-shot", Play.Shot, ["Darkborn"], Lasting(2), [],
                    [ShotWithin("Darkborn", 20, Shrouded + " (shooting, 24\" guns)")], units: 2, enemyRange: 24, carriers: Most("Darkborn", 2)).In36(),
                Game(Books36.VoidspaceStalkers, "voidspace-stalkers-shadowborn", Play.Charged, ["Shadowborn"], Lasting(3), [],
                    [ChargeAt("Shadowborn", 10, Shrouded + " (charging)")], enemyUnits: 4, carriers: Most("Shadowborn", 3)).In36(),
                Game(Books36.VoidspaceStalkers, "voidspace-stalkers-shadowborn-shot", Play.Shot, ["Shadowborn"], Lasting(2), [],
                    [ShotWithin("Shadowborn", 20, Shrouded + " (shooting, 24\" guns)")], units: 2, enemyRange: 24, carriers: Most("Shadowborn", 2)).In36(),
                Game(Books36.HavocBrothers, "havoc-brothers-havocbound", Play.Shoots, ["Havocbound"], Each(2, "Havocbound"), [],
                    [ApGrant("Havocbound", By("Havocbound", when: When.Shooting), 1, "\"When this model shoots/charges enemies over 9\"/6\" away, its weapons get AP(+1)\" (shooting)")]).In36(),
                // It charges from 4.5": no AP(+1).
                With(Game(Books36.HavocBrothers, "havoc-brothers-havocbound-within-6", Play.Charges, ["Havocbound"], [R(1, "Havocbound")], [],
                    [NoAp("Havocbound", By("Havocbound", when: When.Charging), "\"When this model shoots/charges enemies over 9\"/6\" away\" (charging from 4.5\")")],
                    units: 1, enemyUnits: 1, length: 26, anchors: Near, enemyAnchors: NearEnemy), ("Havocbound", log => Attacked(log))).In36(),
                Game(Books36.HighElfFleets, "high-elf-fleets-highborn", Play.Shoots, ["Highborn"], Each(2, "Highborn"), [],
                    [RangeBonus("Highborn", By("Highborn", when: When.Shooting), 12, 4, "\"This model gets +4\"/+2\" range when shooting/charging.\" (shooting)")],
                    gunRange: 12, length: 26, anchors: Apart, enemyAnchors: ApartEnemy).In36(),
                // Advance and Rush keep p.7's 6" and 12" (3.5.1's Highborn moved +2" on each).
                Game(Books36.HighElfFleets, "high-elf-fleets-highborn-charge", Play.Moves, ["Highborn"], Each(4, "Highborn"), [],
                    [Allowance("Highborn", null, "\"This model gets +4\"/+2\" range when shooting/charging.\" (charging; no other move)", ("advance", 6), ("rush", 12), ("charge", 14))]).In36(),
                Game(Books36.LustDisciples, "lust-disciples-lustbound", Play.Moves, ["Lustbound"], Each(4, "Lustbound"), [],
                    [Allowance("Lustbound", null, "\"Moves +1\" when using Advance, and +2\" when using Rush/Charge.\"", ("advance", 7), ("rush", 14), ("charge", 14))]).In36(),
                // Carried's shot (three wounds), the rule on two of the three models.
                Game(Books36.PlagueDisciples, "plague-disciples-plaguebound", Play.Shot, ["Plaguebound"], [], [Fix("hit", 6, 6, 6), Fix("defense", 1, 1, 1)],
                    [IgnoreWounds("Plaguebound", 6, "\"When a unit where most models have this rule takes wounds, roll one die for each. On a 6+ it is ignored.\"")],
                    units: 1, enemyUnits: 1, enemyModels: 1, enemyAttacks: 3, rounds: 2, seed: 4, carriers: Most("Plaguebound", 1)).In36(),
                Game(Books36.BloodPrimeBrothers, "blood-prime-brothers-primeborn", Play.Shot, ["Primeborn"], Lasting(1), [Fix("hit", 6, 6, 6)],
                    [ApGrant("Primeborn", On("Primeborn"), 0, "\"When units where most models have this rule take hits, those hits count as having AP(-1), to a min. of AP(0).\" (against AP(1), shot from 4.5\")")],
                    units: 1, enemyUnits: 1, enemyModels: 1, enemyAttacks: 3, rounds: 2, enemyWeapon: [R(0, "AP", 1)], carriers: Most("Primeborn", 1),
                    length: 26, anchors: Near, enemyAnchors: NearEnemy).In36(),
                Game(Books36.WarDisciples, "war-disciples-warbound", Play.Shoots, ["Warbound"], Each(2, "Warbound"), [],
                    [ExtraWounds("Warbound", By("Warbound", when: When.Shooting), [1], "\"When this model shoots/charges enemies over 9\"/6\" away, unmodified results of 1 to block hits from weapons deal 1 extra wound.\" (shooting)")]).In36(),
                // It shoots from 4.5": no extra wound on a 1.
                (Game(Books36.WarDisciples, "war-disciples-warbound-within-9", Play.Shoots, ["Warbound"], [R(1, "Warbound")], [Fix("defense", 1)],
                    [ExtraWounds("Warbound", By("Warbound"), [], "\"When this model shoots/charges enemies over 9\"/6\" away\" (shooting from 4.5\")")],
                    units: 1, enemyUnits: 1, length: 26, anchors: Near, enemyAnchors: NearEnemy) with { Evidence = new Dictionary<string, Func<IReadOnlyList<GameEvent>, bool>> { ["Warbound"] = Attacked } }).In36(),
                Game(Books36.WatchBrothers, "watch-brothers-watchborn", Play.Shoots, ["Watchborn"], Each(2, "Watchborn"), [],
                    [Modifier("Watchborn", "hit", 1, WatchbornText)], more: s => { UsePicks(Books36.WatchBrothers.Army, 1, "Watchborn")(s); Choose("Watchborn", "+1 to hit")(s); }).In36(),
            ];
        }
    }
}
