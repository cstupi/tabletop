# Task 14: The Ask panel and the declared move

**Files:**
- Create: `client/src/Panels/AskPanel.cs`, `client/src/Table/MoveRegionNode.cs`, `client/src/Screens/GameScreen.Play.cs`

**Interfaces:**
- Consumes: `AskViewModel` (`Mine`, `SpendableBy`, `ConfirmLabel`), `AskView`, `AskMode`, `MoveRuler` (`Read`, `UsedInFrame`, `Region`, `ExitRegion`, `ExitYawAt`, `IsDisembark`), `Names` (Core); `Session` (`Projection`, `Game`, `MyTeamId`, `Settings.MutedWindowKinds`, `SendCommandAsync`, `EventsApplied`, `Log`); `TableView` (with `ShowExitGhosts`/`ClearExitGhosts`, Task 11), `TableInput` (with `PieceLeftClicked` and `CancelDrag`, Task 12), `RulerNode`, `PieceNode` (Table); `Ask`, `Options`, `PieceSelection`, `Allocation`, `Assignment`, `MoveRegion`, `MoveContainment`, `TokenSpend`, `Confirmation`, `AnswerAsk`, `Chosen`, `SelectedPieces`, `Allocated`, `Assigned`, `Destinations`, `TokensSpent`, `Confirmed`, `Passed`, `ModelRelocation`, `OnTable`, `Inside`, `Placement` (Protocol); `PlacedModel`, `ModelBody`, `Yaw`, `WorldVolume` (Geometry).
- Produces: `partial class AskPanel : PanelContainer` — `void Bind(Session, TableView, TableInput)`; renders `AskViewModel.Mine(...)` by mode: `Choose` → one button per option (highlighting each option's pieces on hover), `PickPieces` → click eligible pieces on the table (highlighted), a counter `n of min–max` that names the piece when `Max` is 1, the consumed click cancelling the drag it started, "Done" → `SelectedPieces`; `Allocate` → a `SpinBox` per target summing to `Amount`, "Done" → `Allocated`; `Assign` → an `OptionButton` per item over the targets, "Done" → `Assigned`; `Move` → the region drawn (`MoveRegionNode`), the models draggable with the ruler showing distance and remaining allowance (`MoveRuler.Read(start, current, Allowance − UsedInFrame)`), positions streamed as `proposed` ghosts, "Commit" → `Destinations` with the settled positions — and, when the legal set's `Containment` says so, two more things: any `Enterable` container is a click target whose click answers `Destinations` with `Inside(container)` for **every** model of the unit, and a non-null `ExitingId` switches the whole mode to a disembark, where the region is `MoveRuler.ExitRegion` about the container's own base, the drag ghosts are `TableView.ShowExitGhosts` on the container's node, and the remaining-allowance readout is suppressed; `TokenSpend` → one private `SpinBox` per entry of `AskViewModel.SpendableBy`, prefixed with the model's name, bounded by that model's own maximum, with no total anywhere (p.13: any number up to what each model holds), "Spend" → `TokensSpent` of the non-zero boxes, plus `AnsweredTeams` as "Team 2 has answered" and, once the server closes the sealed Ask, every team's answer at once from `ask_closed` (`LogText.Answer`); `Confirm` → one button labelled `AskViewModel.ConfirmLabel` ("Continue" for a step gate, "End the round" for the scheduler's `end_round` Ask) → `Confirmed`; a "Pass" button whenever `AllowPass`; an `AutoPass` view sends `Passed` at once and shows "auto-passed (muted: {kind})" with an "Unmute" link; `WaitingFor` renders as "Waiting for Team N…"; `AnsweredBy` shows under a sealed Ask. A `Refused` for the panel's last command shows in the panel (`strict` enforcement: "the server refused: outside the legal set"). `partial class MoveRegionNode : Node3D` — `Show(IReadOnlyList<Point2> boundaryWire, Color)`, `Hide()`. `GameScreen.Play.cs` — `partial void ShowPlayPanels(bool)` creating the `AskPanel` (and Task 15's `LogPanel`) at `playing`/`ended`.
- The move button of the spec is the activate Ask's option (`{unit}:{Action}`): choosing it starts the sequence, whose move step opens the `MoveRegion` Ask (client shell → Input and presence). "Hand moves arrange models and never end a turn" is the plain drag of Task 12.
- **The panel keys on the legal set, never on the question name**, which is why a second ruleset question needs no new branch. Two of them arrive carrying a `move_region`: **`move`**, opened by the move step of `hold`/`advance`/`rush`/`charge` (interpreter plan Task 14), and **`disembark`**, opened by `transport-destroyed`'s `place` step when a wrecked container's survivors must be placed fully within 6" of it before it is removed (interpreter plan Task 19, rulebook p.15). `disembark` always carries `Containment.ExitingId`, sets `AllowPass` false, and puts the exit distance itself in `Allowance` — which `MoveRuler.IsDisembark` makes the panel ignore, so it renders exactly as a disembark move does and shares every line of `RenderMove`. Neither name belongs in `AskQuestions`: those are the engine's own eight, and a ruleset's question names are plain strings (`move`, `disembark`, `shoot-or-not`, `declare-targets`, `charge-target`, …) that this panel never switches on. The one place a question name is read at all is `AskViewModel.ConfirmLabel`, and it only ever compares against `AskQuestions.EndRound`.

Spec: client shell → Asks and windows (all of it), Input and presence (the move button; the region at the allowance; the checkpoint's remaining distance from the frame; the `proposed` ghost until commit); game flow → Inside a container (an occupant has no table position of its own, moves when the container moves, and disembarks relative to it; enter or exit with any move action, never both in one activation, exit within 6"); rulebook p.15 `Transport(X)` (*"only one model needs to reach the Transport for everyone to embark"*, *"must stay fully within 6” of it when exiting"*); action sequences → Asks (any team member answers), Windows (react or pass; sealed simultaneous), One roll primitive (the confirm gate is an Ask); scheduler → The turn is an Ask, There is no round limit (the `end_round` Ask at the round boundary — one `Confirmation` addressed to every team, rendered by the same panel), Windows (muted kinds per player; the pass is that player's pass); whole-design review ruling 1 (`strict` refuses, the panel says so); geometry plan → *Interfaces needed → Client shell plan* (`Movement.Region(...).Boundary`, `Movement.Check(...).Distance`) and Task 6 (`Containment.BaseFullyWithin`, which `MoveRuler.ExitRegion` turns into the disembark boundary); contracts plan Task 8 (the legal set and answer shapes, `MoveRegion.Containment`, `TokenSpend.MaxByModel`, `TokensSpent.Counts`); interpreter plan Tasks 14 and 19 (what the server offers and expects back); rulebook p.13 `Caster(X)` (the team's simultaneous spend, one input per eligible model, each bounded by what that model holds, no total) and `Takedown` (the shooter picks the model, so the eligible set is model ids and a click on a model answers it).

- [ ] **Step 1: Write `MoveRegionNode.cs`**

```csharp
using Godot;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>The reachable region as a flat polygon just above the slab: the server's parameters, rendered by Tabletop.Geometry, drawn here.</summary>
public partial class MoveRegionNode : Node3D
{
    MeshInstance3D? _mesh;
    public void Show(IReadOnlyList<Point2> boundaryWire, Color colour)
    {
        Hide();
        var verts = boundaryWire.Select(p => WorldFrame.ToGodot(new Point3(p.X, p.Y, 0.03))).ToArray();
        var centre = verts.Aggregate(Vector3.Zero, (a, v) => a + v) / verts.Length;
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        for (var i = 0; i < verts.Length; i++) { st.AddVertex(centre); st.AddVertex(verts[(i + 1) % verts.Length]); st.AddVertex(verts[i]); }
        _mesh = new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = Palette.Translucent(colour with { A = 0.25f }) };
        AddChild(_mesh);
    }
    public new void Hide() { _mesh?.QueueFree(); _mesh = null; }
}
```

Both regions this node draws are star-shaped about the point the boundary is measured from — the start placement for `MoveRuler.Region` (geometry plan Task 12) and the container's base centre for `MoveRuler.ExitRegion` (each ray marches outward from that centre and stops, which is star-shaped by construction, blockers included) — so a fan from the vertex centroid is exact in both cases and the node needs no variant.

- [ ] **Step 2: Write `AskPanel.cs`**

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Table;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

/// <summary>One panel, always in the same place, rendering whatever is addressed to the player's team.</summary>
public partial class AskPanel : PanelContainer
{
    Session _s = null!; TableView _view = null!; TableInput _input = null!;
    readonly VBoxContainer _box = new(); readonly Label _title = new(), _note = new(); MoveRegionNode? _region;
    Guid? _renderedAsk, _lastCommand; readonly List<Guid> _picked = new(); Dictionary<Guid, Point3>? _proposed;
    readonly HashSet<Guid> _sealed = new(); string? _revealed;

    public void Bind(Session s, TableView view, TableInput input)
    {
        _s = s; _view = view; _input = input;
        AddChild(_box); _box.AddChild(_title); _box.AddChild(_note);
        view.AddChild(_region = new MoveRegionNode());
        s.EventsApplied += events => { Reveal(events); Render(); }; s.GameChanged += Render; s.BoardChanged += Render;
        s.RefusedReceived += r => { if (r.MessageId == _lastCommand) { _note.Text = $"The server refused: {r.Detail}"; _renderedAsk = null; Render(); } };
        Render();
    }

    void Render()
    {
        var p = _s.Projection; var game = _s.Game;
        if (p is null || game is null) return;
        var mine = AskViewModel.Mine(p, game, _s.MyTeamId, _s.Settings.MutedWindowKinds);
        if (mine is null)
        {
            ClearControls(); _renderedAsk = null; _region?.Hide();
            _title.Text = AskViewModel.WaitingFor(p, game, _s.MyTeamId) is { } teams ? $"Waiting for {teams}…" : (p.OpenAsks.Count == 0 ? "" : "Waiting…");
            _note.Text = _revealed ?? "";                                  // a sealed Ask's reveal stays up until the next Ask
            return;
        }
        if (_renderedAsk == mine.Ask.Id) return;
        ClearControls(); _renderedAsk = mine.Ask.Id; _picked.Clear(); _proposed = null; _note.Text = ""; _revealed = null;
        _title.Text = mine.Ask.Prompt;
        foreach (var id in mine.Highlights) _view.PieceAt(id)?.SetHighlight(Palette.Selected);
        if (mine.AutoPass) { _ = Answer(mine.Ask, new Passed()); _note.Text = $"auto-passed (muted: {mine.Ask.WindowKind})"; _box.AddChild(Btn("Unmute", () => _s.UpdateSettings(x => x.Unmute(mine.Ask.WindowKind!)))); return; }
        var names = new Names(game, p);
        switch (mine.Mode)
        {
            case AskMode.Choose: foreach (var c in ((Options)mine.Ask.Legal).Choices) _box.AddChild(Btn(c.Label, () => _ = Answer(mine.Ask, new Chosen(c.Id)))); break;
            case AskMode.PickPieces:
                var sel = (PieceSelection)mine.Ask.Legal; var count = new Label { Text = $"0 of {sel.Min}–{sel.Max}" }; _box.AddChild(count);
                _input.PieceLeftClicked += OnPick;
                // The clicked piece's own id first, then the id selection widened to: an Eligible set holds
                // model ids (a Takedown's chosen model, p.14) or unit ids (a charge target, a deploy), and a bare
                // click on a model selects its unit — so keying on Selected.PieceId alone can never match a
                // model-id set. A click that answered is not also a drag: CancelDrag drops the one OnLeftDown
                // started, which for a model-id set is a drag of the unit the player was only pointing at.
                void OnPick(Guid clicked)
                {
                    var id = sel.Eligible.Contains(clicked) ? clicked
                        : _input.Selected is { } s && sel.Eligible.Contains(s.PieceId) ? s.PieceId : (Guid?)null;
                    if (id is { } pick && !_picked.Contains(pick) && _picked.Count < sel.Max)
                    {
                        _picked.Add(pick); _input.CancelDrag();
                        // One model out of a unit is worth reading back before Done, so a single pick shows the name rather than a tally.
                        count.Text = sel.Max == 1 ? names.Piece(pick) : $"{_picked.Count} of {sel.Min}–{sel.Max}";
                        _view.PieceAt(pick)?.SetHighlight(Palette.Ghost);
                    }
                }
                _box.AddChild(Btn("Done", () => { _input.PieceLeftClicked -= OnPick; _ = Answer(mine.Ask, new SelectedPieces(_picked.ToList())); }));
                break;
            case AskMode.Allocate:
                var alloc = (Allocation)mine.Ask.Legal; var spins = alloc.Targets.ToDictionary(t => t, t => new SpinBox { MinValue = 0, MaxValue = alloc.Amount, Prefix = names.Piece(t) });
                foreach (var sp in spins.Values) _box.AddChild(sp);
                _box.AddChild(Btn($"Done (assign {alloc.Amount})", () => _ = Answer(mine.Ask, new Allocated(spins.ToDictionary(kv => kv.Key, kv => (int)kv.Value.Value)))));
                break;
            case AskMode.Assign:
                var asg = (Assignment)mine.Ask.Legal; var picks = new Dictionary<string, OptionButton>();
                foreach (var item in asg.Items) { var ob = new OptionButton(); foreach (var t in asg.Targets) ob.AddItem(names.Piece(t)); _box.AddChild(new Label { Text = item.Label }); _box.AddChild(ob); picks[item.Id] = ob; }
                _box.AddChild(Btn("Done", () => _ = Answer(mine.Ask, new Assigned(picks.ToDictionary(kv => kv.Key, kv => asg.Targets[kv.Value.Selected])))));
                break;
            case AskMode.Move: RenderMove(mine.Ask, (MoveRegion)mine.Ask.Legal, p, names); break;
            case AskMode.TokenSpend:
                // p.13: "may spend any number of spell tokens", capped per model by what that model holds. So one box per
                // model, each bounded by its own maximum, and no total: the allocation mode's single amount split exactly
                // would make spending one token of a possible five illegal, which is the wrong answer to the wrong question.
                // SpendableBy drops the other team's spenders, which the map carries because the window addresses both.
                var spendable = AskViewModel.SpendableBy(p, _s.Game!, _s.MyTeamId!.Value, (TokenSpend)mine.Ask.Legal);
                var spends = spendable.ToDictionary(kv => kv.Key, kv => new SpinBox { MinValue = 0, MaxValue = kv.Value, Prefix = names.Piece(kv.Key) });
                foreach (var sp in spends.Values) _box.AddChild(sp);
                _box.AddChild(Btn("Spend", () => _ = Answer(mine.Ask,
                    new TokensSpent(spends.Where(kv => kv.Value.Value > 0).ToDictionary(kv => kv.Key, kv => (int)kv.Value.Value)))));
                if (mine.AnsweredTeams.Count > 0) _box.AddChild(new Label { Text = string.Join(", ", mine.AnsweredTeams.Select(t => $"{t} has answered")) });
                break;
            case AskMode.Confirm: _box.AddChild(Btn(AskViewModel.ConfirmLabel(mine.Ask), () => _ = Answer(mine.Ask, new Confirmed()))); break;   // "Continue" for a step gate, "End the round" at the round boundary
        }
        if (mine.Ask.AllowPass) _box.AddChild(Btn("Pass", () => _ = Answer(mine.Ask, new Passed())));
        if (mine.Ask.WindowKind is { } kind) _box.AddChild(Btn($"Mute '{kind}' windows", () => _s.UpdateSettings(x => x.Mute(kind))));
        if (AskViewModel.AnsweredBy(_s.Log, mine.Ask.Id, names) is { } who) _note.Text = $"{who} answered for your team";
    }

    void RenderMove(Ask ask, MoveRegion legal, Projection p, Names names)
    {
        if (legal.ModelIds.Count == 0) return;                                      // nothing to move; the server closes the Ask
        var lead = p.Models[legal.ModelIds[0]];
        var body = new ModelBody(lead.Slot.Base, lead.Slot.Height);
        var ordinal = _s.Game?.Teams.FirstOrDefault(t => t.Id == _s.MyTeamId)?.Ordinal ?? 1;
        _input.Proposing.Clear(); foreach (var id in legal.ModelIds) _input.Proposing.Add(id);
        _input.ModelDragged += OnDragged;

        if (MoveRuler.IsDisembark(legal))
        {
            // The unit is inside, so there is no start placement to reach from and the straight-line region is meaningless:
            // the offer is p.15's "fully within 6" of it when exiting", drawn about the container's own base. Allowance is the
            // action's remaining distance and binds nothing here, so no remaining-allowance readout goes up and the ruler gets none.
            var containerId = legal.Containment!.ExitingId!.Value;
            if (p.Models.GetValueOrDefault(containerId)?.Location is not OnTable at)
            {
                _note.Text = "The container is no longer on the table.";               // it was wrecked and shelved; the server will close this Ask
                return;
            }
            var host = p.Models[containerId];
            var container = new PlacedModel(new ModelBody(host.Slot.Base, host.Slot.Height), at.Placement);
            var within = legal.Containment.ExitWithin ?? 0;
            _region!.Show(MoveRuler.ExitRegion(body, container, legal, _view.Volumes, _view.Size), Palette.Team(ordinal));
            _view.ShowExitGhosts(legal.ModelIds, containerId);                          // Inside carries no Placement: the ghosts start on the container's node
            _note.Text = $"Disembark: place every model fully within {within:0.0}\" of {names.Piece(containerId)}. Drag them out, then commit.";
            _input.Allowance = null;
            _proposed = legal.ModelIds.ToDictionary(id => id, _ => at.Placement.Position);
        }
        else
        {
            if (lead.Location is not OnTable on) return;
            var frame = ask.FrameId is { } f ? p.Stack.FirstOrDefault(x => x.Id == f) : null;
            var used = frame is null ? 0 : MoveRuler.UsedInFrame(frame, lead.Id);
            var remaining = Math.Max(0, legal.Allowance - used);
            _region!.Show(MoveRuler.Region(new PlacedModel(body, on.Placement), legal with { Allowance = remaining }, _view.Volumes, _view.Size), Palette.Team(ordinal));
            _note.Text = $"Move up to {remaining:0.0}\" (used {used:0.0}\"). Drag the models, then commit.";
            _input.Allowance = remaining;
            _proposed = legal.ModelIds.ToDictionary(id => id, id => ((OnTable)p.Models[id].Location).Placement.Position);
            OfferEmbark(ask, legal, p, names);
        }
        _box.AddChild(Btn("Commit move", () => _ = Answer(ask, new Destinations(legal.ModelIds.Select(id =>
            new ModelRelocation(id, p.Models[id].Location, new OnTable(new Placement(_proposed![id], YawOf(p, id))))).ToList()))));
    }

    /// <summary>
    /// p.15: "only one model needs to reach the Transport for everyone to embark". The containers the server listed are the
    /// click targets — already highlighted, because <c>AskViewModel.Mine</c> puts them in <c>Highlights</c> — and clicking one
    /// answers the <b>whole unit</b> into it: one relocation per model of the unit to the same <c>Inside</c>, never one per
    /// reaching model. Nothing is dragged and nothing is measured, so the click cancels the drag it just started on the
    /// container's node. The embark offer sits beside the ordinary move: the player may still drag and commit instead.
    /// </summary>
    void OfferEmbark(Ask ask, MoveRegion legal, Projection p, Names names)
    {
        IReadOnlyList<Guid> enterable = legal.Containment?.Enterable ?? [];
        if (enterable.Count == 0) return;
        _note.Text += $" Or click a container in reach to embark: {string.Join(", ", enterable.Select(names.Piece))}.";
        _input.PieceLeftClicked += OnEmbark;
        void OnEmbark(Guid clicked)
        {
            if (_renderedAsk != ask.Id) { _input.PieceLeftClicked -= OnEmbark; return; }   // a later Ask is up: this closure is stale
            if (!enterable.Contains(clicked)) return;
            _input.PieceLeftClicked -= OnEmbark;
            _input.CancelDrag();
            _ = Answer(ask, new Destinations(legal.ModelIds.Select(id => new ModelRelocation(id, p.Models[id].Location, new Inside(clicked))).ToList()));
        }
    }

    /// <summary>The yaw a destination keeps: the model's own if it has a placement, else the container's — an occupant has none (game flow → Inside a container).</summary>
    /// <summary>The facing the answer carries. A model on the table keeps its own. A disembarking model takes whatever fits at
    /// the point the player dropped it (p.7) — MoveRuler.ExitYawAt runs the same search that drew the region,
    /// so the answer is a placement the server accepts; the container's own facing is the fallback when nothing fits, which
    /// leaves the server to refuse that model and leave it inside.</summary>
    Yaw YawOf(Projection p, Guid modelId) => p.Models[modelId].Location switch
    {
        OnTable on => on.Placement.Yaw,
        Inside i when p.Models.GetValueOrDefault(i.ContainerId)?.Location is OnTable c =>
            MoveRuler.ExitYawAt(BodyOf(p, modelId), new PlacedModel(BodyOf(p, i.ContainerId), c.Placement),
                _exitWithin, _proposed![modelId].Horizontal, _volumes, _size) ?? c.Placement.Yaw,
        _ => new Yaw(0),
    };

    void OnDragged(Guid modelId, Point3 position) { if (_proposed is not null && _proposed.ContainsKey(modelId)) _proposed[modelId] = position; }

    /// <summary>A sealed Ask withholds every answer until it closes; the closing frame carries them all, and that frame is
    /// the reveal — shown at once, for the teams that answered and for the team that did not (client shell → Asks and windows).</summary>
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

    async Task Answer(Ask ask, AskAnswer answer)
    {
        _input.ModelDragged -= OnDragged; _input.Proposing.Clear(); _input.Allowance = null; _proposed = null;
        _view.ClearExitGhosts();                                              // the occupants go back to having no node until the event lands
        _lastCommand = await _s.SendCommandAsync(new AnswerAsk(ask.Id, answer)); _region?.Hide();
    }
    void ClearControls() { foreach (var c in _box.GetChildren()) if (c != _title && c != _note) c.QueueFree(); _view.ClearExitGhosts(); foreach (var n in _view.Pieces.OfType<PieceNode>()) n.SetHighlight(null); }
    static Button Btn(string text, Action a) { var b = new Button { Text = text }; b.Pressed += a; return b; }
}
```

**The round boundary needs no control of its own.** The round does not advance by itself, and the scheduler stops at the boundary with one unframed `Confirmation` Ask (question `AskQuestions.EndRound`, `AllowPass` false) addressed to every team that has a participant. The panel renders it as it stands — `AskViewModel.Mine` matches on `Teams.Contains(myTeam)` and does not care that several teams are named, `Highlights` is empty for a `Confirmation`, `AutoPass` cannot fire (no `WindowKind`), and there is no Pass button because the Ask allows none — so the two words this plan adds are the button's label (`ConfirmLabel`) and the answer's log line ("Clay confirmed for Team 1", with the engine's "started round 2" under it). No end-round button goes on the game screen or the team panel: the spec's *"one Ask panel, always in the same place, rendering whatever Ask is currently addressed to the player's team"* is the whole affordance, a second control would be a second path to the same command, and the menu already has *Set the round* for a player who wants to jump. Because the Ask is unsealed, whoever answers first ends the round and the panel of every other player replaces it with the next round's turn Ask on the same `EventsApplied` tick; the answering player is named in the log line, as with any answer.

While a declared move is open the panel fills `TableInput.Proposing`, so the drag streams `proposed` ghosts and the release sends no `MoveModels`; the panel's Commit sends `Destinations`, whose `To.Z` is the client's settled height (contracts plan Task 8). The ruler during that drag reads `MoveRuler.Read(start, current, remaining)` — `TableInput` passes `Proposing.Contains(lead) ? _screenAllowance : null`, a `double? Allowance` property the panel sets alongside `Proposing`.

**A disembark is the same panel with three things swapped.** `MoveRuler.IsDisembark` splits `RenderMove` in two, and the disembark half differs only in where the region comes from, where the ghosts start, and what the readout says:

| | an ordinary move | a disembark (`Containment.ExitingId` set) |
| --- | --- | --- |
| region | `MoveRuler.Region` from the start placement | `MoveRuler.ExitRegion` about the container's base |
| draggable node | the model's own `PieceNode` | `TableView.ShowExitGhosts` on the container's node |
| `TableInput.Allowance` | `Allowance − UsedInFrame` | `null` — `Allowance` binds nothing here (contracts plan Task 8) |
| the ruler | start → current, with the remaining allowance | start (the container) → current, distance only |
| `ModelRelocation.From` | the model's `OnTable` | the model's `Inside`, passed straight through |
| yaw kept | the model's own | whatever fits at the drop point, through `YawOf` → `MoveRuler.ExitYawAt` |

Everything else is shared: `Proposing`, the `proposed` presence stream, the Commit button, `Answer`, the `Refused` line. The ruler still draws — from the container's placement, because `TableInput.Position` resolves an `Inside` model to its container (Task 12) — and it shows a bare distance rather than a remaining allowance, which is right: p.14 constrains where the model may *end*, not how far it travels. The exit distance is on the panel's note instead.

**Embarking answers with the unit, not the model.** `OfferEmbark` sends one `ModelRelocation` per model of the unit, all to the same `Inside(container)`, because p.15 embarks the whole unit when one model reaches the transport; the server's `MoveStep.Apply` takes that as sent and only drops an `Inside` whose container is not on the table (interpreter plan Task 14). There is no per-reaching-model answer and no partial embark.

- [ ] **Step 3: Write `GameScreen.Play.cs`**

```csharp
using Godot;
using Tabletop.Client.Panels;

namespace Tabletop.Client.Screens;

public partial class GameScreen
{
    AskPanel? _askPanel;

    partial void ShowPlayPanels(bool playing)
    {
        if (!playing) { _askPanel?.QueueFree(); _askPanel = null; return; }
        if (_askPanel is not null) return;
        _askPanel = new AskPanel(); _side.AddChild(_askPanel); _askPanel.Bind(_session, Table, Input);
    }
}
```

- [ ] **Step 4: Run it against the game server skeleton.** With `InertEngine` no Ask ever opens, so exercise the panel with the skeleton's test engine: temporarily register `FixedAskEngineFactory` with an activate Ask in `server/game/Program.cs` (or run the scheduler plan's server once it exists). Expected: the option buttons appear for the addressed team, "Waiting for Team 1…" on the other client, the choice logs `ask_answered` and `ask_closed`, the panel shows who answered; muting `casting` in settings auto-passes a simultaneous token spend. Then, once the interpreter plan's casting is in the server, cast with two token holders in reach: the panel shows one spin box per eligible model of my own team and none for the other team's spenders, each box stops at that model's own token count, there is no total to satisfy, the other team's panel says "Team 2 has answered" without saying what, and the close prints *"Revealed: Team 1 spent 2 tokens (…), Team 2 spent 1 token (…)"* with the same words in the log. A Takedown's shot opens a pick over the target unit's models: the models highlight rather than the unit, one click takes it and drags nothing, the panel names the model it took, and the log line reads *"Clay answered for Team 1: picked"* followed by that model's unit and slot name. Once the scheduler plan's server exists, play a two-team round out: at the boundary both clients show one button reading "End the round", either player's click starts the next round, and the log reads *"… confirmed for Team 2"* then *"the game started round 2"*. Then, once the interpreter plan's `Transport(X)` is in the server, play the two containment paths: with a transport in reach the move Ask's note names it and it is highlighted, one click puts **every** model of the unit inside it and the log reads *"Clay put Battle Brothers inside Bus (Bus)"* on one line, and the occupants' nodes vanish from the table; activating that unit next turn opens the disembark offer instead — the region is a rounded-square band 6" out from the transport's base rather than a circle about its centre, one ghost per model sits on the transport, the note reads *"place every model fully within 6.0\" of Bus (Bus)"* with no remaining-allowance figure, dragging a ghost draws a ruler with a distance and no allowance, and Commit sends `Destinations` whose `From` is `Inside`. A ghost dropped outside the region still commits and the server leaves that model inside — the offer is drawn, not enforced, here. `./scripts/check-client` prints nothing. Commit: `Add the Ask panel and the declared move's region, ghost and commit`.
