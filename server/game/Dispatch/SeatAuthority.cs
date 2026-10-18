using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Scheduler;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Dispatch;

/// <summary>
/// Who on a team may answer (game-flow spec → Participants; rulebook p.50 Team Games: "the turn order is on a team basis and not on a player basis,
/// so for each team's turn only one of the players may activate a single unit"). An Ask naming a Seat is that seat's; an OwnUnitsOnly answer may
/// pick only the answering seat's own units of its team. One account answers for every seat it holds (self-play). Never an enforcement question:
/// a refusal under every setting, as an Ask addressed to another team is.
/// </summary>
public static class SeatAuthority
{
    /// <summary>Null when the actor's seat may give this answer; otherwise the refusal's detail.</summary>
    public static string? Breach(GameState state, Ask ask, AskAnswer answer, Actor actor)
    {
        var account = AccountOf(state, actor.ParticipantId);
        if (ask.Seat is { } seat && seat != actor.ParticipantId && AccountOf(state, seat) != account)
            return $"this ask is {NameOf(state, seat)}'s: each player commands their own army (p.50)";
        if (!ask.OwnUnitsOnly) return null;
        foreach (var unitId in PickedUnits(state, ask, answer))
        {
            if (!state.Projection.Units.TryGetValue(unitId, out var unit) || unit.ParticipantId == actor.ParticipantId) continue;
            if (TeamOf(state, unit.ParticipantId) != actor.TeamId || AccountOf(state, unit.ParticipantId) == account) continue;
            return $"{unit.Definition.Name} is {NameOf(state, unit.ParticipantId)}'s unit: each player activates their own units (p.50)";
        }
        return null;
    }

    /// <summary>The units an answer picks: a chosen option's pieces (an activate option's id names its unit even outside the set), or the selected
    /// pieces; a model id stands for its unit.</summary>
    static IEnumerable<Guid> PickedUnits(GameState state, Ask ask, AskAnswer answer)
    {
        IEnumerable<Guid> pieces = answer switch
        {
            Chosen c when ask.Legal is Options o && o.Choices.FirstOrDefault(x => x.Id == c.OptionId) is { } option => option.Pieces,
            Chosen c when ActivationOption.TryParse(c.OptionId, out var unit, out _) => [unit],
            SelectedPieces s => s.PieceIds,
            _ => [],
        };
        return pieces.Select(id => state.Projection.Models.TryGetValue(id, out var m) ? m.UnitId : id).Distinct();
    }

    static Guid? AccountOf(GameState state, Guid participant) => state.Info.Participants.FirstOrDefault(p => p.Id == participant)?.AccountId;
    static Guid? TeamOf(GameState state, Guid participant) => state.Info.Participants.FirstOrDefault(p => p.Id == participant)?.TeamId;
    static string NameOf(GameState state, Guid participant) => state.Info.Participants.FirstOrDefault(p => p.Id == participant)?.Name ?? "another player";
}
