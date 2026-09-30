using Godot;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>The reachable region as a flat polygon just above the slab: the server's parameters, rendered by Tabletop.Geometry, drawn here.</summary>
public partial class MoveRegionNode : Node3D
{
    MeshInstance3D? _mesh;
    public void Show(IReadOnlyList<Point2> boundaryWire, Color colour)
    {
        Hide();
        if (boundaryWire.Count == 0) return;
        var verts = boundaryWire.Select(p => WorldFrame.ToGodot(new Point3(p.X, p.Y, 0.03))).ToArray();
        var centre = verts.Aggregate(Vector3.Zero, (a, v) => a + v) / verts.Length;
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        for (var i = 0; i < verts.Length; i++) { st.AddVertex(centre); st.AddVertex(verts[(i + 1) % verts.Length]); st.AddVertex(verts[i]); }
        _mesh = new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = Palette.Translucent(colour with { A = 0.25f }) };
        AddChild(_mesh);
    }
    public new void Hide() { if (_mesh is not null && IsInstanceValid(_mesh)) _mesh.QueueFree(); _mesh = null; }
}
