using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    /// <summary>Batch 3 (army-book rules spec → Authored rules 3): the deployment rules.</summary>
    private static partial class ArmyGames
    {
        static Evidence Deployed(string question, int? inRound = null) => log =>
            Rounds(log).Any(x => x.Payload is AskClosed c && x.Ask(c.AskId) == question && (inRound is null || x.Round == inRound) && c.Answers.Any(a => a.Answer is SelectedPieces));

        delegate bool Evidence(IReadOnlyList<GameEvent> log);

        /// <summary>The effective log's events with the round each falls in, and the question of each Ask opened so far.</summary>
        static IEnumerable<(EventPayload Payload, int Round, Func<Guid, string?> Ask)> Rounds(IReadOnlyList<GameEvent> log)
        {
            var round = 0;
            var asks = new Dictionary<Guid, string>();
            foreach (var e in EventApplier.EffectiveLog(log))
            {
                if (e.Payload is RoundStarted r) round = r.Round;
                if (e.Payload is AskOpened o) asks[o.Ask.Id] = o.Ask.Question;
                yield return (e.Payload, round, id => asks.GetValueOrDefault(id));
            }
        }

        static TestGame With(TestGame game, params (string Entry, Evidence Evidence)[] evidence) =>
            game with { Evidence = evidence.ToDictionary(x => x.Entry, x => (Func<IReadOnlyList<GameEvent>, bool>)(log => x.Evidence(log))) };

        /// <summary>Each model of the named units placed from the shelf once rounds began: where it landed, the seq and the round.</summary>
        static IEnumerable<(Guid Model, Point2 At, long Seq, int Round)> Arrivals(SuiteGameRun run, Func<Guid, bool> unit)
        {
            var round = 0;
            foreach (var e in EventApplier.EffectiveLog(run.EffectiveLog))
            {
                if (e.Payload is RoundStarted r) round = r.Round;
                if (round == 0) continue;
                foreach (var m in LogQueries.ChangesOf(e.Payload).OfType<ModelsMoved>().SelectMany(m => m.Moves))
                    if (m.From is OnShelf && m.To is OnTable t && unit(run.Final.Models[m.ModelId].UnitId)) yield return (m.ModelId, t.Placement.Position.Horizontal, e.Seq, round);
            }
        }

        /// <summary>The nearest base-to-base gap from the model, at that point, to any model of the units the filter names on the table just before the seq.</summary>
        static double Gap(SuiteGameRun run, LogQueries log, Guid model, Point2 at, long seq, Func<Guid, bool> unit)
        {
            PlacedModel Body(Guid m, Placement p) => new(new ModelBody(run.Final.Models[m].Slot.Base, run.Final.Models[m].Slot.Height), p);
            var self = Body(model, new Placement(new Point3(at.X, at.Y, 0), new Yaw(0)));
            return log.LocationsBefore(seq).Where(kv => kv.Value is OnTable && unit(run.Final.Models[kv.Key].UnitId))
                .Select(kv => Distance.EdgeToEdge(self, Body(kv.Key, ((OnTable)kv.Value).Placement))).DefaultIfEmpty(double.PositiveInfinity).Min();
        }

        static Func<Guid, bool> Named(SuiteGameRun run, string prefix) => u => run.Final.Units[u].Definition.Name.StartsWith(prefix, StringComparison.Ordinal);

        /// <summary>Every arrival of the holder's units is over the stated distance from every enemy model.</summary>
        static GameCheck ArrivesOver(string entry, string army, double inches, string quote) =>
            Exact(entry, quote, (run, _) =>
            {
                var log = LogQueries.Of(run);
                return Arrivals(run, u => Holds(run, u, entry)).Select(a => Gap(run, log, a.Model, a.At, a.Seq, u => !Named(run, army)(u)) is var gap && gap > inches ? null : $"placed {gap:0.00}\" from an enemy");
            });

        /// <summary>Repel Ambushers: every enemy Ambusher's arrival is over 12" from the holder's unit.</summary>
        static GameCheck RepelledOver(string quote) =>
            Exact("Repel Ambushers", quote, (run, _) =>
            {
                var log = LogQueries.Of(run);
                return Arrivals(run, Named(run, "Opponents"))
                    .Select(a => Gap(run, log, a.Model, a.At, a.Seq, u => Holds(run, u, "Repel Ambushers")) is var gap && gap > 12 ? null : $"an Ambusher placed {gap:0.00}\" from the unit");
            });

        /// <summary>Ambush Beacon: every Ambusher arrival of the holder's side within 9" of an enemy is within 6" of a friendly beacon.</summary>
        static GameCheck NearBeacon(string army, string quote) =>
            Exact("Ambush Beacon", quote, (run, _) =>
            {
                var log = LogQueries.Of(run);
                return Arrivals(run, Named(run, army)).Where(a => Gap(run, log, a.Model, a.At, a.Seq, u => !Named(run, army)(u)) <= 9)
                    .Select(a => Gap(run, log, a.Model, a.At, a.Seq, u => Holds(run, u, "Ambush Beacon")) is var gap && gap <= 6 ? null : $"placed {gap:0.00}\" from the beacon within 9\" of an enemy");
            });

        /// <summary>A rule deploying a unit in round one: every arrival of the holder's units was in round one.</summary>
        static GameCheck ArrivesInRoundOne(string entry, string quote) =>
            Exact(entry, quote, (run, _) => Arrivals(run, u => Holds(run, u, entry)).Select(a => a.Round == 1 ? null : $"arrived in round {a.Round}"));

        /// <summary>Ambushing Piercing Shot: the holder's shooting blocks carry AP(1) in the round it arrived, and no AP in later rounds.</summary>
        static GameCheck PiercingOnArrival(string quote) =>
            Exact("Ambushing Piercing Shot", quote, (run, _) =>
            {
                var log = LogQueries.Of(run);
                var arrived = Arrivals(run, u => Holds(run, u, "Ambushing Piercing Shot")).GroupBy(a => run.Final.Models[a.Model].UnitId).ToDictionary(g => g.Key, g => g.Min(a => a.Round));
                return from s in log.Steps
                       where s.Name == "roll-to-block" && s.Frame.Nearest("shooting") is not null && s.Frame.Context.ActorUnitId is { } u && arrived.ContainsKey(u)
                       from r in s.Rolls("defense")
                       let ap = r.Roll.Modifiers.Where(m => m.Source == "AP").Sum(m => m.Amount)
                       select (s.Frame.Round == arrived[s.Frame.Context.ActorUnitId!.Value]) == (ap == -1) ? null : $"AP {ap} in round {s.Frame.Round}";
            });

        /// <summary>Re-Deployment: at most two units of the holder's side are re-deployed, each placed again within its zone (round 0).</summary>
        static GameCheck DeployedAgain(string quote) =>
            Exact("Re-Deployment", quote, (run, _) =>
            {
                var again = LogQueries.Of(run).Conditions.Where(c => c.Condition == "re-deployed" && c.Present).Select(c => c.Subject.Id).ToList();
                return again.Count > 2 ? [$"{again.Count} units re-deployed"]
                    : again.Select(u => run.Final.Units[u].Definition.Models.All(m => run.Final.Models[m.Id].Location is not OnShelf) ? null : "a re-deployed unit was left off the table");
            });

        /// <summary>Ambush Re-Deployment: each unit it removed deploys as an Ambusher at the next round's start, when the game reaches it.</summary>
        static GameCheck ReturnsNextRound(string quote) =>
            Exact("Ambush Re-Deployment", quote, (run, _) =>
            {
                var log = LogQueries.Of(run);
                var last = log.Frames.Select(f => f.Round).DefaultIfEmpty(0).Max();
                return from f in log.FramesOf("ambush-redeploy")
                       where f.Round < last
                       let back = Arrivals(run, u => u == f.Context.ActorUnitId).Select(a => a.Round).Where(r => r > f.Round).DefaultIfEmpty(0).Min()
                       select back == f.Round + 1 ? null : back == 0 ? "never deployed again" : $"removed in round {f.Round}, back in round {back}";
            });

        /// <summary>Fanatic, Vanguard: each placement is offered after the unit is deployed (before round one) within 9".</summary>
        static GameCheck PlacedAfterDeploying(string entry, string quote) =>
            Exact(entry, quote, (run, _) =>
                from a in LogQueries.Of(run).Asks
                where a.Ask.Question == "move" && a.Frame is { Sequence: "reposition" } f && f.Context.Action == entry
                select a.Frame!.Round != 0 ? $"offered in round {a.Frame.Round}" : a.Ask.Legal is MoveRegion m && m.Allowance == 9 ? null : $"allowed {(a.Ask.Legal as MoveRegion)?.Allowance}\"");

        /// <summary>Surprise Attack: each use is in the unit's first activation, and its target takes one hit per 2+ of the X dice, blocked under AP(1).</summary>
        static GameCheck SurpriseHits(string quote) =>
            Exact("Surprise Attack", quote, (run, _) =>
            {
                var log = LogQueries.Of(run);
                return from f in log.FramesOf("pick-rule")
                       where f.Context.Action?.StartsWith("Surprise Attack|", StringComparison.Ordinal) == true
                       let roll = f.Steps.SelectMany(s => s.Rolls("Surprise Attack")).FirstOrDefault()
                       where roll is not null
                       let hits = log.Children(f).Where(c => c.Sequence == "rule-hits").ToList()
                       let blocks = hits.SelectMany(h => log.Descendants(h)).SelectMany(d => d.Steps).Where(s => s.Name == "roll-to-block").SelectMany(s => s.Rolls("defense")).ToList()
                       let activations = log.Activations.Count(a => a.Unit == f.Context.ActorUnitId && a.Seq <= f.Seq)
                       select activations != 1 ? $"used in activation {activations}"
                           : roll.Successes == 0 ? (hits.Count == 0 ? null : "hits without a success")
                           : hits.Count != 1 || hits[0].Context.Action != $"Surprise Attack|{roll.Successes}" ? $"{roll.Successes} successes dealt {string.Join(",", hits.Select(h => h.Context.Action))}"
                           : blocks.All(b => b.Roll.Modifiers.Any(m => m.Source == "AP" && m.Amount == -1)) ? null : "a block without AP(1)";
            });

        static TestGame Batch3Game((string Uid, string Army) book, string name, Play play, string entry, IReadOnlyList<ScenarioRule> rules, GameCheck check, (string, Evidence)? evidence = null,
            int units = 2, IReadOnlyList<int>? setAside = null, IReadOnlyList<ScenarioRule>? enemy = null, IReadOnlyList<int>? enemySetAside = null, IReadOnlyList<ScenarioModel>? carriers = null,
            IReadOnlyDictionary<int, Point2>? anchors = null, IReadOnlyDictionary<int, Point2>? enemyAnchors = null, double? length = null, Action<ScriptBuilder>? more = null)
        {
            var game = Game(book, name, play, [entry], rules, [], [check], units: units, setAside: setAside, enemy: enemy, enemySetAside: enemySetAside, carriers: carriers,
                anchors: anchors, enemyAnchors: enemyAnchors, length: length, more: more);
            return evidence is { } e ? With(game, e) : game;
        }

        const string Ambusher = AskQuestions.DeployAmbusher;

        public static IReadOnlyList<TestGame> Batch3 =>
        [
            Batch3Game(Robots, "robot-legions-infiltrate", Play.Shoots, "Infiltrate", [R(2, "Infiltrate")],
                ArrivesOver("Infiltrate", Robots.Army, 3, "\"Counts as having Ambush, but may be deployed anywhere over 3” away from enemy unit\""), ("Infiltrate", Deployed(Ambusher)), setAside: [2]),
            Batch3Game(Robots, "robot-legions-ambushing-piercing-shot", Play.Shoots, "Ambushing Piercing Shot", [R(2, "Ambushing Piercing Shot")],
                PiercingOnArrival("\"Counts as having Ambush, and its weapons get AP(+1) when shooting on the round in which it deploys via this rule\""), setAside: [2]),
            Batch3Game(Robots, "robot-legions-repel-ambushers", Play.Shoots, "Repel Ambushers", [R(1, "Repel Ambushers")],
                RepelledOver("\"Enemy units using Ambush must be set up over 12\" away from this model's unit\""), ("Repel Ambushers", Deployed(Ambusher)),
                units: 1, enemy: [R(1, "Tough", 3), R(2, "Ambush")], enemySetAside: [2]),
            Batch3Game(Saurians, "saurian-starhost-ambush-beacon", Play.Shoots, "Ambush Beacon", [R(2, "Ambush")],
                NearBeacon(Saurians.Army, "\"Friendly units using Ambush may ignore distance restrictions from enemies if they are deployed within 6\" of this model\""), ("Ambush Beacon", Deployed(Ambusher)),
                setAside: [2], carriers: [new ScenarioModel(1, 1, [R(1, "Ambush Beacon")])], anchors: new Dictionary<int, Point2> { [1] = new(16, 15) },
                enemyAnchors: new Dictionary<int, Point2> { [1] = new(16, 10.5) }, length: 26),
            Batch3Game(DarkBrothers, "dark-brothers-rapid-ambush", Play.Shoots, "Rapid Ambush", [R(2, "Rapid Ambush")],
                ArrivesInRoundOne("Rapid Ambush", "\"Counts as having Ambush, but may be deployed at the start of any round, including the first\""), ("Rapid Ambush", Deployed(Ambusher, 1)), setAside: [2]),
            Batch3Game(BattleBrothers, "battle-brothers-re-deployment", Play.Shoots, "Re-Deployment", [R(1, "Re-Deployment")],
                DeployedAgain("\"After all other units are deployed (excluding units that were set aside), you may remove up to two friendly units from the table and deploy them again\""),
                ("Re-Deployment", log => log.Any(e => e.Payload is ConditionSet { Condition: "re-deployed", Present: true }))),
            Batch3Game(Jesters, "elven-jesters-ambush-re-deployment", Play.Moves, "Ambush Re-Deployment", [R(1, "Ambush Re-Deployment")],
                ReturnsNextRound("\"you may immediately remove it from the table … and deploy it as if it had Ambush at the beginning of the next round\""),
                units: 1, more: UsePicks(Jesters.Army, 1, "Ambush Re-Deployment")),
            Batch3Game(Cults, "soul-snatcher-cults-fanatic", Play.Moves, "Fanatic", [R(1, "Fanatic")],
                PlacedAfterDeploying("Fanatic", "\"After this model is deployed, it may be placed anywhere fully within 9” of its position\""), units: 1, more: UsePicks(Cults.Army, 1, "Fanatic")),
            Batch3Game(Ratmen, "ratmen-clans-vanguard", Play.Moves, "Vanguard", [R(1, "Vanguard")],
                PlacedAfterDeploying("Vanguard", "\"After this model is deployed, it may be placed anywhere fully within 9” of its position\""), units: 1, more: UsePicks(Ratmen.Army, 1, "Vanguard")),
            Batch3Game(Inquisition, "human-inquisition-surprise-attack", Play.Charges, "Surprise Attack", [R(1, "Surprise Attack", 4)],
                SurpriseHits("\"The first time this unit is activated, pick one enemy unit within 6\" in line of sight, and roll X dice. For each 2+ it takes one hit with AP(1)\" (X 4)"),
                units: 1, anchors: new Dictionary<int, Point2> { [1] = new(16, 15) }, enemyAnchors: new Dictionary<int, Point2> { [1] = new(16, 10.5) },
                more: UsePicks(Inquisition.Army, 1, "Surprise Attack")),
        ];
    }
}
