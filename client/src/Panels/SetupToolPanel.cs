using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Hud;
using Tabletop.Client.Screens;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Panels;

/// <summary>The open tool's panel beside the rail (spec 2026-11-21-setup-screen-layout-design.md → Tool panel): its heading (icon, title with its
/// count, the tool's one action) over its controls; Terrain's list takes the panel's height, the other tools scroll.</summary>
public partial class SetupToolPanel : PanelContainer
{
    readonly GameScreen _screen = null!;
    VBoxContainer _column = null!; Label _title = null!; TextureRect _icon = null!; HBoxContainer _head = null!;
    Session _s = null!; Action? _onBoard;
    public SetupTool? Tool { get; private set; }
    public SetupToolBody? Body { get; private set; }

    public SetupToolPanel() { }
    public SetupToolPanel(GameScreen screen) => _screen = screen;

    public override void _Ready()
    {
        _s = Main.Instance.Session;
        var box = SetupRail.Band(); box.SetContentMarginAll(Space.S); AddThemeStyleboxOverride("panel", box);   // compact over the table: two team columns fit
        _column = new VBoxContainer(); _column.AddThemeConstantOverride("separation", Space.M); AddChild(_column);
        _head = new HBoxContainer(); _head.AddThemeConstantOverride("separation", Space.S); _column.AddChild(_head);
        _head.AddChild(_icon = HudBits.Icon("terrain", Ui.Accent, 20));
        _head.AddChild(_title = new Label { ThemeTypeVariation = Variation.Accent, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, VerticalAlignment = VerticalAlignment.Center });
        _onBoard = ShowTitle; _s.BoardChanged += _onBoard; _s.GameChanged += _onBoard;
    }

    public override void _ExitTree() { if (_s is not null && _onBoard is not null) { _s.BoardChanged -= _onBoard; _s.GameChanged -= _onBoard; } }

    /// <summary>Shows the tool's controls, freeing the last tool's; null hides the panel.</summary>
    public void Open(SetupTool? tool)
    {
        Visible = tool is not null;
        if (tool == Tool && Body is not null && IsInstanceValid(Body)) return;
        Tool = tool;
        if (Body is not null && IsInstanceValid(Body)) { var old = Body.Fills ? (Node)Body : Body.GetParent(); old.GetParent()?.RemoveChild(old); old.QueueFree(); }
        foreach (var c in _head.GetChildren().Skip(2)) { _head.RemoveChild(c); c.QueueFree(); }
        Body = null;
        if (tool is not { } t) return;
        Body = SetupToolBody.Of(t, _screen);
        _head.AddChild(Body.Heading);
        if (Body.Fills) { Body.SizeFlagsVertical = SizeFlags.ExpandFill; _column.AddChild(Body); }
        else
        {
            var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
                VerticalScrollMode = ScrollContainer.ScrollMode.Reserve };
            scroll.AddChild(Body); _column.AddChild(scroll);
        }
        _icon.Texture = Ui.Icon(SetupTools.Icon(t));
        ShowTitle();
    }

    void ShowTitle()
    {
        if (Tool is not { } t || !IsInstanceValid(_title)) return;
        var title = SetupTools.Title(t, _s.Draft, _s.Game); var cut = title.IndexOfAny("0123456789·".ToCharArray());
        _title.Text = cut < 0 ? title.ToUpperInvariant() : title[..cut].ToUpperInvariant() + title[cut..];
        _title.TooltipText = title;
    }
}
