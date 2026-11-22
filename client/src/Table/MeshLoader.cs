using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Screens;
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
    /// <summary>The editor's import default: normals within this angle merge while a level of detail is simplified.</summary>
    const float LodNormalMergeAngle = 60;
    readonly Dictionary<string, Task<Node3D?>> _templates = new();
    /// <summary>The built-in meshes' templates, shared by every game and the shader warm-up: a built-in file (a content hash or a packed
    /// file) never changes, so it is parsed once per run. Freed at exit.</summary>
    static readonly Dictionary<string, Task<Node3D?>> Builtins = new();
    /// <summary>The files this table is loading, for the loading strip ("Loading models 3 of 12").</summary>
    public AssetProgress Progress { get; } = new();
    bool _disposed;
    readonly Dictionary<Guid, ManifestEntry> _manifest = new();
    readonly PriorityQueue<(Guid AssetId, string Hash, Uri Url, long Cap, TaskCompletionSource<string?> Done), float> _queue = new();
    readonly SemaphoreSlim _inFlight = new(2, 2);
    DateTimeOffset _expires = DateTimeOffset.MaxValue;
    CoalescedRefresh? _refresh;

    /// <summary>Manifest fetches, one at a time. Loads (an unknown id, lapsed URLs) ask through <see cref="CoalescedRefresh.RunIfDue"/>:
    /// at most one start per 5 s, so a batch of unknown ids shares one refresh instead of one each.</summary>
    CoalescedRefresh Refresh => LazyInitializer.EnsureInitialized(ref _refresh, () => new CoalescedRefresh(() => Task.Run(async () =>
    {
        try
        {
            var m = await management.GetManifest(gameId);
            lock (_manifest) { _manifest.Clear(); foreach (var e in m.Assets) _manifest[e.AssetId] = e; _expires = m.ExpiresAt; }
        }
        catch (Exception e) { GD.PushWarning($"manifest: {e.Message}"); }
    }), TimeSpan.FromSeconds(5)));

    /// <summary>Starts a manifest fetch, or joins the one in flight. Returns the task so a caller can wait for it.</summary>
    public Task RefreshManifest() => Refresh.Run();

    /// <summary>Each asset's file and corrections once found, so a second piece of it (another model of the unit, a ghost drawn again)
    /// is a duplicate of the template at once: no manifest wait, no queue, no frame drawn with the hitbox alone. A failure is not kept.</summary>
    readonly Dictionary<Guid, Task<(string Path, double Scale, Orientation Orientation)?>> _resolved = new();

    /// <summary>Fetches the bytes (queued by distance), then builds the scene on the main thread. Null when the asset cannot be had.</summary>
    public async Task<Node3D?> LoadAssetAsync(Guid assetId, Vector3 cameraPosition, Vector3 piecePosition)
    {
        if (!_resolved.TryGetValue(assetId, out var resolved) || resolved.IsCompleted && resolved.Result is null)
            _resolved[assetId] = resolved = Resolve(assetId, cameraPosition, piecePosition);
        var key = assetId.ToString();
        var ready = resolved.IsCompletedSuccessfully && resolved.Result is { } known && _templates.TryGetValue(known.Path, out var t) && t.IsCompleted;
        if (!ready) Progress.Started(key);
        try { return await resolved is { } file ? await Build(file.Path, file.Scale, file.Orientation) : null; }
        finally { if (!ready) Progress.Finished(key); }
    }

    async Task<(string Path, double Scale, Orientation Orientation)?> Resolve(Guid assetId, Vector3 cameraPosition, Vector3 piecePosition)
    {
        await Refresh.Current;
        bool lapsed; lock (_manifest) lapsed = DateTimeOffset.UtcNow >= _expires;
        if (lapsed) await Refresh.RunIfDue();      // the entries' download URLs have lapsed
        ManifestEntry? entry; lock (_manifest) entry = _manifest.GetValueOrDefault(assetId);
        if (entry is null)
        {
            // Another player's army or terrain that arrived after the last fetch: one shared refresh before the owner-only fallback.
            await Refresh.RunIfDue();
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
        return path is null ? null : (path, scale, orientation);
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

    /// <summary>A built-in piece's mesh, fitted to <paramref name="fit"/>. A file not on hand is fetched by the catalog's hash
    /// (<see cref="BuiltinContent.FileAsync"/>) unless <paramref name="fetch"/> is false; null when it cannot be had, and TerrainNode
    /// keeps its proxies visible.</summary>
    public async Task<Node3D?> LoadBuiltinAsync(string name, Volume? fit = null, bool fetch = true)
    {
        if (BuiltinContent.MeshPathOf(name) is not { } path) return null;
        // A cached blob is preflighted in Template like any asset; a packed res:// file (a development run) is passed through
        // unchanged: GltfDocument reads it with Godot's FileAccess, which GlobalizePath would not.
        var file = BuiltinContent.File(path);
        if (file is null && !fetch) return null;
        var ready = file is not null && Builtins.TryGetValue(file, out var t) && t.IsCompleted;
        var key = $"builtin:{path}";
        if (!ready) Progress.Started(key);
        Node3D? scene;
        try
        {
            file ??= await BuiltinContent.FileAsync(path);
            if (file is null || !Godot.FileAccess.FileExists(file)) return null;
            scene = await Build(file, 1, BuiltinContent.MeshOrientation, builtin: true);
            if (scene is null) return null;
            await TerrainLook.ApplyAsync(scene);
        }
        finally { if (!ready) Progress.Finished(key); }
        if (_disposed) { scene.QueueFree(); return null; }
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
    async Task<Node3D?> Build(string path, double scale, Orientation orientation, bool builtin = false)
    {
        var templates = builtin ? Builtins : _templates;
        if (!templates.TryGetValue(path, out var template)) templates[path] = template = Template(path, builtin);
        if (await template is not { } root || _disposed) return null;
        var scene = (Node3D)root.Duplicate();
        Orient(scene, scale, orientation);
        return scene;
    }

    /// <summary>The file read and parsed on the thread pool; the scene tree generated on the main thread the await resumes on.
    /// A path is a content hash or a built-in that ships in the build, so its template never goes stale.</summary>
    async Task<Node3D?> Template(string path, bool builtin)
    {
        var parsed = await Task.Run(() =>
        {
            // A cached blob may predate the server's checks or be hostile: Godot sees it only after GlbPreflight. A res:// file
            // ships in the build.
            if (!path.StartsWith("res://", StringComparison.Ordinal))
            {
                string? refusal;
                try { refusal = GlbPreflight.CheckFile(path); } catch (IOException e) { refusal = e.Message; }
                if (refusal is not null) { GD.PushWarning($"GLB {path} refused: {refusal}"); return ((GltfDocument, GltfState)?)null; }
            }
            var doc = new GltfDocument(); var state = new GltfState();
            var err = doc.AppendFromFile(path, state);
            if (err != Error.Ok) { GD.PushWarning($"GLB {path}: {err}"); return ((GltfDocument, GltfState)?)null; }
            // Levels of detail, which Godot's runtime glTF import leaves out: a printed miniature is tens of thousands of
            // triangles and a table of 150 of them few pixels each, in the camera's view and every shadow split.
            foreach (var mesh in state.GetMeshes()) mesh.Mesh?.GenerateLods(LodNormalMergeAngle, 0, []);
            return (doc, state);
        });
        if (parsed is not var (d, s)) return null;
        // The scene is made on the main thread, within the frame's budget: a table's files arrive together. Not on the pool: the
        // rendering server runs a thread's resource creation on the main thread anyway, and its queue then stalled the main
        // thread's own calls for 170-260 ms in one frame (a loaded layout's markers), where here each file takes its own 10-80 ms frame.
        await FrameBudget.Turn();
        var generated = FrameBudget.Use(() => d.GenerateScene(s));
        if (generated is not Node3D scene || _disposed && !builtin) { generated?.Free(); return null; }
        Settle(scene);
        return scene;
    }

    /// <summary>Two things Godot's runtime glTF import leaves out. COLOR_0 multiplies the base colour (glTF 2.0, Meshes), but the
    /// import turns vertex colours on for every primitive except a mesh's first. And a node whose every surface is alpha-blended
    /// (the built-ins' smog and water surfaces) would still cast an opaque shadow.</summary>
    internal static void Settle(Node node)
    {
        if (node is MeshInstance3D { Mesh: { } mesh } instance)
        {
            var translucent = mesh.GetSurfaceCount() > 0;
            for (var i = 0; i < mesh.GetSurfaceCount(); i++)
            {
                var material = mesh.SurfaceGetMaterial(i) as BaseMaterial3D;
                if (material is not null && mesh is ArrayMesh array && (array.SurfaceGetFormat(i) & Mesh.ArrayFormat.FormatColor) != 0) material.VertexColorUseAsAlbedo = true;
                translucent &= material is not null && material.Transparency != BaseMaterial3D.TransparencyEnum.Disabled;
            }
            if (translucent) instance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
        foreach (var child in node.GetChildren()) Settle(child);
    }

    /// <summary>At exit: frees the built-in templates, which outlive every game, before Godot's servers shut down.</summary>
    public static void FreeBuiltins()
    {
        foreach (var t in Builtins.Values) if (t.IsCompletedSuccessfully && t.Result is { } root && GodotObject.IsInstanceValid(root)) root.Free();
        Builtins.Clear();
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
