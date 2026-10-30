using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>
/// The engine's 3.6 readings of the reserve rules and Hazardous, grouped army:3.6:&lt;book uid&gt;: Spawn's unit seizes and contests nothing on the
/// round it is placed; Reinforcement's copy of a destroyed unit stands fully within 9" of a table edge and over 9" from enemy units; Hazardous's 1 to hit wounds
/// the model that rolled it, X wounds with Blast(X).
/// </summary>
public static partial class SuiteCatalog
{
    private static partial class ArmyGames
    {
        /// <summary>The Spawn model's anchor and the marker 5" east of it: out of the carrier's 3", under the spawned models placed 4.5" east of it.</summary>
        static readonly Point2 SpawnAt = new(8, 32);

        public static IReadOnlyList<TestGame> Army36F =>
        [
            Game(Books36.RobotLegions, "robot-legions-spawn-objectives", Play.Shoots, ["Spawn"], [], [],
                [NoObjectivesOnPlacedRound("Spawn", "\"Units placed via Spawn can’t seize or contest objectives on the round they are placed\"")],
                carriers: [new ScenarioModel(1, 1, [R(1, "Spawn")])], units: 2, sizes: new Dictionary<int, int> { [1] = 1, [2] = 3 }, reserves: [new ScenarioReserve(2, "Spawn", 1, 1)],
                anchors: new Dictionary<int, Point2> { [1] = SpawnAt },
                board: b => b.Marker(MarkerKind.Objective, new Point3(SpawnAt.X + 5, SpawnAt.Y, 0), label: "Spawn"),
                more: s => s.Match(AskQuestions.React, (ask, _) => Answers.AskPicks.Option(ask, c => c.Id.StartsWith("Spawn|", StringComparison.Ordinal)))
                    .Match("place", (ask, p) => ask.Legal is MoveRegion { Containment.ExitingId: { } at } m && p.Models[at].Location is OnTable { Placement: var c }
                        ? new Destinations(m.ModelIds.Select((id, i) => new ModelRelocation(id, new OnShelf(),
                            new OnTable(new Placement(new Point3(c.Position.X + 4.5, c.Position.Y + (i - 1) * 1.5, 0), new Yaw(0))))).ToList())
                        : null)).In36(),
            // By construction: the opponent's one rifle (three attacks) fixed to kill two of the three rats, whose end-of-activation morale test (p.10, half
            // or less) is fixed to fail into Shaken; the removal is answered remove and the copy picked at round two's start.
            With(Game(Books36.RatmenClans, "ratmen-clans-reinforcement-36", Play.Shot, ["Reinforcement"], [R(1, "Reinforcement")],
                [Fix("hit", 6, 6, 1), Fix("defense", 1, 1), new DiceFix("morale", 0, [1], new RollKey(Actor: $"{Books36.RatmenClans.Army} unit 1"))],
                [Reinforced36("\"When a unit where all models have this rule is Shaken, you may remove it from the table as destroyed. At the beginning of the next round after a unit where all models have this rule is destroyed, after Ambushers have been deployed, you may place a new copy of it fully within 9\" of any table edge, over 9\" away from enemy units\"")],
                units: 2, sizes: new Dictionary<int, int> { [1] = 3, [2] = 3 }, reserves: [new ScenarioReserve(2, "Reinforcement", 1)],
                enemyUnits: 1, enemyModels: 1, enemyAttacks: 3, rounds: 2,
                more: s => s.Match(GfReserves.RemoveQuestion, (_, _) => new Chosen("remove"))
                    .Match(AskQuestions.DeployReinforcement, (ask, _) => ask.Legal is PieceSelection { Eligible: [var copy, ..] } ? new SelectedPieces([copy]) : null)),
                ("Reinforcement", Deployed(AskQuestions.DeployReinforcement))).In36(),
            Game(Books36.RatmenClans, "ratmen-clans-hazardous-blast", Play.Shoots, ["Hazardous"], [], [],
                [HazardOnTheRoller("Hazardous", 3, "\"on unmodified results of 1 to hit this model takes one wound, or X wounds if this weapon has the Blast(X)/Deadly(X) rule\"")],
                weapon: [R(0, "Hazardous"), R(0, "Blast", 3)], units: 2).In36(),
        ];

        /// <summary>
        /// At the end of the round a unit was placed by its rule (a `place-reserve` frame for it that round), each marker one of its models certainly stands within
        /// 3" of (base edge to the marker's centre, a margin either side) while no other model on the table might: its control does not change.
        /// </summary>
        static GameCheck NoObjectivesOnPlacedRound(string entry, string quote) =>
            Exact(entry, quote, (run, _) =>
            {
                var log = LogQueries.Of(run);
                var verdicts = new List<string?>();
                var control = new Dictionary<Guid, Guid?>();
                // A round end writes round_ended only when control changes; otherwise the next round_started (or game_ended) closes it unchanged.
                var ends = new List<(long Seq, RoundEnded Ended)>();
                var round = 0;
                foreach (var e in log.Events)
                    switch (e.Payload)
                    {
                        case RoundEnded r: ends.Add((e.Seq, r)); break;
                        case RoundStarted r when round >= 1 && ends.All(x => x.Ended.Round != round): ends.Add((e.Seq, new RoundEnded(round, []))); round = r.Round; break;
                        case RoundStarted r: round = r.Round; break;
                        case GameEnded when round >= 1 && ends.All(x => x.Ended.Round != round): ends.Add((e.Seq, new RoundEnded(round, []))); break;
                    }
                foreach (var (seq, ended) in ends)
                {
                    var e = (Seq: seq, Payload: ended);
                    var placed = log.FramesOf(GfReserves.PlaceReserveSeq).Where(f => f.Round == ended.Round && f.Context.Action == entry).Select(f => f.Context.TargetUnitIds[0]).ToHashSet();
                    var at = log.LocationsBefore(e.Seq);
                    foreach (var marker in run.Final.Board.Table.Markers.Where(m => m.Kind == MarkerKind.Objective))
                    {
                        var before = control.GetValueOrDefault(marker.Id);
                        var after = ended.Changes.OfType<ObjectiveControlSet>().LastOrDefault(c => c.MarkerId == marker.Id) is { } set ? set.TeamId : before;
                        control[marker.Id] = after;
                        double Edge(Guid m) => Distance.EdgeToEdge(new PlacedModel(new ModelBody(run.Final.Models[m].Slot.Base, run.Final.Models[m].Slot.Height), ((OnTable)at[m]).Placement), marker.Position.Horizontal);
                        var standing = at.Where(kv => kv.Value is OnTable).Select(kv => kv.Key).ToList();
                        if (!standing.Any(m => placed.Contains(log.UnitOf(m)) && Edge(m) <= 3 - 0.01)) continue;
                        if (standing.Any(m => !placed.Contains(log.UnitOf(m)) && Edge(m) <= 3 + 0.01)) continue;
                        verdicts.Add(after == before ? null : $"round {ended.Round} marker {marker.Label}: control changed with only a unit placed that round within 3\"");
                    }
                }
                return verdicts;
            });

        /// <summary>
        /// Each removal offer (`reinforcement_remove`): made to a Shaken unit; answered remove, its models leave the table, and its copy arrives at a later
        /// round's start in one event, every base fully within 9" of one table edge and over 9" from every enemy model on the table. No offer is a failure.
        /// </summary>
        static GameCheck Reinforced36(string quote) =>
            Exact(GfReserves.Reinforcement, quote, (run, _) =>
            {
                var log = LogQueries.Of(run);
                PlacedModel Body(Guid m, Placement p) => new(new ModelBody(run.Final.Models[m].Slot.Base, run.Final.Models[m].Slot.Height), p);
                var offers = log.AsksOf(GfReserves.RemoveQuestion).ToList();
                if (offers.Count == 0) return ["no removal offered to a Shaken unit"];
                var arrivals = Arrivals(run, u => run.Final.Units[u].Definition.Reserve?.Property == GfReserves.Reinforcement).ToList();
                var verdicts = new List<string?>();
                foreach (var offer in offers)
                {
                    if (offer.Frame?.Context.ActorUnitId is not { } original || !log.HasCondition(original, GfNames.Shaken, offer.Seq)) { verdicts.Add("removal offered to a unit not Shaken"); continue; }
                    if (offer.Answer is not Chosen { OptionId: "remove" }) continue;
                    if (log.Living(original, log.Events[^1].Seq + 1) > 0) { verdicts.Add("removed, yet its models stand"); continue; }
                    var copy = run.Final.Units.Values.FirstOrDefault(u => u.Definition.Reserve is { Property: GfReserves.Reinforcement } r && r.Of == original)?.Id;
                    var mine = arrivals.Where(a => copy is { } c && run.Final.Models[a.Model].UnitId == c).ToList();
                    if (mine.Count == 0) { verdicts.Add("its copy never arrived"); continue; }
                    if (mine.Select(a => a.Seq).Distinct().Count() > 1) { verdicts.Add("placed in more than one event"); continue; }
                    var seq = mine[0].Seq;
                    if (seq <= offer.Seq || mine[0].Round <= (log.Events.Where(e => e.Seq <= offer.Seq).Select(e => e.Payload).OfType<RoundStarted>().LastOrDefault()?.Round ?? 0)) { verdicts.Add("arrived before the round after its removal"); continue; }
                    var before = log.LocationsBefore(seq);
                    var team = run.Final.Units[copy!.Value].ParticipantId;
                    var enemies = before.Where(kv => kv.Value is OnTable && run.Final.Units[log.UnitOf(kv.Key)].ParticipantId != team).Select(kv => Body(kv.Key, ((OnTable)kv.Value).Placement)).ToList();
                    foreach (var (model, at, _, _) in mine)
                    {
                        var self = Body(model, new Placement(new Point3(at.X, at.Y, 0), new Yaw(0)));
                        verdicts.Add(!GfReserves.NearEdge(run.Game.Scenario.Table, self, 9) ? "placed beyond 9\" of every edge"
                            : enemies.Any(e => Distance.EdgeToEdge(self, e) <= 9 + 0.01) ? "placed within 9\" of an enemy model" : null);
                    }
                }
                return verdicts;
            });

        /// <summary>
        /// Each attack with the rule that rolled a 1 to hit: the attack's self wounds are X per unmodified 1, and the deal-wounds under it asks no allocation and
        /// wounds only the models whose own hit dice (the roll-to-hit record that names them) showed a 1.
        /// </summary>
        static GameCheck HazardOnTheRoller(string entry, int x, string quote) =>
            Exact(entry, quote, (run, _) =>
            {
                var log = LogQueries.Of(run);
                var weapons = run.Final.Models.Values.SelectMany(m => m.Slot.Weapons).Where(w => w.Properties.Any(p => p.Name == entry)).Select(w => w.Id).ToHashSet();
                var verdicts = new List<string?>();
                foreach (var attack in log.FramesOf(GfNames.AttackSequence).Where(f => f.Context.WeaponIds.Any(weapons.Contains)))
                {
                    if (attack.Steps.FirstOrDefault(s => s.Name == "roll-to-hit") is not { } hit || attack.Steps.FirstOrDefault(s => s.Name == "score-hits") is not { } score) continue;
                    var ones = new Dictionary<Guid, int>();
                    for (var i = 0; i < hit.AllRolls.Count; i++)
                    {
                        var key = hit.Step.Values?.Keys.FirstOrDefault(k => k.StartsWith($"hit_model_{i}_", StringComparison.Ordinal));
                        var rolled = hit.AllRolls[i].Raw(1);
                        if (rolled == 0) continue;
                        if (key is null) { verdicts.Add("a 1 on a hit record that names no model"); continue; }
                        var model = Guid.ParseExact(key[$"hit_model_{i}_".Length..], "N");
                        ones[model] = ones.GetValueOrDefault(model) + rolled;
                    }
                    if (ones.Count == 0) continue;
                    var total = ones.Values.Sum();
                    verdicts.Add(score.Value("self_wounds") == x * total ? null : $"{score.Value("self_wounds")} self wounds for {total} ones, expected {x * total}");
                    foreach (var deal in log.Children(attack).Where(f => f.Sequence == GfNames.DealWounds && f.Context.ActorUnitId is null))
                    {
                        if (log.Asks.Any(a => a.Frame == deal)) verdicts.Add("the self wounds asked an allocation");
                        var took = deal.Steps.Where(s => s.Name == "assign").SelectMany(s => s.Step.Values?.Keys ?? []).Where(k => k.StartsWith("took_", StringComparison.Ordinal))
                            .Select(k => Guid.ParseExact(k["took_".Length..], "N"));
                        if (took.Any(m => !ones.ContainsKey(m))) verdicts.Add("a model that rolled no 1 took a self wound");
                    }
                }
                return verdicts;
            });
    }
}
