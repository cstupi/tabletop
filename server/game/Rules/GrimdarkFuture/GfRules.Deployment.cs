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
                Selection([.. ids, .. again], u => ids.Contains(u) ? GfKeepAway.Scout(state, u) : null), AllowPass: ids.Count == 0);
    }

    /// <summary>A one-unit deploy pick carrying each eligible unit's distance from enemy units (PieceSelection.KeepAway), where it has one.</summary>
    static PieceSelection Selection(IReadOnlyList<Guid> eligible, Func<Guid, KeepAway?> keep)
    {
        var kept = eligible.Select(u => (u, k: keep(u))).Where(x => x.k is not null).ToDictionary(x => x.u, x => x.k!);
        return new PieceSelection(eligible, 1, 1, kept.Count == 0 ? null : kept);
    }

    /// <summary>
    /// A hand placement from the shelf onto the table in the phases before round one, judged against the placing team's deployment zone when the
    /// game has one. Page 6: "places one unit fully within 12” of their table edge" (the zone, p.22's styles); page 14 Scout: "may be deployed
    /// anywhere fully within 12” of their deployment zone", 3.6.0 p.15 "fully within 9”" (GfEdition.ScoutReach). Ambush (page 13) deploys from
    /// round two and is not read here.
    /// </summary>
    public static AnswerBreachFound? ZoneBreach(GameState state, IReadOnlyList<ModelRelocation> moves)
    {
        var p = state.Projection;
        if (p.Scheduler.Round != 0 || p.Scheduler.Phase is not (Deployment or Scouts)) return null;
        var table = state.Board.Table;
        if (table.Zones.Count == 0) return null;
        var edition = GfEdition.Of(state);
        var legs = GfEdition.ZoneLegsOf(edition);
        foreach (var move in moves)
        {
            if (move.To is not OnTable { Placement: var at } || !p.Models.TryGetValue(move.ModelId, out var model) || model.Location is not OnShelf) continue;
            var ordinal = Teams.Of(state, model.UnitId) is { } team ? state.Info.Teams.FirstOrDefault(t => t.Id == team)?.Ordinal : null;
            if (table.Zones.FirstOrDefault(z => z.TeamOrdinal == ordinal) is not { } zone) continue;
            var placed = new PlacedModel(new ModelBody(model.Slot.Base, model.Slot.Height), at);
            // Re-Deployment's units "deploy them again": within the zone, in the Scouts phase as before it.
            var again = p.Units[model.UnitId].Conditions.Contains(GfAmbush.ReDeployed);
            if ((p.Scheduler.Phase == Deployment || again) && !ZoneStyles.BaseFullyWithin(zone.Shape, table.Size, placed, legs))
                return new(DeployZoneRule, $"{model.Slot.Name} would be placed outside its deployment zone: units deploy fully within it");
            if (p.Scheduler.Phase == Scouts && !again && !ZoneStyles.BaseFullyWithin(zone.Shape, table.Size, placed, edition.ScoutReach, legs))
                return new(DeployZoneRule, $"{model.Slot.Name} would be placed beyond {edition.ScoutReach}\" of its deployment zone: Scouts deploy fully within {edition.ScoutReach}\" of it");
        }
        return null;
    }

    /// <summary>
    /// 3.6.0 p.15 Scout: "may be deployed anywhere fully within 9” of their deployment zone, over 9” away from non-Scout enemy units"
    /// (GfEdition.ScoutClearance; 3.5.1 has none). In the Scouts phase, a Scout's base within the clearance of a model of an enemy unit that does not
    /// itself carry Scout (or a Scout Aura, GfAmbush.Carries) is refused, measured base to base (p.5). A Re-Deployed unit is not a Scout placement.
    /// Scouts are recognised by GfAmbush.Carries because PlacementBreach has no PropertySet (as AmbushBreach); on today's content it agrees with
    /// Lookup.UnitHas, Scout Aura being the only rule that grants Scout.
    /// </summary>
    public static AnswerBreachFound? ScoutBreach(GameState state, IReadOnlyList<ModelRelocation> moves)
    {
        var p = state.Projection;
        if (p.Scheduler.Round != 0 || p.Scheduler.Phase != Scouts) return null;
        var edition = GfEdition.Of(state);
        if (edition.ScoutClearance <= 0) return null;
        var moving = moves.Select(m => m.ModelId).ToHashSet();
        foreach (var move in moves)
        {
            if (move.To is not OnTable { Placement: var at } || !p.Models.TryGetValue(move.ModelId, out var model) || model.Location is not OnShelf) continue;
            if (p.Units[model.UnitId].Conditions.Contains(GfAmbush.ReDeployed)) continue;
            if (GfKeepAway.Scout(state, model.UnitId) is { } keep && GfKeepAway.Broken(state, keep, Battlefield.At(state, model.Id, at), moving) is not null)
                return new(ScoutDistanceRule, $"{model.Slot.Name} would be placed within {edition.ScoutClearance}\" of a non-Scout enemy unit: Scouts deploy over {edition.ScoutClearance}\" away from them");
        }
        return null;
    }

    /// <summary>
    /// 3.6 Fanatic and Vanguard: "After this model is deployed, it may be placed anywhere fully within 9" of its position, over 9" away from non-Vanguard
    /// enemy units" (GfEdition.VanguardClearance; 3.5.1's text has no distance). The rule's placement is its `reposition` frame's move Ask: a landing
    /// with any base within the clearance of a model of an enemy unit that does not carry Vanguard (GfAmbush.Carries: the rule or its Aura) is refused,
    /// measured base to base (p.5).
    /// </summary>
    public static AnswerBreachFound? VanguardBreach(GameState state, Ask ask, MoveRegion region, Destinations answer)
    {
        var clearance = GfEdition.Of(state).VanguardClearance;
        if (clearance <= 0 || ask.FrameId is not { } id || state.Projection.Stack.FirstOrDefault(f => f.Id == id) is not { Sequence: GfNames.RepositionSeq } frame
            || frame.Context.Action is not (GfNames.Fanatic or GfNames.Vanguard)) return null;
        var p = state.Projection;
        var moves = GfAnswers.Moves(state, region, answer);
        var moving = moves.Select(m => m.ModelId).ToHashSet();
        foreach (var move in moves)
        {
            if (move.To is not OnTable { Placement: var at } || !p.Models.TryGetValue(move.ModelId, out var model)) continue;
            if (GfKeepAway.Vanguard(state, model.UnitId) is { } keep && GfKeepAway.Broken(state, keep, Battlefield.At(state, model.Id, at), moving) is not null)
                return new(GfNames.VanguardDistanceRule, $"{model.Slot.Name} would be placed within {clearance}\" of a non-Vanguard enemy unit: {frame.Context.Action} places it over {clearance}\" away from them");
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
            // Army-book rules (batch 3): Ambush Beacon lifts the distance within 6" of a friendly beacon; Infiltrate deploys over 3"; Repel Ambushers
            // holds an Ambusher over 12" from its own unit (GfKeepAway.Ambush).
            var reach = GfAmbush.Reach(state, model.UnitId);
            if (GfKeepAway.Broken(state, GfKeepAway.Ambush(state, model.UnitId), Battlefield.At(state, model.Id, at), moving) is { } near)
            {
                var kept = near.Over;
                return new(AmbushDistanceRule, kept > reach
                    ? $"{model.Slot.Name} would be placed within {kept}\" of an enemy unit with Repel Ambushers: Ambushers set up over 12\" away from it"
                    : $"{model.Slot.Name} would be placed within {reach}\" of an enemy unit: Ambushers deploy over {reach}\" away from enemy units");
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
            Selection(Ambushers(state, props, next), u => GfKeepAway.Ambush(state, u)), AllowPass: true, Sealed: false, WindowKind: null, FrameId: null);
        return new HookEmit([new AskOpened(ask)]);
    }
}
