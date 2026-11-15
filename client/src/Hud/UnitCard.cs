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
    readonly StyleBoxFlat _stripFrame = new() { BorderWidthLeft = 6, ContentMarginLeft = 14, ContentMarginTop = 8, ContentMarginRight = 8, ContentMarginBottom = 8 };
    readonly Kept _stripKids;

    public UnitCard() => _stripKids = new(_stripBody);
    Guid? _unit, _hovered; HudRect? _slot; bool _fitQueued;
    UnitSheetModel? _sheet; Names? _names; UnitCardContent? _content;
    Action<RenderCause>? _onFrame; Action? _onChanged, _onTheme; Action<Guid?>? _onHover;
    public Guid? UnitId => _unit;
    public event Action<Guid>? ModelPicked;
    public event Action<Guid?>? FocusChanged;
    /// <summary>The models a row under the pointer names (a weapon's carriers, a group's standing models), for the table to light; null when the pointer leaves.</summary>
    public event Action<IReadOnlyList<Guid>?>? Lit;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session; _input = input;
        // The strip stacks above the card, not beside it, so the card never leaves the left column (the spec's Unit card → Hovered enemy).
        AddChild(_strip); _strip.AddChild(_stripBody); _strip.AddThemeStyleboxOverride("panel", _stripFrame);
        var panel = new PanelContainer { SizeFlagsVertical = SizeFlags.ShrinkEnd }; AddChild(panel); panel.AddChild(_card); _card.AddChild(_body);
        // A ScrollContainer reports no content height: the card takes the body's, capped by the slot, whenever the body's changes.
        _body.MinimumSizeChanged += QueueFit;
        _onFrame = _ => Render(); _onChanged = Render; _onHover = id => { _hovered = id; OnHover(); };   // the frame: every cause
        session.Frame.Render += _onFrame;
        input.SelectionChanged += _onChanged; input.HoveredUnitChanged += _onHover;
        _onTheme = Rethemed; Ui.Changed += _onTheme;
        Render();
    }

    /// <summary>A new theme: every part and the strip are made again in it, whatever the content (a kept control holds what it was made with).</summary>
    void Rethemed()
    {
        foreach (var part in _parts) { part.Clear(); var box = part.Box; box.GetParent()?.RemoveChild(box); box.QueueFree(); }
        _parts.Clear(); _kids.Clear(); _stripKids.Clear();
        _content = null; Render();
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
        var content = new UnitCardContent(sheet, SheetFocus.TeamOf(p!, game!, u)?.Ordinal ?? 0, DecisionView.NoteOf(p!, game!, _s.MyTeamId, u),
            _input.Selected is { } sel && sel.PieceId == u ? sel.ModelIds : null, _s.InCover.Contains(u));
        if (content != _content)
        {
            // The same unit's card keeps its place in the scroll; another unit's starts at the top.
            var scroll = _content?.Sheet.UnitId == u ? _card.ScrollVertical : 0;
            var parts = UnitCardParts.Changed(_content, content);
            _content = content;
            RenderCard(content, parts);
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
    /// (why my open choice limits this unit, AskOption.Note), the model groups, the weapons and the rules. Each part is its own box in the body,
    /// set again only when UnitCardParts names it, and its controls stand across renders (Kept): a render sets their text, colours and
    /// visibility, so a new selection makes no controls once the card has drawn a unit as large.</summary>
    void RenderCard(UnitCardContent c, IReadOnlyList<UnitCardPart> parts)
    {
        if (_parts.Count == 0)
            foreach (var _ in Enum.GetValues<UnitCardPart>())
            {
                var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                box.AddThemeConstantOverride("separation", _body.GetThemeConstant("separation"));
                _body.AddChild(box); _parts.Add(new Kept(box));
            }
        foreach (var part in parts)
        {
            var kept = _parts[(int)part].Begin();
            DrawPart(kept, part, c);
            ((Control)kept.Box).Visible = kept.End() > 0;   // an empty part takes no separation
        }
    }
    readonly List<Kept> _parts = new();

    /// <summary>Makes every part's controls for the largest unit on the table, hidden, while the game loads: the first selection then only sets them.</summary>
    public void Prebuild()
    {
        if (_unit is not null || _s.Projection is not { } p || _s.Game is not { } game) return;
        var names = new Names(game, p);
        var largest = p.Units.Keys.Select(u => UnitSheet.Of(p, names, u, _s.Rules, _s.MaxWounds))
            .MaxBy(s => s.Weapons.Count + s.Weapons.Sum(w => w.Rules.Count) + s.Rules.Count + s.Models.Count + s.Chips.Count);
        if (largest is null) return;
        RenderCard(new UnitCardContent(largest, SheetFocus.TeamOf(p, game, largest.UnitId)?.Ordinal ?? 0, null), Enum.GetValues<UnitCardPart>());
        _body.GetCombinedMinimumSize();   // shapes the text while hidden
        _content = null;                  // the first real render sets every part
    }

    void DrawPart(Kept box, UnitCardPart part, UnitCardContent c)
    {
        var m = c.Sheet;
        var team = c.Team > 0 ? Ui.Team(c.Team) : (Color?)null;
        switch (part)
        {
            case UnitCardPart.Title:
                var title = box.Take("title", () =>
                {
                    var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10);
                    row.AddChild(HudBits.Band(0));
                    row.AddChild(new Label { ThemeTypeVariation = Variation.Display, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill });
                    var eye = new Button { Flat = true, ThemeTypeVariation = Variation.FlatButton, FocusMode = FocusModeEnum.None, TooltipText = "Centre the camera on this unit",
                        ExpandIcon = false, SizeFlagsVertical = SizeFlags.ShrinkCenter };
                    eye.Pressed += () => { if (_unit is { } shown) _input.Focus(shown); };
                    row.AddChild(eye);
                    return row;
                });
                var band = (ColorRect)title.GetChild(0); var bandColour = team ?? Ui.DimText with { A = 0 };
                if (band.Color != bandColour) band.Color = bandColour;
                var name = (Label)title.GetChild(1); Look.Text(name, m.Name, Variation.Display); Look.Ink(name, team);
                var eyeButton = (Button)title.GetChild(2); var watch = Ui.Icon("watch");
                if (!ReferenceEquals(eyeButton.Icon, watch)) { eyeButton.Icon = watch; eyeButton.Text = watch is null ? "◉" : ""; }
                break;
            case UnitCardPart.Count:
                var count = box.Take("count", () =>
                {
                    var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10);
                    row.AddChild(new Label()); row.AddChild(Wrap("", Variation.Dim));
                    return row;
                });
                var pips = (Label)count.GetChild(0); Look.Text(pips, c.Pips); Look.Ink(pips, team);
                Look.Text((Label)count.GetChild(1), c.CountNote, Variation.Dim);
                break;
            case UnitCardPart.State:
                var state = box.Take("state", () =>
                {
                    var flow = new HFlowContainer(); flow.AddThemeConstantOverride("h_separation", 8); flow.AddThemeConstantOverride("v_separation", 4);
                    return flow;
                });
                var chips = Kids(state).Begin();
                Stat(chips, "quality", "Q", m.Quality, "Quality"); Stat(chips, "defense", "D", m.Defense, "Defense");
                if (c.CoverTip is { } coverTip) Shield(chips, coverTip);
                foreach (var chip in m.Chips) StatusChip(chips, chip);
                chips.End();
                break;
            case UnitCardPart.Note:
                if (c.Note is { } note) Look.Text(box.Take("note", () => Wrap("", Variation.Warning)), $"⚠ {note}", Variation.Warning);
                break;
            case UnitCardPart.Models:
                if (c.Groups.Count == 0) break;
                Caption(box, "Models");
                foreach (var group in c.Groups) GroupRow(box, group, c.Marked(group), team);
                break;
            case UnitCardPart.Weapons:
                if (m.Weapons.Count > 0) WeaponTable(box, m.Weapons);
                break;
            case UnitCardPart.Rules:
                if (m.Rules.Count == 0) break;
                Caption(box, "Rules");
                var rules = Kids(box.Take("flow", () => new HFlowContainer())).Begin();
                foreach (var r in m.Rules) RuleChip(rules, r);
                rules.End();
                break;
        }
    }

    /// <summary>A container's kept children, made with it.</summary>
    readonly Dictionary<Control, Kept> _kids = new();
    Kept Kids(Control box) => _kids.TryGetValue(box, out var k) ? k : _kids[box] = new Kept(box);

    static void Caption(Kept box, string text) => Look.Text(box.Take("caption", () => new Label()), text.ToUpperInvariant(), Variation.Dim);

    /// <summary>A stat badge: the theme's icon and `Q 4+`, the stat's word in the tooltip.</summary>
    static void Stat(Kept into, string icon, string letter, string? value, string word)
    {
        var badge = into.Take("stat", () =>
        {
            var b = new PanelContainer { ThemeTypeVariation = Variation.Badge, MouseFilter = MouseFilterEnum.Stop };
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 6); b.AddChild(row);
            row.AddChild(HudBits.Icon(icon, Ui.Accent, 16)); row.AddChild(new Label());
            return b;
        });
        Look.Tip(badge, $"{word} {value ?? "-"}");
        var row = badge.GetChild(0);
        Look.Icon((TextureRect)row.GetChild(0), icon, Ui.Accent);
        Look.Text((Label)row.GetChild(1), $"{letter} {value ?? "-"}");
    }

    /// <summary>The cover shield ending the Quality / Defense badges: the theme's cover icon, its tooltip the only words.</summary>
    static void Shield(Kept into, string tip)
    {
        var shield = into.Take("cover", () =>
        {
            var b = new PanelContainer { ThemeTypeVariation = Variation.Badge, MouseFilter = MouseFilterEnum.Stop };
            b.AddChild(HudBits.Icon("cover", Ui.Accent, 16));
            return b;
        });
        Look.Tip(shield, tip);
        Look.Icon((TextureRect)shield.GetChild(0), "cover", Ui.Accent);
    }

    /// <summary>A group row's controls, made once: the frame (a chip around a row holding a picked model), the pips, the Hero badge, the name
    /// button, the wound pips and count.</summary>
    sealed partial class GroupControls : PanelContainer
    {
        public readonly HBoxContainer Row = new();
        public readonly Label Pips = new() { CustomMinimumSize = new Vector2(56, 0) }, Wounds = new() { TooltipText = "Wounds left" }, WoundCount = new() { ThemeTypeVariation = Variation.Dim };
        public readonly PanelContainer Hero = Badge("Hero");
        public readonly Button Title = new() { Flat = true, ThemeTypeVariation = Variation.FlatButton, ClipText = true, Alignment = HorizontalAlignment.Left, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        public ModelGroup? Group;
        public UnitCard? Card;

        public GroupControls()
        {
            ThemeTypeVariation = Variation.Chip;
            Row.AddThemeConstantOverride("separation", 8); AddChild(Row);
            foreach (var c in new Control[] { Pips, Hero, Title, Wounds, WoundCount }) Row.AddChild(c);
            Title.MouseEntered += () => { if (Card is { } card && Group is { Standing.Count: > 0 } group) card.Lit?.Invoke(group.Standing); };
            Title.MouseExited += () => Card?.Lit?.Invoke(null);
            Title.Pressed += () =>
            {
                if (Card is not { } card || Group is not { } group) return;
                var selected = card._input.Selected is { ModelIds.Count: 1 } sel ? sel.ModelIds[0] : (Guid?)null;
                if (group.Next(selected) is { } id) card.ModelPicked?.Invoke(id);
            };
        }
    }
    static readonly StyleBoxEmpty Unframed = new();

    /// <summary>One row per group of like models: its pips, its name (ModelGroup.Name) and, when a model has more than one wound, the
    /// group's wound pips and count; a joined hero's marked; a group with none standing dimmed. A click selects the group's next standing
    /// model after the selected one, so repeated clicks cycle through it. A row holding a picked model (a Shift+click, a Shift+drag, a row
    /// click) is framed as a chip.</summary>
    void GroupRow(Kept into, ModelGroup group, bool marked, Color? team)
    {
        var g = into.Take("group", () => new GroupControls { Card = this });
        g.Group = group;
        Look.Box(g, marked ? null : Unframed);
        Look.Text(g.Pips, group.Pips); Look.Ink(g.Pips, team);
        g.Hero.Visible = group.Hero;
        if (g.Title.Text != group.Name) g.Title.Text = group.Name;
        Look.Tip(g.Title, group.Line);
        var none = group.Standing.Count == 0;
        if (g.Title.Disabled != none) g.Title.Disabled = none;
        var fade = none ? Ui.Faded : Ui.Faded with { R = 1, G = 1, B = 1, A = 1 };
        if (g.Row.Modulate != fade) g.Row.Modulate = fade;
        g.Wounds.Visible = g.WoundCount.Visible = group.WoundPips is not null;
        if (group.WoundPips is { } wounds) { Look.Text(g.Wounds, wounds); Look.Text(g.WoundCount, group.WoundCount, Variation.Dim); }
    }

    /// <summary>The weapons as a five-column grid: the heads once, then a row per weapon.</summary>
    void WeaponTable(Kept box, IReadOnlyList<SheetWeapon> weapons)
    {
        var grid = box.Take("weapons", () =>
        {
            var g = new GridContainer { Columns = 5 }; g.AddThemeConstantOverride("h_separation", 14);
            foreach (var (h, tip) in new[] { ("Weapon", ""), ("Range", ""), ("A", "Attacks"), (UnitSheet.ApLabel, ""), ("Rules", "") })
                g.AddChild(new Label { Text = h.ToUpperInvariant(), ThemeTypeVariation = Variation.Dim, TooltipText = tip, MouseFilter = tip.Length > 0 ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore });
            return g;
        });
        var cells = (_kids.TryGetValue(grid, out var made) ? made : _kids[grid] = new Kept(grid, 5)).Begin();
        // The heads are the grid's first five children, made with it and never taken: the kept cells follow them.
        foreach (var w in weapons)
        {
            var name = cells.Take("name", () =>
            {
                var l = new WeaponName { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Stop };
                l.MouseEntered += () => { if (l.Carriers.Count > 0) Lit?.Invoke(l.Carriers); };      // the table lights the models carrying it
                l.MouseExited += () => Lit?.Invoke(null);
                return l;
            });
            name.Carriers = w.Carriers ?? [];
            Look.Text(name, w.Count > 1 ? $"{w.Name} ×{w.Count}" : w.Name);
            Look.Text(cells.Take("range", () => new Label()), w.Range);
            Look.Text(cells.Take("attacks", () => new Label()), w.Attacks);
            var ap = cells.Take("ap", () => new Label { MouseFilter = MouseFilterEnum.Stop }); Look.Text(ap, w.Ap); Look.Tip(ap, w.ApRule is { } r ? Tooltip(r) : "");
            var flow = Kids(cells.Take("rules", () => new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill })).Begin();
            foreach (var rule in w.Rules) RuleChip(flow, rule);
            flow.End();
        }
        cells.End();
    }

    static void RuleChip(Kept into, SheetRule r) => Chip(into, r.NotAutomated ? $"{r.Label} (not automated)" : r.Label, r.NotAutomated ? Variation.Dim : "", Tooltip(r), null);
    /// <summary>A state chip: its icon (UnitCardContent.ChipIcon) and its words (ChipText); the whole chip text in the tooltip.</summary>
    static void StatusChip(Kept into, SheetChip c) => Chip(into, UnitCardContent.ChipText(c), c.Kind switch { ChipKind.Condition => Variation.Warning, ChipKind.Activated => Variation.Dim, _ => "" },
        c.Text, UnitCardContent.ChipIcon(c) is { } icon ? Ui.Icon(icon) : null);
    static string Tooltip(SheetRule r) => !string.IsNullOrEmpty(r.Text) ? r.Text : r.NotAutomated ? "Not automated: no rule text." : "";
    /// <summary>A chip: the optional icon and the label, the label in the given Label variation ("" for none).</summary>
    static void Chip(Kept into, string text, string variation, string tooltip, Texture2D? icon)
    {
        var chip = into.Take("chip", () =>
        {
            var c = new PanelContainer { ThemeTypeVariation = Variation.Chip, MouseFilter = MouseFilterEnum.Stop };
            var row = new HBoxContainer(); c.AddChild(row);
            row.AddChild(new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(16, 16) });
            row.AddChild(new Label());
            return c;
        });
        Look.Tip(chip, tooltip);
        var row = chip.GetChild(0);
        var image = (TextureRect)row.GetChild(0);
        if (!ReferenceEquals(image.Texture, icon)) image.Texture = icon;
        image.Visible = icon is not null;
        Look.Text((Label)row.GetChild(1), text, variation);
    }
    static PanelContainer Badge(string text) { var badge = new PanelContainer { ThemeTypeVariation = Variation.Badge }; badge.AddChild(new Label { Text = text }); return badge; }
    /// <summary>A weapon row's name cell: it holds the standing models carrying the weapon (SheetWeapon.Carriers) for its hover.</summary>
    sealed partial class WeaponName : Label { public IReadOnlyList<Guid> Carriers = []; }
    /// <summary>A label that wraps to the slot's width, so its text never sets the card's minimum width.</summary>
    static Label Wrap(string text, string variation = "") =>
        new() { Text = text, ThemeTypeVariation = variation, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };

    /// <summary>Above the card when it shows my unit and the pointer is over an enemy (EnemyStrip). The sight check (L) answers who can shoot.</summary>
    void RenderStrip(Projection p, GameInfo game, Names names, UnitSheetModel mine)
    {
        _strip.Visible = false;
        var measured = EnemyStrip.Measured(p, game, _s.MyTeamId, mine.UnitId);   // the acting shooter's weapons, whatever the card shows
        if (_hovered is not { } h || !p.Units.ContainsKey(h) || !EnemyStrip.Shows(p, game, _s.MyTeamId, measured, h)) return;
        if (measured != mine.UnitId) mine = UnitSheet.Of(p, names, measured, _s.Rules, _s.MaxWounds);
        var m = EnemyStrip.Of(mine, UnitSheet.Of(p, names, h, _s.Rules, _s.MaxWounds), HoverView.Of(p, game, names, h, measured, _s.MaxWounds)?.Distance);
        var band = SheetFocus.TeamOf(p, game, h)?.Ordinal ?? 0;
        var edge = band > 0 ? Ui.Team(band) : Ui.DimText;
        if (_stripFrame.BorderColor != edge) _stripFrame.BorderColor = edge;
        var ground = Ui.Background with { A = 0.9f };
        if (_stripFrame.BgColor != ground) _stripFrame.BgColor = ground;
        var lines = _stripKids.Begin();
        var name = lines.Take("name", () => Wrap("", Variation.Display)); Look.Text(name, m.Name, Variation.Display); Look.Ink(name, band > 0 ? Ui.Team(band) : null);
        Look.Text(lines.Take("line", () => Wrap("")), $"Defense {m.Defense ?? "-"} · {m.Alive} alive" + (m.Distance is { } d ? $" · {d:0.0}\"" : ""));
        foreach (var r in m.Ranges)
            Look.Text(lines.Take("line", () => Wrap("")), $"{r.Weapon} {r.Range}{(r.Within switch { true => " within", false => " beyond", null => "" })}",
                r.Within switch { true => Variation.Success, false => Variation.Warning, null => "" });
        lines.End();
        _strip.Visible = true;
    }
}
