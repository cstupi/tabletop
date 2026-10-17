using Tabletop.Content;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Scenarios;
using Tabletop.Sim.Suite;
using static Tabletop.GameServer.Tests.Infrastructure.InMemoryGameRows;

namespace Tabletop.Sim.Tests.Infrastructure;

/// <summary>
/// ISocketRows over a seeder's in-memory stores, the ones the test's GameServerFactory serves, as SocketRowSeeder writes Postgres. A
/// run's lock is a semaphore per run id: HoldAsync waits for it as pg_advisory_lock does, the sweep only tries it.
/// </summary>
public sealed class InMemorySocketRows(InMemorySeeder db) : ISocketRows
{
    readonly Dictionary<Guid, SemaphoreSlim> _locks = new();

    public InMemorySeeder Db => db;

    public async Task SeedAsync(CompiledScenario scenario, SocketCredentials credentials, CancellationToken ct)
    {
        if (!await db.Rows.PropertiesExistAsync(scenario.Properties.ContentHash, ct))
            throw new PropertiesMissingException(scenario.Properties.ContentHash);

        var info = scenario.Info;
        var accounts = info.Participants.Select((_, i) => credentials.AccountOf(i)).ToList();
        for (var i = 0; i < accounts.Count; i++) db.GameRows.InsertAccount(accounts[i], info.Participants[i].Name, ifAbsent: true);
        db.GameRows.InsertGame(new GameRow(info.Id, info.Name, Tabletop.Protocol.GameStatus.Lobby, info.GameRules, info.PropertiesHash, info.SchedulerShape,
            info.Enforcement, info.WaitsBetweenSteps, info.RngSeed, accounts[0]));
        foreach (var team in info.Teams) db.GameRows.InsertTeam(new TeamRow(team.Id, info.Id, team.Ordinal));
        for (var i = 0; i < info.Participants.Count; i++)
            db.GameRows.InsertParticipant(new ParticipantRow(info.Participants[i].Id, info.Id, info.Participants[i].TeamId, accounts[i], null, null));

        await db.Rows.StartSetupAsync(info.Id, scenario.Snapshot, ct);
        await db.Rows.EndSetupAsync(info.Id, scenario.Snapshot,
            scenario.Board.Armies.ToDictionary(a => a.ParticipantId, a => (ArmySnapshot?)a.Snapshot), ct);
    }

    public Task DeleteAsync(Guid gameId, CancellationToken ct)
    {
        db.Log.Remove(gameId);
        db.GameRows.DeleteGame(gameId);
        return Task.CompletedTask;
    }

    public Task SeedSpectatorAsync(Guid gameId, Guid accountId, string name, CancellationToken ct)
    {
        db.GameRows.InsertAccount(accountId, name, ifAbsent: true);
        db.GameRows.InsertParticipant(new ParticipantRow(Guid.CreateVersion7(), gameId, null, accountId, null, null));
        return Task.CompletedTask;
    }

    SemaphoreSlim LockOf(Guid runId)
    {
        lock (_locks) return _locks.TryGetValue(runId, out var l) ? l : _locks[runId] = new SemaphoreSlim(1, 1);
    }

    public async Task<IAsyncDisposable> HoldAsync(Guid runId, CancellationToken ct)
    {
        var gate = LockOf(runId);
        await gate.WaitAsync(ct);
        return new Held(gate);
    }

    sealed class Held(SemaphoreSlim gate) : IAsyncDisposable
    {
        int _released;
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release();
            return ValueTask.CompletedTask;
        }
    }

    public async Task<IReadOnlyList<Guid>> SweepAsync(Guid runId, CancellationToken ct)
    {
        var byRun = db.GameRows.Games
            .Select(g => (Game: g.Id, Run: SuiteRows.TransientRunOf(g.Name)))
            .Where(x => x.Run is { } r && r != runId)
            .GroupBy(x => x.Run!.Value, x => x.Game);
        var swept = new List<Guid>();
        foreach (var run in byRun)
        {
            var gate = LockOf(run.Key);
            if (!gate.Wait(0)) continue;                    // a live run
            try
            {
                foreach (var game in run) await DeleteAsync(game, ct);
                swept.AddRange(run);
            }
            finally { gate.Release(); }
        }
        return swept;
    }
}
