using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Scheduler;

/// <summary>Which units are a team's: through the participant that brought them. A participant with no team owns units no team may use.</summary>
public static class TeamUnits
{
    public static Guid? TeamOf(GameState state, Guid participantId) => state.Info.Participants.FirstOrDefault(p => p.Id == participantId)?.TeamId;
    public static IReadOnlyList<UnitInPlay> Of(GameState state, Guid teamId) =>
        state.Projection.Units.Values.Where(u => TeamOf(state, u.ParticipantId) == teamId).OrderBy(u => u.Definition.Ordinal).ToList();
    public static IReadOnlyList<ModelInPlay> ModelsOf(GameState state, UnitInPlay unit) => unit.Definition.Models.Select(m => state.Projection.Models[m.Id]).ToList();
    public static bool AllShelved(GameState state, UnitInPlay unit) => ModelsOf(state, unit).All(m => m.Location is OnShelf);
    public static bool AnyOnTable(GameState state, UnitInPlay unit) => ModelsOf(state, unit).Any(m => InPlay(state, m));
    /// <summary>On the table, or inside a container that is in play. A model inside a container still on the shelf (waiting to deploy with it)
    /// is as out of play as the shelf.</summary>
    public static bool InPlay(GameState state, ModelInPlay model)
    {
        for (var depth = 0; depth < 8; depth++)
        {
            if (model.Location is OnTable) return true;
            if (model.Location is not Inside i || !state.Projection.Models.TryGetValue(i.ContainerId, out var holder)) return false;
            model = holder;
        }
        return false;
    }
    public static int IndexOf(IReadOnlyList<Guid> order, Guid team) { for (var i = 0; i < order.Count; i++) if (order[i] == team) return i; return -1; }
}
