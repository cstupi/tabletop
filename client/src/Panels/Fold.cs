using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

/// <summary>A heading that shows and hides the body under it (▾ open, ▸ closed), its state kept in the game screen's <see cref="SetupFolds"/>.</summary>
public partial class Fold : VBoxContainer
{
    readonly Button _head;
    readonly Func<string> _title;
    public VBoxContainer Body { get; } = new();

    public Fold(SetupFolds folds, SetupFold fold, GameStatus status, Func<string> title)
    {
        _title = title;
        var open = folds.IsOpen(fold, status);
        _head = new Button { Flat = true, ThemeTypeVariation = Variation.FlatButton, ToggleMode = true, ButtonPressed = open, Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.None };
        _head.Toggled += on => { Body.Visible = on; folds.Set(fold, on, status); Refresh(); };
        Body.Visible = open;
        AddChild(_head); AddChild(Body);
        Refresh();
    }

    /// <summary>The heading again, for a count that changed.</summary>
    public void Refresh() { if (IsInstanceValid(_head)) _head.Text = $"{(_head.ButtonPressed ? "▾" : "▸")} {_title()}"; }
}
