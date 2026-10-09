using System.Net.WebSockets;
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
/// the factory's fake JWKS, so no test reaches management or Stytch.
/// </summary>
[Collection("Postgres")]
public class SuiteSocketTests(PostgresFixture pg) : IDisposable
{
    readonly GameServerFactory _factory = new();
    readonly List<Guid> _minted = [];

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
        return new SuiteSocket(pg.Db, (c, minted, _) =>
        {
            foreach (var p in c.Info.Participants) { _minted.Add(p.AccountId); minted.Add(p.AccountId); }
            return Task.FromResult(new SocketCredentials(c.Info.Participants.Select(p => new MintedSession(TestKeys.Mint(p.AccountId), p.AccountId)).ToList()));
        }, () => new FactoryConnector(_factory));
    }

    static SuiteOptions Options(bool keep = false, string? outDir = null) => new([Name], DriverKind.Socket, keep, outDir ?? TempDir());

    Task<int> Games(string prefix) => pg.CountAsync("select count(*) from management.game where starts_with(name, $1)", prefix);
    Task<int> Accounts() => pg.CountAsync("select count(*) from management.account where id = any($1)", _minted.ToArray());

    static string Why(SuiteReport report) => string.Join("\n", report.Games.SelectMany(g => g.Failures));

    [Theory]
    [InlineData(6, 1)]
    [InlineData(1, 0)]
    public async Task A_socket_run_plays_the_sample_with_its_dice_fix_and_deletes_its_rows_and_accounts(int face, int defenseDice)
    {
        var check = Checks.Event("the fixed hit decides the block", log =>
            DefenseAfterFirstEdit(log) is { } d ? d.Sum(r => r.Dice.Count) == defenseDice ? null : $"{d.Sum(r => r.Dice.Count)} defense dice" : "no dice_edited");
        var game = Game(dice: [new DiceFix("hit", 0, [face])], checks: [Checks.Ended(), Checks.Invariants(), Checks.FiredAll(), check]);

        var report = await new SuiteRunner([game], await SocketAsync()).RunAsync(Options(), null, Ct);

        Assert.True(report.Passed, Why(report));
        var result = Assert.Single(report.Games);
        Assert.Null(result.GameId);
        Assert.Equal(SuiteRunner.ReplayOf(game, DriverKind.Socket), result.Replay);
        Assert.Equal(0, await Games(SuiteRows.GameName(report.RunId, "", keep: false)));
        Assert.NotEmpty(_minted);
        Assert.Equal(0, await Accounts());
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
            Assert.Equal(_minted.Count, await Accounts());
        }
        finally
        {
            if (report?.Games.Single().GameId is { } id) await new SuiteRows(pg.Db).DeleteGameAsync(id, _minted, Ct);
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
        await new SocketRowSeeder(pg.Db, new PostgresGameRows(pg.Db)).SeedAsync(c, await socket.CredentialsAsync(c, new List<Guid>(), Ct), Ct);
        try
        {
            await using (await rows.HoldAsync(live, Ct))
            {
                var report = await new SuiteRunner([Game()], socket).RunAsync(Options(), null, Ct);
                Assert.True(report.Passed, Why(report));
                Assert.Equal(1, await pg.CountAsync("select count(*) from management.game where id = $1", c.Info.Id));
            }
            Assert.Contains(c.Info.Id, await rows.SweepAsync(Guid.CreateVersion7(), Ct));
            Assert.Equal(0, await pg.CountAsync("select count(*) from management.game where id = $1", c.Info.Id));
        }
        finally { await rows.DeleteGameAsync(c.Info.Id, _minted, Ct); }
    }

    [Fact]
    public async Task Accounts_minted_before_a_failed_seed_are_deleted()
    {
        var minted = new List<Guid>();
        var failing = new SuiteSocket(pg.Db, async (c, sink, ct) =>
        {
            var id = await pg.SeedAccountAsync("sim-suite-mint-then-fail");
            minted.Add(id); sink.Add(id);
            throw new InvalidOperationException("the second mint failed");
        }, () => new FactoryConnector(_factory));
        await SocketAsync();

        var report = await new SuiteRunner([Game()], failing).RunAsync(Options(), null, Ct);

        Assert.Contains("setup: the second mint failed", report.Games.Single().Failures);
        Assert.Equal(0, await pg.CountAsync("select count(*) from management.account where id = any($1)", minted.ToArray()));
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
            await new SocketRowSeeder(pg.Db, new PostgresGameRows(pg.Db)).SeedAsync(c, await socket.CredentialsAsync(c, new List<Guid>(), Ct), Ct);
            return c;
        }
        var interrupted = await SeedAsync(keep: false);
        var kept = await SeedAsync(keep: true);
        try
        {
            var report = await new SuiteRunner([Game()], socket).RunAsync(Options(), null, Ct);
            Assert.True(report.Passed, Why(report));

            Assert.Equal(0, await pg.CountAsync("select count(*) from management.game where id = $1", interrupted.Info.Id));
            Assert.Equal(0, await pg.CountAsync("select count(*) from management.account where id = any($1)",
                interrupted.Info.Participants.Select(p => p.AccountId).ToArray()));
            Assert.Equal(1, await pg.CountAsync("select count(*) from management.game where id = $1", kept.Info.Id));
            Assert.Equal(0, await Games("suite-run "));
        }
        finally
        {
            await rows.DeleteGameAsync(interrupted.Info.Id, _minted, Ct);
            await rows.DeleteGameAsync(kept.Info.Id, _minted, Ct);
        }
    }
}
