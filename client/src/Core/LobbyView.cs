using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>One team button in the lobby: what it says, whether it can be pressed, and why not.</summary>
public sealed record TeamChoice(Guid TeamId, string Text, bool Enabled, string Why);

/// <summary>
/// The lobby's seat and team buttons, as the game server's lobby authority has them: a player joins an empty team; a team another
/// participant holds is joined only by the creator placing them there; the configuration, Start setup and End setup are the creator's.
/// </summary>
public static class LobbyView
{
    public const string CreatorOnly = "Only the game's creator changes the configuration.";

    /// <summary>Where I sit, and what that means once play starts.</summary>
    public static string Seat(GameInfo game, Guid? participantId)
    {
        var me = game.Participants.FirstOrDefault(p => p.Id == participantId);
        if (me is null) return "You are watching this game.";
        var army = me.ArmyId is null ? " Pick an army below." : "";
        return me.TeamId is { } t && game.Teams.FirstOrDefault(x => x.Id == t) is { } team
            ? $"You are on Team {team.Ordinal}.{army}"
            : "You have no team: join one, or stay without one to watch once play starts.";
    }

    public static IReadOnlyList<TeamChoice> Teams(GameInfo game, Guid? participantId, bool isCreator) => game.Teams.OrderBy(t => t.Ordinal).Select(t =>
    {
        var holders = game.Participants.Where(p => p.TeamId == t.Id && p.Id != participantId).Select(p => p.Name).ToList();
        var mine = game.Participants.Any(p => p.Id == participantId && p.TeamId == t.Id);
        if (mine) return new TeamChoice(t.Id, $"On team {t.Ordinal}", false, "You are on this team.");
        if (holders.Count == 0 || isCreator) return new TeamChoice(t.Id, $"Join team {t.Ordinal}", true, holders.Count == 0 ? "" : $"Held by {string.Join(", ", holders)}; as the creator you may join it.");
        return new TeamChoice(t.Id, $"Team {t.Ordinal}: {string.Join(", ", holders)}", false, $"{string.Join(", ", holders)} holds this team: ask the creator to place you on it.");
    }).ToList();

    /// <summary>What a participant who is not the creator waits for, by status; null for the creator.</summary>
    public static string? WaitingForCreator(GameInfo game, bool isCreator) => isCreator ? null : game.Status switch
    {
        GameStatus.Lobby => "Waiting for the creator to start setup.",
        GameStatus.Setup => "Anyone may edit the table. Waiting for the creator to end setup.",
        _ => null,
    };
}
