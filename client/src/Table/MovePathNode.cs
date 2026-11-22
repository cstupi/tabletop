using Godot;
using Tabletop.Client.Core;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>
/// Move paths faintly on the table (MovePaths): each model's route from where it stood as a line, and its base's outline at each footprint along it
/// at the start's facing, in the moving team's colour; a footprint touching dangerous ground in the dangerous colour. Drawn just over the slab and
/// under the ghosts; one mesh per colour, refilled on each Show.
/// </summary>
public partial class MovePathNode : Node3D
{
    readonly Dictionary<Color, (ImmediateMesh Mesh, MeshInstance3D Node)> _layers = new();

    public void Show(IReadOnlyList<ModelPath> paths, Color team)
    {
        var lines = new Dictionary<Color, List<(Vector3, Vector3)>>();
        void Add(Color c, Vector3 a, Vector3 b) { if (!lines.TryGetValue(c, out var l)) lines[c] = l = []; l.Add((a, b)); }
        var rgba = new Rgba(team.R, team.G, team.B, team.A);
        var line = team with { A = MovePaths.LineAlpha };
        foreach (var path in paths)
        {
            var z = path.Start.Placement.Position.Z + 0.05;
            Vector3 At(Point2 p) => WorldFrame.ToGodot(new Point3(p.X, p.Y, z));
            for (var i = 0; i + 1 < path.Route.Count; i++) Add(line, At(path.Route[i]), At(path.Route[i + 1]));
            foreach (var f in path.Footprints)
            {
                var c = MovePaths.Colour(f, rgba);
                var colour = new Color(c.R, c.G, c.B, c.A);
                var ring = MovePaths.Outline(path.Start.Body.Base, f.At, path.Start.Placement.Yaw);
                for (var i = 0; i < ring.Count; i++) Add(colour, At(ring[i]), At(ring[(i + 1) % ring.Count]));
            }
        }
        foreach (var (colour, layer) in _layers) if (!lines.ContainsKey(colour)) layer.Node.Visible = false;
        foreach (var (colour, segments) in lines)
        {
            if (!_layers.TryGetValue(colour, out var layer))
            {
                var mesh = new ImmediateMesh();
                var node = new MeshInstance3D { Mesh = mesh, MaterialOverride = Palette.Unshaded(colour), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                AddChild(node); _layers[colour] = layer = (mesh, node);
            }
            layer.Mesh.ClearSurfaces(); layer.Mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
            foreach (var (a, b) in segments) { layer.Mesh.SurfaceAddVertex(a); layer.Mesh.SurfaceAddVertex(b); }
            layer.Mesh.SurfaceEnd();
            layer.Node.Visible = true;
        }
        Visible = paths.Count > 0;
    }

    public new void Hide() => Visible = false;
}
