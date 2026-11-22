using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Screens;
using Tabletop.Client.Theming;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

/// <summary>The Zones tool: the diagram in the team colours with ⇄ on it (a button for two teams, a menu of the pairs for more), then the
/// deployment styles as tiles.</summary>
public partial class SetupZonesTool : SetupToolBody
{
    ZoneDiagramControl _diagram = null!; HFlowContainer _styles = null!; Control? _swap;
    readonly ButtonGroup _group = new() { AllowUnpress = false };
    string? _shown;
    Action? _onTheme;

    public SetupZonesTool() { }
    public SetupZonesTool(GameScreen screen) : base(screen) { }

    protected override void Build()
    {
        AddChild(_diagram = new ZoneDiagramControl { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        AddChild(_styles = new HFlowContainer());
        _styles.AddThemeConstantOverride("h_separation", Space.S); _styles.AddThemeConstantOverride("v_separation", Space.S);
        _onTheme = () => { _shown = null; Refresh(); }; Ui.Changed += _onTheme;
    }

    protected override void Gone() => Ui.Changed -= _onTheme;

    protected override void Refresh()
    {
        if (!IsInstanceValid(_diagram)) return;
        var d = S.Draft;
        _diagram.Show(d?.Size ?? new TableSize(72, 48), d?.Zones ?? [], S.Zones);
        var style = d is null ? null : ZoneDiagram.StyleOf(d.Zones);
        var teams = S.Game?.Teams.OrderBy(t => t.Ordinal).Select(t => t.Ordinal).ToList() ?? [];
        var key = $"{style}|{string.Join(",", teams)}|{Ui.Current.Id}";
        if (key == _shown) return;
        _shown = key;
        foreach (var c in _styles.GetChildren()) { _styles.RemoveChild(c); c.QueueFree(); }
        foreach (var z in ZoneStyles.All)
        {
            var tile = new Button { Text = SetupWords.ZoneStyle(z), ToggleMode = true, ButtonGroup = _group, ButtonPressed = z == style, FocusMode = FocusModeEnum.None };
            tile.AddThemeStyleboxOverride("pressed", Framed()); tile.AddThemeStyleboxOverride("hover_pressed", Framed());
            tile.AddThemeColorOverride("font_pressed_color", Ui.Text); tile.AddThemeColorOverride("font_hover_pressed_color", Ui.Text);
            var pick = z;
            tile.Pressed += () => { if (ZoneDiagram.StyleOf(S.Draft?.Zones ?? []) != pick) Guarded.Send(S, SetupEdits.Zones(pick)); };
            _styles.AddChild(tile);
        }
        if (_swap is not null && IsInstanceValid(_swap)) _swap.QueueFree();
        _swap = null;
        if (teams.Count < 2) return;
        if (teams.Count == 2)
        {
            var swap = new Button { Text = "⇄", TooltipText = $"Swap Team {teams[0]} and Team {teams[1]}", FocusMode = FocusModeEnum.None };
            swap.Pressed += () => Guarded.Send(S, SetupEdits.SwapZones(teams[0], teams[1]));
            _swap = swap;
        }
        else
        {
            var menu = new MenuButton { Text = "⇄", TooltipText = "Swap two teams' zones", FocusMode = FocusModeEnum.None, Flat = false };
            var pairs = teams.SelectMany((a, i) => teams.Skip(i + 1).Select(b => (a, b))).ToList();
            for (var i = 0; i < pairs.Count; i++) menu.GetPopup().AddItem($"Team {pairs[i].a} ⇄ Team {pairs[i].b}", i);
            menu.GetPopup().IdPressed += id => Guarded.Send(S, SetupEdits.SwapZones(pairs[(int)id].a, pairs[(int)id].b));
            _swap = menu;
        }
        _diagram.AddChild(_swap);
        _swap.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight, LayoutPresetMode.KeepSize, Space.XS);
    }
}

/// <summary>The zones diagram: the table's outline, each team's zone filled in its colour, the style's name under it.</summary>
public partial class ZoneDiagramControl : Control
{
    TableSize _size = new(72, 48); IReadOnlyList<DeploymentZone> _zones = []; ZoneLegs _legs = ZoneLegs.Default;
    ZoneDiagramView? _view;
    const float MaxHeight = 200, Caption = Space.L;

    public override void _Ready() { Resized += Recompute; MouseFilter = MouseFilterEnum.Pass; }

    public void Show(TableSize size, IReadOnlyList<DeploymentZone> zones, ZoneLegs legs) { _size = size; _zones = zones; _legs = legs; Recompute(); }

    void Recompute()
    {
        var width = Math.Max(Size.X, 120);
        _view = ZoneDiagram.Of(_size, _zones, _legs, width, MaxHeight);
        CustomMinimumSize = new Vector2(0, (float)_view.Height + Caption);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_view is not { } v) return;
        var x0 = (Size.X - (float)v.Width) / 2;
        var outline = new Rect2(x0, 0, (float)v.Width, (float)v.Height);
        DrawRect(outline, Ui.Background with { A = 0.6f });
        foreach (var zone in v.Zones)
            foreach (var polygon in zone.Polygons)
            {
                var points = polygon.Select(p => new Vector2(x0 + (float)p.X, (float)p.Y)).ToArray();
                if (points.Length >= 3) DrawColoredPolygon(points, Ui.Team(zone.Ordinal) with { A = 0.55f });
                DrawPolyline([.. points, points[0]], Ui.Team(zone.Ordinal), 2);
            }
        DrawRect(outline, Ui.DimText, false, 1.5f);
        var font = GetThemeDefaultFont(); var fontSize = GetThemeDefaultFontSize();
        var caption = v.Style;
        var w = font.GetStringSize(caption, HorizontalAlignment.Left, -1, fontSize).X;
        DrawString(font, new Vector2((Size.X - w) / 2, (float)v.Height + Caption - Space.XS), caption, HorizontalAlignment.Left, -1, fontSize, Ui.DimText);
    }
}
