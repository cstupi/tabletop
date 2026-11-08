using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Table;
using Tabletop.Client.Theming;
using Tabletop.Protocol;
using Projection = Tabletop.Protocol.Projection;

namespace Tabletop.Client.Hud;

/// <summary>Lower left (client UX spec → Unit card), placed by GameScreen.Layout: the unit SheetFocus names, and above it, inside the left column, the hovered enemy's strip. Hidden when it names none.</summary>
public partial class UnitCard : VBoxContainer
{
    Session _s = null!; TableInput _input = null!;
    readonly ScrollContainer _card = new() { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.Reserve, SizeFlagsVertical = SizeFlags.ShrinkEnd };
    readonly VBoxContainer _body = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill }, _stripBody = new();
    readonly PanelContainer _strip = new() { Visible = false, SizeFlagsVertical = SizeFlags.ShrinkEnd };
    Guid? _unit, _hovered; HudRect? _slot; bool _fitQueued;
    UnitSheetModel? _sheet; Names? _names; UnitCardContent? _content;
    Action<RenderCause>? _onFrame; Action? _onChanged, _onTheme; Action<Guid?>? _onHover;
    public Guid? UnitId => _unit;
    public event Action<Guid>? ModelPicked;
    public event Action<Guid?>? FocusChanged;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session; _input = input;
        // The strip stacks above the card, not beside it, so the card never leaves the left column (the spec's Unit card → Hovered enemy).
        AddChild(_strip); _strip.AddChild(_stripBody);
        var panel = new PanelContainer { SizeFlagsVertical = SizeFlags.ShrinkEnd }; AddChild(panel); panel.AddChild(_card); _card.AddChild(_body);
        // A ScrollContainer reports no content height: the card takes the body's, capped by the slot, whenever the body's changes.
        _body.MinimumSizeChanged += QueueFit;
        _onFrame = _ => Render(); _onChanged = Render; _onHover = id => { _hovered = id; OnHover(); };   // the frame: every cause
        session.Frame.Render += _onFrame;
        input.SelectionChanged += _onChanged; input.HoveredUnitChanged += _onHover;
        _onTheme = () => { _content = null; Render(); }; Ui.Changed += _onTheme;   // a new theme rebuilds whatever the content
        Render();
    }

    public override void _ExitTree()
    {
        Ui.Changed -= _onTheme;
        if (_s is not null) _s.Frame.Render -= _onFrame;
        if (_input is not null && IsInstanceValid(_input)) { _input.SelectionChanged -= _onChanged; _input.HoveredUnitChanged -= _onHover; }
    }

    /// <summary>Into its HudLayout slot: the slot's width, the content's height up to the slot's, against the slot's bottom.</summary>
    public void Place(HudRect slot)
    {
        _slot = slot;
        Fit();
        HudPlacement.Bottom(this, slot);
    }

    void QueueFit()
    {
        if (_fitQueued) return;
        _fitQueued = true;
        Callable.From(() => { _fitQueued = false; Fit(); }).CallDeferred();
    }

    /// <summary>The card's height: the body's, or what the slot leaves after the strip, the panel's chrome and the separation.
    /// A change raises this part's MinimumSizeChanged, and the screen places it again. Whole pixels: a box container sums its children's minimums as
    /// integers, so a fractional height (a slot from an odd window height) reads back as different chrome every pass and never settles.</summary>
    void Fit()
    {
        if (_slot is not { } slot || !IsInstanceValid(_card)) return;
        var chrome = GetCombinedMinimumSize().Y - _card.GetCombinedMinimumSize().Y;
        _card.CustomMinimumSize = new Vector2(0, MathF.Floor(Math.Max(0, Math.Min(_body.GetCombinedMinimumSize().Y, slot.H - chrome))));
    }

    void Render()
    {
        var p = _s.Projection; var game = _s.Game;
        var unit = p is not null && game is not null && game.Status is GameStatus.Playing or GameStatus.Ended
            ? SheetFocus.Of(p, game, _s.MyTeamId, _input.Selected?.PieceId) : null;
        if (unit != _unit) { _unit = unit; FocusChanged?.Invoke(unit); }
        Visible = unit is not null;
        if (unit is not { } u) { _sheet = null; _names = null; _content = null; return; }
        var names = new Names(game!, p!);
        var sheet = UnitSheet.Of(p!, names, u, _s.Rules, _s.MaxWounds);
        _sheet = sheet; _names = names;
        var content = new UnitCardContent(sheet, SheetFocus.TeamOf(p!, game!, u)?.Ordinal ?? 0, DecisionView.NoteOf(p!, game!, _s.MyTeamId, _s.Settings.MutedWindowKinds, u),
            _input.Selected is { } sel && sel.PieceId == u ? sel.ModelIds : null);
        if (content != _content)
        {
            // The same unit's card keeps its place in the scroll; another unit's starts at the top.
            var scroll = _content?.Sheet.UnitId == u ? _card.ScrollVertical : 0;
            _content = content;
            RenderCard(content);
            if (scroll > 0) RestoreScroll(scroll);
        }
        RenderStrip(p!, game!, names, sheet);
    }

    /// <summary>After the rebuilt body's layout, which sets the scroll range the position is clamped to: the next process frame.</summary>
    void RestoreScroll(int scroll) =>
        GetTree().Connect(SceneTree.SignalName.ProcessFrame, Callable.From(() => { if (IsInstanceValid(_card)) _card.ScrollVertical = scroll; }), (uint)ConnectFlags.OneShot);

    /// <summary>A hovered-unit change redraws only the strip, so the card keeps its scroll position and open tooltip.</summary>
    void OnHover()
    {
        if (!Visible || _sheet is null || _names is null || _s.Projection is not { } p || _s.Game is not { } game) { Render(); return; }
        RenderStrip(p, game, _names, _sheet);
    }

    /// <summary>The card (UX redesign part 2 → Unit card): the band and the name alone, with the eye that centres the camera on the unit; a pip
    /// per model and the count note; Quality and Defense as badges with their icons and the state chips with theirs; the limited-choice note
    /// (why my open choice limits this unit, AskOption.Note), the model groups, the weapons and the rules.</summary>
    void RenderCard(UnitCardContent c)
    {
        var m = c.Sheet; var note = c.Note;
        Clear(_body);
        var title = new HBoxContainer(); title.AddThemeConstantOverride("separation", 10); _body.AddChild(title);
        title.AddChild(HudBits.Band(c.Team));
        var name = new Label { Text = m.Name, ThemeTypeVariation = Variation.Display, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        if (c.Team > 0) name.AddThemeColorOverride("font_color", Ui.Team(c.Team));
        title.AddChild(name);
        var eye = new Button { Flat = true, ThemeTypeVariation = Variation.FlatButton, FocusMode = FocusModeEnum.None, TooltipText = "Centre the camera on this unit",
            Icon = Ui.Icon("watch"), Text = Ui.Icon("watch") is null ? "◉" : "", ExpandIcon = false, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var unit = m.UnitId; eye.Pressed += () => _input.Focus(unit);
        title.AddChild(eye);

        var count = new HBoxContainer(); count.AddThemeConstantOverride("separation", 10); _body.AddChild(count);
        var pips = new Label { Text = c.Pips }; if (c.Team > 0) pips.AddThemeColorOverride("font_color", Ui.Team(c.Team));
        count.AddChild(pips);
        count.AddChild(Wrap(c.CountNote, Variation.Dim));

        var state = new HFlowContainer(); state.AddThemeConstantOverride("h_separation", 8); state.AddThemeConstantOverride("v_separation", 4); _body.AddChild(state);
        state.AddChild(Stat("quality", "Q", m.Quality, "Quality")); state.AddChild(Stat("defense", "D", m.Defense, "Defense"));
        foreach (var chip in m.Chips) state.AddChild(StatusChip(chip));
        if (note is not null) _body.AddChild(Wrap($"⚠ {note}", Variation.Warning));
        if (c.Groups.Count > 0)
        {
            _body.AddChild(Caption("Models"));
            foreach (var group in c.Groups) _body.AddChild(GroupRow(group, c.Marked(group), c.Team));
        }
        if (m.Weapons.Count > 0) _body.AddChild(WeaponTable(m.Weapons));
        if (m.Rules.Count > 0) { _body.AddChild(Caption("Rules")); _body.AddChild(Flow(m.Rules.Select(RuleChip))); }
    }

    static Label Caption(string text) => new() { Text = text.ToUpperInvariant(), ThemeTypeVariation = Variation.Dim };

    /// <summary>A stat badge: the theme's icon and `Q 4+`, the stat's word in the tooltip.</summary>
    static PanelContainer Stat(string icon, string letter, string? value, string word)
    {
        var badge = new PanelContainer { ThemeTypeVariation = Variation.Badge, TooltipText = $"{word} {value ?? "-"}", MouseFilter = MouseFilterEnum.Stop };
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 6); badge.AddChild(row);
        row.AddChild(HudBits.Icon(icon, Ui.Accent, 16));
        row.AddChild(new Label { Text = $"{letter} {value ?? "-"}" });
        return badge;
    }

    /// <summary>One row per group of like models: its pips, its name (ModelGroup.Name) and, when a model has more than one wound, the
    /// group's wound pips and count; a joined hero's marked; a group with none standing dimmed. A click selects the group's next standing
    /// model after the selected one, so repeated clicks cycle through it. A row holding a picked model (a Shift+click, a Shift+drag, a row
    /// click) is framed as a chip.</summary>
    Control GroupRow(ModelGroup group, bool marked, int team)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
        Control outer = row;
        if (marked) { var frame = new PanelContainer { ThemeTypeVariation = Variation.Chip }; frame.AddChild(row); outer = frame; }
        var pips = new Label { Text = group.Pips, CustomMinimumSize = new Vector2(56, 0) };
        if (team > 0) pips.AddThemeColorOverride("font_color", Ui.Team(team));
        row.AddChild(pips);
        if (group.Hero) row.AddChild(Badge("Hero"));
        var b = new Button { Text = group.Name, TooltipText = group.Line, Flat = true, ThemeTypeVariation = Variation.FlatButton, ClipText = true,
            Disabled = group.Standing.Count == 0, Alignment = HorizontalAlignment.Left, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        if (group.Standing.Count == 0) row.Modulate = Ui.Faded;
        b.Pressed += () =>
        {
            var selected = _input.Selected is { ModelIds.Count: 1 } sel ? sel.ModelIds[0] : (Guid?)null;
            if (group.Next(selected) is { } id) ModelPicked?.Invoke(id);
        };
        row.AddChild(b);
        if (group.WoundPips is { } wounds)
        {
            row.AddChild(new Label { Text = wounds, TooltipText = "Wounds left" });
            row.AddChild(new Label { Text = group.WoundCount, ThemeTypeVariation = Variation.Dim });
        }
        return outer;
    }

    static GridContainer WeaponTable(IReadOnlyList<SheetWeapon> weapons)
    {
        var grid = new GridContainer { Columns = 5 }; grid.AddThemeConstantOverride("h_separation", 14);
        foreach (var (h, tip) in new[] { ("Weapon", ""), ("Range", ""), ("A", "Attacks"), (UnitSheet.ApLabel, ""), ("Rules", "") })
            grid.AddChild(new Label { Text = h.ToUpperInvariant(), ThemeTypeVariation = Variation.Dim, TooltipText = tip, MouseFilter = tip.Length > 0 ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore });
        foreach (var w in weapons)
        {
            grid.AddChild(Wrap(w.Count > 1 ? $"{w.Name} ×{w.Count}" : w.Name));
            grid.AddChild(new Label { Text = w.Range }); grid.AddChild(new Label { Text = w.Attacks });
            grid.AddChild(new Label { Text = w.Ap, TooltipText = w.ApRule is { } ap ? Tooltip(ap) : "", MouseFilter = MouseFilterEnum.Stop });
            var rules = Flow(w.Rules.Select(RuleChip)); rules.SizeFlagsHorizontal = SizeFlags.ExpandFill; grid.AddChild(rules);
        }
        return grid;
    }

    static Control RuleChip(SheetRule r) => Chip(r.NotAutomated ? $"{r.Label} (not automated)" : r.Label, r.NotAutomated ? Variation.Dim : "", Tooltip(r));
    /// <summary>A state chip: its icon (UnitCardContent.ChipIcon) and its words (ChipText); the whole chip text in the tooltip.</summary>
    static Control StatusChip(SheetChip c) => Chip(UnitCardContent.ChipText(c), c.Kind switch { ChipKind.Condition => Variation.Warning, ChipKind.Activated => Variation.Dim, _ => "" },
        c.Text, UnitCardContent.ChipIcon(c) is { } icon ? Ui.Icon(icon) : null);
    static string Tooltip(SheetRule r) => !string.IsNullOrEmpty(r.Text) ? r.Text : r.NotAutomated ? "Not automated: no rule text." : "";
    /// <summary>A chip: the optional icon and the label, the label in the given Label variation ("" for none).</summary>
    static PanelContainer Chip(string text, string variation, string tooltip = "", Texture2D? icon = null)
    {
        var chip = new PanelContainer { ThemeTypeVariation = Variation.Chip, TooltipText = tooltip, MouseFilter = MouseFilterEnum.Stop };
        var row = new HBoxContainer(); chip.AddChild(row);
        if (icon is not null) row.AddChild(new TextureRect { Texture = icon, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(16, 16) });
        row.AddChild(new Label { Text = text, ThemeTypeVariation = variation });
        return chip;
    }
    static PanelContainer Badge(string text) { var badge = new PanelContainer { ThemeTypeVariation = Variation.Badge }; badge.AddChild(new Label { Text = text }); return badge; }
    /// <summary>A label that wraps to the slot's width, so its text never sets the card's minimum width.</summary>
    static Label Wrap(string text, string variation = "") =>
        new() { Text = text, ThemeTypeVariation = variation, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
    static HFlowContainer Flow(IEnumerable<Control> items) { var flow = new HFlowContainer(); foreach (var c in items) flow.AddChild(c); return flow; }
    static void Clear(Node box) { foreach (var c in box.GetChildren()) { box.RemoveChild(c); c.QueueFree(); } }

    /// <summary>Above the card when it shows my unit and the pointer is over an enemy (EnemyStrip). The sight check (L) answers who can shoot.</summary>
    void RenderStrip(Projection p, GameInfo game, Names names, UnitSheetModel mine)
    {
        _strip.Visible = false;
        var measured = EnemyStrip.Measured(p, game, _s.MyTeamId, mine.UnitId);   // the acting shooter's weapons, whatever the card shows
        if (_hovered is not { } h || !p.Units.ContainsKey(h) || !EnemyStrip.Shows(p, game, _s.MyTeamId, measured, h)) return;
        if (measured != mine.UnitId) mine = UnitSheet.Of(p, names, measured, _s.Rules, _s.MaxWounds);
        var m = EnemyStrip.Of(mine, UnitSheet.Of(p, names, h, _s.Rules, _s.MaxWounds), HoverView.Of(p, game, names, h, measured, _s.MaxWounds)?.Distance);
        Clear(_stripBody);
        var band = SheetFocus.TeamOf(p, game, h)?.Ordinal ?? 0;
        var strip = new StyleBoxFlat { BgColor = Ui.Background with { A = 0.9f }, BorderColor = band > 0 ? Ui.Team(band) : Ui.DimText };
        strip.BorderWidthLeft = 6; strip.SetContentMarginAll(8); strip.ContentMarginLeft = 14; _strip.AddThemeStyleboxOverride("panel", strip);
        var name = Wrap(m.Name, Variation.Display); if (band > 0) name.AddThemeColorOverride("font_color", Ui.Team(band));
        _stripBody.AddChild(name);
        _stripBody.AddChild(Wrap($"Defense {m.Defense ?? "-"} · {m.Alive} alive" + (m.Distance is { } d ? $" · {d:0.0}\"" : "")));
        foreach (var r in m.Ranges)
            _stripBody.AddChild(Wrap($"{r.Weapon} {r.Range}{(r.Within switch { true => " within", false => " beyond", null => "" })}", r.Within switch { true => Variation.Success, false => Variation.Warning, null => "" }));
        _strip.Visible = true;
    }
}
