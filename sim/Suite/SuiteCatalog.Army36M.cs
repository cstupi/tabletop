using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;

namespace Tabletop.Sim.Suite;

/// <summary>
/// 3.6 hand part, batch M: the weapon rules Pierce, Smash, Destructive, Decimate and Scourge, and Brace and Parry (Army Forge's common rules), one
/// army:3.6:&lt;book uid&gt; game per case on a 3.6 book that prints the rule. The range and size each rule tests is read from the log (the attack's
/// measured range, the target's models or wounds as the attack began), so every check judges each attack by the case it is.
/// </summary>
public static partial class SuiteCatalog
{
    private static partial class ArmyGames
    {
        const string PierceText = "\"Ignores all positive Defense modifiers on the target, and when attacking units over half of this weapon's range away, this weapon gets AP(+1).\"";
        const string SmashText = "\"When attacking units over half of this weapon's range away, the target must re-roll unmodified Defense results of 6.\"";
        const string DestructiveText = "\"When attacking units with more than half of their total size or Tough value (for single-model units), those hits get AP(+1).\"";
        const string DecimateText = "\"When attacking units with more than half of their total size or Tough value (for single-model units), the target must re-roll unmodified Defense results of 6.\"";
        const string ScourgeText = "\"For each unmodified result of 1 to block hits, enemies count as having taken 2 extra wounds when checking who won melee, or if shooting wounds would trigger Morale Tests.\"";
        const string BraceText = "\"Enemies charging this unit get -X total Impact rolls, unless this unit is Fatigued.\"";
        const string ParryText = "\"When this unit takes hits in melee, roll X dice per model with this rule in it, up to as many dice as hits taken. For each 6+ one hit is ignored (defender picks which).\"";

        /// <summary>The rule's one unit 18" from the opponents' one (five Tough(3) models standing in a cover area): about 16.7" base to base.</summary>
        static readonly IReadOnlyDictionary<int, Point2> PierceAt = new Dictionary<int, Point2> { [1] = new(14, 26) }, PierceEnemyAt = new Dictionary<int, Point2> { [1] = new(14, 8) };

        /// <summary>A low cover area under the opponents' unit at PierceEnemyAt.</summary>
        static Tabletop.Sim.Scenarios.ScenarioBuilder CoverUnderEnemy(Tabletop.Sim.Scenarios.ScenarioBuilder b) => b.Terrain("ruin", new Point3(17.5, 8, 0), volumes:
            [new Volume(VolumeKind.Area, Footprint.Rect(12, 5), new Point2(0, 0), new Yaw(0), 0, 1, TerrainDeclarations.Cover, null)]);

        /// <summary>The army's units activate in order, each Holding when it is offered.</summary>
        static Action<ScriptBuilder> InOrderM(string army, int units) => s => s.Match(AskQuestions.Activate, (ask, _) =>
            Enumerable.Range(1, units).Select(k => AskPicks.Activate(ask, $"{army} unit {k}", "Hold")).FirstOrDefault(a => a is not null));

        /// <summary>
        /// Scourge on a shooting acts where no step completes: the activation's end pushes the morale test its counted wounds call for (GfHooks), and no step
        /// lists it. The evidence: a morale test for wounds, which in change-disciples-scourge-morale the wounds alone never call for (three of five models
        /// stay); CountedMorale judges it exactly.
        /// </summary>
        static bool CountedTestM(IReadOnlyList<GameEvent> log) =>
            log.Any(e => e.Payload is SequenceStarted { Sequence: "morale-test" } m && m.Context.Action == "wounds");

        static DiceFix FixM(string roll, string actor, params int[] faces) => new(roll, 0, faces, new RollKey(Actor: actor));

        public static IReadOnlyList<TestGame> Army36M =>
        [
            // A 24" gun from 16.7": over half its range, AP(+1); the target stands in cover, and its +1 is dropped.
            Game(Books36.KnightPrimeBrothers, "knight-prime-brothers-pierce-far", Play.Shoots, ["Pierce"], [], [],
                [ApGrant("Pierce", BeyondM(With("Pierce"), 12), 1, PierceText + " (from over 12\" with a 24\" gun)"),
                 IgnoredModifier("Pierce", "cover", With("Pierce"), PierceText)],
                weapon: [R(0, "Pierce")], gunRange: 24, units: 1, enemyUnits: 1, anchors: PierceAt, enemyAnchors: PierceEnemyAt, board: CoverUnderEnemy).In36(),
            // The same with a 60" gun: under half its range, no AP; cover's +1 is dropped still.
            Game(Books36.KnightPrimeBrothers, "knight-prime-brothers-pierce-near", Play.Shoots, ["Pierce"], [], [],
                [NoAp("Pierce", WithinM(With("Pierce"), 30), PierceText + " (from 30\" or less with a 60\" gun)"),
                 IgnoredModifier("Pierce", "cover", With("Pierce"), PierceText)],
                weapon: [R(0, "Pierce")], units: 1, enemyUnits: 1, anchors: PierceAt, enemyAnchors: PierceEnemyAt, board: CoverUnderEnemy).In36(),

            // 20" guns, five models a unit, on a 26" table. Unit 1 stands about 15.7" from the opponents' unit 1, unit 2 about 4.1" from their unit 2;
            // every other pair is over 21" apart. The opponents (three Tough(6) models, Defense 2+) last the game, so each pair rolls many defense dice.
            Game(Books36.BattleBrothers, "battle-brothers-smash", Play.Shoots, ["Smash"], [], [],
                [RerollsSeen("Smash", BeyondM(With("Smash"), 10), SmashText + " (from over 10\" with a 20\" gun)"),
                 NoReroll("Smash", WithinM(With("Smash"), 10), SmashText + " (from 10\" or less with a 20\" gun)")],
                weapon: [R(0, "Smash")], gunRange: 20, units: 2, models: 5, enemyUnits: 2, enemyModels: 3, enemy: [R(1, "Tough", 6), R(2, "Tough", 6)], enemyStats: Stats(4, 2),
                length: 26, anchors: new Dictionary<int, Point2> { [1] = new(1, 25), [2] = new(26, 15.7) }, enemyAnchors: new Dictionary<int, Point2> { [1] = new(1, 8), [2] = new(28, 10.3) }).In36(),

            // Five one-wound models: the first shooting's twelve hit dice are fixed to three hits and its three defense dice to 1s, so it is made at full
            // strength and leaves two; every later one is made at two of five.
            Game(Books36.EternalDynasty, "eternal-dynasty-destructive", Play.Shoots, ["Destructive"], [],
                [FixM("hit", "Eternal Dynasty unit 1", 6, 6, 6, 1, 1, 1, 1, 1, 1, 1, 1, 1), FixM("defense", "Eternal Dynasty unit 1", 1, 1, 1)],
                [ApGrant("Destructive", SizedM(With("Destructive"), overHalf: true), 1, DestructiveText + " (5 of 5 models)"),
                 NoAp("Destructive", SizedM(With("Destructive"), overHalf: false), DestructiveText + " (2 of 5 models)")],
                weapon: [R(0, "Destructive")], units: 1, enemyUnits: 1, enemyModels: 5, enemy: []).In36(),

            // One Tough(6) model. Unit 1 (ten models, forty hit dice fixed to 6s) shoots it first at 0 wounds; its forty defense dice are rolled, then fixed to
            // three 1s and 6s, so it is left at 3 wounds. Unit 2 (forty 6s to hit) shoots it at 3 wounds.
            Game(Books36.DaemonsOfPlague, "daemons-of-plague-decimate", Play.Shoots, ["Decimate"], [],
                [FixM("hit", "Daemons of Plague unit 1", [.. Enumerable.Repeat(6, 40)]),
                 FixM("defense", "Daemons of Plague unit 1", [1, 1, 1, .. Enumerable.Repeat(6, 37)]),
                 FixM("hit", "Daemons of Plague unit 2", [.. Enumerable.Repeat(6, 40)])],
                [RerollsSeen("Decimate", SizedM(With("Decimate"), overHalf: true), DecimateText + " (Tough(6) at 0 wounds)"),
                 NoReroll("Decimate", SizedM(With("Decimate"), overHalf: false), DecimateText + " (Tough(6) at 3 wounds)")],
                weapon: [R(0, "Decimate")], units: 2, models: 10, enemyUnits: 1, enemyModels: 1, enemy: [R(1, "Tough", 6)], rounds: 1,
                more: InOrderM(Books36.DaemonsOfPlague.Army, 2),
                // Anchored so neither unit's models stand across another's line to the target: all forty dice of each volley are rolled whatever the seed.
                anchors: new Dictionary<int, Point2> { [1] = new(4, 31), [2] = new(22, 26) }, enemyAnchors: new Dictionary<int, Point2> { [1] = new(10, 8) }).In36(),

            // The charge: one hit of three dice, its defense die a 1 (1 wound, 2 counted); the strike back: two hits, both defense dice 1s (2 wounds). The
            // charger took 2, the defender 1 and 2 counted: the defender loses on the counted wounds alone. Tough(3) on both, so nobody falls.
            Game(Books36.HavocBrothers, "havoc-brothers-scourge-melee", Play.Charges, ["Scourge"], [R(1, "Tough", 3)],
                [FixM("hit", "Havoc Brothers unit 1", 6, 1, 1), FixM("defense", "Havoc Brothers unit 1", 1),
                 FixM("hit", "Opponents unit 1", 6, 6, 1), FixM("defense", "Opponents unit 1", 1, 1)],
                [CountedWounds("Scourge", 2, ScourgeText + " (melee)")],
                weapon: [R(0, "Scourge")], units: 1, enemyUnits: 1, enemy: [R(1, "Tough", 3)], rounds: 1,
                anchors: new Dictionary<int, Point2> { [1] = new(14, 15) }, enemyAnchors: new Dictionary<int, Point2> { [1] = new(14, 6) }).In36(),

            // One model, four hit dice fixed to two hits, both defense dice 1s: two of five one-wound models fall (three left, over half) and 4 wounds count.
            Game(Books36.ChangeDisciples, "change-disciples-scourge-morale", Play.Shoots, ["Scourge"], [],
                [FixM("hit", "Change Disciples unit 1", 6, 6, 1, 1), FixM("defense", "Change Disciples unit 1", 1, 1)],
                [CountedMorale("Scourge", 2, ScourgeText + " (shooting; 3.6.0 p.10 \"half or less of its starting size\")")],
                weapon: [R(0, "Scourge")], units: 1, models: 1, enemyUnits: 1, enemyModels: 5, enemy: [], rounds: 1)
                .In36() with { Evidence = new Dictionary<string, Func<IReadOnlyList<GameEvent>, bool>> { ["Scourge"] = CountedTestM } },

            // Three Impact(2) models charge three Brace(1) Blades (one weapon type): 6 dice less 1.
            Game(Books36.DaemonsOfWar, "daemons-of-war-brace", Play.Charged, ["Brace"], [R(1, "Tough", 12)], [],
                [RollCount("Brace", "Impact", BraceDice, "at a unit not Fatigued", BraceText + " (Brace(1))")],
                weapon: [R(0, "Brace", 1)], units: 1, enemyUnits: 1, enemy: [R(1, "Impact", 2)], rounds: 1,
                anchors: new Dictionary<int, Point2> { [1] = new(14, 15) }, enemyAnchors: new Dictionary<int, Point2> { [1] = new(14, 6) }).In36(),
            // Two Impact(2) units charge it in turn in round 1: it strikes back at the first and is Fatigued when the second charges, which then rolls all 6.
            Game(Books36.DaemonsOfLust, "daemons-of-lust-brace-fatigued", Play.Charged, ["Brace"], [R(1, "Tough", 12)], [],
                [RollCount("Brace", "Impact", BraceDice, "at a Fatigued unit", BraceText + " (Brace(1), Fatigued)")],
                weapon: [R(0, "Brace", 1)], units: 1, enemyUnits: 2, enemy: [R(1, "Impact", 2), R(2, "Impact", 2)], rounds: 1,
                anchors: new Dictionary<int, Point2> { [1] = new(14, 15) }, enemyAnchors: new Dictionary<int, Point2> { [1] = new(6, 6), [2] = new(22, 6) }).In36(),

            // Three Parry(1) models charged by three one-attack models whose hits are fixed to three: 3 Parry dice, fixed to 6, 6, 1, leave one hit to block.
            Game(Books36.DaemonsOfChange, "daemons-of-change-parry", Play.Charged, ["Parry"], [R(1, "Tough", 12)],
                [FixM("hit", "Opponents unit 1", 6, 6, 6), FixM("Parry", "Opponents unit 1", 6, 6, 1)],
                [Parried("Parry", ParryText + " (Parry(1))")],
                weapon: [R(0, "Parry", 1)], units: 1, enemyUnits: 1, rounds: 1,
                anchors: new Dictionary<int, Point2> { [1] = new(14, 15) }, enemyAnchors: new Dictionary<int, Point2> { [1] = new(14, 6) }).In36(),
        ];
    }
}
