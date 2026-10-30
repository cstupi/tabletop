using Tabletop.Geometry;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.Protocol;

namespace Tabletop.Sim.Suite;

/// <summary>
/// Readings of a finished game of three or more teams against rulebook p.50. Free-for-all (3-4 players): "All players roll-off to determine
/// the order in which they will deploy. Then the players alternate in picking a table corner and deploying one unit each within a 2'x1' area,
/// starting with the player that got the highest score, and then alternating in descending order." "During the first round, turn order goes
/// from the player that deployed first to the one that deployed last. Starting from the second round, turn order goes from the player that
/// finished activating first to the one that finished last." Team Games: "players from the same team must deploy their units in the same
/// deployment zone, placing a single unit when it's their team's turn to deploy"; "for each team's turn only one of the players may activate
/// a single unit"; "when checking who won, all objective markers and deployment zones are shared between players of the same team".
/// p.6: "the player that controls most markers wins". Each returns null or what did not hold. They read the log; none computes a rule.
/// </summary>
public static class FreeForAllChecks
{
    /// <summary>The deployment order: round 0's round_started, which the roll-off ordered (scheduler: the first round_started carries the roll-off's order).</summary>
    public static IReadOnlyList<Guid> DeploymentOrder(LogQueries log) =>
        log.Events.Select(e => e.Payload).OfType<RoundStarted>().FirstOrDefault()?.Order ?? [];

    /// <summary>"All players roll-off": one die for each of the teams in the first roll, and the order the roll-off gave ranks every team once.</summary>
    public static string? RollOff(LogQueries log, int teams)
    {
        var first = log.Rolls(GfNames.RollOff).FirstOrDefault();
        if (first is null) return "no roll-off was rolled";
        if (first.Dice.Count != teams) return $"the roll-off's first roll has {first.Dice.Count} dice for {teams} teams";
        var order = DeploymentOrder(log);
        return order.Count == teams && order.Distinct().Count() == teams ? null : $"the deployment order ranks {order.Distinct().Count()} of {teams} teams";
    }

    /// <summary>"alternating in descending order", one unit a turn: each deploy pick is the next team in the roll-off order that still has a unit to place.</summary>
    public static string? DeploymentRotation(LogQueries log)
    {
        var order = DeploymentOrder(log);
        var picks = log.Deployments.Where(d => d.Question == AskQuestions.Deploy).ToList();
        return Rotation(order, picks.Select(p => p.Team).ToList(), "deployment");
    }

    /// <summary>
    /// "deploying one unit each within a 2'x1' area" in their own corner, teammates in "the same deployment zone": every model on the table when
    /// round one starts stands fully within its team's zone (`ordinals` maps team id to ordinal), or, with no ordinals, within one zone its team
    /// holds alone.
    /// </summary>
    public static string? DeployedInZones(LogQueries log, IReadOnlyDictionary<Guid, int>? ordinals = null, ZoneLegs? legs = null)
    {
        var table = log.Final.Board.Table;
        var teamOf = log.Deployments.Where(d => d.Question == AskQuestions.Deploy).ToDictionary(d => d.Unit, d => d.Team);
        var roundOne = log.Events.FirstOrDefault(e => e.Payload is RoundStarted { Round: 1 });
        if (roundOne is null) return "round one never started";
        var zoneOf = new Dictionary<Guid, string>();
        foreach (var (modelId, at) in log.LocationsBefore(roundOne.Seq))
        {
            if (at is not OnTable { Placement: var placement }) continue;
            var model = log.Final.Models[modelId];
            if (!teamOf.TryGetValue(model.UnitId, out var team)) return $"{model.Slot.Name} stands on the table without a deploy pick";
            var placed = new PlacedModel(new ModelBody(model.Slot.Base, model.Slot.Height), placement);
            var zones = ordinals is null ? table.Zones : table.Zones.Where(z => z.TeamOrdinal == ordinals.GetValueOrDefault(team)).ToList();
            if (zones.Count == 0) return $"team {ordinals?.GetValueOrDefault(team)} has no deployment zone";
            var within = zones.FirstOrDefault(z => ZoneStyles.BaseFullyWithin(z.Shape, table.Size, placed, legs));
            if (within is null) return $"{model.Slot.Name} stands outside its team's deployment zone at round one";
            if (zoneOf.TryGetValue(team, out var held) && held != within.Shape) return $"{model.Slot.Name} stands in {within.Shape}, its team in {held}";
            zoneOf[team] = within.Shape;
        }
        var shared = zoneOf.GroupBy(kv => kv.Value).FirstOrDefault(g => g.Count() > 1);
        return shared is null ? null : $"{shared.Count()} teams deployed in {shared.Key}";
    }

    /// <summary>Round one's order is the deployment order; each later round's goes from the team that finished activating first to the one that finished last.</summary>
    public static string? TurnOrder(LogQueries log)
    {
        var deployment = DeploymentOrder(log);
        IReadOnlyList<Guid>? previous = null;
        var lastTurnEnd = new Dictionary<Guid, long>();
        var round = 0;
        foreach (var e in log.Events)
        {
            if (e.Payload is TurnEnded t && round >= 1) lastTurnEnd[t.TeamId] = e.Seq;
            if (e.Payload is not RoundStarted { Round: >= 1 } r) continue;
            if (r.Round == 1 && !r.Order.SequenceEqual(deployment))
                return $"round one's order is not the deployment order";
            if (r.Round > 1 && previous is not null)
            {
                var finished = previous.OrderBy(team => lastTurnEnd.GetValueOrDefault(team)).ToList();
                if (!r.Order.SequenceEqual(finished)) return $"round {r.Round}'s order is not the order the teams finished activating in round {round}";
            }
            previous = r.Order; round = r.Round; lastTurnEnd.Clear();
        }
        return round >= 1 ? null : "no round started";
    }

    /// <summary>"for each team's turn only one of the players may activate a single unit": in every round the turns go round the round's order, a team skipped only when it has no turn left that round.</summary>
    public static string? TurnRotation(LogQueries log)
    {
        var round = 0; IReadOnlyList<Guid> order = [];
        var turns = new List<Guid>();
        foreach (var e in log.Events.Append(null))
        {
            if (e?.Payload is TurnStarted t && round >= 1) turns.Add(t.TeamId);
            if (e is not null && e.Payload is not RoundStarted) continue;
            if (round >= 1 && Rotation(order, turns, $"round {round}'s turns") is { } broken) return broken;
            if (e?.Payload is RoundStarted r) { round = r.Round; order = r.Order; turns.Clear(); }
        }
        return null;
    }

    /// <summary>In every round each team with a model on the table when the round started took a turn, and four rounds were played.</summary>
    public static string? EveryTeamActivated(LogQueries log)
    {
        var teamOf = log.Deployments.GroupBy(d => d.Unit).ToDictionary(g => g.Key, g => g.First().Team);
        var starts = log.Events.Where(e => e.Payload is RoundStarted { Round: >= 1 }).ToList();
        if (starts.Count < 4) return $"only {starts.Count} round(s) were played";
        for (var i = 0; i < starts.Count; i++)
        {
            var from = starts[i].Seq; var to = i + 1 < starts.Count ? starts[i + 1].Seq : long.MaxValue;
            var turned = log.Events.Where(e => e.Seq > from && e.Seq < to).Select(e => e.Payload).OfType<TurnStarted>().Select(t => t.TeamId).ToHashSet();
            var standing = log.LocationsBefore(from).Where(kv => kv.Value is OnTable).Select(kv => teamOf.GetValueOrDefault(log.UnitOf(kv.Key))).ToHashSet();
            if (((RoundStarted)starts[i].Payload).Order.FirstOrDefault(t => standing.Contains(t) && !turned.Contains(t)) is var idle && idle != Guid.Empty)
                return $"team {((RoundStarted)starts[i].Payload).Order.ToList().IndexOf(idle) + 1} of round {i + 1}'s order stood on the table and took no turn";
        }
        return null;
    }

    /// <summary>"the player that controls most markers wins": game_ended names every team holding the most markers at the end (all of them, a tie, when none holds one).</summary>
    public static string? Winners(LogQueries log)
    {
        if (log.Final.Ended is not { } ended) return "the game did not end";
        var held = DeploymentOrder(log).Select(team => (Team: team, Count: log.Final.Control.Values.Count(t => t == team))).ToList();
        var most = held.Where(h => h.Count == held.Max(x => x.Count)).Select(h => h.Team).ToHashSet();
        return most.SetEquals(ended.Winners) ? null
            : $"game_ended names {ended.Winners.Count} winner(s); {most.Count} team(s) hold the most markers ({string.Join(", ", held.Select(h => h.Count))})";
    }

    /// <summary>Each entry is the team after the one before it in the cyclic order (the first is the order's first) that still has an entry to come.</summary>
    static string? Rotation(IReadOnlyList<Guid> order, IReadOnlyList<Guid> teams, string what)
    {
        if (order.Count == 0) return teams.Count == 0 ? null : $"{what}: no order";
        var at = -1;
        for (var i = 0; i < teams.Count; i++)
        {
            var remaining = teams.Skip(i).ToHashSet();
            var expected = Enumerable.Range(1, order.Count).Select(k => order[(at + k + order.Count) % order.Count]).First(remaining.Contains);
            if (teams[i] != expected) return $"{what}: turn {i + 1} went to team {Index(order, teams[i])} of the order, not {Index(order, expected)}";
            at = Index(order, teams[i]) - 1;
        }
        return null;
    }

    static int Index(IReadOnlyList<Guid> order, Guid team) => order.ToList().IndexOf(team) + 1;
}
