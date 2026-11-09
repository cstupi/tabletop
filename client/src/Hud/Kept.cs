using Godot;

namespace Tabletop.Client.Hud;

/// <summary>A box's children kept across renders: a render takes each child in order by its key, reusing the control made for that key before
/// and making one only when none stands, then hides the ones it did not take. A render sets text, colours and visibility on standing controls,
/// so Godot frees and makes none, and a control whose text is unchanged keeps its shaping. The box holds only these children after its first `first`.</summary>
public sealed class Kept(Node box, int first = 0)
{
    readonly List<(string Key, Control Node)> _items = [];
    int _at;

    public Node Box => box;

    /// <summary>Starts a render: the next Take is the first child.</summary>
    public Kept Begin() { _at = 0; return this; }

    /// <summary>The next child: the one made for this key, moved here if it stood later, else a new one from `make`.</summary>
    public T Take<T>(string key, Func<T> make) where T : Control
    {
        var i = -1;
        for (var k = _at; k < _items.Count; k++) if (_items[k].Key == key && _items[k].Node is T) { i = k; break; }
        Control node;
        if (i < 0)
        {
            node = make();
            box.AddChild(node);
            _items.Insert(_at, (key, node));
            box.MoveChild(node, first + _at);
        }
        else
        {
            node = _items[i].Node;
            if (i != _at) { _items.RemoveAt(i); _items.Insert(_at, (key, node)); box.MoveChild(node, first + _at); }
        }
        node.Visible = true;
        _at++;
        return (T)node;
    }

    /// <summary>Ends a render: every child not taken hides, kept for a later one. Returns how many were taken.</summary>
    public int End()
    {
        for (var k = _at; k < _items.Count; k++) _items[k].Node.Visible = false;
        return _at;
    }

    /// <summary>Frees every child: the next render makes them again (a theme change).</summary>
    public void Clear()
    {
        foreach (var (_, node) in _items) { box.RemoveChild(node); node.QueueFree(); }
        _items.Clear(); _at = 0;
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
