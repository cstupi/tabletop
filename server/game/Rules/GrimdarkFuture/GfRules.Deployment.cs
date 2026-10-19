using Tabletop.Geometry;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Scheduler;
using Tabletop.Protocol;
using static Tabletop.GameServer.Rules.GrimdarkFuture.GfNames;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

public sealed partial class GrimdarkFutureRules
{
    /// <summary>Deployment: the shelf minus set-aside units. Scouts: the set-aside Scouts, after every other unit (page 14). One unit per Ask; no pass.</summary>
    private static TurnAsk? DeployAsk(GameState state, PropertySet props, Guid teamId, string phase)
    {
        var units = TeamUnits.Of(state, teamId).Where(u => TeamUnits.AllShelved(state, u) && !GfReserves.IsReserve(u));   // a reserve unit is placed by its rule only
        units = phase == Deployment
            ? units.Where(u => !u.Conditions.Contains(SetAside))
            : units.Where(u => u.Conditions.Contains(SetAside) && Lookup.UnitHas(state, props, u.Id, Scout));
        var ids = units.Select(u => u.Id).ToList();
        // Re-Deployment: "After all other units are deployed … you may remove up to two friendly units from the table and deploy them again": picked beside the Scouts.
        var again = phase == Scouts ? GfAmbush.ReDeployable(state, teamId) : [];
        if (ids.Count == 0 && again.Count == 0) return null;
        return phase == Deployment
            ? new TurnAsk(AskQuestions.Deploy, "Deploy one unit", new PieceSelection(ids, 1, 1), AllowPass: false)
            : new TurnAsk(AskQuestions.DeployScouts, again.Count == 0 ? "Deploy a Scout" : ids.Count == 0 ? "Re-deploy a unit or pass" : "Deploy a Scout or re-deploy a unit",
                new PieceSelection([.. ids, .. again], 1, 1), AllowPass: ids.Count == 0);
    }

    /// <summary>
    /// A hand placement from the shelf onto the table in the phases before round one, judged against the placing team's deployment zone when the
    /// game has one. Page 6: "places one unit fully within 12” of their table edge" (the zone, p.22's styles); page 14 Scout: "may be deployed
    /// anywhere fully within 12” of their deployment zone". Ambush (page 13) deploys from round two and is not read here.
    /// </summary>
    public static AnswerBreachFound? ZoneBreach(GameState state, IReadOnlyList<ModelRelocation> moves)
    {
        var p = state.Projection;
        if (p.Scheduler.Round != 0 || p.Scheduler.Phase is not (Deployment or Scouts)) return null;
        var table = state.Board.Table;
        if (table.Zones.Count == 0) return null;
        foreach (var move in moves)
        {
            if (move.To is not OnTable { Placement: var at } || !p.Models.TryGetValue(move.ModelId, out var model) || model.Location is not OnShelf) continue;
            var ordinal = Teams.Of(state, model.UnitId) is { } team ? state.Info.Teams.FirstOrDefault(t => t.Id == team)?.Ordinal : null;
            if (table.Zones.FirstOrDefault(z => z.TeamOrdinal == ordinal) is not { } zone) continue;
            var placed = new PlacedModel(new ModelBody(model.Slot.Base, model.Slot.Height), at);
            // Re-Deployment's units "deploy them again": within the zone, in the Scouts phase as before it.
            var again = p.Units[model.UnitId].Conditions.Contains(GfAmbush.ReDeployed);
            if ((p.Scheduler.Phase == Deployment || again) && !ZoneStyles.BaseFullyWithin(zone.Shape, table.Size, placed))
                return new(DeployZoneRule, $"{model.Slot.Name} would be placed outside its deployment zone: units deploy fully within it (p.6)");
            if (p.Scheduler.Phase == Scouts && !again && !ZoneStyles.BaseFullyWithin(zone.Shape, table.Size, placed, ScoutReach))
                return new(DeployZoneRule, $"{model.Slot.Name} would be placed beyond 12\" of its deployment zone: Scouts deploy fully within 12\" of it (p.14)");
        }
        return null;
    }

    /// <summary>
    /// A set-aside unit arriving from the shelf once rounds have begun is an Ambusher (Scouts arrive before round one). Page 13 Ambush: "At the
    /// start of any round after the first, may be deployed anywhere over 9” away from enemy units": every base of it stays over 9" from every
    /// enemy model on the table, measured base to base (p.5).
    /// </summary>
    public static AnswerBreachFound? AmbushBreach(GameState state, IReadOnlyList<ModelRelocation> moves)
    {
        var p = state.Projection;
        if (p.Scheduler.Round < 1) return null;
        var moving = moves.Select(m => m.ModelId).ToHashSet();
        foreach (var move in moves)
        {
            if (move.To is not OnTable { Placement: var at } || !p.Models.TryGetValue(move.ModelId, out var model) || model.Location is not OnShelf) continue;
            if (!p.Units[model.UnitId].Conditions.Contains(SetAside)) continue;
            var team = Teams.Of(state, model.UnitId);
            var placed = Battlefield.At(state, model.Id, at);
            // Army-book rules (batch 3): Ambush Beacon lifts the distance within 6" of a friendly beacon; Infiltrate deploys over 3"; Repel Ambushers
            // holds an Ambusher over 12" from its own unit.
            if (GfAmbush.NearBeacon(state, team, placed)) continue;
            var reach = GfAmbush.Reach(state, model.UnitId);
            var near = p.Models.Values.FirstOrDefault(o => !moving.Contains(o.Id) && o.Location is OnTable && Teams.Of(state, o.UnitId) != team
                && Distance.EdgeToEdge(placed, Battlefield.Placed(state, o.Id)!.Value) <= GfAmbush.Kept(state, o.UnitId, reach) + 1e-6);
            if (near is not null)
            {
                var kept = GfAmbush.Kept(state, near.UnitId, reach);
                return new(AmbushDistanceRule, kept > reach
                    ? $"{model.Slot.Name} would be placed within {kept}\" of an enemy unit with Repel Ambushers: Ambushers set up over 12\" away from it"
                    : $"{model.Slot.Name} would be placed within {reach}\" of an enemy unit: Ambushers deploy over {reach}\" away from enemy units (p.13)");
            }
        }
        return null;
    }

    /// <summary>The set-aside units of the team that may deploy as Ambushers now: those holding Ambush (Surprise Attack counts as having Infiltrate, which counts as having Ambush), or removed by Ambush Re-Deployment ("deploy it as if it had Ambush"),
    /// from round two; in round one only those with Rapid Ambush ("may be deployed at the start of any round, including the first").</summary>
    public static IReadOnlyList<Guid> Ambushers(GameState state, PropertySet props, Guid teamId) =>
        TeamUnits.Of(state, teamId).Where(u => u.Conditions.Contains(SetAside) && TeamUnits.AllShelved(state, u)
            && (Lookup.UnitHas(state, props, u.Id, Ambush) || GfAmbush.RemovedToAmbush(state, u.Id) || GfAmbush.Carries(state, u.Id, GfAmbush.SurpriseAttack))
            && (state.Projection.Scheduler.Round >= 2 || GfAmbush.Carries(state, u.Id, GfAmbush.RapidAmbush))).Select(u => u.Id).ToList();

    /// <summary>
    /// Page 13: from round two, Ambushers deploy "starting with the player that activates next", alternating, until every team has passed
    /// or has none left. Read from the deploy_ambusher Asks since round_started; a pass stands for this boundary; nothing is written to the record.
    /// </summary>
    HookResult AmbushBoundary(GameState state, PropertySet props, int round, IReadOnlyList<Guid> order)
    {
        if (round < 1) return new HookDone();
        var log = SchedulerLog.Of(state);
        var since = log.SinceRound.Select(e => e.Payload).ToList();
        var asks = since.OfType<AskOpened>().Where(o => o.Ask.Question == AskQuestions.DeployAmbusher).ToList();
        var passed = new HashSet<Guid>();
        foreach (var a in asks)
        {
            if (state.Projection.OpenAsks.ContainsKey(a.Ask.Id)) return new HookWaiting();
            var answer = since.OfType<AskClosed>().FirstOrDefault(c => c.AskId == a.Ask.Id)?.Answers.FirstOrDefault()?.Answer;   // no close in the log: nothing was answered, as a pass
            if (answer is SelectedPieces { PieceIds: [var unit] } && state.Projection.Units.TryGetValue(unit, out var u))
            {
                if (TeamUnits.ModelsOf(state, u).Any(m => m.Location is OnShelf)) return new HookWaiting();      // models still coming off the shelf
                if (GfHooks.ArrivedFromSetAside(state, u.Id) is { Count: > 0 } arrived)                          // arrived, with any unit inside: no longer set aside, so casualties on the shelf are never offered again
                    return new HookEmit([.. arrived.Cast<EventPayload>(), GfAmbush.Arrived(u.Id)]);
            }
            if (answer is not SelectedPieces) passed.Add(a.Ask.Teams[0]);
        }
        var eligible = order.Where(t => !passed.Contains(t) && Ambushers(state, props, t).Count > 0).ToList();
        if (eligible.Count == 0) return new HookDone();
        var next = eligible[0];
        if (asks.Count > 0)
        {
            var lastIndex = TeamUnits.IndexOf(order, asks[^1].Ask.Teams[0]);
            next = eligible.FirstOrDefault(t => TeamUnits.IndexOf(order, t) > lastIndex, eligible[0]);
        }
        var ask = new Ask(Guid.CreateVersion7(), AskQuestions.DeployAmbusher, "Deploy an Ambusher or pass", [next],
            new PieceSelection(Ambushers(state, props, next), 1, 1), AllowPass: true, Sealed: false, WindowKind: null, FrameId: null);
        return new HookEmit([new AskOpened(ask)]);
    }
}
