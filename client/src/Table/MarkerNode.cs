using Godot;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>An objective marker: a disc of its diameter with its label. Markers ignore terrain (setup content).</summary>
public partial class MarkerNode : Node3D, IDrawn<(Marker Marker, Color? Team)>
{
    public Guid MarkerId { get; init; }
    public Guid Id => MarkerId;
    public (Marker Marker, Color? Team) Drawn { get; set; }
    MeshInstance3D? _disc;

    /// <summary>Once per node. A controlled objective takes its team's colour; a neutral one keeps the objective gold. A new colour or shape gets a new node.</summary>
    public void Build(Marker m, Color? team = null)
    {
        Place(m);
        var r = (float)m.Diameter / 2;
        _disc = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r, Height = 0.1f }, Position = new Vector3(0, 0.05f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = team ?? new Color(0.95f, 0.8f, 0.2f) } };
        AddChild(_disc);
        if (m.Label is { } text) AddChild(new Label3D { Text = text, Position = new Vector3(0, 0.6f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled });
    }

    public void Place(Marker m) { Position = WorldFrame.ToGodot(m.Position); Rotation = new Vector3(0, WorldFrame.Radians(m.Yaw), 0); }

    public Aabb Bounds => _disc is null ? new Aabb(GlobalPosition, Vector3.One) : _disc.GlobalTransform * _disc.GetAabb();
}
