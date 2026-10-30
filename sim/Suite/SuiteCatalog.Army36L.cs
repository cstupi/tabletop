using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Sim.Suite;

/// <summary>
/// 3.6 hand part, batch L: the rules the generator left text-only although the grammar expresses them, one army:3.6:&lt;book uid&gt; game per entry
/// on a 3.6 book that prints it. A "most models" rule sits on two of each unit's three models (`Most`) on Tough(12) units, so it acts only under the
/// 3.6 aggregation and no carrier falls; the generated Predator Aura and Enhanced Guerrilla Aura are covered by pattern through their bases' games.
/// </summary>
public static partial class SuiteCatalog
{
    private static partial class ArmyGames
    {
        static IReadOnlyList<Tabletop.Sim.Scenarios.ScenarioRule> Tough12L(int units) => [.. Enumerable.Range(1, units).Select(u => R(u, "Tough", 12))];

        const string MarkTextL = "\"pick one enemy unit within 18\" in line of sight, which friendly units gets ";

        public static IReadOnlyList<TestGame> Army36L =>
        [
            // Only the opponents move, so every melee is a charge at the Dodge units; four charging units meet three, so a unit that struck back is charged again.
            Game(Books36.Jackals, "jackals-dodge", Play.Charged, ["Dodge"], Tough12L(3), [],
                [Modifier("Dodge", "hit", -1, DodgeText, s => s.Nearest(MeleeSequence) is not null), DodgeFresh(DodgeText)],
                units: 3, enemyUnits: 4, carriers: Most(3, "Dodge")).In36(),
            Game(Books36.MachineCults, "machine-cults-grounded-evasion", Play.Shot, ["Grounded Evasion"], Tough12L(1), [],
                [Modifier("Grounded Evasion", "hit", -1, "\"If a unit where most models have this rule has most of them within 1\" of terrain, enemies get -1 to hit rolls when attacking them\"")],
                units: 1, carriers: Most(1, "Grounded Evasion"), board: Ruins).In36(),
            Game(Books36.RobotLegions, "robot-legions-imprecise", Play.Shoots, ["Imprecise"], [], [],
                [Modifier("Imprecise", "hit", -1, "\"Gets -1 to hit when attacking.\"")], weapon: [R(0, "Imprecise")], units: 2).In36(),
            // Unit 2 is set aside and arrives as an Ambusher at (17, 15), about 11" from the opponents' unit 1 at (16, 3) (their unit 2 at (30, 3)), so it
            // may charge in the round it arrives; the rule's side charges whenever it may.
            Game(Books36.ElvenJesters, "elven-jesters-infiltrating-precision-strike", Play.Charges, ["Infiltrating Precision Strike"], [R(2, "Infiltrating Precision Strike")], [],
                [PrecisionOnArrival("\"Counts as having Infiltrate, and gets +1 to hit rolls in melee on the round in which it deploys via this rule\"")],
                units: 2, setAside: [2], length: 26, anchors: new Dictionary<int, Point2> { [2] = new(17, 15) },
                enemyAnchors: new Dictionary<int, Point2> { [1] = new(16, 3), [2] = new(30, 3) }).In36(),
            Game(Books36.OrcMarauders, "orc-marauders-precision-charge", Play.Charges, ["Precision Charge"], All3("Precision Charge"), [],
                [GrantedModifier("Precision Charge", By("Precision Charge", when: When.Charging), "Precision Charge", "hit", 1, "\"This model gets +1 to hit rolls when charging.\"")]).In36(),
            Game(Books36.EternalDynasty, "eternal-dynasty-precision-fighter", Play.Charges, ["Precision Fighter"], All3("Precision Fighter"), [],
                [GrantedModifier("Precision Fighter", By("Precision Fighter", when: When.Charging), "Precision Fighter", "hit", 1, "\"This model gets +1 to hit rolls in melee.\"")]).In36(),
            Game(Books36.AlienHives, "alien-hives-predator", Play.Shoots, ["Predator"], Both("Predator"), [],
                [ExtraAttacks("Predator", By("Predator", when: When.Shooting), [6], "\"For each unmodified roll of 6 to hit when attacking, this model may roll +1 attack with that weapon\" (shooting)")]).In36(),
            Game(Books36.SaurianStarhosts, "saurian-starhosts-predator-charging", Play.Charges, ["Predator"], All3("Predator"), [],
                [ExtraAttacks("Predator", By("Predator", when: When.Charging), [6], "\"For each unmodified roll of 6 to hit when attacking, this model may roll +1 attack with that weapon\" (in melee)")],
                carriers: HeavyH(3), enemy: Tough6H).In36(),
            Game(Books36.RobotLegions, "robot-legions-unstoppable-when-shooting", Play.Shoots, ["Unstoppable when Shooting"], Both("Unstoppable when Shooting"), [],
                [NoPenalty("Unstoppable when Shooting", By("Unstoppable when Shooting", when: When.Shooting), "\"This model gets Unstoppable on its weapons when shooting.\": " + UnstoppableH)],
                enemy: [.. Each(2, "Stealth"), R(1, "Tough", 3), R(2, "Tough", 3)]).In36(),
            Game(Books36.RebelGuerrillas, "rebel-guerrillas-enhanced-guerrilla", Play.Shoots, ["Enhanced Guerrilla"], Each(2, "Guerrilla", "Enhanced Guerrilla"), [],
                [Reposition("Enhanced Guerrilla", 6, "\"they may move by up to 6\" from Guerrilla (instead of only 3\")\"")]).In36(),

            // Buffs and Marks: the carrier's unit takes every use offered (a Buff on its own unit).
            // Twelve attacks a model at a Tough(6) unit that lasts: the melee the grant is spent in rolls many defense dice, so a 6 to re-roll comes up.
            Game(Books36.HumanDefenseForce, "human-defense-force-bane-in-melee-buff", Play.Charges, ["Bane in Melee Buff"], [], [],
                [Rerolls("Bane in Melee Buff", Granted("Bane in Melee Buff"), [6], successOnly: false, BuffText + "Bane on its weapons in melee once\": " + BaneH),
                 Spent("Bane in Melee Buff", "\"Bane in Melee Buff\"")],
                carriers: [.. Enumerable.Range(1, 3).Select(m => new Tabletop.Sim.Scenarios.ScenarioModel(1, m, m == 1 ? [R(1, "Bane in Melee Buff")] : [], Weapons: [Blade(12)]))],
                units: 1, enemyUnits: 1, enemy: Tough6H, length: 26, more: UsePicks(Books36.HumanDefenseForce.Army, 1, "Bane in Melee Buff")).In36(),
            Pick(Books36.VoidspaceStalkers, "voidspace-stalkers-thrust-in-melee-buff", Play.Charges, "Thrust in Melee Buff",
                [GrantedModifier("Thrust in Melee Buff", Granted("Thrust in Melee Buff"), "Thrust", "hit", 1, BuffText + "Thrust in Melee once\": 3.6.0 p.15 Thrust, \"When charging, gets +1 to hit rolls\"")]).In36(),
            AnchoredH(Pick(Books36.OrcMarauders, "orc-marauders-rending-mark", Play.Shoots, "Rending Mark",
                [PerDieAp("Rending Mark", Granted("Rending Mark", onTarget: true), 4, MarkTextL + "Rending on their weapons against once\": " + RendingH)]), 15, 11).In36(),
            Pick(Books36.BattleBrothers, "battle-brothers-unstoppable-mark", Play.Charges, "Unstoppable Mark",
                [NoPenalty("Unstoppable Mark", Granted("Unstoppable Mark", onTarget: true), MarkTextL + "Unstoppable on their weapons against once\": " + UnstoppableH)],
                enemy: [R(1, "Melee Evasion")]).In36(),
            IndirectMarkL(),

            // From 4.5", inside Ferocious's 9": the extra hit is Enhanced Ferocious's, once a 6.
            Game(Books36.OrcMarauders, "orc-marauders-enhanced-ferocious-aura", Play.Shoots, ["Enhanced Ferocious Aura"], [R(1, "Ferocious")], [],
                [ExtraHits("Enhanced Ferocious Aura", Carrying("Enhanced Ferocious Aura", When.Shooting), [6],
                    "\"This model and its unit Enhanced Ferocious.\": \"the target always takes hits on unmodified hit results of 6 from Ferocious\" (shooting from 4.5\")")],
                carriers: Carriers("Enhanced Ferocious Aura", 1), units: 1, enemyUnits: 1, length: 26, anchors: Born36.Near, enemyAnchors: Born36.NearEnemy).In36(),

            // Versatile Expert has no range band: shooting from 4.5" and charging, each option in its own game.
            Game(Books36.BattleBrothers, "battle-brothers-versatile-expert", Play.Shoots, ["Versatile Expert"], [R(1, "Versatile Expert")], [],
                [Modifier("Versatile Expert", "hit", 1, VersatileExpertText + " (+1 to hit, shooting from 4.5\")")],
                more: s => { UsePicks(Books36.BattleBrothers.Army, 1, "Versatile Expert")(s); Choose("Versatile Expert", "+1 to hit")(s); },
                units: 1, enemyUnits: 1, length: 26, anchors: Born36.Near, enemyAnchors: Born36.NearEnemy).In36(),
            Game(Books36.KnightBrothers, "knight-brothers-versatile-expert-ap", Play.Charges, ["Versatile Expert"], All3("Versatile Expert"), [],
                [ApGrant("Versatile Expert", By("Versatile Expert", when: When.Charging), 1, VersatileExpertText + " (AP(+1), charging)")],
                more: s => { UsePicks(Books36.KnightBrothers.Army, 1, "Versatile Expert")(s); Choose("Versatile Expert", "AP(+1)")(s); }).In36(),
            Game(Books36.MachineCults, "machine-cults-versatile-warrior", Play.Shoots, ["Versatile Warrior"], Each(2, "Versatile Warrior"), [],
                [Modifier("Versatile Warrior", "hit", 1, "\"pick one effect: until the end of the activation, when shooting/charging enemies over 9\"/6\" away, all models with this rule in it either get AP(+1) or get +1 to hit rolls\"")],
                more: s => { UsePicks(Books36.MachineCults.Army, 1, "Versatile Warrior")(s); Choose("Versatile Warrior", "+1 to hit")(s); }).In36(),
        ];

        const string DodgeText = "\"If most models in this unit have this rule, as long as they aren't Fatigued, enemy units get -1 to hit rolls in melee against them\"";
        const string VersatileExpertText = "\"pick one effect: until the end of the activation all models with this rule in this unit either get AP(+1) when attacking, or get +1 to hit rolls when attacking\"";

        /// <summary>
        /// Indirect Mark under 3.6.0 p.14 Indirect ("When using Hold actions and shooting, may target enemies that are not in line of sight"), on a 26" table:
        /// Robot Legions unit 1, one model carrying the Mark with a blade only, at (14, 15), sees Opponents unit 1 (one Tough(6) model at (4, 2)); Robot Legions
        /// unit 2, one model holding with a 60" gun at (4, 15), does not, a 4"-tall sight-blocking wall (x 2.8–5.2, y 3.5–4.5) standing between. Unit 2 can
        /// shoot the opponents only through the marked unit's grant.
        /// </summary>
        static TestGame IndirectMarkL()
        {
            const string entry = "Indirect Mark";
            var game = Game(Books36.RobotLegions, "robot-legions-indirect-mark", Play.Shoots, [entry], [], [],
                [HiddenUnitShot(entry, MarkTextL + "Indirect on their weapons against once\": 3.6.0 p.14 Indirect, \"may target enemies that are not in line of sight as if in line of sight\""),
                 Spent(entry, $"\"{entry}\"")],
                carriers: [new Tabletop.Sim.Scenarios.ScenarioModel(1, 1, [R(1, entry)], Weapons: [Blade(1)])], units: 2, models: 1, enemyUnits: 1, enemyModels: 1, enemy: [R(1, "Tough", 6)],
                anchors: new Dictionary<int, Point2> { [1] = new(14, 15), [2] = new(4, 15) }, enemyAnchors: new Dictionary<int, Point2> { [1] = new(4, 2) }, length: 26,
                board: b => b.Terrain("wall", new Point3(4, 4, 0), volumes:
                    [new Volume(VolumeKind.Solid, Footprint.Rect(2.4, 1), new Point2(0, 0), new Yaw(0), 0, 4, TerrainDeclarations.BlocksLineOfSight, null)]),
                more: UsePicks(Books36.RobotLegions.Army, 1, entry));
            return game.In36();
        }
    }
}
