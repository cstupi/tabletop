# Task 8: The socket driver, and the two drivers agree

**Files:**
- Create: `sim/Drivers/IWebSocketConnector.cs`, `sim/Drivers/SocketRowSeeder.cs`, `sim/Drivers/SocketDriver.cs`
- Test: `tests/Tabletop.Sim.Tests/DriverAgreementTests.cs`

**Interfaces:**
- Consumes: `IGameDriver`, `SubmitResult`, `StepResult`, `DriverMode` (Task 4); `IGameRows`, `PostgresGameRows`, `GameServerFactory`, `TestKeys` (game server skeleton; the last two only from the test project); `SocketMessage` and every kind, `WireJson`, `ProtocolVersion`, `Connect`, `Welcome`, `Rejected`, `BoardMessage`, `EventsMessage`, `CommandMessage`, `Refused`, `GameEvent`, `EventApplier`, `Projection`, `OpenAsk`, `Board`, `GameInfo`, `GameStatus`, `SchedulerShape`, `Enforcement` (Protocol); `ArmySnapshot`, `TableState`, `ContentJson` (Content); `NpgsqlDataSource` (Npgsql); `CompiledScenario` (Task 1).
- Produces: `interface IWebSocketConnector { Task<WebSocket> ConnectAsync(CancellationToken ct); }`; `sealed class ClientWebSocketConnector(Uri uri) : IWebSocketConnector`; `sealed record MintedSession(string Jwt, Guid AccountId)`; `sealed record SocketCredentials(IReadOnlyList<MintedSession> Sessions)` with `static Task<SocketCredentials> MintAsync(CompiledScenario, HttpClient, CancellationToken)`, `static string NameFor(CompiledScenario, int index)`, `Guid AccountOf(int index)`, `string For(int index)`; `sealed class SocketRowSeeder(NpgsqlDataSource db, IGameRows rows)` with `Task SeedAsync(CompiledScenario, SocketCredentials, CancellationToken)` and `Task DeleteAsync(Guid gameId, CancellationToken)`, plus `sealed class PropertiesMissingException(string hash)`; `sealed class SocketDriver : IGameDriver` with `static Task<SocketDriver> StartAsync(CompiledScenario, IWebSocketConnector, SocketCredentials, NpgsqlDataSource, CancellationToken)`, `TimeSpan RestTimeout { get; init; }`, `Task<WebSocketCloseStatus?> ReconnectRecorderAsync(CancellationToken)`.

Spec: spec → *Two drivers behind one interface* — "Over the socket. A real WebSocket to a running game server, a real session JWT, real rows and a real event log in Postgres. This exercises the handshake, the wire format, hydration, reconnect and the whole path `architecture.md` calls the only path"; "The same scenario passing in process and failing over the socket localises the fault to transport, serialization or persistence, which is the reason both exist." Module prompt → "Prefer writing rows directly through `IGameRows` plus a test-signed JWT, as the game server's own tests do."

**How the rows are written.** `account`, `game` (at `lobby`, with the scenario's own id, its configuration and its `rng_seed`), `team` and `participant` go in as raw SQL, because those four are management's writes and `IGameRows` has no method for them (game server skeleton plan → Interfaces needed 1). The `rng_seed` column is the whole of how a socket run sets the seed: the in-process driver hands `compiled.Info` straight to the session, while this driver has to put the same number in the row the server will hydrate from. Both therefore run the same dice for the same scenario seed, which is what makes the two-driver agreement test meaningful. Everything after that goes through `IGameRows`: `StartSetupAsync(gameId, snapshot)` writes the whole `table_state` tree and flips to `setup`, and `EndSetupAsync(gameId, snapshot, snapshotsByParticipant)` writes each participant's `army_snapshot` and flips to `playing`. So the scenario's own ids survive — `CreateTeamAsync` would mint its own, and the driver's projection, its recording and its answers are all keyed on the `CompiledScenario`'s ids — and the tree is written by the code that owns it.

**One minted session per team, plus one for the recorder.** A session JWT names one account, `FindParticipantAsync(gameId, accountId)` maps an account to one participant, and a newer socket for a participant closes the older one — so N participants need N accounts and N tokens. In tests `TestKeys.Mint` produces them against the fake JWKS, so the suite reaches no network. From the console they come from the management API's development-only mint, `POST /v1/dev/sessions`, one call per participant in team-ordinal order with the recorder last. Ruling 1: hand-pasting tokens is rejected, and management holds the Stytch secret, so the mint lives there and returns a real Stytch session JWT for a named account, creating the Stytch user and the `account` row as needed.

**What follows here.** `SocketCredentials` reads no environment variable and decodes no JWT to find its account id: `MintAsync` calls the endpoint and the response carries `account.id` beside the token, so `AccountOf` is a lookup. `NameFor(scenario, index)` is `sim-{scenario name}-{index}`, which is stable across runs and distinct within a scenario, so re-running a seed mints for the same accounts rather than accumulating new ones. `SocketRowSeeder` inserts `management.account` with `on conflict (id) do nothing`, which is a no-op for a minted account and is the path for a test-minted one. `--driver socket` needs no token variable at all; it needs `TABLETOP_MANAGEMENT_URL` (default `http://localhost:5100`) and a management API running under `ASPNETCORE_ENVIRONMENT=Development`, which `./scripts/management` sets.

**And what it costs.** A console socket run reaches Stytch's test project once per participant, because the tokens are real ones minted there. The in-process driver reaches nothing, and neither does `DriverAgreementTests`, which mints with `TestKeys`. A `404` from the mint means the management API is not running as a development environment, and `MintAsync` says so rather than reporting a bad token later at the handshake.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Net.WebSockets;
using Npgsql;
using Tabletop.GameServer.Persistence;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Invariants;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Tests;

[Collection("Postgres")]
public class DriverAgreementTests(PostgresFixture pg)
{
    sealed class FactoryConnector(GameServerFactory factory) : IWebSocketConnector
    {
        public async Task<WebSocket> ConnectAsync(CancellationToken ct) =>
            await factory.Server.CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/ws"), ct);
    }

    // The test never calls management's mint: TestKeys signs against the fake JWKS the factory serves,
    // so DriverAgreementTests reaches no network and needs no Stytch project.
    static SocketCredentials Credentials(CompiledScenario c) =>
        new(c.Info.Participants.Select(p => new MintedSession(TestKeys.Mint(p.AccountId), p.AccountId)).ToList());

    static async Task<RunReport> RunWith(IGameDriver driver, CompiledScenario c, ulong seed, CancellationToken ct)
    {
        var loop = new RunLoop(driver, c, new ComposedAnswers(c.Scenario.Script, new AnswerPolicy(new Chooser(seed))), new Chooser(seed));
        var report = await loop.RunAsync(ct);
        Assert.All(Invariants.CheckAll(await driver.ProjectionAsync(ct), loop.Log, c), r => Assert.True(r.Held, $"{driver.Name} {r.Name}: {r.Detail}"));
        return report;
    }

    [Fact]
    public async Task The_same_scenario_plays_the_same_game_on_both_drivers()
    {
        const ulong seed = 21;
        var scenario = ScenarioCatalog.Get("skirmish").WithSeed(seed);
        var c = ScenarioCompiler.Compile(scenario);
        var ct = TestContext.Current.CancellationToken;

        RunReport inProcess;
        await using (var driver = await InProcessDriver.StartAsync(c, ct)) inProcess = await RunWith(driver, c, seed, ct);

        using var factory = new GameServerFactory();
        var rows = new PostgresGameRows(pg.Db);
        var seeder = new SocketRowSeeder(pg.Db, rows);
        await seeder.SeedAsync(c, Credentials(c), ct);
        try
        {
            RunReport overSocket;
            await using (var driver = await SocketDriver.StartAsync(c, new FactoryConnector(factory), Credentials(c), pg.Db, ct))
                overSocket = await RunWith(driver, c, seed, ct);

            // Run for run, not merely a legitimate end on each: see the note under this block for why
            // the rest frame makes this sound.
            Assert.Equal("in-process", inProcess.Driver);
            Assert.Equal("socket", overSocket.Driver);
            Assert.True(inProcess.Passed, string.Join("\n", inProcess.Failures));
            Assert.True(overSocket.Passed, string.Join("\n", overSocket.Failures));
            Assert.Equal(inProcess.Outcome, overSocket.Outcome);
            Assert.Equal(inProcess.Rounds, overSocket.Rounds);
            Assert.Equal(inProcess.LastSeq, overSocket.LastSeq);
            Assert.Equal(inProcess.Frames, overSocket.Frames);
            Assert.Equal(inProcess.AsksAnswered, overSocket.AsksAnswered);

            // The run reached a legitimate end and did real work, so the equalities above are not two
            // runs agreeing on having gone nowhere.
            Assert.Contains(overSocket.Outcome.Kind, (string[])["decided", "round_limit"]);
            Assert.True(overSocket.Rounds >= 1, $"the socket run only reached round {overSocket.Rounds}");
            Assert.True(overSocket.AsksAnswered > 10);

            // Nothing lost, reordered or duplicated on the wire: seq starts at 1 and rises by one per
            // event, and the recording keeps a frame per event, so Frames == LastSeq on both drivers.
            Assert.Equal(inProcess.LastSeq, (long)inProcess.Frames);
            Assert.Equal(overSocket.LastSeq, (long)overSocket.Frames);
        }
        finally { await seeder.DeleteAsync(c.Info.Id, ct); }
    }

    [Fact]
    public async Task The_log_lands_in_postgres_and_hydration_reproduces_the_game()
    {
        const ulong seed = 22;
        var c = ScenarioCompiler.Compile(ScenarioCatalog.Get("stalemate").WithSeed(seed));
        var ct = TestContext.Current.CancellationToken;
        using var factory = new GameServerFactory();
        var seeder = new SocketRowSeeder(pg.Db, new PostgresGameRows(pg.Db));
        await seeder.SeedAsync(c, Credentials(c), ct);
        try
        {
            long lastSeq;
            Projection played;
            await using (var driver = await SocketDriver.StartAsync(c, new FactoryConnector(factory), Credentials(c), pg.Db, ct))
            {
                var report = await RunWith(driver, c, seed, ct);
                lastSeq = report.LastSeq;
                played = await driver.ProjectionAsync(ct);
            }
            // PostgresFixture.CountAsync returns int, so the cast is what makes the comparison a long one.
            Assert.Equal(lastSeq, (long)await pg.CountAsync(
                "select coalesce(max(seq), 0) from game.game_event where game_id = $1", c.Info.Id));

            // Every socket gone, so the session left memory; connecting again hydrates from the log alone.
            await using var again = await SocketDriver.StartAsync(c, new FactoryConnector(factory), Credentials(c), pg.Db, ct);
            Assert.Empty(ProjectionComparer.Differences(played, await again.ProjectionAsync(ct)));
        }
        finally { await seeder.DeleteAsync(c.Info.Id, ct); }
    }

    [Fact]
    public async Task Undoing_and_redoing_over_the_socket_reproduces_the_dice()
    {
        const ulong seed = 23;
        var c = ScenarioCompiler.Compile(ScenarioCatalog.Get("skirmish").WithSeed(seed));
        var ct = TestContext.Current.CancellationToken;
        using var factory = new GameServerFactory();
        var seeder = new SocketRowSeeder(pg.Db, new PostgresGameRows(pg.Db));
        await seeder.SeedAsync(c, Credentials(c), ct);
        try
        {
            await using var driver = await SocketDriver.StartAsync(c, new FactoryConnector(factory), Credentials(c), pg.Db, ct);
            var loop = new RunLoop(driver, c, new ComposedAnswers(c.Scenario.Script, new AnswerPolicy(new Chooser(seed))), new Chooser(seed));
            await loop.RunAsync(ct);

            var before = loop.Log.Where(e => e.Payload is StepCompleted { Rolls.Count: > 0 }).ToList();
            Assert.NotEmpty(before);
            var target = before[before.Count / 2].Seq - 1;

            await driver.SubmitAsync(driver.ParticipantFor(driver.TeamIds[0]), new Undo(target), ct);
            var replay = new RunLoop(driver, c, new ComposedAnswers(Replayed(loop), null), new Chooser(seed));
            await replay.RunAsync(ct);

            var after = replay.Log.Where(e => e.Payload is StepCompleted { Rolls.Count: > 0 }).ToList();
            var overlap = Math.Min(before.Count, after.Count);
            Assert.Equal(
                before.Take(overlap).SelectMany(e => ((StepCompleted)e.Payload).Rolls.SelectMany(r => r.Dice.Select(d => d.Raw))),
                after.Take(overlap).SelectMany(e => ((StepCompleted)e.Payload).Rolls.SelectMany(r => r.Dice.Select(d => d.Raw))));
        }
        finally { await seeder.DeleteAsync(c.Info.Id, ct); }
    }

    /// <summary>The answers the first run gave, replayed in order, so the redo takes the same path.</summary>
    static AnswerScript Replayed(RunLoop loop)
    {
        var builder = AnswerScript.Build();
        foreach (var answered in loop.Answers.History) builder.At(answered.Question, answered.Ordinal, answered.Answer);
        return builder.Build();
    }
}
```

**Why the two drivers are compared run-for-run.** The test asserts `inProcess.Outcome == overSocket.Outcome` and equality of `Rounds`, `LastSeq`, `Frames` and `AsksAnswered`. A quiet window could not guarantee that the two drivers saw the same Asks at the same step boundary, which would force the assertion down to a legitimate-end check; the rest frame does guarantee it, and this note records why the strict form is sound.

What the socket transport guarantees: every event the server emits reaches the recorder's socket, once, in `seq` order, and `DrainEventsAsync` sorts by `seq` and skips anything already applied, so the socket driver's projection is always a prefix of the same event sequence and never a different one. Per-viewer filtering does not weaken that, because it nulls `ask_answered.Answer` rather than withholding an event (contracts design → *Sequences*: "the filtering nulls a field rather than withholding an event"), so every stream carries every sequence number. That is what the `Frames == LastSeq` assertions check, and it is what a serialization, transport or persistence fault breaks.

What a quiet window cannot guarantee is *when* an event is observed. A server slower than the window hands the loop a shorter list of open Asks than the in-process driver sees, and two Asks of the same question open at once can then be paired with different `(question, ordinal)` keys, take different answers from the same seeded `Chooser`, and play a different game from there. `AtRest` closes exactly that gap. Both drivers return from `StepAsync` on a positive rest signal rather than on a guess: the marker at the back of the mailbox in process, the frame on the recorder's socket over the wire. Each step therefore sees every event of the batch and every Ask the batch opened, so the `(question, ordinal)` keys, the answers drawn from the `Chooser` and the sequence of commands are the same on both drivers by construction, for the same scenario and the same seed.

What remains transport-invariant rather than run-identical is stated separately and still holds: both runs pass, both satisfy all five invariants (`RunWith` checks them on each driver), and `Frames == LastSeq` holds on each independently of the other. Those are the assertions that survive a driver disagreement and localise it, which is why they stay even though the run-for-run equalities subsume them on a passing run. The two tests below carry the rest of the localisation the spec asks for: the log lands in Postgres, and hydrating from it alone reproduces the projection. A socket run that now disagrees with the in-process run on outcome, rounds, `LastSeq`, `Frames` or `AsksAnswered` is a real fault in transport, serialization or persistence, which is what this test exists to say.

`RunLoop` therefore exposes its answer source: add `public IAnswerSource Answers => answers;`.

`PostgresFixture.CountAsync` was checked against the game server skeleton plan rather than assumed: it is `public async Task<int> CountAsync(string sql, params object[] args)` (that plan, Task 3's fixture), which does take positional parameters, so the `$1` form above is right and the inline-SQL fallback is not needed. It returns `int` rather than `long` — it ends in `Convert.ToInt32(await cmd.ExecuteScalarAsync())` — which is why the `max(seq)` comparison casts. A game long enough to overflow an `int` sequence is not a game this test runs.
- [ ] **Step 2: Run; expect failure, then write `IWebSocketConnector.cs`**

Run: `docker compose up -d && dotnet test tests/Tabletop.Sim.Tests --filter DriverAgreementTests`
Expected: `CS0246: ... 'SocketDriver' could not be found`.

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Drivers;

/// <summary>Where the socket comes from: a real server over TCP, or a test server in memory.</summary>
public interface IWebSocketConnector
{
    Task<WebSocket> ConnectAsync(CancellationToken ct);
}

public sealed class ClientWebSocketConnector(Uri uri) : IWebSocketConnector
{
    public async Task<WebSocket> ConnectAsync(CancellationToken ct)
    {
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(uri, ct);
        return socket;
    }
}

/// <summary>A real Stytch session JWT and the account it names.</summary>
public sealed record MintedSession(string Jwt, Guid AccountId);

/// <summary>
/// One session per participant, in team-ordinal order with the recorder last. Nothing here verifies a
/// token: the game server does that against Stytch's JWKS. From the console the sessions come from
/// management's development-only mint; in tests TestKeys mints them against the fake JWKS.
/// </summary>
public sealed record SocketCredentials(IReadOnlyList<MintedSession> Sessions)
{
    public const string ManagementUrlVariable = "TABLETOP_MANAGEMENT_URL";
    public const string MintPath = "/v1/dev/sessions";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record MintAccount(Guid Id, string Name);
    private sealed record MintResponse(string SessionJwt, MintAccount Account);

    /// <summary>Stable across runs and distinct within a scenario, so a re-run mints for the same accounts.</summary>
    public static string NameFor(CompiledScenario scenario, int index) => $"sim-{scenario.Scenario.Name}-{index}";

    public static async Task<SocketCredentials> MintAsync(CompiledScenario scenario, HttpClient http, CancellationToken ct)
    {
        var sessions = new List<MintedSession>();
        for (var i = 0; i < scenario.Info.Participants.Count; i++)
        {
            using var response = await http.PostAsJsonAsync(MintPath, new { name = NameFor(scenario, i) }, Json, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new InvalidOperationException(
                    $"{http.BaseAddress}{MintPath.TrimStart('/')} answered 404; the management API must run as a development environment (./scripts/management sets ASPNETCORE_ENVIRONMENT=Development)");
            response.EnsureSuccessStatusCode();
            var minted = await response.Content.ReadFromJsonAsync<MintResponse>(Json, ct)
                ?? throw new InvalidOperationException("the mint returned an empty body");
            sessions.Add(new MintedSession(minted.SessionJwt, minted.Account.Id));
        }
        return new SocketCredentials(sessions);
    }

    public string For(int index) => At(index).Jwt;
    public Guid AccountOf(int index) => At(index).AccountId;

    private MintedSession At(int index) => index < Sessions.Count
        ? Sessions[index]
        : throw new InvalidOperationException($"{Sessions.Count} sessions were minted; participant {index} needs one of its own");
}
```

`MintAsync` mints serially rather than in parallel. Each call is one HTTP round trip plus three or four
Stytch calls, three participants make it four calls, and the mint inserts an `account` row, so a
sequential loop is both fast enough and free of a write race on one account id.

- [ ] **Step 3: Write `SocketRowSeeder.cs`**

```csharp
using Npgsql;
using Tabletop.Content;
using Tabletop.GameServer.Persistence;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Drivers;

public sealed class PropertiesMissingException(string hash)
    : Exception($"no properties row for {hash}; start the game server once so it seeds its embedded documents");

/// <summary>
/// The rows a scenario needs before a socket can name its game. account, game, team and participant are
/// management's writes and are raw SQL here; the table_state tree and the army snapshots go through
/// IGameRows, which owns them (game server skeleton plan → Interfaces needed 1).
/// </summary>
public sealed class SocketRowSeeder(NpgsqlDataSource db, IGameRows rows)
{
    public async Task SeedAsync(CompiledScenario scenario, SocketCredentials credentials, CancellationToken ct)
    {
        if (!await rows.PropertiesExistAsync(scenario.Properties.ContentHash, ct))
            throw new PropertiesMissingException(scenario.Properties.ContentHash);

        var accounts = scenario.Info.Participants.Select((_, i) => credentials.AccountOf(i)).ToList();
        var info = scenario.Info;

        await using var connection = await db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        for (var i = 0; i < accounts.Count; i++)
            await Execute(connection, transaction, ct,
                "insert into management.account (id, name) values ($1, $2) on conflict (id) do nothing",
                accounts[i], info.Participants[i].Name);

        // rng_seed is how a socket run sets the seed: the driver writes a row rather than constructing a
        // GameInfo, so the scenario's seed goes in the insert and the game server reads it back at
        // hydration with the rest of the game's row.
        await Execute(connection, transaction, ct,
            """
            insert into management.game (id, name, status, game_rules, properties_hash, scheduler_shape, enforcement, waits_between_steps, created_by, rng_seed)
            values ($1, $2, 'lobby', $3, $4, $5, $6, $7, $8, $9)
            """,
            info.Id, info.Name, info.GameRules, info.PropertiesHash,
            Wire(info.SchedulerShape), Wire(info.Enforcement), info.WaitsBetweenSteps, accounts[0], info.RngSeed);

        foreach (var team in info.Teams)
            await Execute(connection, transaction, ct,
                "insert into management.team (id, game_id, ordinal) values ($1, $2, $3)", team.Id, info.Id, team.Ordinal);

        for (var i = 0; i < info.Participants.Count; i++)
            await Execute(connection, transaction, ct,
                "insert into management.participant (id, game_id, team_id, account_id, army_id, army_snapshot) values ($1, $2, $3, $4, null, null)",
                info.Participants[i].Id, info.Id, info.Participants[i].TeamId, accounts[i]);

        await transaction.CommitAsync(ct);

        // The setup result, written by the code that owns the tree; end-setup flips the game to playing.
        await rows.StartSetupAsync(info.Id, scenario.Snapshot, ct);
        await rows.EndSetupAsync(info.Id, scenario.Snapshot,
            scenario.Board.Armies.ToDictionary(a => a.ParticipantId, a => (ArmySnapshot?)a.Snapshot), ct);
    }

    public async Task DeleteAsync(Guid gameId, CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        await Execute(connection, null, ct, "delete from game.game_event where game_id = $1", gameId);
        await Execute(connection, null, ct, "delete from management.game where id = $1", gameId);   // the tree cascades
    }

    private static string Wire<T>(T value) where T : struct, Enum => WireJson.Serialize(value).Trim('"');

    private static async Task Execute(NpgsqlConnection connection, NpgsqlTransaction? transaction, CancellationToken ct,
        string sql, params object?[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }
}
```

`management.participant` carries `game_id` and `army_id` per the skeleton plan's *Interfaces needed* item 1 (`participant (id, game_id, team_id, account_id, army_id, army_snapshot)`); `docs/schema.md`'s diagram omits both, which is a staleness in the diagram rather than in the schema — `scripts/postgres/init/001_schema.sql` is the authority. If the SQL disagrees, follow the SQL and record the discrepancy as a correction to `docs/schema.md`.
- [ ] **Step 4: Write `SocketDriver.cs`**

```csharp
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Npgsql;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Drivers;

/// <summary>
/// A real WebSocket to a running game server, a real session JWT, real rows and a real event log in Postgres
/// — the whole path architecture.md calls the only path (spec → Two drivers behind one interface).
/// </summary>
public sealed class SocketDriver : IGameDriver
{
    private sealed class Peer(Guid participantId, WebSocket socket)
    {
        public Guid ParticipantId { get; } = participantId;
        public WebSocket Socket { get; } = socket;
        public Channel<SocketMessage> Inbox { get; } = Channel.CreateUnbounded<SocketMessage>();
        public Task? Reader { get; set; }
    }

    private readonly CompiledScenario _scenario;
    private readonly IWebSocketConnector _connector;
    private readonly SocketCredentials _credentials;
    private readonly EventApplier _applier;
    private readonly List<Peer> _players = [];
    private readonly List<GameEvent> _pending = [];
    private readonly CancellationTokenSource _stopping = new();
    // True when the last command appended, so a rest frame is on its way to the recorder too.
    private bool _restDueOnRecorder;
    private Peer _recorder = null!;

    private SocketDriver(CompiledScenario scenario, IWebSocketConnector connector, SocketCredentials credentials)
    {
        _scenario = scenario;
        _connector = connector;
        _credentials = credentials;
        _applier = new EventApplier(scenario.Board, scenario.Info);
    }

    /// <summary>How long one wait for AtRest may take before the server counts as hung. Not a quiescence guess.</summary>
    public TimeSpan RestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public static async Task<SocketDriver> StartAsync(CompiledScenario scenario, IWebSocketConnector connector,
        SocketCredentials credentials, NpgsqlDataSource db, CancellationToken ct)
    {
        var driver = new SocketDriver(scenario, connector, credentials);
        await driver.StartAsync(ct);
        return driver;
    }

    public string Name => "socket";
    public Guid GameId => _scenario.Info.Id;
    public GameInfo Info => _scenario.Info;
    public Board Board => _scenario.Board;
    public IReadOnlyList<Guid> TeamIds => _scenario.TeamIds;
    public Guid ParticipantFor(Guid teamId) => _scenario.ParticipantFor(teamId);
    public DriverMode Mode { get; set; } = DriverMode.Run;

    public async Task StartAsync(CancellationToken ct)
    {
        // The recorder connects first, so its stream carries the whole log from the start.
        var recorderIndex = _scenario.Info.Participants.Count - 1;
        _recorder = await OpenAsync(_scenario.RecorderParticipantId, recorderIndex, ct);
        for (var i = 0; i < _scenario.ParticipantIds.Count; i++)
            _players.Add(await OpenAsync(_scenario.ParticipantIds[i], i, ct));
        // Connect appends nothing, so its AtRest goes to the connecting client alone: waiting for the
        // recorder's own says the whole log has arrived, where draining whatever had arrived raced it.
        await RestAsync(ct);
        await DrainEventsAsync(ct);
    }

    private async Task<Peer> OpenAsync(Guid participantId, int credentialIndex, CancellationToken ct)
    {
        var socket = await _connector.ConnectAsync(ct);
        var peer = new Peer(participantId, socket);
        peer.Reader = Task.Run(() => ReadLoopAsync(peer), _stopping.Token);
        await SendAsync(peer, new Connect(_credentials.For(credentialIndex), GameId, ProtocolVersion.Current));
        var welcome = await ExpectAsync(peer, ct);
        if (welcome is Rejected rejected) throw new InvalidOperationException($"the server rejected the connection: {rejected.Reason}");
        if (welcome is not Welcome w) throw new InvalidOperationException($"expected a welcome, got {welcome.GetType().Name}");
        if (w.ParticipantId != participantId)
            throw new InvalidOperationException($"the JWT at index {credentialIndex} named participant {w.ParticipantId:N}, not {participantId:N}");
        return peer;
    }

    public Task<IReadOnlyList<OpenAsk>> OpenAsksAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<OpenAsk>>(_applier.Projection.OpenAsks.Values.ToList());

    public async Task<SubmitResult> SubmitAsync(Guid participantId, Command command, CancellationToken ct)
    {
        var peer = _players.FirstOrDefault(p => p.ParticipantId == participantId)
            ?? throw new ArgumentException($"no socket for participant {participantId}", nameof(participantId));
        var commandId = Guid.CreateVersion7();
        await SendAsync(peer, new CommandMessage(commandId, command));
        // Refusals come back on the submitting socket, before that command's AtRest, so the rest frame
        // ends the wait and no timing window is involved. Its Seq is the log's high-water mark: above
        // what the recorder has applied means the command appended, and the game server broadcasts the
        // batch and its rest frame to every socket. Equal means it appended nothing, and the rest frame
        // went to this socket alone, so the recorder has nothing coming and StepAsync must not wait.
        var (refusal, restSeq) = await WaitForRefusalAsync(peer, commandId, ct);
        _restDueOnRecorder = restSeq > _applier.Projection.LastSeq;
        return refusal is null ? new SubmitResult.Accepted(null) : new SubmitResult.Rejected(refusal.Reason, refusal.Detail);
    }

    public Task<IReadOnlyList<GameEvent>> DrainEventsAsync(CancellationToken ct)
    {
        // Anything RestAsync already read is in _pending; anything queued since is read here. Every
        // kind but EventsMessage is ignored rather than rejected, which is how a driver tolerates a
        // frame it has no use for.
        while (_recorder.Inbox.Reader.TryRead(out var frame))
            if (frame is EventsMessage message) _pending.AddRange(message.Events);
        foreach (var player in _players) while (player.Inbox.Reader.TryRead(out _)) { }
        var ordered = _pending.OrderBy(e => e.Seq).ToList();
        _pending.Clear();
        foreach (var e in ordered) if (e.Seq > _applier.Projection.LastSeq) _applier.Apply(e);
        return Task.FromResult<IReadOnlyList<GameEvent>>(ordered);
    }

    public Task<Projection> ProjectionAsync(CancellationToken ct) => Task.FromResult(_applier.Projection);

    public async Task<StepResult> StepAsync(CancellationToken ct)
    {
        // No batch is on its way when the last command appended nothing, and none is on its way before
        // the first command of the run: the server is already at rest and the step is an empty one.
        AtRest? rest = null;
        if (_restDueOnRecorder) { rest = await RestAsync(ct); _restDueOnRecorder = false; }
        var events = await DrainEventsAsync(ct);
        // One connection delivers in order, so every EventsMessage of the batch preceded its AtRest.
        // A mismatch is a lost, reordered or duplicated frame, and it is a fault rather than a step.
        if (rest is not null && _applier.Projection.LastSeq != rest.Seq)
            throw new InvalidOperationException(
                $"the server rested at seq {rest.Seq} but the recorder's stream reached {_applier.Projection.LastSeq}");
        return new StepResult(events, _applier.Projection.OpenAsks.Values.ToList(), events.Count == 0);
    }

    /// <summary>Reconnect: the same participant, a second socket. The first is closed by the server with 1000.</summary>
    public async Task<WebSocketCloseStatus?> ReconnectRecorderAsync(CancellationToken ct)
    {
        var old = _recorder;
        _pending.Clear();
        _restDueOnRecorder = false;
        _recorder = await OpenAsync(old.ParticipantId, _scenario.Info.Participants.Count - 1, ct);
        await RestAsync(ct);
        await DrainEventsAsync(ct);
        return old.Socket.CloseStatus;
    }

    /// <summary>
    /// Read the recorder's socket until the server says it is at rest, keeping the events read on the
    /// way for DrainEventsAsync. RestTimeout guards a hung server; it is not a quiescence guess.
    /// </summary>
    private async Task<AtRest> RestAsync(CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(RestTimeout);
        try
        {
            while (true)
            {
                var frame = await _recorder.Inbox.Reader.ReadAsync(deadline.Token);
                if (frame is EventsMessage message) _pending.AddRange(message.Events);
                else if (frame is AtRest rest) return rest;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new RestTimeoutException(RestTimeout, _applier.Projection.LastSeq);
        }
        catch (ChannelClosedException)
        {
            throw new RestTimeoutException(RestTimeout, _applier.Projection.LastSeq);
        }
    }

    /// <summary>
    /// A command's Refused precedes that command's AtRest on the submitting socket, whatever the
    /// outcome, so the rest frame ends the wait. Returns the refusal if there was one and the rest
    /// frame's Seq either way.
    /// </summary>
    private async Task<(Refused? Refusal, long RestSeq)> WaitForRefusalAsync(Peer peer, Guid commandId, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(RestTimeout);
        Refused? refusal = null;
        try
        {
            while (true)
            {
                var frame = await peer.Inbox.Reader.ReadAsync(deadline.Token);
                if (frame is Refused r && r.MessageId == commandId) refusal = r;
                if (frame is AtRest rest) return (refusal, rest.Seq);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new RestTimeoutException(RestTimeout, _applier.Projection.LastSeq);
        }
        catch (ChannelClosedException)
        {
            throw new RestTimeoutException(RestTimeout, _applier.Projection.LastSeq);
        }
    }

    private async Task<SocketMessage> ExpectAsync(Peer peer, CancellationToken ct)
    {
        using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
        window.CancelAfter(TimeSpan.FromSeconds(10));
        return await peer.Inbox.Reader.ReadAsync(window.Token);
    }

    private static async Task SendAsync(Peer peer, SocketMessage message) =>
        await peer.Socket.SendAsync(Encoding.UTF8.GetBytes(WireJson.Serialize(message)), WebSocketMessageType.Text, true, CancellationToken.None);

    private async Task ReadLoopAsync(Peer peer)
    {
        var buffer = new byte[4 * 1024 * 1024];
        try
        {
            while (peer.Socket.State == WebSocketState.Open)
            {
                var total = 0;
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await peer.Socket.ReceiveAsync(new Memory<byte>(buffer, total, buffer.Length - total), _stopping.Token);
                    if (result.MessageType == WebSocketMessageType.Close) { peer.Inbox.Writer.TryComplete(); return; }
                    total += result.Count;
                } while (!result.EndOfMessage);
                peer.Inbox.Writer.TryWrite(WireJson.Deserialize(Encoding.UTF8.GetString(buffer, 0, total)));
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally { peer.Inbox.Writer.TryComplete(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        foreach (var peer in _players.Append(_recorder))
        {
            try { await peer.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "sim finished", CancellationToken.None); }
            catch (WebSocketException) { }
            peer.Socket.Dispose();
        }
        _stopping.Dispose();
    }
}
```

`RestAsync` is the socket driver's one wait. It reads the recorder's socket until an `AtRest` arrives, keeping the events it passes on the way, so a step costs what the server takes to answer it and nothing more. Three hazards a quiet window would carry are absent: a slow server cannot read as a rested one, `StartAsync` and `ReconnectRecorderAsync` cannot race the connect-time log delivery, and `WaitForRefusalAsync` cannot read a slow refusal as an acceptance. The in-process driver is the default, because it needs no Postgres, no server process and no Docker, and the socket one runs a smaller set of scenarios in CI.

**What Task 8 breaks without**, all of it already decided by the game server skeleton plan and consumed rather than requested here: an `AtRest` at the end of `ConnectAsync` at every status, an `AtRest` at the end of `HandleCommandAsync` whatever the outcome, a `Refused` before that command's `AtRest` on the sender's socket, and `Seq` as the log's high-water mark. A server that appends events and never sends the frame leaves the step waiting out `RestTimeout` and the run `Failed`, which is loud rather than silent and is why the wait is bounded at all.

- [ ] **Step 5: Run the tests; expect all green, then commit**

```bash
docker compose up -d
dotnet test tests/Tabletop.Sim.Tests --filter DriverAgreementTests
git add sim/Drivers tests/Tabletop.Sim.Tests
git commit -m "Play the same scenario over a real socket and a real Postgres log"
```
