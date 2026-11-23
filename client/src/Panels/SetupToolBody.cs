using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Screens;
using Tabletop.Client.Theming;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Client.Panels;

/// <summary>
/// One setup tool's panel body (spec 2026-11-21-setup-screen-layout-design.md → Tool panel): the tool's controls, which follow the draft;
/// <see cref="Heading"/> holds the tool's one heading action (+ Objective, Save…). <see cref="Fills"/>: the body takes the panel's height
/// itself (Terrain's list) instead of scrolling inside it. Anyone may use every control; the server applies in arrival order.
/// </summary>
public partial class SetupToolBody : VBoxContainer
{
    protected readonly GameScreen Screen = null!;
    protected Session S = null!;
    Action? _onBoard;
    readonly List<(Button Button, Func<TableState?, string?> Why, string Base)> _gates = new();

    public SetupToolBody() { }
    protected SetupToolBody(GameScreen screen) { Screen = screen; SizeFlagsHorizontal = SizeFlags.ExpandFill; }

    /// <summary>The heading row's right end: the tool's own action.</summary>
    public HBoxContainer Heading { get; } = new();

    public virtual bool Fills => false;

    public override void _Ready()
    {
        S = Main.Instance.Session;
        AddThemeConstantOverride("separation", Space.M);
        Heading.AddThemeConstantOverride("separation", Space.XS);
        Build();
        _onBoard = () => { if (IsInstanceValid(this)) { Refresh(); RefreshGates(); } };
        S.BoardChanged += _onBoard; S.GameChanged += _onBoard;
        _onBoard();
    }

    public override void _ExitTree()
    {
        if (S is not null && _onBoard is not null) { S.BoardChanged -= _onBoard; S.GameChanged -= _onBoard; }
        Gone();
    }

    protected virtual void Build() { }
    /// <summary>The draft or the game changed.</summary>
    protected virtual void Refresh() { }
    protected virtual void Gone() { }

    /// <summary>A button disabled with the reason while the draft is at a cap.</summary>
    protected T Gated<T>(T button, Func<TableState?, string?> why) where T : Button { _gates.Add((button, why, button.TooltipText)); return button; }

    void RefreshGates()
    {
        foreach (var (button, why, baseTip) in _gates)
        {
            if (!IsInstanceValid(button)) continue;
            var reason = why(S.Draft);
            button.Disabled = reason is not null; button.TooltipText = reason ?? baseTip;   // the reason only while disabled
        }
    }

    protected static TableSize SizeNow(Session s) => s.Draft?.Size ?? new TableSize(72, 48);
    protected static Button Btn(string text, Action a) { var b = new Button { Text = text }; b.Pressed += a; return b; }

    protected static TextureRect Icon(string name, Color tint, int size) => new()
    {
        Texture = Ui.Icon(name), Modulate = tint, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        CustomMinimumSize = new Vector2(size, size), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore,
    };

    /// <summary>A row of controls with the row gap.</summary>
    protected static HBoxContainer Row(int gap = Space.S) { var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", gap); return row; }

    /// <summary>The chosen swatch or style: framed in the accent colour.</summary>
    protected static StyleBoxFlat Framed()
    {
        var b = new StyleBoxFlat { BgColor = Ui.Accent with { A = 0.18f }, BorderColor = Ui.Accent };
        b.SetBorderWidthAll(2); b.SetCornerRadiusAll(Math.Min(Ui.Current.Shape.CornerRadius, 4)); b.SetContentMarginAll(Space.S);
        return b;
    }

    protected void Confirm(string title, string ok, string text, Action then)
    {
        var d = new ConfirmationDialog { Title = title, OkButtonText = ok, DialogText = text, DialogAutowrap = true };
        d.Confirmed += then; d.VisibilityChanged += () => { if (!d.Visible) d.QueueFree(); };
        AddChild(d); d.PopupCentered(new Vector2I(360, 0));
    }

    /// <summary>The tool's body for the rail's tool.</summary>
    public static SetupToolBody Of(SetupTool tool, GameScreen screen) => tool switch
    {
        SetupTool.Terrain => new SetupTerrainTool(screen),
        SetupTool.Objectives => new SetupObjectivesTool(screen),
        SetupTool.Table => new SetupTableTool(screen),
        SetupTool.Zones => new SetupZonesTool(screen),
        SetupTool.Layouts => new SetupLayoutsTool(screen),
        SetupTool.Teams => new SetupTeamsTool(screen),
        _ => new SetupGameTool(screen),
    };
}
