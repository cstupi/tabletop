using Tabletop.Protocol;
namespace Tabletop.Client.Core;

/// <summary>One lobby row per participant, teamless ones included. Selected indexes Choices.</summary>
public sealed record LobbySeat(Guid ParticipantId, string Name, string Seat, bool HasArmy, int Selected);
public sealed record SeatChoice(Guid? TeamId, string Label);

/// <summary>The lobby's participant list. Teams are shared (game flow spec): the creator places any participant on any team with
/// PickTeam(teamId, participantId); the server refuses it from anyone else.</summary>
public static class LobbySeats
{
    public const string NoTeam = "No team";

    /// <summary>No team first, then the teams by ordinal.</summary>
    public static IReadOnlyList<SeatChoice> Choices(GameInfo game) =>
        [new(null, NoTeam), .. game.Teams.OrderBy(t => t.Ordinal).Select(t => new SeatChoice(t.Id, $"Team {t.Ordinal}"))];

    public static IReadOnlyList<LobbySeat> Of(GameInfo game)
    {
        var choices = Choices(game);
        return game.Participants.Select(p =>
        {
            var at = Math.Max(0, choices.ToList().FindIndex(c => c.TeamId == p.TeamId));
            return new LobbySeat(p.Id, p.Name, choices[at].Label, p.ArmyId is not null, at);
        }).ToList();
    }

    public static PickTeam Pick(GameInfo game, Guid participantId, int choice) => new(Choices(game)[choice].TeamId, participantId);
}
