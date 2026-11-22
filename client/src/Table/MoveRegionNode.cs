using Godot;
using Tabletop.Client.Core;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>Reachable regions as flat triangles just above the slab: the server's parameters, rendered by Tabletop.Geometry, drawn here. A move's area
/// is drawn in its bands (MoveBandLook), one mesh each.</summary>
public partial class MoveRegionNode : Node3D
{
    readonly List<MeshInstance3D> _meshes = [];

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

    /// <summary>The held models' areas (MoveRegions.Bands), each band in one mesh across every model, in its look for the moving team's colour.</summary>
    public void ShowBands(IReadOnlyList<MoveBands> areas, Color team)
    {
        Hide();
        var rgba = new Rgba(team.R, team.G, team.B, team.A);
        foreach (var (band, triangles) in MoveBandLook.Layers(areas))
        {
            var c = MoveBandLook.Colour(band, rgba);
            var colour = new Color(c.R, c.G, c.B, c.A);
            // The dangerous band draws over every model's clear and difficult bands (MoveBandLook.Priority, Lift).
            Add(triangles, MoveBandLook.Striped(band) ? Striped(colour) : Palette.Translucent(colour), MoveBandLook.Lift(band));
        }
    }

    /// <summary>Several areas in one mesh, each as triangles (three points each).</summary>
    public void ShowTriangles(IReadOnlyList<IReadOnlyList<Point2>> areas, Color colour)
    {
        Hide();
        Add(areas, Palette.Translucent(colour with { A = 0.25f }));
    }

    void Add(IReadOnlyList<IReadOnlyList<Point2>> areas, Material material, double lift = 0.03)
    {
        if (areas.All(a => a.Count < 3)) return;
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var area in areas)
            for (var i = 0; i + 2 < area.Count; i += 3)
            {
                Vector3 a = At(area[i], lift), b = At(area[i + 1], lift), c = At(area[i + 2], lift);
                // Godot culls clockwise-from-above back faces: wind each triangle one way whatever order the geometry gave it.
                if ((area[i + 1] - area[i]).Cross(area[i + 2] - area[i]) > 0) { st.AddVertex(a); st.AddVertex(c); st.AddVertex(b); }
                else { st.AddVertex(a); st.AddVertex(b); st.AddVertex(c); }
            }
        var mesh = new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(mesh);
        _meshes.Add(mesh);
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
    }
}
