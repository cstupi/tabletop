using System.Net;
using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

/// <summary>Replay: the game opened read-only on /ws?watch=1, the table at a position in the log, nothing sent.</summary>
public class SessionReplayTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("tabletop-replay-tests").FullName;
    readonly FakeManagement _http = new();
    readonly FakeSocketFactory _sockets = new();
    readonly Session _s;
    static readonly GameInfo Game = Samples.Game with { Status = GameStatus.Ended };
    static readonly AccountRow Clay = new(Game.Participants[0].AccountId, "Clay");

    public SessionReplayTests()
    {
        _s = new Session(new ManagementClient(new HttpClient(_http), new Uri("http://localhost:5100")), _sockets, new SessionStore(Path.Combine(_dir, "session.bin")),
            ClientSettings.Default, _ => { }, new ImmediateDispatcher(), _ => TimeSpan.Zero);
    }
    public void Dispose() => Directory.Delete(_dir, true);

    static GameEvent Score(long seq, int score) => new(seq, Samples.P1, new ScoreSet(Samples.Team1, score), null);

    static void Catchup(FakeGameSocket sock, int events = 3)
    {
        sock.Deliver(new Welcome(Guid.Empty, Game));
        sock.Deliver(new BoardMessage(Samples.Board));
        sock.Deliver(new EventsMessage(Enumerable.Range(1, events).Select(i => Score(i, i)).ToList()));
        sock.Deliver(new ModelFacts(new Dictionary<Guid, int>()));
    }

    async Task<FakeGameSocket> Replaying()
    {
        _http.Answer(HttpStatusCode.OK, new SessionResponse("a.jwt-1.z", Clay));
        await _s.LoginWithPasswordAsync("c@example.com", "pw");
        await _s.ReplayAsync(Game.Id);
        Catchup(_sockets.Last);
        return _sockets.Last;
    }

    [Fact]
    public async Task A_replay_connects_read_only_and_starts_at_the_board()
    {
        var sock = await Replaying();
        Assert.Equal(new Uri("ws://127.0.0.1:5200/ws?watch=1"), sock.Url);
        Assert.Equal(Game.Id, Assert.IsType<Connect>(Assert.Single(sock.Sent)).GameId);
        Assert.True(_s.Replaying && _s.IsWatching);
        Assert.Equal((0, 3), (_s.Replay!.Position, _s.Replay.Count));
        Assert.Equal(0, _s.Projection!.Scores.GetValueOrDefault(Samples.Team1));
        Assert.Empty(_s.Log);
        Assert.Equal(3, _s.Applier!.Log.Count);                             // the live log underneath
    }

    [Fact]
    public async Task Seeking_moves_the_table_and_the_log_forward_with_the_events_and_back_with_a_redraw()
    {
        await Replaying();
        var batches = new List<IReadOnlyList<GameEvent>>(); var boards = 0;
        _s.EventsApplied += batches.Add; _s.BoardChanged += () => boards++;

        _s.SeekReplay(2, animate: true);
        Assert.Equal([1L, 2L], Assert.Single(batches).Select(e => e.Seq));
        Assert.Equal((2, 2, 0), (_s.Projection!.Scores[Samples.Team1], _s.Log.Count, boards));

        _s.SeekReplay(1, animate: true);                                    // backward never animates
        Assert.Equal((1, 1, 1), (_s.Projection!.Scores[Samples.Team1], _s.Log.Count, boards));
        Assert.Empty(batches[^1]);

        _s.SeekReplay(3);                                                   // a jump redraws
        Assert.Equal((3, 2), (_s.Projection!.Scores[Samples.Team1], boards));
    }

    [Fact]
    public async Task A_replay_sends_nothing_that_changes_the_game()
    {
        var sock = await Replaying();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _s.SendCommandAsync(new SetScore(Samples.Team1, 9)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _s.SendEditAsync(new CreateTeam()));
        await _s.UndoToAsync(1);
        await _s.QuerySightAsync(Guid.CreateVersion7(), Samples.Unit, Samples.Unit);
        await _s.SendPresenceAsync(new Presence(Guid.Empty, null, null, [], null));
        await _s.SendPingAsync(new Ping(Guid.Empty, new(1, 1, 0), null));
        Assert.Single(sock.Sent);                                           // the connect frame alone
        Assert.Null(_s.PendingUndoTo);
    }

    [Fact]
    public async Task Live_events_extend_the_timeline_and_leave_the_table_where_it_is()
    {
        var sock = await Replaying();
        _s.SeekReplay(1);
        sock.Deliver(new EventsMessage([Score(4, 4)]));
        Assert.Equal((1, 4), (_s.Replay!.Position, _s.Replay.Count));
        Assert.Equal(1, _s.Projection!.Scores[Samples.Team1]);
    }

    [Fact]
    public async Task A_reconnect_returns_the_replay_to_where_it_was()
    {
        var sock = await Replaying();
        _s.SeekReplay(2);
        sock.Drop(1001, "going away");
        var second = await _sockets.ConnectedAsync(2);
        Assert.Equal("?watch=1", second.Url!.Query);
        Catchup(second, events: 4);
        Assert.Equal((2, 4), (_s.Replay!.Position, _s.Replay.Count));
        Assert.True(_s.Replaying);
    }

    [Fact]
    public async Task Going_home_ends_the_replay_and_a_join_plays_again()
    {
        await Replaying();
        await _s.DisconnectAsync();
        Assert.False(_s.Replaying);
        Assert.Null(_s.Replay);
        await _s.ConnectAsync(Game.Id);
        Assert.Equal(new Uri("ws://127.0.0.1:5200/ws"), _sockets.Last.Url);
        _sockets.Last.Deliver(new Welcome(Samples.P1, Game));
        _sockets.Last.Deliver(new BoardMessage(Samples.Board));
        _sockets.Last.Deliver(new EventsMessage([Score(1, 5)]));
        Assert.False(_s.Replaying);
        Assert.Null(_s.Replay);
        Assert.Equal(5, _s.Projection!.Scores[Samples.Team1]);
    }
}
