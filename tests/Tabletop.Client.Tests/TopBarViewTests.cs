using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

/// <summary>Spec 2026-11-21-setup-screen-layout-design.md → Top bar and Game menu.</summary>
public class TopBarViewTests
{
    static readonly Guid Account = Guid.NewGuid(), Other = Guid.NewGuid();

    static GameInfo Game(GameStatus status, params Participant[] rows) => Samples.Game with { Status = status, CreatedBy = Account, Participants = rows };

    [Fact]
    public void A_chip_a_team_with_its_state_icon()
    {
        var game = Game(GameStatus.Setup, new Participant(Guid.NewGuid(), Account, "Ann", Samples.Team1, Guid.NewGuid(), "Orcs"));
        var bar = TopBarView.Of(game, isCreator: true, null)!;
        Assert.Equal([("Team 1", ColumnState.Ready, "ready"), ("Team 2", ColumnState.NeedsPlayer, "warning")], bar.Teams.Select(c => (c.Title, c.State, c.Icon)));
        Assert.Equal([1, 2], bar.Teams.Select(c => c.Ordinal));
        Assert.Equal([Samples.Team1, Samples.Team2], bar.Teams.Select(c => c.TeamId));
        Assert.Equal("Team 1: Ready · Ann · Orcs · 3.5", bar.Teams[0].Tooltip);
        Assert.Equal("Team 2: Needs a player", bar.Teams[1].Tooltip);
    }

    [Fact]
    public void To_do_counts_the_missing_lines_and_hides_at_none()
    {
        var game = Game(GameStatus.Setup, new Participant(Guid.NewGuid(), Account, "Ann", Samples.Team1, null));
        var bar = TopBarView.Of(game, isCreator: true, null)!;
        Assert.Equal(["Team 2 needs a player", "Team 1 needs an army"], bar.ToDo.Select(l => l.Text));
        Assert.Equal("2 to do", bar.ToDoText);
        var ready = Game(GameStatus.Setup, new Participant(Guid.NewGuid(), Account, "Ann", Samples.Team1, Guid.NewGuid()),
            new Participant(Guid.NewGuid(), Other, "Ben", Samples.Team2, Guid.NewGuid()));
        Assert.Null(TopBarView.Of(ready, isCreator: true, null)!.ToDoText);
        Assert.Empty(TopBarView.Of(ready, isCreator: true, null)!.ToDo);
        Assert.Equal("1 to do", TopBarView.Of(ready with { Participants = [ready.Participants[0]] }, isCreator: true, null)!.ToDoText);
    }

    [Fact]
    public void The_creator_starts_and_ends_setup_and_everyone_else_reads_who_does()
    {
        var ann = new Participant(Guid.NewGuid(), Account, "Ann", Samples.Team1, null);
        var lobby = Game(GameStatus.Lobby, ann, new Participant(Guid.NewGuid(), Other, "Ben", null, null));
        var mine = TopBarView.Of(lobby, isCreator: true, null)!;
        Assert.Equal(("Start setup", (LobbyEdit?)new StartSetup(), (string?)null), (mine.Action, mine.Edit, mine.Waiting));
        var theirs = TopBarView.Of(lobby, isCreator: false, null)!;
        Assert.Equal(((string?)null, (LobbyEdit?)null, "Ann starts setup"), (theirs.Action, theirs.Edit, theirs.Waiting));
        var setup = lobby with { Status = GameStatus.Setup };
        var end = TopBarView.Of(setup, isCreator: true, null)!;
        Assert.Equal(("End setup", "End setup", (LobbyEdit?)new EndSetup()), (end.Action, end.ActionTip, end.Edit));
        Assert.Equal("Ann ends setup", TopBarView.Of(setup, isCreator: false, null)!.Waiting);
        Assert.Null(TopBarView.Of(setup with { Status = GameStatus.Playing }, isCreator: true, null));
    }

    [Fact]
    public void End_setup_counts_the_pieces_off_the_table_it_will_remove()
    {
        var draft = Tabletop.Content.Tests.TableStateTests.Sample();
        var piece = draft.Terrain[0];
        var off = draft with { Terrain = [piece with { Id = Guid.NewGuid(), Position = new Tabletop.Geometry.Point3(-10, -10, 0) },
            piece with { Id = Guid.NewGuid(), Position = new Tabletop.Geometry.Point3(-20, -10, 0) }] };
        var bar = TopBarView.Of(Game(GameStatus.Setup), isCreator: true, off)!;
        Assert.Equal("End setup (2 off the table)", bar.Action);
        Assert.Equal("End setup (2 pieces off the table will be removed)", bar.ActionTip);
    }

    [Fact]
    public void One_account_on_both_teams_reads_both_ready()
    {
        var game = Game(GameStatus.Setup, new Participant(Guid.NewGuid(), Account, "Ann", Samples.Team1, Guid.NewGuid(), "Orcs"),
            new Participant(Guid.NewGuid(), Account, "Ann", Samples.Team2, Guid.NewGuid(), "Elves"));
        var bar = TopBarView.Of(game, isCreator: true, null)!;
        Assert.All(bar.Teams, c => Assert.Equal(ColumnState.Ready, c.State));
        Assert.Null(bar.ToDoText);
    }

    [Fact]
    public void The_game_menu_holds_resume_options_leave_and_home()
    {
        Assert.Equal([GameMenuItem.Resume, GameMenuItem.Options, GameMenuItem.Leave, GameMenuItem.Home], GameMenuView.Items(canLeave: true));
        Assert.Equal([GameMenuItem.Resume, GameMenuItem.Options, GameMenuItem.Home], GameMenuView.Items(canLeave: false));
        Assert.Equal(["Resume", "Options", "Leave game", "Home"], GameMenuView.Items(true).Select(GameMenuView.Text));
    }

    [Fact]
    public void Leave_game_is_offered_only_to_a_seated_player_who_is_not_watching()
    {
        Assert.True(GameMenuView.CanLeave(participant: true, watching: false));
        Assert.False(GameMenuView.CanLeave(participant: false, watching: false));
        Assert.False(GameMenuView.CanLeave(participant: true, watching: true));   // a replay, or an account with no team
    }

    [Fact]
    public void Esc_opens_the_menu_when_nothing_else_takes_it() => Assert.Equal(EscPress.Open, GameMenuView.OnEsc(new EscState()));

    [Fact]
    public void Esc_closes_the_open_menu_whatever_else_is_going_on() =>
        Assert.Equal(EscPress.Close, GameMenuView.OnEsc(new EscState(MenuOpen: true, TextFocus: true, Selection: true, AskCancel: true, Measuring: true)));

    public static TheoryData<EscState> Busy => new()
    {
        new EscState(TextFocus: true), new EscState(Overlay: true), new EscState(AskCancel: true), new EscState(Selection: true),
        new EscState(Holding: true), new EscState(Picks: true), new EscState(Measuring: true),
    };

    [Theory, MemberData(nameof(Busy))]
    public void Esc_passes_to_whatever_uses_it_first(EscState state) => Assert.Equal(EscPress.Pass, GameMenuView.OnEsc(state));
}
