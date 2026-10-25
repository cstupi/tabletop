using Godot;
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

    public static Color ForDeclarations(TerrainDeclarations d) => d switch
    {
        _ when d.HasFlag(TerrainDeclarations.Impassable) => new(0.5f, 0.5f, 0.55f, 0.6f),
        _ when d.HasFlag(TerrainDeclarations.Dangerous) => new(0.9f, 0.4f, 0.2f, 0.5f),
        _ when d.HasFlag(TerrainDeclarations.DangerousOnRushCharge) => new(0.8f, 0.55f, 0.3f, 0.5f),
        _ when d.HasFlag(TerrainDeclarations.BlocksLineOfSight) => new(0.45f, 0.35f, 0.3f, 0.6f),
        _ when d.HasFlag(TerrainDeclarations.Cover) => new(0.3f, 0.65f, 0.3f, 0.5f),
        _ when d.HasFlag(TerrainDeclarations.Difficult) => new(0.7f, 0.65f, 0.3f, 0.5f),
        _ => new(0.7f, 0.7f, 0.7f, 0.4f),
    };

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
