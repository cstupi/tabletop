using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>A column's state: a player and an army (Ready), no player, players but no army; Watching is the column of everyone without a team.</summary>
public enum ColumnState { Ready, NeedsPlayer, NeedsArmy, Watching }

/// <summary>One participant row in a column. Army is "Name · 3.6", null with none; NoArmyWarns when the team has no army at all. MyArmy is my
/// picker on my own row. Moves are the targets this viewer may send the row to (spec 2026-11-07-lobby-and-setup-redesign-design.md).</summary>
public sealed record ColumnMember(Guid ParticipantId, string Name, string? Army, bool NoArmyWarns, bool You, ArmySeat? MyArmy,
    IReadOnlyList<ColumnMove> Moves);

/// <summary>A row's move: the menu text and its edit; TeamId null is Watching.</summary>
public sealed record ColumnMove(Guid? TeamId, string Text, PickTeam Edit);

/// <summary>A column's one button: Join (or Watch on Watching), enabled or not with the line that says why.</summary>
public sealed record ColumnJoin(string Text, PickTeam? Edit, string? Why)
{
    public bool Enabled => Edit is not null;
}

/// <summary>One Still to do line; Missing for a team that lacks a player or an army (or no teams at all), drawn as a warning.</summary>
public sealed record ToDoLine(string Text, bool Missing);

/// <summary>One column: a team by ordinal (Ordinal 0 and TeamId null for Watching), its members, its button and Also play this side.</summary>
public sealed record TeamColumn(Guid? TeamId, int Ordinal, string Title, ColumnState State, string StateText, IReadOnlyList<ColumnMember> Members,
    ColumnJoin? Join, PickTeam? AlsoPlay);

/// <summary>
/// The lobby's and setup's team columns (spec 2026-11-07-lobby-and-setup-redesign-design.md): one column per team, then Watching. Every move is a
/// PickTeam the game server's lobby authority accepts: a player takes an empty team, moves their own seat onto one no other account holds, or
/// adds one (one account, several teams); the creator places anyone anywhere. Nothing is offered that the server would refuse.
/// </summary>
public static class TeamColumns
{
    public const string Watching = "Watching", WatchingState = "Watches once play starts", Join = "Join", Watch = "Watch",
        AlsoPlay = "Also play this side", NoArmy = "no army yet", AddTeam = "+ Add team", AddTeamCompact = "+ Team", You = "(you)";

    public static string StateText(ColumnState s) => s switch
    {
        ColumnState.Ready => "Ready",
        ColumnState.NeedsPlayer => "Needs a player",
        ColumnState.NeedsArmy => "Needs an army",
        _ => WatchingState,
    };

    /// <summary>A team's state from the lobby rows: WaitingForPlayers' rule (a player, and an army on one of its seats).</summary>
    public static ColumnState StateOf(GameInfo game, Guid teamId)
    {
        var seats = game.Participants.Where(p => p.TeamId == teamId).ToList();
        return seats.Count == 0 ? ColumnState.NeedsPlayer : seats.Any(p => p.ArmyId is not null) ? ColumnState.Ready : ColumnState.NeedsArmy;
    }

    /// <summary>The creator's name from the participant rows; "the creator" once they have left.</summary>
    public static string CreatorName(GameInfo game) => game.Participants.FirstOrDefault(p => p.AccountId == game.CreatedBy)?.Name ?? "the creator";

    /// <summary>The army line: its name and the game's edition (the server clears an army of another edition), null with no army.</summary>
    public static string? ArmyText(GameInfo game, Participant p)
    {
        var name = p.ArmyName ?? (p.ArmyId is null ? null : "army");
        if (name is null) return null;
        return RuleSets.All.Contains(game.GameRules) ? $"{name} · {RuleSets.EditionOf(game.GameRules)}" : name;
    }

    /// <summary>The teams by ordinal, then Watching. <paramref name="compact"/> (setup's side column) leaves Watching out while no one watches.</summary>
    public static IReadOnlyList<TeamColumn> Of(GameInfo game, Guid? participantId, bool isCreator, bool compact = false)
    {
        var mine = Seats.Mine(game, participantId);
        var account = mine.FirstOrDefault()?.AccountId;
        var held = mine.Where(p => p.TeamId is not null).ToList();
        var columns = new List<TeamColumn>();
        foreach (var t in game.Teams.OrderBy(t => t.Ordinal))
        {
            var state = StateOf(game, t.Id);
            var members = game.Participants.Where(p => p.TeamId == t.Id).Select(p => Member(game, p, participantId, isCreator, state == ColumnState.NeedsArmy)).ToList();
            ColumnJoin? join = null; PickTeam? also = null;
            if (account is { } me && mine.All(p => p.TeamId != t.Id))
            {
                var holders = game.Participants.Where(p => p.TeamId == t.Id && p.AccountId != me).Select(p => p.Name).Distinct().ToList();
                if (holders.Count > 0 && !isCreator)
                    join = new ColumnJoin(Join, null, $"{string.Join(", ", holders)} {(holders.Count == 1 ? "holds" : "hold")} this team: the creator adds players to it.");
                else if (held.Count == 1)
                {
                    join = new ColumnJoin(Join, new PickTeam(t.Id, held[0].Id), null);   // moves my one seat, its army with it
                    also = new PickTeam(t.Id);                                            // a second seat: one account playing both sides
                }
                else join = new ColumnJoin(Join, new PickTeam(t.Id), null);              // takes my teamless row, or adds a seat
            }
            columns.Add(new TeamColumn(t.Id, t.Ordinal, $"Team {t.Ordinal}", state, StateText(state), members, join, also));
        }
        var watching = game.Participants.Where(p => p.TeamId is null).Select(p => Member(game, p, participantId, isCreator, false)).ToList();
        if (!compact || watching.Count > 0)
            columns.Add(new TeamColumn(null, 0, Watching, ColumnState.Watching, WatchingState, watching,
                held.Count > 0 ? new ColumnJoin(Watch, LobbyEdits.Watch(), null) : null, null));
        return columns;
    }

    static ColumnMember Member(GameInfo game, Participant p, Guid? participantId, bool isCreator, bool teamNeedsArmy)
    {
        var you = Seats.Mine(game, participantId).Any(s => s.Id == p.Id);
        var army = you ? LobbyView.Armies(game, participantId).FirstOrDefault(a => a.TeamId == p.TeamId) : null;
        return new ColumnMember(p.Id, p.Name, ArmyText(game, p), teamNeedsArmy, you, army, Moves(game, participantId, isCreator, p.Id));
    }

    /// <summary>Where this viewer may send a row: the creator any row, anyone else their own; another column, never one the row's account already
    /// holds, never (for a non-creator) a team another account holds. "Watch" last, for a row on a team.</summary>
    public static IReadOnlyList<ColumnMove> Moves(GameInfo game, Guid? participantId, bool isCreator, Guid rowId)
    {
        if (game.Participants.FirstOrDefault(p => p.Id == rowId) is not { } row) return [];
        var mine = Seats.Mine(game, participantId);
        if (mine.Count == 0 || (!isCreator && mine.All(s => s.Id != rowId))) return [];
        var moves = new List<ColumnMove>();
        foreach (var t in game.Teams.OrderBy(t => t.Ordinal))
        {
            if (row.TeamId == t.Id || game.Participants.Any(p => p.TeamId == t.Id && p.AccountId == row.AccountId)) continue;
            if (!isCreator && game.Participants.Any(p => p.TeamId == t.Id && p.AccountId != row.AccountId)) continue;
            moves.Add(new ColumnMove(t.Id, $"Move to Team {t.Ordinal}", new PickTeam(t.Id, rowId)));
        }
        if (row.TeamId is not null) moves.Add(new ColumnMove(null, Watch, new PickTeam(null, rowId)));
        return moves;
    }

    /// <summary>A row dropped on a column (null: Watching): the move it makes, or null when the row may not go there.</summary>
    public static PickTeam? MoveTo(GameInfo game, Guid? participantId, bool isCreator, Guid rowId, Guid? teamId) =>
        Moves(game, participantId, isCreator, rowId).FirstOrDefault(m => m.TeamId == teamId)?.Edit;

    /// <summary>The Still to do lines (spec → Still to do), from the lobby rows alone; empty outside lobby and setup.</summary>
    public static IReadOnlyList<ToDoLine> StillToDo(GameInfo game, bool isCreator)
    {
        if (game.Status is not (GameStatus.Lobby or GameStatus.Setup)) return [];
        var who = CreatorName(game);
        var lines = new List<ToDoLine>();
        if (game.Teams.Count == 0) lines.Add(new(isCreator ? "Add a team for each side" : $"{who} adds the teams", true));
        var teams = game.Teams.OrderBy(t => t.Ordinal).Select(t => (t.Ordinal, State: StateOf(game, t.Id))).ToList();
        lines.AddRange(teams.Where(t => t.State == ColumnState.NeedsPlayer).Select(t => new ToDoLine($"Team {t.Ordinal} needs a player", true)));
        lines.AddRange(teams.Where(t => t.State == ColumnState.NeedsArmy).Select(t => new ToDoLine($"Team {t.Ordinal} needs an army", true)));
        if (game.Status == GameStatus.Lobby) lines.Add(new(isCreator ? "You start setup" : $"{who} starts setup", false));
        else { lines.Add(new("Anyone may edit the table", false)); lines.Add(new(isCreator ? "You end setup" : $"{who} ends setup", false)); }
        return lines;
    }

    /// <summary>The participants whose presence selects or holds a piece: who is editing the table now.</summary>
    public static IReadOnlySet<Guid> Editing(IEnumerable<Presence> presences) =>
        presences.Where(p => p.Selection is not null || p.Placements.Count > 0).Select(p => p.ParticipantId).ToHashSet();
}
