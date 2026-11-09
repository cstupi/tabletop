using Godot;
using Tabletop.Client.Core;

namespace Tabletop.Client.Hud;

/// <summary>A box's children kept across renders: a render takes each child in order by its key, reusing the control made for that key before
/// and making one only when none stands, then hides the ones it did not take. A render sets text, colours and visibility on standing controls,
/// so Godot frees and makes none, and a control whose text is unchanged keeps its shaping. The box holds only these children after its first `first`.</summary>
public sealed class Kept(Node box, int first = 0)
{
    readonly KeptSlots<Control> _slots = new();

    public Node Box => box;

    /// <summary>Starts a render: the next Take is the first child.</summary>
    public Kept Begin() { _slots.Begin(); return this; }

    /// <summary>The next child: the one made for this key, moved here if it stood later, else a new one from `make` (KeptSlots).</summary>
    public T Take<T>(string key, Func<T> make) where T : Control
    {
        var (node, made, movedTo) = _slots.Take(key, make);
        if (made) box.AddChild(node);
        if (movedTo is { } at) box.MoveChild(node, first + at);
        node.Visible = true;
        return node;
    }

    /// <summary>Ends a render: every child not taken hides, kept for a later one. Returns how many were taken.</summary>
    public int End()
    {
        foreach (var node in _slots.End()) node.Visible = false;
        return _slots.Taken;
    }

    /// <summary>Frees every child: the next render makes them again (a theme change).</summary>
    public void Clear()
    {
        foreach (var node in _slots.Clear()) { box.RemoveChild(node); node.QueueFree(); }
    }
}

/// <summary>Sets a control's look only when it differs: a theme override set again re-reads the theme and reshapes a label's text.</summary>
public static class Look
{
    public static void Ink(Control c, Color? colour, string name = "font_color")
    {
        if (colour is not { } col) { if (c.HasThemeColorOverride(name)) c.RemoveThemeColorOverride(name); return; }
        if (!c.HasThemeColorOverride(name) || c.GetThemeColor(name) != col) c.AddThemeColorOverride(name, col);
    }

    public static void FontSize(Control c, int? size, string name = "font_size")
    {
        if (size is not { } s) { if (c.HasThemeFontSizeOverride(name)) c.RemoveThemeFontSizeOverride(name); return; }
        if (!c.HasThemeFontSizeOverride(name) || c.GetThemeFontSize(name) != s) c.AddThemeFontSizeOverride(name, s);
    }

    /// <summary>A stylebox override; a box equal by reference to the one set stays.</summary>
    public static void Box(Control c, StyleBox? box, string name = "panel")
    {
        if (box is null) { if (c.HasThemeStyleboxOverride(name)) c.RemoveThemeStyleboxOverride(name); return; }
        if (!c.HasThemeStyleboxOverride(name) || !ReferenceEquals(c.GetThemeStylebox(name), box)) c.AddThemeStyleboxOverride(name, box);
    }

    /// <summary>A label's text and variation.</summary>
    public static Label Text(Label l, string text, string variation = "")
    {
        if (l.Text != text) l.Text = text;
        if (l.ThemeTypeVariation != variation) l.ThemeTypeVariation = variation;
        return l;
    }

    public static void Tip(Control c, string tip) { if (c.TooltipText != tip) c.TooltipText = tip; }

    /// <summary>A theme icon on a TextureRect, tinted.</summary>
    public static void Icon(TextureRect r, string name, Color tint)
    {
        var t = Theming.Ui.Icon(name);
        if (!ReferenceEquals(r.Texture, t)) r.Texture = t;
        if (r.Modulate != tint) r.Modulate = tint;
    }
}
