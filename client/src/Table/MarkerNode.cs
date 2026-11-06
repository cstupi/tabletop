using Godot;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>An objective marker: a disc of its diameter with its label, and a thin ring at the rule set's marker range about its centre
/// (Session.MarkerRange, from Welcome): a model whose base edge reaches the ring is within range. Markers ignore terrain (setup content).</summary>
public partial class MarkerNode : Node3D, IDrawn<(Marker Marker, Color? Team, double? Range)>
{
    public Guid MarkerId { get; init; }
    public Guid Id => MarkerId;
    public (Marker Marker, Color? Team, double? Range) Drawn { get; set; }
    MeshInstance3D? _disc;

    /// <summary>Once per node. A controlled objective takes its team's colour; a neutral one keeps the objective gold. A new colour or shape gets a new node.</summary>
    public void Build(Marker m, Color? team = null, double? range = null)
    {
        Place(m);
        var r = (float)m.Diameter / 2;
        var colour = team ?? new Color(0.95f, 0.8f, 0.2f);
        _disc = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r, Height = 0.1f }, Position = new Vector3(0, 0.05f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = colour } };
        AddChild(_disc);
        if (range is { } reach and > 0)
            AddChild(new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = (float)reach - 0.06f, OuterRadius = (float)reach + 0.06f, Rings = 96, RingSegments = 6 },
                Position = new Vector3(0, 0.06f, 0), Scale = new Vector3(1, 0.4f, 1), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D { AlbedoColor = colour with { A = 0.85f }, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
            });
        if (m.Label is { } text) AddChild(new Label3D { Text = text, Position = new Vector3(0, 0.6f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled });
    }

    public void Place(Marker m) { Position = WorldFrame.ToGodot(m.Position); Rotation = new Vector3(0, WorldFrame.Radians(m.Yaw), 0); }

    public Aabb Bounds => _disc is null ? new Aabb(GlobalPosition, Vector3.One) : _disc.GlobalTransform * _disc.GetAabb();
}
