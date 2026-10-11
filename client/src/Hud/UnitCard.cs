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
    readonly ScrollContainer _card = new() { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ShrinkEnd };
    readonly VBoxContainer _body = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill }, _stripBody = new();
    readonly PanelContainer _strip = new() { Visible = false, SizeFlagsVertical = SizeFlags.ShrinkEnd };
    Guid? _unit, _hovered; HudRect? _slot; bool _fitQueued;
    UnitSheetModel? _sheet; Names? _names;
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action? _onChanged, _onTheme; Action<Guid?>? _onHover;
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
        _onEvents = _ => Render(); _onChanged = Render; _onHover = id => { _hovered = id; OnHover(); };
        session.EventsApplied += _onEvents; session.GameChanged += _onChanged; session.BoardChanged += _onChanged; session.FactsChanged += _onChanged;
        input.SelectionChanged += _onChanged; input.HoveredUnitChanged += _onHover;
        _onTheme = Render; Ui.Changed += _onTheme;
        Render();
    }

    public override void _ExitTree()
    {
        Ui.Changed -= _onTheme;
        if (_s is not null) { _s.EventsApplied -= _onEvents; _s.GameChanged -= _onChanged; _s.BoardChanged -= _onChanged; _s.FactsChanged -= _onChanged; }
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
    /// A change raises this part's MinimumSizeChanged, and the screen places it again.</summary>
    void Fit()
    {
        if (_slot is not { } slot || !IsInstanceValid(_card)) return;
        var chrome = GetCombinedMinimumSize().Y - _card.GetCombinedMinimumSize().Y;
        _card.CustomMinimumSize = new Vector2(0, Math.Max(0, Math.Min(_body.GetCombinedMinimumSize().Y, slot.H - chrome)));
    }

    void Render()
    {
        var p = _s.Projection; var game = _s.Game;
        var unit = p is not null && game is not null && game.Status is GameStatus.Playing or GameStatus.Ended
            ? SheetFocus.Of(p, game, _s.MyTeamId, _input.Selected?.PieceId) : null;
        if (unit != _unit) { _unit = unit; FocusChanged?.Invoke(unit); }
        Visible = unit is not null;
        if (unit is not { } u) { _sheet = null; _names = null; return; }
        var names = new Names(game!, p!);
        var sheet = UnitSheet.Of(p!, names, u, _s.Rules, _s.MaxWounds);
        _sheet = sheet; _names = names;
        RenderCard(sheet, SheetFocus.TeamOf(p!, game!, u)?.Ordinal ?? 0);
        RenderStrip(p!, game!, names, sheet);
    }

    /// <summary>A hovered-unit change redraws only the strip, so the card keeps its scroll position and open tooltip.</summary>
    void OnHover()
    {
        if (!Visible || _sheet is null || _names is null || _s.Projection is not { } p || _s.Game is not { } game) { Render(); return; }
        RenderStrip(p, game, _names, _sheet);
    }

    void RenderCard(UnitSheetModel m, int team)
    {
        Clear(_body);
        _body.AddChild(new ColorRect { Color = Palette.Team(team), CustomMinimumSize = new Vector2(0, 4) });
        _body.AddChild(Wrap($"{m.Name} · {m.Alive}/{m.Size} models"));
        var badges = new HBoxContainer(); _body.AddChild(badges);
        badges.AddChild(Badge($"Quality {m.Quality ?? "-"}")); badges.AddChild(Badge($"Defense {m.Defense ?? "-"}"));
        _body.AddChild(Flow(m.Chips.Select(StatusChip)));
        foreach (var model in m.Models) _body.AddChild(WoundRow(model));
        if (m.Weapons.Count > 0) _body.AddChild(WeaponTable(m.Weapons));
        _body.AddChild(Flow(m.Rules.Select(RuleChip)));
    }

    /// <summary>One row per model: a pip per wound left out of ModelFacts' max, a removed model dimmed; a click selects the model.</summary>
    Button WoundRow(SheetModel model)
    {
        var b = new Button { Text = $"{model.Label}  {new string('●', model.Left)}{new string('○', model.Max - model.Left)}", Flat = true, ThemeTypeVariation = Variation.FlatButton, ClipText = true,
            Disabled = model.Removed, Alignment = HorizontalAlignment.Left };
        if (model.Removed) b.Modulate = Ui.Faded;
        var id = model.Id; b.Pressed += () => ModelPicked?.Invoke(id);
        return b;
    }

    static GridContainer WeaponTable(IReadOnlyList<SheetWeapon> weapons)
    {
        var grid = new GridContainer { Columns = 5 };
        foreach (var h in new[] { "Weapon", "Range", "Attacks", UnitSheet.ApLabel, "Rules" }) grid.AddChild(new Label { Text = h, ThemeTypeVariation = Variation.Dim });
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
    static Control StatusChip(SheetChip c) => c.Kind switch
    {
        ChipKind.Condition => Chip(c.Text, Variation.Warning, icon: c.Condition is { } name ? Ui.Icon(name) : null),
        ChipKind.Activated => Chip(c.Text, Variation.Dim),
        _ => Chip(c.Text, ""),
    };
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
        if (_hovered is not { } h || !p.Units.ContainsKey(h) || !EnemyStrip.Shows(p, game, _s.MyTeamId, mine.UnitId, h)) return;
        var m = EnemyStrip.Of(mine, UnitSheet.Of(p, names, h, _s.Rules, _s.MaxWounds), HoverView.Of(p, game, names, h, mine.UnitId, _s.MaxWounds)?.Distance);
        Clear(_stripBody);
        _stripBody.AddChild(Wrap(m.Name));
        _stripBody.AddChild(Wrap($"Defense {m.Defense ?? "-"} · {m.Alive} alive" + (m.Distance is { } d ? $" · {d:0.0}\"" : "")));
        foreach (var r in m.Ranges)
            _stripBody.AddChild(Wrap($"{r.Weapon} {r.Range}{(r.Within switch { true => " within", false => " beyond", null => "" })}", r.Within switch { true => Variation.Success, false => Variation.Warning, null => "" }));
        _strip.Visible = true;
    }
}
