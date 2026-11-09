using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

public static class Palette
{
    public static Color Team(int ordinal) => Ui.Team(ordinal);
    public static readonly Color Proxy = new(0.3f, 0.9f, 0.4f, 0.45f);       // semi-transparent green (asset library)
    /// <summary>An unhighlighted model's proxy: Proxy blended halfway toward its team's colour, so the sides tell apart and a mesh keeps its look; Proxy with no team.</summary>
    public static Color Tinted(int? teamOrdinal) => teamOrdinal is { } o ? Proxy.Lerp(Team(o), 0.5f) with { A = Proxy.A } : Proxy;
    public static readonly Color Selected = new(1f, 1f, 1f);
    public static readonly Color Picked = new(1f, 0.85f, 0.2f, 0.7f);        // the selected unit's models
    public static readonly Color Hovered = new(0.25f, 0.9f, 1f);             // the models of the decision box option under the pointer
    /// <summary>A FixedSize Label3D's pixel size: a 40 px font reads about 20 px tall at 720 lines, whatever the camera's distance.</summary>
    public const float ScreenPixel = 0.0011f;
    public static readonly Color Ghost = new(0.9f, 0.9f, 1f, 0.35f);
    /// <summary>A ghost's mesh: this see-through per instance (0 opaque, 1 gone), with <see cref="GhostOverlay"/> washing it toward the ghost colour.</summary>
    public const float GhostTransparency = 0.4f;
    static StandardMaterial3D? _ghostOverlay, _ghostRing;
    /// <summary>One material for every ghost's mesh, drawn over its own materials: a pale blue wash, unlit, so ghosts never cost a material each.</summary>
    public static StandardMaterial3D GhostOverlay => _ghostOverlay ??= new()
    {
        AlbedoColor = new Color(0.75f, 0.85f, 1f, 0.35f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };
    static StandardMaterial3D GhostRingMaterial => _ghostRing ??= new()
    {
        AlbedoColor = new Color(0.85f, 0.9f, 1f, 0.9f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    /// <summary>A thin ring on a base's outline, just above the ground: a circle's or an oval's ellipse, a rectangle's four sides. Base A is the
    /// length along the facing (Godot −Z), B the width across (X), a circle's A its diameter.</summary>
    public static Node3D BaseRing(Base b, Material? material = null)
    {
        material ??= GhostRingMaterial;
        const float Lift = 0.03f, Line = 0.08f;
        var ring = new Node3D { Name = "BaseRing" };
        if (b.Shape == BaseShape.Rect)
        {
            float w = (float)b.B, l = (float)b.A;
            var sides = _rings.TryGetValue(b, out var made) ? made : _rings[b] = [new BoxMesh { Size = new Vector3(w, Line / 2, Line) }, new BoxMesh { Size = new Vector3(Line, Line / 2, l) }];
            foreach (var (mesh, at) in new[] { (sides[0], new Vector3(0, Lift, l / 2)), (sides[0], new Vector3(0, Lift, -l / 2)), (sides[1], new Vector3(w / 2, Lift, 0)), (sides[1], new Vector3(-w / 2, Lift, 0)) })
                ring.AddChild(new MeshInstance3D { Mesh = mesh, Position = at, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            return ring;
        }
        float across = (float)(b.Shape == BaseShape.Circle ? b.A : b.B), along = (float)b.A;
        // A torus of outer radius 1 scaled onto the ellipse: the tube is Line wide on the shorter axis.
        var tube = Line / Math.Min(across, along) * 2;
        var torus = _rings.TryGetValue(b, out var shared) ? shared[0] : (_rings[b] = [new TorusMesh { InnerRadius = 1 - tube, OuterRadius = 1, Rings = 48, RingSegments = 6 }])[0];
        ring.AddChild(new MeshInstance3D
        {
            Mesh = torus, Scale = new Vector3(across / 2, 0.25f, along / 2), Position = new Vector3(0, Lift, 0),
            MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        return ring;
    }
    public static readonly Color Unassigned = new(0.6f, 0.6f, 0.6f, 0.35f);
    public static readonly Color WoundDot = new(0.9f, 0.15f, 0.15f);              // wounds left over a model
    public static readonly Color TokenDot = new(0.3f, 0.55f, 1f);                 // spell tokens over a model
    public static readonly Color ShakenRing = new(1f, 0.85f, 0.1f);
    /// <summary>The activated band's colour: the theme's success green.</summary>
    public static Color ActivatedRing => Ui.Success;

    static readonly Dictionary<Color, StandardMaterial3D> _flat = new();
    /// <summary>A flat mark on the ground (the star and activated bands) in a colour: opaque, unlit, both faces, casting no shadow; shared like <see cref="Translucent"/>.</summary>
    public static StandardMaterial3D Flat(Color c) => _flat.TryGetValue(c, out var m) ? m
        : _flat[c] = new() { AlbedoColor = c with { A = 1 }, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Disabled };

    static readonly Dictionary<Base, Mesh> _bands = new();
    /// <summary>The activated band of a base (ModelMarks.Band), one mesh per base shared by every model on one.</summary>
    public static Mesh ActivatedBand(Base b) => _bands.TryGetValue(b, out var mesh) ? mesh : _bands[b] = Strip(ModelMarks.Band(b));

    /// <summary>How far a model mesh's lowest part, its base, reaches from the piece's centre across (x) and along (z): the furthest vertex within
    /// <see cref="GroundBand"/> of the lowest, less the piece's jolt. Each mesh instance is measured once per mesh and transform under the piece.</summary>
    public static (float X, float Z) GroundExtent(Node3D piece, Node3D scene, Vector3 jolt)
    {
        var parts = new List<(float Low, float X, float Z)>();
        foreach (var node in scene.FindChildren("*", nameof(MeshInstance3D), true, false).Prepend(scene))
        {
            if (node is not MeshInstance3D { Mesh: { } mesh } instance) continue;
            var at = Transform3D.Identity;
            for (Node3D? n = instance; n is not null && n != piece; n = n.GetParent() as Node3D) at = n.Transform * at;
            at.Origin -= jolt;
            parts.Add(_ground.TryGetValue((mesh, at), out var m) ? m : _ground[(mesh, at)] = Lowest(mesh, at));
        }
        if (parts.Count == 0) return (0, 0);
        var floor = parts.Min(p => p.Low);
        float x = 0, z = 0;
        foreach (var p in parts) if (p.Low <= floor + GroundBand) { x = Math.Max(x, p.X); z = Math.Max(z, p.Z); }
        return (x, z);
    }

    /// <summary>How high above a mesh's lowest vertex its base is taken to reach.</summary>
    const float GroundBand = 0.2f;
    static readonly Dictionary<(Mesh, Transform3D), (float Low, float X, float Z)> _ground = new();

    static (float Low, float X, float Z) Lowest(Mesh mesh, Transform3D at)
    {
        var points = new List<Vector3>();
        for (var i = 0; i < mesh.GetSurfaceCount(); i++)
            foreach (var v in mesh.SurfaceGetArrays(i)[(int)Mesh.ArrayType.Vertex].AsVector3Array()) points.Add(at * v);
        if (points.Count == 0) return (float.MaxValue, 0, 0);
        var low = points.Min(p => p.Y);
        float x = 0, z = 0;
        foreach (var p in points) if (p.Y <= low + GroundBand) { x = Math.Max(x, Math.Abs(p.X)); z = Math.Max(z, Math.Abs(p.Z)); }
        return (low, x, z);
    }

    /// <summary>A flat closed band through (inner, outer) samples: two triangles per sample, wire (x, y) to Godot (x, 0, -y).</summary>
    public static Mesh Strip(IReadOnlyList<RingSample> ring)
    {
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        static Vector3 G(Point2 q) => new((float)q.X, 0, (float)-q.Y);
        for (var i = 0; i < ring.Count; i++)
        {
            var (a, c) = (ring[i], ring[(i + 1) % ring.Count]);
            foreach (var v in new[] { a.Inner, a.Outer, c.Outer, a.Inner, c.Outer, c.Inner }) st.AddVertex(G(v));
        }
        return st.Commit();
    }

    /// <summary>A volume's tint: its strongest effect's colour (TerrainEffects.Primary), the legend's and the chips' colour, see-through.</summary>
    public static Color ForDeclarations(TerrainDeclarations d)
    {
        var c = TerrainEffects.Primary(d).Colour;
        return new(c.R, c.G, c.B, d == TerrainDeclarations.None ? 0.35f : 0.5f);
    }

    static readonly Dictionary<Color, StandardMaterial3D> _translucent = new(), _unshaded = new();
    /// <summary>One shared material per colour, never changed after it is made: a highlight or a redraw swaps materials instead of making
    /// new ones, and the pieces of one look draw with one material.</summary>
    public static StandardMaterial3D Translucent(Color c) => _translucent.TryGetValue(c, out var m) ? m
        : _translucent[c] = new() { AlbedoColor = c, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
    /// <summary>The base ring's material in a colour (opaque, unlit), shared like <see cref="Translucent"/>.</summary>
    public static StandardMaterial3D Unshaded(Color c) => _unshaded.TryGetValue(c, out var m) ? m
        : _unshaded[c] = new() { AlbedoColor = c, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };

    static readonly Dictionary<(Base, double), Mesh> _bodies = new();
    static readonly Dictionary<Base, Mesh[]> _rings = new();
    /// <summary>A model's body: its base extruded to its height, one mesh per base and height, shared by every model with them.</summary>
    public static Mesh Body(Base b, double height) => _bodies.TryGetValue((b, height), out var mesh) ? mesh : _bodies[(b, height)] = b.Shape switch
    {
        BaseShape.Circle => new CylinderMesh { TopRadius = (float)b.A / 2, BottomRadius = (float)b.A / 2, Height = (float)height },
        BaseShape.Rect => new BoxMesh { Size = new Vector3((float)b.B, (float)height, (float)b.A) },      // width across X, length along the facing axis
        _ => new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = (float)height },
    };
}
