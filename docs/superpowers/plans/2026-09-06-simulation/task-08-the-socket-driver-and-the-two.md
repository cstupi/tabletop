# Task 8: The socket driver, and the two drivers agree

**Files:**
- Create: `sim/Drivers/IWebSocketConnector.cs`, `sim/Drivers/SocketRowSeeder.cs`, `sim/Drivers/SocketDriver.cs`
- Modify: `sim/Drivers/IGameDriver.cs` (`RestTimeoutException`, `DriverFaultException`), `sim/Drivers/RecorderClient.cs` (`CloseStatus`, `CloseReason`), `sim/Run/RunLoop.cs` (`Answers`), `sim/Run/RunReport.cs` (`RunSettings.Record`)
- Test: `tests/Tabletop.Sim.Tests/DriverAgreementTests.cs`

**Interfaces:**
- Consumes: `IGameDriver`, `SubmitResult`, `StepResult`, `DriverMode` (Task 4); `IGameRows`, `PostgresGameRows`, `GameServerFactory`, `TestKeys` (game server skeleton; the last two only from the test project), `PostgresPropertiesStore`, `PropertiesSeeder`, `SessionRegistry` (game server; the tests only); `SocketMessage` and every kind, `WireJson`, `ProtocolVersion`, `Connect`, `Welcome`, `Rejected`, `BoardMessage`, `EventsMessage`, `CommandMessage`, `Refused`, `GameEvent`, `EventApplier`, `Projection`, `OpenAsk`, `Board`, `GameInfo`, `GameStatus`, `SchedulerShape`, `Enforcement` (Protocol); `ArmySnapshot`, `TableState`, `ContentJson` (Content); `NpgsqlDataSource` (Npgsql); `CompiledScenario` (Task 1).
- Produces: `interface IWebSocketConnector { Task<WebSocket> ConnectAsync(CancellationToken ct); }`; `sealed class ClientWebSocketConnector(Uri uri) : IWebSocketConnector`; `sealed record MintedSession(string Jwt, Guid AccountId)`; `sealed record SocketCredentials(IReadOnlyList<MintedSession> Sessions)` with `static Task<SocketCredentials> MintAsync(CompiledScenario, HttpClient, CancellationToken)`, `static string NameFor(CompiledScenario, int index)`, `Guid AccountOf(int index)`, `string For(int index)`; `sealed class SocketRowSeeder(NpgsqlDataSource db, IGameRows rows)` with `Task SeedAsync(CompiledScenario, SocketCredentials, CancellationToken)`, `Task DeleteAsync(Guid gameId, CancellationToken)` and `Task DeleteAccountsAsync(SocketCredentials, CancellationToken)` (for the accounts a test minted), plus `sealed class PropertiesMissingException(string hash)`; `sealed class SocketDriver : IGameDriver` with `static Task<SocketDriver> StartAsync(CompiledScenario, IWebSocketConnector, SocketCredentials, CancellationToken)` (no database handle: the rows are written by `SocketRowSeeder`, not by the driver), `TimeSpan RestTimeout { get; init; }`, `Task<WebSocketCloseStatus?> ReconnectRecorderAsync(CancellationToken)`. On the recorder, `RecorderClient.CloseStatus` and `RecorderClient.CloseReason` keep the server's close. `RestTimeoutException(TimeSpan, long, Exception? cause = null)` carries the reader's first fault as its inner exception, and `DriverFaultException(string)` reports a lost, reordered or duplicated frame. `RunSettings` gains `bool Record = true`, so the undo-replay loop builds no frames, and `RunLoop` gains `Answers`.

Spec: spec → *Two drivers behind one interface* — "Over the socket. A real WebSocket to a running game server, a real session JWT, real rows and a real event log in Postgres. This exercises the handshake, the wire format, hydration, reconnect and the whole path `architecture.md` calls the only path"; "The same scenario passing in process and failing over the socket localises the fault to transport, serialization or persistence, which is the reason both exist." Module prompt → "Prefer writing rows directly through `IGameRows` plus a test-signed JWT, as the game server's own tests do."

**How the rows are written.** `account`, `game` (at `lobby`, with the scenario's own id, its configuration and its `rng_seed`), `team` and `participant` go in as raw SQL, because those four are management's writes and `IGameRows` has no method for them (game server skeleton plan → Interfaces needed 1). The `rng_seed` column is the whole of how a socket run sets the seed: the in-process driver hands `compiled.Info` straight to the session, while this driver has to put the same number in the row the server will hydrate from. Both therefore run the same dice for the same scenario seed, which is what makes the two-driver agreement test meaningful. Everything after that goes through `IGameRows`: `StartSetupAsync(gameId, snapshot)` writes the whole `table_state` tree and flips to `setup`, and `EndSetupAsync(gameId, snapshot, snapshotsByParticipant)` writes each participant's `army_snapshot` and flips to `playing`. So the scenario's own ids survive — `CreateTeamAsync` would mint its own, and the driver's projection, its recording and its answers are all keyed on the `CompiledScenario`'s ids — and the tree is written by the code that owns it.

**One minted session per team, plus one for the recorder.** A session JWT names one account, `FindParticipantAsync(gameId, accountId)` maps an account to one participant, and a newer socket for a participant closes the older one — so N participants need N accounts and N tokens. In tests `TestKeys.Mint` produces them against the fake JWKS, so the suite reaches no network. From the console they come from the management API's development-only mint, `POST /v1/dev/sessions`, one call per participant in team-ordinal order with the recorder last. Ruling 1: hand-pasting tokens is rejected, and management holds the Stytch secret, so the mint lives there and returns a real Stytch session JWT for a named account, creating the Stytch user and the `account` row as needed.

**What follows here.** `SocketCredentials` reads no environment variable and decodes no JWT to find its account id: `MintAsync` calls the endpoint and the response carries `account.id` beside the token, so `AccountOf` is a lookup. `NameFor(scenario, index)` is `sim-{scenario name}-{index}`, which is stable across runs and distinct within a scenario, so re-running a seed mints for the same accounts rather than accumulating new ones. `SocketRowSeeder` inserts `management.account` with `on conflict (id) do nothing`, which is a no-op for a minted account and is the path for a test-minted one. `--driver socket` needs no token variable at all; it needs `TABLETOP_SIM_MANAGEMENT` (default `http://127.0.0.1:5100`) and a management API running under `ASPNETCORE_ENVIRONMENT=Development`, which `./scripts/management` sets.

**And what it costs.** A console socket run reaches Stytch's test project once per participant, because the tokens are real ones minted there. The in-process driver reaches nothing, and neither does `DriverAgreementTests`, which mints with `TestKeys`. A `404` from the mint means the management API is not running as a development environment, and `MintAsync` says so rather than reporting a bad token later at the handshake.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Net.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Tabletop.GameServer.Persistence;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Session;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Tests;

[Collection("Postgres")]
public class DriverAgreementTests(PostgresFixture pg)
{
    static CancellationToken Ct => CancellationToken.None;

    sealed class FactoryConnector(GameServerFactory factory) : IWebSocketConnector
    {
        public async Task<WebSocket> ConnectAsync(CancellationToken ct) =>
            await factory.Server.CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/ws"), ct);
    }

    // The test never calls management's mint: TestKeys signs against the fake JWKS the factory serves,
    // so DriverAgreementTests reaches no network and needs no Stytch project.
    static SocketCredentials Credentials(CompiledScenario c) =>
        new(c.Info.Participants.Select(p => new MintedSession(TestKeys.Mint(p.AccountId), p.AccountId)).ToList());

    /// <summary>
    /// GameServerFactory runs in the Test environment, where Program does not seed the embedded documents, so
    /// the core document goes into Postgres through the server's own store (idempotent, hash-checked).
    /// sim/ never writes a properties row.
    /// </summary>
    async Task SeedCoreProperties() =>
        await new PostgresPropertiesStore(pg.Db).SeedAsync(
            PropertiesSeeder.EmbeddedDocuments().Single(d => d.GameRules == GrimdarkFutureRules.RulesId), Ct);

    /// <summary>The seeder and credentials exist before the try, so the finally can always clean up what a partial seed left.</summary>
    (SocketRowSeeder Seeder, SocketCredentials Credentials) Prepare(CompiledScenario c) =>
        (new SocketRowSeeder(pg.Db, new PostgresGameRows(pg.Db)), Credentials(c));

    async Task Seed(SocketRowSeeder seeder, CompiledScenario c, SocketCredentials credentials)
    {
        await SeedCoreProperties();
        await seeder.SeedAsync(c, credentials, Ct);
    }

    static async Task Clean(SocketRowSeeder seeder, CompiledScenario c, SocketCredentials credentials)
    {
        try { await seeder.DeleteAsync(c.Info.Id, Ct); }
        finally { await seeder.DeleteAccountsAsync(credentials, Ct); }
    }

    static RunLoop LoopFor(IGameDriver driver, CompiledScenario c, ulong seed, AnswerScript? script = null, RunSettings? settings = null) =>
        new(driver, c, new ComposedAnswers(script ?? c.Scenario.Script, new AnswerPolicy(new Chooser(seed))), HandPlacement.ChooserFor(seed), settings);

    static async Task<(RunReport Report, RunLoop Loop)> RunWith(IGameDriver driver, CompiledScenario c, ulong seed)
    {
        var loop = LoopFor(driver, c, seed);
        var report = await loop.RunAsync(Ct);
        Assert.All(Invariants.Invariants.CheckAll(await driver.ProjectionAsync(Ct), loop.Log, c),
            r => Assert.True(r.Held, $"{driver.Name} {r.Name}: {r.Detail}"));
        return (report, loop);
    }

    static IEnumerable<int> Dice(IEnumerable<GameEvent> log) =>
        log.Where(e => e.Payload is StepCompleted).SelectMany(e => ((StepCompleted)e.Payload).Rolls.SelectMany(r => r.Dice.Select(d => d.Raw)));

    /// <summary>Question, ordinal and the kind of answer: ids differ between runs, so the answer's own content is not compared.</summary>
    static IEnumerable<string> History(RunLoop loop) =>
        loop.Answers.History.Select(a => $"{a.Question}#{a.Ordinal}:{a.Answer.GetType().Name}");

    [Theory]
    [InlineData(21UL)]
    [InlineData(24UL)]
    public async Task The_same_scenario_plays_the_same_game_on_both_drivers(ulong seed)
    {
        var scenario = ScenarioCatalog.Get("skirmish").WithSeed(seed);
        var inProcessScenario = ScenarioCompiler.Compile(scenario);

        RunReport inProcess;
        RunLoop inProcessLoop;
        await using (var driver = await InProcessDriver.StartAsync(inProcessScenario, Ct))
            (inProcess, inProcessLoop) = await RunWith(driver, inProcessScenario, seed);

        // A second compile: the ids differ, the seed does not.
        var c = ScenarioCompiler.Compile(scenario);
        using var factory = new GameServerFactory();
        var (seeder, credentials) = Prepare(c);
        try
        {
            await Seed(seeder, c, credentials);
            RunReport overSocket;
            RunLoop socketLoop;
            await using (var driver = await SocketDriver.StartAsync(c, new FactoryConnector(factory), credentials, Ct))
                (overSocket, socketLoop) = await RunWith(driver, c, seed);

            Assert.Equal("in-process", inProcess.Driver);
            Assert.Equal("socket", overSocket.Driver);
            Assert.True(inProcess.Passed, string.Join("\n", inProcess.Failures));
            Assert.True(overSocket.Passed, string.Join("\n", overSocket.Failures));
            // The winning team's id differs between runs, so the kind is compared, not the record.
            Assert.Equal(inProcess.Outcome.Kind, overSocket.Outcome.Kind);
            Assert.Equal(inProcess.Rounds, overSocket.Rounds);
            Assert.Equal(inProcess.LastSeq, overSocket.LastSeq);
            Assert.Equal(inProcess.Frames, overSocket.Frames);
            Assert.Equal(inProcess.AsksAnswered, overSocket.AsksAnswered);
            Assert.Equal(History(inProcessLoop), History(socketLoop));
            Assert.Equal(Dice(inProcessLoop.Log), Dice(socketLoop.Log));
            Assert.Equal(inProcessLoop.Log.Select(e => e.Payload.GetType()), socketLoop.Log.Select(e => e.Payload.GetType()));

            // The run reached a legitimate end and did real work, so the equalities above are not two
            // runs agreeing on having gone nowhere.
            Assert.Contains(overSocket.Outcome.Kind, (string[])["decided", "round_limit"]);
            Assert.True(overSocket.Rounds >= 1, $"the socket run only reached round {overSocket.Rounds}");
            Assert.True(overSocket.AsksAnswered > 10);
            Assert.NotEmpty(Dice(socketLoop.Log));

            // Nothing lost, reordered or duplicated on the wire: seq starts at 1 and rises by one per
            // event, and the recording keeps a frame per event, so Frames == LastSeq on both drivers.
            Assert.Equal(inProcess.LastSeq, (long)inProcess.Frames);
            Assert.Equal(overSocket.LastSeq, (long)overSocket.Frames);
            Assert.Equal(socketLoop.Log.Select(e => e.Seq), socketLoop.Log.Select(e => e.Seq).Distinct());
        }
        finally { await Clean(seeder, c, credentials); }
    }

    [Fact]
    public async Task The_log_lands_in_postgres_and_hydration_reproduces_the_game()
    {
        const ulong seed = 22;
        var c = ScenarioCompiler.Compile(ScenarioCatalog.Get("stalemate").WithSeed(seed));
        using var factory = new GameServerFactory();
        var (seeder, credentials) = Prepare(c);
        try
        {
            await Seed(seeder, c, credentials);
            long lastSeq;
            Projection played;
            await using (var driver = await SocketDriver.StartAsync(c, new FactoryConnector(factory), credentials, Ct))
            {
                var (report, _) = await RunWith(driver, c, seed);
                lastSeq = report.LastSeq;
                played = await driver.ProjectionAsync(Ct);
            }
            // PostgresFixture.CountAsync returns int, so the cast is what makes the comparison a long one.
            Assert.Equal(lastSeq, (long)await pg.CountAsync(
                "select coalesce(max(seq), 0) from game.game_event where game_id = $1", c.Info.Id));

            // Every socket gone, so the session leaves memory; connecting again hydrates from the log alone.
            // The registry releases it after the last socket's unwind, which is asynchronous to the dispose.
            var registry = factory.Services.GetRequiredService<SessionRegistry>();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (registry.Live.Count > 0 && DateTime.UtcNow < deadline) await Task.Delay(20);
            Assert.Empty(registry.Live);

            await using var again = await SocketDriver.StartAsync(c, new FactoryConnector(factory), credentials, Ct);
            Assert.Empty(Invariants.ProjectionComparer.Differences(played, await again.ProjectionAsync(Ct)));
        }
        finally { await Clean(seeder, c, credentials); }
    }

    [Fact]
    public async Task A_submit_is_not_answered_by_a_stale_rest_frame()
    {
        // Team 1's socket holds the connect-time AtRest and the AtRest broadcast by team 0's command.
        // A refused command from team 1 must still be reported as refused, not read off one of those.
        var c = ScenarioCompiler.Compile(ScenarioCatalog.Get("skirmish").WithSeed(5));
        using var factory = new GameServerFactory();
        var (seeder, credentials) = Prepare(c);
        try
        {
            await Seed(seeder, c, credentials);
            IGameDriver driver = await SocketDriver.StartAsync(c, new FactoryConnector(factory), credentials, Ct);
            await using var _ = driver;
            var gate = (await driver.OpenAsksAsync(Ct)).Single().Ask;
            Assert.IsType<SubmitResult.Accepted>(await driver.AnswerAsync(gate.Teams[0], gate.Id, new Confirmed(), Ct));

            var stale = await driver.AnswerAsync(c.TeamIds[1], Guid.NewGuid(), new Confirmed(), Ct);
            Assert.IsType<SubmitResult.Rejected>(stale);

            var step = await driver.StepAsync(Ct);                                   // the accepted answer's events, once
            Assert.Contains(step.Events, e => e.Payload is AskAnswered);
            Assert.Equal(AskQuestions.Deploy, step.OpenAsks.Single().Ask.Question);
            var again = await driver.StepAsync(Ct);
            Assert.Empty(again.Events);
            Assert.True(again.Quiescent);
        }
        finally { await Clean(seeder, c, credentials); }
    }

    [Fact]
    public async Task Every_event_is_returned_exactly_once_across_submit_and_drain()
    {
        var c = ScenarioCompiler.Compile(ScenarioCatalog.Get("skirmish").WithSeed(6));
        using var factory = new GameServerFactory();
        var (seeder, credentials) = Prepare(c);
        try
        {
            await Seed(seeder, c, credentials);
            IGameDriver driver = await SocketDriver.StartAsync(c, new FactoryConnector(factory), credentials, Ct);
            await using var _ = driver;
            var seen = new List<GameEvent>();
            var gate = (await driver.OpenAsksAsync(Ct)).Single().Ask;
            await driver.AnswerAsync(gate.Teams[0], gate.Id, new Confirmed(), Ct);
            seen.AddRange(await driver.DrainEventsAsync(Ct));
            seen.AddRange((await driver.StepAsync(Ct)).Events);
            Assert.NotEmpty(seen);
            Assert.Equal(seen.Select(e => e.Seq), seen.Select(e => e.Seq).Distinct());
            Assert.Equal((await driver.ProjectionAsync(Ct)).LastSeq, seen.Max(e => e.Seq));
        }
        finally { await Clean(seeder, c, credentials); }
    }

    [Fact]
    public async Task Undoing_and_redoing_over_the_socket_reproduces_the_dice()
    {
        const ulong seed = 23;
        var c = ScenarioCompiler.Compile(ScenarioCatalog.Get("skirmish").WithSeed(seed));
        using var factory = new GameServerFactory();
        var (seeder, credentials) = Prepare(c);
        try
        {
            await Seed(seeder, c, credentials);
            await using var driver = await SocketDriver.StartAsync(c, new FactoryConnector(factory), credentials, Ct);
            var loop = LoopFor(driver, c, seed);
            await loop.RunAsync(Ct);

            // A target after deployment: the first dice-bearing step is a play step, and the event before it is live.
            var diceSteps = loop.Log.Where(e => e.Payload is StepCompleted { Rolls.Count: > 0 }).ToList();
            Assert.NotEmpty(diceSteps);
            var target = diceSteps[diceSteps.Count / 2].Seq - 1;
            var before = loop.Log.Where(e => e.Seq > target).ToList();

            Assert.IsType<SubmitResult.Accepted>(
                await driver.SubmitAsync(driver.ParticipantFor(driver.TeamIds[0]), new Undo(target), Ct));

            // There is no Redo command: redoing is answering again. The new loop's counters start at zero over a
            // game now at `target`, so each question's ordinals shift by the answers already in the kept prefix.
            var replay = LoopFor(driver, c, seed, Shifted(loop, target), new RunSettings(CheckInvariants: false, Record: false));
            var report = await replay.RunAsync(Ct);
            Assert.True(report.Outcome is RunOutcome.Decided or RunOutcome.RoundLimit, report.Outcome.ToString());

            var original = Dice(before).ToList();
            Assert.NotEmpty(original);
            Assert.Equal(original, Dice(replay.Log).ToList());
        }
        finally { await Clean(seeder, c, credentials); }
    }

    sealed class TrackingConnector(GameServerFactory factory, Func<WebSocket, WebSocket>? wrap = null) : IWebSocketConnector
    {
        public List<WebSocket> Sockets { get; } = [];
        public async Task<WebSocket> ConnectAsync(CancellationToken ct)
        {
            WebSocket socket = await factory.Server.CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/ws"), ct);
            var given = wrap?.Invoke(socket) ?? socket;
            Sockets.Add(given);
            return given;
        }
    }

    /// <summary>Delivers the real frames until `garbleAt`, then a frame that is not JSON.</summary>
    sealed class GarblingSocket(WebSocket inner, int garbleAt) : WebSocket
    {
        private int _received;
        public override WebSocketCloseStatus? CloseStatus => inner.CloseStatus;
        public override string? CloseStatusDescription => inner.CloseStatusDescription;
        public override WebSocketState State => inner.State;
        public override string? SubProtocol => inner.SubProtocol;
        public override void Abort() => inner.Abort();
        public override Task CloseAsync(WebSocketCloseStatus s, string? d, CancellationToken ct) => inner.CloseAsync(s, d, ct);
        public override Task CloseOutputAsync(WebSocketCloseStatus s, string? d, CancellationToken ct) => inner.CloseOutputAsync(s, d, ct);
        public override void Dispose() => inner.Dispose();
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> b, CancellationToken ct) => inner.ReceiveAsync(b, ct);
        public override Task SendAsync(ArraySegment<byte> b, WebSocketMessageType t, bool e, CancellationToken ct) => inner.SendAsync(b, t, e, ct);
        public override async ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken ct)
        {
            if (++_received < garbleAt) return await inner.ReceiveAsync(buffer, ct);
            var junk = "{ this is not a frame"u8.ToArray();
            junk.CopyTo(buffer.Span);
            return new ValueWebSocketReceiveResult(junk.Length, WebSocketMessageType.Text, true);
        }
    }

    [Fact]
    public async Task A_malformed_frame_is_reported_as_the_cause_of_the_failed_wait()
    {
        var c = ScenarioCompiler.Compile(ScenarioCatalog.Get("skirmish").WithSeed(7));
        using var factory = new GameServerFactory();
        var (seeder, credentials) = Prepare(c);
        try
        {
            await Seed(seeder, c, credentials);
            var connector = new TrackingConnector(factory, s => new GarblingSocket(s, garbleAt: 2));   // welcome, then junk
            var ex = await Assert.ThrowsAsync<RestTimeoutException>(() => SocketDriver.StartAsync(c, connector, credentials, Ct));
            Assert.NotNull(ex.InnerException);
            Assert.IsAssignableFrom<System.Text.Json.JsonException>(ex.InnerException);
            Assert.All(connector.Sockets, s => Assert.NotEqual(WebSocketState.Open, s.State));
        }
        finally { await Clean(seeder, c, credentials); }
    }

    [Fact]
    public async Task A_rejected_connect_leaves_no_socket_open_and_no_session_pinned()
    {
        var c = ScenarioCompiler.Compile(ScenarioCatalog.Get("skirmish").WithSeed(8));
        using var factory = new GameServerFactory();
        var (seeder, good) = Prepare(c);
        try
        {
            await Seed(seeder, c, good);
            // The recorder (index last) connects first and is fine; team 0's token is signed by another key.
            var sessions = good.Sessions.ToList();
            sessions[0] = new MintedSession(TestKeys.Mint(sessions[0].AccountId, signingKey: TestKeys.OtherKey), sessions[0].AccountId);
            var connector = new TrackingConnector(factory);
            await Assert.ThrowsAsync<InvalidOperationException>(() => SocketDriver.StartAsync(c, connector, new SocketCredentials(sessions), Ct));

            Assert.True(connector.Sockets.Count >= 2);
            Assert.All(connector.Sockets, s => Assert.NotEqual(WebSocketState.Open, s.State));
            var registry = factory.Services.GetRequiredService<SessionRegistry>();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (registry.Live.Count > 0 && DateTime.UtcNow < deadline) await Task.Delay(20);
            Assert.Empty(registry.Live);
        }
        finally { await Clean(seeder, c, good); }
    }

    /// <summary>The answers the first run gave after `target`, keyed from zero, so the redo takes the same path.</summary>
    static AnswerScript Shifted(RunLoop first, long target)
    {
        var questionOf = first.Log.Where(e => e.Payload is AskOpened).ToDictionary(e => ((AskOpened)e.Payload).Ask.Id, e => ((AskOpened)e.Payload).Ask.Question);
        var kept = first.Log.Where(e => e.Seq <= target && e.Payload is AskAnswered)
            .GroupBy(e => questionOf[((AskAnswered)e.Payload).AskId]).ToDictionary(g => g.Key, g => g.Count());
        var builder = AnswerScript.Build();
        foreach (var answered in first.Answers.History)
        {
            var skip = kept.GetValueOrDefault(answered.Question);
            if (answered.Ordinal >= skip) builder.At(answered.Question, answered.Ordinal - skip, answered.Answer);
        }
        return builder.Build();
    }
}
```

**Why the two drivers are compared run-for-run.** The test is a Theory over two seeds. It asserts equal `Outcome.Kind` (the winning team's id differs between two compiles of the same scenario) and equality of `Rounds`, `LastSeq`, `Frames`, `AsksAnswered`, the answer history, the dice and the payload types of the log. A quiet window could not guarantee that the two drivers saw the same Asks at the same step boundary, which would force the assertion down to a legitimate-end check; the rest frame does guarantee it, and this note records why the strict form is sound.

What the socket transport guarantees: every event the server emits reaches the recorder's socket, once, in `seq` order, and `DrainEventsAsync` sorts by `seq` and skips anything already applied, so the socket driver's projection is always a prefix of the same event sequence and never a different one. Per-viewer filtering does not weaken that, because it nulls `ask_answered.Answer` rather than withholding an event (contracts design → *Sequences*: "the filtering nulls a field rather than withholding an event"), so every stream carries every sequence number. That is what the `Frames == LastSeq` assertions check, and it is what a serialization, transport or persistence fault breaks.

What a quiet window cannot guarantee is *when* an event is observed. A server slower than the window hands the loop a shorter list of open Asks than the in-process driver sees, and two Asks of the same question open at once can then be paired with different `(question, ordinal)` keys, take different answers from the same seeded `Chooser`, and play a different game from there. `AtRest` closes exactly that gap. Both drivers return from `StepAsync` on a positive rest signal rather than on a guess: the marker at the back of the mailbox in process, the frame on the recorder's socket over the wire. Each step therefore sees every event of the batch and every Ask the batch opened, so the `(question, ordinal)` keys, the answers drawn from the `Chooser` and the sequence of commands are the same on both drivers by construction, for the same scenario and the same seed.

What remains transport-invariant rather than run-identical is stated separately and still holds: both runs pass, both satisfy every invariant (`RunWith` checks them on each driver), and `Frames == LastSeq` holds on each independently of the other. Those are the assertions that survive a driver disagreement and localise it, which is why they stay even though the run-for-run equalities subsume them on a passing run. The two tests below carry the rest of the localisation the spec asks for: the log lands in Postgres, and hydrating from it alone reproduces the projection. A socket run that now disagrees with the in-process run on outcome kind, rounds, `LastSeq`, `Frames` or `AsksAnswered` is a real fault in transport, serialization or persistence, which is what this test exists to say.

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
        try { await socket.ConnectAsync(uri, ct); }
        catch { socket.Dispose(); throw; }
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
    public const string ManagementUrlVariable = "TABLETOP_SIM_MANAGEMENT";
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

    /// <summary>
    /// The accounts a test minted for its tokens. Only for accounts this run inserted: a console run's
    /// minted accounts are stable by name across runs and are left alone.
    /// </summary>
    public async Task DeleteAccountsAsync(SocketCredentials credentials, CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        foreach (var session in credentials.Sessions)
            await Execute(connection, null, ct, "delete from management.account where id = $1", session.AccountId);
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
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Drivers;

/// <summary>
/// A real WebSocket to a running game server, a real session JWT, real rows and a real event log in Postgres
/// — the whole path architecture.md calls the only path (spec → Two drivers behind one interface).
/// </summary>
/// <remarks>
/// Every wait is on a log sequence number, never on a guess. An AtRest frame has no correlation id, but its
/// Seq is the log's high-water mark, and the server sends one after every command (broadcast when the batch
/// appended, to the sender alone when it did not) and one after every connect. So:
/// <list type="bullet">
/// <item>_knownHead is the high-water mark this driver has been told of. A submit's own AtRest is the first one
/// above it, or the one after its Refused; an AtRest at or below it is stale and is never accepted.</item>
/// <item>_restExpected is the mark the recorder must reach before a step may return. The recorder's AtRest
/// frames are recorded as they are read, wherever they are read (a drain included), so none is lost.</item>
/// </list>
/// An accepted command always appends at least one event (an override one, an answer two or more, an Undo one),
/// which is what makes "above the head" the test for "mine".
/// </remarks>
public sealed class SocketDriver : IGameDriver
{
    private sealed class Peer(Guid participantId, WebSocket socket)
    {
        public Guid ParticipantId { get; } = participantId;
        public WebSocket Socket { get; } = socket;
        public Channel<SocketMessage> Inbox { get; } = Channel.CreateUnbounded<SocketMessage>();
        public Task Reader { get; set; } = Task.CompletedTask;
        /// <summary>The first thing that broke the read loop (transport, framing, JSON), kept so a wait can report its cause.</summary>
        public Exception? Fault { get; set; }
    }

    private readonly CompiledScenario _scenario;
    private readonly IWebSocketConnector _connector;
    private readonly SocketCredentials _credentials;
    private readonly EventApplier _applier;
    private readonly List<Peer> _players = [];
    private readonly List<Peer> _all = [];                  // every socket ever opened, for disposal
    private bool _disposed;
    private readonly List<GameEvent> _incoming = [];        // read off the recorder's socket, not yet applied
    private readonly List<GameEvent> _handout = [];         // applied, not yet handed to a caller
    private readonly CancellationTokenSource _stopping = new();
    private Peer _recorder = null!;
    private long _recorderRest = -1;                        // the highest AtRest.Seq read off the recorder's socket
    private long _restExpected;                             // the recorder may not step until it has read this
    private long _knownHead;                                // the log's high-water mark as far as any socket has told us

    private SocketDriver(CompiledScenario scenario, IWebSocketConnector connector, SocketCredentials credentials)
    {
        _scenario = scenario;
        _connector = connector;
        _credentials = credentials;
        _applier = new EventApplier(scenario.Board, scenario.Info);
    }

    /// <summary>How long one wait for the server may take before it counts as hung. Not a quiescence guess.</summary>
    public TimeSpan RestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public static async Task<SocketDriver> StartAsync(CompiledScenario scenario, IWebSocketConnector connector,
        SocketCredentials credentials, CancellationToken ct)
    {
        var driver = new SocketDriver(scenario, connector, credentials);
        try { await driver.StartAsync(ct); }
        catch { await driver.DisposeAsync(); throw; }   // a rejected connect leaves no socket, reader or pinned session
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
        // The recorder connects first, so its stream carries the whole log from the start. Connect appends
        // nothing, so its AtRest goes to the connecting client alone: waiting for each socket's own says that
        // socket has the whole log, where draining whatever had arrived raced it.
        var recorderIndex = _scenario.Info.Participants.Count - 1;
        _recorder = await OpenAsync(_scenario.RecorderParticipantId, recorderIndex, ct);
        await AwaitConnectRestAsync(ct);
        for (var i = 0; i < _scenario.ParticipantIds.Count; i++)
        {
            var player = await OpenAsync(_scenario.ParticipantIds[i], i, ct);
            _players.Add(player);
            _knownHead = Math.Max(_knownHead, await ConnectRestAsync(player, ct));
        }
        Absorb();
    }

    private async Task<Peer> OpenAsync(Guid participantId, int credentialIndex, CancellationToken ct)
    {
        var socket = await _connector.ConnectAsync(ct);
        var peer = new Peer(participantId, socket);
        _all.Add(peer);
        peer.Reader = Task.Run(() => ReadLoopAsync(peer), CancellationToken.None);
        await SendAsync(peer, new Connect(_credentials.For(credentialIndex), GameId, ProtocolVersion.Current));
        var welcome = await ReadAsync(peer, ct);
        if (welcome is Rejected rejected) throw new InvalidOperationException($"the server rejected the connection: {rejected.Reason}");
        if (welcome is not Welcome w) throw new InvalidOperationException($"expected a welcome, got {welcome.GetType().Name}");
        if (w.ParticipantId != participantId)
            throw new InvalidOperationException($"the JWT at index {credentialIndex} named participant {w.ParticipantId:N}, not {participantId:N}");
        return peer;
    }

    /// <summary>Reads the recorder to its connect-time AtRest, keeping the log it delivers on the way.</summary>
    private async Task AwaitConnectRestAsync(CancellationToken ct)
    {
        _recorderRest = -1;
        while (_recorderRest < 0) Take(await ReadAsync(_recorder, ct));
        _restExpected = _recorderRest;
        _knownHead = Math.Max(_knownHead, _recorderRest);
    }

    /// <summary>A player's own connect AtRest; its events are the recorder's concern and are dropped.</summary>
    private async Task<long> ConnectRestAsync(Peer player, CancellationToken ct)
    {
        while (true)
            if (await ReadAsync(player, ct) is AtRest rest) return rest.Seq;
    }

    public Task<IReadOnlyList<OpenAsk>> OpenAsksAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<OpenAsk>>(_applier.Projection.OpenAsks.Values.ToList());

    public async Task<SubmitResult> SubmitAsync(Guid participantId, Command command, CancellationToken ct)
    {
        var peer = _players.FirstOrDefault(p => p.ParticipantId == participantId)
            ?? throw new ArgumentException($"no socket for participant {participantId}", nameof(participantId));
        var commandId = Guid.CreateVersion7();
        var head = _knownHead;
        await SendAsync(peer, new CommandMessage(commandId, command));

        // Only this command's own rest frame ends the wait. This socket may already hold AtRest frames from
        // the connect and from other participants' batches; every one of those is at or below the head.
        // Refusals come before that command's AtRest, so after a Refused the next AtRest is the one.
        Refused? refusal = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(RestTimeout);
        long restSeq;
        while (true)
        {
            var frame = await ReadAsync(peer, deadline.Token, ct);
            if (frame is Refused r && r.MessageId == commandId) refusal = r;
            else if (frame is AtRest rest && (refusal is not null || rest.Seq > head)) { restSeq = rest.Seq; break; }
        }

        // An accepted command appended, so the recorder has a batch and the same rest frame coming.
        _knownHead = Math.Max(_knownHead, restSeq);
        if (refusal is null) _restExpected = Math.Max(_restExpected, restSeq);
        Absorb();
        return refusal is null ? new SubmitResult.Accepted(null) : new SubmitResult.Rejected(refusal.Reason, refusal.Detail);
    }

    public Task<IReadOnlyList<GameEvent>> DrainEventsAsync(CancellationToken ct)
    {
        Absorb();
        return Task.FromResult(Handout());
    }

    public Task<Projection> ProjectionAsync(CancellationToken ct) => Task.FromResult(_applier.Projection);

    public async Task<StepResult> StepAsync(CancellationToken ct)
    {
        // Wait on the sequence the last accepted command rested at, not on a frame: a rest frame already read
        // by an earlier drain still counts, and a refused or empty command leaves nothing to wait for.
        Absorb();
        if (_recorderRest < _restExpected)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(RestTimeout);
            while (_recorderRest < _restExpected) Take(await ReadAsync(_recorder, deadline.Token, ct));
            Absorb();
        }
        // One connection delivers in order, so every EventsMessage of a batch preceded its AtRest. A mismatch
        // is a lost, reordered or duplicated frame, and it is a fault rather than a step.
        if (_applier.Projection.LastSeq != _recorderRest)
            throw new DriverFaultException(
                $"the server rested at seq {_recorderRest} but the recorder's stream reached {_applier.Projection.LastSeq}");
        var events = Handout();
        return new StepResult(events, _applier.Projection.OpenAsks.Values.ToList(), events.Count == 0);
    }

    /// <summary>Reconnect: the same participant, a second socket. The first is closed by the server with 1000.</summary>
    public async Task<WebSocketCloseStatus?> ReconnectRecorderAsync(CancellationToken ct)
    {
        Absorb();
        var old = _recorder;
        _incoming.Clear();
        _recorder = await OpenAsync(old.ParticipantId, _scenario.Info.Participants.Count - 1, ct);
        await AwaitConnectRestAsync(ct);
        // Its log arrives again from seq 1; what is already applied is skipped.
        Absorb();
        await Task.WhenAny(old.Reader, Task.Delay(TimeSpan.FromSeconds(5), ct));
        return old.Socket.CloseStatus;
    }

    /// <summary>
    /// Everything already on the recorder's socket goes to the projection, and every other socket's backlog
    /// is dropped. AtRest frames are recorded, not discarded, so a drain cannot cost a step its rest.
    /// </summary>
    private void Absorb()
    {
        while (_recorder.Inbox.Reader.TryRead(out var frame)) Take(frame);
        foreach (var player in _players) while (player.Inbox.Reader.TryRead(out _)) { }
        if (_recorder.Fault is { } fault) throw new RestTimeoutException(RestTimeout, _applier.Projection.LastSeq, fault);
        if (_incoming.Count == 0) return;
        var ordered = _incoming.OrderBy(e => e.Seq).ToList();
        _incoming.Clear();
        foreach (var e in ordered)
        {
            if (e.Seq <= _applier.Projection.LastSeq) continue;
            _applier.Apply(e);
            _handout.Add(e);
        }
    }

    /// <summary>A recorder frame: events are kept for Absorb, a rest frame raises the mark; anything else is of no use here.</summary>
    private void Take(SocketMessage frame)
    {
        switch (frame)
        {
            case EventsMessage message: _incoming.AddRange(message.Events); break;
            case AtRest rest: _recorderRest = Math.Max(_recorderRest, rest.Seq); break;
        }
    }

    private IReadOnlyList<GameEvent> Handout()
    {
        var taken = _handout.ToList();
        _handout.Clear();
        return taken;
    }

    private Task<SocketMessage> ReadAsync(Peer peer, CancellationToken ct) => ReadAsync(peer, ct, ct);

    /// <summary>One frame, bounded by RestTimeout (or by `deadline` when the caller holds one). A dead connection is a timeout too.</summary>
    private async Task<SocketMessage> ReadAsync(Peer peer, CancellationToken deadline, CancellationToken caller)
    {
        using var window = CancellationTokenSource.CreateLinkedTokenSource(deadline);
        if (deadline == caller) window.CancelAfter(RestTimeout);
        try { return await peer.Inbox.Reader.ReadAsync(window.Token); }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested)
        {
            throw new RestTimeoutException(RestTimeout, _applier.Projection.LastSeq, peer.Fault);
        }
        catch (ChannelClosedException)
        {
            // A reader fault is the cause; a plain close carries none.
            throw new RestTimeoutException(RestTimeout, _applier.Projection.LastSeq, peer.Fault);
        }
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
                    if (total == buffer.Length) throw new InvalidOperationException($"a frame larger than {buffer.Length} bytes");
                    result = await peer.Socket.ReceiveAsync(new Memory<byte>(buffer, total, buffer.Length - total), _stopping.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        // A server fault closes with InternalServerError and the reason; keep both for the wait that fails.
                        peer.Fault ??= new IOException($"the server closed the socket: {peer.Socket.CloseStatus} {peer.Socket.CloseStatusDescription}");
                        peer.Inbox.Writer.TryComplete();
                        return;
                    }
                    total += result.Count;
                } while (!result.EndOfMessage);
                peer.Inbox.Writer.TryWrite(WireJson.Deserialize(Encoding.UTF8.GetString(buffer, 0, total)));
            }
        }
        catch (OperationCanceledException) { peer.Inbox.Writer.TryComplete(); }
        catch (Exception ex)
        {
            peer.Fault = ex;
            peer.Inbox.Writer.TryComplete(ex);
        }
        finally { peer.Inbox.Writer.TryComplete(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        var peers = _all.ToList();
        // Close while the readers still run, so the server's close reply is read; its registry then drops the game.
        foreach (var peer in peers)
        {
            try
            {
                using var window = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await peer.Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "sim finished", window.Token);
            }
            catch (Exception) { }
        }
        await Task.WhenAny(Task.WhenAll(peers.Select(p => p.Reader)), Task.Delay(TimeSpan.FromSeconds(2)));
        await _stopping.CancelAsync();
        foreach (var peer in peers) peer.Socket.Dispose();
        _stopping.Dispose();
    }
}
```

Every wait in `SocketDriver` is on a log sequence number. `_knownHead` is the highest `AtRest.Seq` any socket has reported, and a submit accepts only the first `AtRest` above it, or the first after its own `Refused`, so a rest frame left over from the connect or from another participant's batch is never read as the answer to a command. `_restExpected` is the sequence the recorder must reach before `StepAsync` returns. The recorder's `AtRest` frames are recorded wherever they are read, drains included, so a drain cannot cost a step its rest. A slow server cannot read as a rested one, `StartAsync` and `ReconnectRecorderAsync` cannot race the connect-time log delivery, and a slow refusal cannot read as an acceptance.

The reader keeps the first fault that broke it, whether transport, framing or JSON, and `RestTimeoutException` carries it as its inner exception. A server close is a fault too, with its status and reason. A failed `StartAsync` disposes every socket it opened, and `DisposeAsync` closes them while the readers still run so the server's registry releases the session. A reader fault found while draining surfaces as `RestTimeoutException`, and a recorder stream that ends at a different sequence than the server rested at surfaces as `DriverFaultException`. The run loop reports both as `Failed`.

The in-process driver is the default, because it needs no Postgres, no server process and no Docker, and the socket one runs a smaller set of scenarios in CI.

**What the tests set up.** `GameServerFactory` runs in the `Test` environment, where the game server seeds no properties documents, so the tests seed the core `GrimdarkFuture` document through `PostgresPropertiesStore.SeedAsync` before `SeedAsync`. `sim/` never writes a `properties` row. A test deletes the game's rows and then the accounts it minted with `DeleteAccountsAsync`. The tests that read the registry wait up to ten seconds for `SessionRegistry.Live` to empty, because the registry releases a session after the last socket unwinds, which is asynchronous to the dispose.

**The undo test.** The test undoes to a target after deployment, taken as the sequence just before the middle dice-bearing step. There is no Redo command, so redoing is answering again: a second `RunLoop` over the same driver, with `RunSettings(CheckInvariants: false, Record: false)`, answers from a script built by `Shifted`. Its counters start at zero over a game now at the target, so `Shifted` drops the answers at or before the target from each question's ordinals. The test compares the dice after the target against the dice the second loop rolls.

**What Task 8 breaks without**, all of it already decided by the game server skeleton plan and consumed rather than requested here: an `AtRest` at the end of `ConnectAsync` at every status, an `AtRest` at the end of `HandleCommandAsync` whatever the outcome, a `Refused` before that command's `AtRest` on the sender's socket, and `Seq` as the log's high-water mark. A server that appends events and never sends the frame leaves the step waiting out `RestTimeout` and the run `Failed`, which is loud rather than silent and is why the wait is bounded at all.

- [ ] **Step 5: Run the tests; expect all green, then commit**

```bash
docker compose up -d
dotnet test tests/Tabletop.Sim.Tests --filter DriverAgreementTests
git add sim/Drivers sim/Run tests/Tabletop.Sim.Tests
git commit -m "Play the same scenario over a real socket and a real Postgres log"
```
