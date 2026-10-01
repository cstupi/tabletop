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
    public static bool AnyOnTable(GameState state, UnitInPlay unit) => ModelsOf(state, unit).Any(m => m.Location is not OnShelf);
    public static int IndexOf(IReadOnlyList<Guid> order, Guid team) { for (var i = 0; i < order.Count; i++) if (order[i] == team) return i; return -1; }
}
