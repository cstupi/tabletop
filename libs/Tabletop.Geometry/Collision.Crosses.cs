namespace Tabletop.Geometry;

public static partial class Collision
{
    /// <summary>
    /// The moving body, swept in a straight line from its placement to `to`, meets another model's body: their true base outlines in plan and
    /// their heights, sampled every <see cref="Step"/> and at the end, the moving body rested at each sample on what is under its base
    /// (<see cref="RestHeight(PlacedModel, IReadOnlyList{WorldVolume})"/> over `volumes`). A body passing above or below another does not cross it.
    /// A model whose body it already overlaps at the start is left out, so a body left on another can leave it. Touching is not crossing.
    /// </summary>
    public static bool Crosses(PlacedModel moving, Point2 to, IReadOnlyList<PlacedModel> models, IReadOnlyList<WorldVolume> volumes)
    {
        var start = moving.BaseOutline();
        var others = models.Where(m => !Overlap.Bases(moving, m)).Select(m => (Outline: m.BaseOutline(), Bottom: m.Bottom(), Top: m.Top())).ToList();
        if (others.Count == 0) return false;
        var d = to - moving.Placement.Position.Horizontal;
        var steps = Sampling.Steps(d.Length, Step);
        for (var k = 1; k <= steps; k++)
        {
            var at = start.Shift(d * ((double)k / steps));
            var bottom = Rest([at], volumes, floors: true);
            if (others.Any(o => Overlap.IntervalsOverlap(bottom, bottom + moving.Body.Height, o.Bottom, o.Top) && Outline.InteriorsIntersect(at, o.Outline))) return true;
        }
        return false;
    }
}
