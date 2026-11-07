using System.Net;
using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class HomeViewTests
{
    static readonly Guid Me = Guid.CreateVersion7(), Other = Guid.CreateVersion7();
    static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    static GameSummary Game(GameStatus status, bool joined = true, Guid? by = null, string name = "g", GameProgress? progress = null, int teams = 0, int ready = 0,
        string[]? players = null, DateTimeOffset? at = null) =>
        new(Guid.CreateVersion7(), name, status, by ?? Me, at ?? Now.AddHours(-2), players?.Length ?? 1, joined, "Grimdark Future v3.6.0", players ?? ["Clay"], teams, ready, progress);

    static HomeCard Card(GameSummary g) => HomeView.CardOf(g, Me, Now);

    [Fact]
    public void Joined_games_are_yours_the_rest_not_ended_are_open_and_ended_ones_fold_until_asked()
    {
        var lobby = Game(GameStatus.Lobby); var open = Game(GameStatus.Setup, joined: false); var ended = Game(GameStatus.Ended);
        var notMineEnded = Game(GameStatus.Ended, joined: false);
        var lists = HomeView.Of([lobby, open, ended, notMineEnded], Me, "", endedToo: false, Now);
        Assert.Equal([lobby.Id], lists.Yours.Select(c => c.Game.Id));
        Assert.Equal([open.Id], lists.Open.Select(c => c.Game.Id));
        Assert.Empty(lists.Ended);
        Assert.Equal([ended.Id], HomeView.Of([lobby, open, ended, notMineEnded], Me, "", endedToo: true, Now).Ended.Select(c => c.Game.Id));
    }

    [Fact]
    public void Newest_first_and_the_name_filter_applies_to_every_section()
    {
        var older = Game(GameStatus.Lobby, name: "Friday night", at: Now.AddDays(-3)); var newer = Game(GameStatus.Playing, name: "Ruins run", at: Now.AddDays(-1));
        Assert.Equal([newer.Id, older.Id], HomeView.Of([older, newer], Me, "", false, Now).Yours.Select(c => c.Game.Id));
        Assert.Equal([older.Id], HomeView.Of([older, newer], Me, "fri", false, Now).Yours.Select(c => c.Game.Id));
    }

    [Fact]
    public void The_primary_follows_the_status_and_open_games_join()
    {
        Assert.Equal(HomeAction.Open, Card(Game(GameStatus.Lobby)).Primary);
        Assert.Equal(HomeAction.Continue, Card(Game(GameStatus.Setup)).Primary);
        Assert.Equal(HomeAction.Play, Card(Game(GameStatus.Playing)).Primary);
        Assert.Equal(HomeAction.Replay, Card(Game(GameStatus.Ended)).Primary);
        Assert.Equal(HomeAction.Join, Card(Game(GameStatus.Lobby, joined: false)).Primary);
        Assert.Equal(HomeAction.Join, Card(Game(GameStatus.Playing, joined: false)).Primary);
    }

    [Fact]
    public void The_menu_holds_replay_on_a_playing_game_end_and_delete_for_the_creator_and_leave()
    {
        Assert.Equal([HomeAction.Replay, HomeAction.End, HomeAction.Delete, HomeAction.Leave], Card(Game(GameStatus.Playing)).Menu);
        Assert.Equal([HomeAction.Leave], Card(Game(GameStatus.Lobby, by: Other)).Menu);
        Assert.Equal([HomeAction.Delete], Card(Game(GameStatus.Ended)).Menu);
        Assert.Empty(Card(Game(GameStatus.Ended, by: Other)).Menu);
        Assert.Empty(Card(Game(GameStatus.Lobby, joined: false)).Menu);
    }

    [Fact]
    public void The_edition_is_the_rule_sets_version_and_players_show_three_then_a_count()
    {
        var card = Card(Game(GameStatus.Lobby, players: ["Clay", "Ann", "Ben", "Cid", "Dee"]));
        Assert.Equal("3.6.0", card.Edition);
        Assert.Equal(["Clay", "Ann", "Ben"], card.Players);
        Assert.Equal(2, card.MorePlayers);
        Assert.Equal("Grimdark Future core", HomeView.EditionOf("Grimdark Future core"));
        Assert.Equal("", HomeView.EditionOf(null));
    }

    [Fact]
    public void The_lobby_line_counts_ready_teams_and_setup_lays_the_table()
    {
        Assert.Equal(("1 of 2 teams ready", false), Card(Game(GameStatus.Lobby, teams: 2, ready: 1)) is var c ? (c.State, c.StateWarns) : default);
        Assert.Equal("2 hours ago", Card(Game(GameStatus.Lobby)).State);
        Assert.Equal("Laying the table", Card(Game(GameStatus.Setup)).State);
    }

    [Fact]
    public void A_playing_game_says_your_move_when_an_ask_waits_on_me_else_the_round_and_the_acting_team()
    {
        var mine = Card(Game(GameStatus.Playing, progress: new GameProgress(2, 1, [Me], null)));
        Assert.Equal(("Your move", true), (mine.State, mine.StateWarns));
        Assert.Equal(("Round 3 · Team 2's turn", false), Card(Game(GameStatus.Playing, progress: new GameProgress(3, 2, [Other], null))) is var c ? (c.State, c.StateWarns) : default);
        Assert.Equal("Deployment · Team 1's turn", Card(Game(GameStatus.Playing, progress: new GameProgress(0, 1, [], null))).State);
        Assert.Equal("Round 4", Card(Game(GameStatus.Playing, progress: new GameProgress(4, null, [], null))).State);
        Assert.Equal("2 hours ago", Card(Game(GameStatus.Playing)).State);
    }

    [Fact]
    public void An_ended_game_names_its_outcome()
    {
        Assert.Equal("Team 1 wins", Card(Game(GameStatus.Ended, progress: new GameProgress(4, null, [], [1]))).State);
        Assert.Equal("Draw", Card(Game(GameStatus.Ended, progress: new GameProgress(4, null, [], [1, 2]))).State);
        Assert.Equal("No result", Card(Game(GameStatus.Ended, progress: new GameProgress(4, null, [], []))).State);
        Assert.Equal("2 hours ago", Card(Game(GameStatus.Ended)).State);
    }

    [Theory]
    [InlineData(0.2, "just now")]
    [InlineData(5, "5 minutes ago")]
    [InlineData(1, "1 minute ago")]
    [InlineData(180, "3 hours ago")]
    [InlineData(60 * 30, "yesterday")]
    [InlineData(60 * 24 * 4, "4 days ago")]
    public void Ago_words_the_time_since(double minutes, string words) => Assert.Equal(words, HomeView.Ago(Now.AddMinutes(-minutes), Now));

    [Fact]
    public void Ago_past_two_weeks_is_the_date() => Assert.Equal("19 Oct 2026", HomeView.Ago(new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero), Now));

    [Fact]
    public async Task End_posts_delete_deletes_and_leave_posts()
    {
        var f = new FakeManagement(); var c = new ManagementClient(new HttpClient(f), new Uri("http://localhost:5100"));
        var id = Guid.CreateVersion7();
        f.Answer(HttpStatusCode.NoContent).Answer(HttpStatusCode.NoContent).Answer(HttpStatusCode.NoContent);
        await c.EndGame(id); await c.DeleteGame(id); await c.LeaveGame(id);
        Assert.Equal((HttpMethod.Post, $"/v1/games/{id}/end"), (f.Requests[0].Method, f.Requests[0].RequestUri!.AbsolutePath));
        Assert.Equal((HttpMethod.Delete, $"/v1/games/{id}"), (f.Requests[1].Method, f.Requests[1].RequestUri!.AbsolutePath));
        Assert.Equal((HttpMethod.Post, $"/v1/games/{id}/leave"), (f.Requests[2].Method, f.Requests[2].RequestUri!.AbsolutePath));
    }
}
