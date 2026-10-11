# Task 4b: Sight question and model facts over the socket

**Wave:** 3 · **Depends on:** 4a · **Model:** opus

**Files:**
- Create: `server/game/Session/GameSession.Facts.cs`
- Modify: `server/game/Session/GameSession.Frames.cs` (`SightQuery` arm in `ReceiveAsync`)
- Modify: `server/game/Session/GameSession.Connect.cs` (sends `ModelFacts` on connect)
- Modify: `server/game/Session/GameSession.cs` (`AppendAsync` broadcasts changed facts after a batch)
- Test: `tests/Tabletop.GameServer.Tests/SightAndFactsSessionTests.cs` (new)

**Interfaces:**
- Consumes: `SightQuery`, `SightAnswer`, `SightLine`, `ModelFacts` (Task 1); `Sight.Line`, `IGameRules.MaxWounds`,
  `IGameRules.Shooters(GameState, PropertySet, Guid, Guid)`, `IRulesEngine.Properties`, `FakeRules.MaxWoundsHook`,
  `SightLineTests.Table` (Task 4a).
- Produces: the socket behaviour below; no new C# member other tasks call.

Spec: *Server and protocol changes → Sight question*, *Model facts*.

Session behaviour:
- `SightQuery` → one `SightAnswer` to the asker alone; nothing appended, nothing broadcast, no `AtRest`. Any
  participant may ask. Before playing (`Play` null), or when either unit is unknown or has no model on the table, both
  lists are empty.
- `ModelFacts` for every model in `Play.Projection.Models` to the connecting client, after the log and before
  `AtRest`, whenever `Play` is not null.
- After each appended batch, `ModelFacts` to every client when any value differs from the last facts sent (none sent
  yet counts as differing), after the batch's `EventsMessage`.

- [ ] **Step 1: Write the failing session tests**

Create `tests/Tabletop.GameServer.Tests/SightAndFactsSessionTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Tabletop.GameServer.Configuration;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Engine;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Log;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Session;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>The sight question and model facts over the session (in-game UX spec → Sight question, Model facts).</summary>
public class SightAndFactsSessionTests
{
    static readonly GameServerOptions Options = new() { DbConnection = "", StytchProjectId = "p", StytchJwksUrl = "u" };

    sealed class Engines(IGameRules rules) : IRulesEngineFactory
    {
        public IRulesEngine Create(IGameRules _, GameInfo game) => new RulesEngine(rules, TestProperties.Core, new ScriptedDice(4), new NoScheduler());
    }

    /// <summary>SightLineTests.Table's two units, playing, observers armed with a 12" rifle; the log holds the deployment event.</summary>
    static async Task<(GameSession Session, InMemoryEventLog Log, GameState State, Tabletop.Content.Unit Observers, Tabletop.Content.Unit Targets)> PlayingAsync(double wallX = 0, IGameRules? rules = null)
    {
        rules ??= GrimdarkFutureRules.Instance;
        var (table, observers, targets, _) = SightLineTests.Table(wallX, Gun("Rifle", "12\"", 1));
        var state = new GameState(table.Info, table.Board, table.Applier, rules);
        var log = new InMemoryEventLog();
        await log.AppendAsync(state.Info.Id, state.Applier.Log, CancellationToken.None);
        var services = new SessionServices(null!, log, new Engines(rules), Options, NullLoggerFactory.Instance);
        var session = new GameSession(state.Info.Id, state.Info, rules, null, state, services.Engines.Create(rules, state.Info), services);
        await session.StartAsync();
        return (session, log, state, observers, targets);
    }

    [Fact]
    public async Task A_sight_query_is_answered_to_the_asker_alone_and_logs_nothing()
    {
        var (session, log, _, observers, targets) = await PlayingAsync();
        var (clay, opponent) = (new RecordingClient(Samples.P1), new RecordingClient(P2));
        await session.AttachAsync(clay); await session.AttachAsync(opponent);
        var id = Guid.CreateVersion7();
        await session.ReceiveAsync(clay, new SightQuery(id, observers.Id, targets.Id));
        Assert.Equal(id, Assert.IsType<SightAnswer>(Assert.Single(clay.Received)).QueryId);       // no AtRest
        Assert.Empty(opponent.Received);
        Assert.Single(await log.ReadAsync(session.GameId, 0, CancellationToken.None));
    }

    [Fact]
    public async Task The_answer_is_the_rules_sight_and_who_can_shoot()
    {
        var (session, _, state, observers, targets) = await PlayingAsync(wallX: -3.5);
        var clay = new RecordingClient(Samples.P1);
        await session.AttachAsync(clay);
        await session.ReceiveAsync(clay, new SightQuery(Guid.CreateVersion7(), observers.Id, targets.Id));
        var answer = Assert.IsType<SightAnswer>(Assert.Single(clay.Received));
        var sight = Sight.From(state, observers.Id);
        var expected = observers.Models.SelectMany(f => targets.Models.Select(t => sight.Line(f.Id, t.Id)!)).ToList();
        Assert.Equal(expected.Select(l => (l.FromModelId, l.ToModelId, l.Clear, l.Screened)), answer.Lines.Select(l => (l.FromModelId, l.ToModelId, l.Clear, l.Screened)));
        Assert.Equal([observers.Models[1].Id], answer.CanShoot);
    }

    [Fact]
    public async Task A_unit_unknown_or_off_the_table_gives_empty_lists()
    {
        var (session, _, state, observers, targets) = await PlayingAsync();
        var clay = new RecordingClient(Samples.P1);
        await session.AttachAsync(clay);
        await session.ReceiveAsync(clay, new SightQuery(Guid.CreateVersion7(), observers.Id, Guid.CreateVersion7()));
        var unknown = Assert.IsType<SightAnswer>(Assert.Single(clay.Received));
        Assert.Empty(unknown.Lines); Assert.Empty(unknown.CanShoot);

        await session.ReceiveAsync(clay, new CommandMessage(Guid.CreateVersion7(), new MoveModels(targets.Models.Select(m =>
            new ModelRelocation(m.Id, state.Projection.Models[m.Id].Location, new OnShelf())).ToList())));
        clay.Received.Clear();
        await session.ReceiveAsync(clay, new SightQuery(Guid.CreateVersion7(), observers.Id, targets.Id));
        var shelved = Assert.IsType<SightAnswer>(Assert.Single(clay.Received));
        Assert.Empty(shelved.Lines); Assert.Empty(shelved.CanShoot);
    }

    [Fact]
    public async Task Before_play_a_sight_query_gives_empty_lists()
    {
        var log = new InMemoryEventLog();
        var info = Samples.Game with { Status = GameStatus.Lobby };
        var services = new SessionServices(null!, log, new InertEngineFactory(), Options, NullLoggerFactory.Instance);
        var session = new GameSession(info.Id, info, GrimdarkFutureRules.Instance, null, null, null, services);
        await session.StartAsync();
        var clay = new RecordingClient(Samples.P1);
        await session.AttachAsync(clay);
        await session.ReceiveAsync(clay, new SightQuery(Guid.CreateVersion7(), Samples.Unit, Samples.Unit));
        var answer = Assert.IsType<SightAnswer>(Assert.Single(clay.Received));
        Assert.Empty(answer.Lines); Assert.Empty(answer.CanShoot);
    }

    [Fact]
    public async Task A_connecting_client_is_told_every_models_max_wounds_before_rest()
    {
        var (session, _, state, observers, targets) = await PlayingAsync();
        var clay = new RecordingClient(Samples.P1);
        await session.ConnectAsync(clay);
        var kinds = clay.Received.Select(m => m.GetType()).ToList();
        Assert.Equal([typeof(Welcome), typeof(BoardMessage), typeof(EventsMessage), typeof(ModelFacts), typeof(AtRest)], kinds);
        var facts = clay.Received.OfType<ModelFacts>().Single().MaxWounds;
        Assert.Equal(state.Projection.Models.Keys.Order(), facts.Keys.Order());
        Assert.All(targets.Models, m => Assert.Equal(3, facts[m.Id]));
        Assert.All(observers.Models, m => Assert.Equal(1, facts[m.Id]));
    }

    [Fact]
    public async Task Facts_are_broadcast_after_a_batch_only_when_they_change()
    {
        var rules = FakeRules.Gf();
        rules.MaxWoundsHook = (s, m) => 1 + s.Projection.Models[m].Wounds;                      // a fact that moves with the log
        var (session, _, _, _, targets) = await PlayingAsync(rules: rules);
        var (clay, opponent) = (new RecordingClient(Samples.P1), new RecordingClient(P2));
        await session.ConnectAsync(clay); await session.ConnectAsync(opponent);
        clay.Received.Clear(); opponent.Received.Clear();

        await session.ReceiveAsync(clay, new CommandMessage(Guid.CreateVersion7(), new SetScore(Samples.Team1, 2)));
        Assert.DoesNotContain(clay.Received, m => m is ModelFacts);
        Assert.DoesNotContain(opponent.Received, m => m is ModelFacts);

        clay.Received.Clear(); opponent.Received.Clear();
        var hurt = targets.Models[0].Id;
        await session.ReceiveAsync(clay, new CommandMessage(Guid.CreateVersion7(), new SetWounds(hurt, 2)));
        foreach (var c in new[] { clay, opponent })
        {
            Assert.Equal([typeof(EventsMessage), typeof(ModelFacts), typeof(AtRest)], c.Received.Select(m => m.GetType()));
            Assert.Equal(3, c.Received.OfType<ModelFacts>().Single().MaxWounds[hurt]);
        }
    }
}
```

`SightLineTests.Table` returns `Armies.State`'s game: participants `Samples.P1` (team 1) and `Armies.P2` (team 2),
status `Playing`, enforcement `Warn`, so the shelving `MoveModels` in the third test is accepted with or without
Task 2.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter SightAndFactsSessionTests`
Expected: FAIL. A `SightQuery` closes the client as "not a client frame" with nothing received; no `ModelFacts`
arrives on connect or after a batch.

- [ ] **Step 3: Implement the session side**

Create `server/game/Session/GameSession.Facts.cs`:

```csharp
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Session;

/// <summary>Answers outside the log (in-game UX spec → Sight question, Model facts): nothing here appends, and a sight answer produces no AtRest.</summary>
public sealed partial class GameSession
{
    /// <summary>The last ModelFacts sent; null before any. Facts change only through a batch, and every batch compares against this.</summary>
    private IReadOnlyDictionary<Guid, int>? _factsSent;

    /// <summary>One line per observer model and target model on the table, in unit order, and who may shoot — to the asker alone.</summary>
    private async Task HandleSightAsync(ISessionClient from, SightQuery query)
    {
        IReadOnlyList<SightLine> lines = []; IReadOnlyList<Guid> canShoot = [];
        if (Play is { } state && state.Projection.Units.TryGetValue(query.ObserverUnitId, out var observer) && state.Projection.Units.TryGetValue(query.TargetUnitId, out var target))
        {
            var (watching, watched) = (OnTable(state, observer), OnTable(state, target));
            if (watching.Count > 0 && watched.Count > 0)
            {
                var sight = Sight.From(state, observer.Id);
                lines = watching.SelectMany(f => watched.Select(t => sight.Line(f, t))).OfType<SightLine>().ToList();
                canShoot = Engine?.Properties is { } properties ? state.Rules.Shooters(state, properties, observer.Id, target.Id) : [];
            }
        }
        await from.SendAsync(new SightAnswer(query.QueryId, lines, canShoot));
    }

    static List<Guid> OnTable(GameState state, UnitInPlay unit) =>
        unit.Definition.Models.Select(m => m.Id).Where(id => state.Projection.Models[id].Location is OnTable).ToList();

    ModelFacts Facts(GameState state) => new(state.Projection.Models.Keys.ToDictionary(id => id, id => state.Rules.MaxWounds(state, id)));

    /// <summary>On connect, once playing: every model's facts, after the log.</summary>
    private async Task SendFactsAsync(ISessionClient client)
    {
        if (Play is null) return;
        var facts = Facts(Play);
        _factsSent = facts.MaxWounds;
        await client.SendAsync(facts);
    }

    /// <summary>After a batch: to everyone, when any value differs from the last sent.</summary>
    private async Task BroadcastFactsIfChangedAsync()
    {
        if (Play is null) return;
        var facts = Facts(Play);
        if (_factsSent is { } sent && sent.Count == facts.MaxWounds.Count && facts.MaxWounds.All(kv => sent.TryGetValue(kv.Key, out var v) && v == kv.Value)) return;
        _factsSent = facts.MaxWounds;
        await BroadcastAsync(facts);
    }
}
```

In `server/game/Session/GameSession.Frames.cs`, add the arm to `ReceiveAsync`'s switch, after `EditMessage`:

```csharp
        SightQuery q => HandleSightAsync(from, q),
```

In `server/game/Session/GameSession.Connect.cs`, inside `if (Play is not null)`, after
`if (log.Count == 0) await client.SendAsync(new EventsMessage([]));`:

```csharp
            await SendFactsAsync(client);
```

In `server/game/Session/GameSession.cs`, in `AppendAsync`, after the line that sends each client its
`EventsMessage`:

```csharp
        await BroadcastFactsIfChangedAsync();
```

so the facts follow the batch they changed with, before `MarkEndedAsync` and before the caller's `AtRest`.

- [ ] **Step 4: Run them to see them pass, then the whole suite**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "SightAndFactsSessionTests|ModelFactsTests|SightLineTests|GameSessionTests"`
Expected: PASS.

Run: `docker compose up -d && dotnet test tests/Tabletop.GameServer.Tests && dotnet test tests/Tabletop.Sim.Tests`
Expected: PASS. `SocketTests`' silence checks hold: facts go out on connect and with a batch, never on an idle
socket. The sim's `SocketDriver.Take` ignores frames it does not use, and the client's `Session.Handle` has no
default arm.

- [ ] **Step 5: Commit**

```bash
git add server/game/Session/GameSession.Facts.cs server/game/Session/GameSession.Frames.cs server/game/Session/GameSession.Connect.cs \
  server/game/Session/GameSession.cs tests/Tabletop.GameServer.Tests/SightAndFactsSessionTests.cs
git commit -m "Answer the sight question to the asker and send model facts on connect and when a batch changes them" \
  -m "Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```
