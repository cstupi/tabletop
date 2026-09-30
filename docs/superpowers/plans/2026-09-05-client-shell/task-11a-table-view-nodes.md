# Task 11a: The table view: frame, slab, proxies, terrain, markers, zones, camera

**Files:**
- Create: `client/scenes/TableView.tscn`, `client/src/Table/WorldFrame.cs`, `client/src/Table/Palette.cs`, `client/src/Table/SlabNode.cs`, `client/src/Table/PieceNode.cs`, `client/src/Table/TerrainNode.cs`, `client/src/Table/MarkerNode.cs`, `client/src/Table/ZoneNode.cs`, `client/src/Table/OrbitCamera.cs`, `client/src/Table/MeshLoader.cs`, `client/src/Table/TableView.cs`

**Interfaces:**
- Consumes: `Session` (`Game`, `Draft`, `Projection`, `BoardChanged`, `EventsApplied`, `GameChanged`), `AssetCache`, `BuiltinContent`, `ManagementClient.GetManifest/GetDownload` (Tasks 1–7); `Projection`, `ModelInPlay`, `Location`, `OnTable` (Protocol); `TableState`, `TerrainPlacement`, `Marker`, `DeploymentZone`, `VisualRef` (Content); `Base`, `BaseShape`, `Volume`, `Footprint`, `FootprintKind`, `ZoneStyles`, `Settling`, `TableSize`, `Point3`, `Yaw` (Geometry).
- Produces (namespace `Tabletop.Client.Table`): `static class WorldFrame` — `Vector3 ToGodot(Point3)`, `Point3 FromGodot(Vector3)`, `float Radians(Yaw)`, `Yaw YawFrom(float radians)`; `static class Palette` — `Color Team(int ordinal)`, `Color ForDeclarations(TerrainDeclarations)`, `Color Proxy`, `Color Selected`, `Color Ghost`; `partial class SlabNode : Node3D` (`Set(TableSize, VisualRef texture)`); `partial class PieceNode : Node3D` (`Guid ModelId`, `Guid UnitId`, `void SetBody(Base, double height)`, `void SetMesh(Node3D? scene)`, `void SetHighlight(Color?)`, `Aabb Bounds`, the proxy is a `MeshInstance3D` of a `CylinderMesh`/`BoxMesh`/scaled cylinder for circle/square/oval with a translucent material); `partial class TerrainNode : Node3D` (`Guid PlacementId`, `void Set(TerrainPlacement)`: one proxy `MeshInstance3D` per volume, offset, yaw, scale and heights applied, coloured by declaration; `SetMesh`); `partial class MarkerNode : Node3D` (`Guid MarkerId`, `Set(Marker)`: a disc of the diameter, a label); `partial class ZoneNode : Node3D` (`Set(DeploymentZone zone, TableSize size, Color colour)`: `ZoneStyles.Resolve(zone.Shape, size)` → flat `ArrayMesh` fans at 0.02" above the slab, with no frame conversion — `ZoneStyles` already answers in the wire frame; an unknown shape id draws nothing and logs a warning); `partial class OrbitCamera : Camera3D` (orbit with the right mouse button, pan with the middle, zoom with the wheel; `Home` snaps top-down over the table centre; `End` drops to table level; `FocusOn(TableSize)`); `sealed class MeshLoader(AssetCache cache, ManagementClient management, Guid gameId)` — `Task<Node3D?> LoadAssetAsync(Guid assetId, Vector3 cameraPosition, Vector3 piecePosition)` (waits for a manifest fetch in flight; refetches once past the manifest's `ExpiresAt` and once on a miss; then the manifest by asset id, or the owner's own asset → cache → `GltfDocument.AppendFromFile` + `GenerateScene` on the main thread; nearest-first through a priority queue keyed by distance to the camera, each item carrying its own hash and URL; two downloads in flight; a failure logs and returns null so the proxy stays), `Node3D? LoadBuiltin(string name, Volume? fit = null)` (the group mesh `BuiltinContent.TerrainMeshFile` names, when the file exists, wrapped in a node that scales its unit box onto `fit`), `Task RefreshManifest()` (starts a fetch, or returns the one in flight; a failure logs), `static void Orient(Node3D, double scale, Orientation)`; `interface IHasId { Guid Id }`, implemented by `PieceNode`, `TerrainNode`, `MarkerNode` and `ZoneNode`; `partial class TableView : Node3D` — `void Bind(Session)` (once: a second call throws; the subscriptions are fields removed in `_ExitTree`; the manifest is fetched at bind and again when the game's status becomes `playing`), `PieceNode? PieceAt(Guid modelId)`, `TerrainNode? TerrainAt(Guid)`, `MarkerNode? MarkerAt(Guid)`, `IReadOnlyList<WorldVolume> Volumes` (wire frame, from the board or draft), `TableSize Size`, `void Redraw()` (diff the projection or draft against the nodes: add, move, remove), `void ShowExitGhosts(IReadOnlyList<Guid> modelIds, Guid containerId)` and `void ClearExitGhosts()` (the disembark's draggable stand-ins, Task 14), `bool IsExitGhost(Guid modelId)`, `IEnumerable<Node3D> Pieces` (pieces, ghosts, terrain and markers: what picking tests), `OrbitCamera Camera`.
- **A piece is drawn as its hitbox first**: `PieceNode.SetBody` builds the proxy at once; `SetMesh` adds the loaded scene as a child and keeps the proxy at 12% alpha beneath it; a terrain proxy is hidden once its mesh is in, and a later `Set` keeps it hidden; the scene arrives from `MeshLoader` already scaled and rotated by the asset's `Scale` and `Orientation` (`ManifestEntry` for any participant's asset, `AssetDto` for an own asset outside the manifest — `MeshLoader.Orient`). Terrain and markers come from `Draft` during setup and from `Projection.Board.Table` (plus `Projection.Markers` for tokens) from playing on.
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
```

```csharp
using Godot;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

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
```

```csharp
using Godot;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>A model: its base extruded to its height, then the mesh decorating it. The proxy is the thing the rules measure; it never goes away.</summary>
public partial class PieceNode : Node3D, IHasId
{
    public Guid ModelId { get; init; }
    public Guid Id => ModelId;
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
```

```csharp
using Godot;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>One proxy per volume, placed by the volume's offset, yaw and heights under the piece's transform and uniform scale.</summary>
public partial class TerrainNode : Node3D, IHasId
{
    public Guid PlacementId { get; init; }
    public Guid Id => PlacementId;
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
                Visible = _mesh is null, Position = new Vector3((float)v.Offset.X, (float)v.Bottom + height / 2, (float)-v.Offset.Y), Rotation = new Vector3(0, WorldFrame.Radians(v.Yaw), 0) };
            _proxies.Add(node); AddChild(node);
        }
    }

    public void SetMesh(Node3D? scene) { _mesh?.QueueFree(); _mesh = scene; if (scene is not null) { AddChild(scene); foreach (var p in _proxies) p.Visible = false; } }
    public Aabb Bounds => _proxies.Count == 0 ? new Aabb(GlobalPosition, Vector3.One) : _proxies.Skip(1).Aggregate(_proxies[0].GlobalTransform * _proxies[0].GetAabb(), (a, m) => a.Merge(m.GlobalTransform * m.GetAabb()));
}
```

```csharp
using Godot;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>A marker: a disc of its diameter with its label. Markers ignore terrain (setup content).</summary>
public partial class MarkerNode : Node3D, IHasId
{
    public Guid MarkerId { get; init; }
    public Guid Id => MarkerId;
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
public partial class ZoneNode : Node3D, IHasId
{
    public Guid Id { get; private set; }
    public int TeamOrdinal { get; private set; }

    public void Set(DeploymentZone zone, TableSize size, Color colour)
    {
        Id = zone.Id;         TeamOrdinal = zone.TeamOrdinal;
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
```

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

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

