# Task 11: The table view — frame, slab, proxies, terrain, markers, zones, camera, mesh loading

**Files:**
- Create: `client/scenes/TableView.tscn`, `client/src/Table/WorldFrame.cs`, `client/src/Table/Palette.cs`, `client/src/Table/SlabNode.cs`, `client/src/Table/PieceNode.cs`, `client/src/Table/TerrainNode.cs`, `client/src/Table/MarkerNode.cs`, `client/src/Table/ZoneNode.cs`, `client/src/Table/OrbitCamera.cs`, `client/src/Table/MeshLoader.cs`, `client/src/Table/TableView.cs`

**Interfaces:**
- Consumes: `Session` (`Game`, `Draft`, `Projection`, `BoardChanged`, `EventsApplied`, `GameChanged`), `AssetCache`, `BuiltinContent`, `ManagementClient.GetManifest/GetDownload` (Tasks 1–7); `Projection`, `ModelInPlay`, `Location`, `OnTable` (Protocol); `TableState`, `TerrainPlacement`, `Marker`, `DeploymentZone`, `VisualRef` (Content); `Base`, `BaseShape`, `Volume`, `Footprint`, `FootprintKind`, `ZoneStyles`, `Settling`, `TableSize`, `Point3`, `Yaw` (Geometry).
- Produces (namespace `Tabletop.Client.Table`): `static class WorldFrame` — `Vector3 ToGodot(Point3)`, `Point3 FromGodot(Vector3)`, `float Radians(Yaw)`, `Yaw YawFrom(float radians)`; `static class Palette` — `Color Team(int ordinal)`, `Color ForDeclarations(TerrainDeclarations)`, `Color Proxy`, `Color Selected`, `Color Ghost`; `partial class SlabNode : Node3D` (`Set(TableSize, VisualRef texture)`); `partial class PieceNode : Node3D` (`Guid ModelId`, `Guid UnitId`, `void SetBody(Base, double height)`, `void SetMesh(Node3D? scene)`, `void SetHighlight(Color?)`, `Aabb Bounds`, the proxy is a `MeshInstance3D` of a `CylinderMesh`/`BoxMesh`/scaled cylinder for circle/square/oval with a translucent material); `partial class TerrainNode : Node3D` (`Guid PlacementId`, `void Set(TerrainPlacement)`: one proxy `MeshInstance3D` per volume, offset, yaw, scale and heights applied, coloured by declaration; `SetMesh`); `partial class MarkerNode : Node3D` (`Guid MarkerId`, `Set(Marker)`: a disc of the diameter, a label); `partial class ZoneNode : Node3D` (`Set(DeploymentZone zone, TableSize size, Color colour)`: `ZoneStyles.Resolve(zone.Shape, size)` → flat `ArrayMesh` fans at 0.02" above the slab, with no frame conversion — `ZoneStyles` already answers in the wire frame; an unknown shape id draws nothing and logs a warning); `partial class OrbitCamera : Camera3D` (orbit with the right mouse button, pan with the middle, zoom with the wheel; `Home` snaps top-down over the table centre; `End` drops to table level; `FocusOn(TableSize)`); `sealed class MeshLoader(AssetCache cache, ManagementClient management, Guid gameId)` — `Task<Node3D?> LoadAssetAsync(Guid assetId, Vector3 cameraPosition, Vector3 piecePosition)` (the manifest by asset id → cache → `GltfDocument.AppendFromFile` + `GenerateScene` on the main thread; nearest-first through a priority queue keyed by distance to the camera; two downloads in flight; a failure logs and returns null so the proxy stays), `Node3D? LoadBuiltin(string name, Volume? fit = null)` (the group mesh `BuiltinContent.TerrainMeshFile` names, when the file exists, wrapped in a node that scales its unit box onto `fit`), `void RefreshManifest()`; `partial class TableView : Node3D` — `void Bind(Session)`, `PieceNode? PieceAt(Guid modelId)`, `TerrainNode? TerrainAt(Guid)`, `MarkerNode? MarkerAt(Guid)`, `IReadOnlyList<WorldVolume> Volumes` (wire frame, from the board or draft), `TableSize Size`, `void Redraw()` (diff the projection or draft against the nodes: add, move, remove), `void ShowExitGhosts(IReadOnlyList<Guid> modelIds, Guid containerId)` and `void ClearExitGhosts()` (the disembark's draggable stand-ins, Task 14), `bool IsExitGhost(Guid modelId)`, `OrbitCamera Camera`.
- **A piece is drawn as its hitbox first**: `PieceNode.SetBody` builds the proxy at once; `SetMesh` adds the loaded scene as a child and keeps the proxy at 25% alpha beneath it; the scene arrives from `MeshLoader` already scaled and rotated by the asset's `Scale` and `Orientation` (`ManifestEntry` for any participant's asset, `AssetDto` for an own asset outside the manifest — `MeshLoader.Orient`). Terrain and markers come from `Draft` during setup and from `Projection.Board.Table` (plus `Projection.Markers` for tokens) from playing on.
- **A model that is not `OnTable` is absent, not drawn somewhere wrong.** `Redraw` syncs `_pieces` against `Projection.Models.Values.Where(m => m.Location is OnTable)`, so a model on the shelf or **inside a container** has no node at all, and `Sync` frees the node the moment an event moves it there. `Location` is the only source: `OnShelf` and `Inside` carry no `Placement` (game flow → Inside a container: *"an occupant has no table position of its own"*), so there is no stale or default position to draw at, and `PieceAt` answering null is what keeps the ruler, the log's highlight and the Ask panel's highlights silently correct for an occupant. **The one exception is a disembark**, and it is deliberately not part of the projection's diff: `ShowExitGhosts` puts `PieceNode`s in a separate `_ghosts` child at the container's placement so the player has something to drag out, `Redraw` never touches that child, and `ClearExitGhosts` frees them when the Ask is answered.

Spec: client shell → The table view (slab at real size with its texture; terrain as meshes; markers; zones as flat polygons; pieces as hitbox first; missing mesh leaves a translucent proxy; runtime `GLTFDocument` from a presigned URL into a content-addressed cache; built-ins load through the same path from local files; nearest to the camera first; camera orbit/pan/zoom, top-down snap, table-level drop; Godot physics not in the scene); asset library → What a slot draws when its asset is missing (the base extruded to its height, semi-transparent green); setup content → The table is a slab (1" render thickness); geometry values → Collision volumes (prisms), Deployment shapes (polygon sets); setup phase → Zones (assigned by ordinal); geometry plan → `ZoneStyles.Resolve`, the frame; table domain model → Regions drawn during deployment.

- [ ] **Step 1: Write `TableView.tscn`** (root `Node3D`, script `res://src/Table/TableView.cs`, one `DirectionalLight3D` child rotated −50° about X, one `WorldEnvironment` child) — the editor may add these; the hand-written file needs only the root and script:

```
[gd_scene load_steps=2 format=3]

[ext_resource type="Script" path="res://src/Table/TableView.cs" id="1"]

[node name="TableView" type="Node3D"]
script = ExtResource("1")
```

- [ ] **Step 2: Write `WorldFrame.cs` and `Palette.cs`**

```csharp
using Godot;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>Wire frame (X width, Y length, Z up, corner origin) to Godot (Y up, −Z forward). Yaw 0 faces +Y = Godot −Z; positive yaw is counter-clockwise from above, which is Godot's positive rotation about +Y.</summary>
public static class WorldFrame
{
    public static Vector3 ToGodot(Point3 p) => new((float)p.X, (float)p.Z, (float)-p.Y);
    public static Point3 FromGodot(Vector3 v) => new(v.X, -v.Z, v.Y);
    public static float Radians(Yaw yaw) => Mathf.DegToRad((float)yaw.Degrees);
    public static Yaw YawFrom(float radians) => new(Mathf.RadToDeg(radians));
}

public static class Palette
{
    static readonly Color[] Teams = [new(0.85f, 0.25f, 0.2f), new(0.2f, 0.45f, 0.9f), new(0.25f, 0.7f, 0.3f), new(0.9f, 0.75f, 0.2f), new(0.6f, 0.3f, 0.8f), new(0.2f, 0.75f, 0.75f)];
    public static Color Team(int ordinal) => Teams[(Math.Max(1, ordinal) - 1) % Teams.Length];
    public static readonly Color Proxy = new(0.3f, 0.9f, 0.4f, 0.45f);       // semi-transparent green (asset library)
    public static readonly Color Selected = new(1f, 1f, 1f);
    public static readonly Color Ghost = new(0.9f, 0.9f, 1f, 0.35f);
    public static readonly Color Unassigned = new(0.6f, 0.6f, 0.6f, 0.35f);

    public static Color ForDeclarations(TerrainDeclarations d) => d switch
    {
        _ when d.HasFlag(TerrainDeclarations.Impassable) => new(0.5f, 0.5f, 0.55f, 0.6f),
        _ when d.HasFlag(TerrainDeclarations.Dangerous) => new(0.9f, 0.4f, 0.2f, 0.5f),
        _ when d.HasFlag(TerrainDeclarations.BlocksLineOfSight) => new(0.45f, 0.35f, 0.3f, 0.6f),
        _ when d.HasFlag(TerrainDeclarations.Cover) => new(0.3f, 0.65f, 0.3f, 0.5f),
        _ when d.HasFlag(TerrainDeclarations.Difficult) => new(0.7f, 0.65f, 0.3f, 0.5f),
        _ => new(0.7f, 0.7f, 0.7f, 0.4f),
    };

    public static StandardMaterial3D Translucent(Color c) => new() { AlbedoColor = c, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
}
```

- [ ] **Step 3: Write `SlabNode.cs`, `PieceNode.cs`, `TerrainNode.cs`, `MarkerNode.cs`**

```csharp
using Godot;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>The board at its real size, 1" thick, top face at Z = 0. Corner origin: the slab spans [0,W] × [0,L] in the wire frame.</summary>
public partial class SlabNode : Node3D
{
    const float Thickness = 1f;
    MeshInstance3D? _mesh;

    public void Set(TableSize size, VisualRef texture)
    {
        _mesh?.QueueFree();
        _mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3((float)size.Width, Thickness, (float)size.Length) } };
        _mesh.Position = WorldFrame.ToGodot(new Point3(size.Width / 2, size.Length / 2, -Thickness / 2));
        var material = new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.45f, 0.3f) };
        if (texture.IsBuiltin && ResourceLoader.Exists(Core.BuiltinContent.TextureFile(texture.Builtin!)))
        { material.AlbedoTexture = GD.Load<Texture2D>(Core.BuiltinContent.TextureFile(texture.Builtin!)); material.AlbedoColor = Colors.White; material.Uv1Scale = new Vector3((float)size.Width / 12, (float)size.Length / 12, 1); }
        _mesh.MaterialOverride = material;
        AddChild(_mesh);
    }
}

/// <summary>A model: its base extruded to its height, then the mesh decorating it. The proxy is the thing the rules measure; it never goes away.</summary>
public partial class PieceNode : Node3D
{
    public Guid ModelId { get; init; }
    public Guid UnitId { get; init; }
    MeshInstance3D _proxy = null!;
    Node3D? _mesh;
    Color _base = Palette.Proxy;

    public void SetBody(Base b, double height)
    {
        _proxy?.QueueFree();
        var h = (float)height;
        Mesh mesh = b.Shape switch
        {
            BaseShape.Circle => new CylinderMesh { TopRadius = (float)b.A / 2, BottomRadius = (float)b.A / 2, Height = h },
            BaseShape.Rect => new BoxMesh { Size = new Vector3((float)b.B, h, (float)b.A) },      // width across X, length along the facing axis
            _ => new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = h },
        };
        _proxy = new MeshInstance3D { Mesh = mesh, Position = new Vector3(0, h / 2, 0), MaterialOverride = Palette.Translucent(_base) };
        if (b.Shape == BaseShape.Oval) _proxy.Scale = new Vector3((float)b.B, 1, (float)b.A);   // oval length along facing (+Y wire = −Z Godot)
        AddChild(_proxy);
    }

    public void SetMesh(Node3D? scene)
    {
        _mesh?.QueueFree(); _mesh = scene;
        if (scene is not null) { AddChild(scene); _proxy.MaterialOverride = Palette.Translucent(_base with { A = 0.12f }); }
    }

    public void SetHighlight(Color? colour)
    {
        _base = colour is { } c ? new Color(c, 0.6f) : Palette.Proxy;
        _proxy.MaterialOverride = Palette.Translucent(_mesh is null ? _base : _base with { A = 0.12f });
    }

    public Aabb Bounds => _proxy.GlobalTransform * _proxy.GetAabb();
}

/// <summary>One proxy per volume, placed by the volume's offset, yaw and heights under the piece's transform and uniform scale.</summary>
public partial class TerrainNode : Node3D
{
    public Guid PlacementId { get; init; }
    readonly List<MeshInstance3D> _proxies = new();
    Node3D? _mesh;

    public void Set(TerrainPlacement p)
    {
        Position = WorldFrame.ToGodot(p.Position); Rotation = new Vector3(0, WorldFrame.Radians(p.Yaw), 0); Scale = Vector3.One * (float)p.Scale;
        foreach (var m in _proxies) m.QueueFree(); _proxies.Clear();
        foreach (var tv in p.Volumes)
        {
            var v = tv.Volume; var height = (float)(v.Top - v.Bottom);
            Mesh mesh = v.Footprint.Kind == FootprintKind.Circle
                ? new CylinderMesh { TopRadius = (float)v.Footprint.A, BottomRadius = (float)v.Footprint.A, Height = height }
                : new BoxMesh { Size = new Vector3((float)v.Footprint.A, height, (float)v.Footprint.B) };
            var node = new MeshInstance3D { Mesh = mesh, MaterialOverride = Palette.Translucent(Palette.ForDeclarations(v.Declarations)),
                Position = new Vector3((float)v.Offset.X, (float)v.Bottom + height / 2, (float)-v.Offset.Y), Rotation = new Vector3(0, WorldFrame.Radians(v.Yaw), 0) };
            _proxies.Add(node); AddChild(node);
        }
    }

    public void SetMesh(Node3D? scene) { _mesh?.QueueFree(); _mesh = scene; if (scene is not null) { AddChild(scene); foreach (var p in _proxies) p.Visible = false; } }
    public Aabb Bounds => _proxies.Count == 0 ? new Aabb(GlobalPosition, Vector3.One) : _proxies.Skip(1).Aggregate(_proxies[0].GlobalTransform * _proxies[0].GetAabb(), (a, m) => a.Merge(m.GlobalTransform * m.GetAabb()));
}

/// <summary>A marker: a disc of its diameter with its label. Markers ignore terrain (setup content).</summary>
public partial class MarkerNode : Node3D
{
    public Guid MarkerId { get; init; }
    MeshInstance3D? _disc; Label3D? _label;

    public void Set(Marker m)
    {
        Position = WorldFrame.ToGodot(m.Position); Rotation = new Vector3(0, WorldFrame.Radians(m.Yaw), 0);
        _disc?.QueueFree(); _label?.QueueFree();
        var r = (float)m.Diameter / 2;
        _disc = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r, Height = 0.1f }, Position = new Vector3(0, 0.05f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = m.Kind == MarkerKind.Objective ? new Color(0.95f, 0.8f, 0.2f) : new Color(0.9f, 0.9f, 0.9f) } };
        AddChild(_disc);
        if (m.Label is { } text) { _label = new Label3D { Text = text, Position = new Vector3(0, 0.6f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled }; AddChild(_label); }
    }

    public Aabb Bounds => _disc is null ? new Aabb(GlobalPosition, Vector3.One) : _disc.GlobalTransform * _disc.GetAabb();
}
```

- [ ] **Step 4: Write `ZoneNode.cs` and `OrbitCamera.cs`**

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>A deployment zone: the style's polygons for this ordinal, flat, just above the slab. Colour is the team's, or grey with no team yet.</summary>
public partial class ZoneNode : Node3D
{
    public int TeamOrdinal { get; private set; }

    public void Set(DeploymentZone zone, TableSize size, Color colour)
    {
        TeamOrdinal = zone.TeamOrdinal;
        foreach (var c in GetChildren()) c.QueueFree();
        IReadOnlyList<Polygon> polygons;
        try { polygons = ZoneStyles.Resolve(zone.Shape, size); }
        catch (ArgumentException) { GD.PushWarning($"Unknown zone shape '{zone.Shape}'; nothing drawn."); return; }
        foreach (var polygon in polygons)
        {
            var verts = polygon.Vertices.Select(p => WorldFrame.ToGodot(new Point3(p.X, p.Y, 0.02))).ToArray();   // ZoneStyles answers in the wire frame
            var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
            for (var i = 1; i + 1 < verts.Length; i++) { st.AddVertex(verts[0]); st.AddVertex(verts[i + 1]); st.AddVertex(verts[i]); }   // fan: zone polygons are convex
            AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = Palette.Translucent(colour) });
        }
    }
}

/// <summary>Orbit, pan and zoom over the table; Home snaps top-down, End drops to table level for line-of-sight checks.</summary>
public partial class OrbitCamera : Camera3D
{
    Vector3 _target; float _distance = 60, _yaw = 0.6f, _pitch = 0.9f;

    public void FocusOn(TableSize size) { _target = WorldFrame.ToGodot(new Point3(size.Width / 2, size.Length / 2, 0)); _distance = (float)Math.Max(size.Width, size.Length) * 0.9f; Apply(); }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion m when Input.IsMouseButtonPressed(MouseButton.Right): _yaw -= m.Relative.X * 0.005f; _pitch = Mathf.Clamp(_pitch + m.Relative.Y * 0.005f, 0.05f, 1.55f); Apply(); break;
            case InputEventMouseMotion m when Input.IsMouseButtonPressed(MouseButton.Middle):
                var right = GlobalTransform.Basis.X; var forward = new Vector3(-GlobalTransform.Basis.Z.X, 0, -GlobalTransform.Basis.Z.Z).Normalized();
                _target += (-right * m.Relative.X + forward * m.Relative.Y) * _distance * 0.001f; Apply(); break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }: _distance = Mathf.Max(4, _distance * 0.9f); Apply(); break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }: _distance = Mathf.Min(300, _distance * 1.1f); Apply(); break;
            case InputEventKey { Pressed: true, Keycode: Key.Home }: _pitch = 1.55f; Apply(); break;
            case InputEventKey { Pressed: true, Keycode: Key.End }: _pitch = 0.05f; _target.Y = 1.0f; Apply(); break;
        }
    }

    void Apply()
    {
        var offset = new Vector3(Mathf.Cos(_pitch) * Mathf.Sin(_yaw), Mathf.Sin(_pitch), Mathf.Cos(_pitch) * Mathf.Cos(_yaw)) * _distance;
        Position = _target + offset;
        LookAt(_target, Vector3.Up);
    }
}
```

- [ ] **Step 5: Write `MeshLoader.cs`**

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>Runtime GLB loading: manifest → cache → GltfDocument. Nearest to the camera first; a failure leaves the proxy and logs.</summary>
public sealed class MeshLoader(AssetCache cache, ManagementClient management, Guid gameId)
{
    readonly Dictionary<Guid, ManifestEntry> _manifest = new();
    readonly PriorityQueue<(Guid AssetId, TaskCompletionSource<string?> Done), float> _queue = new();
    readonly SemaphoreSlim _inFlight = new(2, 2);
    Task? _manifestLoad;

    public void RefreshManifest() => _manifestLoad = Task.Run(async () =>
    {
        try { var m = await management.GetManifest(gameId); lock (_manifest) { _manifest.Clear(); foreach (var e in m.Assets) _manifest[e.AssetId] = e; } }
        catch (ManagementException e) { GD.PushWarning($"manifest: {e.Message}"); }
    });

    /// <summary>Fetches the bytes (queued by distance), then builds the scene on the main thread. Null when the asset cannot be had.</summary>
    public async Task<Node3D?> LoadAssetAsync(Guid assetId, Vector3 cameraPosition, Vector3 piecePosition)
    {
        if (_manifestLoad is not null) await _manifestLoad;
        ManifestEntry? entry; lock (_manifest) entry = _manifest.GetValueOrDefault(assetId);
        Uri url; string hash; double scale; Orientation orientation;
        if (entry is not null) (url, hash, scale, orientation) = (entry.Url, entry.Hash, entry.Scale, entry.Orientation);
        else
        {
            try { var mine = await management.GetDownload(assetId); var asset = (await management.ListAssets(AssetKind.Miniature, null)).Items.Concat((await management.ListAssets(AssetKind.Terrain, null)).Items).FirstOrDefault(a => a.Id == assetId); if (asset is null) return null; (url, hash, scale, orientation) = (mine.Url, asset.Hash, asset.Scale, asset.Orientation); }
            catch (ManagementException) { return null; }   // not in the manifest and not mine: the proxy stays
        }
        var done = new TaskCompletionSource<string?>();
        _queue.Enqueue((assetId, done), cameraPosition.DistanceTo(piecePosition));
        _ = Pump(hash, url);
        var path = await done.Task;
        return path is null ? null : Build(path, scale, orientation);
    }

    async Task Pump(string hash, Uri url)
    {
        await _inFlight.WaitAsync();
        try
        {
            if (!_queue.TryDequeue(out var item, out _)) return;
            try { item.Done.SetResult(await cache.FetchAsync(hash, url, CancellationToken.None)); }
            catch (Exception e) when (e is HttpRequestException or HashMismatchException or IOException) { GD.PushWarning($"asset {item.AssetId}: {e.Message}"); item.Done.SetResult(null); }
        }
        finally { _inFlight.Release(); }
    }

    public Node3D? LoadBuiltin(string name, Volume? fit = null)
    {
        var file = BuiltinContent.TerrainMeshFile(name);
        // The res:// path is passed through unchanged: GltfDocument reads it with Godot's FileAccess, so it works from
        // the editor and from an exported .pck alike, which GlobalizePath would not. Built-ins ship inside the build and
        // never enter the cache. A missing file returns null, and TerrainNode keeps its proxies visible.
        if (file is null || !FileAccess.FileExists(file) || Build(file, 1, default) is not Node3D scene) return null;
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
        if (doc.GenerateScene(state) is not Node3D scene) return null;
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

`Orient` is the only place the convention is spelled out on the client; it must change together with the web app's `toWorld`/`eulerFor` if the convention ever does (web-app plan → Questions 2).

The queue is keyed by distance at request time; when the camera moves, pieces requested later but nearer overtake, which is what "nearest to the camera first" means here. `Pump` dequeues whichever item is nearest, not necessarily the one it was called for — every waiting item gets served once its own `Pump` runs.

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

    public void Bind(Session session)
    {
        _session = session;
        _meshes = new MeshLoader(Main.Instance.Cache, session.Management, session.GameId!.Value);
        _meshes.RefreshManifest();
        session.BoardChanged += Redraw; session.EventsApplied += _ => Redraw(); session.GameChanged += Redraw;
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
        var existing = parent.GetChildren().OfType<TNode>().ToDictionary(n => n.Id);
        var wanted = items.ToDictionary(id);
        foreach (var (key, node) in existing) if (!wanted.ContainsKey(key)) node.QueueFree();
        foreach (var (key, item) in wanted) { if (existing.TryGetValue(key, out var node)) update(node, item); else parent.AddChild(create(item)); }
    }

    async void LoadPieceMesh(PieceNode n, ModelInPlay m) { if (m.Slot.AssetId is { } asset) n.SetMesh(await _meshes.LoadAssetAsync(asset, Camera.GlobalPosition, n.GlobalPosition)); }
    // A built-in's group mesh is fitted to the placement's first volume; an asset's mesh carries its own scale.
    async void LoadTerrainMesh(TerrainNode n, TerrainPlacement t) { n.SetMesh(t.Mesh.IsBuiltin ? _meshes.LoadBuiltin(t.Mesh.Builtin!, t.Volumes.Count > 0 ? t.Volumes[0].Volume : null) : await _meshes.LoadAssetAsync(t.Mesh.AssetId!.Value, Camera.GlobalPosition, n.GlobalPosition)); }

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
```

`IHasId` is declared in `TableView.cs` as `public interface IHasId { Guid Id { get; } }`; `PieceNode` implements it with `public Guid Id => ModelId;`, `TerrainNode` with `=> PlacementId`, `MarkerNode` with `=> MarkerId`, and `ZoneNode` gains `public Guid Id { get; private set; }` set from `zone.Id` in `Set`. Add the `: IHasId` and the property to each node class in Step 3 and Step 4.

- [ ] **Step 7: Run it.** The Game screen does not exist yet; add a temporary line in `Main.Route` mapping `Connected` to `res://scenes/TableView.tscn` and, in `TableView._Ready`, `if (Main.Instance.Session.GameId is not null) Bind(Main.Instance.Session);`. Create a game, start setup from a second client (or with Task 13's lobby panel later): the slab appears at 72×48 with the `grass` texture tiled every 12", or its flat colour if `content/builtin/textures/grass.png` has not been authored yet; orbit, pan, zoom, Home and End work. If Task 6 Step 4's meshes exist, add a built-in of each of the ten groups at both extremes of its footprint and check that the fitted mesh covers its hitbox without poking through the slab; if they do not, every piece shows its proxy and that is the expected degraded picture. Remove the temporary line before committing. `./scripts/check-client` prints nothing. Commit: `Draw the table: slab, hitbox proxies, terrain, markers, zones, camera, runtime GLB loading`.
