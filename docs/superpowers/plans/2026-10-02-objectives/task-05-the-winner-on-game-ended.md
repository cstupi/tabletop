# Task 5: The winner on `game_ended`

**Files:**
- Modify: `server/game/Rules/GrimdarkFuture/GfRules.MissionLength.cs`
- Test: `tests/Tabletop.GameServer.Tests/ObjectivesTests.cs`, `tests/Tabletop.GameServer.Tests/MissionLengthTests.cs`

**Interfaces:**
- Consumes: `GfObjectives.Winners`, `GfObjectives.Standing` (Task 3); the round-end step (Task 4); `GrimdarkFutureRules.JudgeAdvance`, `MissionLengthDetail`, `GameEndingBreach` (enforcement plan Tasks 3 and 5); the `Seizing`, `OntoTheObjective` and `PlayTurns` helpers in `ObjectivesTests` (Task 4).
- Produces: `GrimdarkFutureRules.JudgeAdvance` returns `GameEndingBreach(MissionLength, $"{MissionLengthDetail(n)} {GfObjectives.Standing(state)}", GfObjectives.Winners(state))`. Under `strict` `game_ended` names the winner or the tied teams; under `warn` the warning names the standing; under `silent` nothing but `Projection.Control` records it.

Spec: objectives spec → *Under each setting*; enforcement spec → *Where it is detected*. p.6: "After 4 rounds have been played, the game ends, and the player that controls most markers wins."

- [ ] **Step 1: Write the failing tests**

Append to `tests/Tabletop.GameServer.Tests/ObjectivesTests.cs`, inside the class:

```csharp
    [Fact]
    public void Under_strict_round_fours_seizures_land_before_the_end_and_decide_the_winner()
    {
        var h = Seizing(Enforcement.Strict, judge: true);
        for (var r = 0; r < 3; r++) { PlayTurns(h); h.EndRound(); }
        OntoTheObjective(h);                                                             // seized only at the end of round 4
        PlayTurns(h);
        var seq = h.P.LastSeq; h.EndRound();
        Assert.Equal(["ask_answered", "ask_closed", "round_ended", "game_ended"], h.Kinds(seq));
        var end = Assert.IsType<GameEnded>(h.Log[^1].Payload);
        Assert.Equal([h.Teams[0]], end.Winners);
        Assert.Equal($"{GrimdarkFutureRules.MissionLengthDetail(4)} Markers held: team 1: 1, team 2: 0.", end.Reason);
    }

    [Fact]
    public void Under_strict_an_empty_table_is_a_tie_between_every_team()
    {
        var h = Seizing(Enforcement.Strict, judge: true);
        for (var r = 0; r < 4; r++) { PlayTurns(h); h.EndRound(); }
        Assert.Equal(h.Teams, Assert.IsType<GameEnded>(h.Log[^1].Payload).Winners);
    }

    [Fact]
    public void Under_warn_the_warning_names_the_standing_and_the_game_plays_on()
    {
        var h = Seizing(Enforcement.Warn, judge: true);
        OntoTheObjective(h);
        for (var r = 0; r < 4; r++) { PlayTurns(h); h.EndRound(); }
        var fifth = h.Log.Last(e => e.Payload is RoundStarted);
        Assert.Equal($"{GrimdarkFutureRules.MissionLengthDetail(4)} Markers held: team 1: 1, team 2: 0.", fifth.Warning);
        Assert.DoesNotContain(h.Log, e => e.Payload is GameEnded);
        Assert.Equal(h.Teams[0], Assert.Single(h.P.Control).Value);
    }
```

In `tests/Tabletop.GameServer.Tests/MissionLengthTests.cs` the reason now carries the standing, so the three exact comparisons of a reason or detail with `MissionLengthDetail(n)` become prefix checks:

- `Assert.Equal(GrimdarkFutureRules.MissionLengthDetail(4), end.Reason);` → `Assert.StartsWith(GrimdarkFutureRules.MissionLengthDetail(4), end.Reason);`, and delete the following line that compares `end.Reason` with the literal sentence.
- In `Warn_marks_round_five_and_every_round_after`, both `Assert.Equal(GrimdarkFutureRules.MissionLengthDetail(n), ....Warning)` → `Assert.StartsWith(GrimdarkFutureRules.MissionLengthDetail(n), ....Warning)`.
- In `A_menu_round_after_a_strict_end_reopens_play_until_the_next_boundary`, the `Assert.Equal(GrimdarkFutureRules.MissionLengthDetail(5), ...Reason)` → `Assert.StartsWith`.
- In `The_judge_reads_only_a_round_past_the_fourth_and_survives_a_wrapper`, replace the tuple assertion with `Assert.Equal(GrimdarkFutureRules.MissionLength, breach!.Rule); Assert.StartsWith(GrimdarkFutureRules.MissionLengthDetail(4), breach.Detail);`.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "ObjectivesTests|MissionLengthTests"`
Expected: the three new tests FAIL (`Winners` empty, no standing in the reason); `MissionLengthTests` PASS (a prefix of itself).

- [ ] **Step 3: The judge reads the standing**

In `server/game/Rules/GrimdarkFuture/GfRules.MissionLength.cs`, replace `JudgeAdvance`:

```csharp
    /// <summary>The winners and the standing are read after round 4's seizures: the round-end step runs before BeginRound proposes round 5 (objectives spec → Where the seize runs).</summary>
    public GameEndingBreach? JudgeAdvance(GameState state, PropertySet properties, IReadOnlyList<EventPayload> proposed) =>
        proposed.OfType<RoundStarted>().FirstOrDefault(r => r.Round > FinalRound) is { } next
            ? new GameEndingBreach(MissionLength, $"{MissionLengthDetail(next.Round - 1)} {GfObjectives.Standing(state)}", GfObjectives.Winners(state))
            : null;
```

and change the class summary's last sentence to add: "p.6: the player that controls most markers wins (GfObjectives)."

- [ ] **Step 4: Run the GameServer suite**

Run: `dotnet test tests/Tabletop.GameServer.Tests`
Expected: PASS.

- [ ] **Step 5: Run `sim/`, whose `strict` runs now end with scored winners**

Run: `dotnet test tests/Tabletop.Sim.Tests && dotnet run --project sim -- final-round --seeds 1..3 --no-recording`
Expected: PASS. `final-round` places no marker, so each run prints `ended` at round 4 with both teams tied at none.

- [ ] **Step 6: Commit**

```bash
git add server/game/Rules/GrimdarkFuture/GfRules.MissionLength.cs tests/Tabletop.GameServer.Tests
git commit -m "Name the team with most markers, or the tie, when the game ends after round four"
```
