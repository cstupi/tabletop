using Tabletop.Content;
using Npgsql;
using Tabletop.GameServer.Log;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Scenarios;
using Tabletop.Sim.Suite;
using Tabletop.Sim.Tests.Infrastructure;
using Tabletop.Sim.Tests.Suite;

namespace Tabletop.Sim.Tests;

/// <summary>ISocketRows as a socket run relies on it, run against InMemorySocketRows (unit) and SocketRowSeeder over Postgres (E2E).</summary>
public abstract class SocketRowsContract
{
    protected abstract ISocketRows Rows { get; }
    protected abstract IRowSeeder Seeder { get; }
    protected abstract IEventLog Log { get; }
    protected abstract Task SeedCorePropertiesAsync();
    /// <summary>Every game and account a test made, for a shared database's cleanup.</summary>
    protected readonly List<Guid> Games = [], Accounts = [];
    static CancellationToken Ct => CancellationToken.None;

    CompiledScenario Compile(string name)
    {
        var c = ScenarioCompiler.Compile(SuiteFixtures.Scenario());
        c = c with { Info = c.Info with { Name = name } };
        Games.Add(c.Info.Id);
        return c;
    }

    SocketCredentials Credentials(CompiledScenario c)
    {
        var sessions = c.Info.Participants.Select(_ => Guid.CreateVersion7()).Select(id => new MintedSession(TestKeys.Mint(id), id)).ToList();
        Accounts.AddRange(sessions.Select(s => s.AccountId));
        return new SocketCredentials(sessions);
    }

    async Task<CompiledScenario> SeededAsync(string name, SocketCredentials? credentials = null)
    {
        await SeedCorePropertiesAsync();
        var c = Compile(name);
        await Rows.SeedAsync(c, credentials ?? Credentials(c), Ct);
        return c;
    }

    [Fact]
    public async Task A_seeded_scenario_is_a_playing_game_with_its_teams_participants_table_and_armies()
    {
        await SeedCorePropertiesAsync();
        var c = Compile("socket rows seed");
        var credentials = Credentials(c);
        await Rows.SeedAsync(c, credentials, Ct);

        var game = (await Seeder.Rows.ReadGameAsync(c.Info.Id, Ct))!;
        Assert.Equal((GameStatus.Playing, "socket rows seed", c.Info.RngSeed, credentials.AccountOf(0)), (game.Status, game.Name, game.RngSeed, game.CreatedBy));
        Assert.Equal(c.Info.Teams.Select(t => (t.Id, t.Ordinal)), game.Teams.Select(t => (t.Id, t.Ordinal)));
        Assert.Equal(c.Info.Participants.Select((p, i) => (p.Id, credentials.AccountOf(i), p.TeamId)).OrderBy(x => x.Id),
            game.Participants.Select(p => (p.Id, p.AccountId, p.TeamId)));
        Assert.Equal(c.Snapshot.Id, (await Seeder.Rows.ReadDraftAsync(c.Info.Id, Ct))!.Id);
        Assert.Equal(c.Board.Armies.Select(a => a.ParticipantId).Order(), (await Seeder.Rows.ReadSnapshotsAsync(c.Info.Id, Ct)).Select(a => a.ParticipantId));
    }

    [Fact]
    public async Task The_same_accounts_seed_a_second_game()
    {
        var first = await SeededAsync("socket rows first");
        var credentials = Credentials(first);
        await SeededAsync("socket rows second", credentials);
        await SeededAsync("socket rows third", credentials);
        Assert.Equal(GameStatus.Playing, (await Seeder.Rows.ReadGameAsync(Games[^1], Ct))!.Status);
    }

    [Fact]
    public async Task A_scenario_whose_properties_are_missing_is_refused()
    {
        var c = Compile("socket rows no properties");
        var hash = "sha256:absent-" + Guid.CreateVersion7().ToString("N")[24..];
        c = c with { Properties = new PropertySet(hash, "absent", "grimdark-future", [], [], [], [], []), Info = c.Info with { PropertiesHash = hash } };
        await Assert.ThrowsAsync<PropertiesMissingException>(() => Rows.SeedAsync(c, Credentials(c), Ct));
        Assert.Null(await Seeder.Rows.ReadGameAsync(c.Info.Id, Ct));
    }

    [Fact]
    public async Task Deleting_a_game_takes_its_rows_and_log_and_keeps_its_accounts()
    {
        var c = await SeededAsync("socket rows delete");
        await Log.AppendAsync(c.Info.Id, [new GameEvent(1, null, new PhaseStarted("deployment"), null)], Ct);
        var account = (await Seeder.Rows.ReadGameAsync(c.Info.Id, Ct))!.CreatedBy;

        await Rows.DeleteAsync(c.Info.Id, Ct);

        Assert.Null(await Seeder.Rows.ReadGameAsync(c.Info.Id, Ct));
        Assert.Null(await Seeder.Rows.ReadDraftAsync(c.Info.Id, Ct));
        Assert.Null(await Seeder.Rows.FindParticipantAsync(c.Info.Id, account, Ct));
        Assert.Equal(0, await Log.LastSeqAsync(c.Info.Id, Ct));
        var other = await Seeder.SeedGameAsync(GameStatus.Lobby);
        Games.Add(other.GameId);
        await Seeder.SeedParticipantAsync(other.GameId, account, null);      // the account is still there to join with
    }

    [Fact]
    public async Task A_spectator_is_a_teamless_participant_and_its_account_is_made_once()
    {
        var first = await SeededAsync("socket rows spectate 1");
        var second = await SeededAsync("socket rows spectate 2");
        var spectator = Guid.CreateVersion7();
        Accounts.Add(spectator);
        await Rows.SeedSpectatorAsync(first.Info.Id, spectator, "spectator", Ct);
        await Rows.SeedSpectatorAsync(second.Info.Id, spectator, "spectator", Ct);
        foreach (var c in new[] { first, second })
        {
            var game = (await Seeder.Rows.ReadGameAsync(c.Info.Id, Ct))!;
            var p = Assert.Single(game.Participants, p => p.AccountId == spectator);
            Assert.Equal(("spectator", (Guid?)null, (Guid?)null), (p.Name, p.TeamId, p.ArmyId));
        }
    }

    [Fact]
    public async Task The_sweep_takes_only_unheld_runs_transient_games()
    {
        Guid mine = Guid.CreateVersion7(), live = Guid.CreateVersion7(), dead = Guid.CreateVersion7();
        var ofDead = await SeededAsync(SuiteRows.GameName(dead, "a", keep: false));
        var ofLive = await SeededAsync(SuiteRows.GameName(live, "b", keep: false));
        var ofMine = await SeededAsync(SuiteRows.GameName(mine, "c", keep: false));
        var keptOfDead = await SeededAsync(SuiteRows.GameName(dead, "d", keep: true));
        var foreign = await SeededAsync($"suite-run not-a-run {dead:N}");

        await using (await Rows.HoldAsync(live, Ct))
        {
            var swept = await Rows.SweepAsync(mine, Ct);
            Assert.Contains(ofDead.Info.Id, swept);
            Assert.DoesNotContain(ofLive.Info.Id, swept);
            Assert.DoesNotContain(ofMine.Info.Id, swept);
            Assert.DoesNotContain(keptOfDead.Info.Id, swept);
            Assert.DoesNotContain(foreign.Info.Id, swept);
            Assert.Null(await Seeder.Rows.ReadGameAsync(ofDead.Info.Id, Ct));
            Assert.NotNull(await Seeder.Rows.ReadGameAsync(ofLive.Info.Id, Ct));
        }
        Assert.Contains(ofLive.Info.Id, await Rows.SweepAsync(mine, Ct));       // its lock is gone
        Assert.NotNull(await Seeder.Rows.ReadGameAsync(keptOfDead.Info.Id, Ct));
        Assert.NotNull(await Seeder.Rows.ReadGameAsync(foreign.Info.Id, Ct));
    }

    [Fact]
    public async Task A_held_run_lock_waits_for_its_release()
    {
        var run = Guid.CreateVersion7();
        var first = await Rows.HoldAsync(run, Ct);
        var second = Rows.HoldAsync(run, Ct);
        await Task.Delay(100);
        Assert.False(second.IsCompleted);
        await first.DisposeAsync();
        await using var _ = await second.WaitAsync(TimeSpan.FromSeconds(10));
    }
}

public class InMemorySocketRowsTests : SocketRowsContract
{
    readonly InMemorySeeder _db = new();
    protected override ISocketRows Rows { get; }
    public InMemorySocketRowsTests() => Rows = new InMemorySocketRows(_db);
    protected override IRowSeeder Seeder => _db;
    protected override IEventLog Log => _db.Log;
    protected override Task SeedCorePropertiesAsync() { _db.Properties.Add(ScenarioCompiler.CoreProperties); return Task.CompletedTask; }
}

[Collection("Postgres")]
[Trait("Category", "E2E")]
public class PostgresSocketRowsTests(PostgresFixture pg) : SocketRowsContract, IAsyncLifetime
{
    protected override ISocketRows Rows => new SocketRowSeeder(pg.Db);
    protected override IRowSeeder Seeder => pg;
    protected override IEventLog Log => new PostgresEventLog(pg.Db);
    protected override Task SeedCorePropertiesAsync() =>
        new PostgresPropertiesStore(pg.Db).SeedAsync(ScenarioCompiler.CoreDocuments[RuleSets.Gf351], CancellationToken.None);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var game in Games) await new SocketRowSeeder(pg.Db).DeleteAsync(game, CancellationToken.None);
        await using var command = pg.Db.CreateCommand("delete from management.account where id = any($1)");
        command.Parameters.AddWithValue(Accounts.ToArray());
        await command.ExecuteNonQueryAsync();
    }
}
