namespace Tabletop.Geometry;

public static partial class Collision
{
    /// <summary>
    /// The moving base, swept in a straight line from its placement to `to`, shares area with another model's base: their true outlines in plan,
    /// sampled every <see cref="Step"/> and at the end. A model whose base it already overlaps at the start is left out, so a body left on
    /// another can leave it. Touching is not crossing.
    /// </summary>
    public static bool Crosses(PlacedModel moving, Point2 to, IReadOnlyList<PlacedModel> models)
    {
        var start = moving.BaseOutline();
        var others = models.Select(m => m.BaseOutline()).Where(o => !Outline.InteriorsIntersect(start, o)).ToList();
        if (others.Count == 0) return false;
        var d = to - moving.Placement.Position.Horizontal;
        var steps = Math.Max(1, (int)Math.Ceiling(d.Length / Step));
        for (var k = 1; k <= steps; k++)
        {
            var at = start.Shift(d * ((double)k / steps));
            if (others.Any(o => Outline.InteriorsIntersect(at, o))) return true;
        }
        return false;
    }
}
