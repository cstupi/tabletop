using Godot;
using Tabletop.Client.Core;

namespace Tabletop.Client.Table;

/// <summary>
/// Picking models (in-game UX spec → Moves): Shift+click on a model of my team toggles it in the picks, Shift+drag draws a box
/// from the press to the cursor and adds my team's models whose base centre is inside it (ModelPicks). The picks are the
/// selection's model ids, so they light as the selection does and reach presence. A new Ask, the end of the move, a plain click
/// on the table and Esc clear them.
/// </summary>
public partial class TableInput
{
    ModelPicks _picks = ModelPicks.None;
    /// <summary>A Shift press: where it went down and the piece under it. A box once it travels, a Shift+click if it does not.</summary>
    (Vector2 From, Node3D? Piece)? _box;
    CanvasLayer? _boxLayer; ReferenceRect? _boxRect;

    /// <summary>Models are picked: Esc clears them before anything else.</summary>
    public bool HasPicks => !_picks.IsEmpty;

    /// <summary>The picks end (a new Ask, the end of a move or placement); the unit stays selected.</summary>
    public void ClearPicks()
    {
        if (_picks.IsEmpty) return;
        var unit = _picks.UnitId!.Value; _picks = ModelPicks.None;
        if (Selected?.PieceId == unit) { Selected = _picks.Selection(unit); _presenceDirty = true; SelectionChanged?.Invoke(); }
    }

    void StartBox(Vector2 screen, Node3D? piece)
    {
        _box = (screen, piece);
        if (_boxLayer is null)
        {
            AddChild(_boxLayer = new CanvasLayer());
            _boxLayer.AddChild(_boxRect = new ReferenceRect { EditorOnly = false, BorderColor = Palette.Picked with { A = 1 }, BorderWidth = 1.5f, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false });
        }
    }

    /// <summary>The box from the press to the cursor, drawn once the press has travelled as far as a drag would.</summary>
    void DrawBox(Vector2 screen)
    {
        if (_box is not { } box || _boxRect is null) return;
        _boxRect.Visible = screen.DistanceTo(box.From) >= DragThreshold;
        _boxRect.Position = new Vector2(Mathf.Min(box.From.X, screen.X), Mathf.Min(box.From.Y, screen.Y));
        _boxRect.Size = (screen - box.From).Abs();
    }

    /// <summary>The Shift press released: a box adds the models inside it, a click toggles the model under it.</summary>
    void EndBox(Vector2 screen)
    {
        if (_box is not { } box) return;
        _box = null; if (_boxRect is not null) _boxRect.Visible = false;
        if (screen.DistanceTo(box.From) < DragThreshold) { ShiftClick(box.Piece); return; }
        var next = _picks.Boxed(Candidates(), new ScreenBox(box.From.X, box.From.Y, screen.X, screen.Y));
        if (!ReferenceEquals(next, _picks)) SetPicks(next, next.UnitId!.Value);
    }

    /// <summary>A model of my team goes in or out of the picks. Any other piece is selected as a plain click selects it, without a drag.</summary>
    void ShiftClick(Node3D? piece)
    {
        if (piece is PieceNode pn && Mine(pn.UnitId)) { SetPicks(_picks.Toggle(pn.UnitId, pn.ModelId), pn.UnitId); return; }
        if (piece is null) return;
        _picks = ModelPicks.None;
        Selected = new Protocol.Selection(piece is PieceNode other ? other.UnitId : IdOf(piece), []); _presenceDirty = true;
        SelectionChanged?.Invoke();
        if (piece is PieceNode enemy) UnitClicked?.Invoke(enemy.UnitId);
    }

    void SetPicks(ModelPicks picks, Guid unit)
    {
        _picks = picks; Selected = picks.Selection(unit); _presenceDirty = true;
        SelectionChanged?.Invoke(); UnitClicked?.Invoke(unit);
    }

    /// <summary>My team's drawn models with their base centres on screen; a model behind the camera is not one.</summary>
    List<PickCandidate> Candidates()
    {
        var cam = _view.Camera; var list = new List<PickCandidate>();
        foreach (var n in _view.Pieces.OfType<PieceNode>())
        {
            if (n.IsQueuedForDeletion() || !Mine(n.UnitId) || cam.IsPositionBehind(n.GlobalPosition)) continue;
            var at = cam.UnprojectPosition(n.GlobalPosition);
            list.Add(new PickCandidate(n.ModelId, n.UnitId, at.X, at.Y));
        }
        return list;
    }

    /// <summary>A unit of a team this account holds.</summary>
    bool Mine(Guid unitId) =>
        _session.Projection is { } p && _session.Game is { } game && new Names(game, p).TeamOfUnit(unitId) is { } team && _session.MyTeamIds.Contains(team);
}
