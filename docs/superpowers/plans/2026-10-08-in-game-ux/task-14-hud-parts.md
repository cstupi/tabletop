# Task 14: Header, dice, log drawer, end message, key overlay

**Wave:** 3 · **Depends on:** 9, 11a · **Model:** sonnet

**Files:**
- Create: `client/src/Hud/HeaderBar.cs`
- Create: `client/src/Hud/DiceTray.cs`
- Create: `client/src/Hud/LogDrawer.cs`
- Create: `client/src/Hud/EndMessage.cs`
- Create: `client/src/Hud/KeyOverlay.cs`
- Modify: `client/src/Panels/LogPanel.cs` (each roll's dice inline on its row)
- Test: none. These are Godot nodes; their logic is `HeaderView` (Task 9) and `Hotkeys` (Task 11a), tested there.

**Interfaces:**
- Consumes:
  - `HeaderView.Of(Projection p, GameInfo game, Names n) : HeaderModel`; `HeaderModel(int Round, string Turn, IReadOnlyList<(string Team, int Score)> Scores, string? ActiveUnit, string? Action, IReadOnlyList<OutlineItem> Outline, string? Waiting, string? Result)`; `OutlineItem(string Label, OutlineState State)`; `OutlineState { Done, Current, Next }` (Task 9).
  - `Hotkeys.Of(HotKey key, bool shift, bool ctrl) : Intent?`; `Intent`, `HotKey` (Task 11a). `HotKey` member names are
    `Godot.Key` names, so a Godot `Keycode` converts with `Enum.TryParse<HotKey>(keycode.ToString(), out var k)`.
  - `Session.EventsApplied`, `GameChanged`, `BoardChanged`, `Settings`, `UpdateSettings`, `Log`, `Projection`, `Game` (existing); `EventApplier.EffectiveLog` (existing); `LogPanel.Bind(Session, TableView, Action<string>)` (existing).
- Produces (Task 19 wires these; Task 16a calls `KeyOverlay.IntentOf`):
  - Each part: `void Bind(Session session, TableView view, TableInput input)`; unsubscribes in `_ExitTree`; anchors itself in `Bind`.
  - `HeaderBar.TeamMenuRequested : event Action<Guid, Vector2>` (team id, global mouse position; the caller opens `MenuModel.ForTeam(game, p, id).Concat(MenuModel.OffTable(p, id, names))` as `GameScreen.Play` does today), `HeaderBar.GameOverPressed : event Action`.
  - `DiceTray.Follow(Control below)`: keeps the tray above the decision box.
  - `LogDrawer.Reported : event Action<string>` (the log's send errors, for the screen's toast); `LogDrawer.Toggle()`.
  - `EndMessage.Open()`, `EndMessage.Close()`.
  - `KeyOverlay.IntentOf(InputEvent e, Viewport viewport) : Intent?` — the Godot key event to an intent; null for anything else, and while a text field has focus.

Every file uses `using Godot;` and `using Tabletop.Client.Core;`; Core's key enum is `HotKey`, so `Key` stays
`Godot.Key`.

Positions: header top centre, dice bottom centre above the decision box, log button lower right growing upward, end
message and key overlay centre. Each part is added to `GameScreen` (a full-rect `Control`) by Task 19; Task 19 adds
`EndMessage` and `KeyOverlay` last so their `_Input` runs first and they take **Esc** before anything under them.

- [ ] **Step 1: The key overlay and the key conversion**

Create `client/src/Hud/KeyOverlay.cs`. The rows are the spec's *Hotkeys* table.

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Table;

namespace Tabletop.Client.Hud;

/// <summary>Centre: every hotkey and what it does. F1 or ? opens and closes it; Esc closes it.</summary>
public partial class KeyOverlay : PanelContainer
{
    static readonly (string Key, string Does)[] Rows =
    [
        ("1–9", "pick the numbered option in the decision box"),
        ("1–5, while holding a unit", "arrange it in that many rows"),
        ("Q / E, while holding a unit", "turn the arrangement"),
        ("Enter", "confirm the decision: commit, fire, remove"),
        ("Esc", "cancel, innermost first: a drag, the target assignments, the selection"),
        ("Shift (held)", "one model: click selects, drag moves"),
        ("L (held)", "sight check on the hovered unit"),
        ("M", "measure tool"),
        ("P", "ping"),
        ("Tab / Shift+Tab", "next / previous of my units not yet activated"),
        ("Space", "camera to the active unit, else the selected one"),
        ("Ctrl+Z", "undo my last hand move"),
        ("G", "open or close the log"),
        ("Home / End", "camera top-down / level"),
        ("F1 or ?", "this overlay"),
    ];

    public void Bind(Session session, TableView view, TableInput input)
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.Center, LayoutPresetMode.MinSize);
        GrowHorizontal = GrowDirection.Both; GrowVertical = GrowDirection.Both;
        Visible = false;
        var grid = new GridContainer { Columns = 2 }; AddChild(grid);
        foreach (var (key, does) in Rows) { grid.AddChild(new Label { Text = key }); grid.AddChild(new Label { Text = does }); }
    }

    public override void _Input(InputEvent e)
    {
        switch (IntentOf(e, GetViewport()))
        {
            case Intent.KeyOverlay: Visible = !Visible; GetViewport().SetInputAsHandled(); break;
            case Intent.Cancel when Visible: Visible = false; GetViewport().SetInputAsHandled(); break;
        }
    }

    /// <summary>A key press to its intent. HotKey carries the Godot names, so the keycode converts by name. Nothing while a
    /// text field has focus: a menu's number prompt or a spin box takes its own digits and Enter.</summary>
    public static Intent? IntentOf(InputEvent e, Viewport viewport) =>
        e is InputEventKey { Pressed: true, Echo: false } k && viewport.GuiGetFocusOwner() is not (LineEdit or TextEdit)
            && Enum.TryParse<HotKey>(k.Keycode.ToString(), out var key) && Enum.IsDefined(key) ? Hotkeys.Of(key, k.ShiftPressed, k.CtrlPressed) : null;
}
```

`_Input` runs on a hidden node, so the overlay hears **F1** while closed.

- [ ] **Step 2: The header**

Create `client/src/Hud/HeaderBar.cs`. Team names come from `Names.Team`, which is what `HeaderView` writes into
`Scores`, so each team's label finds its score by name.

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Table;
using Tabletop.Protocol;

namespace Tabletop.Client.Hud;

/// <summary>Top centre, three lines: round, turn and scores; the active unit, its action and the step strip; the waiting
/// line. After the end the first line carries "Game over", which reopens the end message.</summary>
public partial class HeaderBar : PanelContainer
{
    static readonly Color Dim = new(1, 1, 1, 0.45f), Lit = new(1f, 0.85f, 0.2f), Ticked = new(0.6f, 0.85f, 0.6f);
    Session _s = null!;
    readonly HBoxContainer _top = new(), _activation = new();
    readonly Label _waiting = new() { Modulate = new Color(1f, 0.8f, 0.4f) };
    readonly Button _gameOver = new() { Flat = true, Visible = false };
    // Session outlives the header: the subscriptions are fields and come off in _ExitTree.
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action? _onChanged;

    public event Action<Guid, Vector2>? TeamMenuRequested;
    public event Action? GameOverPressed;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session;
        SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop, LayoutPresetMode.MinSize, 8);
        GrowHorizontal = GrowDirection.Both;
        var lines = new VBoxContainer(); AddChild(lines);
        lines.AddChild(_top); lines.AddChild(_activation); lines.AddChild(_waiting);
        _top.AddChild(_gameOver);
        _gameOver.Pressed += () => GameOverPressed?.Invoke();
        _onEvents = _ => Render(); _onChanged = Render;
        session.EventsApplied += _onEvents; session.GameChanged += _onChanged; session.BoardChanged += _onChanged;
        Render();
    }

    public override void _ExitTree()
    {
        if (_s is null) return;
        _s.EventsApplied -= _onEvents; _s.GameChanged -= _onChanged; _s.BoardChanged -= _onChanged;
    }

    void Render()
    {
        var p = _s.Projection; var game = _s.Game;
        Visible = p is not null && game is not null && game.Status is GameStatus.Playing or GameStatus.Ended;
        if (!Visible) return;
        var names = new Names(game!, p);
        var m = HeaderView.Of(p!, game!, names);
        Clear(_top, keep: _gameOver); Clear(_activation, keep: null);

        _top.AddChild(new Label { Text = $"Round {m.Round} · {m.Turn}" });
        foreach (var team in game!.Teams.OrderBy(t => t.Ordinal))
        {
            var name = names.Team(team.Id);
            var score = m.Scores.FirstOrDefault(s => s.Team == name).Score;
            var label = new Label { Text = $"  {name} {score}", Modulate = Palette.Team(team.Ordinal), MouseFilter = MouseFilterEnum.Stop, TooltipText = "Right-click: team menu" };
            var id = team.Id;
            label.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } b) TeamMenuRequested?.Invoke(id, b.GlobalPosition); };
            _top.AddChild(label);
        }
        _gameOver.Visible = m.Result is not null;
        _gameOver.Text = m.Result is { } result ? $"  Game over: {result}" : "";
        _top.MoveChild(_gameOver, -1);

        _activation.Visible = m.ActiveUnit is not null;
        if (m.ActiveUnit is { } unit)
        {
            _activation.AddChild(new Label { Text = m.Action is { } action ? $"{unit} · {action}" : unit });
            foreach (var item in m.Outline)
                _activation.AddChild(new Label
                {
                    Text = item.State == OutlineState.Done ? $"  ✓ {item.Label}" : $"  {item.Label}",
                    Modulate = item.State switch { OutlineState.Done => Ticked, OutlineState.Current => Lit, _ => Dim },
                });
        }

        _waiting.Text = m.Waiting ?? "";
        _waiting.Visible = m.Waiting is not null;
    }

    static void Clear(Node box, Node? keep) { foreach (var c in box.GetChildren()) if (c != keep) { box.RemoveChild(c); c.QueueFree(); } }
}
```

- [ ] **Step 3: The dice**

Create `client/src/Hud/DiceTray.cs`. The latest `StepCompleted` with rolls in the effective log, so an undo past a roll
shows the roll before it; independent of `Settings.Animate` and of any timer.

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Table;
using Tabletop.Protocol;

namespace Tabletop.Client.Hud;

/// <summary>Bottom centre, above the decision box: the latest roll's dice and how many passed, until the next roll.</summary>
public partial class DiceTray : PanelContainer
{
    static readonly Color Pass = new(0.35f, 0.8f, 0.4f), Fail = new(0.5f, 0.5f, 0.5f);
    Session _s = null!; Control? _below;
    readonly VBoxContainer _rolls = new();
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action? _onBoard, _place;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session;
        SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom, LayoutPresetMode.MinSize, 8);
        GrowHorizontal = GrowDirection.Both; GrowVertical = GrowDirection.Begin;
        MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_rolls);
        _onEvents = _ => Render(); _onBoard = Render;
        session.EventsApplied += _onEvents; session.BoardChanged += _onBoard;
        Render();
    }

    /// <summary>Sit above the decision box, whatever its height: re-placed when it resizes or shows or hides.</summary>
    public void Follow(Control below)
    {
        _below = below; _place = Place;
        below.Resized += _place; below.VisibilityChanged += _place;
        Place();
    }

    public override void _ExitTree()
    {
        if (_s is not null) { _s.EventsApplied -= _onEvents; _s.BoardChanged -= _onBoard; }
        if (_below is not null && IsInstanceValid(_below)) { _below.Resized -= _place; _below.VisibilityChanged -= _place; }
    }

    void Place()
    {
        var lift = _below is not null && IsInstanceValid(_below) && _below.Visible ? _below.Size.Y + 16 : 8;
        OffsetBottom = -lift;
        OffsetTop = OffsetBottom - GetCombinedMinimumSize().Y;
    }

    void Render()
    {
        foreach (var c in _rolls.GetChildren()) { _rolls.RemoveChild(c); c.QueueFree(); }
        var step = EventApplier.EffectiveLog(_s.Log).Select(e => e.Payload).OfType<StepCompleted>().LastOrDefault(x => x.Rolls.Count > 0);
        Visible = step is not null;
        if (step is null) return;
        // A step may hold several rolls (one pool per Quality value, p.4): one row each, told apart by threshold.
        foreach (var roll in step.Rolls)
        {
            var row = new HBoxContainer(); _rolls.AddChild(row);
            row.AddChild(new Label { Text = roll.Threshold is { } t ? $"{roll.Roll} {t}+" : roll.Roll });
            foreach (var die in roll.Dice) row.AddChild(Face(die));
            var mods = roll.Modifiers.Count == 0 ? "" : "  " + string.Join(", ", roll.Modifiers.Select(m => $"{m.Amount:+#;-#;0} {m.Source}"));
            row.AddChild(new Label { Text = $"  {roll.Dice.Count(d => d.Success)} of {roll.Dice.Count}{mods}" });
        }
        Place();
    }

    static Control Face(Die d)
    {
        var face = new PanelContainer { SelfModulate = d.Success ? Pass : Fail, CustomMinimumSize = new Vector2(28, 28) };
        face.AddChild(new Label { Text = d.Modified == d.Raw ? $"{d.Raw}" : $"{d.Raw}→{d.Modified}", HorizontalAlignment = HorizontalAlignment.Center });
        return face;
    }
}
```

- [ ] **Step 4: The log drawer**

Create `client/src/Hud/LogDrawer.cs`. It hosts `LogPanel` (Step 5) and takes the Animate checkbox from
the side panel.

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Panels;
using Tabletop.Client.Table;

namespace Tabletop.Client.Hud;

/// <summary>Lower right: a button that opens the log upward from the corner. G toggles it.</summary>
public partial class LogDrawer : VBoxContainer
{
    Session _s = null!;
    readonly PanelContainer _drawer = new() { Visible = false, CustomMinimumSize = new Vector2(480, 420) };
    readonly Button _toggle = new() { Text = "Log (G)", SizeFlagsHorizontal = SizeFlags.ShrinkEnd };

    /// <summary>A failed undo or dice edit from the log, for the screen's toast.</summary>
    public event Action<string>? Reported;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session;
        SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight, LayoutPresetMode.MinSize, 8);
        GrowHorizontal = GrowDirection.Begin; GrowVertical = GrowDirection.Begin;
        Alignment = AlignmentMode.End;
        var inner = new VBoxContainer(); _drawer.AddChild(inner);
        var animate = new CheckBox { Text = "Animate", ButtonPressed = session.Settings.Animate };
        animate.Toggled += on => _s.UpdateSettings(x => x with { Animate = on });
        inner.AddChild(animate);
        var log = new LogPanel(); inner.AddChild(log); log.Bind(session, view, message => Reported?.Invoke(message));
        AddChild(_drawer); AddChild(_toggle);
        _toggle.Pressed += Toggle;
    }

    public void Toggle()
    {
        _drawer.Visible = !_drawer.Visible;
        _toggle.Text = _drawer.Visible ? "Close log (G)" : "Log (G)";
    }

    public override void _Input(InputEvent e)
    {
        if (KeyOverlay.IntentOf(e, GetViewport()) != Intent.ToggleLog) return;
        Toggle(); GetViewport().SetInputAsHandled();
    }
}
```

`LogDrawer` subscribes to nothing on `Session`; the hosted `LogPanel` takes its own subscriptions off in its
`_ExitTree`.

- [ ] **Step 5: Each roll's dice in the log**

Spec → *Log*: "the full log as `LogPanel` draws it today …, with each roll's dice inline". `LogText` gives a step's
"k of n succeeded"; the faces go beside it, one group per roll, a passing die in brackets. In
`client/src/Panels/LogPanel.cs`, `Render`, after `row.AddChild(text);`:

```csharp
            if (e.Payload is StepCompleted { Rolls.Count: > 0 } rolled)   // each roll's dice inline (in-game UX spec → Log)
                row.AddChild(new Label { Text = string.Join("   ", rolled.Rolls.Select(r =>
                    $"{r.Roll}: {string.Join(" ", r.Dice.Select(d => d.Success ? $"[{d.Raw}]" : $"{d.Raw}"))}")) });
```

An undone step's row keeps its dice; the row's own text is struck through.

- [ ] **Step 6: The end message**

Create `client/src/Hud/EndMessage.cs`. It opens once when `Projection.Ended` becomes set, closes on ✕, **Esc** or a
click outside its panel, and `Open()` brings it back. An undo past the `GameEnded` event clears it, and a second end
opens it again.

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Table;
using Tabletop.Protocol;

namespace Tabletop.Client.Hud;

/// <summary>Centre: the result, its reason (the mission result or the strict breach's detail) and each team's score.</summary>
public partial class EndMessage : Control
{
    Session _s = null!; bool _opened;
    readonly PanelContainer _panel = new();
    readonly Label _result = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill }, _reason = new();
    readonly VBoxContainer _scores = new();
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action? _onChanged;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore }; centre.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(centre);
        centre.AddChild(_panel);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) }; _panel.AddChild(box);
        var top = new HBoxContainer(); box.AddChild(top);
        top.AddChild(_result);
        var close = new Button { Text = "✕", Flat = true }; close.Pressed += Close; top.AddChild(close);
        box.AddChild(_reason); box.AddChild(_scores);
        _onEvents = _ => Render(); _onChanged = Render;
        session.EventsApplied += _onEvents; session.GameChanged += _onChanged;
        Render();
    }

    public override void _ExitTree()
    {
        if (_s is null) return;
        _s.EventsApplied -= _onEvents; _s.GameChanged -= _onChanged;
    }

    public void Open() { if (Fill()) Visible = true; }
    public void Close() => Visible = false;

    void Render()
    {
        if (_s.Projection?.Ended is null) { _opened = false; Visible = false; return; }
        if (!_opened) { _opened = true; Open(); }
        else if (Visible) Fill();
    }

    bool Fill()
    {
        var p = _s.Projection; var game = _s.Game;
        if (p?.Ended is not { } ended || game is null) return false;
        var names = new Names(game, p);
        var m = HeaderView.Of(p, game, names);
        _result.Text = m.Result ?? "Game over";
        _reason.Text = ended.Reason;
        foreach (var c in _scores.GetChildren()) { _scores.RemoveChild(c); c.QueueFree(); }
        foreach (var team in game.Teams.OrderBy(t => t.Ordinal))
        {
            var name = names.Team(team.Id);
            _scores.AddChild(new Label { Text = $"{name}  {m.Scores.FirstOrDefault(s => s.Team == name).Score}", Modulate = Palette.Team(team.Ordinal) });
        }
        return true;
    }

    /// <summary>The backdrop takes the clicks that miss the panel: a click outside closes the message.</summary>
    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true } b && !_panel.GetGlobalRect().HasPoint(b.GlobalPosition)) { Close(); AcceptEvent(); }
    }

    public override void _Input(InputEvent e)
    {
        if (!Visible || KeyOverlay.IntentOf(e, GetViewport()) != Intent.Cancel) return;
        Close(); GetViewport().SetInputAsHandled();
    }
}
```

- [ ] **Step 7: Build and check**

```bash
dotnet build client/Tabletop.csproj 2>&1 | grep -E "error|warning CS" ; ./scripts/check-client
```

Expected: no output.

- [ ] **Step 8: Commit**

```bash
git add client/src/Hud/HeaderBar.cs client/src/Hud/DiceTray.cs client/src/Hud/LogDrawer.cs client/src/Hud/EndMessage.cs client/src/Hud/KeyOverlay.cs \
  client/src/Panels/LogPanel.cs
git commit -m "Add the header, dice tray, log drawer, end message and key overlay; dice inline in the log

Co-Authored-By: Claude Sonnet <noreply@anthropic.com>"
```

