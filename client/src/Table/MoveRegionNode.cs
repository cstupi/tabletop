using Godot;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>Reachable regions as flat polygons just above the slab: the server's parameters, rendered by Tabletop.Geometry, drawn here.</summary>
public partial class MoveRegionNode : Node3D
{
    MeshInstance3D? _mesh;

    /// <summary>One region as a fan about its centre, or, with an inner edge (a minimum move, MoveRuler.Inner), the ring between the two.</summary>
    public void Show(IReadOnlyList<Point2> boundaryWire, Color colour, IReadOnlyList<Point2>? innerWire = null) => Show([(boundaryWire, innerWire)], colour);

    /// <summary>Several regions in one mesh: one per held model of a move.</summary>
    public void Show(IReadOnlyList<(IReadOnlyList<Point2> Boundary, IReadOnlyList<Point2>? Inner)> regions, Color colour)
    {
        Hide();
        if (regions.All(r => r.Boundary.Count == 0)) return;
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var (boundaryWire, innerWire) in regions)
        {
            if (boundaryWire.Count == 0) continue;
            var verts = boundaryWire.Select(p => WorldFrame.ToGodot(new Point3(p.X, p.Y, 0.03))).ToArray();
            var centre = verts.Aggregate(Vector3.Zero, (a, v) => a + v) / verts.Length;
            var inner = innerWire?.Select(p => WorldFrame.ToGodot(new Point3(p.X, p.Y, 0.03))).ToArray();
            for (var i = 0; i < verts.Length; i++)
            {
                var j = (i + 1) % verts.Length;
                if (inner is null) { st.AddVertex(centre); st.AddVertex(verts[j]); st.AddVertex(verts[i]); continue; }
                st.AddVertex(inner[i]); st.AddVertex(verts[j]); st.AddVertex(verts[i]);
                st.AddVertex(inner[i]); st.AddVertex(inner[j]); st.AddVertex(verts[j]);
            }
        }
        _mesh = new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = Palette.Translucent(colour with { A = 0.25f }) };
        AddChild(_mesh);
    }

    public new void Hide() { if (_mesh is not null && IsInstanceValid(_mesh)) _mesh.QueueFree(); _mesh = null; }
}
