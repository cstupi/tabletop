# Task 21: Documents

**Wave:** 8 · **Depends on:** 20 · **Model:** opus

**Files:**
- Modify: `docs/design/client/design.md` (the play screen, the decision box in place of the Ask panel, hand moves
  under enforcement, the undo-move button, the built-in meshes' Y-up authoring)
- Modify: `docs/design/game-server/design.md` (the sight query, model facts, the step outline, the `hand_move` breach)
- Modify: `docs/design/contracts/design.md` (three socket kinds, `sequence_started`'s outline)
- Modify: `docs/schema.md` (`waits_between_steps` is not offered to players; `rule_text` is served)
- Modify: `client/AGENTS.md`, `server/game/AGENTS.md`, `libs/Tabletop.Protocol/AGENTS.md`, `server/management/AGENTS.md`
- Modify: `docs/handoff.md`
- Modify: `client/src/Table/TableView.cs` (the `ShowExitGhosts` summary names the decision box)

**Interfaces:** none. The documents state what Tasks 1–20 built, in the present tense, with no history (repo rule:
"State the current decision; never append history").

Each edit is a script of exact replacements that fails loudly when an anchor is missing, so a document changed since
this plan was written stops the step rather than being half-edited. The client and game-server design docs are over
1300 lines; the edits are bounded replacements and one inserted section each, not a rewrite.

- [ ] **Step 1: The client design doc**

The script writes *The play screen* and *The decision box* in place of the *The Ask panel* heading. Inside the
decision box section and the passages that point at it, "the panel" reads "the box".

```bash
python3 - <<'PY'
p = 'docs/design/client/design.md'
s = open(p).read()
PLAY_SCREEN = """From `playing` on the side panel goes and the table fills the window (in-game UX spec → Layout). `GameScreen`
keeps the side panel, the team panels and the selection line for lobby and setup. An overlay `Control` added after the
table carries Home, the status and the toast in every status, and in play `GameScreen.Play` adds the Hud to it: each
part a control in `client/src/Hud/` with `Bind(Session, TableView, TableInput)`, drawing a Godot-free model.

| Part | Where | Draws |
| --- | --- | --- |
| `HeaderBar` | top centre | `HeaderView`: round, turn, scores; the active unit, its action and the step strip from the action frame's `Outline`; the waiting line; "Game over" with the result. A team name's right-click opens the team menu |
| `StatSheet` | lower left | *Stats* (`UnitSheet`: weapons, rules with their `rule_text`, *not automated* for a rule with no catalog entry, the models strip from `ModelFacts`) and *This round* (`RoundHistory`), for the unit `SheetFocus` names |
| `DecisionBox` | bottom centre | the first open Ask addressed to me (below) |
| `DiceTray` | above the decision box | the latest roll and its result until the next |
| `LogDrawer` | lower right | `LogPanel`, each roll's dice inline on its row, and the Animate toggle; G opens it |
| `EndMessage` | centre | the result, the reason and the scores once `game_ended` lands |
| `KeyOverlay` | centre | every hotkey; F1 or ? |

The table overlays are children of the `TableView`. `HoverLabels`: the hovered unit's name in its team colour, the
edge-to-edge distance from my selected unit with a line between the two closest bases (`HoverView`), wounds left over
each multi-wound model, the Shift tag. `SightLines`: the server's `SightAnswer` while L is held over a unit, or while a
target assignment is open; green clear, amber screened, red blocked with the blocker lit, a summary over the target,
and under a target pick the models that cannot shoot dimmed. `TargetLines`: each assigned weapon group's shooters to
its target, one colour per target. `UndoMoveButton`, in a canvas layer of the table's viewport: beside my last hand
move until any later event lands (`HandMoveUndo`).

The Hud parts take keys in `_Input`, which runs before the `SubViewportContainer` hands a key to the table's
viewport, and leave the digits and Esc to the table while a drag is in hand. Hud buttons give the GUI focus straight
back, so Space never presses one. Max wounds, who can shoot and sight come from the server; the client names no
rule."""

def rep(old, new, count=1):
    global s
    assert s.count(old) >= 1, f"missing anchor: {old[:70]!r}"
    s = s.replace(old, new) if count == 0 else s.replace(old, new, count)

rep("| One game screen over four statuses; lobby panel, setup toolbar, team panels | `client/src/Screens/`, `client/src/Panels/` |",
    "| One game screen over four statuses; the side panel (lobby panel, setup toolbar, team panels) in lobby and setup, the Hud in play | `client/src/Screens/`, `client/src/Panels/`, `client/src/Hud/` |")
rep("| Input and presence: select, drag, drop, ruler, measure, ping, others' cursors and ghosts | `client/src/Table/TableInput.cs`, `PresenceOverlay.cs` |",
    "| Input and presence: select, picks, drag, formations, drop, ruler, measure, ping, hotkeys, others' cursors and ghosts | `client/src/Table/TableInput*.cs`, `PresenceOverlay.cs` |\n"
    "| Table overlays: hover labels, sight lines, target lines, the undo-move button | `client/src/Table/{HoverLabels,SightLines,TargetLines,UndoMoveButton}.cs` |")
rep("| The Ask panel, the declared move's region and ghost, containment | `client/src/Panels/AskPanel.cs`, `client/src/Core/MoveRuler.cs` |",
    "| The Hud: header, stat sheet, decision box, dice, log drawer, end message, key overlay; the declared move's region and ghost, containment | `client/src/Hud/`, `client/src/Core/MoveRuler.cs` |")
rep('  ask["AskPanel"]\n  log["LogPanel"]\n  table["TableView + TableInput + PresenceOverlay"]',
    '  hud["Hud: HeaderBar, StatSheet, DecisionBox, DiceTray,<br/>LogDrawer, EndMessage, KeyOverlay"]\n'
    '  table["TableView + TableInput + PresenceOverlay<br/>+ HoverLabels, SightLines, TargetLines, UndoMoveButton"]')
rep('  gamescr --> ask\n  gamescr --> log\n', '  gamescr -- "playing, ended" --> hud\n')
rep('view["TableView, AskPanel, LogPanel, TeamPanel"]', 'view["TableView, the Hud parts, TeamPanel"]')
rep("""Hand moves arrange models and never end a turn, per the scheduler. That is the plain drag. The
declared move belongs to the Ask panel. While one is open the panel fills `TableInput.Proposing`,
so the same drag streams `proposed` ghosts and the release sends nothing.""",
"""A plain drag of a model outside a move step is a hand move: one `MoveModels` on release, which never ends a turn.
The server judges it under the game's enforcement setting (`HandMoves.Judge`, rule `hand_move`): placing the unit
picked at a deploy Ask from the shelf is legal, and anything else is a breach that `silent` accepts, `warn` accepts
with the warning on the event, and `strict` refuses. Under `strict` the drag does not start and the click only
selects. A drag arms after 6 px, so a click never moves anything. The declared move and the deployment placement
belong to the decision box: while one is open it fills `TableInput.Proposing`, the same drag streams `proposed`
ghosts, the release sends nothing, and Enter commits. While a unit is held, 1–5 arrange it in that many rows about
the cursor (`Formation.Arrange`), the rows across the screen as the camera sees it, and Q/E turn them 15°.

A click selects a unit and Shift+click one model; a click on empty table or Esc clears. While a decision wants a
pick (`TableInput.Eligible`), a click on an eligible piece answers through `TargetClicked` or `ModelClicked` and
neither selects nor drags. Tab and Shift+Tab select my next unit with no action this round and centre the camera on
it; Space centres the active unit. Keys go through `Hotkeys.Of`; the table acts on its own and raises
`IntentPressed` for the rest.""")
rep("""colour-only material, and authored in the unit box in the table frame: x and y in [−0.5, 0.5], z in
[0, 1], base at z = 0. `MeshLoader.LoadBuiltin` scales that box""",
"""colour-only material, and authored glTF Y-up in a unit box: the footprint on x and z in [−0.5, 0.5], the height on
y in [0, 1], base at y = 0. `BuiltinContent.MeshOrientation` (pitch 90) reads it into the table frame, z up, the value
the web app writes for a Y-up asset, and `MeshLoader.LoadBuiltin` scales that box""")
rep("""Clicking a row highlights `LogText.Touched(event)`. "Undo to here" on a row is the only undo
affordance. It calls `UndoToAsync(seq)` and greys every row above the target until `PendingUndoTo`
clears. There is no undo button, no undo stack UI and no redo.""",
"""The panel lives in `LogDrawer`, a button lower right that G or a click opens upward; the Animate toggle is in it.
Clicking a row highlights `LogText.Touched(event)`. "Undo to here" on a row calls `UndoToAsync(seq)` and greys every
row above the target until `PendingUndoTo` clears. The other undo affordance is **Undo move** after a hand move
(*The play screen*). There is no undo stack UI and no redo.""")
rep("| right-click a team panel | `ForTeam`", "| right-click a team name in the header | `ForTeam`")
rep("offers the same item from its team's panel,", "offers the same item from its team's menu,")
rep("""Confirm gates (wait-between-steps) are one button in the same panel, labelled by `ConfirmLabel`:
  "Continue" for a step gate, "End the round" for the scheduler's `end_round` Ask.""",
"""A `Confirmation` Ask is its question and **Enter**, labelled by `ConfirmLabel`: "End the round" for
  the scheduler's `end_round` Ask. The interpreter's step gate (`WaitsBetweenSteps`, "Continue: {step}") is a
  development flag: the create-game form does not offer it and sends `false`, so players never meet it.""")
rep("""`strict` refusals show in the panel. A `Refused` whose `MessageId` matches the panel's last
  command prints "the server refused: outside the legal set".""",
"""A refusal shows in the box as an orange line. A `Refused` whose `MessageId` matches the box's last
  command prints "The server refused: {detail}".""")
rep("""### The Ask panel

One panel, always in the same place, rendering whatever Ask is currently addressed to the player's
team.""", """### The play screen

PLAY_SCREEN

### The decision box

One box, bottom centre over the table, rendering whatever Ask is currently addressed to the player's
team through `DecisionView`, with the keys that apply in a hint line under it; with nothing addressed to me it
shows the waiting line.""")
# The decision box section and the passages that point at it: "panel" was the Ask panel.
start = s.index("### The decision box"); end = s.index("### The log panel")
section = s[start:end]
for a, b in [("The panel", "The box"), ("the panel's", "the box's"), ("the panel", "the box"), ("same panel", "same box"), ("player's panel", "player's box")]:
    section = section.replace(a, b)
s = s[:start] + section + s[end:]
for a, b in [("so what the panel sends", "so what the decision box sends"), ("the panel must clear them", "the decision box must clear them"),
             ("and the panel says so", "and the decision box says so"), ("in the panel's note", "in the box's note"),
             ("the Ask panel shows an in-panel line", "the decision box shows an orange line"),
             ("(`if (_askPanel is not null) return`)", "(`if (_header is not null) return`)"),
             ("`client/src/Panels/AskPanel.cs`", "`client/src/Hud/DecisionBox*.cs`"), ("AskPanel.Render", "DecisionBox.Render"),
             ("The Ask panel's", "The decision box's"), ("the one Ask panel", "the one decision box"), ("the Ask panel", "the decision box"),
             ("AskPanel", "DecisionBox")]:
    s = s.replace(a, b)
s = s.replace("PLAY_SCREEN", PLAY_SCREEN)
open(p, 'w').write(s)
PY
grep -n "AskPanel\|Ask panel\|PLAY_SCREEN" docs/design/client/design.md
```

Expected: the grep prints nothing.

- [ ] **Step 2: The game server and contracts design docs, the schema**

```bash
python3 - <<'PY'
def edit(p, pairs):
    s = open(p).read()
    for old, new in pairs:
        assert old in s, f"{p}: missing anchor {old[:70]!r}"
        s = s.replace(old, new, 1)
    open(p, 'w').write(s)

ping_row = "| `PingMessage` | relayed at once, sender stamped | never | never | immediately, except back to the sender | every status |"
edit('docs/design/game-server/design.md', [
 (ping_row, ping_row + "\n| `SightQuery` | `GameSession.Frames` → `Sight.From(state, observer).Line` per model pair, `IGameRules.Shooters` | never | never | one `SightAnswer` to the asker alone, no `AtRest` | every status (empty lists before play) |"),
 ("""**Menu edits are accepted under every setting.** They never reach the policy.
`CommandDispatcher.Classify` puts the fifteen menu and hand-move commands in
`CommandClass.Override`, and `Dispatch` sends that class straight to `MenuTranslation`.""",
 """**Menu edits are accepted under every setting; a hand move is judged.** `CommandDispatcher.Classify` puts the
fifteen menu and hand-move commands in `CommandClass.Override`, and `Dispatch` sends that class to
`MenuTranslation`. A `MoveModels` goes first through `HandMoves.Judge(state, command, actor)` and
`Decide(Enforcement, Breach?)`. Every relocation moving a model of the unit the actor's team last picked at a deploy
Ask (`deploy`, `deploy_scouts`, `deploy_ambusher`) from the shelf to the table, while that unit still has models on
the shelf, is the placement the scheduler waits for and is legal. Anything else is the breach `hand_move`, detail
"{unit} moved by hand outside a move step", `StrictRefuses(Illegal)`: `silent` accepts, `warn` accepts with the
warning on the event, `strict` refuses. The other override commands never reach the policy."""),
 ("### Enforcement and the override path\n", """### The sight query, model facts and the step outline

Three additions serve the play screen, none of them in the log. A `SightQuery(QueryId, ObserverUnitId, TargetUnitId)`
is answered from the current state with the same `Sight` the rules use: one `SightLine` per model pair, `BlockedBy`
the obstructing volumes (a terrain volume's id or a model's), and `CanShoot` the observer's models that p.8's *Who can
shoot* lets fire at the target (`IGameRules.Shooters`). It goes to the asker alone; nothing is appended, broadcast or
followed by `AtRest`. `ModelFacts(MaxWounds)` is `IGameRules.MaxWounds` for every model in play (Grimdark Future:
`Tough(X)`, else 1), sent to a client on connect once the game is playing and broadcast after a batch whenever a value
differs from the last facts sent. `SequenceStarted.Outline` lists a sequence's labelled steps (`IStep.Label`) in order;
a step with no label is internal, and a log written before outlines replays with none. Every `IGameRules` wrapper that
forwards by hand forwards `MaxWounds` and `Shooters`.

### Enforcement and the override path
"""),
])

edit('docs/design/contracts/design.md', [
 ('    K -->|"PresenceMessage or PingMessage"| PR["Presence value or ping"]\n',
  '    K -->|"PresenceMessage or PingMessage"| PR["Presence value or ping"]\n    K -->|"SightQuery"| SQ["SightAnswer to the asker alone:<br/>never logged, no at_rest"]\n'),
 ("`StepCompleted(FrameId, Step, Answer, Rolls, Changes) { Values }` |",
  "`StepCompleted(FrameId, Step, Answer, Rolls, Changes) { Values }`; `SequenceStarted(FrameId, Sequence, Context, Outline?)`, the outline the sequence's player-facing steps as `OutlineStep(Step, Label)`, null in logs written before it |"),
 ("`command`, `edit` |", "`command`, `edit`, `sight_query` (`SightQuery(QueryId, ObserverUnitId, TargetUnitId)`) |"),
 ("`rejected` (`Rejected(RejectReason)`) |",
  "`rejected` (`Rejected(RejectReason)`), `sight_answer` (`SightAnswer(QueryId, Lines, CanShoot)`, to the asker alone), `model_facts` (`ModelFacts(MaxWounds)`) |"),
 ("Thirteen kinds.", "Sixteen kinds."),
 ("relayed rather than processed and never produce one.",
  "relayed rather than processed and never produce one, and a `sight_query` is answered without being processed into the log, so it produces none either."),
])

edit('docs/schema.md', [
 ("a per-player client setting and not here.",
  "a per-player client setting and not here. The client's create-game form does not offer `waits_between_steps` and\nsends `false`; the column serves development."),
 ("set out — attachment points, with a name belonging to exactly one of them.",
  "set out — attachment points, with a name belonging to exactly one of them. `GET /v1/properties/{hash}/rules`\nserves a set's entries with their `rule_text`, the client's hover text; a rule with no entry is tagged *not automated*."),
])
PY
```

- [ ] **Step 3: The AGENTS files**

```bash
python3 - <<'PY'
def edit(p, pairs):
    s = open(p).read()
    for old, new in pairs:
        assert old in s, f"{p}: missing anchor {old[:70]!r}"
        s = s.replace(old, new, 1)
    open(p, 'w').write(s)

edit('client/AGENTS.md', [
 ("  - `AnimationQueue`, `MoveRuler`, `DeployView`, `SelectionView`, `PendingDrops`, `PresenceTable`, `MenuModel`, `LogText`, `Names`.",
  "  - `AnimationQueue`, `MoveRuler`, `DeployView`, `SelectionView`, `PendingDrops`, `PresenceTable`, `MenuModel`, `LogText`, `Names`.\n"
  "  - The play screen's models: `HeaderView`, `RoundHistory`, `UnitSheet`, `SheetFocus`, `DecisionView`, `TargetAssignment`, `CasualtyPick`, `HandMoveUndo`, `HoverView`, `Formation`, `Hotkeys` (`HotKey`, `Intent`), `RuleTexts`."),
 ("- `src/Table/`: the 3D table: `TableView`, `TableInput`,",
  "- `src/Hud/`: the play Hud: `HeaderBar`, `StatSheet`, `DecisionBox` (+ `.Move`, `.Picks`), `DiceTray`, `LogDrawer`, `EndMessage`, `KeyOverlay`.\n"
  "- `src/Table/`: the 3D table: `TableView`, `TableInput` (+ `.Keys`, `.Hold`), `HoverLabels`, `SightLines`, `TargetLines`, `UndoMoveButton`,"),
 ("- `src/Panels/`: `AskPanel`, `LobbyPanel`,", "- `src/Panels/`: `LobbyPanel`,"),
 ("`AskPanel` is the one panel in a fixed place; it renders the mode", "`DecisionBox`, bottom centre over `DecisionView`, renders the mode"),
 ("release sends nothing, and the Ask panel commits.",
  "release sends nothing, and the decision box commits on Enter. Outside one a drag is a hand move, judged under the game's enforcement; under `strict` it does not start.\n"
  "- Play: `GameScreen.Play` builds the Hud over the table and the overlays in it; lobby and setup keep the side panel. Hud parts take keys in `_Input`, before the table's viewport; `TableInput` maps the rest through `Hotkeys.Of` and raises `IntentPressed`. Max wounds, sight and who can shoot come from the server (`ModelFacts`, `SightAnswer`); rule texts from `GET /v1/properties/{hash}/rules`."),
])

edit('server/game/AGENTS.md', [
 ("- `Dispatch/`: `CommandDispatcher`, `EnforcementPolicy` (silent/warn/strict),",
  "- `Dispatch/`: `CommandDispatcher`, `EnforcementPolicy` (silent/warn/strict), `HandMoves` (the `hand_move` breach),"),
 ("gated to the actor's team.", "gated to the actor's team. The client never offers the flag; it serves development."),
 ("- `Windows` offers interrupting reactions",
  "- A `MoveModels` goes through `HandMoves.Judge` and `EnforcementPolicy.Decide`: placing the unit the actor's team last picked at a deploy Ask from the shelf is legal; anything else is `hand_move`, which `silent` accepts, `warn` warns and `strict` refuses.\n"
  "- `SightQuery` is answered to the asker alone from `Sight.Line` per model pair and `IGameRules.Shooters`: nothing appended or broadcast, no `AtRest`. `ModelFacts` (`IGameRules.MaxWounds`) goes to a client on connect once playing, and to everyone after a batch that changes a value.\n"
  "- `IStep.Label` puts a step in its sequence's outline; `SequenceStarted.Outline` carries the labelled steps in order.\n"
  "- `Windows` offers interrupting reactions"),
 ("- `WaitsBetweenSteps` is the per-game flag behind every \"Continue\" Ask (`SequenceInterpreter.cs`).",
  "- `WaitsBetweenSteps` is the per-game flag behind every \"Continue\" Ask (`SequenceInterpreter.cs`). Players never see it.\n"
  "- Every `IGameRules` wrapper that forwards by hand (`WithSequences`, the test fakes) forwards `MaxWounds` and `Shooters`, or a wrapped ruleset answers 1 and nobody."),
])

edit('libs/Tabletop.Protocol/AGENTS.md', [
 ("`PresenceMessage`, `PingMessage`, `PresenceEnded`),",
  "`PresenceMessage`, `PingMessage`, `PresenceEnded`, `SightQuery`, `SightAnswer` with `SightLine`, `ModelFacts`),"),
 ("- `Events.cs`, `StateChanges.cs`: `EventPayload`, `GameEvent`, `StateChange` (the log's content).",
  "- `Events.cs`, `StateChanges.cs`: `EventPayload`, `GameEvent`, `StateChange` (the log's content). `SequenceStarted.Outline` (`OutlineStep`) is null in logs written before it, and `Frame.Outline` is then empty."),
 ("- The server answers with events, `AtRest` and `Refused`.",
  "- The server answers with events, `AtRest` and `Refused`; a `SightQuery` with a `SightAnswer` to the asker alone, outside the log. `ModelFacts` follow the batches that change them."),
])

edit('server/management/AGENTS.md', [
 ("`PropertiesEndpoint` (`GET /v1/properties`).",
  "`PropertiesEndpoint` (`GET /v1/properties`; `GET /v1/properties/{hash}/rules`, a pinned set's catalog entries with `rule_text`, 404 for an unknown hash)."),
])
PY
```

- [ ] **Step 4: The handoff**

Run `dotnet test 2>&1 | grep -E "^(Passed|Failed)!"` with the stack up and note each project's passed count, then:

```bash
python3 - <<'PY'
import datetime
p = 'docs/handoff.md'
s = open(p).read()
pairs = [
 (s.splitlines()[0], f"# Handoff — {datetime.date.today():%Y-%m-%d}"),
 ("""**What it lacks is a usable in-game client.** The
owner played an activation in the Godot client and could not tell what was happening. The work now is the in-game UX,
then polish.""",
  """The in-game client is
built to the in-game UX spec and waits for the owner to play it (*Next* 1); polish follows."""),
 ("""### 1. The in-game UX

Spec: `docs/superpowers/specs/2026-10-08-in-game-ux-design.md`, settled with the owner on 2026-10-08 and waiting for
their review of the written spec. On approval, `superpowers:writing-plans`. Mockups from the session are in
`.superpowers/brainstorm/5808-1791512643/content/`.""",
  """### 1. The owner plays an activation

The in-game UX is built (spec `docs/superpowers/specs/2026-10-08-in-game-ux-design.md`, plan
`docs/superpowers/plans/2026-10-08-in-game-ux/`): the table to the edges with the header, stat sheet, decision box,
dice, log drawer and end message over it; selection, hover, formations, and target and casualty picks on the table;
the sight check, answered by the server; hand moves under the enforcement setting, with **Undo move**. The server side
is checked by `.superpowers/e2e/` (`E2E_SCENARIO=ingame`, once per setting) and the sim's `final-round` scenarios.
What needs the owner's eyes is the rendering and the input: the launch list in the plan's Task 19, Step 6."""),
 ("""
`docs/design/client/design.md` still says the built-ins are authored Z-up; correct it to Y-up at pitch 90.""", ""),
 ("(2) the in-game UX spec's\n`GET /v1/properties/{hash}/rules` delivers", "(2)\n`GET /v1/properties/{hash}/rules` delivers"),
]
for old, new in pairs:
    assert old in s, f"missing anchor {old[:70]!r}"
    s = s.replace(old, new, 1)
open(p, 'w').write(s)
PY
```

Then edit the **Checks** paragraph's `dotnet test passes 1358 with the stack up: Geometry 157, …` sentence to the
total and per-project counts just noted, in the same order and form, and its build-warning count to what
`dotnet build` prints now.

- [ ] **Step 5: Superseded facts**

`TableView.cs`'s `ShowExitGhosts` summary names the deleted panel:

```bash
sed -i 's/so the Ask panel asks for one node per model/so the decision box asks for one node per model/' client/src/Table/TableView.cs
grep -rn "AskPanel\|Ask panel\|authored Z-up\|There is no undo button\|right-click a team panel\|the only undo" \
  docs client server libs AGENTS.md --include='*.md' --include='*.cs' | grep -v "docs/superpowers/\|docs/design/viewer/"
grep -rn "\"Continue\"\|side panel" docs/design client/AGENTS.md server/game/AGENTS.md docs/handoff.md | grep -v "docs/design/viewer/"
```

Expected: the first grep prints nothing (the viewer's `AskPanel.svelte` is the viewer's own component and stays). The
second prints only lines that say the side panel is lobby and setup's, and that the "Continue" step gate is a
development flag players never see. Any other hit is a stale fact: rewrite it to the current behaviour in place.

Run `dotnet build client/Tabletop.csproj 2>&1 | grep -E " error |Build succeeded"` (a comment changed) and
`./scripts/check-client`: `Build succeeded`, no output.

- [ ] **Step 6: Commit**

```bash
git add docs/design/client/design.md docs/design/game-server/design.md docs/design/contracts/design.md docs/schema.md \
  docs/handoff.md client/AGENTS.md server/game/AGENTS.md libs/Tabletop.Protocol/AGENTS.md server/management/AGENTS.md \
  client/src/Table/TableView.cs
git commit -m "Documents: the play screen, the sight query, model facts, the step outline and hand moves under enforcement

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```
