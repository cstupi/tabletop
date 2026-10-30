using Tabletop.Content;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Scheduler;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>
/// p.6's mission: "At the end of each round, if a unit is within 3" of a marker whilst no enemies are, then it counts as being seized.
/// Markers stay under the player's control even if the unit moves away, but if units from both sides contest a marker at the end of a
/// round, then it becomes neutral again. After 4 rounds have been played, the game ends, and the player that controls most markers wins."
/// Shaken units (p.10), Aircraft (p.13) and units that deployed via Ambush this round (p.13) "can't seize or contest objectives".
/// "Both sides" is more than one team: p.50 plays a free-for-all by the regular rules, and every other team is an enemy.
/// "Within 3" of a marker" is from a model's base edge (p.5: "from/to the closest point of their bases") to the marker's centre (owner
/// ruling, 2026-10-25: "Within 3" of the CENTER of the marker"), whatever the marker's size.
/// </summary>
public static class GfObjectives
{
    public const double SeizeRange = 3;
    /// <summary>Tabletop.Geometry's own distance tolerance, which that library keeps internal.</summary>
    const double Epsilon = 1e-6;

    /// <summary>The objective markers: the setup rows not moved in play, then the moved ones' projection entries.</summary>
    public static IReadOnlyList<Marker> Markers(GameState state)
    {
        var play = state.Projection.Markers;
        return state.Board.Table.Markers.Where(m => !play.ContainsKey(m.Id)).Concat(play.Values).ToList();
    }

    /// <summary>One ObjectiveControlSet per marker whose controller changes at this round end; none when nothing changes.</summary>
    public static IReadOnlyList<StateChange> Seize(GameState state, PropertySet props)
    {
        var arrived = ArrivedByAmbushThisRound(state);
        var placedByTeam = state.Info.Teams.ToDictionary(t => t.Id, t => TeamUnits.Of(state, t.Id)
            .Where(u => !u.Conditions.Contains(GfNames.Shaken) && !arrived.Contains(u.Id) && !Lookup.UnitHas(state, props, u.Id, GfNames.Aircraft))
            .SelectMany(u => Lookup.LivingModels(state, u.Id))
            .Select(m => Battlefield.Placed(state, m)).Where(p => p.HasValue).Select(p => p!.Value).ToList());
        var changes = new List<StateChange>();
        foreach (var marker in Markers(state))
        {
            var centre = marker.Position.Horizontal;
            var near = placedByTeam.Where(kv => kv.Value.Any(m => Distance.EdgeToEdge(m, centre) <= SeizeRange + Epsilon))
                .Select(kv => kv.Key).ToList();
            Guid? now = state.Projection.Control.TryGetValue(marker.Id, out var held) ? held : null;
            var next = near.Count switch { 0 => now, 1 => near[0], _ => (Guid?)null };
            if (next != now) changes.Add(new ObjectiveControlSet(marker.Id, next));
        }
        return changes;
    }

    /// <summary>The teams with the most controlled markers, in ordinal order: one is the winner, several are a tie.</summary>
    public static IReadOnlyList<Guid> Winners(GameState state)
    {
        var held = Held(state);
        if (held.Count == 0) return [];
        var most = held.Max(h => h.Count);
        return held.Where(h => h.Count == most).Select(h => h.Team.Id).ToList();
    }

    /// <summary>"Markers held: Team 1: 2, Team 2: 1." Teams by ordinal: the server holds no team names.</summary>
    public static string Standing(GameState state) =>
        $"Markers held: {string.Join(", ", Held(state).Select(h => $"Team {h.Team.Ordinal}: {h.Count}"))}.";

    static IReadOnlyList<(Team Team, int Count)> Held(GameState state)
    {
        var markers = Markers(state).Select(m => m.Id).ToHashSet();
        return state.Info.Teams.OrderBy(t => t.Ordinal)
            .Select(t => (t, state.Projection.Control.Count(kv => kv.Value == t.Id && markers.Contains(kv.Key)))).ToList();
    }

    /// <summary>Units named by a deploy_ambusher Ask closed since this round's round_started (the Asks RoundBoundaryStep reads); under 3.6 (GfEdition.PlacedUnitsHoldNoObjectives)
    /// also the reserves a `place-reserve` started this round placed (Spawn / Split: "Units placed via Spawn can’t seize or contest objectives on the round they are placed").</summary>
    static HashSet<Guid> ArrivedByAmbushThisRound(GameState state)
    {
        var since = SchedulerLog.Of(state).SinceRound.Select(e => e.Payload).ToList();
        // Reinforcement: "Units that deploy via Reinforcement can't seize or contest objectives on the round they deploy" (GfReserves).
        var asks = since.OfType<AskOpened>().Where(o => o.Ask.Question is AskQuestions.DeployAmbusher or AskQuestions.DeployReinforcement).Select(o => o.Ask.Id).ToHashSet();
        var arrived = since.OfType<AskClosed>().Where(c => asks.Contains(c.AskId))
            .SelectMany(c => c.Answers).Select(a => a.Answer).OfType<SelectedPieces>().SelectMany(s => s.PieceIds).ToHashSet();
        if (GfEdition.Of(state).PlacedUnitsHoldNoObjectives)
            arrived.UnionWith(since.OfType<SequenceStarted>().Where(s => s.Sequence == GfReserves.PlaceReserveSeq && s.Context.TargetUnitIds.Count > 0).Select(s => s.Context.TargetUnitIds[0]));
        return arrived;
    }
}
