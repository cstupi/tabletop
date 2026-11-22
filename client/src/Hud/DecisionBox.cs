using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Panels;
using Tabletop.Client.Table;
using Tabletop.Client.Theming;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Projection = Tabletop.Protocol.Projection;

namespace Tabletop.Client.Hud;

/// <summary>The right column, above the undo and log buttons: the first open Ask addressed to my team (AskViewModel.Mine) as a card (UX redesign
/// part 2 → Decision box): a title band in my team's colour with the Ask's icon, the body per mode, the actions at the foot with their key caps;
/// or, with nothing of mine open, a quiet band per team the game waits on. Buttons, Enter, Esc, 1–9 and table clicks answer it. The body's
/// controls scroll once the card reaches the top of its slot; the band, the notes and the foot stay in view.</summary>
public partial class DecisionBox : PanelContainer
{
    Session _s = null!; TableView _view = null!; TableInput _input = null!;
    readonly VBoxContainer _controls = new();
    /// <summary>The card's title band: the Ask's icon, its title in the display font, the unit it is about, "Team 2" for a client holding several, ⌨.</summary>
    readonly PanelContainer _band = new();
    readonly TextureRect _icon = DecisionTiles.Icon(null, 22);
    readonly Label _title = new() { ThemeTypeVariation = Variation.Display, Uppercase = true, VerticalAlignment = VerticalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill },
        _seat = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly Button _keys = new() { FocusMode = FocusModeEnum.None, ThemeTypeVariation = Variation.FlatButton, TooltipText = "Keys", ExpandIcon = true, CustomMinimumSize = new Vector2(28, 28) };
    readonly Label _prompt = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart }, _note = new() { ThemeTypeVariation = Variation.Dim, AutowrapMode = TextServer.AutowrapMode.WordSmart },
        _refusalLabel = new() { ThemeTypeVariation = Variation.Failure, AutowrapMode = TextServer.AutowrapMode.WordSmart };
    readonly HFlowContainer _reveal = new(), _footRow = new();
    readonly VBoxContainer _quiet = new(), _body = new();
    readonly MarginContainer _inset = new();
    MoveRegionNode? _region;
    readonly ScrollContainer _scroll = new() { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.Reserve };
    HudRect? _slot; bool _fitQueued;
    Guid? _renderedAsk, _answering, _lastCommand, _refusalAsk; string? _refusal, _refusalDetail; readonly SealedReveals _reveals = new();
    /// <summary>The set-aside facts the drawn Ask was drawn with (SetAsideView.DrawnKey): facts that arrive after it draw it again.</summary>
    string _asideDrawn = "";
    IReadOnlyList<IReadOnlyList<RevealChip>>? _revealed; IReadOnlyList<IReadOnlyList<RevealChip>>? _revealDrawn;
    /// <summary>The card's head as drawn, and the quiet bands' key: a render redraws them only on a change.</summary>
    (CardHead? Head, int Ordinal) _head; string _quietKey = "";
    /// <summary>The Ask and placement the picked models were made under: a change clears them.</summary>
    (Guid? Ask, Guid? Deploy) _picksFor;
    IReadOnlyList<(int Key, string Label, string OptionId)> _numbered = [];
    // The open Ask's handlers: set by its render, dropped by Unhook whenever the Ask ends by any route.
    Action? _onEnter, _onStop; Func<bool>? _onCancel, _cancels; Action<string>? _onNumber; Action<Guid>? _onTarget; Action<Guid, bool>? _onModel; Action<Guid>? _onUnit; Action? _onDeselect;
    /// <summary>What glows on the table for the open Ask: model ids, unit ids, or both. A unit id lights every model of the unit.</summary>
    readonly HashSet<Guid> _glow = new();
    // Session and the input outlive the box: every subscription is a field and comes off in _ExitTree.
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action<RenderCause>? _onFrame; Action<Refused>? _onRefused;
    Action<Guid>? _onTargetClicked; Action<Guid, bool>? _onModelClicked; Action<IReadOnlyList<(Guid Id, Geometry.Point3 At)>>? _onDragged; Action<Guid>? _onUnitClicked; Action<IReadOnlyList<Guid>>? _onPickedUp; Action? _onSelection;

    /// <summary>The card's ⌨: the screen opens the key overlay.</summary>
    public event Action? KeysRequested;

    /// <summary>PickPieces, Assign and Allocate: DecisionBox.Picks.cs (Task 16b).</summary>
    partial void RenderPicks(AskView mine, Projection p, Names names);
    /// <summary>The picks' own state (target assignment, casualty marks), dropped with the Ask's handlers.</summary>
    partial void UnhookPicks();

    /// <summary>The gap between scrolled content and its bar.</summary>
    public const int ScrollGap = Space.S;

    public void Bind(Session s, TableView view, TableInput input)
    {
        _s = s; _view = view; _input = input;
        CustomMinimumSize = new Vector2(420, 0);
        ThemeTypeVariation = Variation.Card;
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 0); AddChild(box);
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", Space.S); _band.AddChild(head);
        _keys.Icon = Ui.Icon("key"); _keys.Pressed += () => KeysRequested?.Invoke();
        _keys.TooltipText = $"Keys ({Hotkeys.Bindings.Cap(Intent.KeyOverlay)})";
        foreach (var c in new Control[] { _icon, _title, _seat, _keys }) head.AddChild(c);
        // The controls keep clear of the scroll bar: a margin between their right edge and the bar's.
        var inset = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; inset.AddThemeConstantOverride("margin_right", ScrollGap);
        _controls.SizeFlagsHorizontal = SizeFlags.ExpandFill; _controls.AddThemeConstantOverride("separation", Space.S); inset.AddChild(_controls); _scroll.AddChild(inset);
        foreach (var side in new[] { "left", "right", "top", "bottom" }) _inset.AddThemeConstantOverride($"margin_{side}", Space.M);   // a box of a title and several lines: M (spacing rule 5)
        _body.AddThemeConstantOverride("separation", Space.M); _inset.AddChild(_body);
        _footRow.AddThemeConstantOverride("h_separation", Space.S); _footRow.AddThemeConstantOverride("v_separation", Space.S);
        _reveal.AddThemeConstantOverride("h_separation", Space.S); _reveal.AddThemeConstantOverride("v_separation", Space.XS);
        _choices.AddThemeConstantOverride("separation", Space.S);
        foreach (var c in new Control[] { _reveal, _prompt, _note, _scroll, _refusalLabel, _footRow }) _body.AddChild(c);
        _reveal.Visible = false;                                                    // an empty row still takes the body's separation: a gap above the first line
        _quiet.AddThemeConstantOverride("separation", Space.XS);
        foreach (var c in new Control[] { _band, _inset, _quiet }) box.AddChild(c);
        _controls.MinimumSizeChanged += QueueFit;                                  // a ScrollContainer reports no content height
        Ui.Changed += Restyle;
        view.AddChild(_region = new MoveRegionNode());
        _onEvents = Reveal; _onFrame = _ => Render();   // the batch for the sealed reveal; the render once per frame, on every cause
        _onRefused = r =>
        {
            if (r.MessageId != _lastCommand) return;
            _flight.Refused(r.MessageId);
            _refusal = $"The server refused: {r.Detail}"; _refusalDetail = r.Detail; _refusalAsk = _renderedAsk;
            KeepRefused(r);                                              // a refused move or placement comes back where it was dropped, marked
            Reset(); _renderedAsk = null; Render();
        };
        _onTargetClicked = id => _onTarget?.Invoke(id); _onModelClicked = (id, right) => _onModel?.Invoke(id, right); _onDragged = OnDragged;
        _onUnitClicked = id => _onUnit?.Invoke(id); _onPickedUp = ShowRegions;
        _onSelection = () => { if (_input.Selected is null) _onDeselect?.Invoke(); };
        s.EventsApplied += _onEvents; s.Frame.Render += _onFrame; s.RefusedReceived += _onRefused;
        input.TargetClicked += _onTargetClicked; input.ModelClicked += _onModelClicked; input.ModelsDragged += _onDragged; input.UnitClicked += _onUnitClicked; input.PickedUp += _onPickedUp; input.SelectionChanged += _onSelection;
        BindActions();
        Render();
    }

    public override void _ExitTree()
    {
        Ui.Changed -= Restyle;
        if (_s is null) return;
        _s.EventsApplied -= _onEvents; _s.Frame.Render -= _onFrame; _s.RefusedReceived -= _onRefused;
        if (IsInstanceValid(_input)) { _input.TargetClicked -= _onTargetClicked; _input.ModelClicked -= _onModelClicked; _input.ModelsDragged -= _onDragged; _input.UnitClicked -= _onUnitClicked; _input.PickedUp -= _onPickedUp; _input.SelectionChanged -= _onSelection; }
        Unhook();
        if (_region is not null && IsInstanceValid(_region)) _region.QueueFree();
        UnbindActions();
    }

    /// <summary>
    /// A theme change: the card's frame, head and bands are drawn again in the new theme's colours and icons, and so is the open Ask, made
    /// afresh (its tiles and actions too) with a move's or a placement's ghosts kept where they stood, unless its answer is in flight
    /// (AnswerInFlight). Drawn now, not at the next frame's render.
    /// </summary>
    void Restyle()
    {
        _acts.Clear();                                                                       // made again in the new theme; Reset frees the old
        if (_flight.Redraws(_renderedAsk) || _renderedAsk is null && _flight.Redraws(_deployUnit))
        {
            KeepGhosts();
            _choiceGroupsKept?.Clear();
            Reset(); _renderedAsk = null;
        }
        else foreach (var g in _choices.GetChildren().OfType<DecisionTiles.OptionPanel>()) DecisionTiles.Restyle(g);
        _keys.Icon = Ui.Icon("key"); _keys.TooltipText = $"Keys ({Hotkeys.Bindings.Cap(Intent.KeyOverlay)})"; var h = _head; _head = default; if (h.Head is { } head) Head(head, h.Ordinal); _quietKey = ""; _revealDrawn = null; _framed = null;
        Render();
    }

    /// <summary>Into its HudLayout slot: the slot's width, the content's height up to the slot's, against the slot's bottom.</summary>
    public void Place(HudRect slot) { _slot = slot; Fit(); HudPlacement.Bottom(this, slot); }

    void QueueFit() { if (_fitQueued) return; _fitQueued = true; Callable.From(() => { _fitQueued = false; Fit(); }).CallDeferred(); }

    /// <summary>The controls' height, or what the slot leaves after the band, the notes, the foot and the card's chrome. A change raises this
    /// part's MinimumSizeChanged, and the screen places it again. Whole pixels: a box container sums its children's minimums as
    /// integers, so a fractional height (a slot from an odd window height) reads back as different chrome every pass and never settles.</summary>
    void Fit()
    {
        if (_slot is not { } slot || !IsInstanceValid(_scroll)) return;
        var chrome = GetCombinedMinimumSize().Y - _scroll.GetCombinedMinimumSize().Y;
        _scroll.CustomMinimumSize = new Vector2(0, MathF.Floor(Math.Max(0, Math.Min(_controls.GetCombinedMinimumSize().Y, slot.H - chrome))));
    }

    /// <summary>The card's frame: the card's panel with no inset (the band runs edge to edge inside the frame and the body takes its own 16 px), or nothing
    /// while only quiet bands show.</summary>
    bool? _framed;
    void Frame(bool card)
    {
        if (card == _framed) return;
        _framed = card;
        if (!card) { AddThemeStyleboxOverride("panel", new StyleBoxEmpty()); return; }
        var b = (StyleBox)Ui.Theme.GetStylebox("panel", Variation.Card).Duplicate(); b.SetContentMarginAll(Ui.Current.Materials.Panel.Inset);   // the theme's, inside its drawn frame: this control's own override would answer first
        AddThemeStyleboxOverride("panel", b);
    }

    /// <summary>The title band for this head, in the answering team's colour.</summary>
    void Head(CardHead head, int ordinal)
    {
        if (_head == (head, ordinal)) return;
        _head = (head, ordinal);
        _band.AddThemeStyleboxOverride("panel", Ui.TeamBand(ordinal, compact: true));
        var on = Ui.OnBand(ordinal);
        _icon.Texture = Ui.Icon(head.Icon); _icon.Visible = _icon.Texture is not null; _icon.SelfModulate = on;
        _title.Text = head.Subject is { } subject ? $"{head.Title} · {subject}" : head.Title;
        foreach (var l in new[] { _title, _seat }) l.AddThemeColorOverride("font_color", on);
        foreach (var name in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color" }) _keys.AddThemeColorOverride(name, on);
        _prompt.Text = head.Prompt ?? "";
    }

    int MyOrdinal => _s.Game?.Teams.FirstOrDefault(t => t.Id == (_answering ?? _s.MyTeamId))?.Ordinal ?? 1;

    /// <summary>The refusal or send error has its own label, set (not appended) on every render, so N renders show it once.</summary>
    void Render()
    {
        RenderAsk();
        if ((_renderedAsk, _deployUnit) != _picksFor) { _picksFor = (_renderedAsk, _deployUnit); _input.ClearPicks(); }   // a new Ask or placement, or the end of one, clears the picked models
        _refusalLabel.Text = _refusal ?? ""; _refusalLabel.Visible = _refusal is not null;
        var card = _head.Head is not null;
        _band.Visible = card;
        _prompt.Visible = card && _prompt.Text != ""; _note.Visible = _note.Text != "";   // an empty label still takes a line
        _seat.Text = card ? Seats.PlayingAs(_s.Game!, _s.MyTeamIds, _s.MyTeamId) is not null ? $"Team {MyOrdinal}" : "" : "";
        _seat.Visible = _seat.Text != "";
        DrawReveal();
        _footRow.Visible = _footRow.GetChildren().OfType<Control>().Any(c => c.Visible);
        SyncScroll();
        var body = card || _refusal is not null || _revealed is not null || _note.Text != "";
        _inset.Visible = body;
        Frame(card || body && _quiet.GetChildCount() == 0);
        _quiet.Visible = !card && _quiet.GetChildCount() > 0;
        Visible = card || body || _quiet.Visible;
        Rendered?.Invoke();
    }

    /// <summary>Each team's choice in a sealed Ask's reveal: its band and the chip.</summary>
    void DrawReveal()
    {
        if (ReferenceEquals(_revealDrawn, _revealed)) return;
        _revealDrawn = _revealed;
        foreach (var c in _reveal.GetChildren()) { _reveal.RemoveChild(c); c.QueueFree(); }
        foreach (var chip in (_revealed ?? []).SelectMany(r => r)) _reveal.AddChild(DecisionTiles.Band(chip.Ordinal, chip.Team, chip.Choice));
        _reveal.Visible = _reveal.GetChildCount() > 0;
    }

    /// <summary>The quiet bands: one per team the game waits on, redrawn only when they change.</summary>
    void Quiet(IReadOnlyList<(int Ordinal, string Team, string Doing)> bands)
    {
        var key = string.Join("|", bands);
        if (key == _quietKey) return;
        _quietKey = key;
        foreach (var c in _quiet.GetChildren()) { _quiet.RemoveChild(c); c.QueueFree(); }
        foreach (var (ordinal, team, doing) in bands) _quiet.AddChild(DecisionTiles.Band(ordinal, team, doing));
    }

    void ClearHead() { _head = default; _prompt.Text = ""; }

    void RenderAsk()
    {
        var p = _s.Projection; var game = _s.Game;
        if (p is null || game is null || game.Status != GameStatus.Playing)
        {
            if (_s.Phase == SessionPhase.Reconnecting) KeepGhosts();
            Reset(); _renderedAsk = null; ClearHead(); _note.Text = ""; Quiet([]); return;
        }
        var fresh = _reveals.Drain();                                           // every reveal since the last render, in order
        if (fresh.Count > 0) _revealed = fresh;
        var mine = AskViewModel.Mine(p, game, _s.MyTeamId);
        if (mine?.Ask.Id != _refusalAsk) _refusal = null;                       // a different Ask, or none: the refusal is spent
        var names = new Names(game, p);
        if (mine is null)
        {
            var mid = new[] { _s.MyTeamId }.Concat(_s.MyTeamIds.Select(t => (Guid?)t)).Select(t => DeployView.MidDeploy(p, _s.Log, t)).FirstOrDefault(m => m is not null);   // a pick by any team of mine is placed
            if (_renderedAsk is not null || mid?.UnitId != _deployUnit) { Reset(); _renderedAsk = null; ClearHead(); }
            _note.Text = "";                                                    // a sealed Ask's reveal (its own line) stays up until the next Ask
            if (mid is null && _s.WaitingTeams is { Count: > 0 }) Quiet([]);   // the waiting columns hold the slot (WaitingPanel)
            else if (mid is null) Quiet(DecisionCard.Quiet(p, game, _s.MyTeamIds).Select(b => (b.Ordinal, b.Mine ? "Your move" : b.Team, b.Doing)).ToList());
            else { Quiet([]); if (_deployUnit is null) StartDeploy(mid, names); }
            return;
        }
        Quiet([]);
        var aside = SetAsideView.DrawnKey(_s.MaySetAside, _s.SetAsideRules);
        if (_renderedAsk == mine.Ask.Id && aside == _asideDrawn) return;
        _asideDrawn = aside;
        var model = DecisionView.Of(p, game, names, _s.MyTeamId);   // only for an Ask not yet drawn: most renders keep the box as it is
        Reset(); _renderedAsk = mine.Ask.Id; _answering = _s.MyTeamId; _note.Text = "";
        if (fresh.Count == 0) _revealed = null;                                 // a reveal is cleared only after a render has shown it
        _numbered = model.Numbered;
        _cardActions = CardActions.Of(mine.Ask, mine.Mode, p);
        Head(DecisionCard.Head(mine.Ask, p, names), MyOrdinal);
        Glow(mine.Highlights);
        switch (mine.Mode)
        {
            case AskMode.Choose: RenderChoose(mine, p, game, names); break;
            case AskMode.Move: RenderMove(mine.Ask, (MoveRegion)mine.Ask.Legal, p, names); RestoreMove(mine.Ask.Id); ApplyVerdict(); break;
            case AskMode.TokenSpend: RenderSpend(mine, p, names); break;
            case AskMode.Confirm:                                               // "Continue" for a step gate, "End the round" at the round boundary
                _onEnter = () => Send(mine.Ask, new Confirmed());
                Foot(Act(CardActions.Confirm, _onEnter));
                break;
            default: RenderPicks(mine, p, names); break;                       // PickPieces, Assign, Allocate
        }
        if (mine.Ask.AllowPass) Foot(Act(CardActions.Pass, () => Send(mine.Ask, new Passed())));
        Rekey();
        if (AskViewModel.AnsweredBy(_s.Log, mine.Ask.Id, _s.MyTeamId, _s.ParticipantId, names) is { } who) _note.Text = $"{who} answered for your team";
    }

    /// <summary>
    /// Option tiles. An activation's options are grouped by unit (DecisionCard.Groups), its pips and team edge, its note once under it; any other
    /// Ask's options are one row of tiles. A click on the other team's piece exactly one choice names picks that choice (DecisionView.ClickAnswers);
    /// a click on a unit of mine only selects it. A piece several choices name (activate:
    /// one option per unit and action) is not a pick: the click selects its unit as any click does, and the card narrows to that unit's group,
    /// renumbered from 1, until another unit is clicked, or Esc or Tab past the last unit clears the selection and widens it again.
    /// </summary>
    void RenderChoose(AskView mine, Projection p0, GameInfo game, Names names)
    {
        var options = (Options)mine.Ask.Legal; var all = options.Choices;
        Add(_choices);
        var narrowed = false;
        var full = _numbered;
        _onTile = option => Send(mine.Ask, new Chosen(option));
        void Show(IReadOnlyList<AskOption> shown, IReadOnlyList<(int Key, string Label, string OptionId)> numbered)
        {
            if (!IsInstanceValid(_choices)) return;
            if (_lit is not null) Light(null);
            _numbered = numbered;
            var p = _s.Projection ?? p0;
            var groups = ChoiceGroups.Begin();
            foreach (var group in DecisionCard.Groups(mine.Ask, shown, numbered, p, game, names, _s.MaxWounds))
            {
                var tiles = DecisionTiles.Fill(groups.Take("group", () => new DecisionTiles.OptionPanel()), group);
                foreach (var tile in group.Tiles)
                {
                    var b = tiles.Take("tile", ChoiceTile);
                    DecisionTiles.Fill(b, tile);
                    if (mine.Ask.Question == "cast" && SpellText.OfOption(_s.Rules, tile.OptionId, tile.Label) is { } tip) Look.Tip(b, tip);
                }
                tiles.End();
            }
            groups.End();
            Rekey();                                                        // Pass follows the tiles shown, narrowed or not
        }
        Show(all, full);
        _onNumber = option => Send(mine.Ask, new Chosen(option));
        _input.Eligible = DecisionView.ClickAnswers(options, mine.Ask, p0, game);
        _onTarget = clicked =>
        {
            if (_s.Projection is { } p && AskAnswers.ChosenByClick(mine.Ask, p, game, clicked) is { } chosen) Send(mine.Ask, chosen);
        };
        _onUnit = unit =>
        {
            if (_s.Projection is not { } p) return;
            var hits = DecisionView.ChoicesOf(options, p, unit);
            if (hits.Count == 0) { if (narrowed) { narrowed = false; Show(all, full); } return; }
            narrowed = true; Show(hits, DecisionView.Number(hits));
        };
        _onCancel = () => { if (!narrowed) return false; narrowed = false; Show(all, full); return true; };
        _cancels = () => narrowed;
        _onDeselect = () => { if (narrowed) { narrowed = false; Show(all, full); } };
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

    /// <summary>The pieces lit by the tile under the pointer, the rest of the Ask's glow dark so the hovered unit stands out
    /// among others of the same name, and the hover label of the one unit they name (TableInput.HoverUnit); null puts the Ask's glow back.</summary>
    IReadOnlyList<Guid>? _lit;
    public void Light(IReadOnlyList<Guid>? pieces)
    {
        bool In(IReadOnlyList<Guid>? set, PieceNode n) => set is not null && (set.Contains(n.ModelId) || set.Contains(n.UnitId));
        foreach (var n in _view.Pieces.OfType<PieceNode>())
        {
            if (!IsInstanceValid(n)) continue;
            if (In(pieces, n)) n.SetHighlight(Palette.Hovered);
            else if (pieces is not null && Glows(n)) n.SetHighlight(null);
            else if (In(_lit, n) || (pieces is null && Glows(n))) n.SetHighlight(Glows(n) ? Palette.Selected : null);
        }
        _lit = pieces;
        if (IsInstanceValid(_input)) _input.HoverUnit(pieces is not null && _s.Projection is { } p ? DecisionView.UnitNamed(pieces, p) : null);
    }

    /// <summary>
    /// p.13: "may spend any number of spell tokens", capped per model by what that model holds. So one stepper per model, each
    /// bounded by its own maximum, and no total, beside the caster's name and pips of its tokens. SpendableBy drops the other team's spenders,
    /// which the map carries because the window addresses both.
    /// </summary>
    void RenderSpend(AskView mine, Projection p, Names names)
    {
        var spendable = AskViewModel.SpendableBy(p, _s.Game!, _s.MyTeamId!.Value, (TokenSpend)mine.Ask.Legal);
        var spends = new Dictionary<Guid, SpinBox>();
        foreach (var (id, max) in spendable)
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", Space.S);
            row.AddChild(new Label { Text = names.Piece(id), SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart, VerticalAlignment = VerticalAlignment.Center });
            row.AddChild(DecisionTiles.Pips(RosterView.Pips(p.Models.GetValueOrDefault(id)?.SpellTokens ?? max, p.Models.GetValueOrDefault(id)?.SpellTokens ?? max), MyOrdinal));
            var spin = new SpinBox { MinValue = 0, MaxValue = max }; row.AddChild(spin); spends[id] = spin;
            Add(row);
        }
        void Spend() => Send(mine.Ask, AskAnswers.Spent(spends.ToDictionary(kv => kv.Key, kv => (int)kv.Value.Value)));
        _onEnter = Spend;
        Foot(Act(CardActions.Spend, Spend));
        if (mine.AnsweredTeams.Count > 0) _note.Text = string.Join(", ", mine.AnsweredTeams.Select(t => $"{t} has answered"));
    }

    /// <summary>Whether an Esc now is the open Ask's Cancel (clear targets, reset a move or a deploy, widen a narrowed choice); the game menu
    /// opens on Esc only when it is not.</summary>
    public bool TakesEsc => Visible && _onCancel is not null && (_cancels?.Invoke() ?? true);

    public override void _Input(InputEvent e)
    {
        if (!Visible || KeyOverlay.IntentOf(e, GetViewport()) is not { } intent) return;
        var dragging = _input.Holding || Godot.Input.IsMouseButtonPressed(MouseButton.Left);   // a drag in hand owns the digits (rows) and Esc, and holds off Enter
        var used = intent switch
        {
            >= Intent.Option1 and <= Intent.Option9 when !dragging => Number(intent - Intent.Option1 + 1),
            Intent.Confirm when !dragging && _onEnter is { } enter => Run(enter),   // a commit mid-drag would race the drop's own send
            Intent.Cancel when !dragging && !_input.HasPicks => _onCancel?.Invoke() == true,   // Esc clears picked models first (TableInput.Cancel)
            Intent.StopHere when !dragging && _onStop is { } stop => Run(stop),
            _ => false,
        };
        if (used) GetViewport().SetInputAsHandled();
    }

    /// <summary>A number key: the tile it caps, else the foot action it caps (CardActions.Numbered), when that can be pressed.</summary>
    bool Number(int key)
    {
        if (_onNumber is { } pick) foreach (var n in _numbered) if (n.Key == key) { pick(n.OptionId); return true; }
        return Actions.FirstOrDefault(a => a.Key == key) is { Enabled: true } action && Press(action.Id);
    }

    static bool Run(Action a) { a(); return true; }

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
        if (_lastCommand is { } sent) _flight.Refused(sent);
        _refusal = $"Could not send the answer: {message}"; _refusalAsk = askId; _renderedAsk = null; Render();
    }

    async Task Answer(Ask ask, AskAnswer answer)
    {
        SetButtons(true); Unhook(); _sentAsk = ask.Id;                          // one answer per Ask: no second click or key can double-send
        _lastCommand = Guid.CreateVersion7();                                   // before the send, so a Refused that lands first still matches
        _sent.Sent(_lastCommand.Value);                                         // a move commit's ghosts go with it, else none are held
        _flight.Sent(ask.Id, _lastCommand.Value);
        await _s.SendCommandAsync(AskAnswers.Answer(ask, answer, _answering), _lastCommand.Value);   // for the team the Ask was rendered for
    }

    /// <summary>The open Ask's handlers and table state, dropped whenever it ends by any route: our answer, a teammate's, a server close, a replacing Ask.</summary>
    void Unhook()
    {
        _onTile = null; _onEnter = null; _onStop = null; _onCancel = null; _cancels = null; _onNumber = null; _onTarget = null; _onModel = null; _onUnit = null; _onDeselect = null; _glow.Clear();
        if (_lit is not null && IsInstanceValid(_view)) Light(null);                 // a button freed under the pointer never hears its exit
        _proposed = null; _deployAt = null; _deployUnit = null; _turned.Clear(); _regate = null; _gate = new(true, null); _regions = null;
        if (_input is not null && IsInstanceValid(_input)) _input.Violations.Clear();   // a refused commit's marks come back with its ghosts (ApplyVerdict)
        if (_input is not null && IsInstanceValid(_input)) _input.PlacementPoint = null;   // a deploy's unused point pick would eat the next table click
        UnhookPicks();
        if (_input is not null && IsInstanceValid(_input)) _input.DropProposed();   // before Proposing clears: a held ghost would otherwise drop as a hand move
        if (_input is not null && IsInstanceValid(_input)) { _input.Proposing.Clear(); _input.Allowance = null; _input.Proposed = null; _input.Region = null; _input.Reach = null; _input.Eligible = new HashSet<Guid>(); }
        if (_region is not null && IsInstanceValid(_region)) _region.Hide();
        if (_view is not null && IsInstanceValid(_view)) _view.ClearExitGhosts();
    }

    /// <summary>Unhook, and the card's controls and the table's highlights with it.</summary>
    void Reset()
    {
        Unhook(); _numbered = []; _foot.Clear(); _cardActions = []; _sentAsk = null;
        // The option tiles and the foot's actions stand for the next Ask, hidden; whatever else the Ask made goes.
        foreach (var parent in new Node[] { _controls, _footRow })
            foreach (var child in parent.GetChildren())
            {
                if (child is not Control c) continue;
                if (Standing(c)) { c.Visible = false; continue; }
                parent.RemoveChild(c); c.QueueFree();
            }
        if (_view is not null && IsInstanceValid(_view)) foreach (var n in _view.Pieces.OfType<PieceNode>()) n.SetHighlight(null);
    }

    void SetButtons(bool disabled) { foreach (var parent in new Node[] { _controls, _footRow }) foreach (var b in parent.FindChildren("*", "BaseButton", true, false).OfType<BaseButton>()) b.Disabled = disabled; }
    /// <summary>Into the card's body, which scrolls.</summary>
    /// <summary>A control added hidden (a gate line until a reason holds) shows or hides the scroll with it: an empty scroll still takes the
    /// body's separation, a gap above the foot.</summary>
    void Add(Control c)
    {
        if (c.GetParent() == _controls) { _controls.MoveChild(c, -1); c.Visible = true; return; }   // a standing control, hooked when first added
        _controls.AddChild(c); c.VisibilityChanged += SyncScroll;
    }

    /// <summary>Kept across Asks: the option tiles' list and the pooled foot actions in the current theme.</summary>
    bool Standing(Control c) => c == _choices || c is Button b && _acts.ContainsValue(b);

    /// <summary>The option tiles' list, its groups and their tiles kept from one Ask to the next (Kept): an activation's Ask sets them again.</summary>
    readonly VBoxContainer _choices = new();
    Kept? _choiceGroupsKept;
    Kept ChoiceGroups => _choiceGroupsKept ??= new Kept(_choices);
    Action<string>? _onTile;

    DecisionTiles.ChoiceTile ChoiceTile()
    {
        var b = new DecisionTiles.ChoiceTile();
        b.Pressed += () => { if (b.Option is { } o) _onTile?.Invoke(o.OptionId); };
        b.MouseEntered += () => { if (b.Option is { } o) { Light(o.Pieces); TileHovered?.Invoke(o.OptionId); } };
        b.MouseExited += () => { Light(null); TileHovered?.Invoke(null); };
        return b;
    }

    /// <summary>A card's action (DecisionTiles.Action), pooled by its look: the next Ask with the same action takes the same button, pressing this Ask's handler.</summary>
    readonly Dictionary<string, Button> _acts = new(); readonly Dictionary<Button, Action> _actPressed = new();
    readonly AnswerInFlight _flight = new();
    /// <summary>The open Ask's foot actions (CardActions.Of), and the button each stands on in the order made.</summary>
    IReadOnlyList<CardAction> _cardActions = [];
    readonly List<(string Id, Button Button)> _foot = new();

    /// <summary>The action <paramref name="id"/> of this Ask's card as a button, its key cap set by Rekey.</summary>
    Button Act(string id, Action pressed)
    {
        var a = _cardActions.FirstOrDefault(x => x.Id == id) ?? CardActions.Placing.First(x => x.Id == id);
        var look = $"{a.Word}\u001f{a.Primary}\u001f{a.Icon}";
        if (!_acts.TryGetValue(look, out var b) || !IsInstanceValid(b))
        {
            Button made = null!;
            made = DecisionTiles.Action(a.Word, "", () => { if (_actPressed.TryGetValue(made, out var act)) act(); }, a.Primary, a.Icon);
            _acts[look] = b = made;
        }
        _actPressed[b] = pressed;
        b.Disabled = false; b.TooltipText = "";
        _foot.RemoveAll(f => f.Id == id || f.Button == b); _foot.Add((id, b));
        return b;
    }

    /// <summary>The foot's key caps after the numbered tiles (CardActions.Numbered): again whenever the tiles are renumbered.</summary>
    void Rekey()
    {
        var keyed = CardActions.Numbered(_cardActions, _numbered.Count);
        foreach (var (id, b) in _foot) if (IsInstanceValid(b)) DecisionTiles.SetCap(b, DecisionCard.KeyCap(keyed.FirstOrDefault(a => a.Id == id)?.Key ?? 0));
    }

    /// <summary>The foot's actions as the card shows them now: their keys, and enabled as their buttons are (a dim one's tooltip its reason).</summary>
    public IReadOnlyList<CardAction> Actions
    {
        get
        {
            var keyed = CardActions.Numbered(_cardActions, _numbered.Count);
            return _foot.Where(f => IsInstanceValid(f.Button) && f.Button.Visible).Select(f => keyed.FirstOrDefault(a => a.Id == f.Id) is { } a
                ? a with { Enabled = !f.Button.Disabled, Reason = f.Button.Disabled && f.Button.TooltipText != "" ? f.Button.TooltipText : null } : null)
                .OfType<CardAction>().ToList();
        }
    }

    /// <summary>Presses the foot action as its button does; false when it is not on the card or dim.</summary>
    public bool Press(string id)
    {
        if (_foot.FirstOrDefault(f => f.Id == id).Button is not { } b || !IsInstanceValid(b) || !b.Visible || b.Disabled || !_actPressed.TryGetValue(b, out var act)) return false;
        act();
        return true;
    }

    void SyncScroll()
    {
        if (!IsInstanceValid(_scroll)) return;
        _scroll.Visible = _controls.GetChildren().OfType<Control>().Any(c => c.Visible);
    }
    /// <summary>Into the card's foot: its actions, the primary first.</summary>
    void Foot(Control c)
    {
        if (c.GetParent() == _footRow) _footRow.MoveChild(c, -1); else _footRow.AddChild(c);
        c.Visible = true;
    }
}
