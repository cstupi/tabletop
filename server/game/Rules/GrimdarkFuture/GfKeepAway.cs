using Tabletop.GameServer.Interpreter;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>
/// The placements held "over X away from enemy units", as the Ask carries them (Protocol.KeepAway) and as the placement breaches judge them, so a
/// client draws exactly what the server refuses:
/// p.13 Ambush "may be deployed anywhere over 9” away from enemy units" (Infiltrate "over 3”", Repel Ambushers "over 12”" from its unit, Ambush
/// Beacon's 6"); 3.6.0 p.15 Scout "over 9” away from non-Scout enemy units"; 3.6 Reinforcement "over 9” away from enemy units"; 3.6 Fanatic and
/// Vanguard "over 9” away from non-Vanguard enemy units". The listed units are every unit of another team the rule counts, on the table or not:
/// only their models on the table are measured.
/// </summary>
public static class GfKeepAway
{
    public const double BeaconWithin = 6;

    /// <summary>An Ambusher's (and a unit placed as if it had Ambush): its own reach (GfAmbush.Reach), or Repel Ambushers' 12" from that unit; a
    /// friendly Ambush Beacon model lifts it within 6".</summary>
    public static KeepAway Ambush(GameState state, Guid unit)
    {
        var team = Teams.Of(state, unit);
        var reach = GfAmbush.Reach(state, unit);
        var units = Enemies(state, team).Select(u => new AwayFrom(u, GfAmbush.Kept(state, u, reach))).ToList();
        var beacons = state.Projection.Models.Values.Where(m => Teams.Of(state, m.UnitId) == team && m.Slot.Properties.Any(p => p.Name == GfAmbush.AmbushBeacon))
            .Select(m => m.Id).ToList();
        return beacons.Count == 0 ? new KeepAway(units) : new KeepAway(units, beacons, BeaconWithin);
    }

    /// <summary>A Scout's under 3.6 (GfEdition.ScoutClearance); null under 3.5.1, whose Scout keeps no distance.</summary>
    public static KeepAway? Scout(GameState state, Guid unit) => Of(state, Teams.Of(state, unit), GfEdition.Of(state).ScoutClearance, u => !GfAmbush.Carries(state, u, GfNames.Scout));

    /// <summary>A Reinforcement copy's under 3.6 (GfEdition.ReinforcementClearance); null under 3.5.1.</summary>
    public static KeepAway? Reinforcement(GameState state, Guid unit) => Of(state, Teams.Of(state, unit), GfEdition.Of(state).ReinforcementClearance, _ => true);

    /// <summary>A Fanatic or Vanguard placement's under 3.6 (GfEdition.VanguardClearance); null under 3.5.1.</summary>
    public static KeepAway? Vanguard(GameState state, Guid unit) => Of(state, Teams.Of(state, unit), GfEdition.Of(state).VanguardClearance, u => !GfAmbush.Carries(state, u, GfNames.Vanguard));

    static KeepAway? Of(GameState state, Guid? team, double over, Func<Guid, bool> counts) =>
        over <= 0 ? null : new KeepAway(Enemies(state, team).Where(counts).Select(u => new AwayFrom(u, over)).ToList());

    static IEnumerable<Guid> Enemies(GameState state, Guid? team) =>
        state.Projection.Units.Keys.Where(u => Teams.Of(state, u) != team).OrderBy(u => u);

    /// <summary>The first listed unit a base placed at `placed` comes within the distance of (Clearance.Breaks over its models on the table, the
    /// models `moving` left out), or null.</summary>
    public static AwayFrom? Broken(GameState state, KeepAway keep, PlacedModel placed, IReadOnlySet<Guid> moving)
    {
        var p = state.Projection;
        var over = keep.Units.ToDictionary(u => u.UnitId);
        var from = p.Models.Values.Where(m => over.ContainsKey(m.UnitId) && m.Location is OnTable && !moving.Contains(m.Id)).ToList();
        var lifted = (keep.IgnoredNear ?? []).Where(id => p.Models.TryGetValue(id, out var m) && m.Location is OnTable).Select(id => Battlefield.Placed(state, id)!.Value).ToList();
        var hit = Clearance.Breaks(placed, from.Select(m => Battlefield.Placed(state, m.Id)!.Value).ToList(), from.Select(m => over[m.UnitId].Over).ToList(), lifted, keep.IgnoredWithin ?? 0);
        return hit < 0 ? null : over[from[hit].UnitId];
    }
}
