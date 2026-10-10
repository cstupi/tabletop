# In-game UX

Design session, 2026-10-07 to 2026-10-08. The game screen from deployment to the end of the game: what the player
sees, how they act on the table, and the server and protocol changes that flow needs. Placeholder art; no visual
polish. Lobby and setup keep their current screens.

## The owner's requirements

- Use the screen's real estate; the side panel of generic buttons goes.
- Nobody clicks "continue" without knowing what is happening; a stop that carries no decision is removed.
- Clarity through an activation: whose turn, which unit, which step, what just happened, what comes next.
- Hotkeys, including a modifier to select and position individual models, shown on screen.
- No accidental moves.
- A way to check that line of sight is computed correctly.
- Keep the move-range circle.
- Hover a unit: its name, and the distance to it when one of your units is selected.
- Left-click a unit: select it and show its stat sheet in the lower-left corner.

## Flow between steps

The game runs to the next real decision. `WaitsBetweenSteps` (`SequenceInterpreter`'s "Continue: {step}" Ask) stays
as a per-game flag for development and is not offered to players: the client's create-game form drops it and always
sends `false`. Each step's result stays readable in the unit's *This round* tab and in the log; undo and the dice
edits are the correction path.

## Layout

The table fills the window. Every part floats over it:

| Part | Where |
| --- | --- |
| Header | top centre |
| Stat sheet | lower left |
| Decision box, with the hint line under it | bottom centre |
| Dice | above the decision box |
| Log | lower right, minimized to a button |
| End-of-game message | centre, dismissable |

The play-time side panel (`TeamPanel`s, `AskPanel`, `LogPanel`, the Animate checkbox) is removed from the play
screen. Right-click menus (`PieceMenu`, `MenuModel`) stay as they are; the team menu moves from the team panel's
right-click to the header's team names.

## Header

One strip, three lines:

1. Round, whose turn (or phase), and each team's score.
2. The active unit and its action, then the step strip: the action's steps, done ones ticked, the current one lit,
   the rest dimmed.
3. The waiting line: who the game is waiting on and for what (`Red: remove casualties`), from the open Asks.

After the game ends the header shows "Game over" with the result; clicking it reopens the end-of-game message.

The step strip reads the action frame's outline (see *Step outline*): the activation's top frame, the one whose
`SequenceContext.Action` is set. Nested frames (shooting inside Advance, the attack inside shooting) light the
outline step they run under.

## Stat sheet

Lower left, two tabs: **Stats** and **This round**.

**Which unit.** When an activation begins, the sheet switches to the activating unit and its *This round* tab.
Clicking another unit switches the sheet to it, and it stays there until the next activation begins. With no
activation running and nothing selected, the sheet is hidden.

**Stats tab.** From the army snapshot (`ArmyInPlay.Snapshot`) and the projection:

- Team, unit name and model count; Quality and Defense.
- Chips: activated this round (and the action), Shaken, Fatigued, spell tokens, each grant (`GrantAdded`) with its
  source and duration.
- Weapons table: name, count, range, attacks, weapon rules (AP among them).
- Special rules. Hovering a rule shows its `rule_text` from the game's pinned catalog (*Rule text*). A rule with no
  catalog entry is tagged *not automated*: `LiveProperties.Gather` skips it. Until the catalog has loaded, or when its
  fetch failed, no rule is tagged. An entry is taken as played, since many
  rules are played by name in `Rules/GrimdarkFuture/` with no effect rows.
- Models strip: each model with wounds left (`ModelFacts`); a removed model struck through. Clicking a model selects
  it.

**This round tab.** The unit's round, derived in the client from the effective log since the last `RoundStarted`:
the action it took, then one line per result of every frame where it is the actor or a target, in order, each with
its dice. A unit that has not activated reads "Not yet activated" above anything done to it. Lines are built from
`StepCompleted` (rolls, changes, answers) and the existing `LogText` wording.

## Decision box

Bottom centre, rendering the first open Ask addressed to my team (`AskViewModel.Mine`). When nothing is addressed to
me it shows the waiting line instead.

| Ask (legal type) | Decision box | Table |
| --- | --- | --- |
| `confirm`, `end_round` (Confirmation) | the question, **Enter** confirms | — |
| `activate`, `act_or_end_phase`, `shoot_or_not`, `react`, `strike_back`, `cast` (Options) | numbered options, **1–9** picks | `activate`: eligible units glow; clicking one selects it and narrows the box to its options, numbered from 1; an option's only unit answers on a click |
| `move`, `disembark` (MoveRegion) | "Drag to move", **Enter** commits, **Esc** resets | the move-range circle and ghosts, as today |
| `deploy`, `deploy_scouts`, `deploy_ambusher` (PieceSelection of shelved units) | the shelved units as numbered entries | *Deployment placement* |
| `declare_targets` (Assignment) | weapon-group chips (*Shooting targets*) | click targets |
| `remove_casualties` (Allocation) | "Remove N models" or "Assign N wounds", "k of N chosen", **Auto**, **Enter** | click models (*Casualties*) |
| `charge_target`, `takedown_target`, `spell_targets` (PieceSelection) | the prompt | eligible units glow; clicking one picks it |
| `spend_tokens` (TokenSpend) | a stepper per caster, sealed, as today | — |

A refusal shows in the box as an orange line, as `AskPanel` does today.

**Hint line.** Under the box, the keys that apply now, e.g. `Drag to move · Shift: one model · 1–5 while holding:
rows · Enter: commit · Esc: cancel`.

**Dice.** Above the box, the latest roll's dice with its result (`6 hits`) until the next roll. Dice no longer depend
on the Animate setting or a timer. Every roll's dice also appear in the log and in *This round*.

## Log

Lower right, a button by default. **G** or a click opens it upward from the corner: the full log as `LogPanel` draws
it today (undo-to-here, struck-through undone events), with each roll's dice inline. The Animate toggle moves into it.

## Table interaction

**Hover a unit.** Its name floats above it in its team's colour. With one of my units selected, the distance follows
the name (`Red Warriors · 8.4"`), measured between the closest models of the two units, base edge to base edge (p.5),
and a thin line joins those two bases. Each model with more than one wound shows its wounds left above it (`2/3`).

**Select.** Left-click a unit selects it: its models light and the stat sheet shows it. Left-click on empty table or
**Esc** clears the selection. **Shift**+click selects one model. While Shift is held a "Shift: one model" tag shows.

**Target picks.** While an Ask wants a unit picked, eligible units glow, the cursor changes, the hover label reads
"Click to target", and a left-click on an eligible unit answers instead of selecting.

**Moves.** A plain drag moves the whole unit; **Shift**+drag moves one model.

- During my move step (MoveRegion Ask), a drag positions ghosts inside the move-range circle and **Enter** commits,
  as today.
- Outside a move step, a drag is a hand move (`MoveModels`) and follows the game's enforcement setting. Under
  `strict` the drag does not start and the server refuses the command. Under `warn` and `silent` the move is sent on
  release, and an **Undo move** button appears beside the moved unit. It sends `Undo` to the last live event before that
  `ModelsMoved` (an `Undone` marker or an undone event is not a target) and stays until any later event lands in the log, since an undo to that point would take the later
  events with it. **Ctrl+Z** does the same.
- From play on (`Playing` or `Ended`), every objective marker is locked: a drag on it does not start, under every
  enforcement setting. Its right-click menu offers **Unlock**; unlocked, it drags and its drop sends `MoveMarker`, for
  a setup objective as for one placed in play, until **Lock** is chosen. Objective control is judged where the marker
  now stands. A setup objective is never removable. A click on a locked objective still selects it, and hovering it shows "Locked — right-click to unlock". The
  lock is this client's alone and sends nothing. Tokens are never locked, and setup moves every marker freely.

**Formations.** While a unit is held in a drag, **1–5** arranges it in that many rows around the cursor, the models
split as evenly as they go (10 models in 3 rows is 4-3-3). Rows run across the screen as the camera sees it; **Q/E**
turn the arrangement 15°. Bases sit with a fixed gap that keeps unit coherency (p.7). Release drops it as any drag
does: inside a move step it waits for **Enter**, and a model beyond its move distance shows red; outside one it is a
hand move.

**Deployment zones.** Each team's zone is drawn in its team colour through round 0, the phases before the first round
(deployment, then Scouts), and removed when round 1 starts. Today `TableView` syncs `ZoneNode`s from the board in
play as well, yet no zone shows during deployment; the plan's first client task finds why (the zones on the board in
play, or the zone mesh's height and facing over the slab) before building on it.

**Deployment placement.** Once a deploy answer picks a unit, a click on the table drops its shelved models there as
ghosts in a row (`DeployView.Layout`). They drag, **Shift**-drag and take formations like a move, and **Enter** places
them with one `MoveModels` from the shelf. Nothing reaches the server before Enter, so the placement is final under
every enforcement setting.

**Shooting targets** (`declare_targets`). Weapon groups (p.8: one per weapon type) show as chips, `Rifles ×4`,
`Plasma Gun ×1`, all picked to start. Each group may take only a target it can shoot (p.8: in line of sight and in
range of a weapon of that group), from the Ask's per-group list. An enemy glows only when a picked group can shoot it.
Clicking an enemy aims every picked group that can shoot it; the other picked groups keep their assignment. A line in
that target's colour runs from the group's shooters to it. Clicking a chip picks that group alone, to aim it at a second
target; with two targets taken (p.8: up to two), no other unit glows. A group may hold fire (p.8: models "may" shoot):
an aimed chip reads `Rifles ×4 → Red unit 2`, an unaimed one `Plasma Gun ×1 · holds fire`. Models that cannot shoot the hovered or assigned target are
dimmed, from the server's sight answer (*Sight check*). **Enter** fires once at least one group has a target, and the unaimed groups hold fire; **Esc** clears the assignments.

**Casualties** (`remove_casualties`). My hit unit lights. When every offered model has one wound left the box reads
`Remove N models` (N capped at the models offered) and a click marks a model with a red ✕. Otherwise it reads `Assign N wounds`:
on a model with more than one wound each click adds one wound pip and a right-click takes one away; under the Ask's
`MaxTargets` of one (`Assign N wounds to one model`, p.15's Tough order) a click on another model moves the pick there. **Auto**
fills what is left in the Ask's default order. **Enter** confirms and is disabled until the allocation matches the count.

## Sight check

With a unit selected, hold **L** and hover a unit; while `declare_targets` is open, hovering a candidate shows it
without L. The client asks the server and draws the answer:

- a line from every model of the selected unit to every model of the hovered one: green clear, amber clear but
  screened (cover), red blocked, with the blocking terrain piece or model lit;
- a summary, `4 of 5 models can see Red Warriors · 2 of 3 clear lines screened`, and with `declare_targets` open, how
  many models can shoot. Whether the unit is in cover is the rules' (p.11's majority) and the client does not state it.

The answer is the same `Sight` the rules use, so the check tests the server.

## Hotkeys

| Key | Does |
| --- | --- |
| 1–9 | pick the numbered option in the decision box |
| 1–5, while holding a unit | arrange it in that many rows |
| Q / E, while holding a unit | turn the arrangement |
| Enter | confirm the decision: commit, fire, remove |
| Esc | cancel, innermost first: a drag, the target assignments, the selection |
| Shift (held) | one model: click selects, drag moves |
| L (held) | sight check on the hovered unit |
| M | measure tool (existing) |
| Ctrl (held) | measure, as the M tool, shown live to everyone; letting go removes the line |
| P | ping (existing) |
| Tab / Shift+Tab | next / previous of my units not yet activated: select it, camera to it |
| Space | camera to the active unit, else the selected one |
| Ctrl+Z | undo my last hand move, as the **Undo move** button |
| G | open or close the log |
| Home / End | camera top-down / level (existing) |
| F1 or ? | key overlay |

## End of game

When the log ends the game, a message covers the centre: the result (`Blue wins`, `Draw`) and its reason (the mission
result, or the `strict` breach's detail), and each team's score. ✕, **Esc** or a click outside closes it; the table
stays browsable, and the header's "Game over" reopens it.

## Server and protocol changes

### Hand moves under enforcement

`MoveModels` stops bypassing `EnforcementPolicy`. The dispatcher judges a hand move and hands any breach to the
policy, as it does for answers:

- Legal: every relocation moves a model from the shelf to the table, or inside a container the rules allow
  (`IGameRules.MayDeployInside`; Grimdark Future, p.15: a Transport on the table belonging to another unit of the
  actor's team), and the model belongs to the unit the actor's
  team last picked in a deploy answer (`deploy`, `deploy_scouts`, `deploy_ambusher`) while that unit still has models
  on the shelf; this is the state in which `GfRules.Deployment` waits (`HookWaiting`). It covers the client's
  *Deployment placement* and the sim's `HandPlacement`.
- Otherwise a breach, rule `hand_move`, detail `"{unit} moved by hand outside a move step"`, `StrictRefuses(Illegal)`:
  `silent` accepts, `warn` accepts with the warning, `strict` refuses.

The other `Override` commands (wounds, conditions, markers, score, round) are unchanged.

### Sight question

A request and its answer on the game socket, outside the log:

```csharp
// Client → server
public sealed record SightQuery(Guid QueryId, Guid ObserverUnitId, Guid TargetUnitId) : SocketMessage;
// Server → client, to the asker only
public sealed record SightAnswer(Guid QueryId, IReadOnlyList<SightLine> Lines, IReadOnlyList<Guid> CanShoot) : SocketMessage;
public sealed record SightLine(Guid FromModelId, Guid ToModelId, bool Clear, bool Screened, IReadOnlyList<Guid> BlockedBy);
```

The session answers from the current state with `Sight.From(state, observer)`, one `SightLine` per model pair.
`BlockedBy` is `LineOfSightResult.ObstructedBy`. `CanShoot` is the observer's models that may fire at the target
under p.8's *Who can shoot* (line of sight and a weapon in range), from the ruleset. A query appends nothing,
broadcasts nothing and produces no `AtRest`. Any participant may ask about any two units on the table; a unit off the
table gives empty lists.

### Step outline

`SequenceStarted` gains `Outline`, the player-facing labels of the sequence's steps, in order:

```csharp
public sealed record SequenceStarted(Guid FrameId, string Sequence, SequenceContext Context, IReadOnlyList<OutlineStep>? Outline = null) : EventPayload;
public sealed record OutlineStep(string Step, string Label);
```

`IStep` gains an optional label. A step without one is internal and not in the outline. `Step` matches
`StepCompleted.Step`, so the client ticks a step off when its `StepCompleted` arrives. Logs written before the change
carry no outline, and the header then shows the action without a strip.

### Model facts

How many wounds a model takes before it is killed is the ruleset's (Grimdark Future: `Tough(X)`, else 1), so the
client is told rather than reading a rule name:

```csharp
// Server → client
public sealed record ModelFacts(IReadOnlyDictionary<Guid, int> MaxWounds) : SocketMessage;
```

`IGameRules.MaxWounds(GameState, Guid modelId)` answers per model. The session sends `ModelFacts` for every model in
play to a client on connect once the game is playing, and broadcasts it after a batch whenever any value differs
from the last one sent. Hover wounds, the models strip and casualty pips read it.

### Rule text

`GET /v1/properties/{hash}/rules` (management) returns the catalog entries of a pinned set: name, kind,
`takes_parameter`, `rule_text`. A set never changes, so the client fetches it
once per hash when a game opens and caches it beside the asset cache. This is the source of every rule's hover text
and of the *not automated* tag. Filling the catalog's missing army-book rules is a separate work item (handoff,
*Known rule gaps*).

## Client structure

Logic goes in `client/src/Core/`, Godot-free and unit-tested; Godot nodes draw it.

| Core (new) | Does |
| --- | --- |
| `HeaderView` | round, turn, scores, active unit and action, outline progress, waiting line |
| `SheetFocus` | which unit the stat sheet shows, from activation starts and clicks |
| `UnitSheet` | the Stats tab's rows from the snapshot, projection and rule texts |
| `RoundHistory` | a unit's *This round* lines from the effective log |
| `DecisionView` | the decision box's content per Ask mode, numbered options, hint line |
| `TargetAssignment` | weapon groups, picked groups, up to two targets, the `Assignment` answer |
| `CasualtyPick` | the allocation being built, Auto, completeness |
| `Formation` | N rows around a point at a yaw, coherent spacing (geometry from `Tabletop.Geometry`) |
| `HoverView` | name, unit-to-unit distance and its two closest points, per-model wounds |
| `SightSummary` | the sight summary's counts, and the models that cannot shoot the hovered or an assigned target |
| `HandMoveUndo` | the undoable hand move and when it expires |
| `Hotkeys` | key to intent per context |
| `RuleTexts` | the fetched catalog by name |

| Godot (new or changed) | Does |
| --- | --- |
| `Hud` | the play-time overlay; replaces the side panel |
| `HeaderBar`, `StatSheet`, `DecisionBox`, `DiceTray`, `LogDrawer`, `EndMessage`, `KeyOverlay` | the parts above |
| `HoverLabels`, `SightLines`, `TargetLines` | table overlays |
| `TableInput` | selection rules, Shift, held-unit formations, target picks, strict gating, L |
| `AskPanel` | replaced by `DecisionBox` over `DecisionView` |
| `LogPanel` | hosted in `LogDrawer` |

`GameScreen` keeps the side panel for lobby and setup only.

## Testing

- Core types: xUnit in `tests/Tabletop.Client.Tests`, one class per type, against projections built from events.
- Hand moves under enforcement: dispatcher tests for each setting, deployment placement legal under `strict`.
- Sight question: session tests over the socket fixture: lines match `Sight` for a scripted table, nothing appended,
  answer to the asker only.
- Step outline: interpreter tests that `SequenceStarted` carries the labelled steps; replay of a log without them.
- Rule text endpoint: management tests for a seeded set; 404 for an unknown hash.
- End to end over HTTP and the socket (`.superpowers/e2e/`): a game from deployment through an activation with a hand
  move under each setting, a sight query, and the end message's data.
- `./scripts/check-client` and a launch of the client before handing off.

## Out of scope

Visual polish and real art. Lobby and setup screens. Authoring the catalog's missing army-book rules and their
effects. Base shape at import (handoff). Model height stays as imported.
