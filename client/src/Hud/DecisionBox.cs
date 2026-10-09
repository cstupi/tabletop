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
        SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom, LayoutPresetMode.Minsize, 8);
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
        var dragging = _input.Holding || Godot.Input.IsMouseButtonPressed(MouseButton.Left);   // a drag in hand owns the digits (rows) and Esc
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
        if (_pointArmed && _input is not null && IsInstanceValid(_input)) _input.PickNextPoint(null!);   // a deploy's unused point pick would eat the next table click
        _pointArmed = false;
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
