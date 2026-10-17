using Godot;
using Tabletop.Client.Core;
using Tabletop.Geometry;
using Orientation = Tabletop.Geometry.Orientation;

namespace Tabletop.Client.Table;

/// <summary>
/// Runtime GLB loading: manifest → cache → GltfDocument. Nearest to the camera first; a failure leaves the proxy and logs. Each
/// file is parsed once, off the main thread, into a template scene; every piece gets its own duplicate of it. Main thread only,
/// apart from the parse and the downloads.
/// </summary>
public sealed class MeshLoader(AssetCache cache, ManagementClient management, Guid gameId) : IDisposable
{
    readonly Dictionary<string, Task<Node3D?>> _templates = new();
    bool _disposed;
    readonly Dictionary<Guid, ManifestEntry> _manifest = new();
    readonly PriorityQueue<(Guid AssetId, string Hash, Uri Url, long Cap, TaskCompletionSource<string?> Done), float> _queue = new();
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
        Uri url; string hash; double scale; Orientation orientation; AssetKind kind;
        if (entry is not null) (url, hash, scale, orientation, kind) = (entry.Url, entry.Hash, entry.Scale, entry.Orientation, entry.Kind);
        else
        {
            try { var mine = await management.GetDownload(assetId); var asset = (await management.ListAssets(AssetKind.Miniature, null)).Items.Concat((await management.ListAssets(AssetKind.Terrain, null)).Items).FirstOrDefault(a => a.Id == assetId); if (asset is null) return null; (url, hash, scale, orientation, kind) = (mine.Url, asset.Hash, asset.Scale, asset.Orientation, asset.Kind); }
            catch (Exception) { return null; }   // not in the manifest and not mine: the proxy stays
        }
        var done = new TaskCompletionSource<string?>();
        _queue.Enqueue((assetId, hash, url, AssetCache.CapFor(kind), done), cameraPosition.DistanceTo(piecePosition));
        _ = Pump();
        var path = await done.Task;
        return path is null ? null : await Build(path, scale, orientation);
    }

    async Task Pump()
    {
        await _inFlight.WaitAsync();
        try
        {
            if (!_queue.TryDequeue(out var item, out _)) return;
            try { item.Done.SetResult(await cache.FetchAsync(item.Hash, item.Url, item.Cap, CancellationToken.None)); }
            catch (Exception e) { GD.PushWarning($"asset {item.AssetId}: {e.Message}"); item.Done.SetResult(null); }
        }
        finally { _inFlight.Release(); }
    }

    public async Task<Node3D?> LoadBuiltinAsync(string name, Volume? fit = null)
    {
        var file = BuiltinContent.TerrainMeshFile(name);
        // The res:// path is passed through unchanged: GltfDocument reads it with Godot's FileAccess, so it works from
        // the editor and from an exported .pck alike, which GlobalizePath would not. Built-ins ship inside the build and
        // never enter the cache. A missing file returns null, and TerrainNode keeps its proxies visible.
        if (file is null || !Godot.FileAccess.FileExists(file)) return null;
        var scene = await Build(file, 1, BuiltinContent.MeshOrientation);
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

    /// <summary>A duplicate of the file's template, with the asset's corrections applied to its root. Null when the file does not parse.</summary>
    async Task<Node3D?> Build(string path, double scale, Orientation orientation)
    {
        if (!_templates.TryGetValue(path, out var template)) _templates[path] = template = Template(path);
        if (await template is not { } root || _disposed) return null;
        var scene = (Node3D)root.Duplicate();
        Orient(scene, scale, orientation);
        return scene;
    }

    /// <summary>The file read and parsed on the thread pool; the scene tree generated on the main thread the await resumes on.
    /// A path is a content hash or a built-in that ships in the build, so its template never goes stale.</summary>
    async Task<Node3D?> Template(string path)
    {
        var parsed = await Task.Run(() =>
        {
            var doc = new GltfDocument(); var state = new GltfState();
            var err = doc.AppendFromFile(path, state);
            if (err != Error.Ok) { GD.PushWarning($"GLB {path}: {err}"); return ((GltfDocument, GltfState)?)null; }
            return (doc, state);
        });
        if (parsed is not var (d, s)) return null;
        var generated = d.GenerateScene(s);
        if (generated is not Node3D scene || _disposed) { generated?.Free(); return null; }
        return scene;
    }

    /// <summary>Frees the templates, which are never in the tree; the duplicates belong to their pieces.</summary>
    public void Dispose()
    {
        _disposed = true;
        foreach (var t in _templates.Values) if (t.IsCompletedSuccessfully && t.Result is { } root && GodotObject.IsInstanceValid(root)) root.Free();
        _templates.Clear();
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
