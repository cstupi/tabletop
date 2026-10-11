using Godot;
using Tabletop.Client.Core;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>
/// A unit in hand (spec → Formations): 1–5 lay it out in that many rows about the cursor, the rows across the screen as the
/// camera sees it; Q/E turn the rows 15°. The drop is the drag's: a move step waits for Enter, anything else is a hand move.
/// </summary>
public partial class TableInput
{
    /// <summary>Holding models of the projection: a terrain piece or a marker in hand takes no formation.</summary>
    bool HoldingModels => _drag is { Armed: true } d && _session.Projection is { } p && d.ModelIds.All(p.Models.ContainsKey);

    void Arrange(int rows) { _drag!.Rows = rows; Place(Arranged(_drag.Cursor)); }

    void Turn(double degrees)
    {
        if (_drag is not { Rows: > 0 } drag) return;                    // the turn is the arrangement's; an unarranged unit keeps its shape
        drag.Turn += degrees; Place(Arranged(drag.Cursor));
    }

    /// <summary>The held models in Rows rows about the point, facing away from the viewer turned by Q/E; the models take that facing.</summary>
    Dictionary<Guid, Point3> Arranged(Point3 centre)
    {
        var drag = _drag!; var p = _session.Projection!;
        drag.Yaw = new Yaw(CameraYaw() + drag.Turn);
        var at = Formation.Arrange(drag.ModelIds.Select(id => p.Models[id].Slot.Base).ToList(), drag.Rows, centre, drag.Yaw);
        return drag.ModelIds.Select((id, i) => (id, at[i])).ToDictionary(x => x.id, x => x.Item2);
    }

    /// <summary>The camera's heading over the table as a wire yaw (WorldFrame: wire Y is Godot −Z; yaw 0 faces +Y, positive counter-clockwise).</summary>
    double CameraYaw()
    {
        var forward = -_view.Camera.GlobalTransform.Basis.Z;
        var x = forward.X; var y = -forward.Z;
        return Math.Atan2(-x, y) * 180 / Math.PI;
    }

    /// <summary>
    /// The held pieces toward these points and the ruler on the lead model. Held models slide along walls and other models and
    /// rest on the Solid tops under them (DragBodies.Models); a setup terrain piece slides and rests the same way (DragBodies.Piece);
    /// a marker rests on the Solid under its point. Alt places freely; the drop still rests. Inside a move step a model past the
    /// allowance, or short of a minimum the Ask set, turns red (spec → Formations: "a model beyond its move distance shows red").
    /// </summary>
    void Place(IReadOnlyDictionary<Guid, Point3> targets)
    {
        var drag = _drag!;
        var free = Input.IsKeyPressed(Key.Alt);                                          // Alt places freely, for corrections; the drop still rests
        var p = _session.Projection;
        var ends = p is not null && drag.ModelIds.All(p.Models.ContainsKey)
            ? DragBodies.Models(drag.ModelIds.Select(id => new HeldBody(id, new PlacedModel(new ModelBody(p.Models[id].Slot.Base, p.Models[id].Slot.Height),
                new Placement(drag.Current[id], drag.Yaw)), targets[id].Horizontal)).ToList(), _stationary, _view.Volumes, Region, free)
            : targets.ToDictionary(kv => kv.Key, kv => _session.Draft is { } d && d.Terrain.Any(t => t.Id == kv.Key)
                ? DragBodies.Piece(d.Terrain, kv.Key, kv.Value.Horizontal, free)
                : new Point3(kv.Value.X, kv.Value.Y, Collision.RestHeight(kv.Value.Horizontal, _settleAgainst)));   // a marker rests on the Solid under its point
        foreach (var id in targets.Keys)
        {
            drag.Current[id] = ends[id];
            MoveNode(id, drag.Current[id]);
            var piece = _view.PieceAt(id);
            if (drag.Rows > 0 && piece is not null) piece.Rotation = new Vector3(0, WorldFrame.Radians(drag.Yaw), 0);
            ModelDragged?.Invoke(id, drag.Current[id]);
            if (Allowance is { } allowance && Proposing.Contains(id) && piece is not null)
                piece.SetHighlight(MoveRuler.Read(drag.Starts[id].Horizontal, drag.Current[id].Horizontal, allowance, Minimum?.GetValueOrDefault(id)).OutOfRange ? Colors.Red : Palette.Selected);
        }
        var lead = drag.ModelIds[0];
        _ruler.Show(drag.Starts[lead], drag.Current[lead],
            MoveRuler.Read(drag.Starts[lead].Horizontal, drag.Current[lead].Horizontal, Proposing.Contains(lead) ? Allowance : null, Proposing.Contains(lead) ? Minimum?.GetValueOrDefault(lead) : null), Colors.White);
    }
}
