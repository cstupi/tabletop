using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Hud;
using Tabletop.Client.Theming;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

/// <summary>
/// The top bar of lobby and setup (spec 2026-11-21-setup-screen-layout-design.md → Top bar, TopBarView): a band across the window whose left
/// end the corner draws over (Home, the game's name, the status chip), then a chip per team (its colour, title and state icon; in setup a click
/// opens Teams), the to-do count opening its lines, the creator's Start setup or End setup (or who takes it) and ☰, the game menu.
/// </summary>
public partial class SetupTopBar : PanelContainer
{
    HBoxContainer _row = null!; Control _gap = null!;
    Session _s = null!; Action? _onBoard;
    TopBarView? _shown;
    public event Action? TeamsRequested, MenuRequested;

    public override void _Ready()
    {
        _s = Main.Instance.Session;
        var box = SetupRail.Band(); box.ContentMarginTop = box.ContentMarginBottom = Space.S; box.ContentMarginLeft = box.ContentMarginRight = Space.S;
        AddThemeStyleboxOverride("panel", box);
        _row = new HBoxContainer(); _row.AddThemeConstantOverride("separation", Space.M); AddChild(_row);
        _onBoard = ShowView; _s.BoardChanged += _onBoard;
        ShowView();
    }

    public override void _ExitTree() { if (_s is not null && _onBoard is not null) _s.BoardChanged -= _onBoard; }

    /// <summary>The corner's room at the left: the bar's items start past it.</summary>
    public float Lead { set { if (IsInstanceValid(_gap) && _gap.CustomMinimumSize.X != value) _gap.CustomMinimumSize = new Vector2(value, 0); } }

    void ShowView()
    {
        if (_s.Game is not { } game || TopBarView.Of(game, _s.IsCreator, _s.Draft) is not { } view) return;
        if (_shown is { } was && was.Action == view.Action && was.ToDoText == view.ToDoText && was.Teams.SequenceEqual(view.Teams) && was.Waiting == view.Waiting
            && was.ToDo.SequenceEqual(view.ToDo)) return;
        _shown = view;
        var lead = IsInstanceValid(_gap) ? _gap.CustomMinimumSize.X : 0;
        foreach (var c in _row.GetChildren()) { _row.RemoveChild(c); c.QueueFree(); }
        _row.AddChild(_gap = new Control { CustomMinimumSize = new Vector2(lead, 0), MouseFilter = MouseFilterEnum.Ignore });
        var setup = game.Status == GameStatus.Setup;
        var chips = new HBoxContainer(); chips.AddThemeConstantOverride("separation", Space.S); _row.AddChild(chips);
        foreach (var team in view.Teams) chips.AddChild(Chip(team, setup));
        _row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        if (view.ToDoText is { } todo)
        {
            var menu = new MenuButton { Text = $"{todo} ▾", Icon = Ui.Icon("warning"), Flat = true, ThemeTypeVariation = Variation.FlatButton, FocusMode = FocusModeEnum.None,
                SizeFlagsVertical = SizeFlags.ShrinkCenter, TooltipText = string.Join("\n", view.ToDo.Select(l => l.Text)) };
            menu.AddThemeConstantOverride("icon_max_width", 16);
            menu.AddThemeColorOverride("font_color", Ui.Warning); menu.AddThemeColorOverride("icon_normal_color", Ui.Warning);
            foreach (var line in view.ToDo) menu.GetPopup().AddItem(line.Text);
            for (var i = 0; i < view.ToDo.Count; i++) menu.GetPopup().SetItemDisabled(i, true);
            _row.AddChild(menu);
        }
        if (view.Edit is { } edit)
        {
            var action = new Button { Text = view.Action, TooltipText = view.ActionTip, ThemeTypeVariation = Variation.Primary, FocusMode = FocusModeEnum.None,
                SizeFlagsVertical = SizeFlags.ShrinkCenter };
            action.Pressed += () => Guarded.Send(_s, edit);
            _row.AddChild(action);
        }
        else if (view.Waiting is { } wait) _row.AddChild(new Label { Text = wait, ThemeTypeVariation = Variation.Dim, VerticalAlignment = VerticalAlignment.Center });
        var burger = HudBits.IconButton("game-menu", "Menu (Esc)", 22);
        burger.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        burger.Pressed += () => MenuRequested?.Invoke();
        _row.AddChild(burger);
    }

    /// <summary>A team's chip: its colour band, Team N and the state icon, the state and players in the tooltip.</summary>
    Control Chip(TeamChip team, bool setup)
    {
        var b = new Button { TooltipText = team.Tooltip, FocusMode = FocusModeEnum.None, Flat = true, ThemeTypeVariation = Variation.FlatButton,
            SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseDefaultCursorShape = setup ? CursorShape.PointingHand : CursorShape.Arrow };
        if (setup) b.Pressed += () => TeamsRequested?.Invoke();
        var band = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        band.AddThemeStyleboxOverride("panel", Ui.TeamBand(team.Ordinal, compact: true));
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; row.AddThemeConstantOverride("separation", Space.XS); band.AddChild(row);
        var ink = Ui.OnBand(team.Ordinal);
        row.AddChild(HudBits.Icon("team", ink, 14));
        var title = new Label { Text = team.Title.ToUpperInvariant(), MouseFilter = MouseFilterEnum.Ignore };
        title.AddThemeColorOverride("font_color", ink); title.AddThemeFontSizeOverride("font_size", 13); row.AddChild(title);
        row.AddChild(HudBits.Icon(team.Icon, ink, 14));
        b.AddChild(band);
        band.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        band.MinimumSizeChanged += () => b.CustomMinimumSize = band.GetCombinedMinimumSize();
        b.CustomMinimumSize = band.GetCombinedMinimumSize();
        return b;
    }
}
