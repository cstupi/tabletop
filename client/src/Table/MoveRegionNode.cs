using Godot;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>Reachable regions as flat triangles just above the slab: the server's parameters, rendered by Tabletop.Geometry, drawn here.</summary>
public partial class MoveRegionNode : Node3D
{
    MeshInstance3D? _mesh;

    /// <summary>One star-shaped region (the disembark region) as a fan about its centre.</summary>
    public void Show(IReadOnlyList<Point2> boundaryWire, Color colour)
    {
        var fan = new List<Point2>();
        if (boundaryWire.Count > 0)
        {
            var centre = new Point2(boundaryWire.Average(p => p.X), boundaryWire.Average(p => p.Y));
            for (var i = 0; i < boundaryWire.Count; i++) { fan.Add(centre); fan.Add(boundaryWire[(i + 1) % boundaryWire.Count]); fan.Add(boundaryWire[i]); }
        }
        ShowTriangles([fan], colour);
    }

    /// <summary>Several reach areas in one mesh, one per held model of a move, each as triangles (MoveRegions.Outline: three points each).</summary>
    public void ShowTriangles(IReadOnlyList<IReadOnlyList<Point2>> areas, Color colour)
    {
        Hide();
        if (areas.All(a => a.Count < 3)) return;
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var area in areas)
            for (var i = 0; i + 2 < area.Count; i += 3)
            {
                Vector3 a = At(area[i]), b = At(area[i + 1]), c = At(area[i + 2]);
                // Godot culls clockwise-from-above back faces: wind each triangle one way whatever order the geometry gave it.
                if ((area[i + 1] - area[i]).Cross(area[i + 2] - area[i]) > 0) { st.AddVertex(a); st.AddVertex(c); st.AddVertex(b); }
                else { st.AddVertex(a); st.AddVertex(b); st.AddVertex(c); }
            }
        _mesh = new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = Palette.Translucent(colour with { A = 0.25f }) };
        AddChild(_mesh);
    }

    static Vector3 At(Point2 p) => WorldFrame.ToGodot(new Point3(p.X, p.Y, 0.03));

    public new void Hide() { if (_mesh is not null && IsInstanceValid(_mesh)) _mesh.QueueFree(); _mesh = null; }
}
