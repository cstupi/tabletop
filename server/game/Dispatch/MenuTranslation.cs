using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Dispatch;

/// <summary>
/// The override path: each menu or hand-move command becomes the one event that states the change in full. Nothing here is a
/// rule — a dead model may be put back on the table and a unit may be marked moved twice. Only references are checked: the
/// event must name something the projection has, or applying it would throw.
/// </summary>
public static class MenuTranslation
{
    public static EventPayload? Translate(GameState state, Command command, out string? missing)
    {
        var p = state.Projection;
        string? miss = null;   // a local function may not capture an out parameter (CS8175), so it captures this and copies out at the end
        bool HasModel(Guid id) => p.Models.ContainsKey(id) || Missing(ref miss, $"no model {id}");
        bool HasUnit(Guid id) => p.Units.ContainsKey(id) || Missing(ref miss, $"no unit {id}");
        bool HasTeam(Guid id) => state.Info.Teams.Any(t => t.Id == id) || Missing(ref miss, $"no team {id}");
        bool OnBoard(Guid id) => state.Board.Table.Markers.Any(m => m.Id == id);
        // A setup marker moves (its first move puts it into the projection) but is never removed: it is the mission's.
        bool Movable(Guid id) => p.Markers.ContainsKey(id) || OnBoard(id) || Missing(ref miss, $"no marker {id}");
        bool Removable(Guid id) => (p.Markers.ContainsKey(id) && !OnBoard(id)) || Missing(ref miss, OnBoard(id) ? $"setup marker {id} is not removable" : $"no marker {id}");
        // The play entry wins a shared id, as in GfObjectives.Markers; only an objective can be held.
        bool HasObjective(Guid id) => (p.Markers.TryGetValue(id, out var played) ? played : state.Board.Table.Markers.FirstOrDefault(m => m.Id == id)) is { Kind: Tabletop.Content.MarkerKind.Objective } || Missing(ref miss, $"no objective {id}");
        bool SubjectExists(Subject s) => s.Kind == SubjectKind.Unit ? HasUnit(s.Id) : HasModel(s.Id);

        EventPayload? payload = command switch
        {
            MoveModels m when m.Moves.All(x => HasModel(x.ModelId)) => new ModelsMoved(m.Moves.Select(x => x.To is OnTable t
                ? new ModelRelocation(x.ModelId, x.From, new OnTable(Battlefield.Settle(state, x.ModelId, t.Placement))) : x).ToList()),
            PlaceMarker m when !p.Markers.ContainsKey(m.Marker.Id) || Missing(ref miss, $"marker {m.Marker.Id} already placed") => new MarkerPlaced(m.Marker),
            MoveMarker m when Movable(m.MarkerId) => new MarkerMoved(m.MarkerId, m.Position, m.Yaw),
            RemoveMarker m when Removable(m.MarkerId) => new MarkerRemoved(m.MarkerId),
            SetWounds w when HasModel(w.ModelId) => new WoundsSet(w.ModelId, w.Wounds),
            SetCondition c when SubjectExists(c.Subject) => new ConditionSet(c.Subject, c.Condition, c.Present),
            RecordAction a when HasUnit(a.UnitId) => new ActionRecorded(a.UnitId, a.Action),
            ClearActionRecord a when HasUnit(a.UnitId) => new ActionRecordCleared(a.UnitId),
            AddGrant g when HasUnit(g.UnitId) => new GrantAdded(g.UnitId, g.Grant),
            RemoveGrant g when HasUnit(g.UnitId) => new GrantRemoved(g.UnitId, g.GrantId),
            SetSpellTokens s when HasModel(s.ModelId) => new SpellTokensSet(s.ModelId, s.Tokens),
            SetWoundsThisActivation w when HasUnit(w.UnitId) => new WoundsThisActivationSet(w.UnitId, w.Wounds),
            SetScore s when HasTeam(s.TeamId) => new ScoreSet(s.TeamId, s.Score),
            SetActiveTeam t when HasTeam(t.TeamId) => new TurnStarted(t.TeamId),
            SetRound r => new RoundStarted(r.Round, p.Scheduler.Order.ToList(), []),
            SetObjectiveControl o when HasObjective(o.MarkerId) && (o.TeamId is not { } team || HasTeam(team)) => new ObjectiveControlSet(o.MarkerId, o.TeamId),
            _ => null,
        };
        missing = miss;
        return payload;
    }

    static bool Missing(ref string? missing, string detail) { missing ??= detail; return false; }
}
