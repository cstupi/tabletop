using Godot;
using Tabletop.Client.Core;

namespace Tabletop.Client.Table;

/// <summary>Session.Fired drawn: for one second, a beam in the shooter's team colour reaches from each shooter to its target and bolts run down it, then it fades.</summary>
public partial class Lasers : Node3D
{
    const double Seconds = 1.0, Reach = 0.15, BoltLength = 0.8; const int Bolts = 3;
    Session _s = null!;
    Action<IReadOnlyList<Shot>>? _onFired;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session;
        _onFired = shots => { foreach (var shot in shots) Fire(shot); };
        session.Fired += _onFired;
    }

    public override void _ExitTree() { if (_s is not null) _s.Fired -= _onFired; }

    Color ColourOf(Guid unitId)
    {
        var participant = _s.Projection?.Units.GetValueOrDefault(unitId)?.ParticipantId;
        var teamId = _s.Game?.Participants.FirstOrDefault(p => p.Id == participant)?.TeamId;
        return Palette.Team(_s.Game?.Teams.FirstOrDefault(t => t.Id == teamId)?.Ordinal ?? 0);
    }

    void Fire(Shot shot)
    {
        var a = WorldFrame.ToGodot(shot.From); var b = WorldFrame.ToGodot(shot.To);
        var length = a.DistanceTo(b);
        if (length < 0.01f) return;
        var colour = ColourOf(shot.ShooterUnitId);

        // The pivot stands at the shooter with its Y axis along the shot, so a unit-high cylinder scaled on Y is the beam.
        var pivot = new Node3D { Position = a, Basis = new Basis(new Quaternion(Vector3.Up, (b - a) / length)) };
        AddChild(pivot);
        var beamMaterial = Glow(colour, 0.6f);
        var beam = new Node3D { Scale = new Vector3(1, 0.001f, 1) }; pivot.AddChild(beam);
        beam.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = 1, RadialSegments = 8, Rings = 1 }, Position = Vector3.Up * 0.5f, MaterialOverride = beamMaterial });
        var boltMaterial = Glow(colour.Lightened(0.6f), 1f);
        var bolt = new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.1f, Height = (float)BoltLength, RadialSegments = 8, Rings = 2 }, MaterialOverride = boltMaterial, Visible = false };
        pivot.AddChild(bolt);

        var tween = pivot.CreateTween();
        tween.TweenProperty(beam, "scale:y", length, Reach);
        tween.TweenCallback(Callable.From(() => bolt.Visible = true));
        var each = (Seconds - Reach) / Bolts;
        for (var i = 0; i < Bolts; i++)
        {
            tween.TweenCallback(Callable.From(() => bolt.Position = Vector3.Zero));
            tween.TweenProperty(bolt, "position:y", length, each);
            if (i < Bolts - 1) tween.Parallel().TweenProperty(beamMaterial, "albedo_color:a", i % 2 == 0 ? 0.9f : 0.5f, each);   // the beam flickers as the bolts run
            else { tween.Parallel().TweenProperty(beamMaterial, "albedo_color:a", 0f, each); tween.Parallel().TweenProperty(boltMaterial, "albedo_color:a", 0f, each); }
        }
        tween.TweenCallback(Callable.From(pivot.QueueFree));
    }

    static StandardMaterial3D Glow(Color c, float alpha) => new()
    {
        AlbedoColor = c with { A = alpha }, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        EmissionEnabled = true, Emission = c, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };
}
