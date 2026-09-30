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
