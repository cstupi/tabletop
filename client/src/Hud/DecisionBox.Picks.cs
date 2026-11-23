using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Panels;
using Tabletop.Client.Table;
using Tabletop.Client.Theming;
using Tabletop.Protocol;
using Projection = Tabletop.Protocol.Projection;

namespace Tabletop.Client.Hud;

public partial class DecisionBox
{
    readonly List<Label3D> _marks = new();

    /// <summary>The weapon groups and their targets while declare_targets is open; null otherwise. TargetLines draws it.</summary>
    public TargetAssignment? Targets { get; private set; }
    public event Action? TargetsChanged;

    partial void RenderPicks(AskView mine, Projection p, Names names)
    {
        switch (mine.Mode)
        {
            case AskMode.PickPieces: RenderPick(mine.Ask, (PieceSelection)mine.Ask.Legal, p, names); break;
            case AskMode.Allocate: RenderCasualties(mine.Ask, (Allocation)mine.Ask.Legal, p, names); break;
            case AskMode.Assign: RenderTargets(mine.Ask, (Assignment)mine.Ask.Legal, names); break;
        }
    }

    partial void UnhookPicks()
    {
        if (Targets is not null) { Targets = null; TargetsChanged?.Invoke(); }
        PickHovered?.Invoke(null);                                                   // a freed tile hears no exit
        ClearMarks();
    }

    /// <summary>Pick Min–Max pieces: a click on an eligible piece toggles it; a shelved one (a deploy) is a row with its pips and key cap. One pick
    /// of one answers at once, so Done shows only when more than one may be picked.</summary>
    void RenderPick(Ask ask, PieceSelection sel, Projection p, Names names)
    {
        var eligible = DeployView.Pickable(p, ask);                                  // a deploy pick: units with a model on the shelf (passengers spec)
        var pick = new PiecePick(sel, eligible);
        var count = new Label { ThemeTypeVariation = Variation.Numerals, Visible = sel.Max > 1 }; Add(count);
        Button? done = null;
        void Done() { if (pick.Answer() is { } answer) Send(ask, answer); }
        void Refresh() { count.Text = pick.Count(names); if (done is not null) { done.Disabled = !pick.Ready; done.TooltipText = done.Disabled ? count.Text : ""; } }
        void Toggle(Guid id)
        {
            switch (pick.Toggle(id))
            {
                case PickChange.Added: _view.PieceAt(id)?.SetHighlight(Palette.Ghost); break;
                case PickChange.Removed: _view.PieceAt(id)?.SetHighlight(Palette.Selected); break;
                default: return;
            }
            if (pick.AnswersAtOnce) { Done(); return; }
            Refresh();
        }
        var offTable = DeployView.OffTable(p, eligible);
        foreach (var shelved in offTable)
        {
            var key = _numbered.FirstOrDefault(n => n.OptionId == shelved.ToString()).Key;
            var tile = Row(shelved, key, names, () => Toggle(shelved));
            // The roster menu's Set aside beside a unit the server names (SetAsideView), so the unit is held from here too.
            if (_s.IsWatching || SetAsideView.SetAside(p, shelved, _s.MaySetAside) is not { } aside) { Add(tile); continue; }
            var line = new HBoxContainer(); line.AddThemeConstantOverride("separation", Space.S);
            line.AddChild(tile);
            var b = DecisionTiles.Action("Set aside", "", () => SendAside(aside), icon: "wait");
            b.TooltipText = _s.SetAsideRules.GetValueOrDefault(shelved)?.Text ?? "";
            line.AddChild(b);
            Add(line);
        }
        // A unit on the table (a charge's target, a spell's): a tile with its name and team band beside the glowing unit, so the card is
        // never an empty box (UX redesign part 2 → Pick a unit).
        if (offTable.Count == 0)
            foreach (var unit in eligible.Where(p.Units.ContainsKey)) Add(Row(unit, 0, names, () => Toggle(unit)));
        _onNumber = option => { if (Guid.TryParse(option, out var id) && eligible.Contains(id)) Toggle(id); };
        _input.Eligible = eligible.ToHashSet();
        _onTarget = clicked => { if (AskAnswers.Resolve(p, clicked, eligible) is { } id) { _input.CancelDrag(); Toggle(id); } };
        _onEnter = Done;
        if (sel.Max > 1) Foot(done = Act(CardActions.Done, Done));
        Refresh();
    }

    /// <summary>The deploy pick's Set aside: the roster menu's command; a refusal shows on the card.</summary>
    void SendAside(Command aside) => Guarded.Run(() => _s.SendCommandAsync(aside), message => { _refusal = $"Could not set aside: {message}"; _refusalAsk = _renderedAsk; Render(); });

    /// <summary>The unit under the pointer in a deploy pick's tiles, for the unit card; null when the pointer leaves.</summary>
    public event Action<Guid?>? PickHovered;

    /// <summary>A shelved unit as a wide tile: the army icon, its name, its set-aside chip during deployment, its pips in its team's colour, its key cap.</summary>
    Button Row(Guid unit, int key, Names names, Action pressed)
    {
        var b = new Button { FocusMode = FocusModeEnum.None, ThemeTypeVariation = Variation.Tile, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; row.AddThemeConstantOverride("separation", Space.S);
        var ordinal = names.TeamOfUnit(unit) is { } t ? _s.Game?.Teams.FirstOrDefault(x => x.Id == t)?.Ordinal ?? 0 : 0;
        row.AddChild(DecisionTiles.Icon("army", 20, ordinal > 0 ? Ui.Team(ordinal) : null));
        // The name over its set-aside chip, so a chip never takes the name's width (a row beside Set aside is narrow).
        var words = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        words.AddThemeConstantOverride("separation", Space.XS);
        words.AddChild(new Label { Text = names.Piece(unit), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(Space.XXL * 2, 0), MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center });
        if (_s.Projection is { } q && SetAsideView.Of(q, unit, _s.SetAsideRules) is { } chip) { var c = HudBits.AsideChip(); HudBits.FillAside(c, chip); c.SizeFlagsHorizontal = SizeFlags.ShrinkBegin; words.AddChild(c); }
        row.AddChild(words);
        if (_s.Projection is { } p && p.Units.ContainsKey(unit)) { var pips = DecisionTiles.Pips(DecisionCard.UnitPips(p, unit, _s.MaxWounds), ordinal); pips.MouseFilter = MouseFilterEnum.Ignore; row.AddChild(pips); }
        row.AddChild(DecisionTiles.Cap(DecisionCard.KeyCap(key)));
        b.Pressed += pressed;
        b.MouseEntered += () => { Light([unit]); PickHovered?.Invoke(unit); }; b.MouseExited += () => { Light(null); PickHovered?.Invoke(null); };
        return DecisionTiles.Holding(b, row, Space.S, Space.S);
    }

    /// <summary>declare_targets: weapon groups as chips, all picked to start (p.8: one group per weapon type), each aimed group carrying its
    /// target's team band. A click on an enemy aims every picked group that can shoot it (p.8); a chip picks its group alone. Enter fires once a
    /// group has a target (an unaimed group holds fire), Clear (Esc) removes every target.</summary>
    void RenderTargets(Ask ask, Assignment legal, Names names)
    {
        var t = Targets = new TargetAssignment(legal);
        var chips = new HFlowContainer(); chips.AddThemeConstantOverride("h_separation", Space.S); chips.AddThemeConstantOverride("v_separation", Space.S); Add(chips);
        void Fire() { if (t.Answer() is { } answer) Send(ask, answer); }
        bool ClearTargets() { if (!t.CanClear) return false; t.Clear(); Show(); return true; }
        Button fire = Act(CardActions.Fire, Fire), clear = null!;
        clear = Act(CardActions.Clear, () => ClearTargets());
        void Show()
        {
            if (Targets != t || !IsInstanceValid(chips)) return;                       // the Ask ended before a deferred refresh ran
            foreach (var c in chips.GetChildren()) { chips.RemoveChild(c); c.QueueFree(); }
            foreach (var g in t.Groups)
            {
                var id = g.Id;
                var aimed = t.Assigned.TryGetValue(id, out var unit);
                var chip = new Button { ToggleMode = true, ButtonPressed = t.Picked.Contains(id), FocusMode = FocusModeEnum.None, ThemeTypeVariation = Variation.Tile };
                var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; row.AddThemeConstantOverride("separation", Space.S);
                row.AddChild(new Label { Text = TargetAssignment.Chip(g), MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center });
                if (aimed)
                {
                    var ordinal = names.TeamOfUnit(unit) is { } team ? _s.Game?.Teams.FirstOrDefault(x => x.Id == team)?.Ordinal ?? 0 : 0;
                    var colour = ordinal > 0 ? Ui.Team(ordinal) : Ui.Text;
                    row.AddChild(new Label { Text = "→", MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center });
                    row.AddChild(new ColorRect { Color = colour, CustomMinimumSize = new Vector2(5, 0), MouseFilter = MouseFilterEnum.Ignore });
                    var target = new Label { Text = names.Unit(unit), MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
                    target.AddThemeColorOverride("font_color", colour); row.AddChild(target);
                }
                else row.AddChild(new Label { Text = "holds fire", ThemeTypeVariation = Variation.Dim, MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center });
                chip.Pressed += () => { t.PickOnly(id); Callable.From(Show).CallDeferred(); };   // not inside the chip's own signal: Show frees it
                chip.Ready += () => chip.CustomMinimumSize = new Vector2(row.GetParent<Control>().GetCombinedMinimumSize().X, 0);   // the row and its padding
                chips.AddChild(DecisionTiles.Holding(chip, row, Space.S, Space.S));
            }
            _input.Eligible = t.Candidates.ToHashSet(); Glow(t.Candidates);          // only units a picked group can shoot; two targets taken (p.8): nothing else glows
            fire.Disabled = t.Answer() is null; fire.TooltipText = fire.Disabled ? "No target" : "";
            clear.Disabled = !t.CanClear; clear.TooltipText = clear.Disabled ? "No target" : "";
            TargetsChanged?.Invoke();
        }
        _onTarget = clicked => { if (_s.Projection is { } p && AskAnswers.Resolve(p, clicked, legal.Targets) is { } unit && t.Aim(unit)) Show(); };
        _onEnter = Fire;
        _onCancel = ClearTargets; _cancels = () => t.CanClear;
        Foot(fire); Foot(clear); Show();
    }

    /// <summary>remove_casualties: the title says how many models or wounds; the counter `1 of 2` and a pip per one to pick, lit as they are picked.
    /// A click marks a model, each further click on a many-wound model adds a wound, a right-click takes one away. Auto fills the rest in the
    /// Ask's default order. Enter confirms once the count is exact.</summary>
    void RenderCasualties(Ask ask, Allocation legal, Projection p, Names names)
    {
        var left = legal.Targets.ToDictionary(id => id, id => Math.Max(0, _s.MaxWounds.GetValueOrDefault(id, 1) - (p.Models.GetValueOrDefault(id)?.Wounds ?? 0)));
        var pick = new CasualtyPick(legal, left, CasualtyPick.TotalOf(p, ask));
        var unit = legal.Targets.Select(id => p.Models.GetValueOrDefault(id)?.UnitId).FirstOrDefault(u => u is not null);
        Head(new CardHead("wound", pick.Title, unit is { } u ? names.Unit(u) : null, pick.Assigned), MyOrdinal);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", Space.M); Add(row);
        var count = new Label { ThemeTypeVariation = Variation.Display, VerticalAlignment = VerticalAlignment.Center }; row.AddChild(count);
        var pips = new Label { VerticalAlignment = VerticalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.Arbitrary, CustomMinimumSize = HudBits.WrapMin, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        pips.AddThemeColorOverride("font_color", Ui.Failure); row.AddChild(pips);
        void Confirm() { if (pick.Answer() is { } answer) Send(ask, answer); }
        var confirm = Act(CardActions.Confirm, Confirm);
        void Show()
        {
            count.Text = $"{pick.Chosen} of {pick.Required}";
            pips.Text = new string('●', pick.Chosen) + new string('○', Math.Max(0, pick.Required - pick.Chosen));
            confirm.Disabled = !pick.Complete; confirm.TooltipText = confirm.Disabled ? count.Text : "";
            ClearMarks();
            foreach (var (id, n) in pick.Counts)
                if (n > 0 && _view.PieceAt(id) is { } node && IsInstanceValid(node) && p.Models.GetValueOrDefault(id) is { } model)
                {
                    var mark = new Label3D { Text = left[id] > 1 ? $"✕ {n}/{left[id]}" : "✕", Modulate = Ui.Failure, FontSize = 64, PixelSize = 0.01f,
                        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, (float)model.Slot.Height + 0.5f, 0) };
                    node.AddChild(mark); _marks.Add(mark);
                }
        }
        _input.Eligible = legal.Targets.ToHashSet();
        _onModel = (id, right) => { if (!left.ContainsKey(id)) return; if (right) pick.Remove(id); else pick.Click(id); Show(); };
        _onEnter = Confirm;
        Foot(confirm); Foot(Act(CardActions.Auto, () => { pick.Auto(); Show(); })); Foot(Act(CardActions.Clear, () => { pick.Clear(); Show(); })); Show();
    }

    void ClearMarks() { foreach (var m in _marks) if (IsInstanceValid(m)) m.QueueFree(); _marks.Clear(); }
}
