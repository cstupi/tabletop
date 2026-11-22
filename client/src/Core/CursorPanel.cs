namespace Tabletop.Client.Core;

/// <summary>Where a panel that follows the pointer stands (the hovered terrain piece's): below and right of the cursor, else left of it,
/// above it, or both; the first of those inside the view and off every obstacle (the Hud's panels, HudFrame.Obstacles); else the nearest
/// clear place just past a panel. With none clear, flipped at the view's right and bottom edges.</summary>
public static class CursorPanel
{
    const float After = 22, Before = 16, Edge = 4;

    public static (float X, float Y) Place((float X, float Y) mouse, (float W, float H) size, HudRect view, IReadOnlyList<HudRect> obstacles)
    {
        float right = mouse.X + After, left = mouse.X - Before - size.W, below = mouse.Y + After, above = mouse.Y - Before - size.H;
        bool Clear(float x, float y)
        {
            var r = new HudRect(x, y, size.W, size.H);
            return r.X >= view.X + Edge && r.Y >= view.Y + Edge && r.Right <= view.Right && r.Bottom <= view.Bottom && !obstacles.Any(r.Overlaps);
        }
        foreach (var (x, y) in new[] { (right, below), (left, below), (right, above), (left, above) })
            if (Clear(x, y)) return (x, y);
        // Hemmed in: the nearest clear place just past a panel the first choice would sit under.
        var first = new HudRect(right, below, size.W, size.H);
        (float X, float Y)? best = null; var cost = float.MaxValue;
        foreach (var o in obstacles.Where(first.Overlaps))
            foreach (var (x, y) in new[] { (o.X - Edge - size.W, below), (o.Right + Edge, below), (right, o.Bottom + Edge), (right, o.Y - Edge - size.H) })
                if (Clear(x, y) && Math.Abs(x - right) + Math.Abs(y - below) is var c && c < cost) { best = (x, y); cost = c; }
        if (best is { } b) return b;
        var fx = right + size.W > view.Right ? left : right;
        var fy = below + size.H > view.Bottom ? above : below;
        return (Math.Max(view.X + Edge, fx), Math.Max(view.Y + Edge, fy));
    }
}
