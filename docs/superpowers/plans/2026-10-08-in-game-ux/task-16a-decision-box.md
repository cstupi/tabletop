# Task 16a: Decision box, moves and deployment placement

**Wave:** 4 · **Depends on:** 6, 9, 11a, 12, 14, 17 · **Model:** opus

**Files:**
- Create: `client/src/Hud/DecisionBox.cs` — the box, the render per mode, keys, sending, refusals, reveals.
- Create: `client/src/Hud/DecisionBox.Move.cs` — move and disembark Asks, embarking, deployment placement.
- Test: none. A Godot node; its content is `DecisionView` (Task 11a), tested there.

Both files are `partial class DecisionBox`, each under 500 lines. Piece picks, shooting targets and casualties are Task
16b's `DecisionBox.Picks.cs`, reached through two partial methods this task declares (`RenderPicks`, `UnhookPicks`);
until it lands those Asks render the title and hint only.

**Interfaces:**
- Consumes:
  - `DecisionView.Of(Projection p, GameInfo game, Names n, Guid? myTeam, IReadOnlyCollection<string> muted) : DecisionModel`;
    `DecisionModel(Ask? Ask, AskMode? Mode, string Title, IReadOnlyList<(int Key, string Label, string OptionId)> Numbered, string Hint, bool AutoPass = false)` (Task 11a).
    `OptionId` is the `AskOption.Id` for an `Options` Ask and the piece id (`Guid.ToString()`) for a `PieceSelection` entry.
  - `HeaderView.Waiting(Projection p, Names n) : string?` (Task 9): the box's line when nothing is addressed to me.
  - `KeyOverlay.IntentOf(InputEvent e, Viewport viewport) : Intent?` (Task 14); `Intent` (Task 11a).
  - `Session.FactsChanged` (Task 12).
  - `TableInput.TargetClicked`, `TableInput.ModelClicked`, `TableInput.Eligible` (Task 17); `TableInput.Proposing`,
    `Allowance`, `ModelDragged`, `PickNextPoint`, `CancelDrag` (kept by Task 17). Task 17 starts a drag of a
    `Proposing` ghost from its node's position.
  - `TableView.ShowGhosts(IReadOnlyList<ModelRelocation> placements)` (Task 6): one ghost per relocation in the ghost
    child, cleared by `ClearExitGhosts`.
  - `AskViewModel.Mine`, `SpendableBy`, `AnsweredBy`; `DeployView.MidDeploy`, `Layout`; `MoveRuler`; `MoveRegionNode`;
    `TableView.Snap`, `ShowExitGhosts`, `ClearExitGhosts`, `PieceAt`, `Size`, `Volumes` (existing).
- Produces:
  - `DecisionBox.Bind(Session session, TableView view, TableInput input)`; unsubscribes in `_ExitTree`; anchors itself
    bottom centre, growing upward.
  - For Task 16b: `partial void RenderPicks(AskView mine, Projection p, Names names)`, `partial void UnhookPicks()`, and
    the members `_s`, `_view`, `_input`, `_numbered`, `_onEnter`, `_onCancel`, `_onNumber`, `_onTarget`, `_onModel`,
    `Resolve`, `Send`, `Add`, `Btn`.

**What moves from `AskPanel`.** Everything `AskPanel` does stays reachable: the Ask title, numbered options with
hover highlights, piece picks from the table and from the off-table list, the move commit with the move-range
region and the remaining-allowance note, disembark ghosts and the exit region, the embark click, the token spend
steppers and who has answered, Confirm, Pass, Mute and auto-pass with Unmute, "answered for your team", the sealed
Ask's reveal, the orange refusal line, and the failed-send rebuild. New: **Enter**, **Esc** and **1–9**; shooting
targets as chips and table clicks; casualties as table clicks with **Auto**; deployment placement as ghosts committed
by **Enter**.

| Mode | Enter | Esc | 1–9 | Table click (`TargetClicked` / `ModelClicked`) |
| --- | --- | --- | --- | --- |
| Confirm | confirm | — | — | — |
| Choose | — | — | that option | a piece of exactly one option picks it |
| PickPieces | Done | — | that off-table entry | an eligible piece toggles it; `Max` 1 answers at once |
| Move | commit | back to the server's positions | — | an enterable container embarks |
| Assign | fire | clear the assignments | — | aims the picked groups |
| Allocate | confirm when complete | — | — | left adds, right removes |
| TokenSpend | spend | — | — | — |
| Deployment placement | place | clear the ghosts | — | — |

**Keys.** The box handles keys in `_Input`, which runs before the table's `_UnhandledInput`, and marks a key it used
as handled. While the left button is held a drag is in hand: digits are the drag's formation rows and **Esc** the
drag's cancel, so the box leaves both to `TableInput`. An **Esc** the box has no use for falls through to the table,
which clears the selection.

- [ ] **Step 1: The box**

Create `client/src/Hud/DecisionBox.cs`:

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Panels;
using Tabletop.Client.Table;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Projection = Tabletop.Protocol.Projection;

namespace Tabletop.Client.Hud;

/// <summary>Bottom centre: the first open Ask addressed to my team (AskViewModel.Mine), or what the game is waiting on.
/// Buttons, Enter, Esc, 1–9 and table clicks answer it; the hint line under it lists the keys that apply.</summary>
public partial class DecisionBox : PanelContainer
{
    Session _s = null!; TableView _view = null!; TableInput _input = null!;
    readonly VBoxContainer _controls = new();
    readonly Label _title = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart }, _note = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart },
        _refusalLabel = new() { Modulate = Colors.Orange, AutowrapMode = TextServer.AutowrapMode.WordSmart }, _hint = new() { Modulate = new Color(1, 1, 1, 0.6f) };
    MoveRegionNode? _region;
    Guid? _renderedAsk, _lastCommand, _refusalAsk; string? _revealed, _refusal; readonly HashSet<Guid> _sealed = new();
    IReadOnlyList<(int Key, string Label, string OptionId)> _numbered = [];
    // The open Ask's handlers: set by its render, dropped by Unhook whenever the Ask ends by any route.
    Action? _onEnter; Func<bool>? _onCancel; Action<string>? _onNumber; Action<Guid>? _onTarget; Action<Guid, bool>? _onModel;
    // Session and the input outlive the box: every subscription is a field and comes off in _ExitTree.
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action? _onChanged; Action<Refused>? _onRefused;
    Action<Guid>? _onTargetClicked; Action<Guid, bool>? _onModelClicked; Action<Guid, Point3>? _onDragged;

    /// <summary>PickPieces, Assign and Allocate: DecisionBox.Picks.cs (Task 16b).</summary>
    partial void RenderPicks(AskView mine, Projection p, Names names);
    /// <summary>The picks' own state (target assignment, casualty marks), dropped with the Ask's handlers.</summary>
    partial void UnhookPicks();

    public void Bind(Session s, TableView view, TableInput input)
    {
        _s = s; _view = view; _input = input;
        SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom, LayoutPresetMode.MinSize, 8);
        GrowHorizontal = GrowDirection.Both; GrowVertical = GrowDirection.Begin;
        CustomMinimumSize = new Vector2(420, 0);
        var box = new VBoxContainer(); AddChild(box);
        foreach (var c in new Control[] { _title, _note, _controls, _refusalLabel, _hint }) box.AddChild(c);
        view.AddChild(_region = new MoveRegionNode());
        _onEvents = events => { Reveal(events); Render(); }; _onChanged = Render;
        _onRefused = r =>
        {
            if (r.MessageId != _lastCommand) return;
            _refusal = $"The server refused: {r.Detail}"; _refusalAsk = _renderedAsk;
            Reset(); _renderedAsk = null; Render();
        };
        _onTargetClicked = id => _onTarget?.Invoke(id); _onModelClicked = (id, right) => _onModel?.Invoke(id, right); _onDragged = OnDragged;
        s.EventsApplied += _onEvents; s.GameChanged += _onChanged; s.BoardChanged += _onChanged; s.FactsChanged += _onChanged; s.RefusedReceived += _onRefused;
        input.TargetClicked += _onTargetClicked; input.ModelClicked += _onModelClicked; input.ModelDragged += _onDragged;
        Render();
    }

    public override void _ExitTree()
    {
        if (_s is null) return;
        _s.EventsApplied -= _onEvents; _s.GameChanged -= _onChanged; _s.BoardChanged -= _onChanged; _s.FactsChanged -= _onChanged; _s.RefusedReceived -= _onRefused;
        if (IsInstanceValid(_input)) { _input.TargetClicked -= _onTargetClicked; _input.ModelClicked -= _onModelClicked; _input.ModelDragged -= _onDragged; }
        Unhook();
        if (_region is not null && IsInstanceValid(_region)) _region.QueueFree();
    }

    /// <summary>The refusal or send error has its own label, set (not appended) on every render, so N renders show it once.</summary>
    void Render()
    {
        RenderAsk();
        _refusalLabel.Text = _refusal ?? ""; _refusalLabel.Visible = _refusal is not null;
        _hint.Visible = _hint.Text != "";
        Visible = _title.Text != "" || _note.Text != "" || _refusal is not null;
    }

    void RenderAsk()
    {
        var p = _s.Projection; var game = _s.Game;
        if (p is null || game is null || game.Status != GameStatus.Playing) { Reset(); _renderedAsk = null; _title.Text = _note.Text = _hint.Text = ""; return; }
        var mine = AskViewModel.Mine(p, game, _s.MyTeamId, _s.Settings.MutedWindowKinds);
        if (mine?.Ask.Id != _refusalAsk) _refusal = null;                       // a different Ask, or none: the refusal is spent
        var names = new Names(game, p);
        var model = DecisionView.Of(p, game, names, _s.MyTeamId, _s.Settings.MutedWindowKinds);
        if (mine is null)
        {
            var mid = DeployView.MidDeploy(p, _s.Log, _s.MyTeamId);
            if (_renderedAsk is not null || mid?.UnitId != _deployUnit) { Reset(); _renderedAsk = null; }
            _note.Text = _revealed ?? "";                                       // a sealed Ask's reveal stays up until the next Ask
            if (mid is null) { _title.Text = HeaderView.Waiting(p, names) ?? ""; _hint.Text = ""; }   // the header's waiting line, its one source
            else if (_deployUnit is null) StartDeploy(mid, names);
            return;
        }
        if (_renderedAsk == mine.Ask.Id) return;
        Reset(); _renderedAsk = mine.Ask.Id; _note.Text = ""; _revealed = null;
        _title.Text = model.Title; _hint.Text = model.Hint; _numbered = model.Numbered;
        foreach (var id in mine.Highlights) _view.PieceAt(id)?.SetHighlight(Palette.Selected);
        if (model.AutoPass && _refusal is null)   // a failed auto-pass falls through to the normal render with its Pass button, so a dead socket cannot loop
        {
            Send(mine.Ask, new Passed()); _note.Text = $"auto-passed (muted: {mine.Ask.WindowKind})";
            Add(Btn("Unmute", () => _s.UpdateSettings(x => x.Unmute(mine.Ask.WindowKind!))));
            return;
        }
        switch (mine.Mode)
        {
            case AskMode.Choose: RenderChoose(mine); break;
            case AskMode.Move: RenderMove(mine.Ask, (MoveRegion)mine.Ask.Legal, p, names); break;
            case AskMode.TokenSpend: RenderSpend(mine, p, names); break;
            case AskMode.Confirm:                                               // "Continue" for a step gate, "End the round" at the round boundary
                _onEnter = () => Send(mine.Ask, new Confirmed());
                Add(Btn($"{AskViewModel.ConfirmLabel(mine.Ask)} (Enter)", _onEnter));
                break;
            default: RenderPicks(mine, p, names); break;                       // PickPieces, Assign, Allocate
        }
        if (mine.Ask.AllowPass) Add(Btn("Pass", () => Send(mine.Ask, new Passed())));
        if (mine.Ask.WindowKind is { } kind) Add(Btn($"Mute '{kind}' windows", () => _s.UpdateSettings(x => x.Mute(kind))));
        if (AskViewModel.AnsweredBy(_s.Log, mine.Ask.Id, names) is { } who) _note.Text = $"{who} answered for your team";
    }

    /// <summary>Numbered options. A choice's pieces light while its button is hovered; a click on a piece that belongs to exactly one choice picks it (activate: the unit).</summary>
    void RenderChoose(AskView mine)
    {
        var choices = ((Options)mine.Ask.Legal).Choices; var lit = mine.Highlights;
        foreach (var c in choices)
        {
            var key = _numbered.FirstOrDefault(n => n.OptionId == c.Id).Key;
            var b = Btn(key > 0 ? $"{key}. {c.Label}" : c.Label, () => Send(mine.Ask, new Chosen(c.Id)));
            var pieces = c.Pieces;
            b.MouseEntered += () => { foreach (var id in pieces) if (_view.PieceAt(id) is { } n && IsInstanceValid(n)) n.SetHighlight(Palette.Selected); };
            b.MouseExited += () => { foreach (var id in pieces) if (_view.PieceAt(id) is { } n && IsInstanceValid(n)) n.SetHighlight(lit.Contains(id) ? Palette.Selected : null); };
            Add(b);
        }
        _onNumber = option => Send(mine.Ask, new Chosen(option));
        _input.Eligible = choices.SelectMany(c => c.Pieces).ToHashSet();
        _onTarget = clicked =>
        {
            var hits = choices.Where(c => Resolve(clicked, c.Pieces) is not null).ToList();
            if (hits.Count == 1) Send(mine.Ask, new Chosen(hits[0].Id));
        };
    }

    /// <summary>
    /// p.13: "may spend any number of spell tokens", capped per model by what that model holds. So one box per model, each
    /// bounded by its own maximum, and no total. SpendableBy drops the other team's spenders, which the map carries because
    /// the window addresses both.
    /// </summary>
    void RenderSpend(AskView mine, Projection p, Names names)
    {
        var spendable = AskViewModel.SpendableBy(p, _s.Game!, _s.MyTeamId!.Value, (TokenSpend)mine.Ask.Legal);
        var spends = spendable.ToDictionary(kv => kv.Key, kv => new SpinBox { MinValue = 0, MaxValue = kv.Value, Prefix = names.Piece(kv.Key) });
        foreach (var sp in spends.Values) Add(sp);
        void Spend() => Send(mine.Ask, new TokensSpent(spends.Where(kv => kv.Value.Value > 0).ToDictionary(kv => kv.Key, kv => (int)kv.Value.Value)));
        _onEnter = Spend;
        Add(Btn("Spend (Enter)", Spend));
        if (mine.AnsweredTeams.Count > 0) Add(new Label { Text = string.Join(", ", mine.AnsweredTeams.Select(t => $"{t} has answered")) });
    }

    public override void _Input(InputEvent e)
    {
        if (!Visible || KeyOverlay.IntentOf(e, GetViewport()) is not { } intent) return;
        var dragging = Godot.Input.IsMouseButtonPressed(MouseButton.Left);   // a drag in hand owns the digits (rows) and Esc
        var used = intent switch
        {
            >= Intent.Option1 and <= Intent.Option9 when !dragging => Number(intent - Intent.Option1 + 1),
            Intent.Confirm when _onEnter is { } enter => Run(enter),
            Intent.Cancel when !dragging => _onCancel?.Invoke() == true,
            _ => false,
        };
        if (used) GetViewport().SetInputAsHandled();
    }

    bool Number(int key)
    {
        if (_onNumber is not { } pick) return false;
        foreach (var n in _numbered) if (n.Key == key) { pick(n.OptionId); return true; }
        return false;
    }

    static bool Run(Action a) { a(); return true; }

    /// <summary>What a table click names in a legal set, which holds model ids or unit ids: the clicked id itself, its unit, or the set's model of the clicked unit.</summary>
    Guid? Resolve(Guid clicked, IReadOnlyCollection<Guid> ids)
    {
        if (ids.Contains(clicked)) return clicked;
        var p = _s.Projection; if (p is null) return null;
        if (p.Models.GetValueOrDefault(clicked)?.UnitId is { } unit && ids.Contains(unit)) return unit;
        if (p.Units.ContainsKey(clicked)) foreach (var id in ids) if (p.Models.GetValueOrDefault(id)?.UnitId == clicked) return id;
        return null;
    }

    /// <summary>A sealed Ask withholds every answer until it closes; the closing frame carries them all, and that frame is the reveal.</summary>
    void Reveal(IReadOnlyList<GameEvent> events)
    {
        if (_s.Game is null || _s.Projection is null) return;
        var names = new Names(_s.Game, _s.Projection);
        foreach (var payload in events.Select(e => e.Payload))
        {
            if (payload is AskOpened o && o.Ask.Sealed) _sealed.Add(o.Ask.Id);
            if (payload is AskClosed c && _sealed.Remove(c.AskId) && c.Answers.Count > 0)
                _revealed = "Revealed: " + string.Join(", ", c.Answers.Select(a => $"{names.Team(a.TeamId)} {LogText.Answer(a.Answer, names)}"));
        }
    }

    void Send(Ask ask, AskAnswer answer) => Guarded.Run(() => Answer(ask, answer), message => Report(ask.Id, message));

    /// <summary>A failed send is logged. If the Ask is still the one on screen, rebuild it from scratch and show the error; a later Ask is left alone.</summary>
    void Report(Guid askId, string message)
    {
        GD.PushWarning($"Answer failed: {message}");
        if (!IsInstanceValid(this) || _renderedAsk != askId) return;
        _refusal = $"Could not send the answer: {message}"; _refusalAsk = askId; _renderedAsk = null; Render();
    }

    async Task Answer(Ask ask, AskAnswer answer)
    {
        SetButtons(true); Unhook();                                             // one answer per Ask: no second click or key can double-send
        _lastCommand = Guid.CreateVersion7();                                   // before the send, so a Refused that lands first still matches
        await _s.SendCommandAsync(new AnswerAsk(ask.Id, answer), _lastCommand.Value);
    }

    /// <summary>The open Ask's handlers and table state, dropped whenever it ends by any route: our answer, a teammate's, a server close, a replacing Ask.</summary>
    void Unhook()
    {
        _onEnter = null; _onCancel = null; _onNumber = null; _onTarget = null; _onModel = null;
        _proposed = null; _deployAt = null; _deployUnit = null;
        UnhookPicks();
        if (_input is not null && IsInstanceValid(_input)) { _input.Proposing.Clear(); _input.Allowance = null; _input.Eligible = new HashSet<Guid>(); }
        if (_region is not null && IsInstanceValid(_region)) _region.Hide();
        if (_view is not null && IsInstanceValid(_view)) _view.ClearExitGhosts();
    }

    /// <summary>Unhook, and the box's controls and the table's highlights with it.</summary>
    void Reset()
    {
        Unhook(); _numbered = [];
        foreach (var c in _controls.GetChildren()) { _controls.RemoveChild(c); c.QueueFree(); }
        if (_view is not null && IsInstanceValid(_view)) foreach (var n in _view.Pieces.OfType<PieceNode>()) n.SetHighlight(null);
    }

    void SetButtons(bool disabled) { foreach (var b in _controls.FindChildren("*", "BaseButton", true, false).OfType<BaseButton>()) b.Disabled = disabled; }
    void Add(Control c) => _controls.AddChild(c);
    static Button Btn(string text, Action a) { var b = new Button { Text = text, FocusMode = FocusModeEnum.None }; b.Pressed += a; return b; }
}
```

- [ ] **Step 2: Moves and deployment placement**

Create `client/src/Hud/DecisionBox.Move.cs`. The move, disembark and embark logic is `AskPanel`'s, with **Enter**
committing, **Esc** putting the models back, and the embark click arriving as `TargetClicked` on an `Eligible`
container. A dropped model takes the facing its node has, so a formation turned with **Q/E** (Task 17) commits as drawn.

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Panels;
using Tabletop.Client.Table;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Projection = Tabletop.Protocol.Projection;

namespace Tabletop.Client.Hud;

public partial class DecisionBox
{
    Dictionary<Guid, Point3>? _proposed, _deployAt; Guid? _deployUnit;
    // What YawOf needs from the disembark render: the exit distance, the board's volumes and its size.
    double _exitWithin; IReadOnlyList<WorldVolume> _volumes = []; TableSize _size = new(72, 48);

    void RenderMove(Ask ask, MoveRegion legal, Projection p, Names names)
    {
        if (legal.ModelIds.Count == 0) return;                                      // nothing to move; the server closes the Ask
        var lead = p.Models[legal.ModelIds[0]];
        var body = BodyOf(p, lead.Id);
        var ordinal = _s.Game?.Teams.FirstOrDefault(t => t.Id == _s.MyTeamId)?.Ordinal ?? 1;
        _input.Proposing.Clear(); foreach (var id in legal.ModelIds) _input.Proposing.Add(id);
        if (MoveRuler.IsDisembark(legal))
        {
            // p.15 "fully within 6" of it when exiting": no start placement, so the region is the container's footprint grown by the exit distance.
            var containerId = legal.Containment!.ExitingId!.Value;
            if (p.Models.GetValueOrDefault(containerId)?.Location is not OnTable at) { _note.Text = "The container is no longer on the table."; Unhook(); return; }
            var within = legal.Containment.ExitWithin ?? 0;
            (_exitWithin, _volumes, _size) = (within, _view.Volumes, _view.Size);
            _region!.Show(MoveRuler.ExitRegion(body, new PlacedModel(BodyOf(p, containerId), at.Placement), legal, _volumes, _size), Palette.Team(ordinal));
            _note.Text = $"Disembark: place every model fully within {within:0.0}\" of {names.Piece(containerId)}. Drag them out, then commit.";
            _input.Allowance = null;
            void StartExit() { _view.ShowExitGhosts(legal.ModelIds, containerId); _proposed = legal.ModelIds.ToDictionary(id => id, _ => at.Placement.Position); }
            StartExit();
            _onCancel = () => { StartExit(); return true; };
        }
        else
        {
            if (lead.Location is not OnTable on) { Unhook(); return; }
            var frame = ask.FrameId is { } f ? p.Stack.FirstOrDefault(x => x.Id == f) : null;
            var used = frame is null ? 0 : MoveRuler.UsedInFrame(frame, lead.Id);
            var remaining = Math.Max(0, legal.Allowance - used);
            _region!.Show(MoveRuler.Region(new PlacedModel(body, on.Placement), legal with { Allowance = remaining }, _view.Volumes, _view.Size), Palette.Team(ordinal));
            _note.Text = $"Move up to {remaining:0.0}\" (used {used:0.0}\").";
            _input.Allowance = remaining;
            // Drawn and committed agree: a rebuilt Ask (a refusal) or an Esc starts from the server's positions.
            void StartMove() { _proposed = legal.ModelIds.ToDictionary(id => id, id => ((OnTable)p.Models[id].Location).Placement.Position); _view.Snap(legal.ModelIds); }
            StartMove();
            _onCancel = () => { StartMove(); return true; };
            OfferEmbark(ask, legal, p, names);
        }
        void Commit()
        {
            if (_proposed is not { } proposed) return;
            Send(ask, new Destinations(legal.ModelIds.Select(id => new ModelRelocation(id, p.Models[id].Location, new OnTable(new Placement(proposed[id], YawOf(p, id))))).ToList()));
        }
        _onEnter = Commit;
        Add(Btn("Commit move (Enter)", Commit));
    }

    /// <summary>p.15: "only one model needs to reach the Transport for everyone to embark". A click on a listed container answers the
    /// whole unit into it: one relocation per model to the same Inside. The player may still drag and commit instead.</summary>
    void OfferEmbark(Ask ask, MoveRegion legal, Projection p, Names names)
    {
        IReadOnlyList<Guid> enterable = legal.Containment?.Enterable ?? [];
        if (enterable.Count == 0) return;
        _note.Text += $" Or click a container in reach to embark: {string.Join(", ", enterable.Select(names.Piece))}.";
        _input.Eligible = enterable.ToHashSet();
        _onTarget = clicked =>
        {
            if (Resolve(clicked, enterable) is not { } container || !p.Models.ContainsKey(container)) return;
            _input.CancelDrag();
            Send(ask, new Destinations(legal.ModelIds.Select(id => new ModelRelocation(id, p.Models[id].Location, new Inside(container))).ToList()));
        };
    }

    /// <summary>The facing the answer carries. A model on the table takes its node's facing. A disembarking model takes whatever fits at
    /// the point it was dropped (p.7), from the same search that drew the region; the container's facing is the fallback.</summary>
    Yaw YawOf(Projection p, Guid modelId) => p.Models[modelId].Location switch
    {
        OnTable on => NodeYaw(modelId) ?? on.Placement.Yaw,
        Inside i when p.Models.GetValueOrDefault(i.ContainerId)?.Location is OnTable c =>
            MoveRuler.ExitYawAt(BodyOf(p, modelId), new PlacedModel(BodyOf(p, i.ContainerId), c.Placement),
                _exitWithin, _proposed![modelId].Horizontal, _volumes, _size) ?? c.Placement.Yaw,
        _ => new Yaw(0),
    };

    Yaw? NodeYaw(Guid modelId) => _view.PieceAt(modelId) is { } n && IsInstanceValid(n) ? WorldFrame.YawFrom(n.Rotation.Y) : null;

    static ModelBody BodyOf(Projection p, Guid modelId) { var m = p.Models[modelId]; return new ModelBody(m.Slot.Base, m.Slot.Height); }

    void OnDragged(Guid modelId, Point3 position)
    {
        if (_proposed is not null && _proposed.ContainsKey(modelId)) _proposed[modelId] = position;
        if (_deployAt is not null && _deployAt.ContainsKey(modelId)) _deployAt[modelId] = position;
    }

    /// <summary>Deployment placement: the server waits for every model of the unit just picked (MidDeploy). A click on the table drops
    /// the shelved models there as ghosts in a row; they drag and take formations like a move; Enter places them with one MoveModels
    /// from the shelf. Nothing is sent before Enter.</summary>
    void StartDeploy(MidDeploy mid, Names names)
    {
        _deployUnit = mid.UnitId;
        _title.Text = $"Place the models of {names.Unit(mid.UnitId)}";
        _hint.Text = "Click the table where the unit goes";
        var unit = mid.UnitId;
        _input.PickNextPoint(at => DropDeploy(unit, at));
    }

    void DropDeploy(Guid unit, Point3 at)
    {
        if (_deployUnit != unit || _s.Projection is not { } p) return;              // the deploy moved on before the click
        var models = p.Models.Values.Where(m => m.UnitId == unit && m.Location is OnShelf).OrderBy(m => m.Slot.Ordinal).ToList();
        var layout = DeployView.Layout(models, at, _view.Size);
        if (layout.Count == 0) return;
        _deployAt = layout.ToDictionary(r => r.ModelId, r => ((OnTable)r.To).Placement.Position);
        _input.Proposing.Clear(); foreach (var r in layout) _input.Proposing.Add(r.ModelId);
        _view.ShowGhosts(layout);
        _hint.Text = "Drag to place · Shift: one model · 1–5 while holding: rows · Enter: place · Esc: clear";
        _onEnter = PlaceDeploy;
        _onCancel = () => { Reset(); Render(); return true; };                    // Reset drops _deployUnit, so Render arms a fresh click
        Add(Btn("Place (Enter)", PlaceDeploy));
        Render();
    }

    void PlaceDeploy()
    {
        if (_deployAt is null || _s.Projection is not { } p) return;
        var moves = _deployAt.Select(kv => new ModelRelocation(kv.Key, p.Models[kv.Key].Location, new OnTable(new Placement(kv.Value, NodeYaw(kv.Key) ?? new Yaw(0))))).ToList();
        _onEnter = null; _onCancel = null; SetButtons(true);
        var id = Guid.CreateVersion7(); _lastCommand = id;                          // a Refused for it lands in _onRefused, which rebuilds the placement
        Guarded.Run(() => _s.SendCommandAsync(new MoveModels(moves), id), message => { _refusal = $"Could not place the models: {message}"; _refusalAsk = null; Reset(); Render(); });
    }
}
```

The ghosts stay until the models' `ModelsMoved` lands: `MidDeploy` then names no unit (or the next one), and the
render's `Reset` clears them.

- [ ] **Step 3: Build and check**

```bash
dotnet build client/Tabletop.csproj 2>&1 | grep -E "error|warning CS" ; ./scripts/check-client
```

Expected: no output.

- [ ] **Step 4: Commit**

```bash
git add client/src/Hud/DecisionBox.cs client/src/Hud/DecisionBox.Move.cs
git commit -m "Add the decision box: options, moves, token spends, confirmations, deployment placement, keys

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```
