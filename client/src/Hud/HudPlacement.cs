using Godot;
using Tabletop.Client.Core;

namespace Tabletop.Client.Hud;

/// <summary>A Hud part into its HudFrame slot: anchored top left, the slot's width, and the slot's height (Fill) or its own
/// minimum height capped by the slot, against the slot's top or bottom edge.</summary>
public static class HudPlacement
{
    public static void Top(Control c, HudRect r) => Set(c, r.X, r.Y, r.W, Math.Min(c.GetCombinedMinimumSize().Y, r.H));

    /// <summary>Against the slot's bottom by the part's actual height: a part whose minimum outgrows the slot grows upward, not off the screen.</summary>
    public static void Bottom(Control c, HudRect r)
    {
        Set(c, r.X, r.Y, r.W, Math.Min(c.GetCombinedMinimumSize().Y, r.H));
        c.Position = new Vector2(r.X, r.Bottom - c.Size.Y);
    }

    public static void Fill(Control c, HudRect r) => Set(c, r.X, r.Y, r.W, r.H);

    static void Set(Control c, float x, float y, float w, float h)
    {
        c.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        c.GrowHorizontal = Control.GrowDirection.End; c.GrowVertical = Control.GrowDirection.End;
        c.Position = new Vector2(x, y);
        c.Size = new Vector2(Math.Max(0, w), Math.Max(0, h));
    }
}
