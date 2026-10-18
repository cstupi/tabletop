using Godot;
using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Projection = Tabletop.Protocol.Projection;

namespace Tabletop.Client.Table;

/// <summary>One scene for setup and play. Draws from the draft or the projection; never holds truth of its own.</summary>
public partial class TableView : Node3D
{
    Session _session = null!; MeshLoader _meshes = null!;
    readonly SlabNode _slab = new(); readonly Node3D _pieces = new(), _terrain = new(), _markers = new(), _zones = new(), _ghosts = new();
    public OrbitCamera Camera { get; } = new();
    public TableSize Size { get; private set; } = new(72, 48);
    public IReadOnlyList<WorldVolume> Volumes { get; private set; } = [];
    /// <summary>Ids whose nodes Redraw leaves where they are: a drop the server has not answered, a drag in hand.</summary>
    public Func<Guid, bool> IsHeld { get; set; } = _ => false;

    public override void _Ready()
    {
        foreach (var n in new Node[] { _slab, _pieces, _terrain, _markers, _zones, _ghosts, Camera }) AddChild(n);
        // A warm key light that casts the shadows, a cool unshadowed fill from the opposite side, and a flat ambient so the
        // shadowed sides keep their colour; filmic tone mapping keeps the lit tops from clipping. The background stays the
        // theme's clear colour.
        AddChild(new DirectionalLight3D { Rotation = new Vector3(Mathf.DegToRad(-50), Mathf.DegToRad(30), 0), ShadowEnabled = true, LightColor = new Color(1f, 0.96f, 0.9f), LightEnergy = 1.1f });
        AddChild(new DirectionalLight3D { Rotation = new Vector3(Mathf.DegToRad(-35), Mathf.DegToRad(210), 0), LightColor = new Color(0.7f, 0.78f, 0.95f), LightEnergy = 0.25f });
        AddChild(new WorldEnvironment { Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.ClearColor, AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.62f, 0.66f, 0.74f), AmbientLightEnergy = 0.45f, TonemapMode = Godot.Environment.ToneMapper.Filmic, TonemapExposure = 1.05f,
        } });
        Camera.Current = true;
    }

    Action? _onBoard, _onGame, _onBuiltins; Action<IReadOnlyList<GameEvent>>? _onEvents;
    GameStatus? _status;

    public override void _ExitTree()
    {
        if (_session is null) return;
        _meshes.Dispose();
        _session.BoardChanged -= _onBoard; _session.EventsApplied -= _onEvents; _session.GameChanged -= _onBoard; _session.GameChanged -= _onGame;
        BuiltinContent.Changed -= _onBuiltins;
    }

    /// <summary>A new built-in catalog: the slab's texture and every built-in piece's mesh are drawn again from it.</summary>
    void RedrawBuiltins()
    {
        if (Table is { } table) _slab.Set(Size, table.Texture);
        foreach (var n in _terrain.GetChildren().OfType<TerrainNode>()) if (n.Drawn.Mesh.IsBuiltin) LoadTerrainMesh(n, n.Drawn);
    }

    public void Bind(Session session)
    {
        if (_session is not null) throw new InvalidOperationException("TableView is already bound.");
        _session = session;
        _meshes = new MeshLoader(Main.Instance.Cache, session.Management, session.GameId!.Value);
        _meshes.RefreshManifest();
        _status = session.Game?.Status;
        _onBoard = Redraw; _onEvents = _ => Redraw();
        // The manifest lists the game's assets by who has picked what; it is fetched again when play starts, when every army is in.
        _onGame = () => { var status = _session.Game?.Status; if (status == GameStatus.Playing && _status != GameStatus.Playing) _meshes.RefreshManifest(); _status = status; };
        session.BoardChanged += _onBoard; session.EventsApplied += _onEvents; session.GameChanged += _onBoard; session.GameChanged += _onGame;
        _onBuiltins = RedrawBuiltins; BuiltinContent.Changed += _onBuiltins;
        Redraw(); Camera.FocusOn(Size);
    }

    TableState? Table => _session.Projection?.Board.Table ?? _session.Draft;

    public void Redraw()
    {
        var table = Table;
        if (table is null) { Clear(); return; }
        if (table.Size != Size || _slab.GetChildCount() == 0) { Size = table.Size; _slab.Set(Size, table.Texture); }
        Volumes = table.Terrain.SelectMany(t => t.WorldVolumes()).ToList();
        Sync(_terrain, table.Terrain, t => t.Id, BoardDiff.SameShape,
            t => { var n = new TerrainNode { PlacementId = t.Id }; n.Build(t); LoadTerrainMesh(n, t); return n; }, (n, t) => n.Place(t));
        // A play marker wins over a setup marker of the same id: one disc per id.
        var played = _session.Projection?.Markers.Values.ToList() ?? [];
        var markers = table.Markers.Where(m => played.All(q => q.Id != m.Id)).Concat(played);
        Color? ControlColour(Marker m) => _session.Projection?.Control.TryGetValue(m.Id, out var holder) == true
            && _session.Game?.Teams.FirstOrDefault(t => t.Id == holder) is { } team ? Palette.Team(team.Ordinal) : null;
        Sync(_markers, markers.Select(m => (Marker: m, Team: ControlColour(m))).ToList(), x => x.Marker.Id, (a, b) => BoardDiff.SameShape(a.Marker, b.Marker) && a.Team == b.Team,
            x => { var n = new MarkerNode { MarkerId = x.Marker.Id }; n.Build(x.Marker, x.Team); return n; }, (n, x) => n.Place(x.Marker));
        var teams = _session.Game?.Teams ?? [];
        Sync(_zones, ZonesShown(table, _session.Projection, teams).Select(z => (Zone: z, Size, Colour: ZoneColour(z, teams))).ToList(), x => x.Zone.Id, (a, b) => a == b,
            x => { var n = new ZoneNode(); n.Build(x.Zone, x.Size, x.Colour); return n; }, (_, _) => { });
        var onTable = _session.Projection?.Models.Values.Where(m => m.Location is OnTable).ToList() ?? [];
        // Each model's proxy takes its team's tint, recoloured on every redraw so it follows ownership as GameInfo arrives.
        Color Tint(ModelInPlay m) => Palette.Tinted(_session.Projection is { } p && _session.Game is { } g ? SheetFocus.TeamOf(p, g, m.UnitId)?.Ordinal : null);
        Sync(_pieces, onTable, m => m.Id, (_, _) => true,
            m => { var n = new PieceNode { ModelId = m.Id, UnitId = m.UnitId }; n.SetBody(m.Slot.Base, m.Slot.Height); n.SetTint(Tint(m)); Place(n, m); LoadPieceMesh(n, m); return n; },
            (n, m) => { n.SetTint(Tint(m)); Place(n, m); });
    }

    /// <summary>Put the named models' nodes back on the projection's placements, held or not: a move Ask that starts again from the server's positions.</summary>
    public void Snap(IEnumerable<Guid> modelIds)
    {
        foreach (var id in modelIds)
            if (PieceAt(id) is { } node && _session.Projection?.Models.GetValueOrDefault(id) is { Location: OnTable } m) Place(node, m);
    }

    /// <summary>
    /// Setup (no projection): every ordinal, grey without a team. Play: each team's zone through round 0 — deployment, then the
    /// phases before the first round — and none once round 1 has started.
    /// </summary>
    static IReadOnlyList<DeploymentZone> ZonesShown(TableState table, Projection? p, IReadOnlyList<Team> teams) =>
        p is null ? table.Zones
        : p.Scheduler.Round >= 1 ? []
        : table.Zones.Where(z => teams.Any(t => t.Ordinal == z.TeamOrdinal)).ToList();

    static Color ZoneColour(DeploymentZone z, IReadOnlyList<Team> teams) => teams.Any(t => t.Ordinal == z.TeamOrdinal) ? Palette.Team(z.TeamOrdinal) with { A = 0.35f } : Palette.Unassigned;

    static void Place(PieceNode n, ModelInPlay m)
    {
        var p = ((OnTable)m.Location).Placement;
        n.Position = WorldFrame.ToGodot(p.Position); n.Rotation = new Vector3(0, WorldFrame.Radians(p.Yaw), 0);
    }

    /// <summary>
    /// One layer against what its nodes last drew (<see cref="BoardDiff"/>): a removed id's node goes, a new id gets a node, a
    /// reshaped one is replaced, and a kept one only takes its transform, which also snaps back a drag the server did not take.
    /// A held id is left as it is, and keeps what it last drew until it is let go.
    /// </summary>
    void Sync<TNode, TItem>(Node parent, IReadOnlyList<TItem> items, Func<TItem, Guid> id, Func<TItem, TItem, bool> sameShape, Func<TItem, TNode> create, Action<TNode, TItem> place)
        where TNode : Node3D, IDrawn<TItem>
    {
        var nodes = parent.GetChildren().OfType<TNode>().ToDictionary(n => n.Id);
        var diff = BoardDiff.Of(nodes.ToDictionary(kv => kv.Key, kv => kv.Value.Drawn), items, id, sameShape);
        foreach (var key in diff.Removed) Drop(nodes[key]);
        foreach (var item in diff.Reshaped) if (!IsHeld(id(item))) { Drop(nodes[id(item)]); Add(item); }
        foreach (var item in diff.Kept) if (!IsHeld(id(item))) { var n = nodes[id(item)]; n.Drawn = item; place(n, item); }
        foreach (var item in diff.Added) Add(item);
        void Add(TItem item) { var n = create(item); n.Drawn = item; parent.AddChild(n); }
    }

    /// <summary>Out of the tree now, so a lookup by id this frame finds only the live node; freed at the end of the frame.</summary>
    static void Drop(Node n) { n.GetParent().RemoveChild(n); n.QueueFree(); }

    async void LoadPieceMesh(PieceNode n, ModelInPlay m)
    {
        if (m.Slot.AssetId is not { } asset) return;
        var scene = await _meshes.LoadAssetAsync(asset, Camera.GlobalPosition, WorldFrame.ToGodot(((OnTable)m.Location).Placement.Position));
        if (!IsInstanceValid(n)) { scene?.QueueFree(); return; }
        n.SetMesh(scene);
    }
    // A built-in's group mesh is fitted to the placement's first volume; an asset's mesh carries its own scale.
    async void LoadTerrainMesh(TerrainNode n, TerrainPlacement t)
    {
        var scene = t.Mesh.IsBuiltin ? await _meshes.LoadBuiltinAsync(t.Mesh.Builtin!, t.Volumes.Count > 0 ? t.Volumes[0].Volume : null) : await _meshes.LoadAssetAsync(t.Mesh.AssetId!.Value, Camera.GlobalPosition, WorldFrame.ToGodot(t.Position));
        if (!IsInstanceValid(n)) { scene?.QueueFree(); return; }
        n.SetMesh(scene);
    }

    /// <summary>
    /// A disembark's draggable stand-ins. An occupant has no placement and therefore no piece node (game flow → Inside a
    /// container), so the decision box asks for one node per model, stacked on the container's own placement, in the ghost
    /// colour. They live in their own child, which <see cref="Redraw"/> never diffs, so a board event arriving mid-answer
    /// cannot delete them and their dragged positions survive until the player commits.
    /// </summary>
    public void ShowExitGhosts(IReadOnlyList<Guid> modelIds, Guid containerId)
    {
        ClearExitGhosts();
        if (_session.Projection is not { } p || p.Models.GetValueOrDefault(containerId)?.Location is not OnTable at) return;
        foreach (var id in modelIds)
        {
            if (p.Models.GetValueOrDefault(id) is not { } m) continue;
            var n = new PieceNode { ModelId = id, UnitId = m.UnitId };
            n.SetBody(m.Slot.Base, m.Slot.Height);
            n.Position = WorldFrame.ToGodot(at.Placement.Position);
            n.Rotation = new Vector3(0, WorldFrame.Radians(at.Placement.Yaw), 0);
            n.SetHighlight(Palette.Ghost);
            _ghosts.AddChild(n);
        }
    }

    /// <summary>
    /// The deployment placement's stand-ins (in-game UX spec → Deployment placement): one node per relocation at its OnTable
    /// placement, in the ghost colour, for models still on the shelf. Same child as the exit ghosts, so Redraw never diffs
    /// them and ClearExitGhosts clears them.
    /// </summary>
    public void ShowGhosts(IReadOnlyList<ModelRelocation> placements)
    {
        ClearExitGhosts();
        if (_session.Projection is not { } p) return;
        foreach (var r in placements)
        {
            if (r.To is not OnTable to || p.Models.GetValueOrDefault(r.ModelId) is not { } m) continue;
            var n = new PieceNode { ModelId = r.ModelId, UnitId = m.UnitId };
            n.SetBody(m.Slot.Base, m.Slot.Height);
            n.Position = WorldFrame.ToGodot(to.Placement.Position);
            n.Rotation = new Vector3(0, WorldFrame.Radians(to.Placement.Yaw), 0);
            n.SetHighlight(Palette.Ghost);
            _ghosts.AddChild(n);
        }
    }

    public void ClearExitGhosts() { foreach (var c in _ghosts.GetChildren()) c.QueueFree(); }
    public bool IsExitGhost(Guid modelId) => _ghosts.GetChildren().OfType<PieceNode>().Any(p => p.ModelId == modelId);

    /// <summary>The projection's node first, then a disembark ghost — an occupant has only the ghost, and never both at once.</summary>
    public PieceNode? PieceAt(Guid modelId) => _pieces.GetChildren().OfType<PieceNode>().FirstOrDefault(p => p.ModelId == modelId)
        ?? _ghosts.GetChildren().OfType<PieceNode>().FirstOrDefault(p => p.ModelId == modelId);
    public TerrainNode? TerrainAt(Guid id) => _terrain.GetChildren().OfType<TerrainNode>().FirstOrDefault(t => t.PlacementId == id);
    public MarkerNode? MarkerAt(Guid id) => _markers.GetChildren().OfType<MarkerNode>().FirstOrDefault(m => m.MarkerId == id);
    public IEnumerable<Node3D> Pieces => _pieces.GetChildren().OfType<Node3D>().Concat(_ghosts.GetChildren().OfType<Node3D>())
        .Concat(_terrain.GetChildren().OfType<Node3D>()).Concat(_markers.GetChildren().OfType<Node3D>());

    void Clear() { foreach (var parent in new[] { _pieces, _terrain, _markers, _zones, _ghosts }) foreach (var c in parent.GetChildren()) Drop(c); }
}

/// <summary>A table node by its board id, with the item it was last built or placed from.</summary>
public interface IDrawn<TItem> { Guid Id { get; } TItem Drawn { get; set; } }
