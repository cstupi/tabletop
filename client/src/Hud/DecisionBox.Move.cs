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
    Dictionary<Guid, Point3>? _proposed, _deployAt; Guid? _deployUnit; KeepAway? _deployKeep;   // the pick's distance from enemy units (an Ambusher, a 3.6 Scout or Reinforcement copy)
    /// <summary>The facings the player gave the ghosts, read at each drop, and the ghosts kept across a reconnect.</summary>
    readonly Dictionary<Guid, Yaw> _turned = new(); readonly GhostStash _stash = new();
    // What AskAnswers.FacingOf needs from the disembark render: the exit distance, the board's volumes and its size.
    double _exitWithin; IReadOnlyList<WorldVolume> _volumes = []; TableSize _size = new(72, 48);
    /// <summary>The open move's per-model regions and the team colour they draw in; null outside a move and for a disembark.</summary>
    MoveRegions? _regions; Color _regionColour; Label? _measuring;
    /// <summary>The whole unit's area as vertex arrays, built on the pool from the Ask's opening (PrepareArea), so a plain drag's pickup draws it in
    /// its own frame with no work but handing them to Godot.</summary>
    volatile UnitMeshes? _unitMeshes;
    sealed record UnitMeshes(MoveRegions Regions, IReadOnlySet<Guid> Ids, MoveRegionNode.BandMeshes Meshes);
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
                : new CardHead("advance", "Disembark", unitName, $"Fully within {DecisionCard.Inches(within)} of {names.Unit(p.Models[containerId].UnitId)}"), MyOrdinal);
            _input.Allowance = null; _input.Proposed = null; _input.Region = null;
            _input.Pinned.Clear(); _input.Pinned.Add(containerId);                   // the transport (or the anchor) stays put: a drag over it takes a ghost or nothing
            // The ghosts start around the container, clear of it and within the exit reach (MoveRuler.ExitStart): grabbable, and a commit as they stand leaves it.
            void StartExit()
            {
                var layout = MoveRuler.ExitStart(p, legal, _view.Volumes, _view.Size);
                _view.ShowGhosts(layout);
                _proposed = layout.ToDictionary(r => r.ModelId, r => ((OnTable)r.To).Placement.Position);
                foreach (var r in layout) _turned[r.ModelId] = ((OnTable)r.To).Placement.Yaw;
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
            // The area is measured on the thread pool (a long charge among much terrain takes a while): the card says so until it draws.
            Add(_measuring = new Label { Text = "Measuring…", ThemeTypeVariation = Variation.Dim });
            ShowRegions([lead.Id]);
            PrepareArea(regions, legal.ModelIds, lead.Id);                          // every model's reach and the unit's area, before a pickup needs them
            // An Aircraft must move at least its minimum (p.13): the region is the ring beyond it, and a model short of it shows red. Where the table
            // has no room for it, the least is as far as the table allows in the direction the model goes (MoveRegions.Minimum).
            // The bar's mark is that least; the bar reads the farthest ghost's path over the allowance, or the cap once it holds.
            minimum = regions.Minimum(lead.Id);
            Add(DecisionTiles.Bar(out setBar));
            _input.Allowance = remaining; _input.Region = legal; _input.Reach = regions;
            // Every mesh a pickup shows (rulers, landing rings, the area's bands, the paths) made and drawn once under the slab, in the next frame:
            // the Ask's own frame carries the card, and a pickup comes later than that.
            var leadAt = ((OnTable)lead.Location).Placement.Position;
            IReadOnlyList<Base> bases = [.. legal.ModelIds.Where(id => p.Models.GetValueOrDefault(id)?.Location is OnTable).Select(id => p.Models[id].Slot.Base)];
            var askId = ask.Id; var colour = _regionColour;
            Screens.FrameBudget.Next(() => { if (IsInstanceValid(this) && _renderedAsk == askId) WarmAsk(bases, leadAt, colour); });
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
        var commit = Act(CardActions.Commit, Commit); Foot(commit);
        Button? stop = null;
        if (legal.Stoppable && !MoveRuler.IsDisembark(legal))
        {
            void Stop() => Relocate(true);
            _onStop = Stop;
            Foot(stop = Act(CardActions.Stop, Stop));
        }
        var starts = MovePaths.Starts(p, legal);
        var pathCache = _regions is null ? null : new MovePathCache(_regions, starts, legal, _view.Volumes);   // a ghost's path swept again only when it moves
        _regate = () =>
        {
            if (_proposed is not { } proposed || !IsInstanceValid(commit)) return;
            // p.11: once any model goes in or through difficult terrain, every model is held to the cap (MoveRegions.Allowance).
            var unit = allowance is null || _regions is null ? allowance : _regions.Allowance(proposed.ToDictionary(kv => kv.Key, kv => kv.Value.Horizontal));
            var readouts = unit is null ? [] : legal.ModelIds.Where(id => p.Models[id].Location is OnTable && proposed.ContainsKey(id))
                .Select(id => MoveRuler.Read(Moved(p, id, proposed[id].Horizontal, unit < allowance), unit, _regions?.Minimum(id, proposed[id].Horizontal))).ToList();
            var placed = proposed.ToDictionary(kv => kv.Key, kv => new Placement(kv.Value, NodeYaw(kv.Key) ?? new Yaw(0)));
            // p.7 unit coherency, as the server measures it (CoherencyView): a mark where the server refuses it, else a warning.
            var coherency = CoherencyView.Of(p, placed, _s.Coherency, id => _input.Violations.Subject?.Invoke(id) ?? names.Model(id), judged: true);
            _view.Coherency.Show(coherency, _regionColour);
            // A disembarking ghost the exit does not allow where it stands (p.15: beyond 6" of the transport) is marked as its contacts are.
            var contacts = ContactCheck.Words(ContactCheck.Move(p, legal, placed, _view.Volumes, _view.Size, unit, _regions), legal.Separation, names.Unit)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            foreach (var (id, words) in MoveRuler.ExitWords(p, legal, placed, _view.Volumes, _view.Size, c => names.Unit(p.Models[c].UnitId))) contacts.TryAdd(id, words);
            _input.Violations.Measure(legal.ModelIds, CoherencyView.With(KeepAwayArea.With(contacts, p, legal.KeepAway, placed, names.Unit), coherency));
            // Each moved model's path and footprints, and a warning where its swept base crosses dangerous ground: never holding the commit.
            var paths = pathCache?.Proposed(proposed.ToDictionary(kv => kv.Key, kv => kv.Value.Horizontal), unit < allowance,
                placed.ToDictionary(kv => kv.Key, kv => kv.Value.Yaw)) ?? [];
            _input.Violations.Warn(legal.ModelIds, CoherencyView.Warnings(MovePaths.Marks(paths), coherency));
            _view.Paths.Show(paths, _regionColour, id => MovePaths.Vehicle(p, id));
            var warnings = MovePaths.Lines(paths, id => _input.Violations.Subject?.Invoke(id) ?? names.Model(id), v => MovePaths.VolumeName(p.Board.Table, v))
                .Concat(coherency.Blocks ? [] : coherency.Words.Values).ToList();
            _gate = MoveRuler.Gate(readouts, _s.Game?.Enforcement ?? Enforcement.Strict, MarkedOf(legal.ModelIds), warnings);
            if (unit is { } cap) setBar?.Invoke(DecisionCard.Bar(readouts, cap, minimum));
            commit.Disabled = !_gate.Allowed; if (stop is not null) stop.Disabled = !_gate.Allowed;
            var why = _gate.Allowed ? "" : _gate.Reason ?? ""; commit.TooltipText = why; if (stop is not null) stop.TooltipText = why;
            reason.Text = _gate.Reason ?? ""; reason.Visible = _gate.Reason is not null && !RepeatsRefusal(_gate.Reason);
        };
        // The first gate (the readouts, the contacts, the bar, the commit's state) in the next frame: the Ask's own frame carries the card.
        var gated = ask.Id;
        Screens.FrameBudget.Next(() => { if (IsInstanceValid(this) && _renderedAsk == gated) _regate?.Invoke(); });
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

    /// <summary>These models' area as one (MoveRegions.Area), those the move holds; it stays on the last held models after the drop. Drawn when the
    /// thread pool has it and its vertex arrays (MoveRegions.AreaAsync, MoveRegionNode.MeshesOf): a long move among much terrain takes longer than a
    /// frame, and the table and the drag keep running meanwhile. A newer request, or an Ask that has gone, drops an older answer.</summary>
    async void ShowRegions(IReadOnlyList<Guid> modelIds)
    {
        if (_regions is not { } regions || _region is null || !IsInstanceValid(_region)) return;
        var request = ++_regionRequest; var colour = _regionColour;
        // The whole unit's, built from the Ask's opening: drawn in this frame. Else area and vertex arrays on the pool, one wait for the main
        // thread's turn at the end.
        if (_unitMeshes is { } unit && ReferenceEquals(unit.Regions, regions) && unit.Ids.SetEquals(modelIds.Where(regions.Has))) { _region.ShowBands(unit.Meshes, colour); Measured(); return; }
        var meshes = await Task.Run(async () => MoveRegionNode.MeshesOf(await regions.AreaAsync(modelIds).ConfigureAwait(false)));
        if (request != _regionRequest || !ReferenceEquals(regions, _regions) || _region is null || !IsInstanceValid(_region)) return;
        _region.ShowBands(meshes, colour);
        Measured();
    }

    void Measured() { if (_measuring is { } m && IsInstanceValid(m)) m.Visible = false; }
    int _regionRequest;

    /// <summary>MoveRegions.Prepare (the lead's area first, then every reach and the unit's area), then the unit's vertex arrays, all on the pool.</summary>
    void PrepareArea(MoveRegions regions, IReadOnlyList<Guid> ids, Guid lead)
    {
        _unitMeshes = null;
        var colour = _regionColour;
        _ = Task.Run(async () =>
        {
            await regions.Prepare(lead).ConfigureAwait(false);
            var unit = ids.Where(regions.Has).ToHashSet();
            var made = new UnitMeshes(regions, unit, MoveRegionNode.MeshesOf(regions.Area([.. unit])));
            _unitMeshes = made;
            // Uploaded into the area's hidden set on the main thread now, so the pickup only swaps it in.
            Callable.From(() => { if (_unitMeshes == made && _region is not null && IsInstanceValid(_region)) _region.Stage(made.Meshes, colour); }).CallDeferred();
        });
    }

    /// <summary>Behind the loading curtain: what a move Ask's opening makes and draws once (PrepareHold's rulers and landing rings, the area's
    /// bands, the paths) for my team's largest unit on the table, so the first move Ask neither creates those nodes nor compiles their
    /// pipelines. Each Ask still draws them once (WarmAsk).</summary>
    public void WarmMove()
    {
        if (_s.Projection is not { } p || _region is null || !IsInstanceValid(_region)) return;
        var models = p.Models.Values.Where(m => m.Location is OnTable).GroupBy(m => m.UnitId)
            .OrderByDescending(g => p.Units.TryGetValue(g.Key, out var u) && _s.Game?.Participants.FirstOrDefault(x => x.Id == u.ParticipantId)?.TeamId == _s.MyTeamId)
            .ThenByDescending(g => g.Count()).FirstOrDefault()?.ToList();
        if (models is null || models.Count == 0) return;
        var at = ((OnTable)models[0].Location).Placement.Position;
        WarmAsk([.. models.Select(m => m.Slot.Base)], at, Palette.Team(MyOrdinal));
    }

    /// <summary>The move's meshes drawn once under the slab at `at`, before any pickup.</summary>
    void WarmAsk(IReadOnlyList<Base> bases, Point3 at, Color colour)
    {
        _input.PrepareHold(bases, at);
        if (_region is not null && IsInstanceValid(_region)) _region.Warm(at, colour);
        _view.Paths.Warm(at, colour);
    }

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
        _deployUnit = mid.UnitId; _deployKeep = mid.KeepAway;
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
        _cardActions = CardActions.Placing;
        var place = Act(CardActions.Place, PlaceDeploy); Foot(place); Rekey();
        _regate = () =>
        {
            if (_deployAt is not { } at || _s.Projection is not { } p || _s.Game is not { } game || !IsInstanceValid(place)) return;
            var placed = at.ToDictionary(kv => kv.Key, kv => new Placement(kv.Value, NodeYaw(kv.Key) ?? new Yaw(0)));
            var unitName = new Names(game, p).Unit;
            _input.Violations.Measure(at.Keys, KeepAwayArea.With(ContactCheck.Words(ContactCheck.Placement(p, placed, _view.Volumes), null, unitName), p, _deployKeep, placed, unitName));
            // p.7 unit coherency of the ghosts: the server does not judge a deployment's, so it warns and never holds Place.
            var coherency = CoherencyView.Of(p, placed, _s.Coherency, id => _input.Violations.Subject?.Invoke(id) ?? p.Models[id].Slot.Name, judged: false);
            _view.Coherency.Show(coherency, Palette.Team(MyOrdinal));
            _input.Violations.Warn(at.Keys, coherency.Words);
            _gate = MoveRuler.Gate([], game.Enforcement, MarkedOf(at.Keys), coherency.Words.Values.ToList());
            place.Disabled = !_gate.Allowed; place.TooltipText = _gate.Allowed ? "" : _gate.Reason ?? "";
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
