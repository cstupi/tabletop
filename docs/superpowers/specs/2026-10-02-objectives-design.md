# Objectives

The engine scores Grimdark Future's core mission. At the end of every round it seizes, contests and keeps
objective markers by p.6's rule, the projection holds who controls each marker, and after round 4 the team
controlling most markers wins: `game_ended` carries the winner or the tied teams under `strict`, and the
`warn` warning names the standing. The manual score counters stay as they are.

Ruled by the owner on 2026-10-02, reversing the game-flow spec's *Scoring* ("no scoring triggers, and no
mission logic evaluating the board"): seizing at the end of each round, the exclusions for Shaken, Aircraft
and units that arrived by Ambush that round, control that persists, contest that makes a marker neutral,
most markers after round 4 wins, the winner on `GameEnded`. The enforcement tree builds it, after the
enforcement engine ([the enforcement spec](2026-10-02-enforcement-engine-design.md)), as a second plan.

## The rule

> At the end of each round, if a unit is within 3" of a marker whilst no enemies are, then it counts as
> being seized. Markers stay under the player's control even if the unit moves away, but if units from both
> sides contest a marker at the end of a round, then it becomes neutral again. After 4 rounds have been
> played, the game ends, and the player that controls most markers wins. Note that unless stated otherwise,
> players don't ever win the game by fully destroying their opponent's army. (GF p.6)

> Shaken units must stay idle, but may strike back counting as fatigued, always fail morale tests, and
> can't seize or contest objectives. (GF p.10)

> Aircraft: ... can't seize or contest objectives ... (GF p.13)

> Ambush: ... Units that deploy via Ambush can't seize or contest objectives on the round they deploy.
> (GF p.13)

> When measuring the distance between two units you always measure from/to the closest model in each
> unit. (GF p.5)

What each part means here:

- **A marker** is a `Marker` with `Kind = Objective`, either placed at setup (`Board.Table.Markers`, the
  snapshot's rows) or placed in play (`Projection.Markers`, written by `marker_placed`). Where both hold
  an id, the projection's wins. `GfObjectives.Markers(state)` is that union, and it is the one list the
  seize, the menu check and the count read. Tokens are not objectives.
- **Within 3"** is the closest living model of the unit on the table, base edge to the marker's centre: the marker is a point
  (owner ruling, 2026-10-25: *"Within 3" of the CENTER of the marker"*, *"the marker is a point"*), so its drawn size changes
  nothing. `Geometry.Distance.EdgeToEdge(model, centre) <= 3 + 1e-6`, the 1e-6 being the geometry library's own distance
  tolerance, which it keeps internal. Height is not measured: markers ignore terrain.
- **A unit that can seize or contest** has a living model on the table, is not Shaken, has no Aircraft
  rule, and did not arrive by Ambush in the round that is ending. A model inside a transport has no
  position (`Inside`), so it is never within 3" of anything.
- **"Both sides"** is more than one team: p.50 plays a free-for-all by "all of the regular rules", and
  p.6 seizes only "whilst no enemies are" within 3", every other team being an enemy. So a free-for-all
  marker with eligible units from any two teams is contested. A marker with eligible units from exactly one team is seized by that team. A marker with
  no eligible unit keeps its controller.
- **Arrived by Ambush that round:** the unit named by a `deploy_ambusher` Ask closed since the round's
  `round_started` (`GrimdarkFutureRules.RoundBoundaryStep` reads the same Asks). Ambushers deploy at the
  start of a round, so this is the round about to end.

## Where the seize runs

At the round boundary, after a player has ended the round and before the next round's `round_started`
is proposed. In `SchedulerBase.Advance`, once `RoundEnded` reports the `end_round` Ask answered:

1. If no `round_ended` is in the log since `round_started` and the round is 1 or more, the scheduler asks
   the ruleset for the round's end:

   ```csharp
   // IGameRules, a default member returning none
   IReadOnlyList<StateChange> OnRoundEnded(GameState state, PropertySet properties, int round) => [];
   ```

   A non-empty answer is proposed as one event, `round_ended`, and the call returns.
2. Otherwise, as today, `BeginRound` proposes the next `round_started`, which the mission-length judge
   reads (enforcement spec, *Where it is detected*).

So at the boundary after round 4 the log reads: `end_round` answered, `round_ended` with round 4's
seizures, then `round_started` for round 5 under `silent`, the `game_over` Ask under `warn` (then
`game_ended` or `round_started` for round 5 by its answers), or `game_ended` with the winners under `strict`. The judge counts markers from a projection that already holds round 4's result, which
is what "after 4 rounds have been played ... the player that controls most markers wins" needs.

`round_ended` is proposed only when control changes. A round in which nothing is seized or contested
writes no event, so a game with no objective markers has the same log it has today, and the checked-in
viewer fixtures (no markers) are not regenerated. The guard on `round_ended` since `round_started`
makes the step run once per round; a second computation would find nothing to change anyway, since
positions do not move at the boundary.

The judge sees `round_ended` as a proposal of its own and raises nothing on it: it carries no
`round_started`.

The pre-round phases (round 0: deployment, scouts) are not rounds; the step does not run for round 0. A
round started from the menu with `set_round` skips the boundary and so skips the seize, as it skips the
`end_round` question: the override path does what it says.

## Events, changes and the projection

All additive.

```csharp
// libs/Tabletop.Protocol/Events.cs
[JsonDerivedType(typeof(RoundEnded), "round_ended")]
/// <summary>The end of a round: the ruleset's changes at that moment (OPR: objectives seized, contested, kept).</summary>
public sealed record RoundEnded(int Round, IReadOnlyList<StateChange> Changes) : EventPayload;

// libs/Tabletop.Protocol/StateChanges.cs, and on EventPayload's list, as ScoreSet is: the menu writes it alone
[JsonDerivedType(typeof(ObjectiveControlSet), "objective_control_set")]
/// <summary>Who controls an objective marker; TeamId null is neutral.</summary>
public sealed record ObjectiveControlSet(Guid MarkerId, Guid? TeamId) : StateChange;

// libs/Tabletop.Protocol/Commands.cs, an override command
[JsonDerivedType(typeof(SetObjectiveControl), "set_objective_control")]
public sealed record SetObjectiveControl(Guid MarkerId, Guid? TeamId) : Command;
```

`Projection` gains `IReadOnlyDictionary<Guid, Guid> Control`: marker id to controlling team, a neutral
marker absent. `EventApplier` applies `round_ended`'s changes in order; `objective_control_set` sets or
removes the entry; `marker_removed` removes it too, so a removed marker counts for nobody. `Undone`
rebuilds as today.

**The menu.** Every property is editable (game-flow spec, *Nothing is an invariant*), so control has an
override: `SetObjectiveControl` is in `CommandClass.Override`, and `MenuTranslation` turns it into
`ObjectiveControlSet` after checking the marker exists (among the setup markers or the projection's)
and the team, when one is named, exists. The
seize step and the menu write the same change through the same applier.

## The seize, in Grimdark Future

`server/game/Rules/GrimdarkFuture/GfObjectives.cs`, a static class, and the `OnRoundEnded` member in a new
partial `GfRules.Objectives.cs`:

```csharp
public static class GfObjectives
{
    public const double SeizeRange = 3;     // p.6

    /// <summary>The objective markers: the setup rows, then those placed in play, the projection's entry winning on a shared id.</summary>
    public static IReadOnlyList<Marker> Markers(GameState state);

    /// <summary>One ObjectiveControlSet per marker whose controller changes this round end; none when nothing changes.</summary>
    public static IReadOnlyList<StateChange> Seize(GameState state, PropertySet props);

    /// <summary>The teams with the most controlled markers, in ordinal order: one is the winner, several are a tie.</summary>
    public static IReadOnlyList<Guid> Winners(GameState state);

    /// <summary>"Markers held: Team 1: 2, Team 2: 1." for the mission-length detail.</summary>
    public static string Standing(GameState state);
}
```

`Seize` goes marker by marker. For each team it asks whether any eligible unit of that team is within
range; the set of such teams decides: one team seizes (a change only if it did not already control the
marker), two or more make the marker neutral (a change only if someone controlled it), none leaves it.
A unit's team is its participant's team (`TeamUnits.Of`). Shaken is `GfNames.Shaken` on the unit;
Aircraft is `Lookup.UnitHas(state, props, unit, GfNames.Aircraft)`.

`Winners` counts each team's entries in `Projection.Control` over `state.Info.Teams` and returns every
team at the maximum. With no marker controlled every team is at none and every team is returned: a tie.

`Standing` names teams by ordinal ("team 1"), because the server holds no team names; the client and
viewer print their own names for the same teams on `game_ended`.

The enforcement spec's `GfRules.MissionLength.cs` then reads `GfObjectives.Winners(state)` for
`GameEndingBreach.Winners` and appends `GfObjectives.Standing(state)` to its detail.

## Under each setting

Seizing is automation, not enforcement: it runs at every round end under `silent`, `warn` and `strict`
alike, and `Projection.Control` holds the standing at every moment of every game.

| Setting | After round 4 |
| --- | --- |
| `silent` | Round 5 starts. The standing is the projection's control; nothing else is written. |
| `warn` | The `game_over` Ask's prompt reads "4 rounds have been played: the game ends after 4 (p.7). Markers held: Team 1: 2, Team 2: 1." End: `game_ended` as under `strict`. Every team's Continue: round 5 starts with that sentence as its `round_started` warning; seizing continues in later rounds and each later boundary asks again with its own standing. |
| `strict` | `game_ended` with `Winners`, and the same sentence as its `Reason`. |

No winner is recorded under `silent`, or under `warn` when every team continues: the game has not ended, so
there is no result to record, and the players decide what round 4's standing means for their game.

## What players see

**The client** (`client/src/`):

- `Table/MarkerNode.cs`: an objective's disc takes its controlling team's `Palette.Team(ordinal)`; a
  neutral one keeps today's colour. `Table/TableView.cs` passes the colour from `Projection.Control` when
  it syncs markers.
- `Panels/TeamPanel.cs`: each team's line shows the markers it holds beside its score.
- `Core/MenuModel.cs`: `ForMarker` takes the game and the projection and, for an objective, adds
  "Give to {team}" per team and "Make neutral", each a `SetObjectiveControl`. Its one caller,
  `Screens/GameScreen.Play.cs`, passes them.
- `Core/LogText.cs`: `round_ended` reads "ended round {n}", and each `objective_control_set`, alone or
  inside `round_ended`, "{team} took {marker}" or "{marker} became neutral". `game_ended` names the
  winner or the tied teams by the client's own team names (enforcement spec, *What clients see*).

**The viewer** (`viewer/src/`):

- `lib/live/apply.ts`: `Projection.control`, `initialProjection` empty; `objective_control_set` and
  `round_ended` applied as the C# applier does; `marker_removed` clears control.
- A frame's markers today are `Projection.Markers` only, so a setup objective appears in no frame and
  the viewer never draws it. `RecordedFrame.Markers` becomes the setup markers then the play markers,
  the play entry winning on a shared id, in `sim/Recording/FrameProjector.cs` and in
  `lib/live/project.ts` alike. `RecordedMarkerState` gains a trailing optional `ControlledBy`
  (`Guid?`) from `Projection.Control`. Null is not written (`RecordingJson` ignores nulls), and the
  checked-in fixtures hold no markers, so they stay byte-identical and `RecordingJson.SchemaVersion`
  stays.
- `lib/board/frame.ts` and `lib/board/draw.ts`: a controlled objective is stroked in its team's
  `teamColour`. `lib/components/Scoreboard.svelte`: markers held per team, counted from the frame's
  marker states.
- `lib/live/logLines.ts` and `sim/Recording/LogLines.cs`, held to one text by the agreement test:
  `round_ended` is "Round {n} ended", `objective_control_set` is "{marker} to Team {n}" or
  "{marker} neutral", where a marker is its label, or the first eight characters of its id.

## The wire

Additive: two `EventPayload` kinds (`round_ended`, `objective_control_set`), one `StateChange` kind
(`objective_control_set`), one `Command` kind (`set_objective_control`), one `Projection` property
(`Control`), one optional recording field (`ControlledBy`), and `GameEnded.Winners` filled. No rename,
no field changes meaning. `ProtocolVersion` stays 1 under the enforcement spec's recorded assumption.
`wire.generated.ts` and `recording.generated.ts` are regenerated; `npm run check` fails until
`apply.ts`, `project.ts` and `logLines.ts` handle the new kinds.

## sim

- **The scored winner.** The enforcement spec replaces `RunOutcome.Decided` with
  `RunOutcome.Ended(Round, Winners)`; here `Winners` is the scored result. `RecordingMeta.WinningTeamId`
  is the sole winner.
- **A catalog scenario that ends with a scored winner**, `objectives`: `strict`, two armies of 3 units of
  3 models (as `skirmish`), three objective markers along the middle of the table and one deep in Blue's
  deployment zone (p.6 places markers "outside of the deployment zones"; this one is inside on purpose), round
  cap 8. Its policy opts into `ObjectiveGoal.Nearest`: each move takes the legal sampled destination nearest an
  objective marker the model's team does not control. Expectations, which hold for every seed: `Outcome("ended")`,
  `ReachedRound(4)`, `EventOfKind("round_ended")` (at least one seize) and `Expectation.MostMarkersWin`,
  asserting `game_ended` names exactly the teams controlling the most objective markers in the final projection,
  every team when none holds one. `Expectation.Won(int TeamOrdinal)` asserts `game_ended` names exactly that
  team; a test pins the uniform policy's seed 1, a seize and a win, with it.
- **Invariant 7, `ControlFollowsTheMarkers`.** Every `objective_control_set` in the effective log names a
  marker the projection holds at that point and, when it names a team, a team of the game. It reads the
  log and the board only.
- **Recording.** `FrameProjector` writes `ControlledBy`; the viewer's agreement test covers it once a
  fixture with markers exists. No new fixture is checked in: the two existing ones hold no markers.

## Files touched

Created: `server/game/Rules/GrimdarkFuture/GfObjectives.cs`, `server/game/Rules/GrimdarkFuture/GfRules.Objectives.cs`,
`tests/Tabletop.GameServer.Tests/ObjectivesTests.cs` (the seize cases: one team, contest, keep,
Shaken, Aircraft, Ambush this round and last, inside a transport, a removed marker; winners and the
tie; the boundary order with the judge under each setting).

Modified:

- `libs/Tabletop.Protocol/Events.cs`, `StateChanges.cs`, `Commands.cs`, `Projection.cs`, `EventApplier.cs`.
- `server/game/Rules/IGameRules.cs` (`OnRoundEnded`), `server/game/Scheduler/SchedulerBase.cs` (the
  step), `server/game/Dispatch/CommandDispatcher.cs` (`Classify`), `server/game/Dispatch/MenuTranslation.cs`,
  `server/game/Rules/GrimdarkFuture/GfRules.MissionLength.cs` (winners and standing),
  `tests/Tabletop.GameServer.Tests/Infrastructure/StubRules.cs` (`WithSequences` forwards `OnRoundEnded`).
- `client/src/Core/LogText.cs`, `Core/MenuModel.cs`, `Screens/GameScreen.Play.cs`, `Table/MarkerNode.cs`,
  `Table/TableView.cs`, `Panels/TeamPanel.cs`.
- `sim/Recording/Recording.cs` (`ControlledBy`), `sim/Recording/FrameProjector.cs`,
  `sim/Recording/LogLines.cs`, `sim/Invariants/Invariants.cs`, `sim/Invariants/ProjectionComparer.cs`
  (`Control`), `sim/Scenarios/Expectation.cs` (`Won`), `sim/Scenarios/ScenarioCatalog.cs`.
- `viewer/src/lib/protocol/wire.generated.ts`, `recording.generated.ts` (regenerated),
  `viewer/src/lib/live/apply.ts`, `project.ts`, `logLines.ts`, `viewer/src/lib/board/frame.ts`,
  `draw.ts`, `viewer/src/lib/components/Scoreboard.svelte`, and `viewer/tests/apply.test.ts`,
  `frame.test.ts`.
- Tests: `tests/Tabletop.Protocol.Tests/EventApplierTests.cs`, `EventTests.cs`, `StateChangeTests.cs`,
  `CommandTests.cs`; `tests/Tabletop.GameServer.Tests/CommandDispatcherTests.cs`;
  `tests/Tabletop.Client.Tests/ViewModelTests.cs`; `tests/Tabletop.Sim.Tests/InvariantTests.cs`,
  `ExpectationTests.cs`, `ScenarioTests.cs`, `RecordingTests.cs`.
- Docs: the game-flow spec's *Scoring* is reversed; `architecture.md` → *Rules enforcement* gains the
  scored mission.

The spell tree touches none of these except `IGameRules.cs`, `Invariants.cs`, `ScenarioCatalog.cs` and the
regenerated TypeScript, as for the enforcement plan.

## Open for the owner

Nothing. The one reading the code needed, a marker reached by two teams in a free-for-all, is the book's:
p.50 plays free-for-all games by "all of the regular rules", and p.6 seizes only "whilst no enemies are"
within 3", where every other team is an enemy. Two teams there contest it, and it goes neutral.
