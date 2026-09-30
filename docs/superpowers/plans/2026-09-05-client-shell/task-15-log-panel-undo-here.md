# Task 15: The log panel with undo-to-here, the right-click menu, animation, Leave

**Files:**
- Create: `client/src/Panels/LogPanel.cs`, `client/src/Panels/PieceMenu.cs`, `client/src/Table/Animator.cs`
- Modify: `client/src/Screens/GameScreen.Play.cs`, `client/src/Screens/GameScreen.cs`, `client/src/Panels/Guarded.cs`

**Interfaces:**
- Consumes: `LogText`, `Names`, `MenuModel` (with `OffTable`), `MenuItem`, `MenuInput`, `AnimationQueue` (Core); `Guarded` (Task 13); `Session` (`Log`, `Projection`, `PendingUndoTo`, `UndoToAsync`, `SendCommandAsync`, `EventsApplied`, `Settings.Animate`, `UpdateSettings`, `LeaveAsync`); `TableView`, `TableInput.PieceRightClicked`, `TeamPanel.RightClicked`, `PieceNode` (Table); `GameEvent`, `ModelsMoved`, `StepCompleted`, `PlaceMarker`, `Marker`, `MarkerKind`, `RerollDice`, `SetDice`, `AddModifier` (Protocol).
- Produces: `partial class LogPanel : PanelContainer` — `Bind(Session, TableView, Action<string> report)`; one row per event in `Session.Log` (`#{seq}  {LogText.Describe}`), newest at the bottom, auto-scrolled; the `undone` markers shown and the undone range greyed and struck through with U+0336 after each character (`EventApplier.EffectiveLog`); clicking a row highlights `LogText.Touched`; "Undo to here" on a live row calls `UndoToAsync(seq)` and greys every later row until `PendingUndoTo` clears; a live row with rolls offers "Reroll all" (`RerollDice(frame, roll, [])`) and "Set die 1…" (`SetDice` of die 0's raw value) **per `RollRecord` on the step**, labelled by roll name and threshold when the step carries more than one, plus a single "+1" (`AddModifier` of +1 on the step's first roll name), which names a roll by name rather than by id and so acts on every roll of that name. An undone row offers no edits. A step carries several whenever the ruleset splits one roll: OPR's `roll-to-hit` takes one pool per Quality value the attacking models bring, because p.4 tests each attack against *"the model's Quality value"* and a joined hero brings its own. `partial class PieceMenu : PopupMenu` — `PieceMenu(Session, Action<string> report)`, `Open(IReadOnlyList<MenuItem>, Vector2 screen)`, events `PickPieceRequested`, `PickPointRequested` and `ItemChosen(MenuItem)` (raised for every chosen item before its command is built), `static AskInteger` and `AskText`: one entry per item; `Integer`/`Text` items pop a one-field dialog, `PieceId` items wait for the next click on a piece, `TablePoint` items for the next click on the table; the built command goes to `Guarded.Command`, and a null build sends nothing; no confirmation dialog anywhere. Wired: right-click on a model → `MenuModel.ForModel` + `ForFrame`; Shift-right-click on a model → `ForUnit`; a marker → `ForMarker`; a team panel → `ForTeam` and `OffTable` (one "Place … on the table, here" or "Take … out of the container, here" per shelf or container model of that team); Alt-right-click on the table → "Drop a token here" (`PlaceMarker` of a `Token` at the point) and "Leave the game" (`LeaveAsync` through `ItemChosen`). `sealed class Animator(Session, TableView) : IDisposable` — subscribes to `EventsApplied`, enqueues into `AnimationQueue`, and drains the queue with `Take(Settings.Animate)` until a glide starts, again when the glide ends: for a `ModelsMoved` (alone or inside a `StepCompleted`) the piece is put back at the relocation's `From` and tweens to the projection's position over 0.4 s, one glide at a time, with a 0.6 s timer that ends a glide whose piece was freed mid-tween; for rolls a transient `Label3D` over the actor unit for 1.5 s carrying the whole readout of **every** roll on the step, one block each, stacked — the roll's name, its threshold, the faces with the passing dice in brackets, and each modifier by source — and no die object of any kind (ruling 5); everything else redraws at once. `Dispose` removes the subscription and sets `_disposed`, which every callback checks. Presence is never animated. An "Animate" checkbox in the side panel writes `Settings.Animate`.

Spec: client shell → The log, undo and the menu (every event with actor and sequence; newest at the bottom; clicking highlights; undo-to-here greys until confirmed; presence never appears; the menu is every property editable, each item one command, no admin mode, no confirmation; Leave is a menu action), Animation (the player's setting; a queue between projection and renderer; may lag, never delays an Ask; presence never animated), Screens → Game (Leave calls `/leave`); resolution → Players roll their own dice (reroll per roll; a hand-added modifier); scheduler → Deployment (every roll result editable from the menu); game flow → Nothing is an invariant; whole-design review → Tokens had no home in the log (a token dropped in play is a logged command).

- [ ] **Step 1: Write `LogPanel.cs`**

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Table;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

public partial class LogPanel : PanelContainer
{
    Session _s = null!; TableView _view = null!; Action<string> _report = _ => { };
    readonly ScrollContainer _scroll = new(); readonly VBoxContainer _rows = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    // Session outlives this panel: the subscriptions are fields and come off in _ExitTree.
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action? _onBoard;

    public void Bind(Session s, TableView view, Action<string> report)
    {
        _s = s; _view = view; _report = report;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        AddChild(_scroll); _scroll.AddChild(_rows); _scroll.SizeFlagsVertical = SizeFlags.ExpandFill; _scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _onEvents = _ => Render(); _onBoard = Render;
        s.EventsApplied += _onEvents; s.BoardChanged += _onBoard;
        Render();
    }

    public override void _ExitTree()
    {
        if (_s is null) return;
        _s.EventsApplied -= _onEvents; _s.BoardChanged -= _onBoard;
    }

    void Send(Command command) => Guarded.Command(_s, command, _report);

    void Render()
    {
        foreach (var c in _rows.GetChildren()) { _rows.RemoveChild(c); c.QueueFree(); }
        var p = _s.Projection; var game = _s.Game;
        if (p is null || game is null) return;
        var names = new Names(game, p);
        var live = EventApplier.EffectiveLog(_s.Log).Select(e => e.Seq).ToHashSet();
        foreach (var e in _s.Log)
        {
            var row = new HBoxContainer(); _rows.AddChild(row);
            var isLive = live.Contains(e.Seq);
            var line = $"#{e.Seq}  {LogText.Describe(e, names)}";
            var text = new Button { Text = !isLive && e.Payload is not Undone ? Strike(line) : line, Flat = true, Alignment = HorizontalAlignment.Left, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            if (!live.Contains(e.Seq) && e.Payload is not Undone) text.Modulate = new Color(0.5f, 0.5f, 0.5f);
            if (_s.PendingUndoTo is { } to && e.Seq > to) text.Modulate = new Color(0.6f, 0.6f, 0.6f);
            text.Pressed += () =>
            {
                if (!IsInstanceValid(_view)) return;
                foreach (var n in _view.Pieces.OfType<PieceNode>()) n.SetHighlight(null);
                foreach (var id in LogText.Touched(e)) _view.PieceAt(id)?.SetHighlight(Colors.Yellow);
            };
            row.AddChild(text);
            if (isLive && e.Payload is not Undone) { var undo = new Button { Text = "Undo to here", Flat = true }; undo.Pressed += () => Guarded.Run(() => _s.UndoToAsync(e.Seq), _report); row.AddChild(undo); }
            if (isLive && e.Payload is StepCompleted { Rolls.Count: > 0 } step)
            {
                // A step holds several rolls whenever the ruleset splits one: OPR's roll-to-hit takes one pool per Quality value the
                // attacking models bring, because p.4 tests each attack against "the model's Quality value" and a joined hero brings
                // its own. Reroll and set-die name a RollRecord, so there is one pair per roll, labelled by threshold when it would
                // otherwise be ambiguous. AddModifier names the roll by name, so it stays one button for the step.
                foreach (var roll in step.Rolls)
                {
                    var which = step.Rolls.Count == 1 ? "" : $" {roll.Roll}{(roll.Threshold is { } t ? $" {t}+" : "")}";
                    var reroll = new Button { Text = $"Reroll all{which}", Flat = true }; reroll.Pressed += () => Send(new RerollDice(step.FrameId, roll.Id, [])); row.AddChild(reroll);
                    var set = new Button { Text = $"Set die 1{which}…", Flat = true }; set.Pressed += () => PieceMenu.AskInteger(this, "Raw value for die 1", v => Send(new SetDice(step.FrameId, roll.Id, [new DieValue(0, v)]))); row.AddChild(set);
                }
                var mod = new Button { Text = "+1", Flat = true }; mod.Pressed += () => Send(new AddModifier(step.FrameId, step.Rolls[0].Roll, 1)); row.AddChild(mod);
            }
        }
        CallDeferred(nameof(ScrollToEnd));
    }

    /// <summary>A Button has no strike style: U+0336 after each character.</summary>
    static string Strike(string s) => string.Concat(s.Select(c => $"{c}\u0336"));

    void ScrollToEnd() { if (IsInstanceValid(_scroll)) _scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue; }
}
```

- [ ] **Step 2: Write `PieceMenu.cs`**

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

/// <summary>The right-click menu: every property editable, each item one ordinary command. No admin mode, no confirmation dialog — the log is the accountability.</summary>
public partial class PieceMenu : PopupMenu
{
    readonly Session _s; readonly Action<string> _report; IReadOnlyList<MenuItem> _items = [];
    public event Action<Action<Guid>>? PickPieceRequested;    // the table resolves the next click to a piece id
    public event Action<Action<Point3>>? PickPointRequested;  // the table resolves the next click to a wire-frame point
    /// <summary>Fired for every chosen item before its command is built; the owner handles the items that are not commands (Leave).</summary>
    public event Action<MenuItem>? ItemChosen;

    public PieceMenu(Session s, Action<string> report) { _s = s; _report = report; IdPressed += OnPressed; }

    public void Open(IReadOnlyList<MenuItem> items, Vector2 screen)
    {
        if (items.Count == 0) return;
        Clear(); _items = items;
        for (var i = 0; i < items.Count; i++) AddItem(items[i].Label, i);
        Position = (Vector2I)screen; Popup();
    }

    void OnPressed(long id)
    {
        var item = _items[(int)id];
        ItemChosen?.Invoke(item);
        switch (item.Input)
        {
            case MenuInput.None: Send(item.Build(null)); break;
            case MenuInput.Integer: AskInteger(this, item.Label, v => Send(item.Build(v.ToString()))); break;
            case MenuInput.Text: AskText(this, item.Label, t => Send(item.Build(t))); break;
            case MenuInput.PieceId: PickPieceRequested?.Invoke(picked => Send(item.Build(picked.ToString()))); break;
            case MenuInput.TablePoint: PickPointRequested?.Invoke(at => Send(item.Build(FormattableString.Invariant($"{at.X},{at.Y},{at.Z}")))); break;
        }
    }

    void Send(Command? command) { if (command is not null) Guarded.Command(_s, command, _report); }

    public static void AskInteger(Node host, string title, Action<int> then)
    {
        var d = new ConfirmationDialog { Title = title }; var spin = new SpinBox { MinValue = -99, MaxValue = 99 }; d.AddChild(spin);
        d.Confirmed += () => then((int)spin.Value); d.VisibilityChanged += () => { if (!d.Visible) d.QueueFree(); };
        host.AddChild(d); d.PopupCentered();
    }

    public static void AskText(Node host, string title, Action<string> then)
    {
        var d = new ConfirmationDialog { Title = title }; var line = new LineEdit(); d.AddChild(line);
        d.Confirmed += () => then(line.Text); d.VisibilityChanged += () => { if (!d.Visible) d.QueueFree(); };
        host.AddChild(d); d.PopupCentered();
    }
}
```

- [ ] **Step 3: Write `Animator.cs` and extend `GameScreen.Play.cs`**

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Table;

/// <summary>The queue between the projection and the renderer. The projection is already current; this only paces what the table plays out.</summary>
public sealed class Animator : IDisposable
{
    readonly Session _s; readonly TableView _view; readonly AnimationQueue _queue = new();
    readonly Action<IReadOnlyList<GameEvent>> _onEvents;
    bool _busy, _disposed; int _generation;

    public Animator(Session s, TableView view)
    {
        _s = s; _view = view;
        _onEvents = events => { _queue.Enqueue(events); Pump(); };
        s.EventsApplied += _onEvents;
    }

    /// <summary>Session outlives the table: the subscription comes off when the panels go.</summary>
    public void Dispose() { _disposed = true; _s.EventsApplied -= _onEvents; }

    /// <summary>The tween's end, or the fallback timer if the piece was freed mid-glide and the tween died with it; whichever comes first wins.</summary>
    void Done(int generation)
    {
        if (_disposed || generation != _generation) return;
        _generation++; _busy = false;
        if (GodotObject.IsInstanceValid(_view)) Pump();
    }

    void Pump()
    {
        if (_disposed || _busy || !GodotObject.IsInstanceValid(_view)) return;
        while (!_busy && _queue.Count > 0)
        foreach (var e in _queue.Take(_s.Settings.Animate))
        {
            List<ModelsMoved> moves = e.Payload switch { ModelsMoved m => [m], StepCompleted s => [.. s.Changes.OfType<ModelsMoved>()], _ => [] };
            foreach (var relocation in moves.SelectMany(m => m.Moves))
                if (_s.Settings.Animate && relocation.To is OnTable to && _view.PieceAt(relocation.ModelId) is { } node && node.IsInsideTree())
                {
                    // The table has already redrawn the piece at its destination, so the glide starts from where it was.
                    if (relocation.From is OnTable from) node.Position = WorldFrame.ToGodot(from.Placement.Position);
                    var target = WorldFrame.ToGodot(to.Placement.Position);
                    _busy = true; var generation = _generation;
                    var tween = node.CreateTween(); tween.TweenProperty(node, "position", target, 0.4); tween.TweenCallback(Callable.From(() => { if (GodotObject.IsInstanceValid(_view)) _view.Redraw(); Done(generation); }));
                    _view.GetTree().CreateTimer(0.6).Timeout += () => Done(generation);
                    return;   // one at a time while animating
                }
            if (_s.Settings.Animate && e.Payload is StepCompleted { Rolls.Count: > 0 } step)
            {
                var actor = _s.Projection?.Stack.FirstOrDefault(f => f.Id == step.FrameId)?.Context.ActorUnitId;
                var anchor = actor is { } a ? _view.Pieces.OfType<PieceNode>().FirstOrDefault(p => p.UnitId == a) : null;
                if (anchor is not null && anchor.IsInsideTree())
                {
                    // The whole readout of a roll, which is all the client ever shows of one — no die is drawn:
                    // the faces, which dice passed in brackets, the threshold, and every modifier by source.
                    // No die is placed on the table and nothing tumbles; Godot physics is not in the scene.
                    // One block per roll on the step, stacked, because a step may hold several: OPR's roll-to-hit rolls one
                    // pool per Quality value the attacking models bring, since p.4 tests each attack against "the model's
                    // Quality value" and a joined hero brings its own. The threshold on each block is what tells them apart.
                    var text = string.Join("\n", step.Rolls.Select(roll =>
                    {
                        var faces = string.Join(" ", roll.Dice.Select(d => d.Success ? $"[{d.Raw}]" : $"{d.Raw}"));
                        var mods = roll.Modifiers.Count == 0 ? "" : "\n" + string.Join(", ", roll.Modifiers.Select(m => $"{m.Amount:+#;-#;0} {m.Source}"));
                        return $"{roll.Roll}{(roll.Threshold is { } t ? $" {t}+" : "")}\n{faces}{mods}";
                    }));
                    var label = new Label3D { Text = text, Position = new Vector3(0, 3, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 64, PixelSize = 0.01f };
                    anchor.AddChild(label); anchor.GetTree().CreateTimer(1.5).Timeout += () => { if (GodotObject.IsInstanceValid(label)) label.QueueFree(); };
                }
            }
        }
        _view.Redraw();
    }
}
```

`Pump` drains the queue in one call until a glide starts, so a batch of events that animates nothing is played out at once rather than one event per `EventsApplied`. While animating, `Take` hands out one event, and a `ModelsMoved` whose piece is on the table starts a glide and returns. The tween's callback and the fallback timer both call `Done` with the glide's generation; whichever comes first advances the generation and pumps again, and the other is ignored. Every callback checks `_disposed` and `IsInstanceValid(_view)`, because the timers outlive the play panels.

`GameScreen.Play.cs` in full, replacing Task 14's version:

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Panels;
using Tabletop.Client.Table;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Screens;

public partial class GameScreen
{
    AskPanel? _askPanel; LogPanel? _logPanel; Animator? _animator; CheckBox? _animate; PieceMenu? _menu;
    // Input outlives the play panels: its subscriptions are fields and come off when they go.
    Action<Guid, Vector2>? _onPieceRight; Action<Vector3>? _onTableRight; Action<MenuItem>? _onItem;
    readonly MenuItem _leave = new("Leave the game", MenuInput.None, _ => null);

    partial void ShowPlayPanels(bool playing)
    {
        if (!playing) { FreePlayPanels(); return; }
        if (_askPanel is null)
        {
            _askPanel = new AskPanel(); _side.AddChild(_askPanel); _askPanel.Bind(_session, Table, Input);
            _logPanel = new LogPanel(); _side.AddChild(_logPanel); _logPanel.Bind(_session, Table, Toast);
            _animator = new Animator(_session, Table);
            _animate = new CheckBox { Text = "Animate", ButtonPressed = _session.Settings.Animate };
            _animate.Toggled += on => _session.UpdateSettings(x => x with { Animate = on });
            _side.AddChild(_animate);
            _menu = new PieceMenu(_session, Toast); AddChild(_menu);
            _menu.PickPieceRequested += Input.PickNextPiece; _menu.PickPointRequested += Input.PickNextPoint;
            _onItem = item => { if (ReferenceEquals(item, _leave)) Guarded.Run(_session.LeaveAsync, Toast); };   // Leave is a management call, not a command
            _menu.ItemChosen += _onItem;
            _onPieceRight = (id, at) => { if (_menu is not null) _menu.Open(ItemsFor(id, Godot.Input.IsKeyPressed(Key.Shift)), at); };
            _onTableRight = at => _menu?.Open([Token(at), _leave], GetViewport().GetMousePosition());
            Input.PieceRightClicked += _onPieceRight; Input.TableRightClicked += _onTableRight;
        }
        // The team panels are new on every Rebuild, so they are wired on every call.
        foreach (var team in _teams)
        {
            var id = team.Team.Id;
            team.RightClicked += at =>
            {
                if (_session.Game is { } game && _session.Projection is { } p) _menu?.Open(MenuModel.ForTeam(game, p, id).Concat(MenuModel.OffTable(p, id, new Names(game, p))).ToList(), at);
            };
        }
    }

    void FreePlayPanels()
    {
        if (Input is not null && IsInstanceValid(Input)) { Input.PieceRightClicked -= _onPieceRight; Input.TableRightClicked -= _onTableRight; }
        _animator?.Dispose(); _animator = null;
        _askPanel?.QueueFree(); _askPanel = null;
        _logPanel?.QueueFree(); _logPanel = null;
        _animate?.QueueFree(); _animate = null;
        _menu?.QueueFree(); _menu = null;
        _onPieceRight = null; _onTableRight = null; _onItem = null;
    }

    IReadOnlyList<MenuItem> ItemsFor(Guid id, bool shift)
    {
        var p = _session.Projection; if (p is null) return [];
        if (p.Models.TryGetValue(id, out var model))
        {
            var known = MenuModel.KnownActionNames(p, _session.Log);
            return shift ? MenuModel.ForUnit(p, model.UnitId, known) : MenuModel.ForModel(p, id, known).Concat(MenuModel.ForFrame(p)).ToList();
        }
        return p.Markers.ContainsKey(id) ? MenuModel.ForMarker(id) : [];
    }

    static MenuItem Token(Vector3 at) => new("Drop a token here", MenuInput.Text,
        label => new PlaceMarker(new Marker(Guid.CreateVersion7(), MarkerKind.Token, WorldFrame.FromGodot(at), new Yaw(0), 1, label, VisualRef.OfBuiltin("chip"))));
}
```

`FreePlayPanels` runs from `ShowPlayPanels(false)` and from `GameScreen._ExitTree`, which gains one line after its unsubscriptions:

```csharp
        FreePlayPanels();
```

`TableInput` outlives the play panels, so its two handlers are fields removed in `FreePlayPanels`. The team panels are rebuilt on every `Rebuild`, so their `RightClicked` is wired on every call. A team panel's menu is `ForTeam` followed by `MenuModel.OffTable`: a shelf or container model has no node to right-click, and without these items the spec's "move to the shelf or back" is unreachable for it. Leave stays a `MenuItem` whose `Build` returns null, so `PieceMenu.Send` sends nothing for it. `PieceMenu` raises `ItemChosen` for every item before building it, and `GameScreen` answers the Leave item with `Guarded.Run(_session.LeaveAsync, Toast)`, with no confirmation.

`Guarded` (Task 13) gains `Command`, the command-send counterpart of `Send`, which `PieceMenu` and `LogPanel` use:

```csharp
    /// <summary>A command send that fails is logged and reported, never thrown into the engine.</summary>
    public static async void Command(Session s, Command command, Action<string> report)
    {
        try { await s.SendCommandAsync(command); }
        catch (Exception e) { GD.PushWarning($"Command send failed: {e.Message}"); report(e.Message); }
    }
```

- [ ] **Step 4: Run it.** Menu edits log with the actor and appear in the log at once on both clients; "Undo to here" greys the later rows and the table shows the predicted state, then the server's `undone` confirms and the undone rows are struck through; a `strict` game refuses nothing from the menu; a token dropped from the table menu appears for both; "Leave the game" removes the seat at once, with no confirmation (the other client's lobby list loses the name), and returns Home; a shelf model is placed from its team panel's menu with the next table click; with Animate on a hand move glides, with it off it snaps. `./scripts/check-client` prints nothing. Commit: `Add the log with undo-to-here, the right-click menu, animation and Leave`.
