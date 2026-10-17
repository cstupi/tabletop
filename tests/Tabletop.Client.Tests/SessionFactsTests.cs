using System.Net;
using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class SessionFactsTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("tabletop-session-facts-tests").FullName;
    readonly FakeManagement _http = new();
    readonly FakeSocketFactory _sockets = new();
    readonly Session _s;
    static readonly GameInfo Game = Samples.Game;                       // read once: the property mints new ids per read
    static readonly AccountRow Clay = new(Game.Participants[0].AccountId, "Clay");
    static readonly RuleTextDto Tough = new("Tough", "model", true, "Must take X wounds to be killed.");
    static readonly Guid Target = Guid.Parse("0198b7ac-0031-7000-8000-000000000031");

    public SessionFactsTests()
    {
        _s = new Session(new ManagementClient(new HttpClient(_http), new Uri("http://localhost:5100")), _sockets,
            new SessionStore(Path.Combine(_dir, "session.bin")), ClientSettings.Default, _ => { }, new ImmediateDispatcher(), _ => TimeSpan.Zero);
    }
    public void Dispose() => Directory.Delete(_dir, true);

    int RuleRequests => _http.Requests.Count(r => r.RequestUri!.AbsolutePath == "/v1/properties/sha256:abc/rules");

    static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++) await Task.Delay(10);
        Assert.True(done());
    }

    async Task LoggedIn()
    {
        _http.Answer(HttpStatusCode.OK, new SessionResponse("a.jwt-1.z", Clay));
        await _s.LoginWithPasswordAsync("c@example.com", "pw");
    }

    async Task<FakeGameSocket> Connected(GameInfo game)
    {
        await LoggedIn();
        await _s.ConnectAsync(game.Id);
        var sock = _sockets.Last;
        sock.Deliver(new Welcome(Samples.P1, game));
        sock.Deliver(new BoardMessage(Samples.Board));
        sock.Deliver(new EventsMessage([]));
        return sock;
    }

    [Fact]
    public async Task Model_facts_replace_max_wounds_and_a_reconnect_clears_them()
    {
        var sock = await Connected(Game);
        var raised = 0; _s.FactsChanged += () => raised++;
        Assert.Empty(_s.MaxWounds);
        sock.Deliver(new ModelFacts(new Dictionary<Guid, int> { [Samples.Model] = 3 }));
        Assert.Equal((3, 1), (_s.MaxWounds[Samples.Model], raised));
        sock.Deliver(new ModelFacts(new Dictionary<Guid, int> { [Target] = 2 }));
        Assert.Equal(new Dictionary<Guid, int> { [Target] = 2 }, _s.MaxWounds);
        Assert.Equal(2, raised);
        Assert.Empty(_s.Log);                                   // facts are not events
        sock.Drop(1001, "server shutting down");
        await Task.Delay(50);
        Assert.Empty(_s.MaxWounds);
    }

    [Fact]
    public async Task Model_facts_carry_where_each_unit_may_deploy_inside()
    {
        var sock = await Connected(Game);
        Assert.Empty(_s.DeployInside);
        sock.Deliver(new ModelFacts(new Dictionary<Guid, int>(), new Dictionary<Guid, IReadOnlyList<Guid>> { [Samples.Unit] = [Target] }));
        Assert.Equal([Target], _s.DeployInside[Samples.Unit]);
        sock.Deliver(new ModelFacts(new Dictionary<Guid, int>()));                                   // a server that sends none: none
        Assert.Empty(_s.DeployInside);
    }

    [Fact]
    public async Task A_sight_query_goes_out_and_only_its_answer_comes_back()
    {
        await LoggedIn();
        await _s.ConnectAsync(Game.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _s.QuerySightAsync(Guid.CreateVersion7(), Samples.Unit, Target));   // not welcomed yet
        var sock = _sockets.Last;
        sock.Deliver(new Welcome(Samples.P1, Game));
        sock.Deliver(new BoardMessage(Samples.Board));
        var id = Guid.CreateVersion7();                                    // the caller's id, known before the send: the newest query wins
        await _s.QuerySightAsync(id, Samples.Unit, Target);
        var query = Assert.IsType<SightQuery>(sock.Sent[^1]);
        Assert.Equal((id, Samples.Unit, Target), (query.QueryId, query.ObserverUnitId, query.TargetUnitId));
        SightAnswer? got = null; _s.SightAnswered += a => got = a;
        var applied = 0; _s.EventsApplied += _ => applied++;
        var answer = new SightAnswer(id, [new SightLine(Samples.Model, Guid.CreateVersion7(), true, false, [])], [Samples.Model]);
        sock.Deliver(answer);
        Assert.Same(answer, got);
        Assert.Equal(0, applied);
    }

    [Fact]
    public async Task Rule_texts_load_once_per_pinned_set_and_survive_a_reconnect()
    {
        await LoggedIn();
        _http.Answer(HttpStatusCode.OK, new[] { Tough });
        var raised = 0; _s.FactsChanged += () => raised++;
        await _s.ConnectAsync(Game.Id);
        _sockets.Last.Deliver(new Welcome(Samples.P1, Game));
        await Until(() => _s.Rules.IsAutomated("Tough"));
        Assert.Equal("Must take X wounds to be killed.", _s.Rules.TextOf("Tough"));
        Assert.Equal((1, 1), (RuleRequests, raised));
        _sockets.Last.Drop(1001, "server shutting down");
        await _sockets.ConnectedAsync(2);
        Assert.Same(RuleTexts.Empty, _s.Rules);                   // no game, no set
        _sockets.Last.Deliver(new Welcome(Samples.P1, Game));
        _sockets.Last.Deliver(new GameInfoMessage(Game));
        Assert.True(_s.Rules.IsAutomated("Tough"));
        Assert.Equal(1, RuleRequests);
    }

    [Fact]
    public async Task A_lobby_fetches_nothing_and_a_failed_fetch_is_tried_again()
    {
        await LoggedIn();
        await _s.ConnectAsync(Game.Id);
        _sockets.Last.Deliver(new Welcome(Samples.P1, Game with { Status = GameStatus.Lobby }));
        Assert.Equal(0, RuleRequests);
        _http.Answer(HttpStatusCode.InternalServerError, "{\"title\":\"boom\"}", "application/problem+json");
        _sockets.Last.Deliver(new GameInfoMessage(Game));                       // the flip to playing
        await Until(() => RuleRequests == 1);
        await Task.Delay(50);
        Assert.Same(RuleTexts.Empty, _s.Rules);
        _http.Answer(HttpStatusCode.OK, new[] { Tough });
        _sockets.Last.Deliver(new GameInfoMessage(Game));
        await Until(() => _s.Rules.IsAutomated("Tough"));
        Assert.Equal(2, RuleRequests);
    }
}
