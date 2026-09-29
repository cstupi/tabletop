using Godot;
using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Table;

/// <summary>What others are doing, drawn in their team's colour and never animated: cursors, measurements, held pieces, ghosts, pings, and their rulers.</summary>
public partial class PresenceOverlay : Node3D
{
    TableView _view = null!; Session _session = null!;
    readonly Dictionary<Guid, Node3D> _cursors = new(), _measures = new(), _ghosts = new(); readonly Dictionary<Guid, RulerNode> _rulers = new();
    readonly Dictionary<Guid, Presence> _drawn = new();   // what this overlay last drew per participant, so Remove can release it

    public void Bind(TableView view, Session session)
    {
        _view = view; _session = session;
        session.PresenceChanged += Draw; session.Pinged += OnPing;
    }

    public override void _ExitTree()
    {
        if (_session is null) return;
        _session.PresenceChanged -= Draw; _session.Pinged -= OnPing;
    }

    Color ColourOf(Guid participantId)
    {
        var teamId = _session.Game?.Participants.FirstOrDefault(p => p.Id == participantId)?.TeamId;
        var ordinal = _session.Game?.Teams.FirstOrDefault(t => t.Id == teamId)?.Ordinal ?? 0;
        return ordinal == 0 ? Colors.LightGray : Palette.Team(ordinal);
    }

    void Draw(Guid participantId)
    {
        Remove(participantId);
        var value = _session.Others.Of(participantId);
        if (value is null) { _view.Redraw(); return; }                     // a held piece returns to where the projection has it
        _drawn[participantId] = value;
        var colour = ColourOf(participantId);
        if (value.Cursor is { } c)
        {
            var cone = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0, BottomRadius = 0.25f, Height = 0.8f }, Position = WorldFrame.ToGodot(c.Point) + Vector3.Up * 0.5f, MaterialOverride = Palette.Translucent(colour) };
            AddChild(cone); _cursors[participantId] = cone;
            if (c.Target is { } target) _view.PieceAt(target)?.SetHighlight(colour);
        }
        if (value.Measurement is { } m)
        {
            var ruler = new RulerNode(); AddChild(ruler); ruler.Show(m.From, m.To, MoveRuler.Read(m.From.Horizontal, m.To.Horizontal, null), colour); _measures[participantId] = ruler;
        }
        foreach (var placement in value.Placements)
        {
            if (placement.Kind == PlacementKind.Held)
            {
                Node3D? node = _view.PieceAt(placement.PieceId) ?? (Node3D?)_view.TerrainAt(placement.PieceId) ?? _view.MarkerAt(placement.PieceId);
                if (node is null) continue;
                node.Position = WorldFrame.ToGodot(placement.Position); node.Rotation = new Vector3(0, WorldFrame.Radians(placement.Yaw), 0);
                if (node is PieceNode piece) piece.SetHighlight(colour);
                var start = StartOf(placement.PieceId);
                if (start is { } s) { var ruler = new RulerNode(); AddChild(ruler); ruler.Show(s, placement.Position, MoveRuler.Read(s.Horizontal, placement.Position.Horizontal, null), colour); _rulers[placement.PieceId] = ruler; }
            }
            else
            {
                var ghost = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.6f, BottomRadius = 0.6f, Height = 1.5f }, Position = WorldFrame.ToGodot(placement.Position) + Vector3.Up * 0.75f, MaterialOverride = Palette.Translucent(Palette.Ghost) };
                AddChild(ghost); _ghosts[placement.PieceId] = ghost;
            }
        }
    }

    /// <summary>The start the viewer already holds: the projection's placement, or the draft's (presence → drawn "against the start position already in their projection").</summary>
    Point3? StartOf(Guid pieceId)
    {
        var p = _session.Projection; var t = p?.Board.Table ?? _session.Draft;
        if (p?.Models.GetValueOrDefault(pieceId)?.Location is OnTable on) return on.Placement.Position;
        return t?.Terrain.FirstOrDefault(x => x.Id == pieceId)?.Position ?? t?.Markers.FirstOrDefault(m => m.Id == pieceId)?.Position;
    }

    void Remove(Guid participantId)
    {
        foreach (var d in new[] { _cursors, _measures }) if (d.Remove(participantId, out var n)) n.QueueFree();
        _drawn.Remove(participantId, out var previous);
        foreach (var placement in previous?.Placements ?? [])
        { if (_rulers.Remove(placement.PieceId, out var r)) r.QueueFree(); if (_ghosts.Remove(placement.PieceId, out var g)) g.QueueFree(); _view.PieceAt(placement.PieceId)?.SetHighlight(null); }
        if (previous?.Cursor?.Target is { } target) _view.PieceAt(target)?.SetHighlight(null);
    }

    void OnPing(Ping ping)
    {
        var ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.8f, OuterRadius = 1.2f }, Position = WorldFrame.ToGodot(ping.Point) + Vector3.Up * 0.1f, MaterialOverride = Palette.Translucent(ColourOf(ping.Sender)) };
        AddChild(ring);
        var tween = CreateTween(); tween.TweenProperty(ring, "scale", Vector3.One * 3, 2.0); tween.Parallel().TweenProperty(ring, "transparency", 1.0f, 2.0); tween.TweenCallback(Callable.From(ring.QueueFree));
    }
}
