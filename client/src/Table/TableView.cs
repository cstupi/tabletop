using Godot;
using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Table;

/// <summary>One scene for setup and play. Draws from the draft or the projection; never holds truth of its own.</summary>
public partial class TableView : Node3D
{
    Session _session = null!; MeshLoader _meshes = null!;
    readonly SlabNode _slab = new(); readonly Node3D _pieces = new(), _terrain = new(), _markers = new(), _zones = new(), _ghosts = new();
    public OrbitCamera Camera { get; } = new();
    public TableSize Size { get; private set; } = new(72, 48);
    public IReadOnlyList<WorldVolume> Volumes { get; private set; } = [];

    public override void _Ready()
    {
        foreach (var n in new Node[] { _slab, _pieces, _terrain, _markers, _zones, _ghosts, Camera }) AddChild(n);
        AddChild(new DirectionalLight3D { Rotation = new Vector3(Mathf.DegToRad(-50), Mathf.DegToRad(30), 0), ShadowEnabled = true });
        Camera.Current = true;
    }

    Action? _onBoard; Action<IReadOnlyList<GameEvent>>? _onEvents;

    public override void _ExitTree()
    {
        if (_session is null) return;
        _session.BoardChanged -= _onBoard; _session.EventsApplied -= _onEvents; _session.GameChanged -= _onBoard;
    }

    public void Bind(Session session)
    {
        if (_session is not null) throw new InvalidOperationException("TableView is already bound.");
        _session = session;
        _meshes = new MeshLoader(Main.Instance.Cache, session.Management, session.GameId!.Value);
        _meshes.RefreshManifest();
        _onBoard = Redraw; _onEvents = _ => Redraw();
        session.BoardChanged += _onBoard; session.EventsApplied += _onEvents; session.GameChanged += _onBoard;
        Redraw(); Camera.FocusOn(Size);
    }

    TableState? Table => _session.Projection?.Board.Table ?? _session.Draft;

    public void Redraw()
    {
        var table = Table;
        if (table is null) { Clear(); return; }
        if (table.Size != Size || _slab.GetChildCount() == 0) { Size = table.Size; _slab.Set(Size, table.Texture); }
        Volumes = table.Terrain.SelectMany(t => t.WorldVolumes()).ToList();
        Sync(_terrain, table.Terrain, t => t.Id, t => { var n = new TerrainNode { PlacementId = t.Id }; n.Set(t); LoadTerrainMesh(n, t); return n; }, (TerrainNode n, TerrainPlacement t) => n.Set(t));
        var markers = table.Markers.Concat(_session.Projection?.Markers.Values ?? []).ToList();
        Sync(_markers, markers, m => m.Id, m => { var n = new MarkerNode { MarkerId = m.Id }; n.Set(m); return n; }, (MarkerNode n, Marker m) => n.Set(m));
        var teams = _session.Game?.Teams ?? [];
        Sync(_zones, table.Zones, z => z.Id, z => { var n = new ZoneNode(); n.Set(z, Size, ZoneColour(z, teams)); return n; }, (ZoneNode n, DeploymentZone z) => n.Set(z, Size, ZoneColour(z, teams)));
        var onTable = _session.Projection?.Models.Values.Where(m => m.Location is OnTable).ToList() ?? [];
        Sync(_pieces, onTable, m => m.Id, m => { var n = new PieceNode { ModelId = m.Id, UnitId = m.UnitId }; n.SetBody(m.Slot.Base, m.Slot.Height); Place(n, m); LoadPieceMesh(n, m); return n; }, (PieceNode n, ModelInPlay m) => Place(n, m));
    }

    static Color ZoneColour(DeploymentZone z, IReadOnlyList<Team> teams) => teams.Any(t => t.Ordinal == z.TeamOrdinal) ? Palette.Team(z.TeamOrdinal) with { A = 0.35f } : Palette.Unassigned;

    static void Place(PieceNode n, ModelInPlay m)
    {
        var p = ((OnTable)m.Location).Placement;
        n.Position = WorldFrame.ToGodot(p.Position); n.Rotation = new Vector3(0, WorldFrame.Radians(p.Yaw), 0);
    }

    void Sync<TNode, TItem>(Node parent, IReadOnlyList<TItem> items, Func<TItem, Guid> id, Func<TItem, TNode> create, Action<TNode, TItem> update) where TNode : Node3D, IHasId
    {
        var existing = parent.GetChildren().OfType<TNode>().Where(n => !n.IsQueuedForDeletion()).ToDictionary(n => n.Id);
        var wanted = items.ToDictionary(id);
        foreach (var (key, node) in existing) if (!wanted.ContainsKey(key)) node.QueueFree();
        foreach (var (key, item) in wanted) { if (existing.TryGetValue(key, out var node)) update(node, item); else parent.AddChild(create(item)); }
    }

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
        var scene = t.Mesh.IsBuiltin ? _meshes.LoadBuiltin(t.Mesh.Builtin!, t.Volumes.Count > 0 ? t.Volumes[0].Volume : null) : await _meshes.LoadAssetAsync(t.Mesh.AssetId!.Value, Camera.GlobalPosition, WorldFrame.ToGodot(t.Position));
        if (!IsInstanceValid(n)) { scene?.QueueFree(); return; }
        n.SetMesh(scene);
    }

    /// <summary>
    /// A disembark's draggable stand-ins. An occupant has no placement and therefore no piece node (game flow → Inside a
    /// container), so the Ask panel asks for one node per model, stacked on the container's own placement, in the ghost
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

    public void ClearExitGhosts() { foreach (var c in _ghosts.GetChildren()) c.QueueFree(); }
    public bool IsExitGhost(Guid modelId) => _ghosts.GetChildren().OfType<PieceNode>().Any(p => p.ModelId == modelId);

    /// <summary>The projection's node first, then a disembark ghost — an occupant has only the ghost, and never both at once.</summary>
    public PieceNode? PieceAt(Guid modelId) => _pieces.GetChildren().OfType<PieceNode>().FirstOrDefault(p => p.ModelId == modelId)
        ?? _ghosts.GetChildren().OfType<PieceNode>().FirstOrDefault(p => p.ModelId == modelId);
    public TerrainNode? TerrainAt(Guid id) => _terrain.GetChildren().OfType<TerrainNode>().FirstOrDefault(t => t.PlacementId == id);
    public MarkerNode? MarkerAt(Guid id) => _markers.GetChildren().OfType<MarkerNode>().FirstOrDefault(m => m.MarkerId == id);
    public IEnumerable<Node3D> Pieces => _pieces.GetChildren().OfType<Node3D>().Concat(_ghosts.GetChildren().OfType<Node3D>())
        .Concat(_terrain.GetChildren().OfType<Node3D>()).Concat(_markers.GetChildren().OfType<Node3D>());

    void Clear() { foreach (var parent in new[] { _pieces, _terrain, _markers, _zones, _ghosts }) foreach (var c in parent.GetChildren()) c.QueueFree(); }
}

public interface IHasId { Guid Id { get; } }
