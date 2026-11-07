namespace Tabletop.Client.Core;

/// <summary>Readable rings plan: where each ring's label stands on the screen. A label hangs under the outline's point lowest on the screen,
/// so it sits outside the ring nearest the player; labels never overlap and never leave the view.</summary>
public static class RingLabels
{
    /// <summary>Pixels between the outline and its label, and between two labels.</summary>
    public const float Gap = 6;

    /// <summary>The candidate lowest on the screen among those inside the view (less the margin); when none is, the one nearest the view,
    /// pulled inside it. Null with no candidate (a ring behind the camera).</summary>
    public static (float X, float Y)? Anchor(IReadOnlyList<(float X, float Y)> projected, HudRect view, float margin = 0)
    {
        if (projected.Count == 0) return null;
        float l = view.X + margin, t = view.Y + margin, r = view.Right - margin, b = view.Bottom - margin;
        (float X, float Y)? best = null;
        foreach (var p in projected)
            if (p.X >= l && p.X <= r && p.Y >= t && p.Y <= b && (best is null || p.Y > best.Value.Y)) best = p;
        if (best is { } inside) return inside;
        static float Out(float v, float lo, float hi) => v < lo ? lo - v : v > hi ? v - hi : 0;
        var near = projected.MinBy(p => Out(p.X, l, r) + Out(p.Y, t, b));
        return (Math.Clamp(near.X, l, r), Math.Clamp(near.Y, t, b));
    }

    /// <summary>The rectangle a label of this size wants under its anchor, centred on it.</summary>
    public static HudRect Under((float X, float Y) anchor, float w, float h) => new(anchor.X - w / 2, anchor.Y + Gap, w, h);

    /// <summary>Each wanted rectangle kept inside the view, placed lowest on the screen first, so an outer ring's label keeps its place under
    /// its ring and an inner ring's moves up toward its own; one that would overlap a label already placed moves up past it, or, when that
    /// leaves the view, down past it from where it wanted to be. One that fits neither way stays where it wanted to be. In the wanted order.</summary>
    public static IReadOnlyList<HudRect> Place(IReadOnlyList<HudRect> wanted, HudRect view)
    {
        var placed = new List<HudRect>(); var result = new HudRect[wanted.Count];
        foreach (var i in Enumerable.Range(0, wanted.Count).OrderByDescending(i => wanted[i].Y).ThenBy(i => i))
        {
            var start = Clamp(wanted[i], view);
            var up = Slide(start, placed, view, -1);
            placed.Add(result[i] = up is { } u ? u : Slide(start, placed, view, +1) ?? start);
        }
        return result;
    }

    static HudRect Clamp(HudRect r, HudRect view) => r with
    {
        X = Math.Clamp(r.X, view.X, Math.Max(view.X, view.Right - r.W)),
        Y = Math.Clamp(r.Y, view.Y, Math.Max(view.Y, view.Bottom - r.H)),
    };

    static HudRect Grown(HudRect r) => new(r.X - Gap / 2, r.Y - Gap / 2, r.W + Gap, r.H + Gap);

    /// <summary>Moves r past each placed label it meets, upward (dir -1) or downward (+1), until it meets none; null once it would leave the view.</summary>
    static HudRect? Slide(HudRect r, List<HudRect> placed, HudRect view, int dir)
    {
        for (var n = 0; n <= placed.Count; n++)
        {
            var hit = placed.Where(p => Grown(p).Overlaps(r)).ToList();
            if (hit.Count == 0) return r;
            r = r with { Y = dir < 0 ? hit.Min(p => p.Y) - Gap - r.H : hit.Max(p => p.Bottom) + Gap };
            if (r.Y < view.Y || r.Bottom > view.Bottom) return null;
        }
        return null;
    }
}
