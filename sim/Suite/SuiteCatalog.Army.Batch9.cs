using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.Geometry;
using Tabletop.Sim.Answers;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>
    /// The rules the import completes (army-book rules spec → Readings, batches 3 and 7): Takedown Shot and Takedown Strike, whose attack is a weapon the
    /// import adds to the carrying model with the rule's profile and Limited; the scenarios carry that weapon as an import gives it.
    /// </summary>
    private static partial class ArmyGames
    {
        /// <summary>The import's weapon for a Takedown rule: "one … attack … at Quality 2+ with AP(2), Deadly(3), and Takedown", once per game.</summary>
        static ScenarioWeapon TakedownWeapon(string rule, int? range) =>
            new(rule, range is { } r ? new Dictionary<string, string> { ["Range"] = $"{r}\"", ["Attacks"] = "1" } : new Dictionary<string, string> { ["Attacks"] = "1" },
                [R(0, "AP", 2), R(0, "Deadly", 3), R(0, "Reliable"), R(0, "Takedown"), R(0, "Limited")]);

        /// <summary>The rule's weapon attacked at most once a game, each time rolling to hit at 2+.</summary>
        static GameCheck OnceAtTwoPlus(string entry, string quote) =>
            Exact(entry, quote, (run, _) =>
            {
                var weapons = run.Final.Models.Values.SelectMany(m => m.Slot.Weapons).Where(w => w.Name == entry).Select(w => w.Id).ToHashSet();
                var frames = LogQueries.Of(run).FramesOf(GfNames.AttackSequence).Where(f => f.Context.WeaponIds.Any(weapons.Contains)).ToList();
                return frames.GroupBy(f => f.Context.WeaponIds.First(weapons.Contains)).Select(g => g.Count() > 1 ? $"attacked {g.Count()} times" : null)
                    .Concat(frames.SelectMany(f => f.Steps).SelectMany(s => s.Rolls("hit")).Select(r => r.Roll.Threshold == 2 ? null : $"hit at {r.Roll.Threshold}+"))
                    .ToList();
            });

        /// <summary>
        /// Each placement of a reserve unit the entry places, from the log: every model put on the table from the shelf in one event, fully within 6" of the
        /// holding model (Spawn) or the unit's last model still standing (Split), or fully within 12" of a table edge after its original left the table
        /// (Reinforcement); and never before round one.
        /// </summary>
        static GameCheck Places(string entry, string quote) =>
            Exact(entry, quote, (run, _) =>
            {
                var log = LogQueries.Of(run);
                bool Reserve(Guid unit) => run.Final.Units[unit].Definition.Reserve?.Property == entry;
                PlacedModel Body(Guid m, Placement p) => new(new ModelBody(run.Final.Models[m].Slot.Base, run.Final.Models[m].Slot.Height), p);
                var verdicts = new List<string?>();
                verdicts.AddRange(log.Deployments.Where(d => d.Question != AskQuestions.DeployReinforcement && Reserve(d.Unit)).Select(_ => "a reserve unit was deployed"));
                foreach (var unit in Arrivals(run, Reserve).GroupBy(a => run.Final.Models[a.Model].UnitId))
                {
                    if (unit.Select(a => a.Seq).Distinct().Count() > 1) { verdicts.Add("placed in more than one event"); continue; }
                    var seq = unit.First().Seq;
                    var before = log.LocationsBefore(seq);
                    var of = run.Final.Units[unit.Key].Definition.Reserve!.Of;
                    var anchors = entry == GfReserves.Spawn ? [of]
                        : run.Final.Units.TryGetValue(of, out var original) ? original.Definition.Models.Select(m => m.Id).Where(m => before[m] is OnTable).ToList() : [];
                    foreach (var (model, at, _, _) in unit)
                    {
                        var self = Body(model, new Placement(new Point3(at.X, at.Y, 0), new Yaw(0)));
                        verdicts.Add(entry == GfReserves.Reinforcement
                            ? anchors.Count > 0 ? "placed while its original stood" : GfReserves.NearEdge(run.Game.Scenario.Table, self) ? null : "placed beyond 12\" of every edge"
                            : anchors.Any(a => before[a] is OnTable t && Containment.BaseFullyWithin(self, Body(a, t.Placement), GfReserves.Within + 0.01)) ? null : "placed beyond 6\" of its anchor");
                    }
                }
                return verdicts;
            });

        static Action<ScriptBuilder> Picks(string question, Func<Ask, Projection, AskAnswer?> pick) => s => s.Match(question, pick);

        public static IReadOnlyList<TestGame> Batch9 =>
        [
            Game(Inquisition, "human-inquisition-takedown-shot", Play.Shoots, ["Takedown Shot"], [], [],
                [OnceAtTwoPlus("Takedown Shot", "\"Once per game, when this model shoots, it may make one extra attack against the target at Quality 2+\"")],
                carriers: [new ScenarioModel(1, 1, [R(1, "Takedown Shot")], Weapons: [Gun(1, [], 60), TakedownWeapon("Takedown Shot", 60), Blade(1)])], units: 1),
            Game(Ratmen, "ratmen-clans-takedown-strike", Play.Charges, ["Takedown Strike"], [], [],
                [OnceAtTwoPlus("Takedown Strike", "\"Once per game, when it's this model's turn to attack in melee, it may make one attack at Quality 2+\"")],
                carriers: [new ScenarioModel(1, 1, [R(1, "Takedown Strike")], Weapons: [Blade(1), TakedownWeapon("Takedown Strike", null)])], units: 1),
            Game(Robots, "robot-legions-spawn", Play.Shoots, ["Spawn"], [], [],
                [Places("Spawn", "\"Once per game, when this model is activated, you may place a new unit of X fully within 6\" of it\"")],
                carriers: [new ScenarioModel(1, 1, [R(1, "Spawn")])], units: 2, sizes: new Dictionary<int, int> { [1] = 1, [2] = 3 }, reserves: [new ScenarioReserve(2, "Spawn", 1, 1)],
                more: Picks(AskQuestions.React, (ask, _) => AskPicks.Option(ask, c => c.Id.StartsWith("Spawn|", StringComparison.Ordinal)))),
            Game(ChangeDaemons, "wormhole-daemons-of-change-split", Play.Shot, ["Split"], [R(1, "Split")], [],
                [Places("Split", "\"When this unit is fully destroyed, you may place a new unit of X fully within 6\" of it before removing the last model\"")],
                units: 2, sizes: new Dictionary<int, int> { [1] = 1, [2] = 2 }, reserves: [new ScenarioReserve(2, "Split", 1)], enemyAttacks: 4),
            With(Game(Ratmen, "ratmen-clans-reinforcement", Play.Shot, ["Reinforcement"], [R(1, "Reinforcement")], [],
                [Places("Reinforcement", "\"place a new copy of it fully within 12\" of any table edge at the beginning of the next round after Ambushers have been deployed\"")],
                units: 2, sizes: new Dictionary<int, int> { [1] = 1, [2] = 1 }, reserves: [new ScenarioReserve(2, "Reinforcement", 1)], enemyAttacks: 4,
                more: Picks(AskQuestions.DeployReinforcement, (ask, _) => ask.Legal is PieceSelection { Eligible: [var copy, ..] } ? new SelectedPieces([copy]) : null)),
                ("Reinforcement", Deployed(AskQuestions.DeployReinforcement))),
        ];
    }
}
