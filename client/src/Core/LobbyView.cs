using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>One team button in the lobby: what it says, whether it can be pressed, and why not. Leave: it gives up a team I hold (LeaveTeam),
/// else it takes the team (PickTeam), one more team when I already play one.</summary>
public sealed record TeamChoice(Guid TeamId, string Text, bool Enabled, string Why, bool Leave = false)
{
    public LobbyEdit Edit => Leave ? new LeaveTeam(TeamId) : new PickTeam(TeamId);
}
/// <summary>One army picker: the team it brings an army to (null for a participant with no team), its label, the army picked.</summary>
public sealed record ArmySeat(Guid? TeamId, string Label, Guid? ArmyId)
{
    public PickArmy Pick(Guid? armyId) => new(armyId, TeamId);
}
/// <summary>One row of the lobby's Players list. Selected indexes SeatChoices; Army is the seat's army's name, null with none.</summary>
public sealed record LobbySeat(Guid ParticipantId, string Name, string Seat, bool HasArmy, int Selected, string? Army = null);
public sealed record SeatChoice(Guid? TeamId, string Label);

/// <summary>
/// The lobby's seat and team buttons, as the game server's lobby authority has them: a player joins an empty team; a team another
/// participant holds is joined only by the creator placing them there; the configuration, Start setup and End setup are the creator's.
/// Every seat brings its own army, so a team may hold several (rulebook p.50 Team Games).
/// </summary>
public static class LobbyView
{
    public const string CreatorOnly = "Only the game's creator changes the configuration.";

    /// <summary>Where I sit, and what that means once play starts: one team, several (self-play: one player on both sides), or none.</summary>
    public static string Seat(GameInfo game, Guid? participantId)
    {
        var seats = Seats.Mine(game, participantId);
        if (seats.Count == 0) return "You are watching this game.";
        var ordinals = game.Teams.Where(t => seats.Any(s => s.TeamId == t.Id)).OrderBy(t => t.Ordinal).Select(t => $"Team {t.Ordinal}").ToList();
        if (ordinals.Count == 0) return "You have no team: join one, or stay without one to watch once play starts.";
        var army = seats.Any(s => s.TeamId is not null && s.ArmyId is null) ? " Pick your army below." : "";
        var both = ordinals.Count > 1 ? " You play every side you hold." : "";
        var mates = game.Participants.Where(p => p.TeamId is { } t && seats.Any(s => s.TeamId == t) && seats.All(s => s.AccountId != p.AccountId)).Select(p => p.Name).Distinct().ToList();
        var with = mates.Count == 0 ? "" : $" Teammates: {string.Join(", ", mates)}.";
        return $"You are on {string.Join(" and ", ordinals)}.{army}{both}{with}";
    }

    public static IReadOnlyList<TeamChoice> Teams(GameInfo game, Guid? participantId, bool isCreator)
    {
        var account = Seats.Mine(game, participantId).FirstOrDefault()?.AccountId;
        var playing = Seats.Teams(game, participantId).Count > 0;
        return game.Teams.OrderBy(t => t.Ordinal).Select(t =>
        {
            var holders = game.Participants.Where(p => p.TeamId == t.Id && p.AccountId != account).Select(p => p.Name).ToList();
            if (game.Participants.Any(p => p.TeamId == t.Id && p.AccountId == account))
                return new TeamChoice(t.Id, $"Leave team {t.Ordinal}", true, "You play this team; leaving it drops the army you brought to it.", Leave: true);
            var too = playing ? " You keep your other team and play both sides." : "";
            if (holders.Count == 0 || isCreator) return new TeamChoice(t.Id, $"Join team {t.Ordinal}", true, (holders.Count == 0 ? "" : $"Held by {string.Join(", ", holders)}; as the creator you may join it.") + too);
            return new TeamChoice(t.Id, $"Team {t.Ordinal}: {string.Join(", ", holders)} · ask the creator to add you", false,
                $"{string.Join(", ", holders)} holds this team; the creator adds you from the Players list.");
        }).ToList();
    }

    /// <summary>My army picker: "Your army", or one per team I hold ("Your army for Team 2"), or one for my teamless row. Each seat picks its own;
    /// a teammate's army is theirs.</summary>
    public static IReadOnlyList<ArmySeat> Armies(GameInfo game, Guid? participantId)
    {
        var seats = Seats.Mine(game, participantId);
        var teamed = game.Teams.OrderBy(t => t.Ordinal).SelectMany(t => seats.Where(s => s.TeamId == t.Id).Select(s => (Team: t, Seat: s))).ToList();
        if (teamed.Count == 0) return seats.Take(1).Select(s => new ArmySeat(null, "Your army", s.ArmyId)).ToList();
        return teamed.Select(x => new ArmySeat(x.Team.Id, teamed.Count == 1 ? "Your army" : $"Your army for Team {x.Team.Ordinal}", x.Seat.ArmyId)).ToList();
    }

    public const string NoTeam = "No team";

    /// <summary>The Players list's team choices: No team first, then the teams by ordinal.</summary>
    public static IReadOnlyList<SeatChoice> SeatChoices(GameInfo game) =>
        [new(null, NoTeam), .. game.Teams.OrderBy(t => t.Ordinal).Select(t => new SeatChoice(t.Id, $"Team {t.Ordinal}"))];

    /// <summary>The Players list: every participant, teamless ones included, with the choice that names their team.</summary>
    public static IReadOnlyList<LobbySeat> Players(GameInfo game)
    {
        var choices = SeatChoices(game);
        return game.Participants.Select(p =>
        {
            var at = Math.Max(0, choices.ToList().FindIndex(c => c.TeamId == p.TeamId));
            return new LobbySeat(p.Id, p.Name, choices[at].Label, p.ArmyId is not null, at, ArmyOf(p));
        }).ToList();
    }

    public const string NoPlayers = "No players yet";

    /// <summary>A team card's players, one line each with the army that player brings ("Clay ⚔ Iron Legion", "Cid · no army yet"); a new team says
    /// it has none.</summary>
    public static string Members(GameInfo game, Guid teamId) =>
        game.Participants.Where(p => p.TeamId == teamId).Select(p => ArmyOf(p) is { } army ? $"{p.Name} ⚔ {army}" : $"{p.Name} · no army yet").ToList() is { Count: > 0 } names
            ? string.Join("\n", names) : NoPlayers;

    /// <summary>The seat's army by name; "army" when the rows carry the pick but not its name; null with no army.</summary>
    static string? ArmyOf(Participant p) => p.ArmyName ?? (p.ArmyId is null ? null : "army");

    /// <summary>The creator placing a participant: the server's path onto a team another participant holds.</summary>
    public static PickTeam Place(GameInfo game, Guid participantId, int choice) => new(SeatChoices(game)[choice].TeamId, participantId);

    /// <summary>What a participant who is not the creator waits for, by status; null for the creator.</summary>
    public static string? WaitingForCreator(GameInfo game, bool isCreator) => isCreator ? null : game.Status switch
    {
        GameStatus.Lobby => "Waiting for the creator to start setup.",
        GameStatus.Setup => "Anyone may edit the table. Waiting for the creator to end setup.",
        _ => null,
    };

    /// <summary>The End setup button: the game server's DropOffSlab rule removes each piece whose centre is off the table (edge inclusive).</summary>
    public static string EndSetupText(TableState? draft)
    {
        var off = draft is { } d ? d.Terrain.Count(t => !Tabletop.Geometry.Table.ContainsPoint(d.Size, t.Position.Horizontal)) : 0;
        return off switch
        {
            0 => "End setup",
            1 => "End setup (1 piece off the table will be removed)",
            _ => $"End setup ({off} pieces off the table will be removed)",
        };
    }
}
