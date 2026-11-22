using Godot;
using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>One proxy per volume, placed by the volume's offset, yaw and heights under the piece's transform and uniform scale.</summary>
public partial class TerrainNode : Node3D, IDrawn<TerrainPlacement>
{
    public Guid PlacementId { get; init; }
    public Guid Id => PlacementId;
    public TerrainPlacement Drawn { get; set; } = null!;
    readonly List<MeshInstance3D> _proxies = new();
    Node3D? _mesh;
    bool _show = true, _editing;
    readonly List<Color> _colours = new();

    /// <summary>Every proxy draws one of two unit meshes scaled to its volume: a new mesh per proxy cost a mesh upload each, 2 ms a piece
    /// when a Random table lays twenty at once.</summary>
    static readonly Mesh UnitBox = new BoxMesh { Size = Vector3.One }, UnitCylinder = new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1 };

    /// <summary>Once per node: the proxies come from the volumes. A placement whose volumes or mesh change gets a new node.</summary>
    public void Build(TerrainPlacement p)
    {
        Place(p);
        foreach (var tv in p.Volumes)
        {
            var v = tv.Volume; var height = (float)(v.Top - v.Bottom);
            var circle = v.Footprint.Kind == FootprintKind.Circle;
            var size = circle ? new Vector3((float)v.Footprint.A * 2, height, (float)v.Footprint.A * 2) : new Vector3((float)v.Footprint.A, height, (float)v.Footprint.B);
            var node = new MeshInstance3D { Mesh = circle ? UnitCylinder : UnitBox, MaterialOverride = Palette.Translucent(Palette.ForDeclarations(v.Declarations)),
                Position = new Vector3((float)v.Offset.X, (float)v.Bottom + height / 2, (float)-v.Offset.Y), Rotation = new Vector3(0, WorldFrame.Radians(v.Yaw), 0),
                Scale = new Vector3(Math.Max(size.X, 0.001f), Math.Max(size.Y, 0.001f), Math.Max(size.Z, 0.001f)) };
            _proxies.Add(node); _colours.Add(Palette.ForDeclarations(v.Declarations)); AddChild(node);
        }
        Apply();
    }

    public void Place(TerrainPlacement p)
    {
        Position = WorldFrame.ToGodot(p.Position); Rotation = new Vector3(0, WorldFrame.Radians(p.Yaw), 0); Scale = Vector3.One * (float)p.Scale;
    }

    public void SetMesh(Node3D? scene) { _mesh?.QueueFree(); _mesh = scene; if (scene is not null) AddChild(scene); Apply(); }

    /// <summary>Show terrain hitboxes (ClientSettings.ShowTerrainHitboxes) and whether setup is editing this piece, live: see <see cref="HitboxLook.Terrain"/>.</summary>
    public void SetLook(bool showHitboxes, bool editing) { if (showHitboxes == _show && editing == _editing) return; _show = showHitboxes; _editing = editing; Apply(); }

    void Apply()
    {
        var look = HitboxLook.Terrain(_show, _mesh is not null, _editing);
        for (var i = 0; i < _proxies.Count; i++)
        {
            _proxies[i].Visible = look != TerrainProxy.Hidden;
            _proxies[i].MaterialOverride = Palette.Translucent(look == TerrainProxy.Faint ? _colours[i] with { A = 0.12f } : _colours[i]);
        }
    }

    public Aabb Bounds => _proxies.Count == 0 ? new Aabb(GlobalPosition, Vector3.One) : _proxies.Skip(1).Aggregate(_proxies[0].GlobalTransform * _proxies[0].GetAabb(), (a, m) => a.Merge(m.GlobalTransform * m.GetAabb()));
}
