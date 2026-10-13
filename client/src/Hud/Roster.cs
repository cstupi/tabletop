using Godot; using Tabletop.Client.Core; using Tabletop.Client.Table; using Tabletop.Client.Theming; using Tabletop.Protocol;
namespace Tabletop.Client.Hud;

/// <summary>Left column, above the unit card: team headers and unit rows; a click selects and centres the unit, R collapses to the headers.</summary>
public partial class Roster : PanelContainer
{
    Session _s = null!; readonly VBoxContainer _list = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill }; bool _collapsed;
    Action<RenderCause>? _onFrame; Action? _onChanged;
    HudRect? _slot; bool _fitQueued; ScrollContainer _scroll = null!;
    public event Action<Guid>? UnitPicked;

    public void Bind(Session session)
    {
        _s = session; _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.Reserve }; AddChild(_scroll); _scroll.AddChild(_list);
        _list.MinimumSizeChanged += QueueFit;                                                                           // a ScrollContainer reports no content height
        _onFrame = _ => Render(); _onChanged = Render;
        session.Frame.Render += _onFrame;                                                                              // every cause the frame carries
        Ui.Changed += _onChanged;                                                                                      // the theme's icons
        Render();
    }
    public override void _ExitTree() { Ui.Changed -= _onChanged; if (_s is null) return; _s.Frame.Render -= _onFrame; }
    /// <summary>Into its HudLayout slot: the slot's width, the list's height up to the slot's, against the slot's top.</summary>
    public void Place(HudRect slot) { _slot = slot; Fit(); HudPlacement.Top(this, slot); }

    void QueueFit() { if (_fitQueued) return; _fitQueued = true; Callable.From(() => { _fitQueued = false; Fit(); }).CallDeferred(); }

    void Fit()
    {
        if (_slot is not { } slot || _scroll is null || !IsInstanceValid(_scroll)) return;
        var chrome = GetCombinedMinimumSize().Y - _scroll.GetCombinedMinimumSize().Y;
        _scroll.CustomMinimumSize = new Vector2(0, MathF.Floor(Math.Max(0, Math.Min(_list.GetCombinedMinimumSize().Y, slot.H - chrome))));   // whole pixels, as DecisionBox.Fit
    }

    public override void _Input(InputEvent e) { if (KeyOverlay.IntentOf(e, GetViewport()) == Intent.ToggleRoster) { _collapsed = !_collapsed; Render(); GetViewport().SetInputAsHandled(); } }

    void Render()
    {
        foreach (var c in _list.GetChildren()) { _list.RemoveChild(c); c.QueueFree(); }
        if (_s.Projection is not { } p || _s.Game is not { } game) return;
        foreach (var team in RosterView.Of(p, game, new Names(game, p), _s.MyTeamId, _s.MaxWounds))
        {
            var h = new Label { Text = team.Name }; h.AddThemeColorOverride("font_color", Palette.Team(team.Ordinal)); _list.AddChild(h);
            if (_collapsed) continue;
            foreach (var row in team.Rows)
            {
                var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Stop }; var v = row.OffTable ? Variation.Dim : "";   // off the table: dimmed
                line.AddChild(new Label { Text = row.Name, ThemeTypeVariation = v, ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill });
                line.AddChild(row.Activated ? Icon("activated") : new Control { CustomMinimumSize = new Vector2(16, 16), MouseFilter = MouseFilterEnum.Ignore });   // an empty spacer keeps the columns lined up
                line.AddChild(new Label { Text = row.Max <= 12 ? RosterView.Pips(row.Left, row.Max) : $"{row.Left}/{row.Max}", ThemeTypeVariation = v });
                foreach (var c in row.Conditions) line.AddChild(Icon(c));
                var id = row.UnitId;
                line.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) UnitPicked?.Invoke(id); };
                _list.AddChild(line);
            }
        }
    }

    /// <summary>The theme's icon for a condition name or marker kind, else a text chip of the name.</summary>
    public static Control Icon(string key) => Ui.Icon(key) is { } texture
        ? new TextureRect { Texture = texture, MouseFilter = MouseFilterEnum.Pass, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(16, 16), TooltipText = RosterView.Words(key) }
        : Chip(RosterView.Words(key));

    /// <summary>Text in the theme's chip box. Icons and chips pass clicks to their row, keeping their tooltips.</summary>
    public static Control Chip(string text) { var chip = new PanelContainer { ThemeTypeVariation = Variation.Chip, MouseFilter = MouseFilterEnum.Pass }; chip.AddChild(new Label { Text = text }); return chip; }
}
