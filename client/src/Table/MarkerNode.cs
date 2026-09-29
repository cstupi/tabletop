using Godot;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>A marker: a disc of its diameter with its label. Markers ignore terrain (setup content).</summary>
public partial class MarkerNode : Node3D, IHasId
{
    public Guid MarkerId { get; init; }
    public Guid Id => MarkerId;
    MeshInstance3D? _disc; Label3D? _label;

    public void Set(Marker m)
    {
        Position = WorldFrame.ToGodot(m.Position); Rotation = new Vector3(0, WorldFrame.Radians(m.Yaw), 0);
        _disc?.QueueFree(); _label?.QueueFree();
        var r = (float)m.Diameter / 2;
        _disc = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r, Height = 0.1f }, Position = new Vector3(0, 0.05f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = m.Kind == MarkerKind.Objective ? new Color(0.95f, 0.8f, 0.2f) : new Color(0.9f, 0.9f, 0.9f) } };
        AddChild(_disc);
        if (m.Label is { } text) { _label = new Label3D { Text = text, Position = new Vector3(0, 0.6f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled }; AddChild(_label); }
    }

    public Aabb Bounds => _disc is null ? new Aabb(GlobalPosition, Vector3.One) : _disc.GlobalTransform * _disc.GetAabb();
}
