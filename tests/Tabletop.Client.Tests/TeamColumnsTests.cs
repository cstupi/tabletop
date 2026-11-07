using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

/// <summary>Spec 2026-11-07-lobby-and-setup-redesign-design.md: one column per team, then Watching; Join, Also play, Move and Watch as PickTeam.</summary>
public class TeamColumnsTests
{
    static readonly Guid T1 = Guid.CreateVersion7(), T2 = Guid.CreateVersion7(), T3 = Guid.CreateVersion7();
    static readonly Guid AnnAcct = Guid.CreateVersion7(), BenAcct = Guid.CreateVersion7(), CidAcct = Guid.CreateVersion7(), DeeAcct = Guid.CreateVersion7();
    static readonly Guid Ann = Guid.CreateVersion7(), Ben = Guid.CreateVersion7(), Cid = Guid.CreateVersion7(), Dee = Guid.CreateVersion7();
    static readonly Guid Army = Guid.CreateVersion7();

    /// <summary>Ann (the creator) and Ben on Team 1, Ann with an army; Cid on Team 2 with one; Team 3 empty; Dee teamless.</summary>
    static GameInfo Game => new(Guid.CreateVersion7(), "g", GameStatus.Lobby, AnnAcct, RuleSets.Gf360, "h", SchedulerShape.AlternatingActivation, Enforcement.Warn, false, 0,
        [new Team(T2, 2), new Team(T1, 1), new Team(T3, 3)],
        [new Participant(Ann, AnnAcct, "Ann", T1, Army, "Iron Legion"), new Participant(Ben, BenAcct, "Ben", T1, null),
         new Participant(Cid, CidAcct, "Cid", T2, Guid.CreateVersion7(), "Orc Marauders"), new Participant(Dee, DeeAcct, "Dee", null, null)]);

    [Fact]
    public void Columns_are_the_teams_by_ordinal_then_watching_each_with_its_state_and_members()
    {
        var columns = TeamColumns.Of(Game, Ann, isCreator: true);
        Assert.Equal(["Team 1", "Team 2", "Team 3", "Watching"], columns.Select(c => c.Title));
        Assert.Equal([1, 2, 3, 0], columns.Select(c => c.Ordinal));
        Assert.Equal(["Ready", "Ready", "Needs a player", "Watches once play starts"], columns.Select(c => c.StateText));
        Assert.Equal([["Ann", "Ben"], ["Cid"], [], ["Dee"]], columns.Select(c => c.Members.Select(m => m.Name).ToArray()));
        var ann = columns[0].Members[0];
        Assert.Equal(("Iron Legion · 3.6", true), (ann.Army, ann.You));
        Assert.Equal(new ArmySeat(T1, "Your army", Army), ann.MyArmy);
        Assert.Null(columns[0].Members[1].Army);
        Assert.False(columns[0].Members[1].NoArmyWarns);                         // Ann brings Team 1's army
        Assert.Null(columns[0].Members[1].MyArmy);
    }

    [Fact]
    public void A_team_whose_players_have_no_army_needs_one_and_its_rows_warn()
    {
        var game = Game with { Participants = [.. Game.Participants.Select(p => p.Id == Ann ? p with { ArmyId = null, ArmyName = null } : p)] };
        var team1 = TeamColumns.Of(game, Ann, isCreator: true)[0];
        Assert.Equal((ColumnState.NeedsArmy, "Needs an army"), (team1.State, team1.StateText));
        Assert.All(team1.Members, m => Assert.True(m.NoArmyWarns));
    }

    [Fact]
    public void Holding_one_team_join_moves_my_seat_and_also_play_adds_one()
    {
        var team3 = TeamColumns.Of(Game, Cid, isCreator: false)[2];
        Assert.Equal(new ColumnJoin("Join", new PickTeam(T3, Cid), null), team3.Join);
        Assert.Equal(new PickTeam(T3), team3.AlsoPlay);
        Assert.Null(TeamColumns.Of(Game, Cid, isCreator: false)[1].Join);        // my own column has no Join
    }

    [Fact]
    public void A_team_another_account_holds_is_joined_only_by_the_creator()
    {
        var team1 = TeamColumns.Of(Game, Cid, isCreator: false)[0];
        Assert.False(team1.Join!.Enabled);
        Assert.Equal("Ann, Ben hold this team: the creator adds players to it.", team1.Join.Why);
        Assert.Null(team1.AlsoPlay);
        Assert.Equal(new PickTeam(T2, Ann), TeamColumns.Of(Game, Ann, isCreator: true)[1].Join!.Edit);
    }

    [Fact]
    public void Without_a_team_join_takes_it_and_watching_offers_no_button()
    {
        var columns = TeamColumns.Of(Game, Dee, isCreator: false);
        Assert.Equal(new ColumnJoin("Join", new PickTeam(T3), null), columns[2].Join);
        Assert.Null(columns[2].AlsoPlay);
        Assert.Null(columns[3].Join);
        Assert.Equal(new ColumnJoin("Watch", new PickTeam(null), null), TeamColumns.Of(Game, Cid, isCreator: false)[3].Join);
    }

    [Fact]
    public void A_watcher_admin_gets_no_buttons_and_no_moves()
    {
        var columns = TeamColumns.Of(Game, Guid.Empty, isCreator: false);
        Assert.All(columns, c => { Assert.Null(c.Join); Assert.Null(c.AlsoPlay); Assert.All(c.Members, m => Assert.Empty(m.Moves)); });
    }

    [Fact]
    public void The_creator_moves_any_row_a_player_only_their_own_and_never_where_the_server_refuses()
    {
        Assert.Equal(["Move to Team 2", "Move to Team 3", "Watch"], TeamColumns.Moves(Game, Ann, true, Ben).Select(m => m.Text));
        Assert.Equal(new PickTeam(T2, Ben), TeamColumns.Moves(Game, Ann, true, Ben)[0].Edit);
        Assert.Equal(new PickTeam(null, Ben), TeamColumns.Moves(Game, Ann, true, Ben)[2].Edit);
        Assert.Equal(["Move to Team 1", "Move to Team 2", "Move to Team 3"], TeamColumns.Moves(Game, Ann, true, Dee).Select(m => m.Text));
        Assert.Empty(TeamColumns.Moves(Game, Cid, false, Ben));                   // not mine
        Assert.Equal(["Move to Team 3", "Watch"], TeamColumns.Moves(Game, Ben, false, Ben).Select(m => m.Text));   // Team 2 is Cid's
        Assert.Equal(new PickTeam(T3, Ben), TeamColumns.MoveTo(Game, Ben, false, Ben, T3));
        Assert.Null(TeamColumns.MoveTo(Game, Ben, false, Ben, T2));
        Assert.Null(TeamColumns.MoveTo(Game, Ben, false, Ben, T1));               // already there
    }

    /// <summary>One account on two teams: a row per team, and no move onto a team the account already holds.</summary>
    [Fact]
    public void Self_play_shows_a_row_in_each_column_and_never_moves_onto_its_other_team()
    {
        var second = Guid.CreateVersion7();
        var game = Game with { Participants = [.. Game.Participants.Where(p => p.Id != Cid), new Participant(second, AnnAcct, "Ann", T2, null)] };
        var columns = TeamColumns.Of(game, Ann, isCreator: true);
        Assert.True(columns[1].Members.Single().You);
        Assert.Equal(new ArmySeat(T2, "Your army for Team 2", null), columns[1].Members.Single().MyArmy);
        Assert.Equal(["Move to Team 3", "Watch"], TeamColumns.Moves(game, Ann, true, second).Select(m => m.Text));
        Assert.Equal(new ColumnJoin("Join", new PickTeam(T3), null), columns[2].Join);   // several held: Join adds
        Assert.Null(columns[2].AlsoPlay);
    }

    [Fact]
    public void Compact_columns_leave_watching_out_while_no_one_watches()
    {
        var game = Game with { Participants = [.. Game.Participants.Where(p => p.Id != Dee)] };
        Assert.DoesNotContain(TeamColumns.Of(game, Ann, true, compact: true), c => c.TeamId is null);
        Assert.Contains(TeamColumns.Of(game, Ann, true), c => c.TeamId is null);
        Assert.Contains(TeamColumns.Of(Game, Ann, true, compact: true), c => c.TeamId is null);
    }

    [Fact]
    public void Still_to_do_names_each_team_that_waits_then_the_phase_step()
    {
        var game = Game with { Participants = [.. Game.Participants.Select(p => p.Id == Cid ? p with { ArmyId = null, ArmyName = null } : p)] };
        Assert.Equal(["Team 3 needs a player", "Team 2 needs an army", "You start setup"], TeamColumns.StillToDo(game, isCreator: true).Select(l => l.Text));
        Assert.Equal(["Team 3 needs a player", "Team 2 needs an army", "Ann starts setup"], TeamColumns.StillToDo(game, isCreator: false).Select(l => l.Text));
        Assert.Equal(["Team 3 needs a player", "Team 2 needs an army", "Anyone may edit the table", "Ann ends setup"],
            TeamColumns.StillToDo(game with { Status = GameStatus.Setup }, isCreator: false).Select(l => l.Text));
        Assert.Equal(["Add a team for each side", "You start setup"], TeamColumns.StillToDo(game with { Teams = [] }, isCreator: true).Select(l => l.Text));
        Assert.Equal(["the creator adds the teams", "the creator starts setup"],
            TeamColumns.StillToDo(game with { Teams = [], Participants = [.. game.Participants.Where(p => p.Id != Ann)] }, isCreator: false).Select(l => l.Text));
        Assert.Empty(TeamColumns.StillToDo(game with { Status = GameStatus.Playing }, isCreator: true));
        Assert.Equal([true, true, false], TeamColumns.StillToDo(game, isCreator: true).Select(l => l.Missing));
    }

    [Fact]
    public void Editing_is_whoever_selects_or_holds_a_piece()
    {
        var piece = Guid.CreateVersion7();
        Presence P(Guid who, Selection? s = null, params PresencePlacement[] held) => new(who, null, s, held, null);
        var editing = TeamColumns.Editing([P(Ann), P(Ben, new Selection(piece, [])), P(Cid, null, new PresencePlacement(piece, new(1, 1, 0), new(0), PlacementKind.Held))]);
        Assert.Equal(new HashSet<Guid> { Ben, Cid }, editing);
    }
}
