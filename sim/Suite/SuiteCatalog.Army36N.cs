using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>
/// 3.6 batch N: the picks, choices, moves, Auras and Quick Throw that 3.6 prints as text the grammar now plays (once-per-round targets, tokens spent one
/// at a time, the defender's choice per attack, a dice move's count, Ravage from a grant, Indirect off a Hold, spell tokens lost), one army:3.6:&lt;book uid&gt;
/// game per mechanism on a 3.6 book that prints the rule. The generated Enhanced Wolfborn Aura, Enhanced Rapid Blink Aura and Versatile Defense Aura are
/// covered by pattern through their bases' games; Ravage Aura is a hand entry and has its own.
/// </summary>
public static partial class SuiteCatalog
{
    private static partial class ArmyGames
    {
        const string MindControlText = "\"Once per activation, before attacking, pick one enemy unit within 18\" in line of sight, which must take a Morale Test. If failed you may move it by up to 6\" in a straight line in any direction. Units may only be moved once per round in this way.\"";
        const string TagText = "\"Once per game, during this model's activation, before attacking, place X tokens on one enemy unit within 24\" and in line of sight, or one token on X enemy units within 24\" and in line of sight. When attacking, friendly units may remove one token from their target before rolling to ";
        const string VersatileDefenseText = "\"When a unit where most models have this rule is shot/charged from over 9\"/6\" away, pick one effect: the unit either gets +1 to Defense rolls, or enemy units get -1 to hit rolls against it. Note that you may pick a different effect each time the unit is attacked.\"";
        const string EnhancedText = "\"If this model has {0}, it may move by up to 2D3\" from {0} (instead of only D3\").\"";
        const string SpellDrainText = "\"Once per activation, before attacking, pick one enemy model within 18\" in line of sight, which must take a Morale Test. If failed, it loses 2 spell tokens.\"";
        const string QuickThrowText = "\"This model may always target enemies that are not in line of sight when shooting with Indirect weapons (instead of only when using Hold actions).\"";
        const string VdDefense = "Versatile Defense: +1 to Defense", VdHit = "Versatile Defense: -1 to hit";

        public static IReadOnlyList<TestGame> Army36N =>
        [
            MindControlN(),
            // Two carriers of X 1 in unit 1 each tag the one opponent unit before unit 1 shoots: it holds two tokens; each attack at it spends one.
            TagN(Books36.HumanInquisition, "Piercing Tag", "block to get AP(+1) on their weapons.\"", ap: true),
            TagN(Books36.Jackals, "Precision Tag", "hit to get +1 to hit rolls.\"", ap: false),

            // Shot across a 36" table from 12"-deep zones (every shot from over 9"): the defender's team answers each attack's choose_effect, +1 to Defense
            // and -1 to hit in turn.
            Game(Books36.PlagueDisciples, "plague-disciples-versatile-defense-shot", Play.Shot, ["Versatile Defense"], Each(2, "Versatile Defense"), [],
                [Chosen("Versatile Defense", [(VdDefense, "defense", 1), (VdHit, "hit", -1)], VersatileDefenseText + " (shot from over 9\")")],
                more: s => { for (var i = 0; i < 80; i++) s.At("choose_effect", i, new Chosen(i % 2 == 0 ? VdDefense : VdHit)); }).In36(),
            // Shot from 4.5": inside the 9" band, nothing is asked and nothing is given.
            Game(Books36.LustDisciples, "lust-disciples-versatile-defense-near", Play.Shot, [], Each(1, "Versatile Defense"), [],
                [NoAsk("choose_effect", "Versatile Defense", VersatileDefenseText + " (shot from 4.5\")")],
                units: 1, enemyUnits: 1, length: 26, anchors: Born36.Near, enemyAnchors: Born36.NearEnemy).In36(),

            // Models 1 and 2 of three carry the base rule, model 1 the Enhanced one: the reposition rolls two dice and moves the two carriers alone.
            EnhancedMoveN(Books36.WolfBrothers, "wolf-brothers-enhanced-wolfborn", "Wolfborn"),
            EnhancedMoveN(Books36.ElvenJesters, "elven-jesters-enhanced-rapid-blink", "Rapid Blink"),

            // Model 1 of three carries Ravage(2) and the Aura: each strike rolls its 2 plus one die per living model of the unit.
            Game(Books36.DaemonsOfPlague, "daemons-of-plague-ravage-aura", Play.Charges, ["Ravage Aura"], [], [],
                [RavageAuraDice("\"This model and its unit get Ravage(+1).\": \"roll X dice before attacking, unless this unit is Fatigued. For each 6+ the target takes one wound\" (own X 2)")],
                carriers: [new ScenarioModel(1, 1, [R(1, "Ravage", 2), R(1, "Ravage Aura")])], units: 1).In36(),

            SpellDrainN(),
            QuickThrowN(),
        ];

        /// <summary>
        /// Jackals unit 1, models 1 and 2 carrying Mind Control, holding at y 15 against two opponent units at y 11 (AnchoredH) on a 26" table. Model 1's use
        /// picks Opponents unit 1, whose test is fixed to fail (a 1); its team moves the unit. Model 2's use, in the same activation, finds unit 1 moved and is
        /// left unit 2 (the policy picks).
        /// </summary>
        static TestGame MindControlN()
        {
            const string entry = "Mind Control";
            var game = Game(Books36.Jackals, "jackals-mind-control", Play.Shoots, [entry], [], [new DiceFix("morale", 0, [1], new RollKey(Actor: "Opponents unit 1", Action: "spell"))],
                [TestThen(entry, MindControlText, move: 6), ForcedMoveByCaster(entry, MindControlText + ": the caster's side moves it"), MovedOncePerRound(entry, MindControlText)],
                carriers: [new ScenarioModel(1, 1, [R(1, entry)]), new ScenarioModel(1, 2, [R(1, entry)])], units: 1, enemyUnits: 2, length: 26,
                more: s => s.Match(AskQuestions.React, (ask, _) => AskPicks.Option(ask, c => c.Id.StartsWith(entry + "|", StringComparison.Ordinal)))
                    .Match("rule_target", (ask, p) => ask.Prompt.StartsWith(entry + ":", StringComparison.Ordinal) ? AskPicks.Pieces(ask, p, "Opponents unit 1") : null));
            return AnchoredH(game, 15, 11).In36();
        }

        /// <summary>A Tag on models 1 and 2 of unit 1 (X 1 each), two units shooting the one opponent unit (five Tough(3) models) from y 15 at y 11.</summary>
        static TestGame TagN((string Uid, string Army) book, string entry, string tail, bool ap)
        {
            var game = Game(book, $"{Slug(book.Army)}-{Slug(entry)}", Play.Shoots, [entry], [], [],
                [OneTokenAnAttack(entry, ap, TagText + tail + " (X 1, two carriers)")],
                carriers: [new ScenarioModel(1, 1, [R(1, entry, 1)]), new ScenarioModel(1, 2, [R(1, entry, 1)])], units: 2, enemyUnits: 1, length: 26,
                more: UsePicks(book.Army, 1, entry));
            return AnchoredH(game, 15, 11).In36();
        }

        /// <summary>
        /// Unit 1 of three models: models 1 and 2 carry the base rule, model 1 the Enhanced one; every use is taken. Every model's move ranks its destinations by
        /// the centre of its unit's other models (`Gathering`), so the two carriers' move keeps the unit in coherency with the third, which stands.
        /// </summary>
        static TestGame EnhancedMoveN((string Uid, string Army) book, string name, string baseRule) =>
            Game(book, name, Play.Moves, ["Enhanced " + baseRule], [], [],
                [RollCount(baseRule, 2, string.Format(EnhancedText, baseRule)), MovedBy(baseRule, 6, string.Format(EnhancedText, baseRule) + ": the models with " + baseRule)],
                carriers: [new ScenarioModel(1, 1, [R(1, baseRule), R(1, "Enhanced " + baseRule)]), new ScenarioModel(1, 2, [R(1, baseRule)])], units: 1,
                more: UsePicks(book.Army, 1, baseRule), board: b => b.Seeking(Gathering)).In36();

        /// <summary>The centre of the other models of the mover's unit on the table.</summary>
        static Point2? Gathering(Guid modelId, PolicyView view)
        {
            var unit = view.ModelOf(modelId).UnitId;
            var mates = view.Projection.Models.Where(kv => kv.Key != modelId && kv.Value.UnitId == unit && kv.Value.Location is OnTable)
                .Select(kv => ((OnTable)kv.Value.Location).Placement.Position.Horizontal).ToList();
            return mates.Count == 0 ? null : new Point2(mates.Average(p => p.X), mates.Average(p => p.Y));
        }

        /// <summary>
        /// Human Inquisition unit 1 (model 1 carrying Spell Drain) holds at y 15; the one opponent unit at y 11 has a Caster(3) model 1 with Tough(6), which
        /// holds 3 tokens from round one's start. The use picks the caster, whose unit's test is fixed to fail.
        /// </summary>
        static TestGame SpellDrainN()
        {
            const string entry = "Spell Drain";
            var game = Game(Books36.HumanInquisition, "human-inquisition-spell-drain", Play.Shoots, [entry], [], [new DiceFix("morale", 0, [1], new RollKey(Actor: "Opponents unit 1", Action: "spell"))],
                [TokensLost(entry, 2, SpellDrainText)],
                carriers: [new ScenarioModel(1, 1, [R(1, entry)])], units: 1, enemyUnits: 1, length: 26, enemy: [R(1, "Tough", 3)],
                more: s => s.Match(AskQuestions.React, (ask, _) => AskPicks.Option(ask, c => c.Id.StartsWith(entry + "|", StringComparison.Ordinal)))
                    .Match("rule_target", (ask, p) => ask.Prompt.StartsWith(entry + ":", StringComparison.Ordinal) && ask.Legal is PieceSelection sel
                        && sel.Eligible.FirstOrDefault(id => p.Models.TryGetValue(id, out var m) && m.Slot.Properties.Any(x => x.Name == "Caster")) is { } caster && caster != Guid.Empty
                        ? new SelectedPieces([caster]) : null));
            game = AnchoredH(game, 15, 11);
            var armies = game.Scenario.Armies;
            var opponents = armies[1] with { Models = [new ScenarioModel(1, 1, [R(1, "Caster", 3), R(1, "Tough", 6)])] };
            return (game with { Scenario = game.Scenario with { Armies = [armies[0], opponents] } }).In36();
        }

        /// <summary>
        /// Round one alone: Ratmen Clans units 1–3 with an Indirect 60" rifle Advance from their zone (Play.Moves: the first two Advance, the third Rushes) behind a 10"-tall sight-blocking wall
        /// across the whole 36" table at y 18, the opponents (Tough(6)) holding beyond it. Unit 1 carries Quick Throw and shoots out of sight; unit 2, without it, has no
        /// target to shoot.
        /// </summary>
        static TestGame QuickThrowN() =>
            Game(Books36.RatmenClans, "ratmen-clans-quick-throw", Play.Moves, ["Quick Throw"], [R(1, "Quick Throw")], [],
                [OutOfSightOffHold("Quick Throw", QuickThrowText + ": 3.6.0 p.14 Indirect, \"When using Hold actions and shooting, may target enemies that are not in line of sight\"")],
                weapon: [R(0, "Indirect")], units: 3, enemyUnits: 1, enemy: [R(1, "Tough", 6)], rounds: 1,
                board: b => b.Terrain("wall", new Point3(18, 18, 0), volumes:
                    [new Volume(VolumeKind.Solid, Footprint.Rect(36, 1), new Point2(0, 0), new Yaw(0), 0, 10, TerrainDeclarations.BlocksLineOfSight, null)])).In36();
    }
}
