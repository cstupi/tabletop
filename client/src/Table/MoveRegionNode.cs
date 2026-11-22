using Godot;
using Tabletop.Client.Core;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>Reachable regions as flat triangles just above the slab: the server's parameters, rendered by Tabletop.Geometry, drawn here. A move's area
/// is drawn in its bands (MoveBandLook), one standing mesh each, refilled from vertex arrays built off the main thread (MeshesOf); Warm draws every
/// band once under the slab when a move Ask opens, so a pickup's area adds no new node, mesh or material to the frame it lands in.</summary>
public partial class MoveRegionNode : Node3D
{
    readonly List<MeshInstance3D> _meshes = [];
    readonly Dictionary<MoveBand, (ArrayMesh Mesh, MeshInstance3D Node)> _bands = new();
    readonly HashSet<MoveBand> _warming = [];

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

    /// <summary>A move's area (MoveRegions.Area) as vertex arrays, one per band, each triangle wound one way: built on any thread (MeshesOf), so
    /// the main thread only hands them to Godot (ShowBands).</summary>
    public sealed record BandMeshes(IReadOnlyList<(MoveBand Band, Vector3[] Vertices)> Layers);

    /// <summary>The area's vertices per band (MoveBandLook.Layers), wound for Godot; no Godot object is made, so any thread may build them.</summary>
    public static BandMeshes MeshesOf(MoveBands area) =>
        new([.. MoveBandLook.Layers([area]).Select(l => (l.Band, Vertices(l.Areas, MoveBandLook.Lift(l.Band))))]);

    /// <summary>The held models' area in its bands, one mesh each, in its look for the moving team's colour.</summary>
    public void ShowBands(MoveBands area, Color team) => ShowBands(MeshesOf(area), team);

    /// <summary>Built band meshes drawn, each band's standing mesh refilled: one array handed to Godot per band.</summary>
    public void ShowBands(BandMeshes meshes, Color team)
    {
        Hide();
        foreach (var (band, vertices) in meshes.Layers) Fill(band, vertices, team);
    }

    /// <summary>A band's standing mesh (made on first use) refilled with these vertices in its look for the team's colour, hidden when it has none.</summary>
    void Fill(MoveBand band, Vector3[] vertices, Color team)
    {
        _warming.Remove(band);
        if (!_bands.TryGetValue(band, out var layer))
        {
            var mesh = new ArrayMesh();
            var node = new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
            AddChild(node);
            _bands[band] = layer = (mesh, node);
        }
        var c = MoveBandLook.Colour(band, new Rgba(team.R, team.G, team.B, team.A));
        var colour = new Color(c.R, c.G, c.B, c.A);
        // The dangerous band draws over every model's clear and difficult bands (MoveBandLook.Priority, and Lift in MeshesOf).
        Material material = MoveBandLook.Striped(band) ? Striped(colour) : Palette.Translucent(colour);
        if (layer.Node.MaterialOverride != material) layer.Node.MaterialOverride = material;
        layer.Mesh.ClearSurfaces();
        if (vertices.Length >= 3) layer.Mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, Arrays(vertices));
        layer.Node.Visible = vertices.Length >= 3;
    }

    /// <summary>Each band not showing drawn for one frame as a sliver under the slab at `at`, in the team's look, then hidden: its node, mesh and
    /// material exist and have drawn before the first pickup asks for them.</summary>
    public void Warm(Point3 at, Color team)
    {
        var under = WorldFrame.ToGodot(new Point3(at.X, at.Y, -1));
        Vector3[] sliver = [under, under + new Vector3(0.1f, 0, 0), under + new Vector3(0, 0, 0.1f)];
        foreach (var band in Enum.GetValues<MoveBand>())
        {
            if (_bands.TryGetValue(band, out var shown) && shown.Node.Visible) continue;
            Fill(band, sliver, team);
            _warming.Add(band);
        }
        AfterDraw(() => { foreach (var band in _warming) _bands[band].Node.Visible = false; _warming.Clear(); });
    }

    async void AfterDraw(Action then)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (IsInstanceValid(this)) then();
    }

    /// <summary>Several areas in one mesh, each as triangles (three points each).</summary>
    public void ShowTriangles(IReadOnlyList<IReadOnlyList<Point2>> areas, Color colour)
    {
        Hide();
        Add(Vertices(areas), Palette.Translucent(colour with { A = 0.25f }));
    }

    static Vector3[] Vertices(IReadOnlyList<IReadOnlyList<Point2>> areas, double lift = 0.03)
    {
        var vertices = new Vector3[areas.Sum(a => a.Count / 3 * 3)];
        var n = 0;
        foreach (var area in areas)
            for (var i = 0; i + 2 < area.Count; i += 3)
            {
                Vector3 a = At(area[i], lift), b = At(area[i + 1], lift), c = At(area[i + 2], lift);
                // Godot culls clockwise-from-above back faces: wind each triangle one way whatever order the geometry gave it.
                vertices[n++] = a;
                if ((area[i + 1] - area[i]).Cross(area[i + 2] - area[i]) > 0) { vertices[n++] = c; vertices[n++] = b; }
                else { vertices[n++] = b; vertices[n++] = c; }
            }
        return vertices;
    }

    static Godot.Collections.Array Arrays(Vector3[] vertices)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        return arrays;
    }

    void Add(Vector3[] vertices, Material material)
    {
        if (vertices.Length < 3) return;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, Arrays(vertices));
        var instance = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(instance);
        _meshes.Add(instance);
    }

    static readonly Dictionary<Color, StandardMaterial3D> _striped = new();
    static ImageTexture? _stripes;

    /// <summary>A see-through material in a colour, striped diagonally across the table every MoveBandLook.StripeSpacing inches (world triplanar
    /// mapping); shared per colour, never changed.</summary>
    static StandardMaterial3D Striped(Color c)
    {
        if (_striped.TryGetValue(c, out var made)) return made;
        if (_stripes is null)
        {
            const int Size = 16;
            var image = Image.CreateEmpty(Size, Size, false, Image.Format.Rgba8);
            for (var x = 0; x < Size; x++)
                for (var y = 0; y < Size; y++)
                    image.SetPixel(x, y, (x + y) % Size < Size / 2 ? new Color(1, 1, 1, 1) : new Color(1, 1, 1, 0.35f));
            image.GenerateMipmaps();
            _stripes = ImageTexture.CreateFromImage(image);
        }
        var scale = (float)(1 / MoveBandLook.StripeSpacing);
        return _striped[c] = new StandardMaterial3D
        {
            AlbedoColor = c, AlbedoTexture = _stripes, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1Scale = new Vector3(scale, scale, scale),
            RenderPriority = MoveBandLook.Priority(MoveBand.Dangerous),
        };
    }

    static Vector3 At(Point2 p, double lift) => WorldFrame.ToGodot(new Point3(p.X, p.Y, lift));

    public new void Hide()
    {
        foreach (var mesh in _meshes) if (IsInstanceValid(mesh)) mesh.QueueFree();
        _meshes.Clear();
        foreach (var (band, layer) in _bands) if (!_warming.Contains(band)) layer.Node.Visible = false;
    }
}
