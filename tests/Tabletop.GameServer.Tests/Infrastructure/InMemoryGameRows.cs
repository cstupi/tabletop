using Tabletop.Content;
using Tabletop.GameServer.Persistence;
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests.Infrastructure;

/// <summary>
/// Management's tables in memory, behind IGameRows as PostgresGameRows reads and writes them: the same status guards, the same order on
/// read, primary and foreign keys where a write could break one, and each call one atomic update under a lock (a transaction). The
/// properties rows are the IPropertiesStore's, as in Postgres they are one table. InMemorySeeder writes management's own rows here.
/// </summary>
public sealed class InMemoryGameRows(IPropertiesStore properties) : IGameRows
{
    public sealed record GameRow(Guid Id, string Name, GameStatus Status, string GameRules, string PropertiesHash, SchedulerShape SchedulerShape,
        Enforcement Enforcement, bool WaitsBetweenSteps, long RngSeed, Guid CreatedBy);
    public sealed record TeamRow(Guid Id, Guid GameId, int Ordinal);
    public sealed record ParticipantRow(Guid Id, Guid GameId, Guid? TeamId, Guid AccountId, Guid? ArmyId, ArmySnapshot? ArmySnapshot);
    /// <summary>A table_state: a game's draft (GameId set) or a saved layout (OwnerId set).</summary>
    public sealed record TableRow(Guid? GameId, Guid? OwnerId, TableState Table);

    readonly object _gate = new();
    readonly Dictionary<Guid, string> _accounts = new();
    readonly Dictionary<Guid, GameRow> _games = new();
    readonly Dictionary<Guid, TeamRow> _teams = new();
    readonly Dictionary<Guid, ParticipantRow> _participants = new();
    readonly Dictionary<Guid, OwnedArmy> _armies = new();
    readonly Dictionary<Guid, TableRow> _tables = new();

    // Management's writes, for the seeder and the sim's socket rows.

    public void InsertAccount(Guid id, string name, bool ifAbsent = false)
    {
        lock (_gate)
        {
            if (_accounts.ContainsKey(id)) { if (ifAbsent) return; throw Duplicate("account", id); }
            _accounts[id] = name;
        }
    }

    public void InsertGame(GameRow game)
    {
        lock (_gate)
        {
            if (_games.ContainsKey(game.Id)) throw Duplicate("game", game.Id);
            if (!_accounts.ContainsKey(game.CreatedBy)) throw MissingKey("account", game.CreatedBy);
            if (!PropertiesExist(game.PropertiesHash)) throw new InvalidOperationException($"no properties row {game.PropertiesHash}");
            _games[game.Id] = game;
        }
    }

    public void InsertTeam(TeamRow team)
    {
        lock (_gate)
        {
            if (_teams.ContainsKey(team.Id)) throw Duplicate("team", team.Id);
            if (!_games.ContainsKey(team.GameId)) throw MissingKey("game", team.GameId);
            if (_teams.Values.Any(t => t.GameId == team.GameId && t.Ordinal == team.Ordinal)) throw new InvalidOperationException($"team ordinal {team.Ordinal} is taken");
            _teams[team.Id] = team;
        }
    }

    public void InsertParticipant(ParticipantRow participant)
    {
        lock (_gate)
        {
            if (_participants.ContainsKey(participant.Id)) throw Duplicate("participant", participant.Id);
            if (!_games.ContainsKey(participant.GameId)) throw MissingKey("game", participant.GameId);
            if (!_accounts.ContainsKey(participant.AccountId)) throw MissingKey("account", participant.AccountId);
            if (participant.TeamId is { } t && !_teams.ContainsKey(t)) throw MissingKey("team", t);
            if (_participants.Values.Any(p => p.GameId == participant.GameId && p.AccountId == participant.AccountId))
                throw new InvalidOperationException($"account {participant.AccountId} is already in game {participant.GameId}");
            _participants[participant.Id] = participant;
        }
    }

    public void InsertArmy(Army army, Guid ownerId)
    {
        lock (_gate)
        {
            if (_armies.ContainsKey(army.Id)) throw Duplicate("army", army.Id);
            if (!_accounts.ContainsKey(ownerId)) throw MissingKey("account", ownerId);
            _armies[army.Id] = new OwnedArmy(army, ownerId);
        }
    }

    public void InsertTable(TableRow row)
    {
        lock (_gate)
        {
            if (_tables.ContainsKey(row.Table.Id)) throw Duplicate("table_state", row.Table.Id);
            if (row.GameId is { } g && !_games.ContainsKey(g)) throw MissingKey("game", g);
            if (row.OwnerId is { } o && !_accounts.ContainsKey(o)) throw MissingKey("account", o);
            CheckChildKeys(row.Table);
            _tables[row.Table.Id] = row;
        }
    }

    /// <summary>Deletes the game and what cascades from it: teams, participants, its table_state tree.</summary>
    public void DeleteGame(Guid gameId)
    {
        lock (_gate)
        {
            _games.Remove(gameId);
            foreach (var t in _teams.Values.Where(t => t.GameId == gameId).ToList()) _teams.Remove(t.Id);
            foreach (var p in _participants.Values.Where(p => p.GameId == gameId).ToList()) _participants.Remove(p.Id);
            foreach (var s in _tables.Values.Where(s => s.GameId == gameId).ToList()) _tables.Remove(s.Table.Id);
        }
    }

    // Reads for assertions: what a test once counted with SQL.

    public GameRow? Game(Guid id) { lock (_gate) return _games.GetValueOrDefault(id); }
    public IReadOnlyList<GameRow> Games { get { lock (_gate) return [.. _games.Values]; } }
    public IReadOnlyDictionary<Guid, string> Accounts { get { lock (_gate) return new Dictionary<Guid, string>(_accounts); } }
    public IReadOnlyList<ParticipantRow> ParticipantsOf(Guid gameId) { lock (_gate) return [.. _participants.Values.Where(p => p.GameId == gameId).OrderBy(p => p.Id)]; }
    public ParticipantRow? Participant(Guid id) { lock (_gate) return _participants.GetValueOrDefault(id); }
    public TableRow? Table(Guid tableStateId) { lock (_gate) return _tables.GetValueOrDefault(tableStateId); }
    /// <summary>The game's draft as stored, children in the order written.</summary>
    public TableState? DraftOf(Guid gameId) { lock (_gate) return _tables.Values.FirstOrDefault(t => t.GameId == gameId)?.Table; }

    // IGameRows.

    public Task<GameInfo?> ReadGameAsync(Guid gameId, CancellationToken ct)
    {
        lock (_gate)
        {
            if (!_games.TryGetValue(gameId, out var g)) return Task.FromResult<GameInfo?>(null);
            var teams = _teams.Values.Where(t => t.GameId == gameId).OrderBy(t => t.Ordinal).Select(t => new Team(t.Id, t.Ordinal)).ToList();
            var participants = _participants.Values.Where(p => p.GameId == gameId).OrderBy(p => p.Id)
                .Select(p => new Participant(p.Id, p.AccountId, _accounts[p.AccountId], p.TeamId, p.ArmyId)).ToList();
            return Task.FromResult<GameInfo?>(new GameInfo(g.Id, g.Name, g.Status, g.CreatedBy, g.GameRules, g.PropertiesHash, g.SchedulerShape, g.Enforcement,
                g.WaitsBetweenSteps, g.RngSeed, teams, participants));
        }
    }

    public Task<Guid?> FindParticipantAsync(Guid gameId, Guid accountId, CancellationToken ct)
    {
        lock (_gate) return Task.FromResult(_participants.Values.FirstOrDefault(p => p.GameId == gameId && p.AccountId == accountId)?.Id);
    }

    public Task<TableState?> ReadDraftAsync(Guid gameId, CancellationToken ct)
    {
        lock (_gate) return Task.FromResult(_tables.Values.FirstOrDefault(t => t.GameId == gameId) is { } row ? Ordered(row.Table) : null);
    }

    public Task<IReadOnlyList<ArmyInPlay>> ReadSnapshotsAsync(Guid gameId, CancellationToken ct)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<ArmyInPlay>>(_participants.Values.Where(p => p.GameId == gameId && p.ArmySnapshot is not null).OrderBy(p => p.Id)
                .Select(p => new ArmyInPlay(p.Id, p.ArmySnapshot!)).ToList());
    }

    public Task<OwnedArmy?> ReadArmyAsync(Guid armyId, CancellationToken ct)
    {
        lock (_gate) return Task.FromResult(_armies.GetValueOrDefault(armyId));
    }

    public Task<bool> PropertiesExistAsync(string hash, CancellationToken ct) => Task.FromResult(PropertiesExist(hash));

    public Task EndGameAsync(Guid gameId, CancellationToken ct)
    {
        lock (_gate)
            if (_games.TryGetValue(gameId, out var g) && g.Status == GameStatus.Playing) _games[gameId] = g with { Status = GameStatus.Ended };
        return Task.CompletedTask;
    }

    public Task<Team> CreateTeamAsync(Guid gameId, CancellationToken ct)
    {
        lock (_gate)
        {
            if (!_games.ContainsKey(gameId)) throw MissingKey("game", gameId);
            var ordinal = _teams.Values.Where(t => t.GameId == gameId).Select(t => t.Ordinal).DefaultIfEmpty(0).Max() + 1;
            var team = new TeamRow(Guid.CreateVersion7(), gameId, ordinal);
            _teams[team.Id] = team;
            return Task.FromResult(new Team(team.Id, team.Ordinal));
        }
    }

    public Task SetParticipantTeamAsync(Guid participantId, Guid? teamId, CancellationToken ct)
    {
        lock (_gate)
        {
            if (teamId is { } t && !_teams.ContainsKey(t)) throw MissingKey("team", t);
            if (_participants.TryGetValue(participantId, out var p)) _participants[participantId] = p with { TeamId = teamId };
        }
        return Task.CompletedTask;
    }

    public Task SetParticipantArmyAsync(Guid participantId, Guid? armyId, CancellationToken ct)
    {
        lock (_gate)
        {
            if (armyId is { } a && !_armies.ContainsKey(a)) throw MissingKey("army", a);
            if (_participants.TryGetValue(participantId, out var p)) _participants[participantId] = p with { ArmyId = armyId };
        }
        return Task.CompletedTask;
    }

    public async Task SetConfigurationAsync(Guid gameId, SetConfiguration c, CancellationToken ct)
    {
        // game_rules follows the chosen properties row; a hash with no row is the foreign key's refusal.
        var set = await properties.LoadAsync(c.PropertiesHash, ct) ?? throw MissingKey("properties", c.PropertiesHash);
        lock (_gate)
            if (_games.TryGetValue(gameId, out var g))
                _games[gameId] = g with
                {
                    Name = c.Name, PropertiesHash = c.PropertiesHash, GameRules = set.GameRules, SchedulerShape = c.SchedulerShape,
                    Enforcement = c.Enforcement, WaitsBetweenSteps = c.WaitsBetweenSteps,
                };
    }

    public Task StartSetupAsync(Guid gameId, TableState draft, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_tables.ContainsKey(draft.Id)) throw Duplicate("table_state", draft.Id);
            CheckChildKeys(draft);
            if (!_games.TryGetValue(gameId, out var g) || g.Status != GameStatus.Lobby) throw new InvalidOperationException("game is not in lobby");
            _tables[draft.Id] = new TableRow(gameId, null, draft);
            _games[gameId] = g with { Status = GameStatus.Setup };
        }
        return Task.CompletedTask;
    }

    public Task WriteDraftAsync(Guid gameId, TableState draft, CancellationToken ct)
    {
        lock (_gate)
        {
            if (!_tables.TryGetValue(draft.Id, out var row) || row.GameId != gameId) throw new InvalidOperationException("no draft row for this game");
            CheckChildKeys(draft);
            _tables[draft.Id] = row with { Table = draft };
        }
        return Task.CompletedTask;
    }

    public Task EndSetupAsync(Guid gameId, TableState snapshot, IReadOnlyDictionary<Guid, ArmySnapshot?> snapshotsByParticipant, CancellationToken ct)
    {
        lock (_gate)
        {
            if (!_tables.TryGetValue(snapshot.Id, out var row) || row.GameId != gameId) throw new InvalidOperationException("no draft row for this game");
            CheckChildKeys(snapshot);
            if (!_games.TryGetValue(gameId, out var g) || g.Status != GameStatus.Setup) throw new InvalidOperationException("game is not in setup");
            // Every check passed: the transaction commits whole. The name column is not rewritten here, as in PostgresGameRows.
            _tables[snapshot.Id] = row with { Table = snapshot with { Name = row.Table.Name } };
            foreach (var (participantId, army) in snapshotsByParticipant)
                if (_participants.TryGetValue(participantId, out var p) && p.GameId == gameId) _participants[participantId] = p with { ArmySnapshot = army };
            _games[gameId] = g with { Status = GameStatus.Playing };
        }
        return Task.CompletedTask;
    }

    bool PropertiesExist(string hash) => properties.LoadAsync(hash, CancellationToken.None).GetAwaiter().GetResult() is not null;

    /// <summary>Terrain, volume, marker and zone ids are primary keys: a tree may not take one another table already holds.</summary>
    void CheckChildKeys(TableState table)
    {
        var mine = ChildIds(table).ToList();
        if (mine.Count != mine.Distinct().Count()) throw new InvalidOperationException("a table's tree repeats an id");
        var taken = _tables.Values.Where(t => t.Table.Id != table.Id).SelectMany(t => ChildIds(t.Table)).ToHashSet();
        if (mine.FirstOrDefault(taken.Contains) is var clash && clash != Guid.Empty) throw Duplicate("table child", clash);
    }

    static IEnumerable<Guid> ChildIds(TableState t) =>
        t.Terrain.SelectMany(p => p.Volumes.Select(v => v.Id).Prepend(p.Id)).Concat(t.Markers.Select(m => m.Id)).Concat(t.Zones.Select(z => z.Id));

    /// <summary>As PostgresGameRows reads the tree: terrain, volumes and markers by id, zones by team ordinal.</summary>
    static TableState Ordered(TableState t) => t with
    {
        Terrain = t.Terrain.OrderBy(x => x.Id).Select(x => x with { Volumes = x.Volumes.OrderBy(v => v.Id).ToList() }).ToList(),
        Markers = t.Markers.OrderBy(m => m.Id).ToList(),
        Zones = t.Zones.OrderBy(z => z.TeamOrdinal).ToList(),
    };

    static InvalidOperationException Duplicate(string table, object key) => new($"{table} {key} already exists");
    static InvalidOperationException MissingKey(string table, object key) => new($"no {table} row {key}");
}
