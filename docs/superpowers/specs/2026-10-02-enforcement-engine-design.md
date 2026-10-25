# Enforcement engine

The game's enforcement setting (`GameInfo.Enforcement`, `silent | warn | strict`, chosen at creation) is
one policy that every system hands a breach to. A breach says what was broken, the detail players read,
and what `strict` does about it. The policy turns the breach and the setting into one verdict: proceed
silently, proceed with a warning on the event, refuse the command, end the game, or (under `warn`) ask
every team whether to end it. Two kinds of caller
exist: the dispatcher, which judges a player's command before it runs, and the engine, which judges its
own proposed events before it returns them. The end of round 4 (GF p.7) is the first breach raised at
state time; command legality is the existing client.

Ruled by the owner: the round-4 end follows the enforcement setting. `strict` ends the game, `silent`
leaves it to the players, and `warn` asks every team End or Continue when round 4 ends (2026-10-15);
End ends the game as `strict` does, and only every team's Continue starts round 5. The policy is one
function over a breach, so any system passes in what it needs and the game handles it; a generic
enforcement engine beyond that is deferred. The same day the owner
ruled that the engine scores the core mission, so `game_ended` carries the winner; how the winner is
found is [the objectives spec](2026-10-02-objectives-design.md).

The engine still never appends, never broadcasts and holds nothing between calls. A breach is a value a
caller computes and the policy decides; the result is payloads and a warning string returned to the
session, which appends them as it does today.

## The breach

```csharp
public sealed record Breach(string Rule, string Detail, StrictConsequence Strict);

public abstract record StrictConsequence;
public sealed record StrictRefuses(RefusalReason Reason) : StrictConsequence;
public sealed record StrictEndsGame(IReadOnlyList<Guid> Winners) : StrictConsequence;
```

- **`Rule`** names what was broken, in snake_case: `outside_legal_set` and `illegal` for command
  legality, `mission_length` for the round-4 end. A ruleset's rule names live in
  `Rules/GrimdarkFuture/`; the two legality names are generic and live beside the policy. `Rule` is
  not on the wire. Tests assert it and the server's log line names it.
- **`Detail`** is the sentence players read: the warning text under `warn`, the refusal detail or the
  end reason under `strict`.
- **`Strict`** is what `strict` does. Two consequences exist because two clients need them.
  `StrictRefuses` refuses the command. `StrictEndsGame` ends the game with the given winners (one
  team is a win, several are a tie between them). A third is added with the first client that needs it.

The records sit in `server/game/Dispatch/EnforcementPolicy.cs` beside the policy, in the existing
namespace, so `CommandDispatcher` and `RulesEngine` keep their `using` lines.

## The policy

`EnforcementPolicy.Decide(Enforcement, Breach?)` is a pure function. It returns the existing `Verdict`
union:

```csharp
public sealed record Accept(string? Warning) : Verdict;
public sealed record Refuse(RefusalReason Reason, string Detail) : Verdict;
public sealed record EndGame(string Reason, IReadOnlyList<Guid> Winners) : Verdict;
public sealed record OfferEnd(string Reason, IReadOnlyList<Guid> Winners) : Verdict;   // warn: ask End or Continue
```

| Breach | `silent` | `warn` | `strict` |
| --- | --- | --- | --- |
| none | `Accept(null)` | `Accept(null)` | `Accept(null)` |
| `StrictRefuses(r)` | `Accept(null)` | `Accept(Detail)` | `Refuse(r, Detail)` |
| `StrictEndsGame(w)` | `Accept(null)` | `OfferEnd(Detail, w)` | `EndGame(Detail, w)` |

The existing `Decide(Enforcement, Legality, string detail)` stays as an overload that maps a
`Legality` to a breach and delegates: `AlwaysAccepted` and `Legal` to none, `OutsideLegalSet` to
`Breach("outside_legal_set", detail, StrictRefuses(OutsideLegalSet))`, `Illegal` to
`Breach("illegal", detail, StrictRefuses(Illegal))`. Its table in `docs/design/game-server/design.md`
is unchanged and `EnforcementPolicyTests` keep passing. A test runs the overload over every
(`Enforcement`, `Legality`) pair and asserts it never returns `EndGame`.

`EnforcementPolicy.EndGamePayloads(Projection, string reason, IReadOnlyList<Guid> winners)` is the one
place an `EndGame` verdict becomes payloads: `[new GameEnded(reason, winners)]`, alone. It throws
`InvalidOperationException` when the stack is not empty or an Ask is open. The only `EndGame` today
comes at the round boundary, where both already hold (*Open Asks at the end*, below); a later breach
that can end the game mid-sequence designs its own closing then, rather than inheriting an untested one.

**Menu edits never reach the policy.** `CommandClass.Override` still goes straight to
`MenuTranslation`, under every setting, and so does `Undo`. The override path is how players continue
past anything `strict` did.

## Who raises a breach

Two callers, one policy.

**The dispatcher, for a player's command.** `CommandDispatcher.Answer` and `CommandDispatcher.Engine`
already compute a `Legality` and call `Decide`; they keep doing so through the overload. `Refuse`
becomes `Refused` and `Accept` runs the command with its warning. A legality breach is always
`StrictRefuses`, so `EndGame` cannot arrive here: both call sites switch over the verdict and throw
`UnreachableException` on `EndGame`, and the overload test above pins why.

**The engine, for its own proposed events.** `RulesEngine.Advance` computes what the interpreter or
the scheduler would emit, exactly as today, and then asks the ruleset to judge the proposal before
returning it. The hook's result type can only end the game, so a ruleset cannot ask the engine to
refuse a proposal it made itself:

```csharp
/// <summary>A breach found in the engine's own proposal. Under strict it always ends the game.</summary>
public sealed record GameEndingBreach(string Rule, string Detail, IReadOnlyList<Guid> Winners);

// IGameRules, a default member so StubRules and FakeRules need no change
GameEndingBreach? JudgeAdvance(GameState state, PropertySet properties, IReadOnlyList<EventPayload> proposed) => null;
```

`RulesEngine.Advance` wraps a non-null result as `Breach(Rule, Detail, new StrictEndsGame(Winners))`
and returns:

- the proposal unchanged, when the hook returns null or the verdict is `Accept(null)`;
- the proposal with the verdict's warning, on `Accept(warning)`;
- `EndGamePayloads(state.Projection, reason, winners)` in place of the proposal, on `EndGame`;
- on `OfferEnd`, what the round's `game_over` Ask says (*Warn asks to end*, below).

Judging the proposal, rather than threading a breach out of each producer, keeps `SequenceInterpreter`
and `IStep` unchanged: whatever the scheduler or a step proposes passes the same gate. The hook sees the
proposal and the state it was computed from, so it can name the act (`round_started` for round 5) and
need not re-derive the producer's reasoning. It takes the pinned `PropertySet` because the winners are
read from rules the set defines (Shaken, Aircraft, Ambush).

`WithSequences` in `tests/Tabletop.GameServer.Tests/Infrastructure/StubRules.cs` wraps a real ruleset
and forwards each member by hand. It must forward `JudgeAdvance` explicitly, or a wrapped Grimdark
Future silently loses the round-4 end through the default member.

**The contract on a judge.** The engine holds no state, so a breach the engine lets through must not be
raised again on the next call, and a consequence must stop the producer from proposing the same act.
Both hold when the judged act is itself an event: once `round_started` for round 5 is in the log, the
scheduler no longer proposes it; once `game_ended` is in the log, `Advance` is silent (below). A judge
that keys on something the proposal does not change would warn on every call; the session's
10 000-iteration guard is the only stop, and a test per judge asserts the second call is clean.

## How a warning is recorded and shown

**On the event, as today.** `GameEvent.Warning` already carries a command's warning on the first event of
its batch (`GameSession.AppendAsync`). A state-time warning uses the same field on the first event of the
proposal it rode in on. No warning event kind is added: a warning is about an event, and a separate event
would need its own guard against being raised again.

**The engine's output carries the string.** `IRulesEngine.Advance` returns `Emit` instead of a bare list,
and `Emit` gains the field:

```csharp
public sealed record Emit(IReadOnlyList<EventPayload> Events, string? Warning = null) : EngineResult;
```

`GameSession.AdvanceAsync` passes `Emit.Warning` to `AppendAsync`, which already takes one. `Execute`
keeps returning `Emit` with no warning, and `CommandDispatcher.Engine` keeps its verdict's.
`SequenceInterpreter.Advance` still returns a list.

**What players see.** The client's log line already appends the warning (`LogText`: `"{who} {text} —
{warning}"`). The viewer's `LogPanel` already marks a warned frame, and `sim/`'s recording already
carries `RecordedFrame.Warning`. Nothing new is drawn for a warning.

**Undo.** A warning belongs to its event. Undoing past the event removes both. When the engine reaches
the same point again it proposes the same act, the judge raises the same breach, and the new event
carries the warning again. Undo stays on the override path under every setting.

## The round-4 end

> After 4 rounds have been played the game ends, and players determine who won by checking their
> mission objectives. (GF p.7)

The rule's name is `mission_length`, taken from the heading p.59 gives the same count in its own
mission ("Mission Length: Missions are played using the standard rules, however if after 4 rounds there
is no winner, the game continues onto the next round"). The core book gives the rule no heading.

**The final round is fixed at 4** (manager ruling, 2026-10-02). p.27's six-round missions and p.41's
*Prolonged Battle* are optional modules the game does not model; a table playing one uses `warn` or
`silent`, or the menu's set round under `strict`.

### Where it is detected

A round ends when a player answers the `end_round` Ask (scheduler spec, *Rounds count up only when a
player says so*). The round-end step then seizes objectives (objectives spec), and the scheduler
proposes the next round's `round_started` from `SchedulerBase.BeginRound`. Grimdark Future's judge, in
a new partial `Rules/GrimdarkFuture/GfRules.MissionLength.cs`, raises the breach on that proposal:

```csharp
public const int FinalRound = 4;                      // p.7
public const string MissionLength = "mission_length"; // the heading p.59 gives the count

public GameEndingBreach? JudgeAdvance(GameState state, PropertySet properties, IReadOnlyList<EventPayload> proposed) =>
    proposed.OfType<RoundStarted>().FirstOrDefault(r => r.Round > FinalRound) is { } next
        ? new GameEndingBreach(MissionLength, $"{MissionLengthDetail(next.Round - 1)}. {GfObjectives.Standing(state)}", GfObjectives.Winners(state))
        : null;
```

`MissionLengthDetail(int roundsPlayed)` reads "4 rounds have been played: the game ends after 4 (p.7)".
`JudgeAdvance` appends a period and `GfObjectives.Standing(state)`, the standing the objectives spec defines. `GfObjectives.Winners` is the objectives spec's; the enforcement
plan lands the judge with an empty list and the objectives plan fills it.

The breach is raised when the round after the final one would start, which is the moment the final
round has finished: after a player has said so, after round 4's objectives are seized, and before any
of round 5's work. The proposal is `round_started` with its `OnRoundStarted` changes (fatigue cleared,
spell tokens granted), so under `strict` none of round 5's changes are applied. Grimdark Future decides
the first team from round 2 on without a roll-off (`GrimdarkFutureRules.FirstTeam`), so no dice are
drawn ahead of the breach.

Every boundary past the final round raises it again (round 6 after 5, and so on), because each is a new
`round_started` with `Round > 4`. A menu `set_round` is a command with an actor on the override path;
the judge never sees it.

### What each setting does

| Setting | At the boundary after round 4 |
| --- | --- |
| `silent` | `round_started` for round 5, as today. |
| `warn` | The `game_over` Ask to every team (*Warn asks to end*). End: `game_ended` as under `strict`. Every team's Continue: `round_started` for round 5, its `GameEvent.Warning` the detail above, standing included. Each later boundary asks again, with its own count and standing. |
| `strict` | `game_ended` in place of `round_started`, with the winners. The round stays 4. |

### Warn asks to end

`Engine/GameOverAsk.cs`. On `OfferEnd`, `RulesEngine.Advance` reads the log since the last
`round_started`:

- **No `game_over` Ask there** (or one closed with neither an End nor every team's Continue, as an
  abandon leaves it): it emits `ask_opened` in place of the proposal. The Ask is unframed and sealed,
  question `AskQuestions.GameOver` (`game_over`), prompt the breach's detail, `Options` End and Continue
  (`GameOverChoices.End`, `GameOverChoices.Continue`), no pass, addressed to every team with a
  participant (as `end_round` is). With no team to ask, the proposal goes through with its warning.
- **Open:** nothing. The scheduler waits on any open unframed Ask.
- **Closed with an End:** `EndGamePayloads` with the breach's detail and winners, read again from the
  state now, so the result is the one `strict` writes.
- **Closed with every team's Continue:** the proposal, with the warning.

Sealed, the dispatcher closes it only when every addressed team has answered. Before the scheduler
runs, `GameOverAsk.Settle` closes an open `game_over` Ask early when any answer is End (with the answers
so far), so one team's End ends the game without waiting for the other; and closes, with no answers, one
opened before the latest `round_started` (a menu `set_round` while it was open).

Undo of `game_ended` alone leaves the End answer in the log, so the game ends again at once, as under
`strict`.

### The `game_ended` event

```csharp
[JsonDerivedType(typeof(GameEnded), "game_ended")]
/// <summary>The game is over. Reason is the sentence players read. Winners: one team is the winner; several are a tie between them.</summary>
public sealed record GameEnded(string Reason, IReadOnlyList<Guid> Winners) : EventPayload;
```

Under Grimdark Future `Winners` is never empty: when no team controls a marker every team is tied at
none. Until the objectives plan lands, the judge passes an empty list, which the client and viewer
print as no result.

`Projection` gains `GameEnded? Ended`. `EventApplier` sets it on `game_ended` and clears it on
`round_started`, so a later round started from the menu reopens play. `Undone` rebuilds the projection
as it does today, so undoing past the end clears it.

`RulesEngine.Advance` returns nothing while `state.Projection.Ended` is set, before it consults the
interpreter or the scheduler. That is what stops the scheduler proposing round 5 again: its
`end_round` Ask is still answered in the log, and without the check the next call would propose the
same `round_started` and end the game a second time.

The manual score counters stay: `set_score` is an override command, accepted after the end as before
it, and nothing the engine scores writes them.

### A menu round start carries the round-start changes

`set_round` is how a table plays on after a `strict` end, so it must start a round the way the scheduler
does (manager ruling, 2026-10-02). Today `MenuTranslation` writes `round_started` with no changes, so a
round started from the menu grants no spell tokens and clears no fatigue or action records.

- `IScheduler` gains `IReadOnlyList<StateChange> RoundStartChanges(GameState state, int round)`.
  `SchedulerBase` implements it as the shape's changes then `IGameRules.OnRoundStarted`, and
  `BeginRound` calls it instead of building the list inline. `NoScheduler` returns none.
- `IRulesEngine` gains the same member. `RulesEngine` delegates to its scheduler; `InertEngine` and the
  test engines return none.
- `CommandDispatcher`'s override arm builds `set_round`'s `round_started` with
  `engine.RoundStartChanges(state, r.Round)`; `MenuTranslation` keeps the reference check and the order.

The event stays a command with an actor, so the round-advance invariant in `sim/` still reads it as an
override.

### The row and the broadcast

**The game server writes `game.status = 'ended'` when its own log ends the game.** This is the one row
it writes from `playing` on, and it reverses the convention that the game server writes a game's rows
only while it is `lobby` or `setup` (*Statements this reverses*, below). Its `game.status` cell in *Rows
by writer* becomes `lobby → setup → playing`, and `playing → ended` when a `game_ended` it appended
becomes live. Management keeps writing `'ended'` on `/end` and on the last leave. Both write the same
value, so their order does not matter:

```sql
update management.game set status = 'ended' where id = $1 and status = 'playing'
```

`IGameRows.EndGameAsync(Guid gameId, CancellationToken ct)` runs it with one positional parameter, as
the other writes in `PostgresGameRows.Writes.cs` do. A game management already ended is left as it is.

Calling management's `POST /v1/games/{id}/end` instead was rejected: it is the creator's alone
(accounts spec, *Ending and deleting are the creator's alone*), and the game server's only outbound
HTTP is the JWKS fetch; it holds no credential to call management with, and already writes
`game.status` itself at the end of setup.

**Order, in `GameSession.AppendAsync`:** the batch is appended, applied and broadcast as today. Then, if
`Projection.Ended` went from unset to set in this batch and `Info.Status` is `Playing`, the session
calls `EndGameAsync` and `ReloadInfoAsync`, which re-reads the row and broadcasts the new
`GameInfoMessage`. The log first, the row after the broadcast: the same order as setup's rows. The
`at_rest` frame that closes the command or the advance follows the `GameInfoMessage`.

**Hydration.** `GameSession.StartAsync` decides from the log, not the row:

- `Projection.Ended` set and the row `playing`: the crash fell between the append and the row write.
  It writes the row, reloads, and does not advance.
- `Projection.Ended` set and the row `ended`: nothing to do.
- `Projection.Ended` unset: it advances, whatever the row says, unless the row is not `playing` and the log is
  empty. A game ended in setup never played, so it does not start round 0. A `strict` game whose end was undone
  is listed as ended and still has to catch up after a restart. A game management ended with `/end`
  catches up the same way, which is what its next command would do anyway.

**After an undo.** Undoing past `game_ended`, or a menu `set_round` after it, clears `Projection.Ended`
and the engine resumes. The row stays `'ended'` (manager ruling, 2026-10-02; accounts spec, *An ended
game is just one marked ended*), and commands are already accepted while the game is `ended`
(`GameSession.HandleCommandAsync`).

### What clients see

- **The log.** A line for `game_ended` in the client's `LogText` (`"ended the game: {Reason}"`, then
  `"; Team 1 won"` for one winner or `"; tied between Team 1, Team 2"` for several, in its own voice),
  and in the viewer's `logLines.ts` and `sim/`'s `LogLines.cs`, which the agreement test holds to one
  text: `"Game ended: {Reason}"` with the same suffix. Under `warn`, the `round_started` line for
  round 5 carries the warning after its dash.
- **The status.** `GameInfoMessage` arrives with `Status = Ended`. The client's `GameScreen` header
  already prints the status, and `Session` already rebuilds the screen on `GameChanged`; the play
  panels stay, because `GameScreen` shows them for `Playing` and `Ended` alike.
- **Asks.** None are open after the end. Under `warn`, the `game_over` Ask is the client's Game over
  popup (`EndMessage`, `GameOverView`): the prompt, the scores and End and Continue, then "Waiting for the
  other player" once the team has answered. After the end the same popup shows the result, the reason and
  the scores, with Stay (close it; the table, log, roster and undo stay) and Leave (home through
  `Session.DisconnectAsync`). Nobody is removed from the game: sockets stay open and sight questions
  and model facts are answered at `ended` as before.
- **Commands.** Accepted as before. With no open Ask and an empty stack, the useful ones are the
  menu's: `set_score`, marker and model edits, `undo`, and `set_round` to play on.

The end-round question's wording does not change (manager ruling, 2026-10-02): after round 4 under
`strict`, answering "End the round" ends the game, and the log line, the status and undo cover it.

### Open Asks at the end

None. `SchedulerBase.Advance` reaches the boundary only when `Projection.OpenAsks` is empty, and
`RulesEngine.Advance` reaches the scheduler only when the stack is empty and no framed Ask is open. The
`end_round` Ask that led there is already closed by its answer, and under `warn` so is the `game_over`
Ask. `EndGamePayloads` asserts both.

### Playing on after a `strict` end

`strict` refuses nothing from the menu, so the override path is how a table plays a fifth round:

- **`set_round(5)`.** `round_started` with an actor and round 5's changes clears `Projection.Ended`;
  the scheduler runs round 5; at its boundary the judge raises the breach again and the game ends again.
- **`undo` into round 4.** Round 4's remainder is played again, and the boundary ends the game again.
- **`undo` of `game_ended` alone.** The next `Advance` proposes the same `round_started` and the game
  ends at once. Under `strict` the end is the boundary's only outcome.
- **`set_active_team` after the end.** Accepted and logged; nothing advances, because the end still
  stands until a `round_started` follows it.

### Existing games

The final round is ruleset C#, not authored properties, so it is not pinned by `PropertySet` hash and
applies to games already in progress, as any change to `GrimdarkFutureRules` does. A `strict` game
already past round 4 ends at its next boundary. A `warn` game asks from its next boundary on.

## Clients now, and candidates

| Client | Where | Breach | `strict` |
| --- | --- | --- | --- |
| An Ask answer outside its legal set, or one the ruleset cannot read | `CommandDispatcher.Answer` | `outside_legal_set` | refuses |
| A command `RulesEngine.Judge` calls illegal: a dice command naming no such roll or die, or a command the engine does not know | `CommandDispatcher.Engine` | `illegal` | refuses |
| Round 5 or later about to start | `GrimdarkFutureRules.JudgeAdvance` | `mission_length` | ends the game |

Named as candidates only, with nothing built for them: placement flags (`architecture.md` → *Rules
enforcement*; base overlap, a path through a model and broken coherency on a move answer are judged as `move_overlap`, `move_through_models` and `move_coherency`);
hand moves are on the override path, so a flag needs a consequence this design does not define), and a
step that finds the rules broken mid-sequence (its proposal already passes `JudgeAdvance`; ending the
game mid-sequence needs the closing `EndGamePayloads` refuses today).

## The wire

Additive only: one `EventPayload` kind, `game_ended` (`GameEnded(string Reason, IReadOnlyList<Guid>
Winners)`), registered on `EventPayload`'s `[JsonDerivedType]` list so `EventKinds.All` and the
snake_case registration tests pick it up; and one `Projection` property, `Ended`. No type is renamed and
no field changes meaning. `GameEvent.Warning`, `GameInfoMessage`, `GameStatus.Ended` and `Refused`
already exist.

**`ProtocolVersion.Current` stays 1** (manager ruling, 2026-10-02). A client built before this change
cannot deserialize a log that holds `game_ended`. That is safe only while every client is built from the
same commit as the servers, which holds before release; the assumption goes into `docs/architecture.md`
→ *Transport*, so the first release moves the version on the next breaking addition. The viewer's
`CLIENT_VERSION` stays 1 with it.

**The viewer.** `viewer/src/lib/protocol/wire.generated.ts` is regenerated
(`TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests`). `npm run check` then fails until
`viewer/src/lib/live/apply.ts` handles `game_ended`: its `Projection` type and `initialProjection` gain
`ended`, `game_ended` sets it and `round_started` clears it. `viewer/src/lib/live/logLines.ts` gains the
line. `viewer/tests/feed.test.ts` and `apply.test.ts` cover both. The recording schema is unchanged: a
recorded frame's `Kind` is a string and `RecordingMeta.Outcome` gains a value, so
`RecordingJson.SchemaVersion` stays. The checked-in agreement fixtures (`skirmish`, `terrain`, two
rounds) hold no `game_ended` and are not regenerated.

## sim

Scenarios run under `strict` by default (`ScenarioBuilder`), with a default round cap of 8.

**A wipe-out is not a win.** p.6: "unless stated otherwise, players don't ever win the game by fully
destroying their opponent's army". `RunOutcome.Decided` (last team standing) is removed, and with it the
wipe check in `RunLoop.DriveAsync`. A run with one army gone plays on: the empty team has no legal turn,
and the boundary question still reaches every team with a participant.

- **`RunOutcome.Ended(int Round, IReadOnlyList<Guid> Winners)`**, kind `ended`, replaces it.
  `RunLoop.DriveAsync` checks `Projection.Ended` after each step, before the round cap. Under `strict`
  every run that is not stuck or failed ends `ended` at round 4; under `warn` `AnswerPolicy` answers
  the `game_over` Ask End (the one Ask it does not draw), so a run ends `ended` at round 4 too; under
  `silent` runs end `round_limit`. `RunOutcome.Kind`, `RunReport.Passed`, `Summary.OutcomeJson` and
  `RunLoop.Recording.OutcomeOf` follow. `RecordingMeta.Outcome` writes `ended` instead of `decided`;
  `RecordingMeta.WinningTeamId` is the sole winner, null for a tie, and `OutcomeDetail` names a tie's
  teams. `Expectation.Outcome("decided")` is no longer written by any scenario.
- **`ProjectionComparer`** compares `Ended` (reason and winners), so the replay invariant covers it.
- **`SubmitResult.Accepted.Warning`** is populated by both drivers: the `Warning` of the first event of
  the submitted command's own batch (actor the submitting participant, seq above the head at submit).
  `InProcessDriver.SubmitAsync` reads it from the events it absorbs; `SocketDriver.SubmitAsync` from the
  submitting player's own stream up to that player's `at_rest`. It is null when the command appended nothing.
- **Invariant 6, `NothingAdvancesAfterTheEnd`.** In the effective log, every event between a
  `game_ended` and the next `round_started` has an actor. It reads the log only and names no rule.
- **Three catalog scenarios**, two one-unit armies (as `stalemate`) and a round cap of 6:
  `final-round` (`strict`: `Outcome("ended")`, `ReachedRound(4)`, `EventOfKind("game_ended")`),
  `final-round-warn` (`warn`: `Outcome("ended")`, `ReachedRound(4)`, `AskAsked("game_over")`,
  `EventOfKind("game_ended")`) and `final-round-silent` (`silent`: `Outcome("round_limit")`,
  `NoEventOfKind("game_ended")`, `Warnings("round_started", 0)`). `Expectation.Warnings(string Kind,
  int Count)` counts the effective-log events of that kind that carry a warning. The objectives spec adds
  the scenario that ends with a scored winner.
- **Warn runs.** An out-of-set answer under `warn` is covered by a scripted `RunLoopTests` case
  asserting `Accepted.Warning` and the event's `Warning` agree.

Tests that hard-code `decided|round_limit` from a default `strict` run (`RunLoopTests`,
`StepAndWaitTests`, `DriverAgreementTests`, `ProgramTests`, `ExpectationTests`, `RecordingTests`) accept
`ended` instead of `decided`.

## Statements this reverses

The game server writing `game.status` from `playing` on contradicts the convention wherever it is
stated. The plan corrects each: `CLAUDE.md` → *Conventions*; `docs/design/management-api/design.md` →
*Rows by writer* (the column header and the `game.status` row); `docs/architecture.md` → *Game management data model*; `docs/data-model.md`;
`docs/schema.md`; `docs/design/README.md`; `docs/design/contracts/design.md`;
`docs/design/game-server/design.md`; and the doc comments on `IGameRows` and `GameSession`.

## Files touched

Created: `server/game/Rules/GrimdarkFuture/GfRules.MissionLength.cs` (a partial of its own, so
`GfNames.cs` and `GfRules.Sequences.cs` are not edited); `tests/Tabletop.GameServer.Tests/MissionLengthTests.cs`
(the three settings at the boundary after round 4, a clean second `Advance`, round 6 after `set_round(5)`,
`undo` of the end); `tests/Tabletop.GameServer.Tests/GameEndTests.cs` (`[Collection("Postgres")]`: the
row written once, `GameInfoMessage` before `at_rest`, a game management ended left alone, the crash
reconciled, an undone end advanced on restart).

Modified, server and libraries:

- `server/game/Dispatch/EnforcementPolicy.cs`: `Breach`, `StrictConsequence`, `EndGame`,
  `Decide(Enforcement, Breach?)`, the `Legality` overload, `EndGamePayloads`.
- `server/game/Dispatch/CommandDispatcher.cs`: `UnreachableException` on `EndGame`; `set_round` with
  `RoundStartChanges`.
- `server/game/Rules/IRulesEngine.cs`: `Emit.Warning`; `Advance` returns `Emit`; `RoundStartChanges`.
- `server/game/Rules/IGameRules.cs`: `GameEndingBreach`, `JudgeAdvance` (default member).
- `server/game/Rules/InertEngine.cs`: `Advance` returns `Emit`; `RoundStartChanges`.
- `server/game/Engine/IScheduler.cs`: `RoundStartChanges` on `IScheduler` and `NoScheduler`.
- `server/game/Scheduler/SchedulerBase.cs`: `RoundStartChanges`, called by `BeginRound`.
- `server/game/Engine/RulesEngine.cs`: silent while ended; judge; policy; `RoundStartChanges`.
- `server/game/Session/GameSession.cs`: `AdvanceAsync`, `AppendAsync`, `StartAsync`, the doc comment.
- `server/game/Persistence/IGameRows.cs` (`EndGameAsync`, the doc comment),
  `server/game/Persistence/PostgresGameRows.Writes.cs`.
- `libs/Tabletop.Protocol/Events.cs` (`GameEnded`), `Projection.cs` (`Ended`), `EventApplier.cs`.
- `client/src/Core/LogText.cs`: the `game_ended` line.

Modified, `sim/`: `Run/RunOutcome.cs`, `Run/RunLoop.cs`, `Run/RunLoop.Recording.cs`, `Run/RunReport.cs`,
`Summary.cs`, `Drivers/InProcessDriver.cs`, `Drivers/SocketDriver.cs`, `Drivers/ScenarioRows.cs`
(`EndGameAsync` over a mutable status), `Invariants/Invariants.cs`, `Invariants/ProjectionComparer.cs`,
`Scenarios/Expectation.cs`, `Scenarios/ScenarioCatalog.cs`, `Recording/LogLines.cs`,
`Recording/Recording.cs` (the `Outcome` doc comment).

Modified, `viewer/`: `src/lib/protocol/wire.generated.ts` (regenerated), `src/lib/live/apply.ts`,
`src/lib/live/logLines.ts`, `tests/apply.test.ts`, `tests/feed.test.ts`.

Modified, tests: `tests/Tabletop.GameServer.Tests/EnforcementPolicyTests.cs`, `RulesEngineTests.cs`,
`GameSessionTests.cs` (`DesyncingEngine`, `ThrowingEngine`), `SchedulerFactoryTests.cs`,
`CommandDispatcherTests.cs` (`set_round` changes), `Infrastructure/FixedAskEngine.cs`,
`Infrastructure/Sim.cs`, `Infrastructure/StubRules.cs` (`WithSequences` forwards `JudgeAdvance`);
`tests/Tabletop.Protocol.Tests/EventApplierTests.cs`, `EventTests.cs`;
`tests/Tabletop.Client.Tests/ViewModelTests.cs`; `tests/Tabletop.Sim.Tests/RunLoopTests.cs`,
`DriverAgreementTests.cs`, `StepAndWaitTests.cs`, `ProgramTests.cs`, `ExpectationTests.cs`,
`InvariantTests.cs`, `ScenarioTests.cs`, `RecordingTests.cs`.

Docs: the places under *Statements this reverses*, and `docs/architecture.md` → *Transport* (the
`ProtocolVersion` assumption).

**Collisions with the spell-library tree.** This tree merges first; the spell tree takes it in by merge.
Not touched here: `server/game/Content/*.properties.json`, `server/game/Effects/`, `server/game/Properties/`,
`GfCasting.cs`, `GfNames.cs`, `GfRules.Sequences.cs`, `GfRules.Scheduling.cs`, `server/management/`.
Shared, with textual merges expected: `IGameRules.cs`, `sim/Invariants/Invariants.cs`,
`sim/Scenarios/ScenarioCatalog.cs`, docs, and `wire.generated.ts`, which is regenerated after the merge
rather than merged by hand. No migration: `game.status` already takes `'ended'`.

## Settled, not open

Every question this spec raised is settled: an undone `strict` end stays listed as ended; the final
round is fixed at 4; the end-round question's wording stays; `ProtocolVersion` stays 1 under the
recorded pre-release assumption (all manager rulings, 2026-10-02). Nothing is open for the owner.
