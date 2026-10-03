# Task 5: Grimdark Future's mission length

**Files:**
- Create: `server/game/Rules/GrimdarkFuture/GfRules.MissionLength.cs`
- Modify: `tests/Tabletop.GameServer.Tests/Infrastructure/FakeRules.cs` (`JudgeAdvanceHook`)
- Test: `tests/Tabletop.GameServer.Tests/MissionLengthTests.cs` (new)

**Interfaces:**
- Consumes: `GameEndingBreach`, `IGameRules.JudgeAdvance`, `RulesEngine.Advance` judging (Task 3); `set_round` with round-start changes (Task 4); `GameEnded`, `Projection.Ended` (Task 1); `WithSequences.JudgeAdvance` forwarding (Task 3); `SchedulerHarness`, `TestArmies`, `FakeRules.Gf()`.
- Produces: on `GrimdarkFutureRules`: `public const int FinalRound = 4`, `public const string MissionLength = "mission_length"`, `public static string MissionLengthDetail(int roundsPlayed)`, `public GameEndingBreach? JudgeAdvance(GameState state, PropertySet properties, IReadOnlyList<EventPayload> proposed)`; on `FakeRules`: `Func<GameState, PropertySet, IReadOnlyList<EventPayload>, GameEndingBreach?>? JudgeAdvanceHook { get; set; }`.

Spec: enforcement spec → *The round-4 end*: *Where it is detected*, *What each setting does*, *Playing on after a `strict` end*. The rule (GF p.7): "After 4 rounds have been played the game ends, and players determine who won by checking their mission objectives." The name `mission_length` is the heading p.59 gives the same count. The winners list is empty here; the objectives plan fills it.

| Setting | At the boundary after round 4 |
| --- | --- |
| `silent` | `round_started` for round 5, no warning |
| `warn` | `round_started` for round 5 carrying `"4 rounds have been played: the game ends after 4 (p.7)"` |
| `strict` | `game_ended` with that reason in place of `round_started`; the round stays 4 |

- [ ] **Step 1: Give `FakeRules` the hook**

In `tests/Tabletop.GameServer.Tests/Infrastructure/FakeRules.cs`, with the scheduler plan's members:

```csharp
    public Func<GameState, PropertySet, IReadOnlyList<EventPayload>, GameEndingBreach?>? JudgeAdvanceHook { get; set; }
    public GameEndingBreach? JudgeAdvance(GameState state, PropertySet properties, IReadOnlyList<EventPayload> proposed) => JudgeAdvanceHook?.Invoke(state, properties, proposed);
```

`FakeRules.Gf()` does not set it: the scheduler tests keep playing past round 4 as they do today.

- [ ] **Step 2: Write the failing tests**

Create `tests/Tabletop.GameServer.Tests/MissionLengthTests.cs`:

```csharp
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

/// <summary>GF p.7: "After 4 rounds have been played the game ends". silent lets round 5 start, warn marks it, strict ends the game.</summary>
public class MissionLengthTests
{
    static SchedulerHarness Game(Enforcement enforcement)
    {
        var rules = FakeRules.Gf();
        rules.JudgeAdvanceHook = GrimdarkFutureRules.Instance.JudgeAdvance;
        var h = new SchedulerHarness([TestArmies.Build("A", 1, 1), TestArmies.Build("B", 1, 1)], enforcement: enforcement, rules: rules);
        h.Dice.Enqueue(6); h.Dice.Enqueue(1); h.SkipDeployment();
        return h;
    }

    /// <summary>Every open activation answered with its first option, then the boundary question confirmed.</summary>
    static void PlayRound(SchedulerHarness h)
    {
        for (var i = 0; i < 20 && h.Open.Question != AskQuestions.EndRound; i++)
            h.Answer(h.Teams.ToList().IndexOf(h.Open.Teams[0]), new Chosen(((Options)h.Open.Legal).Choices[0].Id));
        h.EndRound();
    }

    static GameEvent LastOf<T>(SchedulerHarness h) where T : EventPayload => h.Log.Last(e => e.Payload is T);

    [Fact]
    public void Strict_ends_the_game_when_round_four_is_ended()
    {
        var h = Game(Enforcement.Strict);
        for (var r = 0; r < 3; r++) PlayRound(h);
        Assert.Equal(4, h.P.Scheduler.Round);
        Assert.Null(h.P.Ended);
        PlayRound(h);
        var end = Assert.IsType<GameEnded>(h.Log[^1].Payload);
        Assert.Equal(GrimdarkFutureRules.MissionLengthDetail(4), end.Reason);
        Assert.Equal("4 rounds have been played: the game ends after 4 (p.7)", end.Reason);
        Assert.Null(h.Log[^1].Actor);
        Assert.Equal(4, h.P.Scheduler.Round);
        Assert.Same(end, h.P.Ended);
        Assert.Empty(h.P.OpenAsks);
        Assert.Empty(h.Run());                                         // the judge does not fire again, and nothing else advances
        h.Rehydrate(); Assert.Empty(h.Run());
    }

    [Fact]
    public void Warn_marks_round_five_and_every_round_after()
    {
        var h = Game(Enforcement.Warn);
        for (var r = 0; r < 4; r++) PlayRound(h);
        var fifth = LastOf<RoundStarted>(h);
        Assert.Equal(5, ((RoundStarted)fifth.Payload).Round);
        Assert.Equal(GrimdarkFutureRules.MissionLengthDetail(4), fifth.Warning);
        Assert.Null(h.P.Ended);
        Assert.Equal(1, h.Log.Count(e => e.Warning is not null));      // once, not on every Advance
        PlayRound(h);
        Assert.Equal(GrimdarkFutureRules.MissionLengthDetail(5), LastOf<RoundStarted>(h).Warning);
        Assert.DoesNotContain(h.Log, e => e.Payload is GameEnded);
    }

    [Fact]
    public void Silent_starts_round_five_with_nothing_said()
    {
        var h = Game(Enforcement.Silent);
        for (var r = 0; r < 4; r++) PlayRound(h);
        Assert.Equal(5, h.P.Scheduler.Round);
        Assert.All(h.Log, e => Assert.Null(e.Warning));
        Assert.DoesNotContain(h.Log, e => e.Payload is GameEnded);
    }

    [Fact]
    public void A_menu_round_after_a_strict_end_reopens_play_until_the_next_boundary()
    {
        var h = Game(Enforcement.Strict);
        for (var r = 0; r < 4; r++) PlayRound(h);
        h.Dispatch(new SetRound(5), team: 0);
        Assert.Null(h.P.Ended);
        Assert.Equal((5, AskQuestions.Activate), (h.P.Scheduler.Round, h.Open.Question));
        PlayRound(h);
        Assert.Equal(GrimdarkFutureRules.MissionLengthDetail(5), Assert.IsType<GameEnded>(h.Log[^1].Payload).Reason);
        Assert.Equal(5, h.P.Scheduler.Round);
    }

    [Fact]
    public void Undoing_only_the_end_ends_the_game_again_at_once()
    {
        var h = Game(Enforcement.Strict);
        for (var r = 0; r < 4; r++) PlayRound(h);
        var endSeq = LastOf<GameEnded>(h).Seq;
        h.Dispatch(new Undo(endSeq - 1), team: 0);
        Assert.IsType<GameEnded>(h.Log[^1].Payload);
        Assert.NotNull(h.P.Ended);
        Assert.Equal(2, h.Log.Count(e => e.Payload is GameEnded));
    }

    [Fact]
    public void The_judge_reads_only_a_round_past_the_fourth_and_survives_a_wrapper()
    {
        var h = Game(Enforcement.Strict);
        var gf = GrimdarkFutureRules.Instance;
        Assert.Null(gf.JudgeAdvance(h.State, TestProperties.Core, [new RoundStarted(4, [], [])]));
        Assert.Null(gf.JudgeAdvance(h.State, TestProperties.Core, [new TurnStarted(h.Teams[0])]));
        var breach = gf.JudgeAdvance(h.State, TestProperties.Core, [new RoundStarted(5, [], [])]);
        Assert.Equal((GrimdarkFutureRules.MissionLength, GrimdarkFutureRules.MissionLengthDetail(4)), (breach!.Rule, breach.Detail));
        Assert.NotNull(new WithSequences(gf).JudgeAdvance(h.State, TestProperties.Core, [new RoundStarted(5, [], [])]));
    }
}
```

- [ ] **Step 3: Run them to see them fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter MissionLengthTests`
Expected: compile error: `GrimdarkFutureRules` has no `JudgeAdvance`, `MissionLengthDetail` or `MissionLength`.

- [ ] **Step 4: Write the judge**

Create `server/game/Rules/GrimdarkFuture/GfRules.MissionLength.cs`:

```csharp
using Tabletop.GameServer.Properties;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>
/// p.7: "After 4 rounds have been played the game ends, and players determine who won by checking their mission objectives."
/// The breach is the act of starting the round after the final one, judged on the scheduler's proposed round_started, so it comes
/// after a player has ended round 4 and before any of round 5's work. A menu set_round is a command and never reaches a judge.
/// </summary>
public sealed partial class GrimdarkFutureRules
{
    /// <summary>Fixed for the core game (manager ruling, 2026-10-02); p.27's and p.41's longer games are optional modules.</summary>
    public const int FinalRound = 4;
    /// <summary>The core book gives the rule no heading; p.59 calls the same count "Mission Length".</summary>
    public const string MissionLength = "mission_length";

    public static string MissionLengthDetail(int roundsPlayed) => $"{roundsPlayed} rounds have been played: the game ends after {FinalRound} (p.7)";

    public GameEndingBreach? JudgeAdvance(GameState state, PropertySet properties, IReadOnlyList<EventPayload> proposed) =>
        proposed.OfType<RoundStarted>().FirstOrDefault(r => r.Round > FinalRound) is { } next
            ? new GameEndingBreach(MissionLength, MissionLengthDetail(next.Round - 1), [])
            : null;
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "MissionLengthTests|RoundBoundaryTests|AlternatingSchedulerTests"`
Expected: PASS. `AlternatingSchedulerTests` plays on `FakeRules.Gf()`, which does not judge, so its `SetRound(4)` test and any round past 4 are unaffected.

- [ ] **Step 6: Run the whole GameServer suite**

Run: `dotnet test tests/Tabletop.GameServer.Tests`
Expected: PASS. Tests on `GrimdarkFutureRules.Instance` directly that play past round 4 under `strict` now end the game; there are none on `main` (they run on `FakeRules`), so a failure here is a test that does and must say what it expects at round 5.

- [ ] **Step 7: Commit**

```bash
git add server/game/Rules/GrimdarkFuture/GfRules.MissionLength.cs tests/Tabletop.GameServer.Tests
git commit -m "End a Grimdark Future game after four rounds under strict, and warn of it under warn"
```
