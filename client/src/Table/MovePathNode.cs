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
    readonly Dictionary<Color, (ArrayMesh Mesh, MeshInstance3D Node)> _layers = new();

    IReadOnlyList<ModelPath> _shown = []; Color _team;

    /// <summary>Refilled only when a path or the colour changed: a drag frame hands back the paths of ghosts that stayed put (MovePathCache).</summary>
    public void Show(IReadOnlyList<ModelPath> paths, Color team)
    {
        if (team == _team && paths.Count == _shown.Count && paths.Select((path, i) => ReferenceEquals(path, _shown[i])).All(same => same)) { Visible = paths.Count > 0; return; }
        _shown = paths; _team = team;
        var lines = new Dictionary<Color, List<(Vector3, Vector3)>>();
        void Add(Color c, Vector3 a, Vector3 b) { if (!lines.TryGetValue(c, out var l)) lines[c] = l = []; l.Add((a, b)); }
        var rgba = new Rgba(team.R, team.G, team.B, team.A);
        var l = MovePaths.Line(rgba);
        var line = new Color(l.R, l.G, l.B, l.A);
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
        Draw(lines);
        Visible = paths.Count > 0;
    }

    /// <summary>Each colour's layer (made on first use) refilled with its segments; a colour with none hidden.</summary>
    void Draw(Dictionary<Color, List<(Vector3, Vector3)>> lines)
    {
        foreach (var (colour, layer) in _layers) if (!lines.ContainsKey(colour)) layer.Node.Visible = false;
        foreach (var (colour, segments) in lines)
        {
            if (!_layers.TryGetValue(colour, out var layer))
            {
                var mesh = new ArrayMesh();
                var node = new MeshInstance3D { Mesh = mesh, MaterialOverride = Material(colour), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                AddChild(node); _layers[colour] = layer = (mesh, node);
            }
            // One array a colour handed to Godot at once: a vertex a call (ImmediateMesh) cost a drag frame up to 15 ms over a unit's footprints.
            var vertices = new Vector3[segments.Count * 2];
            for (var i = 0; i < segments.Count; i++) (vertices[2 * i], vertices[2 * i + 1]) = segments[i];
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = vertices;
            layer.Mesh.ClearSurfaces();
            layer.Mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
            layer.Node.Visible = true;
        }
    }

    /// <summary>The line and both footprint colours (the team's and the dangerous one) drawn for one frame as a stroke under the slab at `at`, then
    /// hidden unless paths came meanwhile: their layers, meshes and materials exist and have drawn before the first pickup.</summary>
    public void Warm(Point3 at, Color team)
    {
        if (_shown.Count > 0) return;
        var rgba = new Rgba(team.R, team.G, team.B, team.A);
        var under = WorldFrame.ToGodot(new Point3(at.X, at.Y, -1));
        var lines = new Dictionary<Color, List<(Vector3, Vector3)>>();
        foreach (var c in new[] { MovePaths.Line(rgba), MovePaths.Colour(new PathFootprint(default, false), rgba), MovePaths.Colour(new PathFootprint(default, true), rgba) })
            lines[new Color(c.R, c.G, c.B, c.A)] = [(under, under + new Vector3(0.1f, 0, 0))];
        Draw(lines);
        Visible = true;
        AfterDraw();
    }

    async void AfterDraw()
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (IsInstanceValid(this) && _shown.Count == 0) Visible = false;
    }

    public new void Hide() => Visible = false;

    static readonly Dictionary<Color, StandardMaterial3D> _materials = new();

    /// <summary>An unlit see-through line material per colour, drawn after the move's bands (MoveBandLook.Priority) so the path reads over them; shared,
    /// never changed.</summary>
    static StandardMaterial3D Material(Color c) => _materials.TryGetValue(c, out var m) ? m : _materials[c] = new StandardMaterial3D
    {
        AlbedoColor = c, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        RenderPriority = MoveBandLook.Priority(MoveBand.Dangerous) + 1,
    };
}
