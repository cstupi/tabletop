using Tabletop.Content;
using System.Net.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Tabletop.GameServer.Session;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;
using Tabletop.Sim.Tests.Infrastructure;

namespace Tabletop.Sim.Tests;

/// <summary>Each scenario on the in-process driver and over the socket against GameServerFactory's in-memory stores.</summary>
public class DriverAgreementTests
{
    readonly InMemorySeeder db = new();
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
    /// GameServerFactory runs in the Test environment, where Program does not seed the embedded documents, so each edition's core set goes
    /// into the factory's properties store here. sim/ never writes a properties row.
    /// </summary>
    (ISocketRows Seeder, SocketCredentials Credentials) Prepare(CompiledScenario c)
    {
        foreach (var rules in RuleSets.All) db.Properties.Add(ScenarioCompiler.CorePropertiesOf(rules));
        return (new InMemorySocketRows(db), Credentials(c));
    }

    static Task Seed(ISocketRows seeder, CompiledScenario c, SocketCredentials credentials) => seeder.SeedAsync(c, credentials, Ct);

    static Task Clean(ISocketRows seeder, CompiledScenario c, SocketCredentials credentials) => seeder.DeleteAsync(c.Info.Id, Ct);

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
    [InlineData("skirmish", 21UL)]
    [InlineData("skirmish", 24UL)]
    [InlineData("casting", 3UL)]
    [InlineData("army-rules", 5UL)]
    public async Task The_same_scenario_plays_the_same_game_on_both_drivers(string name, ulong seed)
    {
        var scenario = ScenarioCatalog.Get(name).WithSeed(seed);
        var inProcessScenario = ScenarioCompiler.Compile(scenario);

        RunReport inProcess;
        RunLoop inProcessLoop;
        await using (var driver = await InProcessDriver.StartAsync(inProcessScenario, Ct))
            (inProcess, inProcessLoop) = await RunWith(driver, inProcessScenario, seed);

        // A second compile: the ids differ, the seed does not.
        var c = ScenarioCompiler.Compile(scenario);
        using var factory = new GameServerFactory(db);
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
            Assert.Contains(overSocket.Outcome.Kind, (string[])["ended", "round_limit"]);
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
    public async Task The_log_lands_in_the_store_and_hydration_reproduces_the_game()
    {
        const ulong seed = 22;
        var c = ScenarioCompiler.Compile(ScenarioCatalog.Get("stalemate").WithSeed(seed));
        using var factory = new GameServerFactory(db);
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
            Assert.Equal(lastSeq, await db.Log.LastSeqAsync(c.Info.Id, Ct));

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
        using var factory = new GameServerFactory(db);
        var (seeder, credentials) = Prepare(c);
        try
        {
            await Seed(seeder, c, credentials);
            IGameDriver driver = await SocketDriver.StartAsync(c, new FactoryConnector(factory), credentials, Ct);
            await using var _ = driver;
            var deploy = (await driver.OpenAsksAsync(Ct)).Single().Ask;                  // the roll-off ran through: deployment's first Ask
            Assert.IsType<SubmitResult.Accepted>(await driver.AnswerAsync(deploy.Teams[0], deploy.Id, new SelectedPieces([((PieceSelection)deploy.Legal).Eligible[0]]), Ct));

            var stale = await driver.AnswerAsync(c.TeamIds[1], Guid.NewGuid(), new Confirmed(), Ct);
            Assert.IsType<SubmitResult.Rejected>(stale);

            var step = await driver.StepAsync(Ct);                                   // the accepted answer's events, once
            Assert.Contains(step.Events, e => e.Payload is AskAnswered);
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
        using var factory = new GameServerFactory(db);
        var (seeder, credentials) = Prepare(c);
        try
        {
            await Seed(seeder, c, credentials);
            IGameDriver driver = await SocketDriver.StartAsync(c, new FactoryConnector(factory), credentials, Ct);
            await using var _ = driver;
            var seen = new List<GameEvent>();
            var deploy = (await driver.OpenAsksAsync(Ct)).Single().Ask;                  // the roll-off ran through: deployment's first Ask
            await driver.AnswerAsync(deploy.Teams[0], deploy.Id, new SelectedPieces([((PieceSelection)deploy.Legal).Eligible[0]]), Ct);
            seen.AddRange(await driver.DrainEventsAsync(Ct));
            seen.AddRange((await driver.StepAsync(Ct)).Events);
            Assert.NotEmpty(seen);
            Assert.Equal(seen.Select(e => e.Seq), seen.Select(e => e.Seq).Distinct());
            Assert.Equal((await driver.ProjectionAsync(Ct)).LastSeq, seen.Max(e => e.Seq));
        }
        finally { await Clean(seeder, c, credentials); }
    }

    /// <summary>The first socket a driver opens is its recorder; this one reads every frame late, as a loaded server's recorder socket may.</summary>
    sealed class SlowRecorderConnector(GameServerFactory factory) : IWebSocketConnector
    {
        int opened;
        public async Task<WebSocket> ConnectAsync(CancellationToken ct)
        {
            var socket = await factory.Server.CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/ws"), ct);
            return Interlocked.Increment(ref opened) == 1 ? new SlowReceiveSocket(socket, TimeSpan.FromMilliseconds(300)) : socket;
        }
    }

    sealed class SlowReceiveSocket(WebSocket inner, TimeSpan delay) : WebSocket
    {
        public override WebSocketCloseStatus? CloseStatus => inner.CloseStatus;
        public override string? CloseStatusDescription => inner.CloseStatusDescription;
        public override WebSocketState State => inner.State;
        public override string? SubProtocol => inner.SubProtocol;
        public override void Abort() => inner.Abort();
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) => inner.CloseAsync(status, description, ct);
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) => inner.CloseOutputAsync(status, description, ct);
        public override void Dispose() => inner.Dispose();
        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct)
        {
            var result = await inner.ReceiveAsync(buffer, ct);
            await Task.Delay(delay, ct);
            return result;
        }
        public override async ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken ct)
        {
            var result = await inner.ReceiveAsync(buffer, ct);
            await Task.Delay(delay, ct);
            return result;
        }
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool endOfMessage, CancellationToken ct) => inner.SendAsync(buffer, type, endOfMessage, ct);
        public override ValueTask SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType type, WebSocketMessageFlags flags, CancellationToken ct) => inner.SendAsync(buffer, type, flags, ct);
    }

    /// <summary>
    /// RunLoop reads the projection right after an answer (PlaceAsync places a unit the answer took off the table, Re-Deployment), so an accepted
    /// submit returns only once the recorder, which feeds the projection, holds the command's events, however late its socket reads.
    /// </summary>
    [Fact]
    public async Task An_accepted_submit_returns_with_its_events_in_the_projection_when_the_recorder_reads_late()
    {
        var c = ScenarioCompiler.Compile(ScenarioCatalog.Get("skirmish").WithSeed(6));
        using var factory = new GameServerFactory(db);
        var (seeder, credentials) = Prepare(c);
        try
        {
            await Seed(seeder, c, credentials);
            IGameDriver driver = await SocketDriver.StartAsync(c, new SlowRecorderConnector(factory), credentials, Ct);
            await using var _ = driver;
            var deploy = (await driver.OpenAsksAsync(Ct)).Single().Ask;
            Assert.IsType<SubmitResult.Accepted>(await driver.AnswerAsync(deploy.Teams[0], deploy.Id, new SelectedPieces([((PieceSelection)deploy.Legal).Eligible[0]]), Ct));

            var drained = await driver.DrainEventsAsync(Ct);
            Assert.Contains(drained, e => e.Payload is AskAnswered answered && answered.AskId == deploy.Id);
            Assert.DoesNotContain(deploy.Id, (await driver.ProjectionAsync(Ct)).OpenAsks.Keys);
        }
        finally { await Clean(seeder, c, credentials); }
    }

    [Fact]
    public async Task Undoing_and_redoing_over_the_socket_draws_fresh_dice()
    {
        // A skirmish whose units come within range and roll in play (most of its seeds end with no attack at all).
        const ulong seed = 20;
        var c = ScenarioCompiler.Compile(ScenarioCatalog.Get("skirmish").WithSeed(seed));
        using var factory = new GameServerFactory(db);
        var (seeder, credentials) = Prepare(c);
        try
        {
            await Seed(seeder, c, credentials);
            await using var driver = await SocketDriver.StartAsync(c, new FactoryConnector(factory), credentials, Ct);
            var loop = LoopFor(driver, c, seed);
            await loop.RunAsync(Ct);

            // A target after deployment: the first dice-bearing step is a play step, and the event before it is live.
            var diceSteps = loop.Log.Where(e => e.Payload is StepCompleted { Rolls.Count: > 0 }).ToList();
            Assert.True(diceSteps.Count > 1, "the game rolls in play, not only for the roll-off");
            var target = diceSteps[diceSteps.Count / 2].Seq - 1;
            var before = loop.Log.Where(e => e.Seq > target).ToList();

            Assert.IsType<SubmitResult.Accepted>(
                await driver.SubmitAsync(driver.ParticipantFor(driver.TeamIds[0]), new Undo(target), Ct));

            // There is no Redo command: redoing is answering again. The new loop's counters start at zero over a
            // game now at `target`, so each question's ordinals shift by the answers already in the kept prefix.
            var replay = LoopFor(driver, c, seed, Shifted(loop, target), new RunSettings(CheckInvariants: false, Record: false));
            var report = await replay.RunAsync(Ct);
            Assert.True(report.Outcome is RunOutcome.Ended or RunOutcome.RoundLimit, report.Outcome.ToString());

            var original = Dice(before).ToList();
            Assert.NotEmpty(original);

            // Dice stay spent after an undo: the redo continues the stream where the undone game left it.
            var drawn = loop.Log.Sum(e => e.Payload switch
            {
                StepCompleted s => s.Rolls.Sum(r => (long)r.Dice.Count),
                DiceRerolled d => d.Replaced.Count,
                _ => 0L,
            });
            var lastBefore = loop.Log[^1].Seq;
            var redone = Dice(replay.Log.Where(e => e.Seq > lastBefore)).ToList();
            Assert.NotEmpty(redone);
            Assert.Equal(new Tabletop.GameServer.Dice.DiceSource().Draw(seed, drawn, 6), redone[0]);
            Assert.NotEqual(original, redone);
        }
        finally { await Clean(seeder, c, credentials); }
    }

    [Fact]
    public async Task Each_socket_reauths_before_its_session_expires_and_plays_on_past_it()
    {
        var c = ScenarioCompiler.Compile(ScenarioCatalog.Get("skirmish").WithSeed(6));
        using var factory = new GameServerFactory(db);
        var (seeder, _) = Prepare(c);
        var refreshed = new List<int>();
        var shortLived = new SocketCredentials(c.Info.Participants.Select(p => new MintedSession(TestKeys.Mint(p.AccountId, lifetime: TimeSpan.FromSeconds(3)), p.AccountId)).ToList())
        {
            Refresh = (i, _) => { lock (refreshed) refreshed.Add(i); var a = c.Info.Participants[i].AccountId; return Task.FromResult(new MintedSession(TestKeys.Mint(a), a)); },
        };
        try
        {
            await Seed(seeder, c, shortLived);
            IGameDriver driver = await SocketDriver.StartAsync(c, new FactoryConnector(factory), shortLived, Ct, renewBefore: TimeSpan.FromSeconds(2));
            await using var _ = driver;
            await Task.Delay(TimeSpan.FromSeconds(4));                          // past the first tokens' expiry
            var deploy = (await driver.OpenAsksAsync(Ct)).Single().Ask;                  // the roll-off ran through: deployment's first Ask
            Assert.IsType<SubmitResult.Accepted>(await driver.AnswerAsync(deploy.Teams[0], deploy.Id, new SelectedPieces([((PieceSelection)deploy.Legal).Eligible[0]]), Ct));
            Assert.NotEmpty((await driver.StepAsync(Ct)).Events);
            lock (refreshed) Assert.Equal(Enumerable.Range(0, c.Info.Participants.Count), refreshed.Order());
        }
        finally { await Clean(seeder, c, shortLived); }
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
        using var factory = new GameServerFactory(db);
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
        using var factory = new GameServerFactory(db);
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
