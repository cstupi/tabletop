using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Panels;
using Tabletop.Client.Table;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Projection = Tabletop.Protocol.Projection;

namespace Tabletop.Client.Hud;

public partial class DecisionBox
{
    Dictionary<Guid, Point3>? _proposed, _deployAt; Guid? _deployUnit; bool _pointArmed;
    // What YawOf needs from the disembark render: the exit distance, the board's volumes and its size.
    double _exitWithin; IReadOnlyList<WorldVolume> _volumes = []; TableSize _size = new(72, 48);

    void RenderMove(Ask ask, MoveRegion legal, Projection p, Names names)
    {
        if (legal.ModelIds.Count == 0) return;                                      // nothing to move; the server closes the Ask
        var lead = p.Models[legal.ModelIds[0]];
        var body = BodyOf(p, lead.Id);
        var ordinal = _s.Game?.Teams.FirstOrDefault(t => t.Id == _s.MyTeamId)?.Ordinal ?? 1;
        _input.Proposing.Clear(); foreach (var id in legal.ModelIds) _input.Proposing.Add(id);
        if (MoveRuler.IsDisembark(legal))
        {
            // p.15 "fully within 6" of it when exiting": no start placement, so the region is the container's footprint grown by the exit distance.
            var containerId = legal.Containment!.ExitingId!.Value;
            if (p.Models.GetValueOrDefault(containerId)?.Location is not OnTable at) { _note.Text = "The container is no longer on the table."; Unhook(); return; }
            var within = legal.Containment.ExitWithin ?? 0;
            (_exitWithin, _volumes, _size) = (within, _view.Volumes, _view.Size);
            _region!.Show(MoveRuler.ExitRegion(body, new PlacedModel(BodyOf(p, containerId), at.Placement), legal, _volumes, _size), Palette.Team(ordinal));
            _note.Text = $"Disembark: place every model fully within {within:0.0}\" of {names.Piece(containerId)}. Drag them out, then commit.";
            _input.Allowance = null;
            void StartExit() { _view.ShowExitGhosts(legal.ModelIds, containerId); _proposed = legal.ModelIds.ToDictionary(id => id, _ => at.Placement.Position); }
            StartExit();
            _onCancel = () => { StartExit(); return true; };
        }
        else
        {
            if (lead.Location is not OnTable on) { Unhook(); return; }
            var frame = ask.FrameId is { } f ? p.Stack.FirstOrDefault(x => x.Id == f) : null;
            var used = frame is null ? 0 : MoveRuler.UsedInFrame(frame, lead.Id);
            var remaining = Math.Max(0, legal.Allowance - used);
            _region!.Show(MoveRuler.Region(new PlacedModel(body, on.Placement), legal with { Allowance = remaining }, _view.Volumes, _view.Size), Palette.Team(ordinal));
            _note.Text = $"Move up to {remaining:0.0}\" (used {used:0.0}\").";
            _input.Allowance = remaining;
            // Drawn and committed agree: a rebuilt Ask (a refusal) or an Esc starts from the server's positions.
            void StartMove() { _proposed = legal.ModelIds.ToDictionary(id => id, id => ((OnTable)p.Models[id].Location).Placement.Position); _view.Snap(legal.ModelIds); }
            StartMove();
            _onCancel = () => { StartMove(); return true; };
            OfferEmbark(ask, legal, p, names);
        }
        void Commit()
        {
            if (_proposed is not { } proposed) return;
            Send(ask, new Destinations(legal.ModelIds.Select(id => new ModelRelocation(id, p.Models[id].Location, new OnTable(new Placement(proposed[id], YawOf(p, id))))).ToList()));
        }
        _onEnter = Commit;
        Add(Btn("Commit move (Enter)", Commit));
    }

    /// <summary>p.15: "only one model needs to reach the Transport for everyone to embark". A click on a listed container answers the
    /// whole unit into it: one relocation per model to the same Inside. The player may still drag and commit instead.</summary>
    void OfferEmbark(Ask ask, MoveRegion legal, Projection p, Names names)
    {
        IReadOnlyList<Guid> enterable = legal.Containment?.Enterable ?? [];
        if (enterable.Count == 0) return;
        _note.Text += $" Or click a container in reach to embark: {string.Join(", ", enterable.Select(names.Piece))}.";
        _input.Eligible = enterable.ToHashSet();
        _onTarget = clicked =>
        {
            if (Resolve(clicked, enterable) is not { } container || !p.Models.ContainsKey(container)) return;
            _input.CancelDrag();
            Send(ask, new Destinations(legal.ModelIds.Select(id => new ModelRelocation(id, p.Models[id].Location, new Inside(container))).ToList()));
        };
    }

    /// <summary>The facing the answer carries. A model on the table takes its node's facing. A disembarking model takes whatever fits at
    /// the point it was dropped (p.7), from the same search that drew the region; the container's facing is the fallback.</summary>
    Yaw YawOf(Projection p, Guid modelId) => p.Models[modelId].Location switch
    {
        OnTable on => NodeYaw(modelId) ?? on.Placement.Yaw,
        Inside i when p.Models.GetValueOrDefault(i.ContainerId)?.Location is OnTable c =>
            MoveRuler.ExitYawAt(BodyOf(p, modelId), new PlacedModel(BodyOf(p, i.ContainerId), c.Placement),
                _exitWithin, _proposed![modelId].Horizontal, _volumes, _size) ?? c.Placement.Yaw,
        _ => new Yaw(0),
    };

    Yaw? NodeYaw(Guid modelId) => _view.PieceAt(modelId) is { } n && IsInstanceValid(n) ? WorldFrame.YawFrom(n.Rotation.Y) : null;

    static ModelBody BodyOf(Projection p, Guid modelId) { var m = p.Models[modelId]; return new ModelBody(m.Slot.Base, m.Slot.Height); }

    void OnDragged(Guid modelId, Point3 position)
    {
        if (_proposed is not null && _proposed.ContainsKey(modelId)) _proposed[modelId] = position;
        if (_deployAt is not null && _deployAt.ContainsKey(modelId)) _deployAt[modelId] = position;
    }

    /// <summary>Deployment placement: the server waits for every model of the unit just picked (MidDeploy). A click on the table drops
    /// the shelved models there as ghosts in a row; they drag and take formations like a move; Enter places them with one MoveModels
    /// from the shelf. Nothing is sent before Enter.</summary>
    void StartDeploy(MidDeploy mid, Names names)
    {
        _deployUnit = mid.UnitId;
        _title.Text = $"Place the models of {names.Unit(mid.UnitId)}";
        _hint.Text = "Click the table where the unit goes";
        var unit = mid.UnitId;
        _pointArmed = true;
        _input.PickNextPoint(at => { _pointArmed = false; DropDeploy(unit, at); });
    }

    void DropDeploy(Guid unit, Point3 at)
    {
        if (_deployUnit != unit || _s.Projection is not { } p) return;              // the deploy moved on before the click
        var models = p.Models.Values.Where(m => m.UnitId == unit && m.Location is OnShelf).OrderBy(m => m.Slot.Ordinal).ToList();
        var layout = DeployView.Layout(models, at, _view.Size);
        if (layout.Count == 0) return;
        _deployAt = layout.ToDictionary(r => r.ModelId, r => ((OnTable)r.To).Placement.Position);
        _input.Proposing.Clear(); foreach (var r in layout) _input.Proposing.Add(r.ModelId);
        _view.ShowGhosts(layout);
        _hint.Text = "Drag to place · Shift: one model · 1–5 while holding: rows · Enter: place · Esc: clear";
        _onEnter = PlaceDeploy;
        _onCancel = () => { Reset(); Render(); return true; };                    // Reset drops _deployUnit, so Render arms a fresh click
        Add(Btn("Place (Enter)", PlaceDeploy));
        Render();
    }

    void PlaceDeploy()
    {
        if (_deployAt is null || _s.Projection is not { } p) return;
        var moves = _deployAt.Select(kv => new ModelRelocation(kv.Key, p.Models[kv.Key].Location, new OnTable(new Placement(kv.Value, NodeYaw(kv.Key) ?? new Yaw(0))))).ToList();
        _onEnter = null; _onCancel = null; SetButtons(true);
        var id = Guid.CreateVersion7(); _lastCommand = id;                          // a Refused for it lands in _onRefused, which rebuilds the placement
        Guarded.Run(() => _s.SendCommandAsync(new MoveModels(moves), id), message => { _refusal = $"Could not place the models: {message}"; _refusalAsk = null; Reset(); Render(); });
    }
}
