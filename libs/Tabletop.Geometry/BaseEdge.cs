namespace Tabletop.Geometry;

/// <summary>
/// One placed base's outline, built once for many point queries: <see cref="To"/> is <see cref="Distance.EdgeToEdge(PlacedModel, Point2)"/>.
/// The base lies between two circles about its centre: it contains the one of radius <see cref="Inner"/> and lies inside the one of radius
/// <see cref="Outer"/>, so a point at r from the centre is between r − Outer and r − Inner from the edge. A round base has Inner = Outer.
/// </summary>
public sealed class BaseEdge
{
    readonly Outline _outline;
    public Point2 Centre { get; }
    public double Inner { get; }
    public double Outer { get; }

    public BaseEdge(PlacedModel m)
    {
        _outline = m.BaseOutline(); Centre = m.Placement.Position.Horizontal;
        (Inner, Outer) = _outline switch
        {
            CircleOutline c => (c.Radius, c.Radius),
            PolygonOutline p => (Enumerable.Range(0, p.Vertices.Count).Min(i => Line(p.Vertices[i], p.Vertices[(i + 1) % p.Vertices.Count])),
                p.Vertices.Max(v => v.DistanceTo(Centre))),
            _ => throw new ArgumentOutOfRangeException(nameof(m)),
        };
    }

    double Line(Point2 a, Point2 b) => Math.Abs((b - a).Cross(Centre - a)) / (b - a).Length;

    public double To(Point2 p) => _outline.DistanceTo(p);
}
