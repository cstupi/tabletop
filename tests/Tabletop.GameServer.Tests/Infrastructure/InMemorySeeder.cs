using Tabletop.Geometry;
using Tabletop.Content;
using Tabletop.GameServer.Log;
using Tabletop.GameServer.Persistence;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;
using static Tabletop.GameServer.Tests.Infrastructure.InMemoryGameRows;

namespace Tabletop.GameServer.Tests.Infrastructure;

/// <summary>
/// One test's database: the in-memory rows, event log and properties store a GameServerFactory built over it serves, and the seeding
/// PostgresFixture does with SQL, written into them. Nothing is shared between instances, so tests over their own seeder run in parallel.
/// </summary>
public sealed class InMemorySeeder : IRowSeeder
{
    public InMemoryPropertiesStore Properties { get; } = new();
    public InMemoryEventLog Log { get; } = new();
    public InMemoryGameRows GameRows { get; }
    public IGameRows Rows => GameRows;

    public InMemorySeeder() => GameRows = new InMemoryGameRows(Properties);

    /// <summary>The game's events in the log, undone ones included.</summary>
    public int EventCount(Guid gameId) => Log.ReadAsync(gameId, 0, CancellationToken.None).GetAwaiter().GetResult().Count;

    public Task<Guid> SeedAccountAsync(string name)
    {
        var id = Guid.CreateVersion7();
        GameRows.InsertAccount(id, name);
        return Task.FromResult(id);
    }

    public Task<string> SeedPropertiesAsync()
    {
        var hash = "sha256:test-" + Guid.CreateVersion7().ToString("N")[24..32];
        Properties.Add(new PropertySet(hash, "GF test", "grimdark-future", [], [], [], [], []));
        return Task.FromResult(hash);
    }

    public async Task<SeededGame> SeedGameAsync(GameStatus status, int teams = 2, int participantsPerTeam = 1, int spectators = 0, Enforcement enforcement = Enforcement.Warn,
        long rngSeed = IRowSeeder.DefaultRngSeed)
    {
        var hash = await SeedPropertiesAsync();
        var creator = await SeedAccountAsync("Creator");
        var gameId = Guid.CreateVersion7();
        GameRows.InsertGame(new GameRow(gameId, "Friday", status, "grimdark-future", hash, SchedulerShape.AlternatingActivation, enforcement, false, rngSeed, creator));
        var teamIds = new List<Guid>(); var participants = new List<SeededParticipant>();
        for (var t = 1; t <= teams; t++)
        {
            var teamId = Guid.CreateVersion7(); teamIds.Add(teamId);
            GameRows.InsertTeam(new TeamRow(teamId, gameId, t));
            for (var p = 0; p < participantsPerTeam; p++)
                participants.Add(await SeedParticipantAsync(gameId, t == 1 && p == 0 ? creator : await SeedAccountAsync($"Player {t}.{p}"), teamId));
        }
        for (var s = 0; s < spectators; s++) participants.Add(await SeedParticipantAsync(gameId, await SeedAccountAsync($"Watcher {s}"), null));
        return new SeededGame(gameId, creator, teamIds, participants, hash, rngSeed);
    }

    public Task<SeededParticipant> SeedParticipantAsync(Guid gameId, Guid accountId, Guid? teamId)
    {
        var id = Guid.CreateVersion7();
        GameRows.InsertParticipant(new ParticipantRow(id, gameId, teamId, accountId, null, null));
        return Task.FromResult(new SeededParticipant(id, accountId, teamId));
    }

    /// <summary>A sample army in play for every participant with a team, as the end of setup snapshots them: a game seeded straight into
    /// playing then waits for no team (WaitingForPlayers).</summary>
    public void SeedArmiesInPlay(SeededGame game)
    {
        foreach (var p in game.Participants.Where(p => p.TeamId is not null))
            GameRows.SetSnapshot(p.ParticipantId, ArmySnapshot.Of(Tabletop.Content.Tests.ArmyTests.Sample(), Guid.CreateVersion7()));
    }

    public Task<Army> SeedArmyAsync(Guid ownerId, Army? given = null)
    {
        var army = given ?? Tabletop.Content.Tests.ArmyTests.Sample();
        GameRows.InsertArmy(army, ownerId);
        return Task.FromResult(army);
    }

    public Task<Guid> SeedTableStateAsync(Guid gameId, double width = 72, double length = 48)
    {
        var id = Guid.CreateVersion7();
        GameRows.InsertTable(new TableRow(gameId, null, new TableState(id, null, new TableSize(width, length), VisualRef.OfBuiltin("grass"), [], [], [])));
        return Task.FromResult(id);
    }

    public Task<TableState> SeedLayoutAsync(Guid ownerId)
    {
        var layout = Tabletop.Content.Tests.TableStateTests.Sample() with { Name = "Saved layout" };
        GameRows.InsertTable(new TableRow(null, ownerId, layout));
        return Task.FromResult(layout);
    }
}
