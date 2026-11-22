using Godot;

namespace Tabletop.Client.Theming;

/// <summary>Every AcceptDialog's button row right-aligned and evenly spaced: Godot puts an expanding spacer before, between and after the
/// buttons (centring them, and showing the trailing one again on popup) and spaces the row by the HBoxContainer's separation. Every spacer
/// moves to the row's start, so the buttons sit at the right edge, the primary last (swap_cancel_ok Cancel First), Space.L apart.
/// <see cref="Main"/> installs it on the tree.</summary>
public static class DialogLayout
{
    public static void Install(SceneTree tree) => tree.NodeAdded += node =>
    {
        if (node is AcceptDialog dialog) dialog.AboutToPopup += () => RightAlign(dialog);
    };

    static void RightAlign(AcceptDialog dialog)
    {
        if (dialog.GetOkButton().GetParent() is not HBoxContainer row) return;
        row.AddThemeConstantOverride("separation", Space.L);
        foreach (var spacer in row.GetChildren().OfType<Control>().Where(c => c is not BaseButton && c.GetType() == typeof(Control)).ToList())
            row.MoveChild(spacer, 0);
    }
}
