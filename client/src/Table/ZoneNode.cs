using Godot;
using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>A deployment zone: the style's polygons for this ordinal, flat, just above the slab. Colour is the team's, or grey with no team yet.</summary>
public partial class ZoneNode : Node3D, IHasId
{
    public Guid Id { get; private set; }
    public int TeamOrdinal { get; private set; }

    public void Set(DeploymentZone zone, TableSize size, Color colour)
    {
        Id = zone.Id;         TeamOrdinal = zone.TeamOrdinal;
        foreach (var c in GetChildren()) c.QueueFree();
        IReadOnlyList<Polygon> polygons;
        try { polygons = ZoneStyles.Resolve(zone.Shape, size); }
        catch (ArgumentException) { GD.PushWarning($"Unknown zone shape '{zone.Shape}'; nothing drawn."); return; }
        foreach (var polygon in polygons)
        {
            var verts = polygon.Vertices.Select(p => WorldFrame.ToGodot(new Point3(p.X, p.Y, 0.02))).ToArray();   // ZoneStyles answers in the wire frame
            var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
            for (var i = 1; i + 1 < verts.Length; i++) { st.AddVertex(verts[0]); st.AddVertex(verts[i + 1]); st.AddVertex(verts[i]); }   // fan: zone polygons are convex
            AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = Palette.Translucent(colour) });
        }
    }
}
