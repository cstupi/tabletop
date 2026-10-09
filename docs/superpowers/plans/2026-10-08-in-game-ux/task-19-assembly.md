# Task 19: Assembly

**Wave:** 6 · **Depends on:** 14, 15, 16a, 16b, 17, 18 · **Model:** opus

**Files:**
- Modify: `client/src/Screens/GameScreen.cs` (an overlay layer over the table in every status carrying Home, the
  status and the toast; the side panel and the team panels only in lobby and setup)
- Replace: `client/src/Screens/GameScreen.Play.cs` (the Hud parts and table overlays built and wired in place of the
  side panel's Ask panel, log panel and Animate checkbox)
- Delete: `client/src/Panels/AskPanel.cs`, `client/src/Panels/AskPanel.cs.uid`
- Modify: `client/src/Core/AskViewModel.cs` (its summary names the decision box)
- Modify: `client/src/Table/Animator.cs` (the floating roll readout goes: dice are the `DiceTray`'s)

**Interfaces:**
- Consumes:
  - `HeaderBar` (`TeamMenuRequested : Action<Guid, Vector2>`, `GameOverPressed : Action`), `DiceTray.Follow(Control)`,
    `LogDrawer` (`Reported : Action<string>`), `EndMessage.Open()`, `KeyOverlay` (Task 14).
  - `StatSheet` (`ModelPicked : Action<Guid>`, a model id) (Task 15).
  - `DecisionBox` (Task 16a); `DecisionBox.Targets : TargetAssignment?`, `TargetsChanged : Action` (Task 16b).
  - `TableInput.Select(Guid unitId, Guid? modelId)`, `PieceRightClicked`, `TableRightClicked`, `PickNextPiece`,
    `PickNextPoint` (Task 17).
  - `HoverLabels`, `SightLines`, `TargetLines.Show(groups, assigned)`, `TargetLines.Clear()`, `UndoMoveButton` (Task 18).
  - Every part's and overlay's `Bind(Session, TableView, TableInput)`.
  - Existing: `PieceMenu`, `MenuModel.ForTeam`, `MenuModel.OffTable`, `Animator`, `PresenceOverlay`, `Guarded`.
- Produces: nothing new for later tasks.

Spec → *Layout*: "The table fills the window. Every part floats over it … The play-time side panel (`TeamPanel`s,
`AskPanel`, `LogPanel`, the Animate checkbox) is removed from the play screen. Right-click menus (`PieceMenu`,
`MenuModel`) stay as they are; the team menu moves from the team panel's right-click to the header's team names."
README: "Lobby and setup screens keep their current side panel."

**Order of children.** `_Input` runs from the last child back, so what is added last hears a key first. The overlay
layer is added after the table's root container; inside it the parts go in the order header, stat sheet, decision box,
dice, log drawer, end message, key overlay (Task 14: the end message and the key overlay take Esc before anything under
them). Keys the parts leave reach the table's viewport through the `SubViewportContainer`, then `TableInput`.

**Focus.** A clicked `Button` keeps the GUI focus, and the GUI then presses it on Space (`ui_accept`), which is the
camera key. The screen releases the focus of any `BaseButton` that takes it, so no Hud button ever holds it; text
fields and spin boxes keep theirs.

**The waiting line.** `HeaderView.Waiting` (Task 9) is its one source: the header's third line, and the decision
box's title when nothing is addressed to me (Task 16a). `DecisionModel` carries none, so nothing here wires it.

**Coordinates.** In play the side panel is hidden and the `SubViewportContainer` fills the window from its corner, so
the table viewport's screen positions are the window's. `UndoMoveButton` and the hover tag live in `CanvasLayer`s
inside the table viewport and need neither.

No xUnit tests: a Godot screen. The task ends with a clean build, an empty `check-client`, the client test suite
still green, and the launch checklist in Step 6.

- [ ] **Step 1: GameScreen.cs — the overlay layer, the side panel in lobby and setup only**

In `client/src/Screens/GameScreen.cs`, replace the field line

```csharp
    Control? _lobby, _setup, _ask, _log; readonly List<TeamPanel> _teams = new();
```

with

```csharp
    Control? _lobby, _setup; readonly List<TeamPanel> _teams = new();
    Control _hud = null!; Viewport.GuiFocusChangedEventHandler? _onFocus;   // the signal's own delegate type, not Action<Control>
```

In `_Ready`, replace

```csharp
        _side = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) }; root.AddChild(_side);
        var top = new HBoxContainer(); _side.AddChild(top);
        var home = new Button { Text = "Home" }; home.Pressed += () => Guarded.Run(_session.DisconnectAsync, Toast); top.AddChild(home);
        top.AddChild(_status = new Label());
        _side.AddChild(_toast = new Label { Modulate = Colors.Orange });
        _side.AddChild(_selection = new Label());
```

with

```csharp
        _side = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) }; root.AddChild(_side);
        _side.AddChild(_selection = new Label());
        // Over the table in every status, after the root so it draws on top and hears keys first: Home, the status and the
        // toast in the corner. GameScreen.Play adds the play Hud to it.
        _hud = new Control { MouseFilter = MouseFilterEnum.Ignore }; _hud.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(_hud);
        var corner = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; _hud.AddChild(corner);
        corner.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft, LayoutPresetMode.MinSize, 8);
        var top = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; corner.AddChild(top);
        var home = new Button { Text = "Home", FocusMode = FocusModeEnum.None }; home.Pressed += () => Guarded.Run(_session.DisconnectAsync, Toast); top.AddChild(home);
        top.AddChild(_status = new Label());
        corner.AddChild(_toast = new Label { Modulate = Colors.Orange });
        // A Hud button that keeps the focus would be pressed by Space, the camera key: buttons give it straight back.
        _onFocus = c => { if (c is BaseButton) c.ReleaseFocus(); };
        GetViewport().GuiFocusChanged += _onFocus;
```

In `_ExitTree`, after `if (_session is null) return;` add:

```csharp
        if (_onFocus is not null) GetViewport().GuiFocusChanged -= _onFocus;
```

In `Rebuild`, replace

```csharp
        if (game is null) return;
        foreach (var team in game.Teams) { var panel = new TeamPanel(team); _teams.Add(panel); _side.AddChild(panel); }
        RefreshTeams();
        if (game.Status is GameStatus.Lobby or GameStatus.Setup) _side.AddChild(_lobby = new LobbyPanel());
        if (game.Status == GameStatus.Setup) _side.AddChild(_setup = new SetupToolbar(this));
        ShowPlayPanels(game.Status is GameStatus.Playing or GameStatus.Ended);   // Tasks 14–15 fill this in
```

with

```csharp
        if (game is null) return;
        var playing = game.Status is GameStatus.Playing or GameStatus.Ended;
        _side.Visible = !playing;                                      // play is the table to the edges (spec → Layout)
        if (!playing) foreach (var team in game.Teams) { var panel = new TeamPanel(team); _teams.Add(panel); _side.AddChild(panel); }
        RefreshTeams();
        if (game.Status is GameStatus.Lobby or GameStatus.Setup) _side.AddChild(_lobby = new LobbyPanel());
        if (game.Status == GameStatus.Setup) _side.AddChild(_setup = new SetupToolbar(this));
        ShowPlayPanels(playing);
```

Replace the summary of `RefreshAll`:

```csharp
    /// <summary>Teams now; the selection once every other handler of this change has run, since the Ask panel clears highlights when it renders.</summary>
```

with

```csharp
    /// <summary>Teams now; the selection once every other handler of this change has run, since the decision box clears highlights when it renders.</summary>
```

In `client/src/Core/AskViewModel.cs`, replace `/// <summary>What the one Ask panel renders.` with
`/// <summary>What the decision box renders.`

- [ ] **Step 2: GameScreen.Play.cs — the Hud and the overlays**

Replace the whole of `client/src/Screens/GameScreen.Play.cs` with:

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Hud;
using Tabletop.Client.Panels;
using Tabletop.Client.Table;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Screens;

/// <summary>
/// Play (spec → Layout): the Hud parts float over the table, the overlays draw on it, and the events between them meet here.
/// Everything is built once when play starts and freed when the screen leaves play.
/// </summary>
public partial class GameScreen
{
    HeaderBar? _header; StatSheet? _sheet; DecisionBox? _decision; DiceTray? _dice; LogDrawer? _logDrawer; EndMessage? _end; KeyOverlay? _keys;
    HoverLabels? _hover; SightLines? _sight; TargetLines? _targets; CanvasLayer? _tableLayer;
    Animator? _animator; PieceMenu? _menu;
    // Input outlives the play parts: its subscriptions are fields and come off when they go.
    Action<Guid, Vector2>? _onPieceRight; Action<Vector3>? _onTableRight; Action<MenuItem>? _onItem;
    readonly MenuItem _leave = new("Leave the game", MenuInput.None, _ => null);

    partial void ShowPlayPanels(bool playing)
    {
        if (!playing) { FreePlayPanels(); return; }
        if (_header is not null) return;

        // Hud parts, bottom of the input order first: the end message and the key overlay are added last and hear keys first.
        _hud.AddChild(_header = new HeaderBar()); _header.Bind(_session, Table, Input);
        _hud.AddChild(_sheet = new StatSheet()); _sheet.Bind(_session, Table, Input);
        _hud.AddChild(_decision = new DecisionBox()); _decision.Bind(_session, Table, Input);
        _hud.AddChild(_dice = new DiceTray()); _dice.Bind(_session, Table, Input); _dice.Follow(_decision);
        _hud.AddChild(_logDrawer = new LogDrawer()); _logDrawer.Bind(_session, Table, Input);
        _hud.AddChild(_end = new EndMessage()); _end.Bind(_session, Table, Input);
        _hud.AddChild(_keys = new KeyOverlay()); _keys.Bind(_session, Table, Input);

        // Table overlays: Node3D children of the table, and the undo button in a canvas layer of the table's own viewport.
        Table.AddChild(_hover = new HoverLabels()); _hover.Bind(_session, Table, Input);
        Table.AddChild(_sight = new SightLines()); _sight.Bind(_session, Table, Input);
        Table.AddChild(_targets = new TargetLines()); _targets.Bind(_session, Table, Input);
        Table.AddChild(_tableLayer = new CanvasLayer());
        var undo = new UndoMoveButton(); _tableLayer.AddChild(undo); undo.Bind(_session, Table, Input);

        _animator = new Animator(_session, Table, Input);
        _menu = new PieceMenu(_session, Toast); AddChild(_menu);
        _menu.PickPieceRequested += Input.PickNextPiece; _menu.PickPointRequested += Input.PickNextPoint;
        _onItem = item => { if (ReferenceEquals(item, _leave)) Guarded.Run(_session.LeaveAsync, Toast); };   // Leave is a management call, not a command
        _menu.ItemChosen += _onItem;
        _onPieceRight = (id, at) => _menu?.Open(ItemsFor(id, Godot.Input.IsKeyPressed(Key.Shift)), at);
        _onTableRight = at => _menu?.Open([Token(at), _leave], GetViewport().GetMousePosition());
        Input.PieceRightClicked += _onPieceRight; Input.TableRightClicked += _onTableRight;

        // The parts die with the screen, so their own events need no unsubscribing.
        _header.TeamMenuRequested += OpenTeamMenu;
        _header.GameOverPressed += _end.Open;
        _sheet.ModelPicked += SelectModel;
        _decision.TargetsChanged += ShowTargets;
        _logDrawer.Reported += Toast;
    }

    void FreePlayPanels()
    {
        if (Input is not null && IsInstanceValid(Input)) { Input.PieceRightClicked -= _onPieceRight; Input.TableRightClicked -= _onTableRight; }
        _animator?.Dispose(); _animator = null;
        foreach (var node in new Node?[] { _header, _sheet, _decision, _dice, _logDrawer, _end, _keys, _hover, _sight, _targets, _tableLayer, _menu })
            if (node is not null && IsInstanceValid(node)) node.QueueFree();
        _header = null; _sheet = null; _decision = null; _dice = null; _logDrawer = null; _end = null; _keys = null;
        _hover = null; _sight = null; _targets = null; _tableLayer = null; _menu = null;
        _onPieceRight = null; _onTableRight = null; _onItem = null;
    }

    /// <summary>The team menu, from the header's team names (spec → Layout): what the team panel's right-click opened.</summary>
    void OpenTeamMenu(Guid teamId, Vector2 at)
    {
        if (_session.Game is { } game && _session.Projection is { } p)
            _menu?.Open(MenuModel.ForTeam(game, p, teamId).Concat(MenuModel.OffTable(p, teamId, new Names(game, p))).ToList(), at);
    }

    /// <summary>A model clicked in the stat sheet's strip selects that one model on the table (spec → Stat sheet).</summary>
    void SelectModel(Guid modelId)
    {
        if (_session.Projection?.Models.GetValueOrDefault(modelId) is { } model) Input.Select(model.UnitId, modelId);
    }

    void ShowTargets()
    {
        if (_targets is null || !IsInstanceValid(_targets)) return;
        if (_decision?.Targets is { } t) _targets.Show(t.Groups, t.Assigned); else _targets.Clear();
    }

    IReadOnlyList<MenuItem> ItemsFor(Guid id, bool shift)
    {
        var p = _session.Projection; if (p is null) return [];
        if (p.Models.TryGetValue(id, out var model))
        {
            var known = MenuModel.KnownActionNames(p, _session.Log);
            return shift ? MenuModel.ForUnit(p, model.UnitId, known) : MenuModel.ForModel(p, id, known).Concat(MenuModel.ForFrame(p)).ToList();
        }
        return _session.Game is { } game ? MenuModel.ForMarker(game, p, id) : [];
    }

    static MenuItem Token(Vector3 at) => new("Drop a token here", MenuInput.Text,
        label => new PlaceMarker(new Marker(Guid.CreateVersion7(), MarkerKind.Token, WorldFrame.FromGodot(at), new Yaw(0), 1, label, VisualRef.OfBuiltin("chip"))));
}
```

`Key.Shift` is `Godot.Key`: Task 11a's key enum is `HotKey`, so nothing in `Tabletop.Client.Core` competes for the
name. `ItemsFor` and `Token` are unchanged.

`TeamPanel` stays for lobby and setup. `LogPanel` is hosted by `LogDrawer`. `Animator` paces moves under the Animate
setting, which lives in the log drawer; `DiceTray` shows every roll (Step 3).

- [ ] **Step 3: Animator — no roll readout**

Spec → *Dice*: "Dice no longer depend on the Animate setting or a timer." In `client/src/Table/Animator.cs`, `Pump`,
delete the whole block that begins

```csharp
            if (_s.Settings.Animate && e.Payload is StepCompleted { Rolls.Count: > 0 } step)
```

through its closing brace (the `Label3D` over the actor and its 1.5 s timer). The loop then only glides moves.

- [ ] **Step 4: Delete the Ask panel**

```bash
git rm client/src/Panels/AskPanel.cs client/src/Panels/AskPanel.cs.uid
grep -rn "AskPanel" client/src
```

Expected: the grep prints nothing. `TableInput`'s and `AskViewModel`'s comments are reworded in Task 17 and Step 1;
`TableView.cs`'s `ShowExitGhosts` summary ("the Ask panel asks") is reworded in Task 21 with the documents.

- [ ] **Step 5: Build, check, test**

Run: `dotnet build client/Tabletop.csproj 2>&1 | grep -E " error |Build succeeded"`
Expected: `Build succeeded`.

Run: `./scripts/check-client`
Expected: no output, exit 0.

Run: `dotnet test tests/Tabletop.Client.Tests`
Expected: all pass (this task changes no Core logic; the run guards the comment edit and the Core types the screen
now binds).

- [ ] **Step 6: Launch and look**

`docker compose up -d`, then `./scripts/client`. Create a game, add a second participant from a second client logged
in as another account, pick teams and armies, end setup. Check, in order:

1. **Lobby and setup**: the side panel is on the right with the team panels, the lobby panel and (setup) the toolbar;
   Home, the status and the toast sit top left over the table.
2. **Play starts**: the side panel is gone and the table fills the window. The header is top centre (round, turn,
   scores; the waiting line), the log is a button lower right, nothing else shows until an Ask is mine.
3. **Deployment**: each team's zone is drawn (Task 6). The decision box, bottom centre, lists my shelved units
   numbered; **2** picks the second. A click on the table drops its models as ghosts in a row; drag one with Shift,
   press **3** while holding the unit for three rows, **Q** to turn; **Enter** places them. One `move_models` reaches
   the server (game server log), none before Enter.
4. **An activation**: my units glow; clicking one picks it without selecting. The stat sheet, lower left, switches to
   the activating unit's *This round* tab; the header shows the unit, its action and the step strip. Dice show above
   the decision box with each roll and stay until the next.
5. **Move step**: the move-range circle and the hint line `Drag to move · …`; a model dragged past its allowance turns
   red; **Esc** returns the models; **Enter** commits.
6. **Shooting**: weapon-group chips in the box; hovering an enemy draws the sight lines without L and dims my models
   that cannot shoot, with the summary over the target; clicking it aims the groups and draws an orange line from
   their shooters; clicking a chip then a second enemy draws a cyan one; **Enter** fires.
7. **Casualties** (my unit hit): clicking a model marks it, right-click takes a pip back, **Auto** fills, **Enter**
   is disabled until the count matches.
8. **Hover and select**: hovering an enemy shows its name in its team colour; with one of my units selected, the
   distance and the line between the closest bases; multi-wound models show `2/3`. Holding Shift shows
   `Shift: one model` at the cursor. A click on empty table clears the selection and hides the sheet when no
   activation runs.
9. **Sight**: with my unit selected, hold **L** over an enemy behind a building: red lines and the building lit;
   release L: the lines go.
10. **Hand move** (`warn` game): drag one of my activated units 1"; release sends it, the event carries the warning,
    and **Undo move** appears beside the unit; any later event removes it; **Ctrl+Z** before that undoes the move.
11. **Keys**: **Tab** cycles my unactivated units and centres the camera; **Space** centres the active unit; **G**
    opens the log drawer; **F1** the key overlay, **Esc** closes it; **Home**/**End** still tilt the camera.
12. **Header team names**: right-click opens the team menu. Right-clicking a model opens the piece menu as before.
13. **End**: when `game_ended` lands (the mission's end after round 4), the end message covers the centre with the
    result, the reason and the scores; **Esc** closes it; clicking "Game over" in the header reopens it.

Record anything that fails as a finding for the owner with the step number; do not restyle (placeholder art only).

- [ ] **Step 7: Commit**

```bash
git add client/src/Screens/GameScreen.cs client/src/Screens/GameScreen.Play.cs client/src/Core/AskViewModel.cs client/src/Table/Animator.cs
git commit -m "Play screen: the Hud and table overlays replace the side panel in play; delete the Ask panel

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

(`git rm` in Step 4 already staged the deletion.)
