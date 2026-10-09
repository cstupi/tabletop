# The in-game UX Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The game screen from deployment to the end of the game as the spec settles it: the table to the edges with
a header, a stat sheet, a decision box, dice, a log drawer and an end message floating over it; selection, hover,
formations, target and casualty picking on the table; a sight check the server answers; hand moves under the
enforcement setting.

**Architecture:** Contracts land first (Task 1). Then the server, management and the client's Godot-free core build
in parallel against those contracts and against the interfaces fixed below, each task owning a disjoint set of
files. Godot nodes follow, each a self-contained control or overlay with a `Bind` method, again in parallel. One
assembly task wires them into `GameScreen` and retires the play-time side panel; the last two validate end to end
and level the documents.

**Tech Stack:** `net10.0`, C# 12, xUnit, Godot 4.7.2 Mono (C#), ASP.NET Core minimal APIs, Npgsql/Dapper; the viewer's
generated TypeScript.

**Spec:** [`docs/superpowers/specs/2026-10-08-in-game-ux-design.md`](../../specs/2026-10-08-in-game-ux-design.md), the
whole document.

**Requires landed:** `main` at `b70c068`.

## Global Constraints

- The rules engine lives only in `server/game/`; a rule is named only in `server/game/Rules/GrimdarkFuture/`. The
  client names no rule: `./scripts/check-client` must print nothing. Max wounds, who can shoot and sight come from the
  server.
- `client/src/Core/` is Godot-free and compiled into `tests/Tabletop.Client.Tests`; every type with logic there is
  test-driven.
- Every wire change is additive. `ProtocolVersion.Current` stays `1`. After a change to any type reachable from
  `SocketMessage`: `TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests`, then
  `cd viewer && npm run check && npm test`.
- One world unit is one inch; the table frame's origin is the table's corner, Z up. `WorldFrame` converts to Godot.
- `.tscn` files are editor-owned; new nodes are built in C#, as `GameScreen` builds its panels today.
- Placeholder art only: Godot default theme, `Palette` colours, plain `Label`/`Button`/`PanelContainer`.
- Lobby and setup screens keep their current side panel.
- `WaitsBetweenSteps` stays on the wire and in the schema; only the client's create-game form stops offering it.
- Tests: `dotnet test` per project; server and management tests need `docker compose up -d`.
- Commit after every task, imperative one-line subject, ending with
  `Co-Authored-By: Claude <Model> <noreply@anthropic.com>`, the model the task's row names: `Claude Opus`,
  `Claude Sonnet` or `Claude Haiku`.


## Parallel execution

Tasks in one wave touch disjoint files and may run at once, each in its own subagent. The *Owns* column is the
contract: a task edits only the files it owns, plus new files it creates under the paths named, plus its tests. A
wave starts when every task it depends on has merged.

| Wave | Task | Owns | Depends on | Model |
| --- | --- | --- | --- | --- |
| 1 | [1. Contracts](task-01-contracts.md) | `libs/Tabletop.Protocol/{Wire,Events,Projection,EventApplier}.cs`, viewer generated types, new `client/src/Core/RuleTexts.cs`, `client/src/Core/{ManagementClient,ManagementDtos}.cs` | — | sonnet |
| 2 | [2. Hand moves under enforcement](task-02-hand-moves-under-enforcement.md) | `server/game/Dispatch/`; `tests/Tabletop.GameServer.Tests/{CommandDispatcherTests,ObjectivesTests}.cs` | 1 | opus |
| 2 | [3. Step outline](task-03-step-outline.md) | `server/game/Interpreter/{Steps,SequenceInterpreter}.cs`; the step labels in `server/game/Rules/GrimdarkFuture/{GfMoves,GfMelee,GfShooting}.cs` | 1 | sonnet |
| 2 | [4a. Sight lines, model facts and who can shoot](task-04a-sight-rules.md) | `server/game/Interpreter/Sight.cs`, `server/game/Rules/{IGameRules,IRulesEngine}.cs`, `server/game/Engine/RulesEngine.cs`, new `server/game/Rules/GrimdarkFuture/GfRules.Facts.cs`, `tests/Tabletop.GameServer.Tests/Infrastructure/{StubRules,FakeRules}.cs` | 1 | opus |
| 2 | [5. Rule text endpoint](task-05-rule-text-endpoint.md) | `server/management/Games/{PropertiesEndpoint,GameRepository}.cs` | — | sonnet |
| 2 | [6. Deployment zones in play](task-06-deployment-zones.md) | `client/src/Table/{TableView,ZoneNode}.cs`, `server/game/Session/GameSession.Lobby.cs` | — | opus |
| 2 | [7. Formation](task-07-formation.md) | new `client/src/Core/Formation.cs` | — | sonnet |
| 2 | [8. Hover and distance](task-08-hover-view.md) | new `client/src/Core/HoverView.cs` | 1 | sonnet |
| 2 | [9. Round history and header](task-09-round-history-and-header.md) | new `client/src/Core/{RoundHistory,HeaderView}.cs` | 1 | sonnet |
| 2 | [10. Unit sheet](task-10-unit-sheet.md) | new `client/src/Core/{UnitSheet,SheetFocus}.cs` | 1 | sonnet |
| 2 | [11a. Decision view and hotkeys](task-11a-decision-view-and-hotkeys.md) | new `client/src/Core/{DecisionView,Hotkeys}.cs` | 1 | opus |
| 2 | [11b. Target assignment, casualty pick, hand-move undo](task-11b-targets-casualties-undo.md) | new `client/src/Core/{TargetAssignment,CasualtyPick,HandMoveUndo}.cs` | — | opus |
| 2 | [12. Session: sight, facts, rule texts](task-12-session-additions.md) | `client/src/Core/Session.cs` | 1 | sonnet |
| 2 | [13. Create-game form](task-13-create-game-form.md) | `client/src/Screens/HomeScreen.cs`, `client/src/Panels/LobbyPanel.cs` | — | haiku |
| 3 | [4b. Sight question and model facts over the socket](task-04b-sight-session.md) | `server/game/Session/{GameSession,GameSession.Connect,GameSession.Frames}.cs`, new `GameSession.Facts.cs` | 4a | opus |
| 3 | [14. Header, dice, log drawer, end message, key overlay](task-14-hud-parts.md) | new `client/src/Hud/{HeaderBar,DiceTray,LogDrawer,EndMessage,KeyOverlay}.cs`, `client/src/Panels/LogPanel.cs` | 9, 11a | sonnet |
| 3 | [17. Table input](task-17-table-input.md) | `client/src/Table/TableInput.cs`, new `TableInput.{Keys,Hold}.cs`, `client/src/Table/OrbitCamera.cs` | 7, 9, 11a | opus |
| 4 | [15. Stat sheet](task-15-stat-sheet.md) | new `client/src/Hud/StatSheet.cs` | 9, 10, 12, 17 | sonnet |
| 4 | [16a. Decision box, moves, deployment placement](task-16a-decision-box.md) | new `client/src/Hud/{DecisionBox,DecisionBox.Move}.cs` | 6, 9, 11a, 12, 14, 17 | opus |
| 4 | [18. Table overlays](task-18-table-overlays.md) | new `client/src/Table/{HoverLabels,SightLines,TargetLines,UndoMoveButton}.cs` | 8, 11a, 11b, 12, 17 | sonnet |
| 5 | [16b. Decision box picks](task-16b-decision-picks.md) | new `client/src/Hud/DecisionBox.Picks.cs` | 11b, 16a | opus |
| 6 | [19. Assembly](task-19-assembly.md) | `client/src/Screens/GameScreen*.cs`, `client/src/Table/Animator.cs`, `client/src/Core/AskViewModel.cs`; deletes `AskPanel.cs` | 14, 15, 16a, 16b, 17, 18 | opus |
| 7 | [20. End to end](task-20-e2e.md) | `.superpowers/e2e/` | 2, 3, 4b, 5, 19 | opus |
| 8 | [21. Documents](task-21-documents.md) | `docs/`, the `AGENTS.md` files, the `ShowExitGhosts` summary in `client/src/Table/TableView.cs` | 20 | opus |

Wave 2 is wide: thirteen tasks. A controller runs each wave's tasks as parallel subagents on one worktree only if
their *Owns* sets stay disjoint; otherwise each task gets a short-lived branch merged by the controller in task order.
Tasks 15, 16a and 18 bind `TableInput` members that Task 17 adds, so they follow it a wave later; 16b fills two
partial methods 16a declares.

## Fixed interfaces

Every task below codes against these names. A task that needs a change here stops and reports it rather than
inventing a variant.

### Protocol (Task 1)

```csharp
// Wire.cs — [JsonDerivedType(typeof(SightQuery), "sight_query")], (SightAnswer, "sight_answer"), (ModelFacts, "model_facts")
public sealed record SightQuery(Guid QueryId, Guid ObserverUnitId, Guid TargetUnitId) : SocketMessage;          // client → server
public sealed record SightAnswer(Guid QueryId, IReadOnlyList<SightLine> Lines, IReadOnlyList<Guid> CanShoot) : SocketMessage;   // to the asker
public sealed record SightLine(Guid FromModelId, Guid ToModelId, bool Clear, bool Screened, IReadOnlyList<Guid> BlockedBy);   // BlockedBy: TerrainVolume ids and model ids
public sealed record ModelFacts(IReadOnlyDictionary<Guid, int> MaxWounds) : SocketMessage;                     // server → client

// Events.cs
public sealed record SequenceStarted(Guid FrameId, string Sequence, SequenceContext Context, IReadOnlyList<OutlineStep>? Outline = null) : EventPayload;
public sealed record OutlineStep(string Step, string Label);

// Projection.cs — Frame
public IReadOnlyList<OutlineStep> Outline { get; }   // empty when the event carried none
```

### Server (Tasks 2–4b)

```csharp
// server/game/Interpreter/Steps.cs (Task 3)
public interface IStep { string Name { get; } string? Label => null; StepOutcome Run(StepContext ctx); }
public sealed class LabelledStep(IStep step, string label) : IStep;
public static class StepLabels { public static IStep Labelled(this IStep step, string label); }

// server/game/Rules/IGameRules.cs (Task 4a; default members; every wrapper that forwards IGameRules by hand forwards these)
int MaxWounds(GameState state, Guid modelId) => 1;
IReadOnlyList<Guid> Shooters(GameState state, PropertySet properties, Guid unitId, Guid targetUnitId) => [];

// server/game/Rules/IRulesEngine.cs (Task 4a); RulesEngine returns its own PropertySet
PropertySet? Properties => null;

// server/game/Interpreter/Sight.cs (Task 4a)
public SightLine? Line(Guid fromModel, Guid toModel);   // null when either model is off the table

// server/game/Dispatch/HandMoves.cs (Task 2)
public static class HandMoves { public const string Rule = "hand_move"; public static Breach? Judge(GameState state, MoveModels command, Actor actor); }
```

`SightLine.BlockedBy` names a terrain volume (`TerrainVolume.Id`, not the `TerrainPlacement.Id`) or a model; the
client lights the terrain piece whose `TerrainPlacement.Volumes` holds the id (Task 18).

### Management (Task 5)

```
GET /v1/properties/{hash}/rules  → 200 [{ "name", "kind", "takesParameter", "ruleText" }], 404 for an unknown hash
```

### Client core (Tasks 1, 7–12)

```csharp
// RuleTexts.cs, ManagementClient, ManagementDtos (Task 1)
public sealed record RuleTextDto(string Name, string Kind, bool TakesParameter, string RuleText);
public Task<IReadOnlyList<RuleTextDto>> GetRuleTexts(string propertiesHash);           // ManagementClient
public sealed class RuleTexts { public static RuleTexts From(IReadOnlyList<RuleTextDto> rows); public static RuleTexts Empty { get; }
    public string? TextOf(string name); public bool IsAutomated(string name); }       // automated = has an entry

// Session.cs (Task 12)
public RuleTexts Rules { get; }                                  // Empty until fetched for Game.PropertiesHash
public IReadOnlyDictionary<Guid, int> MaxWounds { get; }         // from the latest ModelFacts; absent id = 1
public event Action? FactsChanged;                               // ModelFacts arrived or rule texts loaded
public Task<Guid> QuerySightAsync(Guid observerUnitId, Guid targetUnitId);
public event Action<SightAnswer>? SightAnswered;

// Formation.cs (Task 7)
public static class Formation { public const double Gap = 0.5;
    public static IReadOnlyList<Point3> Arrange(IReadOnlyList<Base> bases, int rows, Point3 centre, Yaw facing); }

// HoverView.cs (Task 8)
public sealed record HoverInfo(Guid UnitId, string Name, int TeamOrdinal, double? Distance, Point3? From, Point3? To, IReadOnlyList<(Guid ModelId, int Left, int Max)> Wounds);
public static class HoverView { public static HoverInfo? Of(Projection p, GameInfo game, Names n, Guid hoveredUnit, Guid? selectedUnit, IReadOnlyDictionary<Guid, int> maxWounds); }

// RoundHistory.cs, HeaderView.cs (Task 9)
public sealed record HistoryLine(string Text, IReadOnlyList<int> Dice, long Seq);
public static class RoundHistory { public const string NotYetActivated = "Not yet activated";
    public static IReadOnlyList<HistoryLine> For(IReadOnlyList<GameEvent> effectiveLog, Projection p, Names n, Guid unitId); }
public sealed record OutlineItem(string Label, OutlineState State);   public enum OutlineState { Done, Current, Next }
public sealed record HeaderModel(int Round, string Turn, IReadOnlyList<(string Team, int Score)> Scores, string? ActiveUnit, string? Action,
    IReadOnlyList<OutlineItem> Outline, string? Waiting, string? Result);
public static class HeaderView { public static HeaderModel Of(Projection p, GameInfo game, Names n);
    public static Frame? ActionFrame(Projection p);                       // the outermost frame whose Context.Action is set
    public static IReadOnlyList<OutlineItem> Outline(Frame frame);
    public static string? Waiting(Projection p, Names n); }               // the one waiting line: the header's, and the decision box's

// UnitSheet.cs, SheetFocus.cs (Task 10)
public sealed record SheetRule(string Label, string? Text, bool Automated);
public sealed record SheetWeapon(string Name, int Count, string Range, string Attacks, IReadOnlyList<SheetRule> Rules);
public sealed record SheetModel(Guid Id, string Label, int Left, int Max, bool Removed);
public sealed record UnitSheetModel(Guid UnitId, string Team, string Name, int Alive, int Size, string? Quality, string? Defense,
    IReadOnlyList<string> Chips, IReadOnlyList<SheetWeapon> Weapons, IReadOnlyList<SheetRule> Rules, IReadOnlyList<SheetModel> Models);
public static class UnitSheet { public static UnitSheetModel Of(Projection p, Names n, Guid unitId, RuleTexts rules, IReadOnlyDictionary<Guid, int> maxWounds); }
public sealed class SheetFocus { public Guid? UnitId { get; } public void OnActivationStarted(Guid unitId); public void OnClicked(Guid unitId);
    public void OnCleared(); public void OnActivationEnded(); }

// DecisionView.cs, Hotkeys.cs (Task 11a)
public sealed record DecisionModel(Ask? Ask, AskMode? Mode, string Title, IReadOnlyList<(int Key, string Label, string OptionId)> Numbered,
    string Hint, bool AutoPass = false);                                 // no Ask when nothing is mine; the box then shows HeaderView.Waiting
public static class DecisionView { public static DecisionModel Of(Projection p, GameInfo game, Names n, Guid? myTeam, IReadOnlyCollection<string> muted); }
public enum Intent { Option1, Option2, Option3, Option4, Option5, Option6, Option7, Option8, Option9, Confirm, Cancel, SingleModel, Sight,
    Measure, Ping, NextUnit, PreviousUnit, FocusCamera, UndoMove, ToggleLog, CameraTop, CameraLevel, KeyOverlay, TurnLeft, TurnRight }
public enum HotKey { Key1, Key2, Key3, Key4, Key5, Key6, Key7, Key8, Key9, Enter, KpEnter, Escape, Shift, Q, E, L, M, P, G, Z, Tab, Backtab,
    Space, Home, End, F1, Slash, Question }                              // Godot.Key's member names
public static class Hotkeys { public static Intent? Of(HotKey key, bool shift, bool ctrl); }

// TargetAssignment.cs, CasualtyPick.cs, HandMoveUndo.cs (Task 11b)
public sealed class TargetAssignment { public TargetAssignment(Assignment legal);
    public IReadOnlyList<AskOption> Groups { get; } public IReadOnlySet<string> Picked { get; } public IReadOnlyDictionary<string, Guid> Assigned { get; }
    public void PickOnly(string groupId); public bool Aim(Guid targetUnitId); public void Clear(); public bool CanTarget(Guid unitId); public AskAnswer? Answer(); }
public sealed class CasualtyPick { public CasualtyPick(Allocation legal, IReadOnlyDictionary<Guid, int> woundsLeft);
    public IReadOnlyDictionary<Guid, int> Counts { get; } public int Chosen { get; } public int Required { get; } public bool Complete { get; }
    public void Add(Guid modelId); public void Remove(Guid modelId); public void Auto(); public AskAnswer? Answer(); }
public sealed class HandMoveUndo { public long? UndoTo { get; } public IReadOnlyList<Guid> Models { get; }
    public void OnOwnMove(long movedSeq, IReadOnlyList<Guid> models); public void OnEvent(long seq); }   // UndoTo = movedSeq - 1 until a later seq arrives
```

The Godot side maps a key with `Enum.TryParse<HotKey>(godotKey.ToString(), out var k)`; Core names no `Key`, so
`Key` in a file importing both namespaces stays `Godot.Key`.

### Godot (Tasks 6, 14–18)

Every new control or overlay exposes `void Bind(Session session, TableView view, TableInput input)` and unsubscribes
in `_ExitTree`.

```csharp
// TableView (Task 6)
public void ShowGhosts(IReadOnlyList<ModelRelocation> placements);   // deployment placement: one ghost per relocation; ClearExitGhosts clears them

// OrbitCamera (Task 17)
public void Centre(Vector3 target);

// TableInput (Task 17)
public event Action<Guid?>? HoveredUnitChanged;         // unit under the cursor, null off any unit
public event Action<Guid>? UnitClicked;                  // plain left-click on a unit outside a pick; also raised by Select
public event Action<Guid>? TargetClicked;                // left-click on an Eligible piece: the model id if Eligible holds it, else the unit id
public event Action<Guid, bool>? ModelClicked;           // left (false) or right (true) click on a model whose id is in Eligible
public event Action? EligibleChanged;                    // the Eligible setter ran
public event Action? SightHeldChanged;                   // L went down or up
public event Action<IReadOnlyList<Guid>>? HandMoved;     // a hand move's MoveModels went out for these models
public event Action<Intent>? IntentPressed;              // every intent the table does not consume itself (1–9, Enter, Esc past the drag, G, F1/?, Ctrl+Z)
public bool SightHeld { get; }                           // L is down
public bool Holding { get; }                             // a drag is armed
public IReadOnlySet<Guid> Eligible { get; set; }         // set by the decision box: unit ids, model ids or both; a unit id covers its models
public void Select(Guid unitId, Guid? modelId = null);   // as a click would: Tab, the stat sheet's models strip
// kept: Selected, SelectionChanged, Proposing, Allowance, ModelDragged, PieceLeftClicked, PieceRightClicked, TableRightClicked,
//       PickNextPiece, PickNextPoint, CancelDrag, ConsumeOwn

// HeaderBar, DiceTray, LogDrawer, EndMessage, KeyOverlay (Task 14)
public event Action<Guid, Vector2>? TeamMenuRequested;  // HeaderBar: team id, global mouse position
public event Action? GameOverPressed;                    // HeaderBar
public void Follow(Control below);                       // DiceTray: stays above the decision box
public event Action<string>? Reported;                   // LogDrawer: the log's send errors, for the toast
public void Toggle();                                    // LogDrawer
public void Open(); public void Close();                 // EndMessage
public static Intent? IntentOf(InputEvent e, Viewport viewport);   // KeyOverlay: a key press to its intent; null while a text field has focus

// StatSheet (Task 15)
public event Action<Guid>? ModelPicked;                  // a model in the models strip, by id

// DecisionBox (Task 16a: the box; Task 16b: these)
public TargetAssignment? Targets { get; }                // non-null while declare_targets is open
public event Action? TargetsChanged;

// HoverLabels, SightLines, TargetLines, UndoMoveButton (Task 18)
public void Show(IReadOnlyList<AskOption> groups, IReadOnlyDictionary<string, Guid> assigned);   // TargetLines
public void Clear();                                                                               // TargetLines
public static class LineMesh { public static ImmediateMesh Of(IEnumerable<(Vector3 From, Vector3 To, Color Colour)> segments);
    public static StandardMaterial3D Material(); }
```

`TableInput` behaviour the parts rely on: a click on an `Eligible` piece answers and neither selects nor drags; a drag
of a `Proposing` ghost starts from its node's position; under `strict` a model drag starts only when every dragged
model is `Proposing`; a drag arms after 6 px. The Hud parts take keys in `_Input`, before the table's viewport.

## Review Focus

1. **A hand move refused under `strict` during deployment.** The sim's `HandPlacement` and the client's deployment
   placement both send `MoveModels` from the shelf; a `strict` scenario must deploy every unit (Task 2 test
   `Strict_accepts_placing_the_picked_unit_from_the_shelf`, and the sim's `final-round` scenario under `strict`).
2. **The undo button undoing someone else's event.** An undo to before a hand move after a later event lands would
   erase that event; `HandMoveUndo` must drop its target on any later seq (Task 11b test
   `A_later_event_expires_the_undo`).
3. **A log written before the outline.** Replaying old logs must not fail and the header shows no strip (Task 1
   test `SequenceStarted_without_outline_reads_as_empty`, Task 9 test `No_outline_gives_no_strip`).
4. **A wrapper dropping `MaxWounds` or `Shooters`.** `WithSequences` and every hand-forwarding `IGameRules` wrapper
   must forward the new members, or a wrapped Grimdark Future answers 1 and nobody (Task 4a test
   `A_wrapped_Grimdark_Future_still_answers_max_wounds_and_shooters`).
5. **Casualty clicks past a model's wounds.** Adding to a model already at its wounds left must not count; Complete
   only at exactly the amount (Task 11b test `Add_stops_at_wounds_left`).

## Tasks

1. [Contracts](task-01-contracts.md)
2. [Hand moves under enforcement](task-02-hand-moves-under-enforcement.md)
3. [Step outline](task-03-step-outline.md)
4. [4a. Sight lines, model facts and who can shoot](task-04a-sight-rules.md);
   [4b. Sight question and model facts over the socket](task-04b-sight-session.md)
5. [Rule text endpoint](task-05-rule-text-endpoint.md)
6. [Deployment zones in play](task-06-deployment-zones.md)
7. [Formation](task-07-formation.md)
8. [Hover and distance](task-08-hover-view.md)
9. [Round history and header](task-09-round-history-and-header.md)
10. [Unit sheet](task-10-unit-sheet.md)
11. [11a. Decision view and hotkeys](task-11a-decision-view-and-hotkeys.md);
    [11b. Target assignment, casualty pick, hand-move undo](task-11b-targets-casualties-undo.md)
12. [Session: sight, facts, rule texts](task-12-session-additions.md)
13. [Create-game form](task-13-create-game-form.md)
14. [Header, dice, log drawer, end message, key overlay](task-14-hud-parts.md)
15. [Stat sheet](task-15-stat-sheet.md)
16. [16a. Decision box, moves, deployment placement](task-16a-decision-box.md);
    [16b. Decision box picks](task-16b-decision-picks.md)
17. [Table input](task-17-table-input.md)
18. [Table overlays](task-18-table-overlays.md)
19. [Assembly](task-19-assembly.md)
20. [End to end](task-20-e2e.md)
21. [Documents](task-21-documents.md)
