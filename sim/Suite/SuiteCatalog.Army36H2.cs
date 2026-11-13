using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>
    /// 3.6 hand part, batch H: the weapon and attack rules whose 3.6 text changes what the 3.5.1 game showed (Ranged Slayer's Hold, the Targets' one token,
    /// the Takedowns' X, Indirect on Hold through the Aura) and the new Counter in Melee Buff, each by construction.
    /// </summary>
    private static partial class ArmyGames
    {
        const string TargetH = "\"Once per game, during this model's activation, before attacking, pick X enemy units within 18\" and in line of sight of it, and place one token on them.";

        public static IReadOnlyList<TestGame> Army36H2 =>
        [
            // Both sides hold (Shoots) on a 20" table (zones overlap), the rule's units 6" from the one Tough(3) unit: every shot is a Hold within 9", which 3.5.1's "over 9\" away" never gives AP.
            Game(Books36.DaoUnion, "dao-union-ranged-slayer", Play.Shoots, ["Ranged Slayer"], Both("Ranged Slayer"), [],
                [ApGrant("Ranged Slayer", By("Ranged Slayer", when: When.Shooting), 2, "\"When using Hold actions and shooting, this model's weapons get AP(+2) if most models in the target have Tough(3) or higher.\" (within 9\")")],
                enemyUnits: 1, length: 20, anchors: new Dictionary<int, Point2> { [1] = new(12, 13), [2] = new(24, 13) }, enemyAnchors: new Dictionary<int, Point2> { [1] = new(18, 7) }).In36(),
            // The books print X = 1; the pick's 18" is met by the anchors.
            AnchoredH(Marking(Books36.CustodianBrothers, "Piercing Target", 1,
                [TokensH(PerMarker("Piercing Target", "defense", -1, onTarget: true, TargetH + " Friendly units get AP(+1) on their weapons when attacking them.\" (X 1)", ap: true)),
                 OneTokenH("Piercing Target", TargetH + "\" (X 1)")]), 15, 11).In36(),
            AnchoredH(Marking(Books36.BlessedSisters, "Precision Target", 1,
                [TokensH(PerMarker("Precision Target", "hit", 1, onTarget: true, TargetH + " Friendly units get +1 to hit rolls when attacking them.\" (X 1)")),
                 OneTokenH("Precision Target", TargetH + "\" (X 1)")]), 15, 11).In36(),
            Game(Books36.HumanInquisition, "human-inquisition-takedown-shot", Play.Shoots, ["Takedown Shot"], [], [],
                [OnceAtTwoPlus("Takedown Shot", "\"Once per game, when this model shoots, it may make X extra attacks against the target at Quality 2+\" (X 1)")],
                carriers: [new ScenarioModel(1, 1, [R(1, "Takedown Shot", 1)], Weapons: [Gun(1, [], 60), TakedownWeapon("Takedown Shot", 60), Blade(1)])], units: 1, more: ArmyGames.TakedownShotTogether).In36(),
            Game(Books36.RatmenClans, "ratmen-clans-takedown-strike", Play.Charges, ["Takedown Strike"], [], [],
                [OnceAtTwoPlus("Takedown Strike", "\"Once per game, when it's this model's turn to strike in melee, it may make X attacks at Quality 2+\" (X 1)")],
                carriers: [new ScenarioModel(1, 1, [R(1, "Takedown Strike", 1)], Weapons: [Blade(1), TakedownWeapon("Takedown Strike", null)])], units: 1, more: ArmyGames.TakedownStrikeUsed).In36(),
            IndirectAuraH(),
            // Only the opponents move, so every melee is a charge at the Buff's unit, which buffs itself each activation; every counter Ask is answered strike.
            Pick(Books36.VoidspaceStalkers, "voidspace-stalkers-counter-in-melee-buff", Play.Charged, "Counter in Melee Buff",
                [CounterBuffH("Counter in Melee Buff", BuffText + "Counter in Melee.\": 3.6.0 p.13 Counter, \"May strike first with this weapon when charged\"")],
                enemyUnits: 2, grants: false,
                more: s => s.Match("counter", (ask, _) => ask.Legal is Options o && o.Choices.Any(c => c.Id == "strike") ? new Chosen("strike") : null)).In36(),
        ];

        /// <summary>
        /// Indirect through the Aura over two rounds, with no model moving: Daemons of Change unit 1 (one model carrying the Aura, a 36" gun with no rule, at
        /// (6, 22)) faces Opponents unit 1 (one Tough(6) model at (6, 2)) across a 4"-tall sight-blocking wall (x 4–8, y 11.5–12.5). It Holds in round 1 and
        /// Advances without moving in round 2; the opponents hold with no gun. 3.5.1's Indirect lets it shoot after the Advance too.
        /// </summary>
        static TestGame IndirectAuraH()
        {
            const string name = "daemons-of-change-indirect-when-shooting-aura", entry = "Indirect when Shooting Aura", red = "Daemons of Change unit 1", blue = "Opponents unit 1";
            var script = AnswerScript.Build()
                .Match(AskQuestions.Activate, (ask, p) => p.Scheduler.Round == 1
                    ? AskPicks.Activate(ask, red, "Hold") ?? AskPicks.Activate(ask, blue, "Hold")
                    : AskPicks.Activate(ask, red, "Advance") ?? AskPicks.Activate(ask, blue, "Hold"))
                .Match("shoot_or_not", (ask, _) => ask.Legal is Options o && o.Choices.Any(c => c.Id == "shoot") ? new Chosen("shoot") : null)
                .Match("move", (ask, p) => ask.Legal is MoveRegion region && Scripts36.UnitOf(p, region) == red ? new Destinations([]) : null)
                .Match("declare_targets", (ask, p) => AskPicks.AllAt(ask, p, blue))
                .Build();
            var scenario = new ScenarioBuilder(name, 1).RoundCap(2).Table(36, 24).Scripted(script)
                .Army("Daemons of Change", 1, 1, modelRules: [R(1, entry)], armyBook: "04z57ua0bwth37zh",
                    weapons: [new ScenarioWeapon("Mortar", new Dictionary<string, string> { ["Range"] = "36\"", ["Attacks"] = "1" }, [])], unitStats: Stats(4, 4),
                    anchors: new Dictionary<int, Point2> { [1] = new(6, 22) })
                .Army("Opponents", 1, 1, modelRules: [R(1, "Tough", 6)], weapons: [Blade(1)], unitStats: Stats(4, 6), anchors: new Dictionary<int, Point2> { [1] = new(6, 2) })
                .Terrain("wall", new Point3(6, 12, 0), volumes:
                    [new Volume(VolumeKind.Solid, Footprint.Rect(4, 1), new Point2(0, 0), new Yaw(0), 0, 4, TerrainDeclarations.BlocksLineOfSight, null)])
                .Build();
            var check = GameCheck.Each($"{entry}: \"This model and its unit get Indirect on its weapons when shooting.\": 3.6.0 p.14 Indirect, \"When using Hold actions and shooting, may target enemies that are not in line of sight as if in line of sight\" (after an Advance, the unit behind the wall is no target)", run =>
            {
                var log = LogQueries.Of(run);
                var hidden = log.UnitNamed(blue);
                var asks = log.AsksOf("declare_targets").Where(a => a.Frame is { } f && f.Context.ActorUnitId == log.UnitNamed(red)).ToList();
                var reasons = new List<string>();
                if (!asks.Any(a => a.Frame!.Nearest("hold", "advance")?.Sequence == "hold" && a.Ask.Legal is Assignment t && t.Targets.Contains(hidden)))
                    reasons.Add("holding, it was not offered the unit behind the wall");
                var shots = log.FramesOf("attack-sequence").Where(f => f.Context.ActorUnitId == log.UnitNamed(red) && f.Context.TargetUnitIds.Contains(hidden))
                    .Select(f => f.Nearest("hold", "advance")?.Sequence).ToList();
                if (!shots.Contains("hold"))
                    reasons.Add("holding, it did not shoot the unit behind the wall");
                if (shots.Contains("advance")
                    || asks.Any(a => a.Frame!.Nearest("hold", "advance")?.Sequence == "advance" && a.Ask.Legal is Assignment t && t.Targets.Contains(hidden)))
                    reasons.Add("after an Advance, it was offered or shot the unit behind the wall");
                return reasons;
            }, [entry]);
            return (new TestGame(name, ArmyGroup(RuleSets.Gf351, "04z57ua0bwth37zh"), scenario, [entry], [RollOff], [check])).In36();
        }
    }
}
