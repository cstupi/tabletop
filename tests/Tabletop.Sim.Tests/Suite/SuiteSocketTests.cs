using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Tabletop.Protocol;
using Tabletop.Sim.Serve;
using Tabletop.GameServer.Persistence;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Scenarios;
using Tabletop.Sim.Suite;
using static Tabletop.Sim.Tests.Suite.SuiteFixtures;

namespace Tabletop.Sim.Tests.Suite;

/// <summary>
/// The runner over the socket, against the in-memory game server and the Docker Postgres. Sessions are signed by TestKeys against
/// the factory's fake JWKS, so no test reaches management or Stytch. Every run plays on one TestPool, as every console run plays on
/// the one account pool.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "E2E")]
public class SuiteSocketTests(PostgresFixture pg) : IDisposable
{
    readonly GameServerFactory _factory = new();
    readonly TestPool _pool = new(pg);

    public void Dispose() => _factory.Dispose();

    sealed class FactoryConnector(GameServerFactory factory) : IWebSocketConnector
    {
        public async Task<WebSocket> ConnectAsync(CancellationToken ct) =>
            await factory.Server.CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/ws"), ct);
    }

    async Task<SuiteSocket> SocketAsync()
    {
        await new PostgresPropertiesStore(pg.Db).SeedAsync(
            PropertiesSeeder.EmbeddedDocuments().Single(d => d.GameRules == GrimdarkFutureRules.RulesId), Ct);
        return _pool.Socket(() => new FactoryConnector(_factory));
    }

    /// <summary>Connects with the jwt and returns the server's first message.</summary>
    async Task<SocketMessage> FirstMessageAsync(string jwt, Guid gameId)
    {
        using var socket = await new FactoryConnector(_factory).ConnectAsync(Ct);
        await socket.SendAsync(Encoding.UTF8.GetBytes(WireJson.Serialize(new Connect(jwt, gameId, ProtocolVersion.Current))), WebSocketMessageType.Text, true, Ct);
        var buffer = new byte[1 << 16];
        var read = await socket.ReceiveAsync(buffer, Ct);
        return WireJson.Deserialize(Encoding.UTF8.GetString(buffer, 0, read.Count));
    }

    static SuiteOptions Options(bool keep = false, string? outDir = null) => new([Name], DriverKind.Socket, keep, outDir ?? TempDir());

    Task<int> Games(string prefix) => pg.CountAsync("select count(*) from management.game where starts_with(name, $1)", prefix);
    Task<int> Accounts() => pg.CountAsync("select count(*) from management.account where id = any($1)", _pool.Accounts.ToArray());
    Task<int> Participants(Guid gameId) => pg.CountAsync("select count(*) from management.participant where game_id = $1", gameId);

    static string Why(SuiteReport report) => string.Join("\n", report.Games.SelectMany(g => g.Failures));

    [Theory]
    [InlineData(6, 1)]
    [InlineData(1, 0)]
    public async Task A_socket_run_plays_the_sample_with_its_dice_fix_and_deletes_its_rows_but_not_the_pool(int face, int defenseDice)
    {
        var check = Checks.Event("the fixed hit decides the block", log =>
            DefenseAfterFirstEdit(log) is { } d ? d.Sum(r => r.Dice.Count) == defenseDice ? null : $"{d.Sum(r => r.Dice.Count)} defense dice" : "no dice_edited");
        var game = Game(dice: [new DiceFix("hit", 0, [face])], checks: [Checks.Ended(), Checks.Invariants(), Checks.FiredAll(), check]);

        var report = await new SuiteRunner([game], await SocketAsync()).RunAsync(Options(), null, Ct);

        Assert.True(report.Passed, Why(report));
        var result = Assert.Single(report.Games);
        Assert.Null(result.GameId);
        Assert.Null(result.Spectator);
        Assert.Equal(SuiteRunner.ReplayOf(game, DriverKind.Socket), result.Replay);
        Assert.Equal(0, await Games(SuiteRows.GameName(report.RunId, "", keep: false)));
        Assert.NotEmpty(_pool.Accounts);
        Assert.Equal(_pool.Accounts.Count, await Accounts());
    }

    [Fact]
    public async Task Keep_leaves_the_rows_and_writes_the_recording()
    {
        var directory = TempDir();
        var socket = await SocketAsync();
        SuiteReport? report = null;
        try
        {
            report = await new SuiteRunner([Game()], socket).RunAsync(Options(keep: true, outDir: directory), null, Ct);
            Assert.True(report.Passed, Why(report));
            var result = report.Games.Single();
            Assert.NotNull(result.GameId);
            Assert.Equal(1, await pg.CountAsync("select count(*) from management.game where id = $1 and name = $2",
                result.GameId!.Value, SuiteRows.GameName(report.RunId, Name, keep: true)));
            Assert.True(await pg.CountAsync("select count(*) from game.game_event where game_id = $1", result.GameId.Value) > 0);
            Assert.True(File.Exists(result.RecordingPath));
            Assert.Equal(_pool.Accounts.Count, await Accounts());

            // The spectator: a participant with no team, named by the result, that a socket can connect as.
            Assert.Equal(SuiteRows.Spectator, result.Spectator);
            Assert.Equal(1, await pg.CountAsync(
                "select count(*) from management.participant p join management.account a on a.id = p.account_id where p.game_id = $1 and p.team_id is null and a.name = $2",
                result.GameId.Value, TestPool.TestName(result.Spectator!)));
            var session = await socket.SessionAsync(result.Spectator!, Ct);
            Assert.IsType<Welcome>(await FirstMessageAsync(session.Jwt, result.GameId.Value));
        }
        finally
        {
            if (report?.Games.Single().GameId is { } id) await new SuiteRows(pg.Db).DeleteGameAsync(id, Ct);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task The_spectate_endpoint_gives_a_session_that_connects_to_a_kept_socket_game()
    {
        var socket = await SocketAsync();
        var directory = TempDir();
        await using var app = ServeApp.Build(new ServeArguments(0, "127.0.0.1", directory), new SuiteRunner([Game()], socket));
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.First()), Timeout = TimeSpan.FromSeconds(120) };
        Guid? gameId = null;
        try
        {
            var started = await http.PostAsJsonAsync("/runs", new { driver = "socket", keep = true });
            var run = (await started.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("runId").GetGuid();
            await (await http.GetAsync($"/runs/{run}/events")).Content.ReadAsStringAsync();   // to the end of the stream

            var response = await http.PostAsync($"/runs/{run}/games/{Name}/spectate", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            gameId = body.GetProperty("gameId").GetGuid();
            Assert.IsType<Welcome>(await FirstMessageAsync(body.GetProperty("jwt").GetString()!, gameId.Value));
            Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsync($"/runs/{run}/games/nope/spectate", null)).StatusCode);
        }
        finally
        {
            if (gameId is { } id) await new SuiteRows(pg.Db).DeleteGameAsync(id, Ct);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A run in flight holds its lock, so another run's sweep leaves its games; once its session ends they are swept.</summary>
    [Fact]
    public async Task The_sweep_spares_a_live_run_and_takes_it_once_its_lock_is_gone()
    {
        var socket = await SocketAsync();
        var rows = new SuiteRows(pg.Db);
        var live = Guid.CreateVersion7();
        var c = ScenarioCompiler.Compile(Scenario());
        c = c with { Info = c.Info with { Name = SuiteRows.GameName(live, Name, keep: false) } };
        await new SocketRowSeeder(pg.Db, new PostgresGameRows(pg.Db)).SeedAsync(c, await socket.CredentialsAsync(c, Ct), Ct);
        try
        {
            await using (await rows.HoldAsync(live, Ct))
            {
                var report = await new SuiteRunner([Game()], socket).RunAsync(Options(), null, Ct);
                Assert.True(report.Passed, Why(report));
                // The run played on the live game's accounts and deleted its own game; the live game keeps every participant.
                Assert.Equal(1, await pg.CountAsync("select count(*) from management.game where id = $1", c.Info.Id));
                Assert.Equal(c.Info.Participants.Count, await Participants(c.Info.Id));
            }
            Assert.Contains(c.Info.Id, await rows.SweepAsync(Guid.CreateVersion7(), Ct));
            Assert.Equal(0, await pg.CountAsync("select count(*) from management.game where id = $1", c.Info.Id));
        }
        finally { await rows.DeleteGameAsync(c.Info.Id, Ct); }
    }

    sealed class RefusingConnector : IWebSocketConnector
    {
        public Task<WebSocket> ConnectAsync(CancellationToken ct) => throw new InvalidOperationException("the game server is down");
    }

    [Fact]
    public async Task A_game_that_fails_after_its_seed_is_deleted_and_the_pool_stays()
    {
        await SocketAsync();
        var failing = _pool.Socket(() => new RefusingConnector());

        var report = await new SuiteRunner([Game()], failing).RunAsync(Options(), null, Ct);

        Assert.Contains("setup: the game server is down", report.Games.Single().Failures);
        Assert.Equal(0, await Games(SuiteRows.GameName(report.RunId, "", keep: false)));
        Assert.NotEmpty(_pool.Accounts);
        Assert.Equal(_pool.Accounts.Count, await Accounts());
    }

    /// <summary>
    /// Two runs at once (the console suite beside the runner service) play on the same accounts, and each deletes only its own games.
    /// Each run has its own in-memory game server: two games at once on one TestServer stall at the first Ask whichever accounts they
    /// use, which the compose game server does not do (two socket suites at once pass there). The accounts and Postgres are shared.
    /// </summary>
    [Fact]
    public async Task Two_runs_at_once_share_the_pool()
    {
        var socket = await SocketAsync();
        using var second = new GameServerFactory();

        var reports = await Task.WhenAll(
            new SuiteRunner([Game()], socket).RunAsync(Options(), null, Ct),
            new SuiteRunner([Game(name: "sample-ap-volley-2")], _pool.Socket(() => new FactoryConnector(second)))
                .RunAsync(new SuiteOptions(["sample-ap-volley-2"], DriverKind.Socket, false, TempDir()), null, Ct));

        Assert.All(reports, r => Assert.True(r.Passed, Why(r)));
        foreach (var r in reports) Assert.Equal(0, await Games(SuiteRows.GameName(r.RunId, "", keep: false)));
        // Both games' three participants (two armies and the recorder) were the same three accounts, and they are still there.
        Assert.Equal(3, _pool.Accounts.Count);
        Assert.Equal(3, await Accounts());
    }

    [Fact]
    public async Task A_run_sweeps_games_left_by_an_interrupted_run()
    {
        var socket = await SocketAsync();
        var rows = new SuiteRows(pg.Db);

        // An interrupted run's game, and a game an earlier run kept.
        async Task<CompiledScenario> SeedAsync(bool keep)
        {
            var c = ScenarioCompiler.Compile(Scenario());
            c = c with { Info = c.Info with { Name = SuiteRows.GameName(Guid.CreateVersion7(), Name, keep) } };
            await new SocketRowSeeder(pg.Db, new PostgresGameRows(pg.Db)).SeedAsync(c, await socket.CredentialsAsync(c, Ct), Ct);
            return c;
        }
        var interrupted = await SeedAsync(keep: false);
        var kept = await SeedAsync(keep: true);
        try
        {
            var report = await new SuiteRunner([Game()], socket).RunAsync(Options(), null, Ct);
            Assert.True(report.Passed, Why(report));

            Assert.Equal(0, await pg.CountAsync("select count(*) from management.game where id = $1", interrupted.Info.Id));
            Assert.Equal(_pool.Accounts.Count, await Accounts());
            Assert.Equal(1, await pg.CountAsync("select count(*) from management.game where id = $1", kept.Info.Id));
            Assert.Equal(kept.Info.Participants.Count, await Participants(kept.Info.Id));
            // Only this test's rows: other suite runs may share the Postgres, and their live games are not this run's to judge.
            Assert.Equal(0, await Games(SuiteRows.GameName(report.RunId, "", keep: false)));
        }
        finally
        {
            await rows.DeleteGameAsync(interrupted.Info.Id, Ct);
            await rows.DeleteGameAsync(kept.Info.Id, Ct);
        }
    }
}
