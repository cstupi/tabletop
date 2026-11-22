using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

/// <summary>A section heading that shows and hides the body under it (the terrain card's Mesh and Volumes): its title with its facts, room for the section's
/// one action (<see cref="Actions"/>), and ▸ closed or ▾ open at the right. Its state is kept in the game screen's <see cref="SetupFolds"/>.</summary>
public partial class Fold : VBoxContainer
{
    readonly Button _head;
    readonly Label _arrow;
    readonly Func<string> _title;
    public VBoxContainer Body { get; } = new();
    /// <summary>The heading row's right end, before the arrow: a section's own action (+ Objective) goes here.</summary>
    public HBoxContainer Actions { get; } = new();

    public Fold(SetupFolds folds, SetupFold fold, GameStatus status, Func<string> title)
    {
        _title = title;
        var open = folds.IsOpen(fold, status);
        AddThemeConstantOverride("separation", Space.XS);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", Space.S); AddChild(row);
        _head = new Button { Flat = true, ThemeTypeVariation = Variation.FlatButton, ToggleMode = true, ButtonPressed = open, Alignment = HorizontalAlignment.Left,
            FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        _head.Toggled += on => { Body.Visible = on; folds.Set(fold, on, status); Refresh(); };
        row.AddChild(_head);
        Actions.AddThemeConstantOverride("separation", Space.XS); row.AddChild(Actions);
        row.AddChild(_arrow = new Label { ThemeTypeVariation = Variation.Dim, MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter });
        Body.Visible = open;
        Body.AddThemeConstantOverride("separation", Space.S);
        AddChild(Body);
        Refresh();
    }

    /// <summary>The heading again, for a count that changed.</summary>
    public void Refresh()
    {
        if (!IsInstanceValid(_head)) return;
        var title = _title(); var cut = title.IndexOfAny("0123456789·".ToCharArray());
        _head.Text = cut < 0 ? title.ToUpperInvariant() : title[..cut].ToUpperInvariant() + title[cut..]; _head.TooltipText = title;
        _arrow.Text = _head.ButtonPressed ? "▾" : "▸";
    }
}
