# Task 6: The row, the broadcast and hydration

**Files:**
- Modify: `server/game/Persistence/IGameRows.cs`, `server/game/Persistence/PostgresGameRows.Writes.cs`, `server/game/Session/GameSession.cs`, `sim/Drivers/ScenarioRows.cs`
- Test: `tests/Tabletop.GameServer.Tests/GameEndTests.cs` (new, `[Collection("Postgres")]`), `tests/Tabletop.GameServer.Tests/GameSessionTests.cs`

**Interfaces:**
- Consumes: `Projection.Ended`, `GameEnded` (Task 1); `Emit` (Task 3); `PostgresFixture.SeedGameAsync`, `SeedTableStateAsync`, `Db`; `Hydrator.HydrateAsync`; `RecordingClient`; `FixedAskEngineFactory`.
- Produces: `Task IGameRows.EndGameAsync(Guid gameId, CancellationToken ct)`; `GameSession` writes the row once when `Projection.Ended` goes from unset to set in an appended batch while `Info.Status` is `Playing`, then broadcasts `GameInfoMessage`; `GameSession.StartAsync` decides from the log: ended log over a `playing` row → write the row, no advance; ended log over an `ended` row → nothing; no end in the log → advance, whatever the row says.

Spec: enforcement spec → *The row and the broadcast*, *Hydration*, *After an undo*. This is the one row the game server writes from `playing` on:

```sql
update management.game set status = 'ended' where id = $1 and status = 'playing'
```

- [ ] **Step 1: Write the failing Postgres tests**

Create `tests/Tabletop.GameServer.Tests/GameEndTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Tabletop.GameServer.Configuration;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Log;
using Tabletop.GameServer.Persistence;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Session;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

/// <summary>Ends the game once the log holds a score: a stand-in for a strict breach, so the session's row path is tested alone.</summary>
public sealed class EndsOnScoreEngine : IRulesEngine
{
    public Legality Judge(GameState state, Command command, Actor actor) => Legality.Legal;
    public EngineResult Execute(GameState state, Command command, Actor actor) => new RefuseCommand(RefusalReason.Unknown, "test engine");
    public Emit Advance(GameState state) =>
        state.Projection.Ended is null && state.Applier.Log.Any(e => e.Payload is ScoreSet) ? new Emit([new GameEnded("over", [])]) : Emit.None;
}

public sealed class EndsOnScoreEngineFactory : IRulesEngineFactory
{
    public IRulesEngine Create(IGameRules rules, GameInfo game) => new EndsOnScoreEngine();
}

[Collection("Postgres")]
public class GameEndTests(PostgresFixture pg)
{
    static readonly GameServerOptions Options = new() { DbConnection = "", StytchProjectId = "p", StytchJwksUrl = "u" };
    readonly IGameRows _rows = new PostgresGameRows(pg.Db);

    async Task<(GameSession Session, InMemoryEventLog Log, RecordingClient Clay, SeededGame Seeded)> Hydrated(
        IRulesEngineFactory engines, GameStatus status = GameStatus.Playing, params EventPayload[] history)
    {
        var seeded = await pg.SeedGameAsync(status);
        await pg.SeedTableStateAsync(seeded.GameId);
        var log = new InMemoryEventLog();
        if (history.Length > 0)
            await log.AppendAsync(seeded.GameId, history.Select((p, i) => new GameEvent(i + 1, null, p, null)).ToList(), CancellationToken.None);
        var services = new SessionServices(_rows, log, engines, Options, NullLoggerFactory.Instance);
        var session = await Hydrator.HydrateAsync(seeded.GameId, services, CancellationToken.None);
        await session.StartAsync();
        var clay = new RecordingClient(seeded.Participants[0].ParticipantId);
        await session.AttachAsync(clay);
        return (session, log, clay, seeded);
    }

    async Task<GameStatus> StatusOf(Guid gameId) => (await _rows.ReadGameAsync(gameId, CancellationToken.None))!.Status;

    [Fact]
    public async Task The_end_marks_the_row_and_everyone_hears_it_before_rest()
    {
        var (session, log, clay, seeded) = await Hydrated(new EndsOnScoreEngineFactory());
        await session.ReceiveAsync(clay, new CommandMessage(Guid.CreateVersion7(), new SetScore(seeded.TeamIds[0], 1)));
        Assert.Equal(GameStatus.Ended, await StatusOf(seeded.GameId));
        Assert.Equal(GameStatus.Ended, session.Info.Status);
        Assert.IsType<GameEnded>((await log.ReadAsync(seeded.GameId, 0, CancellationToken.None))[^1].Payload);
        var info = clay.Received.FindIndex(m => m is GameInfoMessage g && g.Game.Status == GameStatus.Ended);
        var rest = clay.Received.FindLastIndex(m => m is AtRest);
        Assert.True(info >= 0 && info < rest, $"GameInfoMessage at {info}, at_rest at {rest}");
    }

    [Fact]
    public async Task Only_a_playing_game_is_marked_ended()
    {
        var seeded = await pg.SeedGameAsync(GameStatus.Lobby);
        await _rows.EndGameAsync(seeded.GameId, CancellationToken.None);
        Assert.Equal(GameStatus.Lobby, await StatusOf(seeded.GameId));
        var playing = await pg.SeedGameAsync(GameStatus.Playing);
        await _rows.EndGameAsync(playing.GameId, CancellationToken.None);
        await _rows.EndGameAsync(playing.GameId, CancellationToken.None);                       // a second writer of the same value changes nothing
        Assert.Equal(GameStatus.Ended, await StatusOf(playing.GameId));
    }

    [Fact]
    public async Task A_crash_between_the_append_and_the_row_is_reconciled_on_hydration()
    {
        var (_, log, _, seeded) = await Hydrated(new EndsOnScoreEngineFactory(), GameStatus.Playing, new ScoreSet(Guid.CreateVersion7(), 1), new GameEnded("over", []));
        Assert.Equal(GameStatus.Ended, await StatusOf(seeded.GameId));
        Assert.Equal(2, (await log.ReadAsync(seeded.GameId, 0, CancellationToken.None)).Count);   // reconciled, not advanced
    }

    [Fact]
    public async Task An_undone_end_still_advances_after_a_restart()
    {
        var ask = new Ask(Guid.CreateVersion7(), AskQuestions.EndRound, "End the round", [Guid.CreateVersion7()], new Confirmation(), false, false, null, null);
        var (_, log, _, seeded) = await Hydrated(new FixedAskEngineFactory(ask), GameStatus.Ended,
            new ScoreSet(Guid.CreateVersion7(), 1), new GameEnded("over", []), new Undone(1));
        var events = await log.ReadAsync(seeded.GameId, 0, CancellationToken.None);
        Assert.IsType<AskOpened>(events[^1].Payload);                                          // the engine caught up although the row says ended
        Assert.Equal(GameStatus.Ended, await StatusOf(seeded.GameId));                          // and the row stays ended (manager ruling)
    }
}
```

`ScoreSet` names a team id the projection has no score for; the applier assigns it (an assignment cannot miss), so the history applies cleanly.

- [ ] **Step 2: Rewrite the one session test the rule changes, and add its pair**

In `tests/Tabletop.GameServer.Tests/GameSessionTests.cs`, replace `An_ended_game_with_a_draft_hydrates_without_the_engine_appending_anything` with:

```csharp
    [Fact]
    public async Task An_ended_row_over_a_log_with_no_end_catches_up_on_hydration()
    {
        var log = new InMemoryEventLog();
        var session = await Hydrate(Info with { Status = GameStatus.Ended }, log, Services(log, new FixedAskEngineFactory(SimultaneousSpend)));
        Assert.False(session.Faulted);
        Assert.IsType<AskOpened>(Assert.Single(await log.ReadAsync(session.GameId, 0, CancellationToken.None)).Payload);   // ended is a flag; the log decides
    }

    [Fact]
    public async Task An_ended_row_over_an_ended_log_hydrates_without_the_engine_appending_anything()
    {
        var log = new InMemoryEventLog();
        await log.AppendAsync(Info.Id, [new GameEvent(1, null, new GameEnded("over", []), null)], CancellationToken.None);
        var session = await Hydrate(Info with { Status = GameStatus.Ended }, log, Services(log, new FixedAskEngineFactory(SimultaneousSpend)));
        Assert.False(session.Faulted);
        Assert.Single(await log.ReadAsync(session.GameId, 0, CancellationToken.None));
    }
```

- [ ] **Step 3: Run them to see them fail**

Run: `docker compose up -d && dotnet test tests/Tabletop.GameServer.Tests --filter "GameEndTests|An_ended_row"`
Expected: compile error, `IGameRows` has no `EndGameAsync`; after Step 4 alone, the session tests fail on behaviour.

- [ ] **Step 4: The row write**

`server/game/Persistence/IGameRows.cs`: change the interface's summary to "Reads at hydration and end-setup; writes only while the game is lobby or setup, and `game.status` once more: `playing → ended` when this server's own log ends the game (enforcement spec → The row and the broadcast)." and add:

```csharp
    /// <summary>playing → ended, guarded on playing, so management's own end and a second call change nothing.</summary>
    Task EndGameAsync(Guid gameId, CancellationToken ct);
```

`server/game/Persistence/PostgresGameRows.Writes.cs`:

```csharp
    public async Task EndGameAsync(Guid gameId, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("update management.game set status = 'ended' where id = $1 and status = 'playing'");
        cmd.Parameters.AddWithValue(gameId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
```

`sim/Drivers/ScenarioRows.cs`: hold the info so the session's reload reads the end in process. Add a field and route the read through it:

```csharp
    private GameInfo _info = scenario.Info;

    public Task<GameInfo?> ReadGameAsync(Guid gameId, CancellationToken ct) =>
        Task.FromResult(gameId == _info.Id ? _info : null);

    public Task EndGameAsync(Guid gameId, CancellationToken ct)
    {
        if (gameId == _info.Id && _info.Status == GameStatus.Playing) _info = _info with { Status = GameStatus.Ended };
        return Task.CompletedTask;
    }
```

(replacing the existing `ReadGameAsync`).

- [ ] **Step 5: The session writes it, and hydration reads the log**

In `server/game/Session/GameSession.cs`:

1. Change the class summary's last sentence to "The log is appended before anything is broadcast; a row is written after the broadcast (setup's rows, and the end of the game)."
2. In `AppendAsync`, capture before applying and act after the broadcast loop:

```csharp
        var endedBefore = Play.Projection.Ended is not null;
```

(placed right after `var sealedBefore = ...`), and after the `foreach (var c in _clients.ToList()) await c.SendAsync(...)` line:

```csharp
        if (!endedBefore && Play.Projection.Ended is not null && Info.Status == GameStatus.Playing) await MarkEndedAsync();
```

3. Add the helper:

```csharp
    /// <summary>
    /// The one row this server writes from playing on (enforcement spec → The row and the broadcast). A failed write is logged and left:
    /// the log already holds the end, and the next hydration writes the row (StartAsync).
    /// </summary>
    private async Task MarkEndedAsync()
    {
        try { await _services.Rows.EndGameAsync(GameId, CancellationToken.None); }
        catch (Exception ex) { _log.LogError(ex, "Game {GameId}: the log ended the game but the row write failed; the next hydration retries.", GameId); return; }
        await ReloadInfoAsync();
    }
```

4. Replace `StartAsync`:

```csharp
    /// <summary>
    /// Starts the loop, then decides from the log, not the row (enforcement spec → Hydration): an ended log over a playing row is a crash
    /// between the append and the row write, so the row is written and nothing advances; an ended log over an ended row is left; a log
    /// with no live end catches up, whatever the row says — ended is a flag, and an undone strict end is still listed as ended.
    /// </summary>
    public Task StartAsync()
    {
        _loop ??= Task.Run(async () => { await foreach (var work in _mailbox.Reader.ReadAllAsync()) await work(); });
        return RunAsync(async () =>
        {
            if (Play is null) return 0;
            if (Play.Projection.Ended is not null) { if (Info.Status == GameStatus.Playing) await MarkEndedAsync(); return 0; }
            await AdvanceAsync();
            return 0;
        });
    }
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/Tabletop.GameServer.Tests && dotnet test tests/Tabletop.Sim.Tests`
Expected: PASS. `GameSessionTests`' other tests pass `null!` rows and never end a game, so `MarkEndedAsync` is never reached there.

- [ ] **Step 7: Commit**

```bash
git add server/game/Persistence server/game/Session/GameSession.cs sim/Drivers/ScenarioRows.cs tests/Tabletop.GameServer.Tests
git commit -m "Mark the row ended when the log ends the game, and hydrate from the log rather than the row"
```
