using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>
/// 3.6 army games, batch P: what the once grants and Predator's Ask do inside one attack. A once grant read by an attack is held to that attack's
/// `attack-end`, so every rule it gives acts in the same attack (Thrust's +1 to hit and AP(+1); Bane's and Unstoppable's Regeneration halves beside
/// their re-roll and penalty halves); Predator's "may roll +1 attack" asks the roller, and the answer decides the added dice.
/// </summary>
public static partial class SuiteCatalog
{
    private static partial class ArmyGames
    {
        const string RegenerationP = "3.6.0 p.14 Regeneration, \"When a unit where most models have this rule takes wounds, roll one die for each. On a 5+ it is ignored.\"";

        /// <summary>Two units of three models of six blade attacks each, unit 1's model 1 carrying the entry: every melee rolls many dice, so failed blocks
        /// come up, and unit 2's charges are made with no Mark of its own.</summary>
        static IReadOnlyList<ScenarioModel> BladesP(string entry) =>
            [.. Enumerable.Range(1, 2).SelectMany(u => Enumerable.Range(1, 3).Select(m => new ScenarioModel(u, m, u == 1 && m == 1 ? [R(1, entry)] : [], Weapons: [Blade(6)])))];

        /// <summary>Three models of twelve-shot rifles, all with Predator: each shooting rolls 36 dice, so two 6s come up in the first.</summary>
        static IReadOnlyList<ScenarioModel> PredatorsP => [.. Enumerable.Range(1, 3).Select(m => new ScenarioModel(1, m, [R(1, "Predator")], Weapons: [Gun(12, [])]))];

        /// <summary>Predator's Ask scripted Yes or No. Declined, Predator records itself as acting nowhere, so that game covers nothing (its checks name Predator).</summary>
        static TestGame PredatorAsksP(string name, bool yes) =>
            Game(Books36.AlienHives, name, Play.Shoots, yes ? ["Predator"] : [], [], [],
                [ExtraAttacks("Predator", By("Predator", when: When.Shooting), [6], PredatorText + " (shooting)"), PredatorAsked(PredatorText, yes)],
                carriers: PredatorsP, units: 1, enemyUnits: 1, enemy: [R(1, "Tough", 12)],
                more: s => s.Match("extra_attacks", (ask, _) => AskPicks.Labeled(ask, yes ? "Yes" : "No"))).In36();

        const string PredatorText = "\"For each unmodified roll of 6 to hit when attacking, this model may roll +1 attack with that weapon. This rule doesn’t apply to newly generated attacks.\"";

        public static IReadOnlyList<TestGame> Army36P =>
        [
            // The buffed unit's charge: one attack reads the grant's +1 to hit and AP(+1), and its attack-end removes the grant.
            Pick(Books36.VoidspaceStalkers, "voidspace-stalkers-thrust-in-melee-buff-ap", Play.Charges, "Thrust in Melee Buff",
                [ThrustBothP("\"Once per activation, before attacking, pick one friendly unit within 12\", which gets Thrust in Melee once (next time the effect would apply).\": "
                    + "3.6.0 p.15 Thrust, \"When charging, gets +1 to hit rolls and AP(+1) in melee.\"")]).In36(),
            // Unit 1 marks and shoots the marked unit; unit 2 shoots it unmarked, so its Regeneration rolls there.
            AnchoredH(Pick(Books36.SaurianStarhosts, "saurian-starhosts-bane-mark-regeneration", Play.Shoots, "Bane Mark",
                [Rerolls("Bane Mark", Granted("Bane Mark", onTarget: true), [6], successOnly: false, MarkText + "s Bane on their weapons when attacking against once\": " + BaneH),
                 MarkedRegenerationP("Bane Mark", MarkText + "s Bane on their weapons when attacking against once\": 3.6.0 p.13 Bane, \"Ignores Regeneration, and when attacking, the target must re-roll unmodified Defense results of 6.\"; " + RegenerationP)],
                units: 2, enemy: [R(1, "Regeneration"), R(1, "Tough", 6)]), 15, 11).In36(),
            // Unit 1 marks and charges the marked unit; unit 2 charges it unmarked, so its Regeneration rolls there.
            Game(Books36.WolfBrothers, "wolf-brothers-unstoppable-mark-both", Play.Charges, ["Unstoppable Mark"], [], [],
                [UnstoppableBothP("\"Once per activation, before attacking, pick one enemy unit within 18\" in line of sight, which friendly units gets Unstoppable on their weapons against once (next time the effect would apply).\": "
                    + "\"This weapon ignores Regeneration, and ignores all negative modifiers which are not from this model's special rules.\""),
                 Spent("Unstoppable Mark", "\"Unstoppable Mark\"")],
                carriers: BladesP("Unstoppable Mark"), units: 2, enemyUnits: 1, enemy: [R(1, "Melee Evasion"), R(1, "Regeneration"), R(1, "Tough", 6)], length: 26,
                more: UsePicks(Books36.WolfBrothers.Army, 1, "Unstoppable Mark")).In36(),
            PredatorAsksP("alien-hives-predator-asks", yes: true),
            PredatorAsksP("alien-hives-predator-asks-declined", yes: false),
        ];
    }
}
