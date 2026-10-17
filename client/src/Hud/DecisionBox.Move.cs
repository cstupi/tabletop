using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Panels;
using Tabletop.Client.Table;
using Tabletop.Client.Theming;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Projection = Tabletop.Protocol.Projection;

namespace Tabletop.Client.Hud;

public partial class DecisionBox
{
    Dictionary<Guid, Point3>? _proposed, _deployAt; Guid? _deployUnit;
    /// <summary>The facings the player gave the ghosts, read at each drop, and the ghosts kept across a reconnect.</summary>
    readonly Dictionary<Guid, Yaw> _turned = new(); readonly GhostStash _stash = new();
    // What YawOf needs from the disembark render: the exit distance, the board's volumes and its size.
    double _exitWithin; IReadOnlyList<WorldVolume> _volumes = []; TableSize _size = new(72, 48);
    /// <summary>The open move's per-model regions and the team colour they draw in; null outside a move and for a disembark.</summary>
    MoveRegions? _regions; Color _regionColour;
    /// <summary>The move's commit against where its ghosts stand, read again on every drag and reset.</summary>
    Action? _regate; MoveGate _gate = new(true, null);

    void RenderMove(Ask ask, MoveRegion legal, Projection p, Names names)
    {
        if (legal.ModelIds.Count == 0) return;                                      // nothing to move; the server closes the Ask
        var lead = p.Models[legal.ModelIds[0]];
        double? allowance = null;
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
            var fromShelf = MoveRuler.IsShelfPlacement(legal, p);                   // a Spawn or Split reserve: placed around its anchor model, the Ask's prompt says what
            _note.Text = fromShelf ? $"{ask.Prompt}. Drag them into place, then commit."
                : $"Disembark: place every model fully within {within:0.0}\" of {names.Piece(containerId)}. Drag them out, then commit.";
            _input.Allowance = null; _input.Minimum = null; _input.Region = null;
            void StartExit()
            {
                if (fromShelf)
                {
                    var layout = DeployView.Around(legal.ModelIds.Select(id => p.Models[id]).ToList(), at.Placement, p.Models[containerId].Slot.Base, _view.Size, legal.Separation);
                    _view.ShowGhosts(layout);
                    _proposed = layout.ToDictionary(r => r.ModelId, r => ((OnTable)r.To).Placement.Position);
                    return;
                }
                _view.ShowExitGhosts(legal.ModelIds, containerId); _proposed = legal.ModelIds.ToDictionary(id => id, _ => at.Placement.Position);
            }
            StartExit();
            _onCancel = () => { StartExit(); return true; };
        }
        else
        {
            if (lead.Location is not OnTable) { Unhook(); return; }
            var frame = ask.FrameId is { } f ? p.Stack.FirstOrDefault(x => x.Id == f) : null;
            var used = frame is null ? 0 : MoveRuler.UsedInFrame(frame, lead.Id);
            // The Ask's Allowance is already what this segment has left (GfMoves reduces it by the distance a cast-split move used): never subtract `used` again.
            var remaining = Math.Max(0, legal.Allowance); allowance = remaining;
            // Each model's own region from where it stands: the lead's shows first, then each pickup's held models'.
            var regions = MoveRegions.Of(p, legal with { Allowance = remaining }, _view.Volumes, _view.Size);
            _regions = regions; _regionColour = Palette.Team(ordinal);
            ShowRegions([lead.Id]);
            // An Aircraft must move at least its minimum (p.13): the region is the ring beyond it, and a model short of it shows red.
            var minimum = regions.Minimum(lead.Id);
            _note.Text = minimum is { } must ? $"Move {must:0.0}\" to {remaining:0.0}\" (used {used:0.0}\") · Alt: place freely." : $"Move up to {remaining:0.0}\" (used {used:0.0}\") · Alt: place freely.";
            _input.Allowance = remaining; _input.Region = legal; _input.Reach = regions;
            _input.Minimum = legal.MinDistance is null ? null : legal.ModelIds.Where(regions.Has).ToDictionary(id => id, id => regions.Minimum(id)!.Value);
            // Drawn and committed agree: a rebuilt Ask (a refusal) or an Esc starts from the server's positions.
            void StartMove() { _proposed = legal.ModelIds.ToDictionary(id => id, id => ((OnTable)p.Models[id].Location).Placement.Position); _view.Snap(legal.ModelIds); }
            StartMove();
            _onCancel = () => { StartMove(); _regate?.Invoke(); return true; };
            OfferEmbark(ask, legal, p, names);
        }
        // A stop (p.13 Caster(X) "at any point before attacking") sends the ghosts as they stand: with no drag, that is a stop before moving.
        void Relocate(bool continues)
        {
            if (_proposed is not { } proposed || !_gate.Allowed) return;
            Send(ask, new Destinations(legal.ModelIds.Select(id => new ModelRelocation(id, p.Models[id].Location, new OnTable(new Placement(proposed[id], YawOf(p, id))))).ToList(), continues));
        }
        void Commit() => Relocate(false);
        _onEnter = Commit;
        var commit = Btn("Commit move (Enter)", Commit); Add(commit);
        Button? stop = null;
        if (legal.Stoppable && !MoveRuler.IsDisembark(legal))
        {
            void Stop() => Relocate(true);
            _onStop = Stop;
            Add(stop = Btn($"{DecisionView.StopLabel(p, legal)} (C)", Stop));
        }
        // Any ghost out of range names how many; under strict the commit and the stop wait until none is.
        var reason = new Label { ThemeTypeVariation = Variation.Warning, AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false }; Add(reason);
        _regate = () =>
        {
            if (_proposed is not { } proposed || !IsInstanceValid(commit)) return;
            var readouts = allowance is null ? [] : legal.ModelIds.Where(id => p.Models[id].Location is OnTable && proposed.ContainsKey(id))
                .Select(id => MoveRuler.Read(((OnTable)p.Models[id].Location).Placement.Position.Horizontal, proposed[id].Horizontal, allowance, _input.Minimum?.GetValueOrDefault(id))).ToList();
            var placed = proposed.ToDictionary(kv => kv.Key, kv => new Placement(kv.Value, NodeYaw(kv.Key) ?? new Yaw(0)));
            _gate = MoveRuler.Gate(readouts, _s.Game?.Enforcement ?? Enforcement.Strict, MoveRuler.TooClose(p, legal, placed), legal.Separation);
            commit.Disabled = !_gate.Allowed; if (stop is not null) stop.Disabled = !_gate.Allowed;
            reason.Text = _gate.Reason ?? ""; reason.Visible = _gate.Reason is not null;
        };
        _regate();
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
        OnShelf => NodeYaw(modelId) ?? new Yaw(0),                                 // a reserve placed from the shelf faces as its ghost does (Q/E)
        _ => new Yaw(0),
    };

    Yaw? NodeYaw(Guid modelId) => _view.PieceAt(modelId) is { } n && IsInstanceValid(n) ? WorldFrame.YawFrom(n.Rotation.Y) : null;

    /// <summary>Each of these models' own region, those the move holds; the outline stays on the last held models after the drop.</summary>
    void ShowRegions(IReadOnlyList<Guid> modelIds)
    {
        if (_regions is not { } regions || _region is null || !IsInstanceValid(_region)) return;
        var shown = modelIds.Where(regions.Has).Select(id => (regions.Outline(id), regions.Inner(id))).ToList();
        if (shown.Count > 0) _region.Show(shown, _regionColour);
    }

    static ModelBody BodyOf(Projection p, Guid modelId) { var m = p.Models[modelId]; return new ModelBody(m.Slot.Base, m.Slot.Height); }

    void OnDragged(Guid modelId, Point3 position)
    {
        if (_proposed is not null && _proposed.ContainsKey(modelId)) { _proposed[modelId] = position; _regate?.Invoke(); }
        if (_deployAt is not null && _deployAt.ContainsKey(modelId)) _deployAt[modelId] = position;
        if ((_proposed?.ContainsKey(modelId) == true || _deployAt?.ContainsKey(modelId) == true) && NodeYaw(modelId) is { } yaw) _turned[modelId] = yaw;
    }

    /// <summary>A reconnect drops the projection and the table's nodes: the move's or the deploy's ghosts are kept for the same Ask or unit.</summary>
    void KeepGhosts()
    {
        if (_renderedAsk is { } ask && _proposed is { } proposed) _stash.Keep(ask, proposed, _turned);
        else if (_deployUnit is { } unit && _deployAt is { } at) _stash.Keep(unit, at, _turned);
    }

    /// <summary>After the move's render for the Ask the ghosts were kept for: each back where it stood, facing as it was, and the commit gated again.</summary>
    void RestoreMove(Guid askId)
    {
        if (_stash.Take(askId) is not { } kept || _proposed is not { } proposed) return;
        foreach (var (id, ghost) in kept)
        {
            if (!proposed.ContainsKey(id) || _view.PieceAt(id) is not { } n || !IsInstanceValid(n)) continue;
            proposed[id] = ghost.Position;
            n.Position = WorldFrame.ToGodot(ghost.Position);
            if (ghost.Yaw is { } yaw) { n.Rotation = new Vector3(0, WorldFrame.Radians(yaw), 0); _turned[id] = yaw; }
        }
        _regate?.Invoke();
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
        _input.PlacementPoint = at => DropDeploy(unit, at);                        // its own slot: a menu point pick cannot take it
        if (_stash.Take(unit) is { } kept && _s.Projection is { } p)                // the placement a reconnect interrupted, for the models still on the shelf
        {
            var layout = kept.Where(kv => p.Models.GetValueOrDefault(kv.Key) is { Location: OnShelf } m && m.UnitId == unit)
                .Select(kv => new ModelRelocation(kv.Key, new OnShelf(), new OnTable(new Placement(kv.Value.Position, kv.Value.Yaw ?? new Yaw(0))))).ToList();
            if (layout.Count > 0) ShowDeploy(layout);
        }
    }

    void DropDeploy(Guid unit, Point3 at)
    {
        if (_deployUnit != unit || _s.Projection is not { } p) return;              // the deploy moved on before the click
        var models = p.Models.Values.Where(m => m.UnitId == unit && m.Location is OnShelf).OrderBy(m => m.Slot.Ordinal).ToList();
        var layout = DeployView.Layout(models, at, _view.Size);
        if (layout.Count == 0) return;                                              // nothing fits there: the pick stands for another click
        ShowDeploy(layout);
    }

    void ShowDeploy(IReadOnlyList<ModelRelocation> layout)
    {
        _input.PlacementPoint = null;
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
