# Task 15: The log panel with undo-to-here, the right-click menu, animation, Leave

**Files:**
- Create: `client/src/Panels/LogPanel.cs`, `client/src/Panels/PieceMenu.cs`, `client/src/Table/Animator.cs`
- Modify: `client/src/Screens/GameScreen.Play.cs`

**Interfaces:**
- Consumes: `LogText`, `Names`, `MenuModel`, `MenuItem`, `MenuInput`, `AnimationQueue` (Core); `Session` (`Log`, `Projection`, `PendingUndoTo`, `UndoToAsync`, `SendCommandAsync`, `EventsApplied`, `Settings.Animate`, `UpdateSettings`, `LeaveAsync`); `TableView`, `TableInput.PieceRightClicked`, `TeamPanel.RightClicked`, `PieceNode` (Table); `GameEvent`, `ModelsMoved`, `StepCompleted`, `PlaceMarker`, `Marker`, `MarkerKind`, `RerollDice`, `SetDice`, `AddModifier` (Protocol).
- Produces: `partial class LogPanel : PanelContainer` — `Bind(Session, TableView)`; one row per event in `Session.Log` (`#{seq}  {LogText.Describe}`), newest at the bottom, auto-scrolled; the `undone` markers shown and the undone range struck through (`EventApplier.EffectiveLog`); clicking a row highlights `LogText.Touched`; "Undo to here" on a row calls `UndoToAsync(seq)` and greys every row above it until `PendingUndoTo` clears; a row with rolls offers "Reroll all" (`RerollDice(frame, roll, [])`) and "Set die…" (`SetDice`) **per `RollRecord` on the step**, labelled by roll name and threshold when the step carries more than one, plus a single "Add modifier…" (`AddModifier`), which names a roll by name rather than by id and so acts on all of them. A step carries several whenever the ruleset splits one roll: OPR's `roll-to-hit` takes one pool per Quality value the attacking models bring, because p.4 tests each attack against *"the model's Quality value"* and a joined hero brings its own. `partial class PieceMenu : PopupMenu` — `Open(IReadOnlyList<MenuItem>, Vector2 screen)`: one entry per item; `Integer`/`Text` items pop a one-field dialog, `PieceId` items wait for the next click on a piece; the built command goes to `SendCommandAsync`; no confirmation dialog anywhere. Wired: right-click on a model → `MenuModel.ForModel` + `ForFrame`; Shift-right-click on a model → `ForUnit`; a marker → `ForMarker`; a team panel → `ForTeam`; Alt-right-click on the table → "Drop a token here" (`PlaceMarker` of a `Token` at the point) and "Leave the game" (`LeaveAsync`). `sealed class Animator(Session, TableView)` — subscribes to `EventsApplied`, enqueues into `AnimationQueue`, and each frame takes `Take(Settings.Animate)`: for a `ModelsMoved` (alone or inside a `StepCompleted`) the piece tweens from its drawn position to the projection's over 0.4 s; for rolls a transient `Label3D` over the actor unit for 1.5 s carrying the whole readout of **every** roll on the step, one block each, stacked — the roll's name, its threshold, the faces with the passing dice in brackets, and each modifier by source — and no die object of any kind (ruling 5); everything else redraws at once. Presence is never animated. An "Animate" checkbox in the side panel writes `Settings.Animate`.

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
    Session _s = null!; TableView _view = null!; readonly ScrollContainer _scroll = new(); readonly VBoxContainer _rows = new();

    public void Bind(Session s, TableView view)
    {
        _s = s; _view = view;
        AddChild(_scroll); _scroll.AddChild(_rows); _scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        s.EventsApplied += _ => Render(); s.BoardChanged += Render;
        Render();
    }

    void Render()
    {
        foreach (var c in _rows.GetChildren()) c.QueueFree();
        var p = _s.Projection; var game = _s.Game;
        if (p is null || game is null) return;
        var names = new Names(game, p);
        var live = EventApplier.EffectiveLog(_s.Log).Select(e => e.Seq).ToHashSet();
        foreach (var e in _s.Log)
        {
            var row = new HBoxContainer(); _rows.AddChild(row);
            var text = new Button { Text = $"#{e.Seq}  {LogText.Describe(e, names)}", Flat = true, Alignment = HorizontalAlignment.Left, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            if (!live.Contains(e.Seq) && e.Payload is not Undone) text.Modulate = new Color(0.5f, 0.5f, 0.5f);
            if (_s.PendingUndoTo is { } to && e.Seq > to) text.Modulate = new Color(0.6f, 0.6f, 0.6f);
            text.Pressed += () => { foreach (var n in _view.Pieces.OfType<PieceNode>()) n.SetHighlight(null); foreach (var id in LogText.Touched(e)) _view.PieceAt(id)?.SetHighlight(Colors.Yellow); };
            row.AddChild(text);
            if (live.Contains(e.Seq) && e.Payload is not Undone) { var undo = new Button { Text = "Undo to here", Flat = true }; undo.Pressed += () => _ = _s.UndoToAsync(e.Seq); row.AddChild(undo); }
            if (e.Payload is StepCompleted { Rolls.Count: > 0 } step)
            {
                // A step holds several rolls whenever the ruleset splits one: OPR's roll-to-hit takes one pool per Quality value the
                // attacking models bring, because p.4 tests each attack against "the model's Quality value" and a joined hero brings
                // its own. Reroll and set-die name a RollRecord, so there is one pair per roll, labelled by threshold when it would
                // otherwise be ambiguous. AddModifier names the roll by name, so it stays one button for the step.
                foreach (var roll in step.Rolls)
                {
                    var which = step.Rolls.Count == 1 ? "" : $" {roll.Roll}{(roll.Threshold is { } t ? $" {t}+" : "")}";
                    var reroll = new Button { Text = $"Reroll all{which}", Flat = true }; reroll.Pressed += () => _ = _s.SendCommandAsync(new RerollDice(step.FrameId, roll.Id, [])); row.AddChild(reroll);
                    var set = new Button { Text = $"Set die 1{which}…", Flat = true }; set.Pressed += () => PieceMenu.AskInteger(this, "Raw value for die 1", v => _ = _s.SendCommandAsync(new SetDice(step.FrameId, roll.Id, [new DieValue(0, v)]))); row.AddChild(set);
                }
                var mod = new Button { Text = "+1", Flat = true }; mod.Pressed += () => _ = _s.SendCommandAsync(new AddModifier(step.FrameId, step.Rolls[0].Roll, 1)); row.AddChild(mod);
            }
        }
        CallDeferred(nameof(ScrollToEnd));
    }

    void ScrollToEnd() => _scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue;
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
    readonly Session _s; IReadOnlyList<MenuItem> _items = [];
    public event Action<Action<Guid>>? PickPieceRequested;    // the table resolves the next click to a piece id
    public event Action<Action<Point3>>? PickPointRequested;  // the table resolves the next click to a wire-frame point

    public PieceMenu(Session s) { _s = s; IdPressed += OnPressed; }

    public void Open(IReadOnlyList<MenuItem> items, Vector2 screen)
    {
        Clear(); _items = items;
        for (var i = 0; i < items.Count; i++) AddItem(items[i].Label, i);
        Position = (Vector2I)screen; Popup();
    }

    void OnPressed(long id)
    {
        var item = _items[(int)id];
        switch (item.Input)
        {
            case MenuInput.None: Send(item.Build(null)); break;
            case MenuInput.Integer: AskInteger(this, item.Label, v => Send(item.Build(v.ToString()))); break;
            case MenuInput.Text: AskText(this, item.Label, t => Send(item.Build(t))); break;
            case MenuInput.PieceId: PickPieceRequested?.Invoke(picked => Send(item.Build(picked.ToString()))); break;
            case MenuInput.TablePoint: PickPointRequested?.Invoke(at => Send(item.Build(FormattableString.Invariant($"{at.X},{at.Y},{at.Z}")))); break;
        }
    }

    void Send(Command? command) { if (command is not null) _ = _s.SendCommandAsync(command); }

    public static void AskInteger(Node host, string title, Action<int> then)
    {
        var d = new ConfirmationDialog { Title = title }; var spin = new SpinBox { MinValue = -99, MaxValue = 99 }; d.AddChild(spin);
        d.Confirmed += () => then((int)spin.Value); host.AddChild(d); d.PopupCentered();
    }

    public static void AskText(Node host, string title, Action<string> then)
    {
        var d = new ConfirmationDialog { Title = title }; var line = new LineEdit(); d.AddChild(line);
        d.Confirmed += () => then(line.Text); host.AddChild(d); d.PopupCentered();
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
public sealed class Animator
{
    readonly Session _s; readonly TableView _view; readonly AnimationQueue _queue = new();
    public Animator(Session s, TableView view) { _s = s; _view = view; s.EventsApplied += events => { _queue.Enqueue(events); Pump(); }; }

    void Pump()
    {
        foreach (var e in _queue.Take(_s.Settings.Animate))
        {
            List<ModelsMoved> moves = e.Payload switch { ModelsMoved m => [m], StepCompleted s => [.. s.Changes.OfType<ModelsMoved>()], _ => [] };
            foreach (var relocation in moves.SelectMany(m => m.Moves))
                if (_s.Settings.Animate && relocation.To is OnTable to && _view.PieceAt(relocation.ModelId) is { } node)
                {
                    var target = WorldFrame.ToGodot(to.Placement.Position);
                    var tween = node.CreateTween(); tween.TweenProperty(node, "position", target, 0.4); tween.TweenCallback(Callable.From(() => { _view.Redraw(); Pump(); }));
                    return;   // one at a time while animating
                }
            if (_s.Settings.Animate && e.Payload is StepCompleted { Rolls.Count: > 0 } step)
            {
                var actor = _s.Projection?.Stack.FirstOrDefault(f => f.Id == step.FrameId)?.Context.ActorUnitId;
                var anchor = actor is { } a ? _view.Pieces.OfType<PieceNode>().FirstOrDefault(p => p.UnitId == a) : null;
                if (anchor is not null)
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
                    anchor.AddChild(label); anchor.GetTree().CreateTimer(1.5).Timeout += label.QueueFree;
                }
            }
        }
        _view.Redraw();
    }
}
```

In `GameScreen.Play.cs`, `ShowPlayPanels(true)` also creates `_logPanel = new LogPanel()` (bound to `_session`, `Table`), `_animator = new Animator(_session, Table)`, an "Animate" `CheckBox` bound to `_session.Settings.Animate` → `UpdateSettings`, and `_menu = new PieceMenu(_session)`; it wires `Input.PieceRightClicked += (id, at) => _menu.Open(ItemsFor(id, shift: Godot.Input.IsKeyPressed(Key.Shift)), at)`, each `TeamPanel.RightClicked += at => _menu.Open(MenuModel.ForTeam(game, p, team.Id), at)`, `Input.TableRightClicked += at => _menu.Open([Token(at), Leave], GetViewport().GetMousePosition())`, and the two deferred inputs: `_menu.PickPieceRequested += Input.PickNextPiece` and `_menu.PickPointRequested += Input.PickNextPoint`. `ItemsFor(id)` is `MenuModel.ForModel(p, id, MenuModel.KnownActionNames(p, _session.Log)).Concat(MenuModel.ForFrame(p))` for a model (`ForUnit` with Shift), `ForMarker(id)` for a marker in `p.Markers`. `Token(at)` is `new MenuItem("Drop a token here", MenuInput.Text, label => new PlaceMarker(new Marker(Guid.CreateVersion7(), MarkerKind.Token, WorldFrame.FromGodot(at), new Yaw(0), 1, label, VisualRef.OfBuiltin("chip"))))`; `Leave` is `new MenuItem("Leave the game", MenuInput.None, _ => null)` whose selection calls `_session.LeaveAsync()` (handled in `GameScreen`, since it is not a command).

- [ ] **Step 4: Run it.** Menu edits log with the actor and appear in the log at once on both clients; "Undo to here" greys the rows above and the table shows the predicted state, then the server's `undone` confirms; a `strict` game refuses nothing from the menu; a token dropped from the table menu appears for both; "Leave the game" removes the seat (the other client's lobby list loses the name) and returns Home; with Animate on a hand move glides, with it off it snaps. `./scripts/check-client` prints nothing. Commit: `Add the log with undo-to-here, the right-click menu, animation and Leave`.
