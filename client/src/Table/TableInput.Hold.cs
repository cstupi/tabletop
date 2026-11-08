using Godot;
using Tabletop.Client.Core;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>
/// Models in hand (spec → Formations): 1–5 lay them out in that many rows about the cursor, the rows across the screen as the
/// camera sees it; Q/E held turn the rows, or with no rows each held model about its own centre, smoothly (p.7: models may move and
/// turn in any direction). The drop is the drag's: a move step waits for Enter, anything else is a hand move.
/// </summary>
public partial class TableInput
{
    /// <summary>Holding models of the projection: a terrain piece or a marker in hand takes no formation.</summary>
    bool HoldingModels => _drag is { Armed: true } d && _session.Projection is { } p && d.ModelIds.All(p.Models.ContainsKey);

    /// <summary>Holding a setup terrain piece: Q/E turn it about its origin, and it moves clear of what the turn swings it into.</summary>
    bool HoldingTerrain => _drag is { Armed: true } d && _session.Draft is { } draft && draft.Terrain.Any(t => t.Id == d.PieceId);

    /// <summary>Something in hand that Q/E turn: the camera does not orbit and follow waits.</summary>
    bool HoldingTurnable => HoldingModels || HoldingTerrain;

    /// <summary>One ruler per held piece, from where it stands in the projection to where it is now; kept and reused.</summary>
    readonly List<RulerNode> _rulers = [];

    void Arrange(int rows) { _drag!.Rows = rows; Place(Arranged(_drag.Cursor)); }

    void Turn(double degrees)
    {
        if (_drag is not { } drag) return;
        if (drag.Rows > 0) { drag.Turn += degrees; Place(Arranged(drag.Cursor)); return; }
        foreach (var id in drag.ModelIds) drag.Yaws[id] = new Yaw(drag.Yaws[id].Degrees + degrees);
        Place(drag.Current.ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    /// <summary>The held models in Rows rows about the point, facing away from the viewer turned by Q/E; the models take that facing.</summary>
    Dictionary<Guid, Point3> Arranged(Point3 centre)
    {
        var drag = _drag!; var p = _session.Projection!;
        var yaw = new Yaw(CameraYaw() + drag.Turn);
        foreach (var id in drag.ModelIds) drag.Yaws[id] = yaw;
        var at = Formation.Arrange(drag.ModelIds.Select(id => p.Models[id].Slot.Base).ToList(), drag.Rows, centre, yaw);
        return drag.ModelIds.Select((id, i) => (id, at[i])).ToDictionary(x => x.id, x => x.Item2);
    }

    /// <summary>The camera's heading over the table as a wire yaw (WorldFrame: wire Y is Godot −Z; yaw 0 faces +Y, positive counter-clockwise).</summary>
    double CameraYaw()
    {
        var forward = -_view.Camera.GlobalTransform.Basis.Z;
        var x = forward.X; var y = -forward.Z;
        return Math.Atan2(-x, y) * 180 / Math.PI;
    }

    void Face(Guid id, Yaw yaw)
    {
        if (((Node3D?)_view.PieceAt(id) ?? _view.TerrainAt(id)) is { } node) node.Rotation = new Vector3(0, WorldFrame.Radians(yaw), 0);
    }

    /// <summary>
    /// The held pieces toward these points, each facing its own yaw, and a ruler on each. Inside a move step each model's target is
    /// held to its reach from its origin: the allowance, and the difficult cap on a line through difficult terrain (MoveRegions.Clamp), and every one
    /// to the cap once any of the move's models goes in or through difficult terrain (MoveRegions.Allowance, p.11). Held models and a setup terrain piece
    /// are lifted until PageDown sets a level (held pieces lift over spec): lifted, each goes straight to its target, drawn hovering over what is under
    /// it, with a ring where it will land; at a level it slides along walls and other models that height and rests nearest the level
    /// (DragBodies.Models, DragBodies.Piece). A marker rests on the Solid under its point. Alt places freely, past the allowance too; the drop still rests. Inside a move step a model past
    /// the allowance, or short of a minimum the Ask set, turns red (spec → Formations: "a model beyond its move distance shows red").
    /// </summary>
    void Place(IReadOnlyDictionary<Guid, Point3> targets)
    {
        var drag = _drag!;
        var free = Input.IsKeyPressed(Key.Alt);                                          // Alt places freely, for corrections; the drop still rests
        var p = _session.Projection;
        Point2 Held(Guid id, double? cap) => !free && Reach is { } reach && Proposing.Contains(id) ? reach.Clamp(id, targets[id].Horizontal, cap) : targets[id].Horizontal;
        // p.11: the unit's allowance, read from where every model of the move is going (the held ones to their targets, the rest where their ghosts stand).
        double? Unit(Func<Guid, Point2> heldAt) => Reach is { } r && Allowance is not null
            ? r.Allowance((Proposed ?? new Dictionary<Guid, Point3>()).Where(kv => Proposing.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => targets.ContainsKey(kv.Key) ? heldAt(kv.Key) : kv.Value.Horizontal))
            : Allowance;
        var capped = Unit(id => Held(id, null)) is { } unit && unit < Allowance ? unit : (double?)null;
        Point2 To(Guid id) => Held(id, capped);
        // Lifted, inside a move step only a spot in the model's reach is a landing (held pieces lift over spec → Landing).
        Func<Guid, Point2, bool>? within = !free && Reach is { } inReach ? (id, at) => !Proposing.Contains(id) || inReach.Reaches(id, at, capped) : null;
        var holdsModels = p is not null && drag.ModelIds.All(p.Models.ContainsKey);
        var ends = holdsModels
            ? DragBodies.Models(drag.ModelIds.Select(id => new HeldBody(id, new PlacedModel(new ModelBody(p!.Models[id].Slot.Base, p.Models[id].Slot.Height),
                new Placement(drag.Current[id], drag.Yaws[id])), To(id))).ToList(), _stationary, _view.Volumes, Region, free, drag.Level, within,
                drag.ModelIds.All(Proposing.Contains) ? DragBodies.Abutment(p!, Region) : null)
            : targets.ToDictionary(kv => kv.Key, kv => _session.Draft is { } d && d.Terrain.Any(t => t.Id == kv.Key)
                ? DragBodies.Piece(d.Terrain, kv.Key, new Placement(drag.Current[kv.Key], drag.Yaws[kv.Key]), kv.Value.Horizontal, free, drag.Level)
                : Still(new Point3(kv.Value.X, kv.Value.Y, Collision.RestHeight(kv.Value.Horizontal, _settleAgainst))));   // a marker rests on the Solid under its point
        ShowLandings(ends, holdsModels);
        var lines = new List<(IReadOnlyList<Point3> Path, RulerReadout Readout)>();
        Violations.Moved(targets.Keys);                                                  // a refused commit's reason lapses once its models move
        foreach (var id in targets.Keys)
        {
            drag.Current[id] = ends[id].Lands;                                            // the ruler, the proposal, presence and the drop read the landing
            MoveNode(id, ends[id].Drawn);
            Face(id, drag.Yaws[id]);
            // In a move the ruler follows the path the base takes round what it may not pass (MoveRegions.Route, p.5) and reads its length.
            var route = Proposing.Contains(id) ? Reach?.Route(id, drag.Current[id].Horizontal, capped is not null) : null;
            var readout = route is { } r
                ? MoveRuler.Read(r.Distance, capped ?? Allowance, Reach?.Minimum(id, drag.Current[id].Horizontal))
                : MoveRuler.Read(drag.Origins[id].Horizontal, drag.Current[id].Horizontal, Proposing.Contains(id) ? capped ?? Allowance : null, Proposing.Contains(id) ? Reach?.Minimum(id, drag.Current[id].Horizontal) : null);
            if (Allowance is not null && Proposing.Contains(id) && _view.PieceAt(id) is { } piece) piece.SetHighlight(readout.OutOfRange ? Colors.Red : Palette.Selected);
            lines.Add((route is { } way ? RulerPath.Lift(way.Points, drag.Origins[id], drag.Current[id]) : [drag.Origins[id], drag.Current[id]], readout));
        }
        ModelsDragged?.Invoke(targets.Keys.Select(id => (id, drag.Current[id])).ToList());
        ShowRulers(lines);
        // A hand move is held to overlap (the server's placement judge); a move's or a deployment's ghosts are the decision box's to measure.
        if (p is not null && _session.Game is { } game && drag.ModelIds.All(p.Models.ContainsKey) && !drag.ModelIds.Any(Proposing.Contains))
            Violations.Measure(drag.ModelIds, ContactCheck.Words(ContactCheck.Placement(p, drag.ModelIds.ToDictionary(id => id, id => new Placement(drag.Current[id], drag.Yaws[id])), _view.Volumes),
                null, new Names(game, p).Unit));
    }

    static HeldEnd Still(Point3 at) => new(at, at);

    /// <summary>A ring where each lifted piece will land (held pieces lift over spec → Lifted): a model's base, a terrain piece's footprints.</summary>
    void ShowLandings(IReadOnlyDictionary<Guid, HeldEnd> ends, bool models)
    {
        var lifted = ends.Where(kv => kv.Value.Drawn != kv.Value.Lands).ToList();
        if (lifted.Count == 0) { _marks.Clear(); return; }
        if (models) { _marks.Show(lifted.Select(kv => (kv.Value.Lands, _drag!.Yaws[kv.Key], _session.Projection!.Models[kv.Key].Slot.Base)).ToList()); return; }
        var (id, end) = (lifted[0].Key, lifted[0].Value);
        if (_session.Draft?.Terrain.FirstOrDefault(t => t.Id == id) is { } piece) _marks.ShowPiece((piece with { Position = end.Lands, Yaw = _drag!.Yaws[id] }).WorldVolumes(), end.Lands.Z);
        else _marks.Clear();
    }

    /// <summary>
    /// PageDown and PageUp while holding (held pieces lift over spec → Held): the level one open surface down or up from where the lead piece lands
    /// (HoldLevel), above the highest lifted again; the held pieces go there at once.
    /// </summary>
    void StepLevel(bool up)
    {
        if (_drag is not { } drag) return;
        var lead = drag.ModelIds[0];
        var at = new Placement(drag.Current[lead], drag.Yaws[lead]);
        IReadOnlyList<double> surfaces = _session.Projection is { } p && p.Models.TryGetValue(lead, out var m)
            ? Collision.Surfaces(new PlacedModel(new ModelBody(m.Slot.Base, m.Slot.Height), at), _view.Volumes)
            : _session.Draft is { } d && d.Terrain.Any(t => t.Id == lead) ? DragBodies.Surfaces(d.Terrain, lead, at) : [];
        drag.Level = up ? HoldLevel.Raise(drag.Level, surfaces) : HoldLevel.Lower(drag.Level, surfaces);
        Place(drag.Rows > 0 ? Arranged(drag.Cursor) : drag.Grabs.ToDictionary(kv => kv.Key, kv => kv.Value.Position with { X = kv.Value.Position.X + drag.Cursor.X - drag.Start.X, Y = kv.Value.Position.Y + drag.Cursor.Y - drag.Start.Y }));
    }

    void ShowRulers(IReadOnlyList<(IReadOnlyList<Point3> Path, RulerReadout Readout)> lines)
    {
        while (_rulers.Count < lines.Count) { var r = new RulerNode(); _view.AddChild(r); _rulers.Add(r); }
        for (var i = 0; i < _rulers.Count; i++)
            if (i < lines.Count) _rulers[i].Show(lines[i].Path, lines[i].Readout, Colors.White); else _rulers[i].Hide();
    }

    void HideRulers() { foreach (var r in _rulers) r.Hide(); }
}
