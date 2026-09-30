# Task 11b: `MeshLoader` and `TableView`

Part of Task 11; its files and interfaces are in [Task 11a](task-11a-table-view-nodes.md).

- [ ] **Step 5: Write `MeshLoader.cs`**

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Geometry;
using Orientation = Tabletop.Geometry.Orientation;

namespace Tabletop.Client.Table;

/// <summary>Runtime GLB loading: manifest → cache → GltfDocument. Nearest to the camera first; a failure leaves the proxy and logs.</summary>
public sealed class MeshLoader(AssetCache cache, ManagementClient management, Guid gameId)
{
    readonly Dictionary<Guid, ManifestEntry> _manifest = new();
    readonly PriorityQueue<(Guid AssetId, string Hash, Uri Url, TaskCompletionSource<string?> Done), float> _queue = new();
    readonly SemaphoreSlim _inFlight = new(2, 2);
    Task? _manifestLoad;
    DateTimeOffset _expires = DateTimeOffset.MaxValue;

    /// <summary>Starts a manifest fetch, or joins the one in flight. Returns the task so a caller can wait for it.</summary>
    public Task RefreshManifest()
    {
        lock (_manifest)
        {
            if (_manifestLoad is { IsCompleted: false } running) return running;
            return _manifestLoad = Task.Run(async () =>
            {
                try
                {
                    var m = await management.GetManifest(gameId);
                    lock (_manifest) { _manifest.Clear(); foreach (var e in m.Assets) _manifest[e.AssetId] = e; _expires = m.ExpiresAt; }
                }
                catch (Exception e) { GD.PushWarning($"manifest: {e.Message}"); }
            });
        }
    }

    /// <summary>Fetches the bytes (queued by distance), then builds the scene on the main thread. Null when the asset cannot be had.</summary>
    public async Task<Node3D?> LoadAssetAsync(Guid assetId, Vector3 cameraPosition, Vector3 piecePosition)
    {
        Task? load; lock (_manifest) load = _manifestLoad;
        if (load is not null) await load;
        if (DateTimeOffset.UtcNow >= _expires) await RefreshManifest();      // the entries' download URLs have lapsed
        ManifestEntry? entry; lock (_manifest) entry = _manifest.GetValueOrDefault(assetId);
        if (entry is null)
        {
            // Another player's army or terrain that arrived after the last fetch: one shared refresh before the owner-only fallback.
            await RefreshManifest();
            lock (_manifest) entry = _manifest.GetValueOrDefault(assetId);
        }
        Uri url; string hash; double scale; Orientation orientation;
        if (entry is not null) (url, hash, scale, orientation) = (entry.Url, entry.Hash, entry.Scale, entry.Orientation);
        else
        {
            try { var mine = await management.GetDownload(assetId); var asset = (await management.ListAssets(AssetKind.Miniature, null)).Items.Concat((await management.ListAssets(AssetKind.Terrain, null)).Items).FirstOrDefault(a => a.Id == assetId); if (asset is null) return null; (url, hash, scale, orientation) = (mine.Url, asset.Hash, asset.Scale, asset.Orientation); }
            catch (Exception) { return null; }   // not in the manifest and not mine: the proxy stays
        }
        var done = new TaskCompletionSource<string?>();
        _queue.Enqueue((assetId, hash, url, done), cameraPosition.DistanceTo(piecePosition));
        _ = Pump();
        var path = await done.Task;
        return path is null ? null : Build(path, scale, orientation);
    }

    async Task Pump()
    {
        await _inFlight.WaitAsync();
        try
        {
            if (!_queue.TryDequeue(out var item, out _)) return;
            try { item.Done.SetResult(await cache.FetchAsync(item.Hash, item.Url, CancellationToken.None)); }
            catch (Exception e) { GD.PushWarning($"asset {item.AssetId}: {e.Message}"); item.Done.SetResult(null); }
        }
        finally { _inFlight.Release(); }
    }

    public Node3D? LoadBuiltin(string name, Volume? fit = null)
    {
        var file = BuiltinContent.TerrainMeshFile(name);
        // The res:// path is passed through unchanged: GltfDocument reads it with Godot's FileAccess, so it works from
        // the editor and from an exported .pck alike, which GlobalizePath would not. Built-ins ship inside the build and
        // never enter the cache. A missing file returns null, and TerrainNode keeps its proxies visible.
        if (file is null || !Godot.FileAccess.FileExists(file)) return null;
        var scene = Build(file, 1, default);
        if (scene is null) return null;
        if (fit is null) return scene;
        // One mesh serves a group of built-ins (Task 6), so it is authored in the unit box — table-frame x and y in
        // [-0.5, 0.5], z in [0, 1] — and fitted here to the volume it is decorating. A circle footprint's A is a radius,
        // a rect's A and B are extents. The scale goes on a wrapping node, because Node3D.Scale would rebuild the basis
        // Orient set. Built-in volumes have no offset and no yaw of their own; an override that gives one moves the
        // proxy and not this mesh, which is the same looseness a swapped asset mesh already has.
        var span = fit.Footprint.Kind == FootprintKind.Circle
            ? new Vector3((float)fit.Footprint.A * 2, 0, (float)fit.Footprint.A * 2)
            : new Vector3((float)fit.Footprint.A, 0, (float)fit.Footprint.B);
        var box = new Node3D { Scale = span with { Y = (float)Math.Max(fit.Top - fit.Bottom, 0.01) }, Position = new Vector3(0, (float)fit.Bottom, 0) };
        box.AddChild(scene);
        return box;
    }

    /// <summary>Main thread only: GltfDocument builds a scene tree, then the asset's corrections are applied to its root.</summary>
    static Node3D? Build(string path, double scale, Orientation orientation)
    {
        var doc = new GltfDocument(); var state = new GltfState();
        var err = doc.AppendFromFile(path, state);
        if (err != Error.Ok) { GD.PushWarning($"GLB {path}: {err}"); return null; }
        var generated = doc.GenerateScene(state);
        if (generated is not Node3D scene) { generated?.Free(); return null; }
        Orient(scene, scale, orientation);
        return scene;
    }

    /// <summary>The asset row's scale and orientation, in the one convention stated on Tabletop.Geometry.Orientation (contracts plan Task 2; the web app writes it): the raw mesh coordinates are read as
    /// table-frame coordinates, rotated by pitch about table X, then roll about table Y, then yaw about table Z (degrees, right-handed, extrinsic), then
    /// scaled, then mapped into Godot's Y-up frame the way <see cref="WorldFrame"/> maps points (table X → X, table Y → −Z, table Z → Y).
    /// A Y-up glTF therefore stands upright at pitch = 90 and a Z-up STL conversion at pitch = 0 — the two values the web app writes.</summary>
    public static void Orient(Node3D scene, double scale, Orientation o)
    {
        var inTable = Basis.Identity
            .Rotated(new Vector3(1, 0, 0), Mathf.DegToRad((float)o.Pitch))
            .Rotated(new Vector3(0, 1, 0), Mathf.DegToRad((float)o.Roll))
            .Rotated(new Vector3(0, 0, 1), Mathf.DegToRad((float)o.Yaw));
        var tableToGodot = new Basis(new Vector3(1, 0, 0), new Vector3(0, 0, -1), new Vector3(0, 1, 0));   // columns: where table X, Y, Z land in Godot
        scene.Transform = new Transform3D((tableToGodot * inTable).Scaled(Vector3.One * (float)scale), Vector3.Zero);
    }
}
```

`Orientation` is aliased to `Tabletop.Geometry.Orientation`, which `Godot` also declares, and `Godot.FileAccess` is qualified against `System.IO.FileAccess`.

`Orient` reads `Orientation` by member name only. The record is declared `(Pitch, Yaw, Roll)` and the rotation order is pitch about table X, then roll about table Y, then yaw about table Z, extrinsic, so a positional read would swap roll and yaw. `Orient` is the only place the convention is spelled out on the client; it must change together with the web app's `toWorld`/`eulerFor` if the convention ever does (web-app plan → Questions 2).

The queue is keyed by distance at request time; when the camera moves, pieces requested later but nearer overtake, which is what "nearest to the camera first" means here. `Pump` dequeues whichever item is nearest, not necessarily the one it was called for, and fetches that item's own hash and URL. Every waiting item is served once its own `Pump` runs. `Pump` completes its item with null on any exception, so no caller waits forever.

The manifest lists the game's assets by who has picked what, so the fetch at `Bind` misses armies picked later. `TableView` fetches it again when the status becomes `playing`, when every army is in. `LoadAssetAsync` refetches once on a miss, for an asset that arrived after the last fetch, and once past `ExpiresAt`, when the presigned URLs have lapsed. Concurrent callers share one fetch. The owner-only lookup is the fallback after that.

`Build` frees a generated root that is not a `Node3D`. `RefreshManifest` and the owner-only lookup catch `Exception`, so a failure shows as a proxy.

- [ ] **Step 6: Write `TableView.cs`**

```csharp
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

    Action? _onBoard, _onGame; Action<IReadOnlyList<GameEvent>>? _onEvents;
    GameStatus? _status;

    public override void _ExitTree()
    {
        if (_session is null) return;
        _session.BoardChanged -= _onBoard; _session.EventsApplied -= _onEvents; _session.GameChanged -= _onBoard; _session.GameChanged -= _onGame;
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
```

`IHasId` is declared in `TableView.cs`; `Sync` diffs any node type that implements it. `Sync` skips nodes already queued for deletion, so a redraw in the same frame as a removal does not see the freed node as present.

`Bind` refuses a second call, because a second set of subscriptions would redraw twice and never be removed. The load priority is computed from the placement, not from `GlobalPosition`, because a node just created is not in the tree yet. A mesh that arrives after its node was freed is itself freed.

- [ ] **Step 7: Build and check.** `dotnet build` succeeds and `./scripts/check-client` prints nothing. Nothing routes to the table until Task 13's Game screen binds it, so the run below is Task 13's owner check. Create a game and start setup: the slab appears at 72×48 with the `grass` texture tiled every 12", or its flat colour if `content/builtin/textures/grass.png` has not been authored yet; orbit, pan, zoom, Home and End work. If Task 6 Step 4's meshes exist, add a built-in of each of the ten groups at both extremes of its footprint and check that the fitted mesh covers its hitbox without poking through the slab; if they do not, every piece shows its proxy and that is the expected degraded picture. Commit: `Draw the table: slab, hitbox proxies, terrain, markers, zones, camera, runtime GLB loading`.
