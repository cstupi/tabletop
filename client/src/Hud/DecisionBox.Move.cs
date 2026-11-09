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
    // What AskAnswers.FacingOf needs from the disembark render: the exit distance, the board's volumes and its size.
    double _exitWithin; IReadOnlyList<WorldVolume> _volumes = []; TableSize _size = new(72, 48);
    /// <summary>The open move's per-model regions and the team colour they draw in; null outside a move and for a disembark.</summary>
    MoveRegions? _regions; Color _regionColour;
    /// <summary>The move's commit against where its ghosts stand, read again on every drag and reset.</summary>
    Action? _regate; MoveGate _gate = new(true, null);
    /// <summary>The last commit, until the server answers it; a refusal turns it into the stash and the verdict the re-render puts back.</summary>
    readonly CommitHold _sent = new(); (IReadOnlyList<Guid> Models, string Reason)? _verdict;

    void RenderMove(Ask ask, MoveRegion legal, Projection p, Names names)
    {
        if (legal.ModelIds.Count == 0) return;                                      // nothing to move; the server closes the Ask
        var lead = p.Models[legal.ModelIds[0]];
        double? allowance = null, minimum = null; Action<AllowanceBar>? setBar = null;
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
            var unitName = names.Unit(lead.UnitId);
            Head(fromShelf ? new CardHead("army", "Place", unitName, ask.Prompt)        // the Ask's prompt says what is placed
                : new CardHead("advance", "Disembark", unitName, $"Fully within {DecisionCard.Inches(within)} of {names.Piece(containerId)}"), MyOrdinal);
            _input.Allowance = null; _input.Proposed = null; _input.Region = null;
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
            _onCancel = () => { StartExit(); _input.Violations.Clear(); _regate?.Invoke(); return true; };
        }
        else
        {
            if (lead.Location is not OnTable) { Unhook(); return; }
            // The Ask's Allowance is already what this segment has left (GfMoves reduces it by the distance a cast-split move used): never subtract `used` again.
            var remaining = Math.Max(0, legal.Allowance); allowance = remaining;
            // Each model's own region from where it stands: the lead's shows first, then each pickup's held models'.
            var regions = MoveRegions.Of(p, legal with { Allowance = remaining }, _view.Volumes, _view.Size);
            _regions = regions; _regionColour = Palette.Team(ordinal);
            ShowRegions([lead.Id]);
            _ = regions.Prepare();                                                  // every model's reach, before a pickup clamps them on this thread
            // An Aircraft must move at least its minimum (p.13): the region is the ring beyond it, and a model short of it shows red. Where the table
            // has no room for it, the least is as far as the table allows in the direction the model goes (MoveRegions.Minimum).
            // The bar's mark is that least; the bar reads the farthest ghost's path over the allowance, or the cap once it holds.
            minimum = regions.Minimum(lead.Id);
            Add(DecisionTiles.Bar(out setBar));
            _input.Allowance = remaining; _input.Region = legal; _input.Reach = regions;
            // Drawn and committed agree: a rebuilt Ask (a refusal) or an Esc starts from the server's positions.
            void StartMove() { _proposed = legal.ModelIds.ToDictionary(id => id, id => ((OnTable)p.Models[id].Location).Placement.Position); _input.Proposed = _proposed; _view.Snap(legal.ModelIds); }
            StartMove();
            _onCancel = () => { StartMove(); _input.Violations.Clear(); _regate?.Invoke(); return true; };
            OfferEmbark(ask, legal, p, names);
        }
        // A stop (p.13 Caster(X) "at any point before attacking") sends the ghosts as they stand: with no drag, that is a stop before moving.
        void Relocate(bool continues)
        {
            if (_proposed is not { } proposed || !_gate.Allowed) return;
            var ghostYaws = legal.ModelIds.Select(id => (id, yaw: NodeYaw(id))).Where(x => x.yaw is not null).ToDictionary(x => x.id, x => x.yaw!.Value);
            var answer = AskAnswers.Move(p, legal, proposed, ghostYaws, continues, MoveRuler.IsDisembark(legal) ? new ExitSite(_exitWithin, _volumes, _size) : null);
            _sent.Remember(ask.Id, answer.Moves);
            Send(ask, answer);
        }
        void Commit() => Relocate(false);
        _onEnter = Commit;
        // Any ghost out of range is counted and each contact named, only while it holds; under strict the commit and the stop wait until none is.
        var reason = new Label { ThemeTypeVariation = Variation.Warning, AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false }; Add(reason);
        var commit = Act("Commit", DecisionCard.Enter, Commit, primary: true, icon: "ready"); Foot(commit);
        Button? stop = null;
        if (legal.Stoppable && !MoveRuler.IsDisembark(legal))
        {
            void Stop() => Relocate(true);
            _onStop = Stop;
            var word = DecisionView.StopLabel(p, legal)!;
            Foot(stop = Act(word, DecisionCard.Stop, Stop, icon: word.StartsWith("Cast", StringComparison.Ordinal) ? "cast" : "hold"));
        }
        _regate = () =>
        {
            if (_proposed is not { } proposed || !IsInstanceValid(commit)) return;
            // p.11: once any model goes in or through difficult terrain, every model is held to the cap (MoveRegions.Allowance).
            var unit = allowance is null || _regions is null ? allowance : _regions.Allowance(proposed.ToDictionary(kv => kv.Key, kv => kv.Value.Horizontal));
            var readouts = unit is null ? [] : legal.ModelIds.Where(id => p.Models[id].Location is OnTable && proposed.ContainsKey(id))
                .Select(id => MoveRuler.Read(Moved(p, id, proposed[id].Horizontal, unit < allowance), unit, _regions?.Minimum(id, proposed[id].Horizontal))).ToList();
            var placed = proposed.ToDictionary(kv => kv.Key, kv => new Placement(kv.Value, NodeYaw(kv.Key) ?? new Yaw(0)));
            _input.Violations.Measure(legal.ModelIds, ContactCheck.Words(ContactCheck.Move(p, legal, placed, _view.Volumes, _view.Size, unit), legal.Separation, names.Unit));
            _gate = MoveRuler.Gate(readouts, _s.Game?.Enforcement ?? Enforcement.Strict, MarkedOf(legal.ModelIds));
            if (unit is { } cap) setBar?.Invoke(DecisionCard.Bar(readouts, cap, minimum));
            commit.Disabled = !_gate.Allowed; if (stop is not null) stop.Disabled = !_gate.Allowed;
            reason.Text = _gate.Reason ?? ""; reason.Visible = _gate.Reason is not null && !RepeatsRefusal(_gate.Reason);
        };
        _regate();
    }

    /// <summary>p.15: "only one model needs to reach the Transport for everyone to embark". A click on a listed container answers the
    /// whole unit into it: one relocation per model to the same Inside. The player may still drag and commit instead.</summary>
    void OfferEmbark(Ask ask, MoveRegion legal, Projection p, Names names)
    {
        IReadOnlyList<Guid> enterable = legal.Containment?.Enterable ?? [];
        if (enterable.Count == 0) return;
        _note.Text = $"Click to embark: {string.Join(", ", enterable.Select(names.Piece))}";
        _input.Eligible = enterable.ToHashSet();
        _onTarget = clicked =>
        {
            if (AskAnswers.Resolve(p, clicked, enterable) is not { } container || !p.Models.ContainsKey(container)) return;
            _input.CancelDrag();
            Send(ask, AskAnswers.Embark(p, legal, container));
        };
    }

    Yaw? NodeYaw(Guid modelId) => _view.PieceAt(modelId) is { } n && IsInstanceValid(n) ? WorldFrame.YawFrom(n.Rotation.Y) : null;

    /// <summary>Each of these models' own region, those the move holds; the outline stays on the last held models after the drop. Drawn when the
    /// thread pool has them (MoveRegions.OutlinesAsync): a long move among much terrain takes longer than a frame, and the table and the drag keep
    /// running meanwhile. A newer request, or an Ask that has gone, drops an older answer.</summary>
    async void ShowRegions(IReadOnlyList<Guid> modelIds)
    {
        if (_regions is not { } regions || _region is null || !IsInstanceValid(_region)) return;
        var request = ++_regionRequest; var colour = _regionColour;
        var shown = await regions.OutlinesAsync(modelIds);
        if (request != _regionRequest || !ReferenceEquals(regions, _regions) || _region is null || !IsInstanceValid(_region)) return;
        if (shown.Count > 0) _region.ShowTriangles(shown, colour);
    }
    int _regionRequest;

    /// <summary>How far a ghost has moved: along its path round what it may not pass (MoveRegions.Route), else in a straight line.</summary>
    double Moved(Projection p, Guid id, Point2 to, bool capped)
    {
        var from = ((OnTable)p.Models[id].Location).Placement.Position.Horizontal;
        if (from.DistanceTo(to) <= 1e-9) return 0;   // not moved: no path to find, and the reach is not built on the main thread when the Ask opens
        return _regions?.Route(id, to, capped)?.Distance ?? from.DistanceTo(to);
    }

    static ModelBody BodyOf(Projection p, Guid modelId) { var m = p.Models[modelId]; return new ModelBody(m.Slot.Base, m.Slot.Height); }

    /// <summary>These models' marks (Violations), for the gate.</summary>
    IReadOnlyDictionary<Guid, string> MarkedOf(IEnumerable<Guid> ids) =>
        ids.Where(_input.Violations.Marked.ContainsKey).ToDictionary(id => id, id => _input.Violations.Marked[id]);

    /// <summary>The server refused a commit: its ghosts are kept for the re-render, which puts them back and marks them (ApplyVerdict).</summary>
    void KeepRefused(Refused r)
    {
        if (_sent.Refused(r.MessageId) is not { } held) return;
        _stash.Keep(held.Key, held.At, held.Yaws);
        _verdict = (held.Moved, r.Detail);
    }

    /// <summary>After the ghosts are back where the refused commit put them: the models it moved marked with the server's reason, the gate read again.</summary>
    void ApplyVerdict()
    {
        if (_verdict is not { } v) return;
        _verdict = null;
        _input.Violations.Refused(v.Models, v.Reason);
        _regate?.Invoke();
    }

    /// <summary>The gate's line says what the server's refusal over it says: the refusal alone shows it.</summary>
    bool RepeatsRefusal(string reason) => _refusalDetail is { Length: > 0 } d && _refusal == $"The server refused: {d}" && reason.Contains(d);

    /// <summary>A drag frame's held models: every ghost takes its place, then the commit is gated once.</summary>
    void OnDragged(IReadOnlyList<(Guid Id, Point3 At)> moved)
    {
        var changed = false;
        foreach (var (modelId, position) in moved)
        {
            if (_refusal is not null && (_proposed?.ContainsKey(modelId) == true || _deployAt?.ContainsKey(modelId) == true))
            { _refusal = null; _refusalLabel.Visible = false; }                    // the player changed the proposal: the refusal is spent
            if (_proposed is not null && _proposed.ContainsKey(modelId)) { _proposed[modelId] = position; changed = true; }
            if (_deployAt is not null && _deployAt.ContainsKey(modelId)) { _deployAt[modelId] = position; changed = true; }
            if ((_proposed?.ContainsKey(modelId) == true || _deployAt?.ContainsKey(modelId) == true) && NodeYaw(modelId) is { } yaw) _turned[modelId] = yaw;
        }
        if (changed) _regate?.Invoke();
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
        Head(new CardHead("army", "Place", names.Unit(mid.UnitId), "Click the table where the unit goes"), MyOrdinal);
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
        if (_head.Head is { } head) Head(head with { Prompt = null }, _head.Ordinal);   // the ghosts are down: the card is the Place button
        _onEnter = PlaceDeploy;
        _onCancel = () => { Reset(); Render(); return true; };                    // Reset drops _deployUnit, so Render arms a fresh click
        // A ghost on another model's base is named, only while it holds; under strict Place waits until none is.
        var reason = new Label { ThemeTypeVariation = Variation.Warning, AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false }; Add(reason);
        var place = Act("Place", DecisionCard.Enter, PlaceDeploy, primary: true, icon: "ready"); Foot(place);
        _regate = () =>
        {
            if (_deployAt is not { } at || _s.Projection is not { } p || _s.Game is not { } game || !IsInstanceValid(place)) return;
            var placed = at.ToDictionary(kv => kv.Key, kv => new Placement(kv.Value, NodeYaw(kv.Key) ?? new Yaw(0)));
            _input.Violations.Measure(at.Keys, ContactCheck.Words(ContactCheck.Placement(p, placed, _view.Volumes), null, new Names(game, p).Unit));
            _gate = MoveRuler.Gate([], game.Enforcement, MarkedOf(at.Keys));
            place.Disabled = !_gate.Allowed;
            reason.Text = _gate.Reason ?? ""; reason.Visible = _gate.Reason is not null && !RepeatsRefusal(_gate.Reason);
        };
        _regate();
        ApplyVerdict();
        Render();
    }

    void PlaceDeploy()
    {
        if (_deployAt is null || _s.Projection is not { } p || _deployUnit is not { } unit || !_gate.Allowed) return;
        var moves = DeployView.Place(p, _deployAt.Select(kv => (kv.Key, new Placement(kv.Value, NodeYaw(kv.Key) ?? new Yaw(0)))).ToList());
        _sent.Remember(unit, moves.Moves);
        _onEnter = null; _onCancel = null; _regate = null; SetButtons(true);   // a ghost dragged while the answer travels gates nothing: Place stays off
        var id = Guid.CreateVersion7(); _lastCommand = id;                          // a Refused for it lands in _onRefused, which rebuilds the placement
        _sent.Sent(id); _flight.Sent(unit, id);
        Guarded.Run(() => _s.SendCommandAsync(moves, id), message => { _flight.Refused(id); _refusal = $"Could not place the models: {message}"; _refusalAsk = null; Reset(); Render(); });
    }
}
