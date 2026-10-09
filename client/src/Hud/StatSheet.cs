using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Table;
using Tabletop.Protocol;

namespace Tabletop.Client.Hud;

/// <summary>Lower left, two tabs: Stats and This round, for the unit SheetFocus names. Hidden when it names none.</summary>
public partial class StatSheet : PanelContainer
{
    const int StatsTab = 0, RoundTab = 1;
    static readonly Color Dim = new(1, 1, 1, 0.55f);
    Session _s = null!; TableInput _input = null!;
    readonly SheetFocus _focus = new();
    readonly TabContainer _tabs = new() { CustomMinimumSize = new Vector2(420, 320) };
    readonly VBoxContainer _stats = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill }, _round = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    Guid? _activation;
    // Session and the input outlive the sheet: every subscription is a field and comes off in _ExitTree.
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action? _onChanged, _onSelection; Action<Guid>? _onUnitClicked;

    public event Action<Guid>? ModelPicked;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session; _input = input;
        SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft, LayoutPresetMode.Minsize, 8);
        GrowVertical = GrowDirection.Begin;
        AddChild(_tabs);
        _tabs.AddChild(Scroll(_stats)); _tabs.AddChild(Scroll(_round));
        _tabs.SetTabTitle(StatsTab, "Stats"); _tabs.SetTabTitle(RoundTab, "This round");
        _onEvents = _ => { FollowActivation(); Render(); };
        _onChanged = Render;
        _onUnitClicked = unit => { _focus.OnClicked(unit); Render(); };
        _onSelection = () => { if (_input.Selected is null) { _focus.OnCleared(); Render(); } };
        session.EventsApplied += _onEvents; session.GameChanged += _onChanged; session.BoardChanged += _onChanged; session.FactsChanged += _onChanged;
        input.UnitClicked += _onUnitClicked; input.SelectionChanged += _onSelection;
        FollowActivation(); Render();
    }

    public override void _ExitTree()
    {
        if (_s is not null) { _s.EventsApplied -= _onEvents; _s.GameChanged -= _onChanged; _s.BoardChanged -= _onChanged; _s.FactsChanged -= _onChanged; }
        if (_input is not null && IsInstanceValid(_input)) { _input.UnitClicked -= _onUnitClicked; _input.SelectionChanged -= _onSelection; }
    }

    /// <summary>A new activation frame moves the sheet to its actor's This round; its end leaves a clicked unit, or hides the sheet.</summary>
    void FollowActivation()
    {
        var frame = _s.Projection is { } p && HeaderView.ActionFrame(p) is { Context.ActorUnitId: not null } f ? f : null;
        if (frame is null)
        {
            if (_activation is null) return;
            _activation = null;
            _focus.OnActivationEnded();
            return;
        }
        if (frame.Id == _activation) return;
        _activation = frame.Id;
        _focus.OnActivationStarted(frame.Context.ActorUnitId!.Value);
        _tabs.CurrentTab = RoundTab;
    }

    void Render()
    {
        var p = _s.Projection; var game = _s.Game; var unit = _focus.UnitId;
        Visible = p is not null && game is not null && game.Status is GameStatus.Playing or GameStatus.Ended
            && unit is { } u && p.Units.ContainsKey(u);
        if (!Visible) return;
        var names = new Names(game!, p);
        RenderStats(UnitSheet.Of(p!, names, unit!.Value, _s.Rules, _s.MaxWounds));
        RenderRound(RoundHistory.For(EventApplier.EffectiveLog(_s.Log), p!, names, unit.Value));
    }

    void RenderStats(UnitSheetModel m)
    {
        Clear(_stats);
        _stats.AddChild(new Label { Text = $"{m.Team} · {m.Name} · {m.Alive}/{m.Size} models" });
        if (m.Quality is not null || m.Defense is not null) _stats.AddChild(new Label { Text = $"Quality {m.Quality ?? "–"}   Defense {m.Defense ?? "–"}" });
        if (m.Chips.Count > 0)
        {
            var chips = new HFlowContainer(); _stats.AddChild(chips);
            foreach (var chip in m.Chips) { var c = new PanelContainer(); c.AddChild(new Label { Text = chip }); chips.AddChild(c); }
        }
        if (m.Weapons.Count > 0)
        {
            var grid = new GridContainer { Columns = 4 }; _stats.AddChild(grid);
            foreach (var heading in new[] { "Weapon", "Range", "Attacks", "Rules" }) grid.AddChild(new Label { Text = heading, Modulate = Dim });
            foreach (var w in m.Weapons)
            {
                grid.AddChild(new Label { Text = w.Count > 1 ? $"{w.Name} ×{w.Count}" : w.Name });
                grid.AddChild(new Label { Text = w.Range });
                grid.AddChild(new Label { Text = w.Attacks });
                grid.AddChild(Rules(w.Rules));
            }
        }
        if (m.Rules.Count > 0) _stats.AddChild(Rules(m.Rules));
        var strip = new HFlowContainer(); _stats.AddChild(strip);
        foreach (var model in m.Models)
        {
            var text = $"{model.Label} {model.Left}/{model.Max}";
            var b = new Button { Text = model.Removed ? Strike(text) : text, Flat = true, Disabled = model.Removed };
            var id = model.Id; b.Pressed += () => ModelPicked?.Invoke(id);
            strip.AddChild(b);
        }
    }

    void RenderRound(IReadOnlyList<HistoryLine> lines)
    {
        Clear(_round);
        foreach (var line in lines)
            _round.AddChild(new Label
            {
                Text = line.Dice.Count == 0 ? line.Text : $"{line.Text}   {string.Join(" ", line.Dice)}",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
    }

    /// <summary>One label per rule; hovering shows its catalog text. A rule with no catalog entry reads "not automated".</summary>
    static HFlowContainer Rules(IReadOnlyList<SheetRule> rules)
    {
        var flow = new HFlowContainer();
        foreach (var r in rules)
            flow.AddChild(new Label
            {
                Text = r.Automated ? r.Label : $"{r.Label} (not automated)",
                TooltipText = r.Text ?? (r.Automated ? "" : "Not automated: no catalog entry."),
                MouseFilter = MouseFilterEnum.Stop,
                Modulate = r.Automated ? Colors.White : Dim,
            });
        return flow;
    }

    static ScrollContainer Scroll(Control content)
    {
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(content);
        return scroll;
    }

    static void Clear(Node box) { foreach (var c in box.GetChildren()) { box.RemoveChild(c); c.QueueFree(); } }

    /// <summary>A Button has no strike style: U+0336 after each character, as LogPanel does.</summary>
    static string Strike(string s) => string.Concat(s.Select(c => $"{c}̶"));
}
