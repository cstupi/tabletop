using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Table;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Projection = Tabletop.Protocol.Projection;

namespace Tabletop.Client.Panels;

/// <summary>One panel, always in the same place, rendering whatever is addressed to the player's team.</summary>
public partial class AskPanel : PanelContainer
{
    Session _s = null!; TableView _view = null!; TableInput _input = null!;
    readonly VBoxContainer _box = new(); readonly Label _title = new(), _note = new(), _refusalLabel = new() { Modulate = Colors.Orange }; MoveRegionNode? _region;
    Guid? _renderedAsk, _lastCommand; readonly List<Guid> _picked = new(); Dictionary<Guid, Point3>? _proposed;
    readonly HashSet<Guid> _sealed = new(); string? _revealed, _refusal; Guid? _refusalAsk;
    // What YawOf needs from the disembark render: the exit distance, the board's volumes and its size.
    double _exitWithin; IReadOnlyList<WorldVolume> _volumes = []; TableSize _size = new(72, 48);
    // Session outlives this panel: every subscription is a field and comes off in _ExitTree. _unhook holds the per-Ask ones.
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action? _onChanged; Action<Refused>? _onRefused; Action? _unhook;

    public void Bind(Session s, TableView view, TableInput input)
    {
        _s = s; _view = view; _input = input;
        AddChild(_box); _box.AddChild(_title); _box.AddChild(_note); _box.AddChild(_refusalLabel);
        view.AddChild(_region = new MoveRegionNode());
        _onEvents = events => { Reveal(events); Render(); }; _onChanged = Render;
        _onRefused = r => { if (r.MessageId == _lastCommand) { _refusal = $"The server refused: {r.Detail}"; _refusalAsk = _renderedAsk; _renderedAsk = null; Render(); } };
        s.EventsApplied += _onEvents; s.GameChanged += _onChanged; s.BoardChanged += _onChanged; s.RefusedReceived += _onRefused;
        Render();
    }

    public override void _ExitTree()
    {
        if (_s is null) return;
        _s.EventsApplied -= _onEvents; _s.GameChanged -= _onChanged; _s.BoardChanged -= _onChanged; _s.RefusedReceived -= _onRefused;
        Unhook();
        if (_input is not null && IsInstanceValid(_input)) { _input.Proposing.Clear(); _input.Allowance = null; }
        if (_region is not null && IsInstanceValid(_region)) { _region.Hide(); _region.QueueFree(); }
    }

    void Unhook() { _unhook?.Invoke(); _unhook = null; ResetMove(); }

    /// <summary>A move Ask's table state, dropped whenever the Ask ends by any route: our answer, a teammate's, a server close, a replacing Ask.</summary>
    void ResetMove()
    {
        if (_input is not null && IsInstanceValid(_input)) { _input.Proposing.Clear(); _input.Allowance = null; }
        if (_region is not null && IsInstanceValid(_region)) _region.Hide();
    }

    /// <summary>The refusal or send error has its own label, set (not appended) on every render, so N renders show it once.</summary>
    void Render() { RenderAsk(); _refusalLabel.Text = _refusal ?? ""; }

    void RenderAsk()
    {
        var p = _s.Projection; var game = _s.Game;
        if (p is null || game is null) return;
        var mine = AskViewModel.Mine(p, game, _s.MyTeamId, _s.Settings.MutedWindowKinds);
        if (mine?.Ask.Id != _refusalAsk) _refusal = null;                       // a different Ask, or none: the refusal is spent
        if (mine is null)
        {
            ClearControls(); Unhook(); _renderedAsk = null; _region?.Hide();
            _title.Text = AskViewModel.WaitingFor(p, game, _s.MyTeamId) is { } teams ? $"Waiting for {teams}…" : (p.OpenAsks.Count == 0 ? "" : "Waiting…");
            _note.Text = _revealed ?? "";                                  // a sealed Ask's reveal stays up until the next Ask
            if (DeployView.MidDeploy(p, _s.Log, _s.MyTeamId) is { } mid) RenderDeploy(mid, new Names(game, p));
            return;
        }
        if (_renderedAsk == mine.Ask.Id) return;
        ClearControls(); Unhook(); _renderedAsk = mine.Ask.Id; _picked.Clear(); _proposed = null; _note.Text = ""; _revealed = null;
        _title.Text = mine.Ask.Prompt;
        foreach (var id in mine.Highlights) _view.PieceAt(id)?.SetHighlight(Palette.Selected);
        if (mine.AutoPass && _refusal is null) {   // a failed auto-pass falls through to the normal render with its Pass button, so a dead socket cannot loop
            Send(mine.Ask, new Passed()); _note.Text = $"auto-passed (muted: {mine.Ask.WindowKind})"; _box.AddChild(Btn("Unmute", () => _s.UpdateSettings(x => x.Unmute(mine.Ask.WindowKind!)))); return; }
        var names = new Names(game, p);
        switch (mine.Mode)
        {
            case AskMode.Choose:
                foreach (var c in ((Options)mine.Ask.Legal).Choices)
                {
                    var b = Btn(c.Label, () => Send(mine.Ask, new Chosen(c.Id)));
                    var pieces = c.Pieces; var lit = mine.Highlights;
                    b.MouseEntered += () => { foreach (var id in pieces) if (_view.PieceAt(id) is { } n && IsInstanceValid(n)) n.SetHighlight(Palette.Selected); };
                    b.MouseExited += () => { foreach (var id in pieces) if (_view.PieceAt(id) is { } n && IsInstanceValid(n)) n.SetHighlight(lit.Contains(id) ? Palette.Selected : null); };
                    _box.AddChild(b);
                }
                break;
            case AskMode.PickPieces:
                var sel = (PieceSelection)mine.Ask.Legal; var count = new Label { Text = $"0 of {sel.Min}–{sel.Max}" }; _box.AddChild(count);
                Button done = null!; done = Btn("Done", () => { _input.PieceLeftClicked -= OnPick; Send(mine.Ask, new SelectedPieces(_picked.ToList())); });
                void Refresh() { count.Text = sel.Max == 1 && _picked.Count == 1 ? names.Piece(_picked[0]) : $"{_picked.Count} of {sel.Min}–{sel.Max}"; done.Disabled = _picked.Count < sel.Min || _picked.Count > sel.Max; }
                // A shelved unit has no node to click (a deploy), so the panel lists it; a click on the table still picks the ones that have one.
                foreach (var shelved in DeployView.OffTable(p, sel.Eligible))
                {
                    var toggle = new Button { Text = names.Piece(shelved), ToggleMode = true };
                    toggle.Toggled += on =>
                    {
                        if (on && _picked.Count >= sel.Max) { toggle.SetPressedNoSignal(false); return; }
                        if (on) _picked.Add(shelved); else _picked.Remove(shelved);
                        Refresh();
                    };
                    _box.AddChild(toggle);
                }
                _input.PieceLeftClicked += OnPick; _unhook += () => _input.PieceLeftClicked -= OnPick;
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
                        Refresh();
                        _view.PieceAt(pick)?.SetHighlight(Palette.Ghost);
                    }
                }
                _box.AddChild(done); Refresh();
                break;
            case AskMode.Allocate:
                var alloc = (Allocation)mine.Ask.Legal; var spins = alloc.Targets.ToDictionary(t => t, t => new SpinBox { MinValue = 0, MaxValue = alloc.Amount, Prefix = names.Piece(t) });
                foreach (var sp in spins.Values) _box.AddChild(sp);
                _box.AddChild(Btn($"Done (assign {alloc.Amount})", () => Send(mine.Ask, new Allocated(spins.ToDictionary(kv => kv.Key, kv => (int)kv.Value.Value)))));
                break;
            case AskMode.Assign:
                var asg = (Assignment)mine.Ask.Legal; var picks = new Dictionary<string, OptionButton>();
                foreach (var item in asg.Items) { var ob = new OptionButton(); foreach (var t in asg.Targets) ob.AddItem(names.Piece(t)); _box.AddChild(new Label { Text = item.Label }); _box.AddChild(ob); picks[item.Id] = ob; }
                _box.AddChild(Btn("Done", () => Send(mine.Ask, new Assigned(picks.ToDictionary(kv => kv.Key, kv => asg.Targets[kv.Value.Selected])))));
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
                _box.AddChild(Btn("Spend", () => Send(mine.Ask,
                    new TokensSpent(spends.Where(kv => kv.Value.Value > 0).ToDictionary(kv => kv.Key, kv => (int)kv.Value.Value)))));
                if (mine.AnsweredTeams.Count > 0) _box.AddChild(new Label { Text = string.Join(", ", mine.AnsweredTeams.Select(t => $"{t} has answered")) });
                break;
            case AskMode.Confirm: _box.AddChild(Btn(AskViewModel.ConfirmLabel(mine.Ask), () => Send(mine.Ask, new Confirmed()))); break;   // "Continue" for a step gate, "End the round" at the round boundary
        }
        if (mine.Ask.AllowPass) _box.AddChild(Btn("Pass", () => Send(mine.Ask, new Passed())));
        if (mine.Ask.WindowKind is { } kind) _box.AddChild(Btn($"Mute '{kind}' windows", () => _s.UpdateSettings(x => x.Mute(kind))));
        if (AskViewModel.AnsweredBy(_s.Log, mine.Ask.Id, names) is { } who) _note.Text = $"{who} answered for your team";
    }

    /// <summary>The server is waiting for every model of the unit just picked to be on the table: one click puts the shelved ones in a row there, and the player drags them into place.</summary>
    void RenderDeploy(MidDeploy mid, Names names)
    {
        _title.Text = $"Place the models of {names.Unit(mid.UnitId)}";
        _box.AddChild(Btn("Place on table", () =>
        {
            _note.Text = "Click the table where the unit goes.";
            _input.PickNextPoint(at =>
            {
                if (_s.Projection is not { } now) return;
                var models = now.Models.Values.Where(m => m.UnitId == mid.UnitId && m.Location is OnShelf).OrderBy(m => m.Slot.Ordinal).ToList();
                if (models.Count == 0) return;
                _note.Text = "";
                Guarded.Command(_s, new MoveModels(DeployView.Layout(models, at, _view.Size)), message => { _refusal = $"Could not place the models: {message}"; _refusalAsk = null; Render(); });
            });
        }));
    }

    void RenderMove(Ask ask, MoveRegion legal, Projection p, Names names)
    {
        if (legal.ModelIds.Count == 0) return;                                      // nothing to move; the server closes the Ask
        var lead = p.Models[legal.ModelIds[0]];
        var body = BodyOf(p, lead.Id);
        var ordinal = _s.Game?.Teams.FirstOrDefault(t => t.Id == _s.MyTeamId)?.Ordinal ?? 1;
        _input.Proposing.Clear(); foreach (var id in legal.ModelIds) _input.Proposing.Add(id);
        _input.ModelDragged += OnDragged; _unhook += () => _input.ModelDragged -= OnDragged;

        if (MoveRuler.IsDisembark(legal))
        {
            // The unit is inside, so there is no start placement to reach from and the straight-line region is meaningless:
            // the offer is p.15's "fully within 6" of it when exiting", drawn about the container's own base. Allowance is the
            // action's remaining distance and binds nothing here, so no remaining-allowance readout goes up and the ruler gets none.
            var containerId = legal.Containment!.ExitingId!.Value;
            if (p.Models.GetValueOrDefault(containerId)?.Location is not OnTable at)
            {
                _note.Text = "The container is no longer on the table.";               // it was wrecked and shelved; the server will close this Ask
                Unhook();
                return;
            }
            var container = new PlacedModel(BodyOf(p, containerId), at.Placement);
            var within = legal.Containment.ExitWithin ?? 0;
            (_exitWithin, _volumes, _size) = (within, _view.Volumes, _view.Size);
            _region!.Show(MoveRuler.ExitRegion(body, container, legal, _volumes, _size), Palette.Team(ordinal));
            _view.ShowExitGhosts(legal.ModelIds, containerId);                          // Inside carries no Placement: the ghosts start on the container's node
            _note.Text = $"Disembark: place every model fully within {within:0.0}\" of {names.Piece(containerId)}. Drag them out, then commit.";
            _input.Allowance = null;
            _proposed = legal.ModelIds.ToDictionary(id => id, _ => at.Placement.Position);
        }
        else
        {
            if (lead.Location is not OnTable on) { Unhook(); return; }
            var frame = ask.FrameId is { } f ? p.Stack.FirstOrDefault(x => x.Id == f) : null;
            var used = frame is null ? 0 : MoveRuler.UsedInFrame(frame, lead.Id);
            var remaining = Math.Max(0, legal.Allowance - used);
            _region!.Show(MoveRuler.Region(new PlacedModel(body, on.Placement), legal with { Allowance = remaining }, _view.Volumes, _view.Size), Palette.Team(ordinal));
            _note.Text = $"Move up to {remaining:0.0}\" (used {used:0.0}\"). Drag the models, then commit.";
            _input.Allowance = remaining;
            _proposed = legal.ModelIds.ToDictionary(id => id, id => ((OnTable)p.Models[id].Location).Placement.Position);
            _view.Snap(legal.ModelIds);                                                  // drawn and committed agree: a rebuilt Ask (a refusal) starts from the server's positions
            OfferEmbark(ask, legal, p, names);
        }
        _box.AddChild(Btn("Commit move", () => { if (_proposed is null) return; Send(ask, new Destinations(legal.ModelIds.Select(id =>
            new ModelRelocation(id, p.Models[id].Location, new OnTable(new Placement(_proposed![id], YawOf(p, id))))).ToList())); }));
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
        _input.PieceLeftClicked += OnEmbark; _unhook += () => _input.PieceLeftClicked -= OnEmbark;
        void OnEmbark(Guid clicked)
        {
            if (_renderedAsk != ask.Id) { _input.PieceLeftClicked -= OnEmbark; return; }   // a later Ask is up: this closure is stale
            if (!enterable.Contains(clicked)) return;
            _input.PieceLeftClicked -= OnEmbark;
            _input.CancelDrag();
            Send(ask, new Destinations(legal.ModelIds.Select(id => new ModelRelocation(id, p.Models[id].Location, new Inside(clicked))).ToList()));
        }
    }

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

    static ModelBody BodyOf(Projection p, Guid modelId) { var m = p.Models[modelId]; return new ModelBody(m.Slot.Base, m.Slot.Height); }

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

    void Send(Ask ask, AskAnswer answer) => Guarded.Run(() => Answer(ask, answer), message => Report(ask.Id, message));

    /// <summary>A failed send is logged. If the Ask is still the one on screen, rebuild it from scratch (its hooks were dropped by the answer) and show the error; a later Ask is left alone.</summary>
    void Report(Guid askId, string message)
    {
        GD.PushWarning($"Answer failed: {message}");
        if (!IsInstanceValid(this) || _renderedAsk != askId) return;
        _refusal = $"Could not send the answer: {message}"; _refusalAsk = askId; _renderedAsk = null; Render();
    }

    void SetButtons(bool disabled) { foreach (var b in _box.GetChildren().OfType<BaseButton>()) if (IsInstanceValid(b)) b.Disabled = disabled; }

    async Task Answer(Ask ask, AskAnswer answer)
    {
        SetButtons(true); Unhook(); _proposed = null;                           // one answer per Ask: no second click can double-send
        _view.ClearExitGhosts();                                              // the occupants go back to having no node until the event lands
        _lastCommand = Guid.CreateVersion7();                                   // before the send, so a Refused that lands first still matches
        await _s.SendCommandAsync(new AnswerAsk(ask.Id, answer), _lastCommand.Value);
    }
    void ClearControls() { ResetMove(); foreach (var c in _box.GetChildren()) if (c != _title && c != _note && c != _refusalLabel) c.QueueFree(); _view.ClearExitGhosts(); foreach (var n in _view.Pieces.OfType<PieceNode>()) n.SetHighlight(null); }
    static Button Btn(string text, Action a) { var b = new Button { Text = text }; b.Pressed += a; return b; }
}
