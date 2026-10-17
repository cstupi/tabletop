using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Panels;
using Tabletop.Client.Table;
using Tabletop.Client.Theming;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Projection = Tabletop.Protocol.Projection;

namespace Tabletop.Client.Hud;

/// <summary>The right column, above the undo and log buttons: the first open Ask addressed to my team (AskViewModel.Mine), or what
/// the game is waiting on. Buttons, Enter, Esc, 1–9 and table clicks answer it; the hint line under it lists the keys that apply.
/// The controls scroll once the box reaches the top of its slot; the title, notes and hint stay in view.</summary>
public partial class DecisionBox : PanelContainer
{
    Session _s = null!; TableView _view = null!; TableInput _input = null!;
    readonly VBoxContainer _controls = new();
    readonly Label _title = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart }, _note = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart },
        _refusalLabel = new() { ThemeTypeVariation = Variation.Warning, AutowrapMode = TextServer.AutowrapMode.WordSmart }, _revealLabel = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart }, _hint = new() { ThemeTypeVariation = Variation.Dim };
    MoveRegionNode? _region;
    readonly ScrollContainer _scroll = new() { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.Reserve };
    HudRect? _slot; bool _fitQueued;
    Guid? _renderedAsk, _lastCommand, _refusalAsk; string? _revealed, _refusal; readonly SealedReveals _reveals = new();
    IReadOnlyList<(int Key, string Label, string OptionId)> _numbered = [];
    // The open Ask's handlers: set by its render, dropped by Unhook whenever the Ask ends by any route.
    Action? _onEnter, _onStop; Func<bool>? _onCancel; Action<string>? _onNumber; Action<Guid>? _onTarget; Action<Guid, bool>? _onModel; Action<Guid>? _onUnit;
    /// <summary>What glows on the table for the open Ask: model ids, unit ids, or both. A unit id lights every model of the unit.</summary>
    readonly HashSet<Guid> _glow = new();
    // Session and the input outlive the box: every subscription is a field and comes off in _ExitTree.
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action<RenderCause>? _onFrame; Action<Refused>? _onRefused;
    Action<Guid>? _onTargetClicked; Action<Guid, bool>? _onModelClicked; Action<Guid, Point3>? _onDragged; Action<Guid>? _onUnitClicked; Action<IReadOnlyList<Guid>>? _onPickedUp;

    /// <summary>PickPieces, Assign and Allocate: DecisionBox.Picks.cs (Task 16b).</summary>
    partial void RenderPicks(AskView mine, Projection p, Names names);
    /// <summary>The picks' own state (target assignment, casualty marks), dropped with the Ask's handlers.</summary>
    partial void UnhookPicks();

    public void Bind(Session s, TableView view, TableInput input)
    {
        _s = s; _view = view; _input = input;
        CustomMinimumSize = new Vector2(420, 0);
        var box = new VBoxContainer(); AddChild(box);
        _controls.SizeFlagsHorizontal = SizeFlags.ExpandFill; _scroll.AddChild(_controls);
        foreach (var c in new Control[] { _title, _revealLabel, _note, _scroll, _refusalLabel, _hint }) box.AddChild(c);
        _controls.MinimumSizeChanged += QueueFit;                                  // a ScrollContainer reports no content height
        view.AddChild(_region = new MoveRegionNode());
        _onEvents = Reveal; _onFrame = _ => Render();   // the batch for the sealed reveal; the render once per frame, on every cause
        _onRefused = r =>
        {
            if (r.MessageId != _lastCommand) return;
            _refusal = $"The server refused: {r.Detail}"; _refusalAsk = _renderedAsk;
            Reset(); _renderedAsk = null; Render();
        };
        _onTargetClicked = id => _onTarget?.Invoke(id); _onModelClicked = (id, right) => _onModel?.Invoke(id, right); _onDragged = OnDragged;
        _onUnitClicked = id => _onUnit?.Invoke(id); _onPickedUp = ShowRegions;
        s.EventsApplied += _onEvents; s.Frame.Render += _onFrame; s.RefusedReceived += _onRefused;
        input.TargetClicked += _onTargetClicked; input.ModelClicked += _onModelClicked; input.ModelDragged += _onDragged; input.UnitClicked += _onUnitClicked; input.PickedUp += _onPickedUp;
        Render();
    }

    public override void _ExitTree()
    {
        if (_s is null) return;
        _s.EventsApplied -= _onEvents; _s.Frame.Render -= _onFrame; _s.RefusedReceived -= _onRefused;
        if (IsInstanceValid(_input)) { _input.TargetClicked -= _onTargetClicked; _input.ModelClicked -= _onModelClicked; _input.ModelDragged -= _onDragged; _input.UnitClicked -= _onUnitClicked; _input.PickedUp -= _onPickedUp; }
        Unhook();
        if (_region is not null && IsInstanceValid(_region)) _region.QueueFree();
    }

    /// <summary>Into its HudLayout slot: the slot's width, the content's height up to the slot's, against the slot's bottom.</summary>
    public void Place(HudRect slot) { _slot = slot; Fit(); HudPlacement.Bottom(this, slot); }

    void QueueFit() { if (_fitQueued) return; _fitQueued = true; Callable.From(() => { _fitQueued = false; Fit(); }).CallDeferred(); }

    /// <summary>The controls' height, or what the slot leaves after the title, notes, hint and the panel's chrome. A change raises this
    /// part's MinimumSizeChanged, and the screen places it again. Whole pixels: a box container sums its children's minimums as
    /// integers, so a fractional height (a slot from an odd window height) reads back as different chrome every pass and never settles.</summary>
    void Fit()
    {
        if (_slot is not { } slot || !IsInstanceValid(_scroll)) return;
        var chrome = GetCombinedMinimumSize().Y - _scroll.GetCombinedMinimumSize().Y;
        _scroll.CustomMinimumSize = new Vector2(0, MathF.Floor(Math.Max(0, Math.Min(_controls.GetCombinedMinimumSize().Y, slot.H - chrome))));
    }

    /// <summary>The refusal or send error has its own label, set (not appended) on every render, so N renders show it once.</summary>
    void Render()
    {
        RenderAsk();
        _refusalLabel.Text = _refusal ?? ""; _refusalLabel.Visible = _refusal is not null;
        _hint.Visible = _hint.Text != "";
        _revealLabel.Text = _revealed ?? ""; _revealLabel.Visible = _revealed is not null;
        Visible = _title.Text != "" || _note.Text != "" || _refusal is not null || _revealed is not null;
    }

    void RenderAsk()
    {
        var p = _s.Projection; var game = _s.Game;
        if (p is null || game is null || game.Status != GameStatus.Playing) { Reset(); _renderedAsk = null; _title.Text = _note.Text = _hint.Text = ""; return; }
        var fresh = _reveals.Drain();                                           // every reveal since the last render, in order
        if (fresh.Count > 0) _revealed = string.Join("\n", fresh);
        var mine = AskViewModel.Mine(p, game, _s.MyTeamId, _s.Settings.MutedWindowKinds);
        if (mine?.Ask.Id != _refusalAsk) _refusal = null;                       // a different Ask, or none: the refusal is spent
        var names = new Names(game, p);
        var model = DecisionView.Of(p, game, names, _s.MyTeamId, _s.Settings.MutedWindowKinds);
        if (mine is null)
        {
            var mid = DeployView.MidDeploy(p, _s.Log, _s.MyTeamId);
            if (_renderedAsk is not null || mid?.UnitId != _deployUnit) { Reset(); _renderedAsk = null; }
            _note.Text = "";                                                    // a sealed Ask's reveal (its own line) stays up until the next Ask
            if (mid is null) { _title.Text = HeaderView.Waiting(p, names) ?? ""; _hint.Text = ""; }   // the header's waiting line, its one source
            else if (_deployUnit is null) StartDeploy(mid, names);
            return;
        }
        if (_renderedAsk == mine.Ask.Id) return;
        Reset(); _renderedAsk = mine.Ask.Id; _note.Text = "";
        if (fresh.Count == 0) _revealed = null;                                 // a reveal is cleared only after a render has shown it
        _title.Text = model.Title; _hint.Text = model.Hint; _numbered = model.Numbered;
        Glow(mine.Highlights);
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

    /// <summary>
    /// Numbered options. A click on a piece exactly one choice names picks that choice. A piece several choices name (activate: one
    /// option per unit and action) is not a pick: the click selects its unit as any click does, and the box narrows to that unit's
    /// options, renumbered from 1, until another unit is clicked or Esc widens it again.
    /// </summary>
    void RenderChoose(AskView mine)
    {
        var options = (Options)mine.Ask.Legal; var all = options.Choices;
        var list = new VBoxContainer(); Add(list);
        var narrowed = false;
        var full = _numbered;
        void Show(IReadOnlyList<AskOption> shown, IReadOnlyList<(int Key, string Label, string OptionId)> numbered)
        {
            if (!IsInstanceValid(list)) return;
            foreach (var c in list.GetChildren()) { list.RemoveChild(c); c.QueueFree(); }
            _numbered = numbered;
            foreach (var row in DecisionView.Rows(shown))
            {
                if (row.Option is not { } c)
                {
                    list.AddChild(new Label { Text = row.Note, ThemeTypeVariation = Variation.Warning, AutowrapMode = TextServer.AutowrapMode.WordSmart });
                    continue;
                }
                var key = _numbered.FirstOrDefault(n => n.OptionId == c.Id).Key;
                var b = Btn(key > 0 ? $"{key}. {c.Label}" : c.Label, () => Send(mine.Ask, new Chosen(c.Id)));
                var pieces = c.Pieces;
                b.MouseEntered += () => Light(pieces, Palette.Selected);
                b.MouseExited += () => Light(pieces, null);
                list.AddChild(b);
            }
        }
        Show(all, full);
        _onNumber = option => Send(mine.Ask, new Chosen(option));
        _input.Eligible = DecisionView.ClickAnswers(options);
        _onTarget = clicked =>
        {
            var hits = all.Where(c => Resolve(clicked, c.Pieces) is not null).ToList();
            if (hits.Count == 1) Send(mine.Ask, new Chosen(hits[0].Id));
        };
        _onUnit = unit =>
        {
            if (_s.Projection is not { } p) return;
            var hits = DecisionView.ChoicesOf(options, p, unit);
            if (hits.Count == 0) { if (narrowed) { narrowed = false; Show(all, full); } return; }
            narrowed = true; Show(hits, DecisionView.Number(hits));
        };
        _onCancel = () => { if (!narrowed) return false; narrowed = false; Show(all, full); return true; };
    }

    /// <summary>The open Ask's glow: its pieces lit, a unit id lighting each of the unit's models. Pieces that leave the set go dark.</summary>
    void Glow(IEnumerable<Guid> ids)
    {
        var next = ids.ToHashSet();
        foreach (var n in _view.Pieces.OfType<PieceNode>())
        {
            if (!IsInstanceValid(n)) continue;
            bool was = Glows(n), now = next.Contains(n.ModelId) || next.Contains(n.UnitId);
            if (was != now) n.SetHighlight(now ? Palette.Selected : null);
        }
        _glow.Clear(); _glow.UnionWith(next);
    }

    /// <summary>Whether a model glows for the open Ask: GameScreen puts the glow back where the selection leaves a model.</summary>
    public bool Glows(PieceNode n) => _glow.Contains(n.ModelId) || _glow.Contains(n.UnitId);

    /// <summary>A choice's pieces lit while its button is hovered, then back to the Ask's glow.</summary>
    void Light(IReadOnlyList<Guid> pieces, Color? colour)
    {
        foreach (var n in _view.Pieces.OfType<PieceNode>())
            if (IsInstanceValid(n) && (pieces.Contains(n.ModelId) || pieces.Contains(n.UnitId))) n.SetHighlight(colour ?? (Glows(n) ? Palette.Selected : null));
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
        var dragging = _input.Holding || Godot.Input.IsMouseButtonPressed(MouseButton.Left);   // a drag in hand owns the digits (rows) and Esc, and holds off Enter
        var used = intent switch
        {
            >= Intent.Option1 and <= Intent.Option9 when !dragging => Number(intent - Intent.Option1 + 1),
            Intent.Confirm when !dragging && _onEnter is { } enter => Run(enter),   // a commit mid-drag would race the drop's own send
            Intent.Cancel when !dragging => _onCancel?.Invoke() == true,
            Intent.StopHere when !dragging && _onStop is { } stop => Run(stop),
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
        _reveals.Read(events, new Names(_s.Game, _s.Projection));
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
        _onEnter = null; _onStop = null; _onCancel = null; _onNumber = null; _onTarget = null; _onModel = null; _onUnit = null; _glow.Clear();
        _proposed = null; _deployAt = null; _deployUnit = null; _regate = null; _gate = new(true, null); _regions = null;
        if (_input is not null && IsInstanceValid(_input)) _input.PlacementPoint = null;   // a deploy's unused point pick would eat the next table click
        UnhookPicks();
        if (_input is not null && IsInstanceValid(_input)) { _input.Proposing.Clear(); _input.Allowance = null; _input.Minimum = null; _input.Region = null; _input.Reach = null; _input.Eligible = new HashSet<Guid>(); }
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
