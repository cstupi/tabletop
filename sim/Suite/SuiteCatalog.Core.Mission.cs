using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>Core games: the mission (p.6) and deployment (p.6, p.13, p.14).</summary>
internal static partial class CoreGames
{
    // ---- core-hold-the-markers -------------------------------------------------------------------------------------------------------

    /// <summary>p.6 the mission: three markers across the middle, every unit heading for the nearest one its team does not hold.</summary>
    public static TestGame HoldTheMarkers()
    {
        const string name = "core-hold-the-markers";
        var scenario = new ScenarioBuilder(name, seed: 1)
            .RoundCap(5).Table(48, 36)
            .Army("Red", 3, 3, weapons: [Rifle(), HandWeapon()])
            .Army("Blue", 3, 3, weapons: [Rifle(), HandWeapon()])
            .Marker(MarkerKind.Objective, new Point3(12, 18, 0), label: "West")
            .Marker(MarkerKind.Objective, new Point3(24, 18, 0), label: "Centre")
            .Marker(MarkerKind.Objective, new Point3(36, 18, 0), label: "East")
            .Seeking(ObjectiveGoal.Nearest)
            .Expect(new Expectation.MostMarkersWin())
            .Build();
        return Game(name, scenario,
        [
            Check("p.6: \"if a unit is within 3\" of a marker whilst no enemies are, then it counts as being seized\"; \"if units from both sides contest a marker at the end of a round, then it becomes neutral\"; p.10: Shaken units \"can't seize or contest objectives\"", log =>
            {
                var (seized, contested) = (0, 0);
                var control = new Dictionary<Guid, Guid?>();
                foreach (var e in log.Events)
                {
                    if (e.Payload is not RoundEnded ended) continue;
                    var at = log.LocationsBefore(e.Seq);
                    foreach (var marker in log.Run.Final.Board.Table.Markers.Where(m => m.Kind == MarkerKind.Objective))
                    {
                        // Each team: certainly within 3" (a margin either side), certainly not, or too close to call. Shaken units count for no one.
                        var near = at.Where(kv => kv.Value is OnTable && log.TeamOf(log.UnitOf(kv.Key)) is not null && !log.HasCondition(log.UnitOf(kv.Key), "shaken", e.Seq))
                            .Select(kv => (Team: log.TeamOf(log.UnitOf(kv.Key))!.Value,
                                Edge: Distance.EdgeToEdge(new PlacedModel(log.Body(kv.Key), ((OnTable)kv.Value).Placement), marker.Position.Horizontal)))
                            .GroupBy(x => x.Team)
                            .ToDictionary(g => g.Key, g => g.Any(x => x.Edge <= 3 - 0.01) ? true : g.All(x => x.Edge > 3 + 0.01) ? false : (bool?)null);
                        var before = control.GetValueOrDefault(marker.Id);
                        var after = ended.Changes.OfType<ObjectiveControlSet>().LastOrDefault(c => c.MarkerId == marker.Id) is { } set ? set.TeamId : before;
                        control[marker.Id] = after;
                        if (near.Values.Any(v => v is null)) continue;
                        var present = near.Where(kv => kv.Value == true).Select(kv => kv.Key).ToList();
                        Guid? expected = present.Count switch { 0 => before, 1 => present[0], _ => null };
                        if (present.Count == 1) seized++;
                        if (present.Count > 1) contested++;
                        if (after != expected) return $"round {ended.Round} marker {marker.Label}: {present.Count} team(s) within 3\", control went to {(after is null ? "nobody" : "a team")} not {(expected is null ? "nobody" : "a team")}";
                    }
                }
                return seized > 0 && contested > 0 ? null : $"{seized} seizure(s) and {contested} contest(s) in the game; both are needed";
            }),
            FinalRound(Enforcement.Strict),
        ]);
    }

    // ---- core-deployment -------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// p.6 deployment: the roll-off is fixed so Blue (team 2) wins, deploys first and takes the first turn. Red's third unit is a Scout and
    /// Blue's third an Ambusher (p.13, p.14); both are set aside before deployment.
    /// </summary>
    public static TestGame Deployment()
    {
        const string name = "core-deployment";
        var scenario = new ScenarioBuilder(name, seed: 1)
            .RoundCap(3).Table(48, 36)
            .Army("Red", 3, 3, unitRules: [new ScenarioRule(3, "Scout")], weapons: [Rifle(), HandWeapon()], setAside: [3])
            .Army("Blue", 3, 3, unitRules: [new ScenarioRule(3, "Ambush")], weapons: [Rifle(), HandWeapon()], setAside: [3])
            .Build();
        string Army(CoreLog log, Guid unit) => log.Run.Final.Units[unit].Definition.Name.Split(' ')[0];
        return Game(name, scenario,
        [
            Check("p.4: \"The player with the highest result wins the roll-off\"; p.6: \"the winner must start deploying their army first\"; p.7: the deployment roll-off winner \"takes the first turn\" (roll-off fixed to 2 for Red, 5 for Blue)", log =>
            {
                if (log.Edited("roll-off", 0) is not [{ Raw: 2 }, { Raw: 5 }, ..]) return "the roll-off was not fixed";
                var deployed = log.Deployments.ToList();
                if (deployed.Count == 0 || Army(log, deployed[0].Unit) != "Blue") return "Blue did not deploy first";
                var blue = log.TeamOf(deployed[0].Unit);
                return log.Rounds.FirstOrDefault(r => r.Round == 1)?.Order[0] == blue ? null : "Blue did not take the first turn";
            }),
            Check("p.6: \"Then the players continue alternating in placing one unit each, until all units have been deployed\"", log =>
            {
                var order = log.Deployments.Where(d => d.Question == AskQuestions.Deploy).Select(d => Army(log, d.Unit)).ToList();
                for (var i = 1; i < order.Count; i++)
                    if (order[i] == order[i - 1] && order.Skip(i).Any(a => a != order[i])) return $"deployment order {string.Join(", ", order)} did not alternate";
                return order.Count >= 4 ? null : "too few deployments";
            }),
            Check("p.6: each unit is placed \"fully within 12\" of their table edge\" (Red y 24–36, Blue y 0–12)", log =>
                Require(log.Deployments.Where(d => d.Question == AskQuestions.Deploy).ToList(), "nobody deployed", d =>
                {
                    var (low, high) = Army(log, d.Unit) == "Red" ? (24.0, 36.0) : (0.0, 12.0);
                    return log.FirstPlacement(d.Unit).All(p => p.Y >= low + ModelRadius - 1e-6 && p.Y <= high - ModelRadius + 1e-6) ? null : $"{log.Run.Final.Units[d.Unit].Definition.Name} was placed outside its zone";
                })),
            Check("p.14 Scout: \"After all other units are deployed, may be deployed anywhere fully within 12\" of their deployment zone\" (Red y 12–36)", log =>
                Require(log.Deployments.Where(d => d.Question == AskQuestions.DeployScouts).ToList(), "no Scout was deployed", d =>
                    log.Deployments.Any(o => o.Question == AskQuestions.Deploy && o.Seq > d.Seq) ? "a Scout deployed before the other units"
                    : log.FirstPlacement(d.Unit).All(p => p.Y >= 12 + ModelRadius - 1e-6) ? null : "a Scout stood outside 12\" of its zone"), "Scout"),
            Check("p.13 Ambush: \"At the start of any round after the first, may be deployed anywhere over 9\" away from enemy units\"", log =>
                Require(log.Deployments.Where(d => d.Question == AskQuestions.DeployAmbusher).ToList(), "no Ambusher was deployed", d =>
                {
                    var round = log.Rounds.LastOrDefault(r => log.Events.Any(e => e.Payload == r && e.Seq < d.Seq))?.Round ?? 0;
                    if (round < 2) return $"an Ambusher deployed in round {round}";
                    var enemies = log.EnemiesOf(d.Unit).SelectMany(u => log.Placed(u, d.Seq)).ToList();
                    return log.FirstPlacement(d.Unit).All(p => enemies.All(q => p.DistanceTo(q) - 2 * ModelRadius > 9)) ? null : "an Ambusher deployed within 9\" of an enemy";
                }), "Ambush"),
        ], covers: ["Scout", "Ambush"], dice: [new DiceFix("roll-off", 0, [2, 5])],
        evidence: new Dictionary<string, Func<IReadOnlyList<GameEvent>, bool>>
        {
            ["Scout"] = log => Answered(log, AskQuestions.DeployScouts),
            ["Ambush"] = log => Answered(log, AskQuestions.DeployAmbusher),
        });
    }

    /// <summary>A unit was picked at an Ask of that question: the scheduler's evidence for Scout and Ambush, which no step records.</summary>
    static bool Answered(IReadOnlyList<GameEvent> log, string question)
    {
        var asks = log.Select(e => e.Payload).OfType<AskOpened>().Where(o => o.Ask.Question == question).Select(o => o.Ask.Id).ToHashSet();
        return log.Select(e => e.Payload).OfType<AskClosed>().Any(c => asks.Contains(c.AskId) && c.Answers.Any(a => a.Answer is SelectedPieces { PieceIds.Count: > 0 }));
    }
}
