using Godot;
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

    /// <summary>Once per node: the proxies come from the volumes. A placement whose volumes or mesh change gets a new node.</summary>
    public void Build(TerrainPlacement p)
    {
        Place(p);
        foreach (var tv in p.Volumes)
        {
            var v = tv.Volume; var height = (float)(v.Top - v.Bottom);
            Mesh mesh = v.Footprint.Kind == FootprintKind.Circle
                ? new CylinderMesh { TopRadius = (float)v.Footprint.A, BottomRadius = (float)v.Footprint.A, Height = height }
                : new BoxMesh { Size = new Vector3((float)v.Footprint.A, height, (float)v.Footprint.B) };
            var node = new MeshInstance3D { Mesh = mesh, MaterialOverride = Palette.Translucent(Palette.ForDeclarations(v.Declarations)),
                Visible = _mesh is null, Position = new Vector3((float)v.Offset.X, (float)v.Bottom + height / 2, (float)-v.Offset.Y), Rotation = new Vector3(0, WorldFrame.Radians(v.Yaw), 0) };
            _proxies.Add(node); AddChild(node);
        }
    }

    public void Place(TerrainPlacement p)
    {
        Position = WorldFrame.ToGodot(p.Position); Rotation = new Vector3(0, WorldFrame.Radians(p.Yaw), 0); Scale = Vector3.One * (float)p.Scale;
    }

    public void SetMesh(Node3D? scene) { _mesh?.QueueFree(); _mesh = scene; if (scene is not null) { AddChild(scene); foreach (var p in _proxies) p.Visible = false; } }
    public Aabb Bounds => _proxies.Count == 0 ? new Aabb(GlobalPosition, Vector3.One) : _proxies.Skip(1).Aggregate(_proxies[0].GlobalTransform * _proxies[0].GetAabb(), (a, m) => a.Merge(m.GlobalTransform * m.GetAabb()));
}
