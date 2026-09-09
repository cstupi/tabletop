# Client Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Godot client's frame in `client/`: three screens over one session object, the management HTTP client, the game socket, the projection built by `Tabletop.Protocol.EventApplier`, presence, the table view with hitbox-first pieces and runtime GLB loading into a content-addressed cache, the setup-phase tools and the four terrain generators, the Ask panel, the log panel with undo-to-here, and the right-click menu — with every Godot-free part extracted under `client/src/Core/` and tested headlessly.

**Architecture:** `client/src/Core/**` is Godot-free C# (`Tabletop.Client.Core`): `Session` owns `ManagementClient`, `IGameSocket`, the `EventApplier`, the draft and a `PresenceTable`, and raises events through an `IDispatcher` so Godot code runs on its main thread. `tests/Tabletop.Client.Tests` compiles those sources directly against the three `libs/`, which is what proves they are Godot-free. Godot code (`Tabletop.Client.Screens`, `.Table`, `.Panels`) is thin: it draws from the projection, converts frames with `WorldFrame`, and forwards input to `Session`. Godot physics is not in the scene; picking is ray-versus-AABB over proxy bounds, and every measurement is `Tabletop.Geometry`'s.

**Tech Stack:** Godot 4.7.2 Mono (Windows host), .NET 10 (`net10.0`, C# 12), `System.Net.Http`, `System.Net.WebSockets.ClientWebSocket`, xUnit 2.9.3; `Tabletop.Geometry`, `Tabletop.Content`, `Tabletop.Protocol` from the contracts and geometry plans.

**Spec:** `docs/superpowers/specs/2026-09-04-client-shell-design.md` (primary); `2026-08-30-presence-design.md`; `2026-09-04-setup-phase-design.md`; `2026-08-29-setup-content-design.md`; `2026-08-27-table-domain-model-design.md`; `2026-08-28-game-flow-design.md`; `2026-08-28-resolution-design.md`; `2026-09-03-action-sequences-design.md`; `2026-09-04-scheduler-design.md`; `2026-08-28-asset-library-design.md`; `2026-08-29-geometry-values-design.md`; `2026-09-04-whole-design-review.md` (newest, wins); `docs/architecture.md`; Grimdark Future v3.5.1 pages 7 and 17–18. Earlier plans obeyed verbatim: `docs/superpowers/plans/2026-09-05-contracts-and-local-dev.md`, `2026-09-05-geometry.md`, `2026-09-05-management-api.md`, `2026-09-05-game-server-skeleton.md`. `docs/workstreams/05-client-shell.md` is stale and not followed.

## Global Constraints

- `client/Tabletop.csproj` stays `Godot.NET.Sdk/4.7.2`, `net10.0`, `LangVersion 12`, `Nullable enable`, `ImplicitUsings enable`, `RootNamespace Tabletop`; the SDK globs `**/*.cs` so no file needs registering. `Directory.Build.props` puts `obj/` under `obj/$(OS)/`; the Windows Godot build of the referenced libs produces `obj/Windows_NT/` beside WSL's `obj/Unix/`.
- C# only; no GDScript. `.tscn`/`.tres`/`.godot`/`.import`/`.uid` files are editor-owned: this plan writes minimal `.tscn` files by hand (a root node and its script) and builds every control tree in `_Ready`; nothing reformats a scene the editor has saved.
- **No rules type, physics body or legality check in `client/`** (`architecture.md` → The load-bearing seam; client shell → What must not be in the client). No reference to `Tabletop.GameServer`; no `StaticBody3D`, `RigidBody3D`, `CharacterBody3D`, `Area3D` or `CollisionShape3D` in any scene or script. `scripts/check-client` greps for both (client shell → Testing). Drawing a region the server offered is not a legality check: `MoveRuler.Region` and `MoveRuler.ExitRegion` render parameters the Ask carried, through `Tabletop.Geometry`, and the server re-decides every answer — `LegalSet.Contains` under `strict`, and `GfMoves.ExitPlacement` for a disembark (interpreter plan Task 14). The client never withholds an answer on its own geometry.
- **No truth outside the projection**: screens read `Session` and keep only what they draw. The projection is `Tabletop.Protocol.Projection`, written only by `EventApplier`.
- One table frame everywhere — on the wire, in `Tabletop.Geometry` (`Table`, `ZoneStyles`, `Movement(table:)`) and in this client: origin at the table's corner, `0 ≤ x ≤ width`, `0 ≤ y ≤ length`, Z up (ruled 2026-09-05 by the compatibility review; geometry plan → Global Constraints, game server plan → `DraftEditor.OnSlab`). Nothing converts between table frames. Yaw 0 faces +Y, positive yaw counter-clockwise from above (geometry plan). Godot: `x = X`, `y = Z`, `z = −Y`, `rotation.y = yaw` in radians (`WorldFrame`).
- One world unit = one inch; Godot units are inches too. The slab is drawn 1" thick (setup content → Thickness is a render constant).
- The namespace `Tabletop.Client.Table` shadows the type `Tabletop.Geometry.Table` wherever both are in scope, so every use of the on-slab check is written `Tabletop.Geometry.Table.ContainsPoint(...)` outside `tests/`. Nothing else in the client names a bare `Table` type.
- Every HTTP request carries `Tabletop-Client-Version: {ProtocolVersion.Current}`; a `426` shows the update-required screen and nothing else runs (management API → The version check). The JWT is refreshed with `POST /v1/auth/session/refresh` every 60 s (management plan → Interfaces needed → Client-shell plan).
- Server wins, always: a `models_moved` event snaps the piece; `Undo` shows `EventApplier.ProjectTo(n)` until the server's `undone` or `refused` arrives; reconnect discards the projection and rebuilds from `Welcome`/`Board`/`Events` (client shell → The connection and the projection). A `ProjectionDesyncException` discards it the same way and resyncs immediately, once per sequence position, and the bound sends the player Home with the reason (owner ruling 2, 2026-09-06).
- Presence is sent at 20 Hz while something changed and never logged; setup drags stream `held` placements and the drop is one edit; play drags the same and the drop is one `MoveModels` (presence; setup phase → The draft is the row).
- Local settings (animate, muted window kinds, remember me, server URLs) live in `user://settings.json`; the remembered session in `user://session.bin` (a plain file, owner-only where the platform has file modes — no Windows-only mechanism, per the owner ruling of 2026-09-06); the mesh cache in `user://cache/` keyed by SHA-256 hash.
- **The client is not Windows-only.** Windows is the first export target and the only one this plan builds a preset for, but no design decision here may depend on Windows: `architecture.md` → Deliverables was corrected on 2026-09-06 to say so. `OS.GetUserDataDir()` and `res://`/`user://` are already portable; the one Windows-only mechanism this plan had assumed, `ProtectedData` for the remembered session, is gone (Task 2). WSL plus a Windows Godot editor stays as the description of the development machine, which is a fact about the current machine and not a platform commitment.
- Identifiers are `Guid.CreateVersion7()` (client-generated ids for terrain placements, markers, commands, edits).
- Local only: management at `http://localhost:5100`, the game server at `ws://localhost:5200/ws`, MinIO presigned URLs at `http://localhost:9000`, all over WSL's forwarded localhost (contracts plan → Local development).
- Test-driven for everything with logic: xUnit in `tests/Tabletop.Client.Tests` over `client/src/Core/**`; the Godot layer is tested by running `./scripts/godot` against the local servers. Commit after every task, one-line imperative message, with the `Co-Authored-By` and `Claude-Session` trailers the session requires.

---

## File Structure

```
client/Tabletop.csproj                       modified: ProjectReferences to the three libs
client/scenes/Main.tscn                      modified: root Node (Main.cs) that swaps screens
client/scenes/Login.tscn  Home.tscn  Game.tscn  UpdateRequired.tscn  TableView.tscn   minimal: root + script
client/src/Main.cs                           modified: owns Session, builds the dispatcher, swaps screens
client/src/Core/                             Godot-free; compiled into the test project by source inclusion
  IDispatcher.cs           where Session raises events; ImmediateDispatcher for tests
  ClientSettings.cs        settings.json: animate, muted window kinds, remember me, URLs
  SessionStore.cs          session.bin: the remembered JWT in an owner-only file, no keystore
  ManagementDtos.cs        the management API's request/response shapes the client reads
  ManagementClient.cs      HttpClient over /v1/*: version header, bearer, 426 -> UpdateRequiredException
  IGameSocket.cs           the socket seam: Send, frames in, close reason
  ClientGameSocket.cs      ClientWebSocket over WireJson
  PresenceTable.cs         others' presence by participant
  Session.cs               the session object: login, connect sequence, projection, undo prediction, reconnect, edits
  BuiltinContent.cs        built-in terrain (volumes + declarations), textures, marker visuals, zone styles
  TerrainGenerators.cs     the four planet tables (GF p.18-19) -> a batch of AddTerrain edits beside the slab
  AssetCache.cs            content-addressed disk cache: download, verify hash, path by hash
  Names.cs                 ids -> words (participants, teams, units, models, pieces)
  LogText.cs               GameEvent -> words, with a naming context
  AskViewModel.cs          Ask -> what the panel shows, who answered, mute decisions
  MenuModel.cs             right-click items -> commands
  MoveRuler.cs             ruler readout: distance, remaining allowance, the move region, the disembark region, distance used from the frame
  AnimationQueue.cs        events between the projection and the renderer
client/src/Screens/  GodotDispatcher.cs  LoginScreen.cs  UpdateRequiredScreen.cs  HomeScreen.cs  GameScreen.cs  GameScreen.Play.cs
client/src/Table/    WorldFrame.cs  TableView.cs  SlabNode.cs  PieceNode.cs  TerrainNode.cs  MarkerNode.cs
                     ZoneNode.cs  OrbitCamera.cs  MeshLoader.cs  TableInput.cs  RulerNode.cs  MoveRegionNode.cs  PresenceOverlay.cs
                     Palette.cs  Animator.cs
client/src/Panels/   LobbyPanel.cs  SetupToolbar.cs  AskPanel.cs  LogPanel.cs  PieceMenu.cs  TeamPanel.cs
client/content/builtin/                      textures/*.png (5) and terrain/*.glb (10 group meshes); absence draws the proxy
tests/Tabletop.Client.Tests/                 xUnit; <Compile Include="../../client/src/Core/**/*.cs" />
scripts/check-client                         the two greps: no physics body in a scene, no rules type in a script
scripts/client                               runs the client (./scripts/godot) after building the libs
CLAUDE.md                                    modified: layout, commands, the Core rule
```

Files split by responsibility: `Core/` holds every decision that can be tested without an engine; `Table/` draws; `Panels/` are the UI the game screen composes; `Screens/` are the three scenes plus the update screen. A `Core` file may reference `Tabletop.Geometry`, `Tabletop.Content`, `Tabletop.Protocol` and the BCL, and nothing else.

---
### Task 1: Reference the libs, the client test project and the two greps

**Files:**
- Modify: `client/Tabletop.csproj`, `Tabletop.sln`
- Create: `tests/Tabletop.Client.Tests/Tabletop.Client.Tests.csproj`, `tests/Tabletop.Client.Tests/ReferencesTests.cs`, `scripts/check-client`

**Interfaces:**
- Consumes: `Point2`, `TableSize`, `Table`, `ZoneStyles` (`Tabletop.Geometry`, contracts plan Task 2 and geometry plan Tasks 11 and 13); `TableStateTests.Sample()` (`Tabletop.Content.Tests`).
- Produces: the test project `Tabletop.Client.Tests` compiling `client/src/Core/**/*.cs` against the three libs and the two sample-holding test projects; `./scripts/check-client` exiting non-zero on a physics body in a scene or a rules type in a script.

Spec: `architecture.md` → What is actually shared (the client consumes the three libs); client shell → Testing (two greps). The table frame is one frame — origin at the table's corner — on the wire and in `Tabletop.Geometry` (compatibility ruling of 2026-09-05; geometry plan → Global Constraints; game server plan → `DraftEditor.OnSlab`), so the client converts nothing; the first tests pin that the library's `Table` and `ZoneStyles` answer in the wire frame.

- [ ] **Step 1: Add the ProjectReferences to `client/Tabletop.csproj`** (inside `<Project>`, after the `PropertyGroup`)

```xml
  <ItemGroup>
    <ProjectReference Include="..\libs\Tabletop.Geometry\Tabletop.Geometry.csproj" />
    <ProjectReference Include="..\libs\Tabletop.Content\Tabletop.Content.csproj" />
    <ProjectReference Include="..\libs\Tabletop.Protocol\Tabletop.Protocol.csproj" />
  </ItemGroup>
```

The client takes no `PackageReference` at all. An earlier draft of this plan added
`System.Security.Cryptography.ProtectedData` for the remembered session; that package is
Windows-only and the client may not be (owner ruling, 2026-09-06), so Task 2 uses the BCL's file
APIs instead and nothing outside the three project references is needed.

- [ ] **Step 2: Write the test project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12</LangVersion>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <RootNamespace>Tabletop.Client.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.13.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.0.2" />
  </ItemGroup>
  <ItemGroup>
    <!-- Core is compiled here from source: if it needs Godot, this project does not build. That is the test. -->
    <Compile Include="../../client/src/Core/**/*.cs" LinkBase="Core" />
    <ProjectReference Include="../../libs/Tabletop.Geometry/Tabletop.Geometry.csproj" />
    <ProjectReference Include="../../libs/Tabletop.Content/Tabletop.Content.csproj" />
    <ProjectReference Include="../../libs/Tabletop.Protocol/Tabletop.Protocol.csproj" />
    <ProjectReference Include="../Tabletop.Content.Tests/Tabletop.Content.Tests.csproj" />
    <ProjectReference Include="../Tabletop.Protocol.Tests/Tabletop.Protocol.Tests.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

The two test-project references bring `ArmyTests.Sample()`, `TableStateTests.Sample()` and `Samples` in.

- [ ] **Step 3: Write the tests that pin the references and the one table frame**

```csharp
using Tabletop.Content.Tests;
using Tabletop.Geometry;

namespace Tabletop.Client.Tests;

/// <summary>Smoke tests for Task 1: the three libs and the two sample projects resolve, and the geometry library answers in the wire frame — corner origin, 0 ≤ x ≤ width, 0 ≤ y ≤ length — so nothing in the client converts.</summary>
public class ReferencesTests
{
    static readonly TableSize Opr = new(72, 48);

    [Fact] public void Zone_polygons_are_in_the_wire_frame()
    {
        var zone = ZoneStyles.Resolve("frontline/1", Opr)[0];
        Assert.Equal(36, zone.Vertices.Min(v => v.Y), 6);
        Assert.Equal(48, zone.Vertices.Max(v => v.Y), 6);
        Assert.Equal(0, zone.Vertices.Min(v => v.X), 6);
    }
    [Fact] public void On_slab_is_the_game_servers_rule()
    {
        Assert.True(Table.ContainsPoint(Opr, new Point2(72, 48)));
        Assert.False(Table.ContainsPoint(Opr, new Point2(72.5, 48)));
        Assert.False(Table.ContainsPoint(Opr, new Point2(-1, 10)));
    }
    [Fact] public void The_sample_table_state_is_on_the_slab()
    {
        var sample = TableStateTests.Sample();
        Assert.All(sample.Terrain, t => Assert.True(Table.ContainsPoint(sample.Size, t.Position.Horizontal)));
        Assert.All(sample.Markers, m => Assert.True(Table.ContainsPoint(sample.Size, m.Position.Horizontal)));
    }
}
```

- [ ] **Step 4: Add the test project to the solution and run; expect 3 passed (the references resolve; `client/src/Core/` is still empty, which the `Compile Include` glob tolerates)**

```bash
cd /home/cstupi/src/tabletop
dotnet sln Tabletop.sln add --solution-folder tests tests/Tabletop.Client.Tests/Tabletop.Client.Tests.csproj
dotnet test tests/Tabletop.Client.Tests
```

- [ ] **Step 5: Write `scripts/check-client`** and `chmod +x` it

```bash
#!/usr/bin/env bash
# The client shell spec's two scripted checks: no physics body in any scene, no rules type in any client script.
set -euo pipefail
cd "$(dirname "$0")/.."
status=0
if grep -rnE 'type="(StaticBody3D|RigidBody3D|CharacterBody3D|Area3D|CollisionShape3D|CollisionPolygon3D)"' client/scenes; then
    echo "check-client: a scene contains a physics body or collision shape" >&2; status=1
fi
if grep -rnE '\b(StaticBody3D|RigidBody3D|CharacterBody3D|Area3D|CollisionShape3D|PhysicsServer3D|PhysicsBody3D)\b' client/src; then
    echo "check-client: a client script uses Godot physics" >&2; status=1
fi
# Rules-engine types only. `GameRules` alone is not one: it is a string field on GameInfo and
# PropertiesSummary that the Home screen displays, so matching the bare word is a false positive.
if grep -rnEs 'Tabletop\.GameServer|\bIGameRules\b|\bGameRulesRegistry\b|\bIRulesEngine\b|\bEnforcementPolicy\b|\bLegalSetMembership\b|\bCommandDispatcher\b|\bLegality\b' client/src; then
    echo "check-client: a client script references a rules type" >&2; status=1
fi
if grep -rlns 'using Godot' client/src/Core; then
    echo "check-client: client/src/Core must be Godot-free" >&2; status=1
fi
exit $status
```

- [ ] **Step 6: Run `dotnet test tests/Tabletop.Client.Tests` (expect 3 passed), `dotnet build` (the client builds in WSL with the references), and `./scripts/check-client` (prints nothing, exits 0). Then from the Windows host: `./scripts/godot -e`, let the import finish, build from the editor; confirm `ls libs/Tabletop.Geometry/obj` shows `Unix/` and `Windows_NT/`.**

- [ ] **Step 7: Commit**

```bash
git add client/Tabletop.csproj tests/Tabletop.Client.Tests scripts/check-client Tabletop.sln
git commit -m "Reference the contract libraries from the client and test its Godot-free core"
```

### Task 2: Local settings and the remembered session

**Files:**
- Create: `client/src/Core/ClientSettings.cs`, `client/src/Core/SessionStore.cs`
- Test: `tests/Tabletop.Client.Tests/SettingsTests.cs`

**Interfaces:**
- Produces: `sealed record ClientSettings(bool Animate, IReadOnlySet<string> MutedWindowKinds, bool RememberMe, string ManagementUrl, string GameServerUrl)` with `static ClientSettings Default`, `static ClientSettings Load(string path)` (defaults when the file is missing or unreadable), `void Save(string path)`, `ClientSettings Mute(string windowKind)`, `ClientSettings Unmute(string windowKind)`; `sealed class SessionStore(string path)` with `string? Read()` (null when absent, unreadable, or not JWT-shaped), `void Write(string jwt)`, `void Clear()`.
- **The store is a plain file with owner-only permissions, not an OS keystore** (owner ruling, 2026-09-06: "We may run on other operating systems"). `Write` creates the file with `UnixFileMode.UserRead | UnixFileMode.UserWrite` on any platform that has file modes, and relies on the user profile's own ACL on Windows, where `OS.GetUserDataDir()` already resolves under `%APPDATA%`. **What that protects against:** another local account on a shared machine reading the token, and the file being picked up by a backup or sync of a directory that would otherwise be group- or world-readable. **What it does not protect against:** anything running as the same user, which can read the file, and the file being copied to another machine, where it still refreshes until the Stytch session behind it expires. Windows data protection would have stopped the second of those, at the cost of being Windows-only. The exposure is bounded by the token being a five-minute JWT whose only power is `POST /v1/auth/session/refresh`, by `Clear()` on logout and on a `401`, and by anything running as the user being able to drive the client itself regardless.
- **Rejected: a per-platform store behind one interface** (`ProtectedData` on Windows, libsecret on Linux, Keychain on macOS). Three implementations, two of which cannot be exercised on the development machine, and libsecret needs a session keyring that a minimal or headless Linux desktop may not have — a hard failure mode where the file has none. The seam is still `SessionStore`: three methods and one call site in `Main._Ready`, so a keystore can replace it later without touching `Session`.
- The JWT lives five minutes; what the store keeps alive is the Stytch session behind it — on launch the stored JWT goes to `POST /v1/auth/session/refresh` (Task 3), and a `401` shows the login screen.

Spec: client shell → Screens → Login ("holds the JWT in memory and, if the player chooses, in the OS credential store so the next launch skips the screen"), Asks and windows ("A mute list per player, in local settings"), Animation ("the player's own animate setting, kept in local settings"; whole-design review ruling 3).

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class SettingsTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("tabletop-client-tests").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Defaults_when_missing_and_round_trips()
    {
        var path = Path.Combine(_dir, "settings.json");
        var s = ClientSettings.Load(path);
        Assert.Equal(ClientSettings.Default, s);
        Assert.Equal(("http://localhost:5100", "ws://localhost:5200/ws", true, false), (s.ManagementUrl, s.GameServerUrl, s.Animate, s.RememberMe));
        s = s.Mute("casting") with { Animate = false, RememberMe = true };
        s.Save(path);
        var back = ClientSettings.Load(path);
        Assert.Equal(s, back);
        Assert.Contains("casting", back.MutedWindowKinds);
        Assert.Empty(back.Unmute("casting").MutedWindowKinds);
    }

    [Fact]
    public void Unreadable_settings_fall_back_to_defaults()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ not json");
        Assert.Equal(ClientSettings.Default, ClientSettings.Load(path));
    }

    [Fact]
    public void Session_store_round_trips_owner_only_and_clears()
    {
        var path = Path.Combine(_dir, "session.bin");
        var store = new SessionStore(path);
        Assert.Null(store.Read());
        store.Write("eyJ.jwt.here");
        Assert.Equal("eyJ.jwt.here", store.Read());
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        store.Clear();
        Assert.Null(store.Read());
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void A_session_file_that_is_not_a_jwt_reads_as_null()
    {
        var path = Path.Combine(_dir, "session.bin");
        var store = new SessionStore(path);
        File.WriteAllBytes(path, [1, 2, 3]);
        Assert.Null(store.Read());
        File.WriteAllText(path, "");
        Assert.Null(store.Read());
        File.WriteAllText(path, "eyJ.only-two-segments");
        Assert.Null(store.Read());
    }
}
```

- [ ] **Step 2: Run `dotnet test tests/Tabletop.Client.Tests --filter SettingsTests`; expect compile errors. Write `ClientSettings.cs`**

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tabletop.Client.Core;

/// <summary>Per-player, per-machine settings. Presentation and convenience only; nothing here reaches the server.</summary>
public sealed record ClientSettings(bool Animate, IReadOnlySet<string> MutedWindowKinds, bool RememberMe, string ManagementUrl, string GameServerUrl)
{
    public static ClientSettings Default { get; } = new(true, new HashSet<string>(), false, "http://localhost:5100", "ws://localhost:5200/ws");

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    sealed record Dto(bool? Animate, string[]? MutedWindowKinds, bool? RememberMe, string? ManagementUrl, string? GameServerUrl);

    public static ClientSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return Default;
            var d = JsonSerializer.Deserialize<Dto>(File.ReadAllText(path), Json);
            if (d is null) return Default;
            return new ClientSettings(d.Animate ?? Default.Animate, new HashSet<string>(d.MutedWindowKinds ?? []), d.RememberMe ?? Default.RememberMe,
                d.ManagementUrl ?? Default.ManagementUrl, d.GameServerUrl ?? Default.GameServerUrl);
        }
        catch (Exception e) when (e is JsonException or IOException) { return Default; }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new Dto(Animate, MutedWindowKinds.Order().ToArray(), RememberMe, ManagementUrl, GameServerUrl), Json));
    }

    public ClientSettings Mute(string windowKind) => this with { MutedWindowKinds = new HashSet<string>(MutedWindowKinds) { windowKind } };
    public ClientSettings Unmute(string windowKind) => this with { MutedWindowKinds = MutedWindowKinds.Where(k => k != windowKind).ToHashSet() };

    public bool Equals(ClientSettings? other) => other is not null && Animate == other.Animate && RememberMe == other.RememberMe
        && ManagementUrl == other.ManagementUrl && GameServerUrl == other.GameServerUrl && MutedWindowKinds.SetEquals(other.MutedWindowKinds);
    public override int GetHashCode() => HashCode.Combine(Animate, RememberMe, ManagementUrl, GameServerUrl, MutedWindowKinds.Count);
}
```

- [ ] **Step 3: Write `SessionStore.cs`**

```csharp
using System.Text;

namespace Tabletop.Client.Core;

/// <summary>The remembered session JWT, in a file only its owner can read. Absent, unreadable, or not JWT-shaped reads as null.
/// Deliberately not an OS keystore: the client may run on more than one operating system (owner ruling, 2026-09-06), and a
/// portable file is honest about being weaker. It keeps the token from other local accounts; it does not keep it from anything
/// running as this user, nor from being copied to another machine, where it refreshes until the Stytch session expires.</summary>
public sealed class SessionStore(string path)
{
    public string? Read()
    {
        try
        {
            if (!File.Exists(path)) return null;
            var text = Encoding.UTF8.GetString(File.ReadAllBytes(path));
            // A JWT is three non-empty dot-separated segments. Anything else is a truncated write or an unrelated
            // file, and must not reach an Authorization header: refusing it here is what "a corrupt session reads as null" means
            // now that nothing decrypts.
            var parts = text.Split('.');
            return parts.Length == 3 && parts.All(p => p.Length > 0) ? text : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    public void Write(string jwt)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        // Set the mode at creation, so there is no instant where the file is readable by anyone else. Windows has no
        // file modes and throws on this property; there the user profile directory's own ACL is the protection.
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var file = new FileStream(path, options);
        file.Write(Encoding.UTF8.GetBytes(jwt));
    }

    public void Clear() { if (File.Exists(path)) File.Delete(path); }
}
```

- [ ] **Step 4: Run `dotnet test tests/Tabletop.Client.Tests`; expect 7 passed. Commit: `Add the client's local settings and the remembered session store`.**

### Task 3: The management client

**Files:**
- Create: `client/src/Core/ManagementDtos.cs`, `client/src/Core/ManagementClient.cs`
- Test: `tests/Tabletop.Client.Tests/FakeManagement.cs`, `tests/Tabletop.Client.Tests/ManagementClientTests.cs`

**Interfaces:**
- Consumes: `ProtocolVersion.Header/Current/UpgradeRequiredStatusCode`, `GameInfo`, `GameStatus`, `SchedulerShape`, `Enforcement` (Protocol); `Army`, `TableState`, `TerrainVolume`, `ContentJson` (Content); `TableSize`, `Orientation`, `Base` (Geometry); the management plan's HTTP surface (its *Interfaces → Produces → HTTP surface* table).
- Produces (namespace `Tabletop.Client.Core`): mirrors of the management DTOs the client reads, byte-compatible under `ContentJson` — `AccountRow(Guid Id, string Name)`, `SessionResponse(string SessionJwt, AccountRow Account)`, `OtpSentResponse(string MethodId)`, `Page<T>(IReadOnlyList<T> Items, Guid? Next)`, `GameSummary(Guid Id, string Name, GameStatus Status, Guid CreatedBy, DateTimeOffset CreatedAt, int Participants)`, `CreateGameRequest(string Name, string PropertiesHash, SchedulerShape SchedulerShape, Enforcement Enforcement, bool WaitsBetweenSteps)`, `PropertiesSummary(string ContentHash, string Name, string GameRules)`, `ArmySummary(Guid Id, string Name, string GameSystem, string Source, int Units, int Models)`, `LayoutSummary(Guid Id, string Name, TableSize Size, int Terrain, int Markers)`, `AssetKind { Miniature, Terrain, Texture }`, `AssetDto(Guid Id, string Name, AssetKind Kind, string Hash, long ByteSize, string ContentType, bool Uploaded, double Scale, Orientation Orientation, Base? ModelledBase)`, `DownloadResponse(Uri Url, DateTimeOffset ExpiresAt)`, `ManifestEntry(Guid AssetId, AssetKind Kind, string Hash, Uri Url, double Scale, Orientation Orientation)` (the owner's import corrections ride on the manifest — management plan Task 12), `Manifest(IReadOnlyList<ManifestEntry> Assets, DateTimeOffset ExpiresAt)`; `ManagementException(int Status, string Title, string? Detail)`; `UpdateRequiredException : ManagementException`.
- `sealed class ManagementClient(HttpClient http, Uri baseUrl)` with `string? Jwt { get; set; }` and: `SendOtp(email)`, `VerifyOtp(methodId, code)`, `LoginWithPassword(email, password)`, `SignUp(email, password)`, `Refresh(jwt)`, `Logout()`, `GetAccount()`, `ListGames(Guid? after, int limit = 50)`, `CreateGame(CreateGameRequest)`, `GetGame(Guid)`, `JoinGame(Guid)`, `LeaveGame(Guid)`, `ListProperties()`, `ListArmies(Guid? after)`, `GetArmy(Guid)`, `ListLayouts()`, `GetLayout(Guid)`, `SaveLayout(TableState)`, `GetManifest(Guid gameId)`, `ListAssets(AssetKind kind, Guid? after)`, `GetAssetVolumes(Guid)`, `GetDownload(Guid)` — all `Task<T>`, all throwing `ManagementException` on a non-2xx with the problem body's title and detail, `UpdateRequiredException` on `426`.

Spec: client shell → Screens (login by password or one-time code through management; Home lists `GET /v1/games`, creates with `GET /v1/properties` and `GET /v1/armies`; the version header on every request; Leave calls `POST /v1/games/{id}/leave`); setup phase → Layouts (`POST /v1/layouts`, `GET /v1/layouts/{id}`); management API → The version check; management plan → HTTP surface (the exact paths and shapes; `Page<T>` with `?limit&after`; problem+json errors; `POST /v1/auth/session/refresh` accepts an expired JWT).

- [ ] **Step 1: Write `FakeManagement.cs` — a handler that records requests and answers from a script**

```csharp
using System.Net;
using System.Text;
using Tabletop.Content;

namespace Tabletop.Client.Tests;

public sealed class FakeManagement : HttpMessageHandler
{
    public readonly List<HttpRequestMessage> Requests = new();
    public readonly List<string> Bodies = new();
    readonly Queue<HttpResponseMessage> _answers = new();

    public FakeManagement Answer(HttpStatusCode status, object? body = null, string mediaType = "application/json")
    {
        var content = body is null ? null : new StringContent(body as string ?? ContentJson.Serialize(body), Encoding.UTF8, mediaType);
        _answers.Enqueue(new HttpResponseMessage(status) { Content = content });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
        return _answers.Count > 0 ? _answers.Dequeue() : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
    }
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
using System.Net;
using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class ManagementClientTests
{
    static (ManagementClient Client, FakeManagement Fake) Make() { var f = new FakeManagement(); return (new ManagementClient(new HttpClient(f), new Uri("http://localhost:5100")), f); }

    [Fact]
    public async Task Every_request_carries_the_version_header_and_the_bearer_when_logged_in()
    {
        var (c, f) = Make();
        f.Answer(HttpStatusCode.OK, new AccountRow(Guid.CreateVersion7(), "Clay"));
        c.Jwt = "jwt-1";
        await c.GetAccount();
        var r = f.Requests.Single();
        Assert.Equal(ProtocolVersion.Current.ToString(), r.Headers.GetValues(ProtocolVersion.Header).Single());
        Assert.Equal("Bearer jwt-1", r.Headers.Authorization!.ToString());
        Assert.Equal("http://localhost:5100/v1/account", r.RequestUri!.ToString());
    }

    [Fact]
    public async Task Login_paths_and_shapes()
    {
        var (c, f) = Make();
        var account = new AccountRow(Guid.CreateVersion7(), "clay");
        f.Answer(HttpStatusCode.OK, new OtpSentResponse("email-1")).Answer(HttpStatusCode.OK, new SessionResponse("jwt-2", account))
         .Answer(HttpStatusCode.OK, new SessionResponse("jwt-3", account)).Answer(HttpStatusCode.Created, new SessionResponse("jwt-4", account))
         .Answer(HttpStatusCode.OK, new SessionResponse("jwt-5", account)).Answer(HttpStatusCode.NoContent);
        Assert.Equal("email-1", (await c.SendOtp("clay@example.com")).MethodId);
        Assert.Equal("jwt-2", (await c.VerifyOtp("email-1", "123456")).SessionJwt);
        Assert.Equal("jwt-3", (await c.LoginWithPassword("clay@example.com", "pw")).SessionJwt);
        Assert.Equal("jwt-4", (await c.SignUp("clay@example.com", "pw")).SessionJwt);
        Assert.Equal("jwt-5", (await c.Refresh("jwt-old")).SessionJwt);
        c.Jwt = "jwt-5"; await c.Logout();
        Assert.Equal(["/v1/auth/otp", "/v1/auth/otp/verify", "/v1/auth/password", "/v1/auth/password/signup", "/v1/auth/session/refresh", "/v1/auth/session"], f.Requests.Select(r => r.RequestUri!.AbsolutePath));
        Assert.Equal(HttpMethod.Delete, f.Requests[5].Method);
        Assert.Contains("\"methodId\":\"email-1\"", f.Bodies[1]);
        Assert.Null(f.Requests[0].Headers.Authorization);
    }

    [Fact]
    public async Task A_426_is_the_update_required_exception_and_problems_carry_their_title()
    {
        var (c, f) = Make();
        f.Answer((HttpStatusCode)426, "{\"title\":\"Update required\",\"detail\":\"This server accepts Tabletop-Client-Version >= 2.\",\"status\":426}", "application/problem+json");
        var up = await Assert.ThrowsAsync<UpdateRequiredException>(() => c.ListProperties());
        Assert.Equal(426, up.Status);
        f.Answer(HttpStatusCode.Unauthorized, "{\"title\":\"Unauthorized\",\"detail\":\"unauthorized_credentials\",\"status\":401}", "application/problem+json");
        var ex = await Assert.ThrowsAsync<ManagementException>(() => c.LoginWithPassword("a", "b"));
        Assert.Equal((401, "Unauthorized", "unauthorized_credentials"), (ex.Status, ex.Title, ex.Detail));
    }

    [Fact]
    public async Task Games_layouts_and_the_manifest_round_trip_contract_types()
    {
        var (c, f) = Make();
        var game = Samples.Game;
        var layout = Tabletop.Content.Tests.TableStateTests.Sample();
        f.Answer(HttpStatusCode.OK, new Page<GameSummary>([new GameSummary(game.Id, "Friday", GameStatus.Lobby, game.CreatedBy, DateTimeOffset.UnixEpoch, 1)], game.Id))
         .Answer(HttpStatusCode.Created, game).Answer(HttpStatusCode.OK, game).Answer(HttpStatusCode.NoContent)
         .Answer(HttpStatusCode.Created, layout).Answer(HttpStatusCode.OK, new Manifest([new ManifestEntry(Guid.CreateVersion7(), AssetKind.Miniature, new string('a', 64), new Uri("http://localhost:9000/b/k"), 2.5, new Tabletop.Geometry.Orientation(90, 0, 0))], DateTimeOffset.UnixEpoch));
        var page = await c.ListGames(null, 25);
        Assert.Equal("http://localhost:5100/v1/games?limit=25", f.Requests[0].RequestUri!.ToString());
        Assert.Equal(game.Id, page.Next);
        Assert.Equal(ContentJson.Serialize(game), ContentJson.Serialize(await c.CreateGame(new CreateGameRequest("Friday", "sha256:abc", SchedulerShape.AlternatingActivation, Enforcement.Warn, false))));
        Assert.Contains("\"schedulerShape\":\"alternating-activation\"", f.Bodies[1]);
        await c.JoinGame(game.Id); await c.LeaveGame(game.Id);
        Assert.Equal($"/v1/games/{game.Id}/leave", f.Requests[3].RequestUri!.AbsolutePath);
        Assert.Equal(ContentJson.Serialize(layout), ContentJson.Serialize(await c.SaveLayout(layout)));
        var entry = (await c.GetManifest(game.Id)).Assets.Single();
        Assert.Equal((AssetKind.Miniature, 2.5, new Tabletop.Geometry.Orientation(90, 0, 0)), (entry.Kind, entry.Scale, entry.Orientation));
        Assert.Equal($"/v1/games/{game.Id}/assets", f.Requests[5].RequestUri!.AbsolutePath);
    }
}
```

- [ ] **Step 3: Run; expect compile errors. Write `ManagementDtos.cs`**

```csharp
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

// Mirrors of Tabletop.Management's DTOs (management plan → Interfaces → Produces). Same property names, same JSON under ContentJson.
public sealed record AccountRow(Guid Id, string Name);
public sealed record SessionResponse(string SessionJwt, AccountRow Account);
public sealed record OtpSentResponse(string MethodId);
public sealed record Page<T>(IReadOnlyList<T> Items, Guid? Next);
public sealed record GameSummary(Guid Id, string Name, GameStatus Status, Guid CreatedBy, DateTimeOffset CreatedAt, int Participants);
public sealed record CreateGameRequest(string Name, string PropertiesHash, SchedulerShape SchedulerShape, Enforcement Enforcement, bool WaitsBetweenSteps);
public sealed record PropertiesSummary(string ContentHash, string Name, string GameRules);
public sealed record ArmySummary(Guid Id, string Name, string GameSystem, string Source, int Units, int Models);
public sealed record LayoutSummary(Guid Id, string Name, TableSize Size, int Terrain, int Markers);
public enum AssetKind { Miniature, Terrain, Texture }
public sealed record AssetDto(Guid Id, string Name, AssetKind Kind, string Hash, long ByteSize, string ContentType, bool Uploaded, double Scale, Orientation Orientation, Base? ModelledBase);
public sealed record DownloadResponse(Uri Url, DateTimeOffset ExpiresAt);
public sealed record ManifestEntry(Guid AssetId, AssetKind Kind, string Hash, Uri Url, double Scale, Orientation Orientation);
public sealed record Manifest(IReadOnlyList<ManifestEntry> Assets, DateTimeOffset ExpiresAt);

/// <summary>A non-2xx answer: the problem+json title and detail when the body had them.</summary>
public class ManagementException(int status, string title, string? detail) : Exception(detail is null ? title : $"{title}: {detail}")
{
    public int Status { get; } = status;
    public string Title { get; } = title;
    public string? Detail { get; } = detail;
}

/// <summary>426: this build is older than the server accepts. The client shows the update screen and stops.</summary>
public sealed class UpdateRequiredException(string title, string? detail) : ManagementException(ProtocolVersion.UpgradeRequiredStatusCode, title, detail);
```

- [ ] **Step 4: Write `ManagementClient.cs`**

```csharp
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>The one HTTP client. Every request carries the version header; the bearer is set once a session exists.</summary>
public sealed class ManagementClient(HttpClient http, Uri baseUrl)
{
    public string? Jwt { get; set; }

    // Auth — anonymous (the bearer is not sent).
    public Task<OtpSentResponse> SendOtp(string email) => Post<OtpSentResponse>("/v1/auth/otp", new { email }, anonymous: true);
    public Task<SessionResponse> VerifyOtp(string methodId, string code) => Post<SessionResponse>("/v1/auth/otp/verify", new { methodId, code }, anonymous: true);
    public Task<SessionResponse> LoginWithPassword(string email, string password) => Post<SessionResponse>("/v1/auth/password", new { email, password }, anonymous: true);
    public Task<SessionResponse> SignUp(string email, string password) => Post<SessionResponse>("/v1/auth/password/signup", new { email, password }, anonymous: true);
    public Task<SessionResponse> Refresh(string sessionJwt) => Post<SessionResponse>("/v1/auth/session/refresh", new { sessionJwt }, anonymous: true);
    public Task Logout() => Send(HttpMethod.Delete, "/v1/auth/session", null, false);

    public Task<AccountRow> GetAccount() => Get<AccountRow>("/v1/account");
    public Task<Page<GameSummary>> ListGames(Guid? after, int limit = 50) => Get<Page<GameSummary>>($"/v1/games?limit={limit}" + (after is null ? "" : $"&after={after}"));
    public Task<GameInfo> CreateGame(CreateGameRequest request) => Post<GameInfo>("/v1/games", request);
    public Task<GameInfo> GetGame(Guid id) => Get<GameInfo>($"/v1/games/{id}");
    public Task<GameInfo> JoinGame(Guid id) => Post<GameInfo>($"/v1/games/{id}/join", new { });
    public Task LeaveGame(Guid id) => Send(HttpMethod.Post, $"/v1/games/{id}/leave", null, false);
    public Task<IReadOnlyList<PropertiesSummary>> ListProperties() => Get<IReadOnlyList<PropertiesSummary>>("/v1/properties");
    public Task<Page<ArmySummary>> ListArmies(Guid? after) => Get<Page<ArmySummary>>("/v1/armies?limit=200" + (after is null ? "" : $"&after={after}"));
    public Task<Army> GetArmy(Guid id) => Get<Army>($"/v1/armies/{id}");
    public Task<IReadOnlyList<LayoutSummary>> ListLayouts() => Get<IReadOnlyList<LayoutSummary>>("/v1/layouts");
    public Task<TableState> GetLayout(Guid id) => Get<TableState>($"/v1/layouts/{id}");
    public Task<TableState> SaveLayout(TableState layout) => Post<TableState>("/v1/layouts", layout);
    public Task<Manifest> GetManifest(Guid gameId) => Get<Manifest>($"/v1/games/{gameId}/assets");
    public Task<Page<AssetDto>> ListAssets(AssetKind kind, Guid? after) => Get<Page<AssetDto>>($"/v1/assets?limit=200&kind={kind.ToString().ToLowerInvariant()}" + (after is null ? "" : $"&after={after}"));
    public Task<IReadOnlyList<TerrainVolume>> GetAssetVolumes(Guid assetId) => Get<IReadOnlyList<TerrainVolume>>($"/v1/assets/{assetId}/volumes");
    public Task<DownloadResponse> GetDownload(Guid assetId) => Get<DownloadResponse>($"/v1/assets/{assetId}/download");

    async Task<T> Get<T>(string path) => Read<T>(await Send(HttpMethod.Get, path, null, false));
    async Task<T> Post<T>(string path, object body, bool anonymous = false) => Read<T>(await Send(HttpMethod.Post, path, body, anonymous));

    async Task<string> Send(HttpMethod method, string path, object? body, bool anonymous)
    {
        using var request = new HttpRequestMessage(method, new Uri(baseUrl, path));
        request.Headers.Add(ProtocolVersion.Header, ProtocolVersion.Current.ToString());
        if (!anonymous && Jwt is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Jwt);
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body, body.GetType(), ContentJson.Options), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode) return text;
        var (title, detail) = Problem(text, response.ReasonPhrase ?? response.StatusCode.ToString());
        if ((int)response.StatusCode == ProtocolVersion.UpgradeRequiredStatusCode) throw new UpdateRequiredException(title, detail);
        throw new ManagementException((int)response.StatusCode, title, detail);
    }

    static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, ContentJson.Options) ?? throw new ManagementException(0, "Empty response", null);

    static (string Title, string? Detail) Problem(string text, string fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var title = doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() : null;
            var detail = doc.RootElement.TryGetProperty("detail", out var d) ? d.GetString() : null;
            return (title ?? fallback, detail);
        }
        catch (JsonException) { return (fallback, text.Length == 0 ? null : text); }
    }
}
```

- [ ] **Step 5: Run `dotnet test tests/Tabletop.Client.Tests`; expect 11 passed. Commit: `Add the management client with the version header and problem mapping`.**

### Task 4: The socket seam, the real socket, and the presence table

**Files:**
- Create: `client/src/Core/IGameSocket.cs`, `client/src/Core/ClientGameSocket.cs`, `client/src/Core/PresenceTable.cs`
- Test: `tests/Tabletop.Client.Tests/FakeGameSocket.cs`, `tests/Tabletop.Client.Tests/PresenceTableTests.cs`

**Interfaces:**
- Consumes: `SocketMessage`, `WireJson`, `Presence` (Protocol).
- Produces: `sealed record SocketClosed(int? Code, string? Reason, Exception? Error)`; `interface IGameSocket : IAsyncDisposable { event Action<SocketMessage> FrameReceived; event Action<SocketClosed> Closed; Task ConnectAsync(Uri url, CancellationToken ct); Task SendAsync(SocketMessage frame); Task CloseAsync(); }`; `interface IGameSocketFactory { IGameSocket Create(); }`; `sealed class ClientGameSocket : IGameSocket` over `ClientWebSocket` (text frames, one `SocketMessage` per frame, a read loop that raises `FrameReceived` on its own thread and `Closed` once); `sealed class ClientGameSocketFactory : IGameSocketFactory`; `sealed class PresenceTable` — `void Set(Presence)`, `void Remove(Guid participantId)`, `Presence? Of(Guid)`, `IReadOnlyCollection<Presence> All`, `void Clear()`. Test infrastructure `FakeGameSocket` (`Sent`, `Deliver(SocketMessage)`, `Drop(int? code, string? reason)`, `Connected`) and `FakeSocketFactory` (`Created` list).
- Both events fire off the Godot main thread; `Session` (Task 5) marshals them through `IDispatcher`. The game server's protocol (game server plan → Interfaces needed → Client shell plan): one text frame per message, `Connect` first, close codes `1000` (normal, or "replaced by a newer connection"), `1001` (server shutting down), `1002` (protocol error).

Spec: `architecture.md` → Transport (WebSockets; Godot's high-level multiplayer is not used); presence → Nothing here is written down (a presence value exists while a socket is open; the table is cleared on close), Connectedness is not a field (who is connected is which values exist).

- [ ] **Step 1: Write `IGameSocket.cs` and `PresenceTable.cs`**

```csharp
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

public sealed record SocketClosed(int? Code, string? Reason, Exception? Error);

/// <summary>One game socket. Events are raised on the socket's own thread; the Session posts them to the main thread.</summary>
public interface IGameSocket : IAsyncDisposable
{
    event Action<SocketMessage> FrameReceived;
    event Action<SocketClosed> Closed;
    Task ConnectAsync(Uri url, CancellationToken ct);
    Task SendAsync(SocketMessage frame);
    Task CloseAsync();
}

public interface IGameSocketFactory { IGameSocket Create(); }
```

```csharp
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>Other participants' latest presence. Existence is connectedness; cleared when our own socket closes.</summary>
public sealed class PresenceTable
{
    readonly Dictionary<Guid, Presence> _values = new();
    public void Set(Presence value) => _values[value.ParticipantId] = value;
    public void Remove(Guid participantId) => _values.Remove(participantId);
    public Presence? Of(Guid participantId) => _values.GetValueOrDefault(participantId);
    public IReadOnlyCollection<Presence> All => _values.Values;
    public void Clear() => _values.Clear();
}
```

- [ ] **Step 2: Write `ClientGameSocket.cs`**

```csharp
using System.Net.WebSockets;
using System.Text;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

public sealed class ClientGameSocket : IGameSocket
{
    const int MaxFrameBytes = 4 * 1024 * 1024;
    readonly ClientWebSocket _socket = new();
    readonly SemaphoreSlim _sendGate = new(1, 1);
    readonly CancellationTokenSource _stop = new();
    int _closedRaised;

    public event Action<SocketMessage>? FrameReceived;
    public event Action<SocketClosed>? Closed;

    public async Task ConnectAsync(Uri url, CancellationToken ct)
    {
        await _socket.ConnectAsync(url, ct);
        _ = Task.Run(ReadLoopAsync);
    }

    public async Task SendAsync(SocketMessage frame)
    {
        var bytes = Encoding.UTF8.GetBytes(WireJson.Serialize(frame));
        await _sendGate.WaitAsync();
        try { if (_socket.State == WebSocketState.Open) await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, _stop.Token); }
        finally { _sendGate.Release(); }
    }

    public async Task CloseAsync()
    {
        try { if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived) await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); }
        catch (WebSocketException) { }
        _stop.Cancel();
        RaiseClosed(new SocketClosed(1000, "bye", null));
    }

    async Task ReadLoopAsync()
    {
        var buffer = new byte[64 * 1024];
        using var frame = new MemoryStream();
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                frame.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(buffer, _stop.Token);
                    if (result.MessageType == WebSocketMessageType.Close) { RaiseClosed(new SocketClosed((int?)_socket.CloseStatus, _socket.CloseStatusDescription, null)); return; }
                    frame.Write(buffer, 0, result.Count);
                    if (frame.Length > MaxFrameBytes) throw new WebSocketException("frame too large");
                } while (!result.EndOfMessage);
                FrameReceived?.Invoke(WireJson.Deserialize(Encoding.UTF8.GetString(frame.GetBuffer(), 0, (int)frame.Length)));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is WebSocketException or System.Text.Json.JsonException or IOException) { RaiseClosed(new SocketClosed(null, ex.Message, ex)); }
    }

    void RaiseClosed(SocketClosed closed) { if (Interlocked.Exchange(ref _closedRaised, 1) == 0) Closed?.Invoke(closed); }

    public async ValueTask DisposeAsync() { _stop.Cancel(); await Task.Yield(); _socket.Dispose(); }
}

public sealed class ClientGameSocketFactory : IGameSocketFactory { public IGameSocket Create() => new ClientGameSocket(); }
```

- [ ] **Step 3: Write `FakeGameSocket.cs` and the presence table test**

```csharp
using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public sealed class FakeGameSocket : IGameSocket
{
    public readonly List<SocketMessage> Sent = new();
    public bool Connected { get; private set; }
    public Uri? Url { get; private set; }
    public event Action<SocketMessage>? FrameReceived;
    public event Action<SocketClosed>? Closed;
    public Task ConnectAsync(Uri url, CancellationToken ct) { Url = url; Connected = true; return Task.CompletedTask; }
    public Task SendAsync(SocketMessage frame) { Sent.Add(frame); return Task.CompletedTask; }
    public Task CloseAsync() { Connected = false; Closed?.Invoke(new SocketClosed(1000, "bye", null)); return Task.CompletedTask; }
    public void Deliver(SocketMessage frame) => FrameReceived?.Invoke(frame);
    public void Drop(int? code, string? reason) { Connected = false; Closed?.Invoke(new SocketClosed(code, reason, null)); }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class FakeSocketFactory : IGameSocketFactory
{
    public readonly List<FakeGameSocket> Created = new();
    public FakeGameSocket Last => Created[^1];
    public IGameSocket Create() { var s = new FakeGameSocket(); Created.Add(s); return s; }
}
```

```csharp
using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class PresenceTableTests
{
    [Fact]
    public void Latest_wins_and_removal_ends_existence()
    {
        var t = new PresenceTable();
        var p = Guid.CreateVersion7();
        t.Set(new Presence(p, new Cursor(new Point3(1, 1, 0), null), null, [], null));
        t.Set(new Presence(p, new Cursor(new Point3(2, 2, 0), null), null, [], null));
        Assert.Equal(2, t.Of(p)!.Cursor!.Point.X);
        Assert.Single(t.All);
        t.Remove(p);
        Assert.Null(t.Of(p)); Assert.Empty(t.All);
    }
}
```

- [ ] **Step 4: Run `dotnet test tests/Tabletop.Client.Tests`; expect 12 passed. Commit: `Add the game socket seam, the ClientWebSocket adapter and the presence table`.**

### Task 5: The session object

**Files:**
- Create: `client/src/Core/IDispatcher.cs`, `client/src/Core/Session.cs`
- Test: `tests/Tabletop.Client.Tests/SessionTests.cs`

**Interfaces:**
- Consumes: Tasks 2–4; `EventApplier`, `Projection`, `GameInfo`, `Board`, `Welcome`, `GameInfoMessage`, `BoardMessage`, `EventsMessage`, `PresenceMessage`, `PingMessage`, `PresenceEnded`, `Refused`, `RefusalReason`, `Rejected`, `RejectReason`, `Connect`, `CommandMessage`, `EditMessage`, `Command`, `Edit`, `Undo`, `Undone`, `AtRest`, `ProjectionDesyncException`, `ProtocolVersion` (Protocol); `TableState` (Content).
- Produces: `interface IDispatcher { void Post(Action action); }`, `sealed class ImmediateDispatcher : IDispatcher`; `enum SessionPhase { LoggedOut, LoggedIn, Connecting, Connected, Reconnecting, UpdateRequired }`; `sealed class Session(ManagementClient management, IGameSocketFactory sockets, SessionStore store, ClientSettings settings, Action<ClientSettings> persistSettings, IDispatcher dispatcher, Func<int, TimeSpan>? backoff = null)` with:
  - state: `SessionPhase Phase`, `AccountRow? Account`, `ClientSettings Settings`, `Guid? GameId`, `Guid? ParticipantId`, `GameInfo? Game`, `TableState? Draft`, `Projection? Projection` (the undo prediction while one is pending), `IReadOnlyList<GameEvent> Log`, `EventApplier? Applier`, `PresenceTable Others`, `Guid? MyTeamId`, `bool IsCreator`, `long? PendingUndoTo`, `string? LastCloseReason`;
  - events (raised on the dispatcher): `PhaseChanged`, `GameChanged`, `BoardChanged`, `EventsApplied(IReadOnlyList<GameEvent>)`, `PresenceChanged(Guid)`, `Pinged(Ping)`, `RefusedReceived(Refused)`, `ConnectionLost(SocketClosed, bool willReconnect)`;
  - login: `Task LoginWithPasswordAsync(email, password)`, `Task SignUpAsync(email, password)`, `Task<string> SendOtpAsync(email)`, `Task VerifyOtpAsync(methodId, code)`, `Task<bool> TryResumeAsync()`, `Task RefreshAsync()`, `Task LogoutAsync()`, `void UpdateSettings(Func<ClientSettings, ClientSettings>)`;
  - game: `Task ConnectAsync(Guid gameId)`, `Task DisconnectAsync()`, `Task LeaveAsync()`, `Task<Guid> SendCommandAsync(Command)`, `Task<Guid> SendEditAsync(Edit)`, `Task UndoToAsync(long seq)`, `Task SendPresenceAsync(Presence)`, `Task SendPingAsync(Ping)`.
- Rules: `Rejected(UpgradeRequired)` and `UpdateRequiredException` both set `Phase = UpdateRequired`. A close the user did not ask for reconnects with backoff (1, 2, 5, 10, 10… s) **except** code `1000` after a `Welcome` (the server replaced this connection — `ConnectionLost(willReconnect: false)`) and any `Rejected`. Reconnect is `ConnectAsync` again: the applier, draft and presence table are discarded first. `UndoToAsync` sends `Undo`, keeps `PendingUndoTo`, and `Projection` returns `Applier.ProjectTo(seq)` until an `undone` event with that `ToSeq` arrives or the command is `Refused`.
- Rules, the desync (owner ruling 2, 2026-09-06): a `ProjectionDesyncException` out of `ApplyAll` or `ProjectTo` means this copy of the projection is unusable. `OnDesync` drops it, closes the socket and connects again, so the rebuild is the ordinary hydrate. The bound is **one attempt per sequence position**: `_desyncFloor` records `Projection.LastSeq` at the fault, a fault that reached past the floor gets its own attempt, and a fault at or before the floor is the same bad event out of the server's own log and instead sets `Phase = LoggedIn` with `LastCloseReason`. Both outcomes raise `ConnectionLost` with `SocketClosed.Error` carrying the exception, which is how `GameScreen` tells a desync from a dropped socket. The resync has no backoff, because time does not fix a bad event. `ConnectAsync` gains a private `resync` parameter: it forces `Reconnecting` and keeps `_desyncFloor` across the reset, and the public one-argument signature is unchanged.
- Rules, an uninteresting kind: `Handle`'s switch has **no default arm**, so a kind this client does not act on is dropped after a successful deserialization. `AtRest(long Seq)` is that kind today (owner ruling 4, 2026-09-06). Nothing in the client waits on the server coming to rest: every panel draws from the projection as events arrive, and the only busy state is `Connecting`/`Reconnecting`, which `Welcome` ends. Acting on it would add a second, weaker source of truth for "the table is settled".

Spec: client shell → Structure (one session object outlives every screen; screens hold no truth), The connection and the projection (the connect sequence; undo local first; reconnect is connect; presence and setup edits bypass the projection; server wins), Screens (Home disconnects, Leave is a menu action calling `/leave`); accounts → Disconnecting is not leaving; game server plan → Interfaces needed → Client shell plan (Connect first; Welcome, Board, Events; `Rejected` then close; replaced sockets close with `1000`; `Refused.MessageId` echoes the id); management plan → Client-shell plan (refresh once a minute).

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using Tabletop.Client.Core;
using Tabletop.Content.Tests;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class SessionTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("tabletop-session-tests").FullName;
    readonly FakeManagement _http = new();
    readonly FakeSocketFactory _sockets = new();
    readonly List<ClientSettings> _persisted = new();
    readonly Session _s;
    // Read the sample game once: it is an expression-bodied property that mints fresh Guids for the game id
    // and the account id on every access, so two reads are two different games.
    static readonly GameInfo Game = Samples.Game;
    static readonly AccountRow Clay = new(Game.Participants[0].AccountId, "Clay");
    // Three dot-separated segments, because SessionStore.Read refuses anything that is not JWT-shaped (Task 2).
    const string Jwt1 = "a.jwt-1.z";

    public SessionTests()
    {
        var store = new SessionStore(Path.Combine(_dir, "session.bin"));
        _s = new Session(new ManagementClient(new HttpClient(_http), new Uri("http://localhost:5100")), _sockets, store,
            ClientSettings.Default with { RememberMe = true }, _persisted.Add, new ImmediateDispatcher(), _ => TimeSpan.Zero);
    }
    public void Dispose() => Directory.Delete(_dir, true);

    async Task LoggedIn() { _http.Answer(HttpStatusCode.OK, new SessionResponse(Jwt1, Clay)); await _s.LoginWithPasswordAsync("c@example.com", "pw"); }
    async Task<FakeGameSocket> ConnectedAtPlaying()
    {
        await LoggedIn();
        await _s.ConnectAsync(Game.Id);
        var sock = _sockets.Last;
        sock.Deliver(new Welcome(Samples.P1, Game));
        sock.Deliver(new BoardMessage(Samples.Board));
        sock.Deliver(new EventsMessage([new GameEvent(1, Samples.P1, new ScoreSet(Samples.Team1, 1), null), new GameEvent(2, Samples.P1, new ScoreSet(Samples.Team1, 2), null)]));
        return sock;
    }

    [Fact]
    public async Task Login_stores_the_jwt_when_remembered_and_resume_refreshes_it()
    {
        await LoggedIn();
        Assert.Equal((SessionPhase.LoggedIn, "Clay"), (_s.Phase, _s.Account!.Name));
        Assert.Equal(Jwt1, new SessionStore(Path.Combine(_dir, "session.bin")).Read());
        _http.Answer(HttpStatusCode.OK, new SessionResponse("a.jwt-2.z", Clay));
        Assert.True(await _s.TryResumeAsync());
        Assert.Equal("/v1/auth/session/refresh", _http.Requests[^1].RequestUri!.AbsolutePath);
        Assert.Contains(Jwt1, _http.Bodies[^1]);
        _http.Answer(HttpStatusCode.Unauthorized, "{\"title\":\"Unauthorized\"}", "application/problem+json");
        Assert.False(await _s.TryResumeAsync());
        Assert.Equal(SessionPhase.LoggedOut, _s.Phase);
    }

    [Fact]
    public async Task A_426_anywhere_is_the_update_required_phase()
    {
        _http.Answer((HttpStatusCode)426, "{\"title\":\"Update required\"}", "application/problem+json");
        await Assert.ThrowsAsync<UpdateRequiredException>(() => _s.LoginWithPasswordAsync("c@example.com", "pw"));
        Assert.Equal(SessionPhase.UpdateRequired, _s.Phase);
    }

    [Fact]
    public async Task Connect_sends_the_connect_frame_first_and_builds_the_projection_from_board_and_log()
    {
        var sock = await ConnectedAtPlaying();
        var connect = Assert.IsType<Connect>(sock.Sent[0]);
        Assert.Equal((Jwt1, Game.Id, ProtocolVersion.Current), (connect.SessionJwt, connect.GameId, connect.ClientVersion));
        Assert.Equal(new Uri("ws://localhost:5200/ws"), sock.Url);
        Assert.Equal((SessionPhase.Connected, Samples.P1, Samples.Team1), (_s.Phase, _s.ParticipantId, _s.MyTeamId));
        Assert.Equal(2, _s.Projection!.Scores[Samples.Team1]);
        Assert.Equal(2, _s.Log.Count);
        Assert.Null(_s.Draft);
    }

    [Fact]
    public async Task At_setup_the_board_is_the_draft_and_a_board_message_replaces_it()
    {
        await LoggedIn();
        await _s.ConnectAsync(Game.Id);
        _sockets.Last.Deliver(new Welcome(Samples.P1, Game with { Status = GameStatus.Setup }));
        _sockets.Last.Deliver(new BoardMessage(new Board(TableStateTests.Sample(), [])));
        Assert.Equal(72, _s.Draft!.Size.Width); Assert.Null(_s.Projection);
        _sockets.Last.Deliver(new BoardMessage(new Board(TableStateTests.Sample() with { Size = new TableSize(44, 30) }, [])));
        Assert.Equal(44, _s.Draft!.Size.Width);
    }

    [Fact]
    public async Task Undo_is_predicted_locally_until_the_server_answers()
    {
        var sock = await ConnectedAtPlaying();
        await _s.UndoToAsync(1);
        var cmd = Assert.IsType<CommandMessage>(sock.Sent[^1]);
        Assert.Equal(1, Assert.IsType<Undo>(cmd.Command).ToSeq);
        Assert.Equal(1, _s.Projection!.Scores[Samples.Team1]);          // predicted
        Assert.Equal(2, _s.Applier!.Projection.Scores[Samples.Team1]);   // the truth so far
        sock.Deliver(new EventsMessage([new GameEvent(3, Samples.P1, new Undone(1), null)]));
        Assert.Null(_s.PendingUndoTo);
        Assert.Equal(1, _s.Projection!.Scores[Samples.Team1]);
        await _s.UndoToAsync(0);
        var id = ((CommandMessage)sock.Sent[^1]).CommandId;
        sock.Deliver(new Refused(id, RefusalReason.Unknown, "no"));
        Assert.Null(_s.PendingUndoTo);
        Assert.Equal(1, _s.Projection!.Scores[Samples.Team1]);           // back to the truth
    }

    [Fact]
    public async Task Presence_pings_and_refusals_bypass_the_projection()
    {
        var sock = await ConnectedAtPlaying();
        var other = Game.Participants[0].Id;
        var changed = new List<Guid>(); _s.PresenceChanged += changed.Add;
        sock.Deliver(new PresenceMessage(new Presence(other, new Cursor(new Point3(1, 1, 0), null), null, [], null)));
        Assert.Single(_s.Others.All); Assert.Equal([other], changed);
        sock.Deliver(new PresenceEnded(other));
        Assert.Empty(_s.Others.All);
        Ping? ping = null; _s.Pinged += p => ping = p;
        sock.Deliver(new PingMessage(new Ping(other, new Point3(3, 3, 0), null)));
        Assert.Equal(3, ping!.Point.X);
        await _s.SendPresenceAsync(new Presence(Guid.Empty, null, null, [], null));
        Assert.IsType<PresenceMessage>(sock.Sent[^1]);
        Assert.Equal(2, _s.Log.Count);
    }

    [Fact]
    public async Task An_unexpected_close_reconnects_and_rebuilds_but_a_replaced_socket_does_not()
    {
        var sock = await ConnectedAtPlaying();
        var lost = new List<bool>(); _s.ConnectionLost += (_, will) => lost.Add(will);
        sock.Drop(1001, "server shutting down");
        await Task.Delay(50);
        Assert.Equal([true], lost);
        Assert.Equal(2, _sockets.Created.Count);
        Assert.IsType<Connect>(_sockets.Last.Sent.Single());
        Assert.Null(_s.Projection); Assert.Empty(_s.Others.All);
        _sockets.Last.Deliver(new Welcome(Samples.P1, Game));
        _sockets.Last.Deliver(new BoardMessage(Samples.Board));
        _sockets.Last.Deliver(new EventsMessage([]));
        Assert.Equal(SessionPhase.Connected, _s.Phase);
        _sockets.Last.Drop(1000, "replaced by a newer connection");
        await Task.Delay(50);
        Assert.Equal([true, false], lost);
        Assert.Equal(2, _sockets.Created.Count);
        Assert.Equal(SessionPhase.LoggedIn, _s.Phase);
    }

    // Owner ruling 1, 2026-09-06: a reconnecting player rejoins the team their participant row names, and the
    // "no Game yet" of a reconnect is a different state from having no team.
    [Fact]
    public async Task A_reconnecting_player_rejoins_their_team_and_re_picks_nothing()
    {
        var sock = await ConnectedAtPlaying();
        Assert.Equal(Samples.Team1, _s.MyTeamId);
        sock.Drop(1001, "server shutting down");
        await Task.Delay(50);
        Assert.Equal(SessionPhase.Reconnecting, _s.Phase);
        Assert.Null(_s.Game); Assert.Null(_s.MyTeamId);                 // nothing to read a team from yet, which is not watching
        _sockets.Last.Deliver(new Welcome(Samples.P1, Game));           // the server names the same team, out of the participant row
        Assert.Equal((SessionPhase.Connected, Samples.Team1), (_s.Phase, _s.MyTeamId));
        Assert.IsType<Connect>(Assert.Single(_sockets.Last.Sent));      // one connect frame, and no PickTeam edit
    }

    [Fact]
    public async Task A_rejected_connect_does_not_reconnect_and_upgrade_required_is_sticky()
    {
        await LoggedIn();
        await _s.ConnectAsync(Game.Id);
        _sockets.Last.Deliver(new Rejected(RejectReason.UpgradeRequired));
        _sockets.Last.Drop(1000, "upgrade_required");
        await Task.Delay(50);
        Assert.Equal(SessionPhase.UpdateRequired, _s.Phase);
        Assert.Single(_sockets.Created);
    }

    [Fact]
    public async Task Home_disconnects_and_keeps_the_seat_while_leave_calls_management()
    {
        var sock = await ConnectedAtPlaying();
        await _s.DisconnectAsync();
        Assert.False(sock.Connected);
        Assert.Equal(SessionPhase.LoggedIn, _s.Phase);
        Assert.Null(_s.GameId);
        Assert.DoesNotContain(_http.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/leave"));
        await _s.ConnectAsync(Game.Id); _sockets.Last.Deliver(new Welcome(Samples.P1, Game));
        _http.Answer(HttpStatusCode.NoContent);
        await _s.LeaveAsync();
        Assert.EndsWith($"/v1/games/{Game.Id}/leave", _http.Requests[^1].RequestUri!.AbsolutePath);
        Assert.Equal((SessionPhase.LoggedIn, (Guid?)null), (_s.Phase, _s.GameId));
    }

    // An event naming an id the projection does not hold: moving a token that was placed during setup is the
    // designed case (contracts design -> the applier's strictness). The applier throws ProjectionDesyncException.
    static GameEvent Desyncing(long seq) => new(seq, Samples.P1, new MarkerMoved(Guid.CreateVersion7(), new Point3(2, 2, 0), new Yaw(0)), null);
    static GameEvent Score(long seq, int score) => new(seq, Samples.P1, new ScoreSet(Samples.Team1, score), null);

    [Fact]
    public async Task A_desync_drops_the_projection_and_rebuilds_it_from_the_log()
    {
        var sock = await ConnectedAtPlaying();
        var lost = new List<bool>(); SocketClosed? closed = null;
        _s.ConnectionLost += (c, will) => { closed = c; lost.Add(will); };
        sock.Deliver(new PresenceMessage(new Presence(Game.Participants[0].Id, null, null, [], null)));
        await _s.UndoToAsync(1);
        sock.Deliver(new EventsMessage([Desyncing(3)]));
        Assert.Equal([true], lost);
        Assert.IsType<ProjectionDesyncException>(closed!.Error);     // how GameScreen tells this from a dropped socket
        Assert.Equal(2, _sockets.Created.Count);                     // the socket was closed and reopened
        Assert.Equal(SessionPhase.Reconnecting, _s.Phase);
        Assert.Null(_s.Projection); Assert.Null(_s.Applier); Assert.Null(_s.PendingUndoTo); Assert.Empty(_s.Others.All);
        _sockets.Last.Deliver(new Welcome(Samples.P1, Game));
        _sockets.Last.Deliver(new BoardMessage(Samples.Board));
        _sockets.Last.Deliver(new EventsMessage([Score(1, 1)]));
        Assert.Equal((SessionPhase.Connected, 1), (_s.Phase, _s.Projection!.Scores[Samples.Team1]));
    }

    [Fact]
    public async Task A_desync_that_makes_no_progress_stops_resyncing_and_goes_home_with_the_reason()
    {
        var sock = await ConnectedAtPlaying();
        var lost = new List<bool>(); _s.ConnectionLost += (_, will) => lost.Add(will);
        sock.Deliver(new EventsMessage([Desyncing(3)]));              // faults with LastSeq 2: the floor
        _sockets.Last.Deliver(new Welcome(Samples.P1, Game));
        _sockets.Last.Deliver(new BoardMessage(Samples.Board));
        _sockets.Last.Deliver(new EventsMessage([Score(1, 1), Score(2, 2), Desyncing(3)]));   // the log replays it
        Assert.Equal([true, false], lost);
        Assert.Equal(2, _sockets.Created.Count);                      // no third socket
        Assert.Equal((SessionPhase.LoggedIn, (Guid?)null), (_s.Phase, _s.GameId));
        Assert.NotNull(_s.LastCloseReason);                           // the Home screen prints it
    }

    [Fact]
    public async Task An_at_rest_frame_is_ignored()
    {
        var sock = await ConnectedAtPlaying();
        var applied = 0; _s.EventsApplied += _ => applied++;
        sock.Deliver(new AtRest(2));
        Assert.Equal((0, 2, SessionPhase.Connected), (applied, _s.Log.Count, _s.Phase));
        Assert.Equal(2, _s.Projection!.Scores[Samples.Team1]);
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `IDispatcher.cs`**

```csharp
namespace Tabletop.Client.Core;

/// <summary>Where Session raises its events. Godot posts to the main thread (CallDeferred); tests run the action at once.</summary>
public interface IDispatcher { void Post(Action action); }

public sealed class ImmediateDispatcher : IDispatcher { public void Post(Action action) => action(); }
```

- [ ] **Step 3: Write `Session.cs`**

```csharp
using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

public enum SessionPhase { LoggedOut, LoggedIn, Connecting, Connected, Reconnecting, UpdateRequired }

/// <summary>
/// The one object that outlives every screen: the management client with the JWT, the socket while one is open, the
/// projection (EventApplier), the draft during setup, and others' presence. Screens read it and hold no truth of their own.
/// </summary>
public sealed class Session(ManagementClient management, IGameSocketFactory sockets, SessionStore store, ClientSettings settings,
    Action<ClientSettings> persistSettings, IDispatcher dispatcher, Func<int, TimeSpan>? backoff = null)
{
    static readonly TimeSpan[] DefaultBackoff = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];
    readonly Func<int, TimeSpan> _backoff = backoff ?? (attempt => DefaultBackoff[Math.Min(attempt, DefaultBackoff.Length - 1)]);
    IGameSocket? _socket;
    bool _welcomed, _userClosed;
    int _attempt;
    long? _desyncFloor;                 // the projection's LastSeq at the last desync: one resync per position
    Guid? _pendingUndoCommand;
    Projection? _predicted;

    public SessionPhase Phase { get; private set; } = SessionPhase.LoggedOut;
    public AccountRow? Account { get; private set; }
    public ClientSettings Settings { get; private set; } = settings;
    public ManagementClient Management => management;
    public Guid? GameId { get; private set; }
    public Guid? ParticipantId { get; private set; }
    public GameInfo? Game { get; private set; }
    public TableState? Draft { get; private set; }
    public EventApplier? Applier { get; private set; }
    public Projection? Projection => _predicted ?? Applier?.Projection;
    public IReadOnlyList<GameEvent> Log => Applier?.Log ?? [];
    public PresenceTable Others { get; } = new();
    public long? PendingUndoTo { get; private set; }
    public string? LastCloseReason { get; private set; }
    public Guid? MyTeamId => Game?.Participants.FirstOrDefault(p => p.Id == ParticipantId)?.TeamId;
    public bool IsCreator => Game is not null && Game.Participants.FirstOrDefault(p => p.Id == ParticipantId)?.AccountId == Game.CreatedBy;

    public event Action? PhaseChanged;
    public event Action? GameChanged;
    public event Action? BoardChanged;
    public event Action<IReadOnlyList<GameEvent>>? EventsApplied;
    public event Action<Guid>? PresenceChanged;
    public event Action<Ping>? Pinged;
    public event Action<Refused>? RefusedReceived;
    public event Action<SocketClosed, bool>? ConnectionLost;

    // ---- login -------------------------------------------------------------------------------------------------------
    public Task LoginWithPasswordAsync(string email, string password) => Complete(() => management.LoginWithPassword(email, password));
    public Task SignUpAsync(string email, string password) => Complete(() => management.SignUp(email, password));
    public async Task<string> SendOtpAsync(string email) => (await Guard(() => management.SendOtp(email))).MethodId;
    public Task VerifyOtpAsync(string methodId, string code) => Complete(() => management.VerifyOtp(methodId, code));

    public async Task<bool> TryResumeAsync()
    {
        var jwt = management.Jwt ?? store.Read();
        if (jwt is null) return false;
        try { await Complete(() => management.Refresh(jwt)); return true; }
        catch (ManagementException e) when (e is not UpdateRequiredException) { management.Jwt = null; store.Clear(); SetPhase(SessionPhase.LoggedOut); return false; }
    }

    /// <summary>Once a minute from the Godot side: the JWT lives five minutes.</summary>
    public async Task RefreshAsync() { if (management.Jwt is { } jwt) await Complete(() => management.Refresh(jwt)); }

    public async Task LogoutAsync()
    {
        await DisconnectAsync();
        try { await management.Logout(); } catch (ManagementException) { }
        management.Jwt = null; Account = null; store.Clear();
        SetPhase(SessionPhase.LoggedOut);
    }

    public void UpdateSettings(Func<ClientSettings, ClientSettings> change)
    {
        Settings = change(Settings);
        persistSettings(Settings);
        if (!Settings.RememberMe) store.Clear(); else if (management.Jwt is { } jwt) store.Write(jwt);
    }

    async Task Complete(Func<Task<SessionResponse>> call)
    {
        var response = await Guard(call);
        management.Jwt = response.SessionJwt;
        Account = response.Account;
        if (Settings.RememberMe) store.Write(response.SessionJwt);
        if (Phase is SessionPhase.LoggedOut) SetPhase(SessionPhase.LoggedIn);
    }

    async Task<T> Guard<T>(Func<Task<T>> call)
    {
        try { return await call(); }
        catch (UpdateRequiredException) { SetPhase(SessionPhase.UpdateRequired); throw; }
    }

    // ---- the game ----------------------------------------------------------------------------------------------------
    public Task ConnectAsync(Guid gameId) => ConnectAsync(gameId, resync: false);

    /// <summary>A resync keeps the desync floor and shows as Reconnecting; a player-initiated connect starts clean.</summary>
    async Task ConnectAsync(Guid gameId, bool resync)
    {
        if (Phase == SessionPhase.UpdateRequired || management.Jwt is null) return;
        if (_socket is not null) await TearDownSocket();
        GameId = gameId; _userClosed = false; _welcomed = false;
        if (!resync) { _desyncFloor = null; LastCloseReason = null; }
        Reset();
        SetPhase(_attempt == 0 && !resync ? SessionPhase.Connecting : SessionPhase.Reconnecting);
        var socket = sockets.Create();
        socket.FrameReceived += frame => dispatcher.Post(() => { if (ReferenceEquals(socket, _socket)) Handle(frame); });
        socket.Closed += closed => dispatcher.Post(() => { if (ReferenceEquals(socket, _socket)) OnClosed(closed); });
        _socket = socket;
        try
        {
            await socket.ConnectAsync(new Uri(Settings.GameServerUrl), CancellationToken.None);
            await socket.SendAsync(new Connect(management.Jwt, gameId, ProtocolVersion.Current));
        }
        catch (Exception e) when (e is System.Net.WebSockets.WebSocketException or System.Net.Sockets.SocketException or IOException)
        { OnClosed(new SocketClosed(null, e.Message, e)); }
    }

    /// <summary>The Home button: closes the socket; the participant row stays (disconnecting is not leaving).</summary>
    public async Task DisconnectAsync()
    {
        _userClosed = true;
        LastCloseReason = null;             // the player asked to leave: Home has nothing to explain
        await TearDownSocket();
        GameId = null; Reset();
        if (Phase is not (SessionPhase.LoggedOut or SessionPhase.UpdateRequired)) SetPhase(SessionPhase.LoggedIn);
    }

    /// <summary>The menu's Leave: the row goes, then the socket.</summary>
    public async Task LeaveAsync()
    {
        if (GameId is { } id) await Guard(async () => { await management.LeaveGame(id); return 0; });
        await DisconnectAsync();
    }

    public async Task<Guid> SendCommandAsync(Command command)
    {
        var id = Guid.CreateVersion7();
        if (_socket is not null) await _socket.SendAsync(new CommandMessage(id, command));
        return id;
    }

    public async Task<Guid> SendEditAsync(Edit edit)
    {
        var id = Guid.CreateVersion7();
        if (_socket is not null) await _socket.SendAsync(new EditMessage(id, edit));
        return id;
    }

    /// <summary>Undo is local first: the prediction shows until the server's undone or refused arrives.</summary>
    public async Task UndoToAsync(long seq)
    {
        if (Applier is null) return;
        // ProjectTo replays a prefix of a log this applier already applied in order, so it cannot fault where
        // ApplyAll did not. The catch holds the invariant rather than a known case.
        try { _predicted = Applier.ProjectTo(seq); }
        catch (ProjectionDesyncException desync) { OnDesync(desync); return; }
        PendingUndoTo = seq;
        _pendingUndoCommand = await SendCommandAsync(new Undo(seq));
        EventsApplied?.Invoke([]);
    }

    public Task SendPresenceAsync(Presence value) => _socket?.SendAsync(new PresenceMessage(value)) ?? Task.CompletedTask;
    public Task SendPingAsync(Ping ping) => _socket?.SendAsync(new PingMessage(ping)) ?? Task.CompletedTask;
```


The second half of `Session.cs` (same class, same file) — the frame handler, the close handler and the reset:

```csharp
    // ---- frames (on the dispatcher) ----------------------------------------------------------------------------------
    void Handle(SocketMessage frame)
    {
        switch (frame)
        {
            case Welcome w:
                _welcomed = true; _attempt = 0;
                ParticipantId = w.ParticipantId; Game = w.Game;
                SetPhase(SessionPhase.Connected);
                GameChanged?.Invoke();
                break;
            case GameInfoMessage g:
                Game = g.Game;
                GameChanged?.Invoke();
                break;
            case BoardMessage b when Game is { Status: GameStatus.Playing or GameStatus.Ended }:
                Draft = null; _predicted = null; PendingUndoTo = null;
                Applier = new EventApplier(b.Board, Game);
                BoardChanged?.Invoke();
                break;
            case BoardMessage b:
                Draft = b.Board.Table;          // a setup edit replaces the draft
                BoardChanged?.Invoke();
                break;
            case EventsMessage e when Applier is not null:
                try { Applier.ApplyAll(e.Events); }
                catch (ProjectionDesyncException desync) { OnDesync(desync); return; }   // partially applied: unusable
                if (PendingUndoTo is { } to && e.Events.Any(x => x.Payload is Undone u && u.ToSeq == to)) ClearPrediction();
                EventsApplied?.Invoke(e.Events);
                break;
            case PresenceMessage p:
                Others.Set(p.Presence); PresenceChanged?.Invoke(p.Presence.ParticipantId);
                break;
            case PresenceEnded p:
                Others.Remove(p.ParticipantId); PresenceChanged?.Invoke(p.ParticipantId);
                break;
            case PingMessage p: Pinged?.Invoke(p.Ping); break;
            case Refused r:
                if (r.MessageId == _pendingUndoCommand) { ClearPrediction(); EventsApplied?.Invoke([]); }
                RefusedReceived?.Invoke(r);
                break;
            case Rejected r:
                _userClosed = true;             // the server closes after this; do not reconnect
                LastCloseReason = r.Reason.ToString();
                if (r.Reason == RejectReason.UpgradeRequired) SetPhase(SessionPhase.UpdateRequired);
                else { GameId = null; SetPhase(SessionPhase.LoggedIn); }
                break;
            // No default arm. A kind this client does not act on is dropped: AtRest, which says the server has
            // nothing further to emit and which nothing here waits for, and anything a later protocol version adds.
            // Deserialization has already succeeded by this point, so an unhandled kind is not an error.
        }
    }

    void ClearPrediction() { _predicted = null; PendingUndoTo = null; _pendingUndoCommand = null; }

    /// <summary>
    /// The applier faulted, so this copy of the projection is unusable: drop it, reopen the socket and rebuild from
    /// the log. One attempt per sequence position. A fault whose projection reached past the last one is a new bad
    /// event and gets its own attempt; a fault at or before it is the same event out of the server's own log, which
    /// replaying cannot fix, so the player goes Home with the reason instead of resyncing forever.
    /// </summary>
    void OnDesync(ProjectionDesyncException desync)
    {
        var reached = Applier?.Projection.LastSeq ?? 0;
        var closed = new SocketClosed(null, desync.Message, desync);
        LastCloseReason = desync.Message;
        if (_desyncFloor is { } floor && reached <= floor)
        {
            _ = TearDownSocket();
            GameId = null; Reset();
            SetPhase(SessionPhase.LoggedIn);
            ConnectionLost?.Invoke(closed, false);
            return;
        }
        _desyncFloor = reached;
        ConnectionLost?.Invoke(closed, true);
        if (GameId is { } id) _ = ConnectAsync(id, resync: true);     // no backoff: time does not fix a bad event
    }

    void OnClosed(SocketClosed closed)
    {
        LastCloseReason = closed.Reason;
        _socket = null;
        Reset();
        if (_userClosed || Phase == SessionPhase.UpdateRequired) { ConnectionLost?.Invoke(closed, false); return; }
        var replaced = _welcomed && closed.Code == 1000;
        if (replaced || GameId is null) { GameId = null; SetPhase(SessionPhase.LoggedIn); ConnectionLost?.Invoke(closed, false); return; }
        ConnectionLost?.Invoke(closed, true);
        SetPhase(SessionPhase.Reconnecting);
        var attempt = _attempt++;
        var gameId = GameId.Value;
        _ = Task.Run(async () => { await Task.Delay(_backoff(attempt)); dispatcher.Post(() => { if (GameId == gameId && _socket is null && !_userClosed) _ = ConnectAsync(gameId); }); });
    }

    async Task TearDownSocket()
    {
        var s = _socket; _socket = null;
        if (s is null) return;
        try { await s.CloseAsync(); await s.DisposeAsync(); } catch (Exception) { }
    }

    /// <summary>Reconnect is connect: the projection, the draft and presence are rebuilt from what the server sends.</summary>
    void Reset()
    {
        Applier = null; Draft = null; Game = null; ParticipantId = null;
        ClearPrediction(); Others.Clear();
        BoardChanged?.Invoke();
    }

    void SetPhase(SessionPhase phase) { if (Phase == phase) return; Phase = phase; PhaseChanged?.Invoke(); }
}
```

`Reset()` clears `Game`, which `IsCreator`/`MyTeamId` read; after a reconnect the `Welcome` restores it. `ConnectAsync` calling `Reset()` before the first frame is what makes "reconnect is connect" literal.

`MyTeamId` is derived, never picked or remembered: it reads `Game.Participants` for `ParticipantId`, and the server fills that from the participant's row. So a reconnecting player is back on the team they were on, and the client has nothing to re-pick — teams are locked from `playing` on, and only the server's `Welcome` says which one is yours (owner ruling 1, 2026-09-06; the model is stated in game flow → Teams are frozen at the flip, and a team is a slot). `MyTeamId` is null in two unrelated cases, and no code may conflate them: a spectator has a `Game` with no team on their participant row, while a client mid-reconnect has no `Game` at all. Both paths that read it return early on a null `Game`: `GameScreen.Rebuild` tears the team panels down and rebuilds them from the `Welcome`, and `AskPanel.Render` draws nothing. So a returning player shows as `Reconnecting…` and then as themselves, never as a watcher.

A resync is that same path, so the projection, the draft, the undo prediction and the presence table go through the one discard. The old socket's close does not reach `OnClosed`: `TearDownSocket` nulls `_socket` before closing it, and each handler is gated on `ReferenceEquals(socket, _socket)`. The `1000`-after-`welcome` inference is therefore not consulted on a resync and is left as it is.

- [ ] **Step 4: Run `dotnet test tests/Tabletop.Client.Tests`; expect 25 passed. Commit: `Add the session object: login, the connect sequence, the projection, local undo, reconnect and the desync resync`.**

### Task 6: Built-in content and the four terrain generators

**Files:**
- Create: `client/src/Core/BuiltinContent.cs`, `client/src/Core/TerrainGenerators.cs`
- Test: `tests/Tabletop.Client.Tests/BuiltinContentTests.cs`, `tests/Tabletop.Client.Tests/TerrainGeneratorTests.cs`
- Author (art, not code — Step 4): `client/content/builtin/terrain/{hill,woods,ruins,building,wall,rock,field,water,pad,haze}.glb`, `client/content/builtin/textures/{grass,desert,urban,snow,ash}.png`

**Interfaces:**
- Consumes: `Volume`, `VolumeKind`, `Footprint`, `TerrainDeclarations`, `Point2`, `Point3`, `Yaw`, `TableSize`, `ZoneStyles` (Geometry); `TerrainPlacement`, `TerrainVolume`, `VisualRef` (Content); `AddTerrain` (Protocol).
- Produces: `sealed record BuiltinTerrain(string Name, string Label, Volume Volume, string? MeshFile)`; `static class BuiltinContent` — `IReadOnlyDictionary<string, BuiltinTerrain> Terrain`, `IReadOnlyList<string> Textures` (`grass`, `desert`, `urban`, `snow`, `ash`), `IReadOnlyList<string> MarkerVisuals` (`objective-ring`, `chip`), `IReadOnlyList<string> TerrainMeshGroups` (the ten shared mesh basenames), `IReadOnlyList<string> ZoneStyleIds => ZoneStyles.All`, `TerrainPlacement Place(string name, Point3 position, Yaw yaw, double scale = 1)` (volumes copied from the built-in — the placement's rows are the volumes), `IReadOnlyList<TerrainVolume> VolumesOf(string name)` (what "clear overrides" re-copies), `string TextureFile(string name)`, `string? TerrainMeshFile(string name)`; `enum Planet { Generic, Hospitable, Wasteland, Death }`; `static class TerrainGenerators` — `IReadOnlyList<AddTerrain> Generate(Planet planet, TableSize table, Random rng)` (nine fixed pieces then 4D3 rolled on 2D6, placed off the slab beside the long edge at `x = width + 4`, in rows 8" apart), `IReadOnlyList<string> Fixed(Planet)`, `IReadOnlyList<string> Rolled(Planet, int twoD6)` (the alternatives an entry names).
- **A minimal art set ships, grouped ten ways** (owner ruling, 2026-09-06). 35 distinct meshes is not minimal, and the pieces fall into ten shapes that differ only by footprint, height, scale and yaw: `hill` (hill, large-hill, steep-hill, hill-small, small-dunes, beast-lair — 6), `woods` (forest, large-forest, plantation, dense-vegetation, deadly-vegetation — 5), `ruins` (ruins, large-ruins, overgrown-ruins, ruin — 4), `building` (large-building, small-buildings, large-sight-blocker — 3), `wall` (wall-or-fence, barricade — 2), `rock` (large-rock, crumbly-rock — 2), `field` (large-field, tall-crops-field — 2), `water` (large-lake, deep-river, shallow-river, toxic-river, sludge-stream — 5), `pad` (crater, sludge-field, mine-field, death-marsh, quicksand — 5), `haze` (toxic-smog — 1). Ten meshes, 35 pieces. Each built-in's `MeshFile` names its group's file, so `TerrainMeshFile` returns the group path; the per-name fallback `res://content/builtin/terrain/<name>.glb` stays for a built-in whose `MeshFile` is null, which lets one piece be given bespoke art later without a code change.
- **Every mesh file is still optional, and a missing one still draws the hitbox.** `MeshLoader.LoadBuiltin` returns null when the file is absent and `TerrainNode` keeps its per-volume proxy visible (asset library → the proxy; setup phase → "the hitbox is the data and the mesh only decorates it"). That is the degraded path, not the plan: it is what happens for whichever of the fifteen files has not been made yet, and it is kept rather than deleted so a build with no `content/builtin/` at all still renders a playable table.
- Declarations follow the tables literally; conditional clauses ("dangerous when using Rush/Charge", "difficult for units moving up", "units on top may ignore one unit/terrain for line of sight") are `Notes`, never flags (geometry values → Declarations: "Everything else OPR does with terrain is free text").

Spec: setup phase → Terrain: imported, built-in, generated (built-ins ship with the client; generators are the rulebook's four tables, nine fixed plus 4D3 on 2D6; a batch of adds; land beside the table; the client rolls; the tables ship with the client and the server never reads them); Grimdark Future p.18–19 (the tables, read above); p.18 size classes (scatter 1–3", large 4–8", up to 12").

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.Client.Core;
using Tabletop.Geometry;

namespace Tabletop.Client.Tests;

public class BuiltinContentTests
{
    [Fact]
    public void Every_generator_entry_names_a_built_in()
    {
        foreach (var planet in Enum.GetValues<Planet>())
        {
            Assert.All(TerrainGenerators.Fixed(planet), n => Assert.True(BuiltinContent.Terrain.ContainsKey(n), $"{planet}: {n}"));
            for (var roll = 2; roll <= 12; roll++)
                Assert.All(TerrainGenerators.Rolled(planet, roll), n => Assert.True(BuiltinContent.Terrain.ContainsKey(n), $"{planet} {roll}: {n}"));
        }
    }

    [Fact]
    public void Declarations_follow_the_rulebook_text()
    {
        Assert.Equal(TerrainDeclarations.Difficult | TerrainDeclarations.Cover | TerrainDeclarations.BlocksLineOfSight | TerrainDeclarations.SeeInAndOut, BuiltinContent.Terrain["forest"].Volume.Declarations);
        Assert.Equal(TerrainDeclarations.Impassable | TerrainDeclarations.BlocksLineOfSight, BuiltinContent.Terrain["large-building"].Volume.Declarations);
        Assert.Equal(TerrainDeclarations.Cover, BuiltinContent.Terrain["ruins"].Volume.Declarations);            // "dangerous when using Rush/Charge" is a note
        Assert.Contains("Rush/Charge", BuiltinContent.Terrain["ruins"].Volume.Notes);
        Assert.Equal(TerrainDeclarations.None, BuiltinContent.Terrain["quicksand"].Volume.Declarations);
        Assert.Equal(TerrainDeclarations.Difficult | TerrainDeclarations.Cover, BuiltinContent.Terrain["wall-or-fence"].Volume.Declarations);   // "Difficult when moving through it" is what Difficult means
        Assert.Equal(VolumeKind.Area, BuiltinContent.Terrain["deep-river"].Volume.Kind);
        Assert.Equal(TerrainDeclarations.Impassable, BuiltinContent.Terrain["deep-river"].Volume.Declarations);
    }

    [Fact]
    public void Samples_and_the_servers_default_render()
    {
        Assert.Contains("grass", BuiltinContent.Textures);                 // the game server's default draft texture
        Assert.Contains("desert", BuiltinContent.Textures);
        Assert.Contains("objective-ring", BuiltinContent.MarkerVisuals); Assert.Contains("chip", BuiltinContent.MarkerVisuals);
        Assert.True(BuiltinContent.Terrain.ContainsKey("hill-small")); Assert.True(BuiltinContent.Terrain.ContainsKey("ruin"));
        Assert.Equal("res://content/builtin/textures/grass.png", BuiltinContent.TextureFile("grass"));
        // Every piece names one of the ten group meshes, so the art set is ten files and not 35.
        Assert.Equal(10, BuiltinContent.TerrainMeshGroups.Count);
        Assert.All(BuiltinContent.Terrain.Values, t => Assert.Contains(t.MeshFile, BuiltinContent.TerrainMeshGroups.Select(g => $"res://content/builtin/terrain/{g}.glb")));
        Assert.Equal("res://content/builtin/terrain/hill.glb", BuiltinContent.TerrainMeshFile("steep-hill"));
        Assert.Equal("res://content/builtin/terrain/water.glb", BuiltinContent.TerrainMeshFile("sludge-stream"));
        Assert.Null(BuiltinContent.TerrainMeshFile("no-such-piece"));
    }

    [Fact]
    public void Placing_copies_the_volumes_with_fresh_ids()
    {
        var a = BuiltinContent.Place("forest", new Point3(10, 10, 0), new Yaw(0));
        var b = BuiltinContent.Place("forest", new Point3(20, 10, 0), new Yaw(0));
        Assert.NotEqual(a.Id, b.Id); Assert.NotEqual(a.Volumes[0].Id, b.Volumes[0].Id);
        Assert.Equal(BuiltinContent.Terrain["forest"].Volume, a.Volumes[0].Volume);
        Assert.Equal("forest", a.Mesh.Builtin);
        Assert.Single(BuiltinContent.VolumesOf("forest"));
    }
}

public class TerrainGeneratorTests
{
    static readonly TableSize Opr = new(72, 48);

    [Theory] [InlineData(Planet.Generic)] [InlineData(Planet.Hospitable)] [InlineData(Planet.Wasteland)] [InlineData(Planet.Death)]
    public void Nine_fixed_plus_4d3_rolled_land_beside_the_slab(Planet planet)
    {
        for (var seed = 0; seed < 20; seed++)
        {
            var batch = TerrainGenerators.Generate(planet, Opr, new Random(seed));
            Assert.InRange(batch.Count, 9 + 4, 9 + 12);
            Assert.All(batch, add => Assert.False(Table.ContainsPoint(Opr, add.Placement.Position.Horizontal)));
            Assert.All(batch, add => Assert.True(add.Placement.Position.X >= Opr.Width + 4));
            Assert.Equal(batch.Count, batch.Select(a => a.Placement.Id).Distinct().Count());
            Assert.All(batch, add => Assert.NotEmpty(add.Placement.Volumes));
        }
    }

    [Fact]
    public void Generic_fixed_pieces_are_the_rulebooks_nine()
    {
        var fixedNames = TerrainGenerators.Fixed(Planet.Generic);
        Assert.Equal(9, fixedNames.Count);
        Assert.Equal(2, fixedNames.Count(n => n == "barricade"));
        Assert.Equal(5, fixedNames.Count(n => n == "large-sight-blocker"));
        Assert.Equal(["large-hill"], TerrainGenerators.Rolled(Planet.Generic, 2));
        Assert.Equal(["large-hill"], TerrainGenerators.Rolled(Planet.Generic, 3));
        Assert.Equal(["barricade"], TerrainGenerators.Rolled(Planet.Generic, 7));
        Assert.Equal(["large-lake"], TerrainGenerators.Rolled(Planet.Generic, 12));
        Assert.Equal(["hill", "forest", "tall-crops-field"], TerrainGenerators.Rolled(Planet.Hospitable, 7));   // "Hill, Forest, or Field"
        Assert.Equal(["quicksand"], TerrainGenerators.Rolled(Planet.Death, 12));
    }

    [Fact]
    public void The_same_seed_gives_the_same_batch() =>
        Assert.Equal(TerrainGenerators.Generate(Planet.Wasteland, Opr, new Random(7)).Select(a => a.Placement.Mesh.Builtin),
                     TerrainGenerators.Generate(Planet.Wasteland, Opr, new Random(7)).Select(a => a.Placement.Mesh.Builtin));
}
```

- [ ] **Step 2: Run; expect compile errors. Write `BuiltinContent.cs`**

```csharp
using Tabletop.Content;
using Tabletop.Geometry;
using static Tabletop.Geometry.TerrainDeclarations;

namespace Tabletop.Client.Core;

/// <summary>A built-in terrain piece: its volume (the data) and an optional mesh file (the appearance). Owned by nobody, in no library.</summary>
public sealed record BuiltinTerrain(string Name, string Label, Volume Volume, string? MeshFile);

/// <summary>What ships inside the client: terrain, textures, marker visuals. The server never reads any of it (setup phase → Terrain).</summary>
public static class BuiltinContent
{
    const string Solid = "solid", Area = "area";
    static Volume V(string kind, Footprint f, double top, TerrainDeclarations d, string? notes = null) =>
        new(kind == Solid ? VolumeKind.Solid : VolumeKind.Area, f, new Point2(0, 0), new Yaw(0), 0, top, d, notes);
    // A built-in names its group's mesh, not one of its own: ten meshes cover the 35 pieces, differing by footprint,
    // height, scale and yaw. MeshLoader.LoadBuiltin fits the group mesh to the placement's volume (Task 11).
    static BuiltinTerrain T(string name, string label, Volume v, string group) => new(name, label, v, $"res://content/builtin/terrain/{group}.glb");
    const string RushCharge = "Dangerous terrain when using Rush/Charge actions";
    const string OnTop = "Units on top may ignore one unit/terrain for line of sight";
    const string MovingUp = "Difficult terrain for units moving up";

    static readonly BuiltinTerrain[] All =
    [
        // Generic (p.18)
        T("barricade", "Barricade", V(Solid, Footprint.Rect(3, 1), 1, Difficult | Cover), "wall"),        // as Wall or Fence, p.18: Cover, and Difficult when moving through it
        T("large-forest", "Large Forest", V(Area, Footprint.Circle(3), 6, Difficult | Cover | BlocksLineOfSight | SeeInAndOut), "woods"),
        T("large-field", "Large Field", V(Area, Footprint.Rect(6, 6), 1, Difficult | Cover), "field"),
        T("large-sight-blocker", "Large Sight Blocker", V(Solid, Footprint.Rect(6, 6), 6, BlocksLineOfSight), "building"),
        T("large-hill", "Large Hill", V(Solid, Footprint.Circle(4), 2, Cover, OnTop), "hill"),
        T("large-ruins", "Large Ruins", V(Area, Footprint.Rect(6, 6), 4, Cover, RushCharge), "ruins"),
        T("large-lake", "Large Lake", V(Area, Footprint.Circle(4), 0.5, Impassable), "water"),
        // Hospitable (p.18)
        T("wall-or-fence", "Wall or Fence", V(Solid, Footprint.Rect(6, 0.5), 1.5, Difficult | Cover), "wall"),   // p.18: "Cover, as well as Difficult terrain when moving through it"
        T("hill", "Hill", V(Solid, Footprint.Circle(3), 2, Cover, OnTop), "hill"),
        T("plantation", "Plantation", V(Area, Footprint.Rect(6, 6), 6, Difficult | Cover | BlocksLineOfSight | SeeInAndOut), "woods"),
        T("forest", "Forest", V(Area, Footprint.Circle(3), 6, Difficult | Cover | BlocksLineOfSight | SeeInAndOut), "woods"),
        T("large-building", "Large Building", V(Solid, Footprint.Rect(6, 6), 8, Impassable | BlocksLineOfSight), "building"),
        T("deep-river", "Deep River or Lake", V(Area, Footprint.Rect(3, 12), 0.5, Impassable), "water"),
        T("shallow-river", "Shallow River or Stream", V(Area, Footprint.Rect(3, 12), 0.5, Difficult, "Optionally one 3\"-6\" wide bridge per full 24\""), "water"),
        T("tall-crops-field", "Tall Crops Field", V(Area, Footprint.Rect(6, 6), 1, Difficult | Cover), "field"),
        T("ruins", "Ruins or Debris", V(Area, Footprint.Rect(6, 6), 4, Cover, RushCharge), "ruins"),
        T("small-buildings", "Group of Small Buildings", V(Solid, Footprint.Rect(6, 6), 4, Impassable | BlocksLineOfSight), "building"),
        T("steep-hill", "Steep Hill", V(Solid, Footprint.Circle(3), 3, Cover, $"{MovingUp}. {OnTop}"), "hill"),
        // Wasteland (p.19)
        T("small-dunes", "Group of Small Dunes", V(Solid, Footprint.Rect(6, 4), 1.5, Cover, OnTop), "hill"),
        T("large-rock", "Large Rock or Two to Three Small Rocks", V(Solid, Footprint.Rect(4, 4), 4, Impassable | BlocksLineOfSight), "rock"),
        T("crater", "Crater", V(Area, Footprint.Circle(3), 1, Cover), "pad"),
        T("toxic-smog", "Toxic Smog", V(Area, Footprint.Circle(4), 6, Cover | Dangerous | BlocksLineOfSight | SeeInAndOut), "haze"),
        T("toxic-river", "Toxic River", V(Area, Footprint.Rect(3, 12), 0.5, Difficult | Dangerous, "Optionally one 3\"-6\" wide bridge per full 24\""), "water"),
        T("sludge-stream", "Sludge Stream or Canal", V(Area, Footprint.Rect(2, 12), 0.5, Dangerous, "Optionally one 3\"-6\" wide bridge per full 24\""), "water"),
        T("sludge-field", "Sludge Field", V(Area, Footprint.Rect(6, 6), 0.5, Difficult), "pad"),
        T("mine-field", "Mine Field", V(Area, Footprint.Rect(6, 6), 0.1, Dangerous), "pad"),
        // Death (p.19)
        T("overgrown-ruins", "Overgrown Ruins", V(Area, Footprint.Rect(6, 6), 4, Difficult | Cover), "ruins"),
        T("deadly-vegetation", "Deadly Vegetation", V(Area, Footprint.Circle(3), 6, Difficult | Cover | Dangerous), "woods"),
        T("dense-vegetation", "Dense Vegetation", V(Area, Footprint.Circle(3), 6, Difficult | Cover | BlocksLineOfSight | SeeInAndOut), "woods"),
        T("death-marsh", "Death Marsh", V(Area, Footprint.Rect(6, 6), 0.2, Difficult | Dangerous), "pad"),
        T("crumbly-rock", "Crumbly Rock", V(Solid, Footprint.Rect(4, 4), 2, Difficult), "rock"),
        T("beast-lair", "Beast Lair", V(Solid, Footprint.Circle(2), 2, Dangerous), "hill"),
        T("quicksand", "Quicksand", V(Area, Footprint.Rect(4, 4), 0.1, None, RushCharge), "pad"),
        // Names the contracts plan's samples use
        T("hill-small", "Small Hill", V(Solid, Footprint.Rect(6, 6), 2, None), "hill"),
        T("ruin", "Ruin", V(Solid, Footprint.Rect(4, 4), 4, Cover | BlocksLineOfSight | SeeInAndOut), "ruins"),
    ];

    /// <summary>The ten mesh files the 35 built-ins share, by their basename under res://content/builtin/terrain/.</summary>
    public static IReadOnlyList<string> TerrainMeshGroups { get; } = ["hill", "woods", "ruins", "building", "wall", "rock", "field", "water", "pad", "haze"];

    public static IReadOnlyDictionary<string, BuiltinTerrain> Terrain { get; } = All.ToDictionary(t => t.Name);
    public static IReadOnlyList<string> Textures { get; } = ["grass", "desert", "urban", "snow", "ash"];
    public static IReadOnlyList<string> MarkerVisuals { get; } = ["objective-ring", "chip"];
    public static IReadOnlyList<string> ZoneStyleIds => ZoneStyles.All;

    public static string TextureFile(string name) => $"res://content/builtin/textures/{name}.png";
    public static string? TerrainMeshFile(string name) => Terrain.TryGetValue(name, out var t) ? t.MeshFile ?? $"res://content/builtin/terrain/{name}.glb" : null;

    public static IReadOnlyList<TerrainVolume> VolumesOf(string name) => [new TerrainVolume(Guid.CreateVersion7(), Terrain[name].Volume)];

    /// <summary>A placement of a built-in: the volumes are copied on add, so the draft is self-contained (contracts plan spec correction 1).</summary>
    public static TerrainPlacement Place(string name, Point3 position, Yaw yaw, double scale = 1) =>
        new(Guid.CreateVersion7(), VisualRef.OfBuiltin(name), position, yaw, scale, VolumesOf(name));
}
```

- [ ] **Step 3: Write `TerrainGenerators.cs`**

```csharp
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

public enum Planet { Generic, Hospitable, Wasteland, Death }

/// <summary>Grimdark Future p.18–19: nine fixed pieces, then 4D3 more each rolled on 2D6. Runs on the client; the result is a batch of adds.</summary>
public static class TerrainGenerators
{
    static readonly Dictionary<Planet, string[]> FixedPieces = new()
    {
        [Planet.Generic] = ["barricade", "barricade", "large-forest", "large-field", "large-sight-blocker", "large-sight-blocker", "large-sight-blocker", "large-sight-blocker", "large-sight-blocker"],
        [Planet.Hospitable] = ["wall-or-fence", "wall-or-fence", "hill", "plantation", "forest", "forest", "forest", "large-building", "large-building"],
        [Planet.Wasteland] = ["small-dunes", "small-dunes", "large-rock", "crater", "large-building", "large-building", "large-building", "toxic-smog", "toxic-smog"],
        [Planet.Death] = ["overgrown-ruins", "overgrown-ruins", "deadly-vegetation", "hill", "dense-vegetation", "dense-vegetation", "dense-vegetation", "steep-hill", "steep-hill"],
    };

    // 2D6 → the entry's alternatives, index 0 = a roll of 2.
    static readonly Dictionary<Planet, string[][]> Table = new()
    {
        [Planet.Generic] = [["large-hill"], ["large-hill"], ["large-ruins"], ["large-field"], ["barricade"], ["barricade"], ["barricade"], ["large-forest"], ["large-sight-blocker"], ["large-lake"], ["large-lake"]],
        [Planet.Hospitable] = [["deep-river"], ["shallow-river"], ["tall-crops-field"], ["ruins"], ["forest", "plantation"], ["hill", "forest", "tall-crops-field"], ["hill"], ["wall-or-fence"], ["large-building"], ["small-buildings"], ["steep-hill"]],
        [Planet.Wasteland] = [["toxic-river"], ["sludge-stream"], ["sludge-field"], ["ruins"], ["large-rock"], ["small-dunes"], ["crater"], ["toxic-smog"], ["large-building"], ["mine-field"], ["small-buildings"]],
        [Planet.Death] = [["death-marsh"], ["crumbly-rock"], ["overgrown-ruins"], ["deadly-vegetation"], ["dense-vegetation"], ["dense-vegetation", "hill"], ["hill"], ["small-buildings"], ["steep-hill"], ["beast-lair"], ["quicksand"]],
    };

    public static IReadOnlyList<string> Fixed(Planet planet) => FixedPieces[planet];
    public static IReadOnlyList<string> Rolled(Planet planet, int twoD6) => Table[planet][Math.Clamp(twoD6, 2, 12) - 2];

    /// <summary>The pieces land beside the long edge, off the slab, in rows 8" apart, four to a row; players drag them on.</summary>
    public static IReadOnlyList<AddTerrain> Generate(Planet planet, TableSize table, Random rng)
    {
        var names = new List<string>(Fixed(planet));
        var extra = Enumerable.Range(0, 4).Sum(_ => rng.Next(1, 4));                        // 4D3
        for (var i = 0; i < extra; i++)
        {
            var alternatives = Rolled(planet, rng.Next(1, 7) + rng.Next(1, 7));            // 2D6
            names.Add(alternatives[rng.Next(alternatives.Length)]);
        }
        return names.Select((name, i) => new AddTerrain(BuiltinContent.Place(name, new Point3(table.Width + 4 + (i % 4) * 8, 4 + (i / 4) * 8, 0), new Yaw(0)))).ToList();
    }
}
```

- [ ] **Step 4: Author the fifteen art files.** **This step is not code, and no code in this plan produces its output.** It is a person with a modelling tool and an image editor making fifteen files and copying them into `client/content/builtin/`. Nothing else in the plan depends on it: every step before and after passes with the directory empty, and the table renders as its hitboxes until the files exist. The owner ruled on 2026-09-06 that a minimal art set ships, so a generated table looks like a table; the minimum is these fifteen.

  **`client/content/builtin/terrain/{group}.glb` — ten meshes**, one per group above. Format glTF 2.0 binary, one mesh, one `BaseColor`-only PBR material with no texture (the material carries the colour; the slab owns the only image), no animation, no skin, no camera, no light, under 2000 triangles and under 256 KB each. **Authored in the unit box, in the table frame:** x and y in [−0.5, 0.5], z in [0, 1], z up, origin at the footprint centre and the base at z = 0 — `MeshLoader.LoadBuiltin` scales that box onto each placement's volume (Task 11), so one file serves a group with no per-piece variant. Each must read correctly at both extremes of its group: `water` is stretched from a 4"-radius lake to a 3"×12" river, `hill` from a 2"-radius mound to 6"×6", `pad` from 0.1" to 1" tall. Silhouette matters and detail does not, since the camera is a table-height orbit at 24"–96".
  - `hill` a smooth dome with a flat walkable crown; `woods` a ring of trunks with canopies above the crown, hollow in the middle so a base placed inside is visible (these carry `SeeInAndOut`); `ruins` two or three broken wall stubs on a rubble pad; `building` a closed box with a pitched roof and cut window and door reliefs; `wall` a straight low barrier along +x with posts; `rock` an angular boulder cluster; `field` a low mat of crop rows; `water` a flat surface inset just below z = 1 with a lipped bank around it; `pad` a shallow flat dish, which is the crater and every ground hazard; `haze` a soft cloud of two or three nested translucent shells.

  **`client/content/builtin/textures/{name}.png` — five tables**, `grass`, `desert`, `urban`, `snow`, `ash`, matching `BuiltinContent.Textures`. Seamless and tileable, 1024×1024, sRGB, no alpha, under 1.5 MB each. `SlabNode.Set` sets `Uv1Scale` from `width/12`, so one tile is a 12"×12" square of table: author them at that scale, not as a whole table.

  **What is not needed.** No mesh for a marker: `MarkerNode` builds its disc and `Label3D` procedurally, so `objective-ring` and `chip` are ids and not files. No mesh and no texture for a zone: `ZoneNode` builds flat `ArrayMesh` fans from `ZoneStyles.Resolve`. No mesh for a model: army models come from the player's own assets through the manifest and the cache.

  **These ship with the build and never enter the asset cache.** They are `res://` resources bundled into the export's `.pck`, read by `GltfDocument`/`ResourceLoader` from the local file (Task 11), with no hash, no manifest entry, no presigned URL and no `user://cache/` write. The content-addressed cache is only for a player's uploaded assets, which arrive over HTTP and are keyed by SHA-256. Adding a built-in therefore means shipping a new client build, which is the trade the ruling accepts.

- [ ] **Step 5: Run `dotnet test tests/Tabletop.Client.Tests`; expect 35 passed. Commit: `Ship the built-in terrain inventory and the rulebook's four terrain generators`.** The art files, if Step 4 has produced them, are a separate commit: `Add the built-in terrain meshes and table textures`.

### Task 7: The content-addressed asset cache

**Files:**
- Create: `client/src/Core/AssetCache.cs`
- Test: `tests/Tabletop.Client.Tests/AssetCacheTests.cs`

**Interfaces:**
- Consumes: `ManifestEntry`, `ManagementClient.GetManifest/GetDownload` (Task 3).
- Produces: `sealed class AssetCache(string root, HttpClient http)` — `string PathFor(string hash)` (`{root}/{hash}.bin`), `bool Has(string hash)`, `Task<string> FetchAsync(string hash, Uri url, CancellationToken ct)` (downloads to a `.part` file, verifies SHA-256 equals `hash`, moves into place; throws `HashMismatchException` and deletes the part on mismatch; returns the path; a second call for a cached hash never touches the network), `static string Sha256Hex(ReadOnlySpan<byte>)`; `sealed class HashMismatchException(string expected, string actual) : Exception` with `Expected`, `Actual`.
- The hash is the key, so the same mesh in two games is one download; the built-ins never enter the cache (they load from `res://`).

Spec: client shell → The table view ("Assets load at runtime through `GLTFDocument` from a presigned URL, never through editor import. A content-addressed cache on disk means a blob is fetched once per machine: the hash is the key"); asset library → Two records (blob keyed by hash); management plan → The manifest (`ManifestEntry.Hash` is the blob's SHA-256; presigned GET URLs valid 60 minutes).

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using System.Security.Cryptography;
using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class AssetCacheTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("tabletop-cache-tests").FullName;
    public void Dispose() => Directory.Delete(_dir, true);
    static readonly byte[] Glb = "glTF fake bytes"u8.ToArray();
    static string Hash(byte[] b) => Convert.ToHexStringLower(SHA256.HashData(b));

    [Fact]
    public async Task Downloads_once_verifies_and_serves_from_disk_afterwards()
    {
        var handler = new BytesHandler(Glb);
        var cache = new AssetCache(_dir, new HttpClient(handler));
        var hash = Hash(Glb);
        Assert.False(cache.Has(hash));
        var path = await cache.FetchAsync(hash, new Uri("http://localhost:9000/tabletop-blobs/o/h"), CancellationToken.None);
        Assert.Equal(cache.PathFor(hash), path);
        Assert.Equal(Glb, await File.ReadAllBytesAsync(path));
        Assert.True(cache.Has(hash));
        await cache.FetchAsync(hash, new Uri("http://localhost:9000/never"), CancellationToken.None);
        Assert.Equal(1, handler.Calls);
        Assert.Empty(Directory.GetFiles(_dir, "*.part"));
    }

    [Fact]
    public async Task A_mismatching_hash_is_refused_and_nothing_is_kept()
    {
        var cache = new AssetCache(_dir, new HttpClient(new BytesHandler(Glb)));
        var wrong = new string('a', 64);
        var ex = await Assert.ThrowsAsync<HashMismatchException>(() => cache.FetchAsync(wrong, new Uri("http://localhost:9000/x"), CancellationToken.None));
        Assert.Equal((wrong, Hash(Glb)), (ex.Expected, ex.Actual));
        Assert.False(cache.Has(wrong));
        Assert.Empty(Directory.GetFiles(_dir));
    }

    sealed class BytesHandler(byte[] bytes) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }); }
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `AssetCache.cs`**

```csharp
using System.Security.Cryptography;

namespace Tabletop.Client.Core;

public sealed class HashMismatchException(string expected, string actual) : Exception($"Downloaded bytes hash to {actual}, expected {expected}.")
{
    public string Expected { get; } = expected;
    public string Actual { get; } = actual;
}

/// <summary>Blobs by SHA-256 on disk. Fetched once per machine; the same mesh in two games is one download.</summary>
public sealed class AssetCache(string root, HttpClient http)
{
    readonly SemaphoreSlim _gate = new(4, 4);

    public string PathFor(string hash) => Path.Combine(root, $"{hash}.bin");
    public bool Has(string hash) => File.Exists(PathFor(hash));

    public async Task<string> FetchAsync(string hash, Uri url, CancellationToken ct)
    {
        var path = PathFor(hash);
        if (File.Exists(path)) return path;
        Directory.CreateDirectory(root);
        var part = Path.Combine(root, $"{hash}.{Guid.CreateVersion7():N}.part");
        await _gate.WaitAsync(ct);
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            using var sha = SHA256.Create();
            await using (var file = File.Create(part))
            await using (var hashing = new CryptoStream(file, sha, CryptoStreamMode.Write))
                await response.Content.CopyToAsync(hashing, ct);
            var actual = Convert.ToHexStringLower(sha.Hash!);
            if (actual != hash) { File.Delete(part); throw new HashMismatchException(hash, actual); }
            if (File.Exists(path)) File.Delete(part); else File.Move(part, path);
            return path;
        }
        catch (Exception) when (File.Exists(part)) { File.Delete(part); throw; }
        finally { _gate.Release(); }
    }

    public static string Sha256Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
```

- [ ] **Step 3: Run `dotnet test tests/Tabletop.Client.Tests`; expect 37 passed. Commit: `Add the content-addressed asset cache`.**

### Task 8: View models — log text, the Ask view, the menu, the ruler, the animation queue

**Files:**
- Create: `client/src/Core/Names.cs`, `client/src/Core/LogText.cs`, `client/src/Core/AskViewModel.cs`, `client/src/Core/MenuModel.cs`, `client/src/Core/MoveRuler.cs`, `client/src/Core/AnimationQueue.cs`
- Test: `tests/Tabletop.Client.Tests/ViewModelTests.cs`

**Interfaces:**
- Consumes: `Projection`, `UnitInPlay`, `ModelInPlay`, `Frame`, `OpenAsk`, `Ask`, `LegalSet` kinds, `AskAnswer` kinds, `GameEvent`, every `EventPayload`, `Command` kinds, `MoveRegion` (with `DifficultVolumeIds`, `DifficultCap` and `Containment`) and `MoveContainment` (contracts plan Task 8), `Inside` (Protocol); `GameInfo` (Protocol); `Movement`, `Containment.BaseFullyWithin`, `PlacedModel`, `ModelBody`, `Base`, `WorldVolume`, `Point2`, `TableSize` (Geometry).
- Produces: `sealed class Names(GameInfo game, Projection? projection)` — `string Participant(Guid?)`, `string Team(Guid)` (`"Team {ordinal}"`), `string Unit(Guid)`, `string Model(Guid)`, `string Piece(Guid)` (unit, model, marker or the id's first eight characters); `static class LogText` — `string Describe(GameEvent, Names)`, `string Answer(AskAnswer, Names)` (an answer in words, naming the pieces it carries), `IReadOnlyList<Guid> Touched(GameEvent)`; `enum AskMode { Choose, PickPieces, Allocate, Assign, Move, TokenSpend, Confirm }`; `sealed record AskView(Ask Ask, AskMode Mode, IReadOnlyList<Guid> Highlights, IReadOnlyList<string> AnsweredTeams, bool AutoPass)`; `static class AskViewModel` — `AskView? Mine(Projection, GameInfo, Guid? myTeam, IReadOnlySet<string> muted)` (the first open Ask addressed to my team that my team has not answered), `string? WaitingFor(Projection, GameInfo, Guid? myTeam)` (the teams other open Asks name), `string? AnsweredBy(IReadOnlyList<GameEvent> log, Guid askId, Names)`, `AskMode ModeOf(LegalSet)`, `IReadOnlyDictionary<Guid, int> SpendableBy(Projection, GameInfo, Guid team, TokenSpend)` (the spend map narrowed to the models of one team), `string ConfirmLabel(Ask)` (`"End the round"` for `AskQuestions.EndRound`, else `"Continue"`); `enum MenuInput { None, Integer, Text, PieceId, TablePoint }` (`TablePoint` is the next left click on the table, handed to `Build` as `"x,y,z"`); `sealed record MenuItem(string Label, MenuInput Input, Func<string?, Command?> Build)`; `static class MenuModel` — `ForModel(Projection, Guid modelId, IReadOnlyList<string> actionNames)`, `ForUnit(Projection, Guid unitId, IReadOnlyList<string> actionNames)`, `ForTeam(GameInfo, Projection, Guid teamId)`, `ForMarker(Guid markerId)`, `ForFrame(Projection)`, `IReadOnlyList<string> KnownActionNames(Projection, IReadOnlyList<GameEvent>)`; `sealed record RulerReadout(double Distance, double? Remaining, bool OverAllowance)`; `static class MoveRuler` — `RulerReadout Read(Point2 start, Point2 current, double? allowance)`, `double UsedInFrame(Frame, Guid modelId)`, `IReadOnlyList<Point2> Region(PlacedModel start, MoveRegion legal, IReadOnlyList<WorldVolume> volumes, TableSize)` (the `Movement.Region` boundary from the Ask's allowance, impassable and difficult ids and cap), `IReadOnlyList<Point2> ExitRegion(ModelBody body, PlacedModel container, MoveRegion legal, IReadOnlyList<WorldVolume> volumes, TableSize)` (the disembark region: the container's base grown by `Containment.ExitWithin`, one vertex per `Movement.Rays` ray, marched out in `Movement.Step` increments and bisected at the first refusal), `bool IsDisembark(MoveRegion legal)` (`legal.Containment?.ExitingId is not null`); `sealed class AnimationQueue` — `void Enqueue(IReadOnlyList<GameEvent>)`, `IReadOnlyList<GameEvent> Take(bool animate)` (everything when not animating, one when animating), `int Count`.
- Action names are not on the wire (the `action` rows never reach the client); `KnownActionNames` reads them from the activate Ask's option labels and from `ActionRecorded` events, and the menu falls back to free text — *Interfaces needed* item 5.
- **`Region` cannot serve a disembark, so there are two region functions.** `MoveRegion.Containment.ExitingId` non-null means the unit is *inside* the container and its models have no `Placement` (game flow → Inside a container: *"an occupant has no table position of its own"*), so there is no `start` to sweep rays from and `Movement.Region` is meaningless — `Allowance` is the action's remaining distance and binds nothing here (contracts plan Task 8). `IsDisembark(legal)` is that test, and `ExitRegion` answers instead: the legal area is every placement `Containment.BaseFullyWithin(candidate, container, ExitWithin)` admits, taken about the container's own `PlacedModel`. It is expressed as a **boundary**, not a predicate, so `MoveRegionNode` (Task 14) draws it with no change: `Movement.Rays` rays out of the container's base centre, each marched outward in `Movement.Step` increments and bisected eight times at the first refusal — the same loop shape `Movement.Region` uses, which is what keeps a ring of impassable terrain from re-admitting placements beyond it. Every candidate is also refused when it would leave the slab or touch an impassable volume, asked through `Movement.Check(candidate, its own centre, 0, impassable, [], null, size)`: distance zero, so `Check` samples exactly that placement and answers `CrossesImpassable` and `LeavesTable` for it. No new geometry primitive. The client draws; the server re-checks every model with `GfMoves.ExitPlacement` and leaves a model placed too far inside (interpreter plan Task 14), so this is rendering an offer, not a legality check.
- The region is drawn for **one** body — the lead model's, as `Region` already is. A unit whose models have different bases gets the lead's region and the server refuses the outliers individually; `ExitRegion` therefore takes a `ModelBody` rather than a `PlacedModel`, because an occupant has no placement to pass.

Spec: client shell → The log, undo and the menu (words with actor and sequence; clicking highlights what it touched; every property editable; each item one command; no admin mode, no confirmation dialog), Asks and windows (one panel; the legal set becomes choices and highlights; other teams' Asks are a waiting indicator; mutes auto-pass and the pass is still sent; sealed shows who answered; confirm gates), Input and presence (the ruler: displacement, remaining allowance, distance used from the frame), Animation (a queue between the projection and the renderer that never delays an Ask); scheduler → The turn is an Ask (options are unit × action), There is no round limit (the round advances only when a player answers the `end_round` Ask — ruled 2026-09-06; the client's whole share of it is two words: the button's label and the answer's log line), Nothing is an invariant (menu edits); game flow → Inside a container (a location, not a condition; an occupant has no table position of its own and disembarks relative to the container; enter or exit with any move action, never both in one activation, exit within 6"); geometry plan → `Movement.Region`, `Containment.BaseFullyWithin` and *Interfaces needed → Client shell plan*; rulebook p.15 `Transport(X)` — *"must stay fully within 6\" of it when exiting"* and *"only one model needs to reach the Transport for everyone to embark"*; rulebook p.13 `Caster(X)` — *"Models within 18\" in line of sight of the caster's unit may spend any number of spell tokens at the same time before rolling"* — and `Takedown`, whose shooter picks the model that takes the wound (owner rulings, 2026-09-06). The spend is a sealed `TokenSpend` carrying each eligible model's own maximum and the pick a `PieceSelection` over model ids, so this task's share of both is the mode, the highlights and the words.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class ViewModelTests
{
    static readonly GameInfo Game = Samples.Game;
    static EventApplier Applier(params EventPayload[] history) { var a = new EventApplier(Samples.Board, Game); a.ApplyAll(history.Select((p, i) => new GameEvent(i + 1, Samples.P1, p, null))); return a; }
    static readonly Ask Activate = new(Samples.AskId, AskQuestions.Activate, "Activate a unit", [Samples.Team1],
        new Options([new AskOption($"{Samples.Unit}:Advance", "Advance", [Samples.Unit]), new AskOption($"{Samples.Unit}:Hold", "Hold", [Samples.Unit])]), true, false, null, null);
    /// <summary>The other team's spender: a model of no unit this projection holds, so it resolves to no team and Names.Piece degrades it to its first eight characters. A fixed id keeps the words stable.</summary>
    static readonly Guid Spender = Guid.Parse("0198b7ac-1111-7000-8000-000000000001");
    /// <summary>The simultaneous spend (p.13): one maximum per eligible model, over both teams' models, sealed and passable. Team 1 may spend only its own.</summary>
    static readonly Ask SpendTokens = new(Guid.CreateVersion7(), AskQuestions.SpendTokens, "Spend spell tokens", [Samples.Team1, Samples.Team2],
        new TokenSpend(new Dictionary<Guid, int> { [Samples.Model] = 2, [Spender] = 1 }), true, true, "casting", Samples.Frame);
    /// <summary>A Takedown group picking the model it targets (p.14): one model of the target unit, min and max both 1, addressed to the attacker's team (interpreter plan, question `takedown_target`).</summary>
    static readonly Ask TakedownAsk = new(Guid.CreateVersion7(), "takedown_target", "Pick the model this Takedown group targets", [Samples.Team1], new PieceSelection([Samples.Model], 1, 1), false, false, null, Samples.Frame);
    /// <summary>The scheduler's round boundary (ruled 2026-09-06): unframed, unsealed, no pass, every team that has a participant.</summary>
    static readonly Ask EndRound = new(Guid.CreateVersion7(), AskQuestions.EndRound, "End the round", [Samples.Team1, Samples.Team2], new Confirmation(), false, false, null, null);

    [Fact]
    public void Log_lines_name_actor_and_subject()
    {
        var a = Applier(new ModelsMoved([new ModelRelocation(Samples.Model, new OnTable(new Placement(new Point3(0, 0, 0), new Yaw(0))), new OnTable(new Placement(new Point3(3, 4, 0), new Yaw(0))))]),
            new ScoreSet(Samples.Team1, 4), new TurnEnded(Samples.Team1, true, []), new Undone(1),
            new AskOpened(EndRound), new AskAnswered(EndRound.Id, Samples.Team1, new Confirmed(), false), new RoundStarted(3, [Samples.Team1, Samples.Team2], []),
            new ModelsMoved([Samples.Embark]), new ModelsMoved([Samples.Disembark]));
        var names = new Names(Game, a.Projection);
        var lines = a.Log.Select(e => LogText.Describe(e, names)).ToList();
        Assert.Equal("Clay moved Battle Brothers 5.0\"", lines[0]);
        Assert.Equal("Clay set Team 1's score to 4", lines[1]);
        Assert.Equal("Clay passed for Team 1", lines[2]);
        Assert.Equal("Clay undid back to #1", lines[3]);
        Assert.Equal("Clay asked Team 1, Team 2: End the round", lines[4]);
        Assert.Equal("Clay confirmed for Team 1", lines[5]);      // the end_round answer: a player ended the round (scheduler plan, ruled 2026-09-06)
        Assert.Equal("Clay started round 3", lines[6]);           // and the engine's own line under it says which round began (actor null in the real log: "the game started round 3")
        // Containment names the container: Names.Piece resolves a container that is a model, and degrades to its first eight characters otherwise.
        Assert.Equal($"Clay put Battle Brothers inside {Names.Short(Samples.Container)}", lines[7]);
        Assert.Equal($"Clay took Battle Brothers out of {Names.Short(Samples.Container)}", lines[8]);
        // p.15 embarks the whole unit when one model reaches the transport, so the answer is one relocation per model — and still one line.
        Assert.Equal(lines[7], LogText.Describe(new GameEvent(99, Samples.P1,
            new ModelsMoved([Samples.Embark, Samples.Embark with { ModelId = Guid.CreateVersion7() }]), null), names));
        Assert.Equal([Samples.Model], LogText.Touched(a.Log[0]));
        Assert.Equal("the game", new Names(Game, a.Projection).Participant(null));
    }

    /// <summary>The two rulings of 2026-09-06 in the log: who spent what over which models, and which model a Takedown picked (p.14).</summary>
    [Fact]
    public void An_answer_names_the_pieces_it_carries()
    {
        var a = Applier(new AskOpened(SpendTokens), new AskOpened(TakedownAsk),
            new AskAnswered(TakedownAsk.Id, Samples.Team1, new SelectedPieces([Samples.Model]), false),
            new AskClosed(SpendTokens.Id, [new TeamAnswer(Samples.Team1, new TokensSpent(new Dictionary<Guid, int> { [Samples.Model] = 2 })), new TeamAnswer(Samples.Team2, new TokensSpent(new Dictionary<Guid, int> { [Spender] = 1 }))]));
        var names = new Names(Game, a.Projection);
        var lines = a.Log.Select(e => LogText.Describe(e, names)).ToList();
        // A pick of one model reads as the model, so a takedown model is named on the line that answered (p.14 Takedown).
        Assert.Equal("Clay answered for Team 1: picked Battle Brothers (Battle Brother)", lines[2]);
        // The sealed spend reads on the line that closed it: each team's total, then the models it came from, ordered by name so the words are stable.
        Assert.Equal("Clay closed the question: Team 1 spent 2 tokens (Battle Brothers (Battle Brother) 2), Team 2 spent 1 token (0198b7ac 1)", lines[3]);
        Assert.Equal([Samples.Model], LogText.Touched(a.Log[2]));
        Assert.Equal([Samples.Model, Spender], LogText.Touched(a.Log[3]));
        Assert.Equal("picked 2 pieces", LogText.Answer(new SelectedPieces([Samples.Model, Spender]), names));   // more than one is still a count
    }

    [Fact]
    public void My_ask_is_the_one_addressed_to_my_team_and_others_are_a_wait()
    {
        var a = Applier(new AskOpened(Activate));
        var mine = AskViewModel.Mine(a.Projection, Game, Samples.Team1, new HashSet<string>());
        Assert.Equal((AskMode.Choose, false), (mine!.Mode, mine.AutoPass));
        Assert.Equal([Samples.Unit], mine.Highlights);
        Assert.Null(AskViewModel.Mine(a.Projection, Game, Samples.Team2, new HashSet<string>()));
        Assert.Equal("Team 1", AskViewModel.WaitingFor(a.Projection, Game, Samples.Team2));
        Assert.Null(AskViewModel.Mine(a.Projection, Game, null, new HashSet<string>()));      // a watcher is never asked
    }

    [Fact]
    public void A_muted_window_auto_passes_and_a_simultaneous_spend_shows_who_answered_without_what()
    {
        var a = Applier(new AskOpened(SpendTokens), new AskAnswered(SpendTokens.Id, Samples.Team2, null, false));
        var mine = AskViewModel.Mine(a.Projection, Game, Samples.Team1, new HashSet<string> { "casting" })!;
        Assert.True(mine.AutoPass);
        Assert.Equal(AskMode.TokenSpend, mine.Mode);
        // The spend names both teams' models (p.13 gives the caster's side +1 and the other side -1), and only my own are mine to spend or to highlight.
        Assert.Equal([Samples.Model], mine.Highlights);
        Assert.Equal(new Dictionary<Guid, int> { [Samples.Model] = 2 }, AskViewModel.SpendableBy(a.Projection, Game, Samples.Team1, (TokenSpend)SpendTokens.Legal));
        Assert.Equal(["Team 2"], mine.AnsweredTeams);
        Assert.Null(AskViewModel.Mine(a.Projection, Game, Samples.Team2, new HashSet<string>()));     // already answered
        Assert.Equal("Clay", AskViewModel.AnsweredBy(a.Log, SpendTokens.Id, new Names(Game, a.Projection)));
        Assert.Equal(AskMode.Confirm, AskViewModel.ModeOf(new Confirmation()));
        Assert.Equal(AskMode.Move, AskViewModel.ModeOf(new MoveRegion([Samples.Model], 6, [], [], null)));
        // A containment offer is still a move: the mode is the legal set's kind, and the containers it names are highlights too (p.14).
        var embarkable = new MoveRegion([Samples.Model], 6, [], [], null, new MoveContainment([Samples.Container], null, null));
        Assert.Equal(AskMode.Move, AskViewModel.ModeOf(embarkable));
        var moveAsk = new Ask(Guid.CreateVersion7(), "move", "Move up to 6\"", [Samples.Team1], embarkable, true, false, null, Samples.Frame);
        Assert.Equal([Samples.Model, Samples.Container], AskViewModel.Mine(Applier(new AskOpened(moveAsk)).Projection, Game, Samples.Team1, new HashSet<string>())!.Highlights);
        var exitAsk = moveAsk with { Id = Guid.CreateVersion7(), Question = "disembark", Legal = new MoveRegion([Samples.Model], 6, [], [], null, new MoveContainment([], Samples.Container, 6)) };
        Assert.Equal([Samples.Model, Samples.Container], AskViewModel.Mine(Applier(new AskOpened(exitAsk)).Projection, Game, Samples.Team1, new HashSet<string>())!.Highlights);
        var gate = new Ask(Guid.CreateVersion7(), AskQuestions.Confirm, "Continue: move", [Samples.Team1], new Confirmation(), false, false, null, Samples.Frame);
        Assert.Equal(["Continue", "End the round"], new[] { gate, EndRound }.Select(AskViewModel.ConfirmLabel));
        var both = Applier(new AskOpened(EndRound));                                                       // unframed and addressed to every team: mine for either of them
        Assert.Equal(AskMode.Confirm, AskViewModel.Mine(both.Projection, Game, Samples.Team2, new HashSet<string>())!.Mode);
        Assert.Equal("Team 2", AskViewModel.WaitingFor(both.Projection, Game, Samples.Team1));             // reported, but never rendered: the panel shows WaitingFor only when Mine is null
    }

    [Fact]
    public void Menu_items_build_the_commands_the_spec_lists()
    {
        var p = Applier().Projection;
        var actions = MenuModel.KnownActionNames(Applier(new AskOpened(Activate)).Projection, []);
        Assert.Equal(["Advance", "Hold"], actions);
        var unit = MenuModel.ForUnit(p, Samples.Unit, actions);
        Assert.IsType<RecordAction>(unit.Single(i => i.Label == "Mark as having taken Advance").Build(null));
        Assert.IsType<ClearActionRecord>(unit.Single(i => i.Label == "Clear the action record").Build(null));
        var shaken = unit.Single(i => i.Label == "Add a condition");
        Assert.Equal(("shaken", true), (((SetCondition)shaken.Build("shaken")!).Condition, ((SetCondition)shaken.Build("shaken")!).Present));
        var model = MenuModel.ForModel(p, Samples.Model, actions);
        Assert.Equal(3, ((SetWounds)model.Single(i => i.Label == "Set wounds").Build("3")!).Wounds);
        Assert.IsType<OnShelf>(((MoveModels)model.Single(i => i.Label == "Move to the shelf").Build(null)!).Moves.Single().To);
        Assert.Equal(MenuInput.PieceId, model.Single(i => i.Label == "Put inside a container").Input);
        Assert.IsType<Inside>(((MoveModels)model.Single(i => i.Label == "Put inside a container").Build(Samples.Unit.ToString())!).Moves.Single().To);
        Assert.Null(model.Single(i => i.Label == "Set wounds").Build("x"));
        // The manual override stays beside the action-driven path: a player may put a model in or take it out with no Ask open.
        var occupant = MenuModel.ForModel(Applier(new ModelsMoved([Samples.Embark])).Projection, Samples.Model, actions);
        var takeOut = occupant.Single(i => i.Label == "Take out of the container, here");
        Assert.Equal(MenuInput.TablePoint, takeOut.Input);
        Assert.Equal((typeof(Inside), typeof(OnTable)), (((MoveModels)takeOut.Build("14,12,0")!).Moves.Single().From.GetType(), ((MoveModels)takeOut.Build("14,12,0")!).Moves.Single().To.GetType()));
        Assert.DoesNotContain("Place on the table, here", occupant.Select(i => i.Label));
        var team = MenuModel.ForTeam(Game, p, Samples.Team1);
        Assert.Equal(2, ((SetScore)team.Single(i => i.Label == "Set score").Build("2")!).Score);
        Assert.IsType<SetActiveTeam>(team.Single(i => i.Label == "Make this the active team").Build(null));
        Assert.IsType<SetRound>(team.Single(i => i.Label == "Set the round").Build("3"));
        Assert.IsType<RemoveMarker>(MenuModel.ForMarker(Samples.Token.Id).Single(i => i.Label == "Remove").Build(null));
        var frame = MenuModel.ForFrame(Applier(new SequenceStarted(Samples.Frame, "attack-sequence", new SequenceContext(Samples.Unit, [], [], null))).Projection);
        Assert.Equal(Samples.Frame, ((AbandonSequence)frame.Single().Build(null)!).FrameId);
        Assert.Empty(MenuModel.ForFrame(p));
    }

    [Fact]
    public void The_ruler_reads_displacement_and_remaining_allowance()
    {
        Assert.Equal(new RulerReadout(5, 1, false), MoveRuler.Read(new Point2(0, 0), new Point2(3, 4), 6));
        Assert.Equal(new RulerReadout(5, null, false), MoveRuler.Read(new Point2(0, 0), new Point2(3, 4), null));
        Assert.True(MoveRuler.Read(new Point2(0, 0), new Point2(7, 0), 6).OverAllowance);
        var a = Applier(new SequenceStarted(Samples.Frame, "advance", new SequenceContext(Samples.Unit, [], [], "Advance")),
            new StepCompleted(Samples.Frame, "move", null, [], [new ModelsMoved([new ModelRelocation(Samples.Model, new OnTable(new Placement(new Point3(0, 0, 0), new Yaw(0))), new OnTable(new Placement(new Point3(2, 0, 0), new Yaw(0))))])]));
        Assert.Equal(2, MoveRuler.UsedInFrame(a.Projection.Stack[0], Samples.Model), 6);
        var start = new PlacedModel(new ModelBody(Base.Circle(1.26), 1.5), new Placement(new Point3(36, 24, 0), new Yaw(0)));
        var region = MoveRuler.Region(start, new MoveRegion([Samples.Model], 6, [], [], null), [], new TableSize(72, 48));
        Assert.Equal(Movement.Rays, region.Count);
        Assert.All(region, p => Assert.Equal(6, p.DistanceTo(new Point2(36, 24)), 6));
        var edge = MoveRuler.Region(start with { Placement = new Placement(new Point3(71, 24, 0), new Yaw(0)) }, new MoveRegion([Samples.Model], 6, [], [], null), [], new TableSize(72, 48));
        Assert.True(edge.Max(p => p.X) <= 72);
        var forest = new WorldVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Circle(1), new Point2(0, 0), new Yaw(0), 0, 8, TerrainDeclarations.Difficult, null), new TerrainTransform(new Point3(39, 24, 0), new Yaw(0), 1));
        var capped = MoveRuler.Region(start, new MoveRegion([Samples.Model], 12, [], [forest.Id], 6), [forest], new TableSize(72, 48));
        Assert.Equal(42, capped[0].X, 6);                                                        // the ray into the forest stops at the 6" cap
        Assert.Equal(24, capped[Movement.Rays / 2].X, 6);                                        // the ray away from it reaches the full 12"
    }

    /// <summary>
    /// A disembark has no start placement, so the region is the container's base grown by the exit distance
    /// (rulebook p.15: "must stay fully within 6\" of it when exiting") rather than the straight-line reach.
    /// </summary>
    [Fact]
    public void The_disembark_region_is_the_container_grown_by_the_exit_distance()
    {
        var bus = new PlacedModel(new ModelBody(Base.Square(2), 3), new Placement(new Point3(36, 24, 0), new Yaw(0)));
        var trooper = new ModelBody(Base.Circle(1), 1.5);
        var size = new TableSize(72, 48);
        var exit = new MoveRegion([Samples.Model], 6, [], [], null, new MoveContainment([], Samples.Container, 6));
        Assert.True(MoveRuler.IsDisembark(exit));
        Assert.False(MoveRuler.IsDisembark(new MoveRegion([Samples.Model], 6, [], [], null, new MoveContainment([Samples.Container], null, null))));
        Assert.False(MoveRuler.IsDisembark(new MoveRegion([Samples.Model], 6, [], [], null)));

        var region = MoveRuler.ExitRegion(trooper, bus, exit, [], size);
        Assert.Equal(Movement.Rays, region.Count);
        // Edge to edge: the bus's edge is 1" from its centre and the trooper's far rim is 0.5" past its own, so 36 + 1 + 6 − 0.5.
        Assert.Equal(42.5, region[0].X, 2);
        Assert.Equal(30.5, region[Movement.Rays / 4].Y, 2);
        Assert.All(region, p => Assert.True(Containment.BaseFullyWithin(
            new PlacedModel(trooper, new Placement(new Point3(p.X, p.Y, 0), new Yaw(0))), bus, 6)));
        // It is not a circle about the centre: the diagonal reaches further than the axis, because the square's corner is further out.
        Assert.True(region[Movement.Rays / 8].DistanceTo(new Point2(36, 24)) > region[0].DistanceTo(new Point2(36, 24)));

        // The slab clamps it, exactly as Movement.Region is clamped: a bus in the corner offers nothing off the table.
        var corner = MoveRuler.ExitRegion(trooper, bus with { Placement = new Placement(new Point3(1, 1, 0), new Yaw(0)) }, exit, [], size);
        Assert.All(corner, p => Assert.True(p.X >= 0.4 && p.Y >= 0.4));           // the 0.5" base radius is the whole clamp

        // An impassable volume beside the bus stops the rays that would end in it.
        var wall = new WorldVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Circle(2), new Point2(0, 0), new Yaw(0), 0, 6, TerrainDeclarations.Impassable, null), new TerrainTransform(new Point3(40, 24, 0), new Yaw(0), 1));
        var blocked = MoveRuler.ExitRegion(trooper, bus, exit with { ImpassableVolumeIds = [wall.Id] }, [wall], size);
        Assert.Equal(37.5, blocked[0].X, 1);                                             // stopped at the wall's near face, 0.5" of base short of it
        Assert.Equal(30.5, blocked[Movement.Rays / 4].Y, 2);                             // and the ray away from it is untouched
    }

    [Fact]
    public void The_animation_queue_drains_at_once_when_not_animating()
    {
        var q = new AnimationQueue();
        q.Enqueue([new GameEvent(1, null, new ScoreSet(Samples.Team1, 1), null), new GameEvent(2, null, new ScoreSet(Samples.Team1, 2), null)]);
        Assert.Single(q.Take(animate: true)); Assert.Equal(1, q.Count);
        Assert.Single(q.Take(animate: false)); Assert.Equal(0, q.Count);
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `Names.cs` and `LogText.cs`**

```csharp
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>Ids to words. Falls back to the id's first eight characters rather than throwing: a name is presentation.</summary>
public sealed class Names(GameInfo game, Projection? projection)
{
    public string Participant(Guid? id) => id is null ? "the game" : game.Participants.FirstOrDefault(p => p.Id == id)?.Name ?? Short(id.Value);
    public string Team(Guid id) => game.Teams.FirstOrDefault(t => t.Id == id) is { } t ? $"Team {t.Ordinal}" : Short(id);
    public string Unit(Guid id) => projection?.Units.GetValueOrDefault(id)?.Definition.Name ?? Short(id);
    public string Model(Guid id) => projection?.Models.GetValueOrDefault(id) is { } m ? $"{Unit(m.UnitId)} ({m.Slot.Name})" : Short(id);
    public Guid? UnitOfModel(Guid modelId) => projection?.Models.GetValueOrDefault(modelId)?.UnitId;
    public string Piece(Guid id) => projection is null ? Short(id)
        : projection.Units.ContainsKey(id) ? Unit(id) : projection.Models.ContainsKey(id) ? Model(id) : projection.Markers.GetValueOrDefault(id)?.Label ?? Short(id);
    public static string Short(Guid id) => id.ToString("N")[..8];
}
```

```csharp
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>Every event in words, with its actor. Presence never appears here because presence is never an event.</summary>
public static class LogText
{
    public static string Describe(GameEvent e, Names n)
    {
        var who = n.Participant(e.Actor);
        var text = e.Payload switch
        {
            ModelsMoved m => Moved(m, n),
            WoundsSet w => $"set {n.Model(w.ModelId)} to {w.Wounds} wound{(w.Wounds == 1 ? "" : "s")}",
            ConditionSet c => $"{(c.Present ? "marked" : "cleared")} {(c.Subject.Kind == SubjectKind.Unit ? n.Unit(c.Subject.Id) : n.Model(c.Subject.Id))} {c.Condition}",
            ActionRecorded a => $"recorded {a.Action} for {n.Unit(a.UnitId)}",
            ActionRecordCleared a => $"cleared {n.Unit(a.UnitId)}'s action record",
            GrantAdded g => $"granted {g.Grant.EntryName} to {n.Unit(g.UnitId)}",
            GrantRemoved g => $"removed a grant from {n.Unit(g.UnitId)}",
            SpellTokensSet s => $"set {n.Model(s.ModelId)} to {s.Tokens} spell token{(s.Tokens == 1 ? "" : "s")}",
            WoundsThisActivationSet w => $"set {n.Unit(w.UnitId)}'s wounds this activation to {w.Wounds}",
            ScoreSet s => $"set {n.Team(s.TeamId)}'s score to {s.Score}",
            MarkerPlaced m => $"placed a {m.Marker.Kind.ToString().ToLowerInvariant()}{(m.Marker.Label is null ? "" : $" '{m.Marker.Label}'")}",
            MarkerMoved m => $"moved marker {n.Piece(m.MarkerId)}",
            MarkerRemoved m => $"removed marker {n.Piece(m.MarkerId)}",
            RoundStarted r => $"started round {r.Round}",
            TurnStarted t => $"started {n.Team(t.TeamId)}'s turn",
            TurnEnded t => t.Passed ? $"passed for {n.Team(t.TeamId)}" : $"ended {n.Team(t.TeamId)}'s turn",
            PhaseStarted p => $"began {p.Phase}",
            SequenceStarted s => $"began {s.Sequence}{(s.Context.ActorUnitId is { } u ? $" with {n.Unit(u)}" : "")}",
            StepCompleted s => Step(s),
            SequenceFinished => "finished the sequence",
            SequenceAbandoned => "abandoned the sequence",
            AskOpened a => $"asked {string.Join(", ", a.Ask.Teams.Select(n.Team))}: {a.Ask.Prompt}",
            // "confirmed" is what a confirm gate and the round boundary's end_round Ask both are; the round_started line under it says which round began.
            AskAnswered a => a.Answer switch
            {
                Passed => $"passed for {n.Team(a.TeamId)}",
                Confirmed => $"confirmed for {n.Team(a.TeamId)}",
                // An unsealed answer is in hand, so it is in words: a takedown's pick has to name the model it picked (p.13).
                { } answer => $"answered for {n.Team(a.TeamId)}: {Answer(answer, n)}{(a.OutsideLegalSet ? " (outside the legal set)" : "")}",
                null => $"answered for {n.Team(a.TeamId)}{(a.OutsideLegalSet ? " (outside the legal set)" : "")}",   // withheld: a sealed Ask reveals at ask_closed
            },
            AskClosed a => a.Answers.Count == 0 ? "closed the question"
                : $"closed the question: {string.Join(", ", a.Answers.Select(x => $"{n.Team(x.TeamId)} {Answer(x.Answer, n)}"))}",   // a sealed Ask's reveal
            DiceRerolled d => $"rerolled {d.Replaced.Count} dice",
            DiceEdited d => $"set {d.Replaced.Count} dice by hand",
            ModifierAdded m => $"added {m.Modifier.Amount:+#;-#;0} to the {m.Roll} roll",
            Undone u => $"undid back to #{u.ToSeq}",
            var other => other.GetType().Name,
        };
        return $"{who} {text}" + (e.Warning is null ? "" : $" — {e.Warning}");
    }

    static string Moved(ModelsMoved m, Names n)
    {
        var first = m.Moves[0];
        var unit = n.UnitOfModel(first.ModelId) is { } unitId ? n.Unit(unitId) : Names.Short(first.ModelId);
        return (first.From, first.To) switch
        {
            (OnTable a, OnTable b) => $"moved {unit} {a.Placement.Position.Horizontal.DistanceTo(b.Placement.Position.Horizontal):0.0}\"",
            (OnShelf, OnTable) => $"deployed {unit}",
            (Inside i, OnTable) => $"took {unit} out of {n.Piece(i.ContainerId)}",
            (_, OnShelf) => $"moved {unit} to the shelf",
            (_, Inside i) => $"put {unit} inside {n.Piece(i.ContainerId)}",
            _ => $"moved {unit}",
        };
    }
    /// <summary>
    /// An answer in words. A sealed Ask's answers reach the client only inside <c>ask_closed</c>, which is the reveal.
    /// Names is taken because two answers have to name pieces (both ruled 2026-09-06, both p.14): a one-piece selection is
    /// a Takedown's chosen model, and a token spend is a count per model rather than one number.
    /// </summary>
    public static string Answer(AskAnswer a, Names n) => a switch
    {
        Chosen c => $"chose {c.OptionId}",
        SelectedPieces { PieceIds.Count: 1 } s => $"picked {n.Piece(s.PieceIds[0])}",
        SelectedPieces s => $"picked {s.PieceIds.Count} piece{(s.PieceIds.Count == 1 ? "" : "s")}",
        Allocated al => $"allocated {al.Counts.Values.Sum()}",
        Assigned => "assigned targets",
        Destinations d => $"moved {d.Moves.Count} model{(d.Moves.Count == 1 ? "" : "s")}",
        TokensSpent t => $"spent {t.Counts.Values.Sum()} token{(t.Counts.Values.Sum() == 1 ? "" : "s")} ({Parts(t.Counts, n)})",
        Confirmed => "confirmed",
        Passed => "passed",
        _ => "answered",
    };

    /// <summary>Which model gave what, ordered by the name so the words do not depend on a dictionary's order.</summary>
    static string Parts(IReadOnlyDictionary<Guid, int> counts, Names n) =>
        string.Join(", ", counts.Where(kv => kv.Value > 0).Select(kv => $"{n.Piece(kv.Key)} {kv.Value}").Order(StringComparer.Ordinal));

    static string Step(StepCompleted s)
    {
        if (s.Rolls.Count == 0) return $"completed {s.Step}";
        var dice = s.Rolls.SelectMany(r => r.Dice).ToList();
        return $"completed {s.Step}: {dice.Count(d => d.Success)} of {dice.Count} succeeded";
    }

    /// <summary>What clicking the entry highlights on the table.</summary>
    public static IReadOnlyList<Guid> Touched(GameEvent e) => e.Payload switch
    {
        ModelsMoved m => m.Moves.Select(x => x.ModelId).ToList(),
        WoundsSet w => [w.ModelId], SpellTokensSet s => [s.ModelId],
        ConditionSet c => [c.Subject.Id], ActionRecorded a => [a.UnitId], ActionRecordCleared a => [a.UnitId],
        GrantAdded g => [g.UnitId], GrantRemoved g => [g.UnitId], WoundsThisActivationSet w => [w.UnitId],
        MarkerPlaced m => [m.Marker.Id], MarkerMoved m => [m.MarkerId], MarkerRemoved m => [m.MarkerId],
        SequenceStarted s => new[] { s.Context.ActorUnitId }.Where(x => x is not null).Select(x => x!.Value).Concat(s.Context.TargetUnitIds).ToList(),
        StepCompleted s => s.Changes.SelectMany(c => Touched(new GameEvent(0, null, c, null))).ToList(),
        AskOpened a => a.Ask.Legal is Options o ? o.Choices.SelectMany(c => c.Pieces).Distinct().ToList() : a.Ask.Legal is PieceSelection p ? p.Eligible : [],
        // An answer names pieces too: the model a Takedown picked, and the models a team spent its tokens from (p.14).
        AskAnswered a => a.Answer is null ? [] : AnswerPieces(a.Answer),
        AskClosed a => a.Answers.SelectMany(x => AnswerPieces(x.Answer)).Distinct().ToList(),
        _ => [],
    };

    static IReadOnlyList<Guid> AnswerPieces(AskAnswer a) => a switch
    {
        SelectedPieces s => s.PieceIds,
        Allocated al => al.Counts.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList(),
        TokensSpent t => t.Counts.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList(),
        Assigned a2 => a2.Targets.Values.Distinct().ToList(),
        Destinations d => d.Moves.Select(m => m.ModelId).ToList(),
        _ => [],
    };
}
```

`Moved` names the unit the first moved model belongs to, through `Names.UnitOfModel`; a unit-wide drag is several relocations and one line. That is what makes the embark line right: p.15 embarks the **whole** unit when one model reaches the transport, so the answer is one relocation per model to the same `Inside`, and the log reads *"Clay put Battle Brothers inside Bus (Bus)"* once, naming the container through `Names.Piece` — a model id, so `Names.Model` resolves it, and an unknown id degrades to its first eight characters like every other name here. The disembark reads *"took … out of …"* rather than falling through to the bare *"moved"*.

- [ ] **Step 3: Write `AskViewModel.cs`**

```csharp
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

public enum AskMode { Choose, PickPieces, Allocate, Assign, Move, TokenSpend, Confirm }

/// <summary>What the one Ask panel renders. Highlights are the pieces the legal set names.</summary>
public sealed record AskView(Ask Ask, AskMode Mode, IReadOnlyList<Guid> Highlights, IReadOnlyList<string> AnsweredTeams, bool AutoPass);

public static class AskViewModel
{
    public static AskMode ModeOf(LegalSet legal) => legal switch
    {
        Options => AskMode.Choose, PieceSelection => AskMode.PickPieces, Allocation => AskMode.Allocate,
        Assignment => AskMode.Assign, MoveRegion => AskMode.Move, TokenSpend => AskMode.TokenSpend, Confirmation => AskMode.Confirm,
        _ => throw new ArgumentOutOfRangeException(nameof(legal)),
    };

    /// <summary>
    /// The spend map narrowed to one team's own models. p.13's window addresses both teams — the caster's side spends for
    /// +1 and the other side for -1 — so <c>TokenSpend.MaxByModel</c> names models this player may not spend, and a spin box
    /// over one of them would be a control the player cannot use. The map itself discloses nothing: every projection already
    /// carries each model's <c>SpellTokens</c>. A model whose unit or participant this projection cannot resolve is not mine.
    /// </summary>
    public static IReadOnlyDictionary<Guid, int> SpendableBy(Projection p, GameInfo game, Guid team, TokenSpend legal) =>
        legal.MaxByModel.Where(kv => TeamOfModel(p, game, kv.Key) == team).ToDictionary(kv => kv.Key, kv => kv.Value);

    static Guid? TeamOfModel(Projection p, GameInfo game, Guid modelId) =>
        p.Models.GetValueOrDefault(modelId) is { } m && p.Units.GetValueOrDefault(m.UnitId) is { } unit
            ? game.Participants.FirstOrDefault(x => x.Id == unit.ParticipantId)?.TeamId
            : null;

    /// <summary>The first open Ask addressed to my team that my team has not answered. Null for a watcher: a participant with no team is never Asked. `myTeam` comes from the participant's row by way of `Welcome`, so a player who reconnects mid-Ask is addressed by it again (owner ruling 1, 2026-09-06); callers pass null only for a real watcher, never for a client that is mid-reconnect.</summary>
    public static AskView? Mine(Projection p, GameInfo game, Guid? myTeam, IReadOnlySet<string> muted)
    {
        if (myTeam is not { } team) return null;
        var open = p.OpenAsks.Values.OrderBy(a => a.Ask.Id).FirstOrDefault(a => a.Ask.Teams.Contains(team) && !a.Answers.ContainsKey(team));
        if (open is null) return null;
        var names = new Names(game, p);
        var ask = open.Ask;
        var highlights = ask.Legal switch
        {
            Options o => o.Choices.SelectMany(c => c.Pieces).Distinct().ToList(),
            PieceSelection s => s.Eligible, Allocation a => a.Targets, Assignment a => a.Targets, MoveRegion m => MoveHighlights(m),
            TokenSpend t => SpendableBy(p, game, team, t).Keys.ToList(),          // my own spenders only (p.13)
            _ => (IReadOnlyList<Guid>)[],
        };
        var autoPass = ask.WindowKind is { } kind && muted.Contains(kind) && ask.AllowPass;
        return new AskView(ask, ModeOf(ask.Legal), highlights, open.Answers.Keys.Select(names.Team).ToList(), autoPass);
    }

    /// <summary>
    /// The pieces a move Ask names: its models, plus the containers containment offers. An enterable container is a click
    /// target for the answer, and the container a unit is exiting anchors the region, so both are highlights — "the legal set
    /// from the server becomes the panel's choices and the table's highlights" (client shell → Asks and windows). An occupant
    /// has no node, so highlighting its model id is a no-op; the container's node is the one the player can see and click.
    /// </summary>
    static IReadOnlyList<Guid> MoveHighlights(MoveRegion m)
    {
        var ids = m.ModelIds.ToList();
        if (m.Containment is { } c) { ids.AddRange(c.Enterable); if (c.ExitingId is { } exiting) ids.Add(exiting); }
        return ids.Distinct().ToList();
    }

    /// <summary>The waiting indicator: which teams the other open Asks are addressed to.</summary>
    public static string? WaitingFor(Projection p, GameInfo game, Guid? myTeam)
    {
        var names = new Names(game, p);
        var teams = p.OpenAsks.Values.SelectMany(a => a.Ask.Teams.Where(t => !a.Answers.ContainsKey(t) && t != myTeam)).Distinct().Select(names.Team).ToList();
        return teams.Count == 0 ? null : string.Join(", ", teams);
    }

    /// <summary>
    /// What the one button of a Confirm Ask says. A step gate says Continue; the round boundary's end_round Ask (unframed, addressed to
    /// every team that has a participant) says what it does, because the round advances only when a player answers it — ruled 2026-09-06.
    /// </summary>
    public static string ConfirmLabel(Ask ask) => ask.Question == AskQuestions.EndRound ? "End the round" : "Continue";

    /// <summary>Who on the team answered: the ask_answered event's actor.</summary>
    public static string? AnsweredBy(IReadOnlyList<GameEvent> log, Guid askId, Names names) =>
        log.LastOrDefault(e => e.Payload is AskAnswered a && a.AskId == askId) is { } e ? names.Participant(e.Actor) : null;
}
```

- [ ] **Step 4: Write `MenuModel.cs`**

```csharp
using System.Globalization;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

public enum MenuInput { None, Integer, Text, PieceId, TablePoint }

/// <summary>One menu entry: a label, what it asks the player for, and the command it becomes. Null from Build means the input did not parse.</summary>
public sealed record MenuItem(string Label, MenuInput Input, Func<string?, Command?> Build);

/// <summary>Every property is editable, each item one ordinary command through the same path automation uses. No admin mode.</summary>
public static class MenuModel
{
    static int? Int(string? s) => int.TryParse(s, out var n) ? n : null;
    static Guid? Id(string? s) => Guid.TryParse(s, out var g) ? g : null;
    /// <summary>A TablePoint item's input: the wire-frame point the player clicked, as "x,y,z".</summary>
    static Point3? Point(string? s) => s?.Split(',') is [var x, var y, var z]
        && double.TryParse(x, CultureInfo.InvariantCulture, out var px) && double.TryParse(y, CultureInfo.InvariantCulture, out var py)
        && double.TryParse(z, CultureInfo.InvariantCulture, out var pz) ? new Point3(px, py, pz) : null;

    public static IReadOnlyList<MenuItem> ForUnit(Projection p, Guid unitId, IReadOnlyList<string> actionNames)
    {
        var items = new List<MenuItem>();
        foreach (var a in actionNames) items.Add(new($"Mark as having taken {a}", MenuInput.None, _ => new RecordAction(unitId, a)));
        items.Add(new("Mark as having taken…", MenuInput.Text, s => string.IsNullOrWhiteSpace(s) ? null : new RecordAction(unitId, s.Trim())));
        items.Add(new("Clear the action record", MenuInput.None, _ => new ClearActionRecord(unitId)));
        items.Add(new("Add a condition", MenuInput.Text, s => string.IsNullOrWhiteSpace(s) ? null : new SetCondition(new Subject(SubjectKind.Unit, unitId), s.Trim(), true)));
        foreach (var c in p.Units.GetValueOrDefault(unitId)?.Conditions ?? new HashSet<string>())
            items.Add(new($"Remove {c}", MenuInput.None, _ => new SetCondition(new Subject(SubjectKind.Unit, unitId), c, false)));
        items.Add(new("Set wounds this activation", MenuInput.Integer, s => Int(s) is { } n ? new SetWoundsThisActivation(unitId, n) : null));
        foreach (var g in p.Units.GetValueOrDefault(unitId)?.Grants ?? [])
            items.Add(new($"Remove grant {g.EntryName}", MenuInput.None, _ => new RemoveGrant(unitId, g.Id)));
        var models = p.Models.Values.Where(m => m.UnitId == unitId).ToList();
        items.Add(new("Move the unit to the shelf", MenuInput.None, _ => new MoveModels(models.Select(m => new ModelRelocation(m.Id, m.Location, new OnShelf())).ToList())));
        return items;
    }

    public static IReadOnlyList<MenuItem> ForModel(Projection p, Guid modelId, IReadOnlyList<string> actionNames)
    {
        var model = p.Models[modelId];
        var items = new List<MenuItem>
        {
            new("Set wounds", MenuInput.Integer, s => Int(s) is { } n ? new SetWounds(modelId, n) : null),
            new("Set spell tokens", MenuInput.Integer, s => Int(s) is { } n ? new SetSpellTokens(modelId, n) : null),
            new("Add a condition", MenuInput.Text, s => string.IsNullOrWhiteSpace(s) ? null : new SetCondition(new Subject(SubjectKind.Model, modelId), s.Trim(), true)),
            new("Move to the shelf", MenuInput.None, _ => new MoveModels([new ModelRelocation(modelId, model.Location, new OnShelf())])),
            new("Put inside a container", MenuInput.PieceId, s => Id(s) is { } c ? new MoveModels([new ModelRelocation(modelId, model.Location, new Inside(c))]) : null),
        };
        foreach (var c in model.Conditions) items.Add(new($"Remove {c}", MenuInput.None, _ => new SetCondition(new Subject(SubjectKind.Model, modelId), c, false)));
        // "move to the shelf or back", "put inside a container or take out": coming back is one relocation to OnTable,
        // and the position comes from the next click on the table (client shell -> the menu).
        if (model.Location is not OnTable)
            items.Add(new(model.Location is Inside ? "Take out of the container, here" : "Place on the table, here", MenuInput.TablePoint,
                s => Point(s) is { } at ? new MoveModels([new ModelRelocation(modelId, model.Location, new OnTable(new Placement(at, new Yaw(0))))]) : null));
        items.AddRange(ForUnit(p, model.UnitId, actionNames).Where(i => i.Label.StartsWith("Mark") || i.Label == "Clear the action record"));
        return items;
    }

    public static IReadOnlyList<MenuItem> ForTeam(GameInfo game, Projection p, Guid teamId) =>
    [
        new("Set score", MenuInput.Integer, s => Int(s) is { } n ? new SetScore(teamId, n) : null),
        new("Make this the active team", MenuInput.None, _ => new SetActiveTeam(teamId)),
        new("Set the round", MenuInput.Integer, s => Int(s) is { } n ? new SetRound(n) : null),
    ];

    public static IReadOnlyList<MenuItem> ForMarker(Guid markerId) => [new("Remove", MenuInput.None, _ => new RemoveMarker(markerId))];

    /// <summary>Abandon the top frame: pops it and everything above; applied effects stay applied.</summary>
    public static IReadOnlyList<MenuItem> ForFrame(Projection p) =>
        p.Stack.Count == 0 ? [] : [new($"Abandon {p.Stack[^1].Sequence}", MenuInput.None, _ => new AbandonSequence(p.Stack[^1].Id))];

    /// <summary>The action names the client has seen: the activate Ask's option labels and recorded actions. The rows themselves never reach the client.</summary>
    public static IReadOnlyList<string> KnownActionNames(Projection p, IReadOnlyList<GameEvent> log)
    {
        var names = new List<string>();
        foreach (var a in p.OpenAsks.Values.Where(a => a.Ask.Question == AskQuestions.Activate))
            if (a.Ask.Legal is Options o) names.AddRange(o.Choices.Select(c => c.Label));
        names.AddRange(log.Select(e => e.Payload).OfType<ActionRecorded>().Select(a => a.Action));
        names.AddRange(p.Units.Values.SelectMany(u => u.Actions));
        return names.Distinct().ToList();
    }
}
```

`Place on the table, here` / `Take out of the container, here` is a `TablePoint` item: `PieceMenu` (Task 15) waits for the next left click on the table and hands `Build` the wire-frame point as `"x,y,z"`. That is the "or back" and the "take out" half of the spec's list; the shelf and container items are the other half.

**"Put inside a container" and "Take out of the container, here" stay, beside the action-driven path.** The move Ask's `Containment` (Task 14) is how a player embarks or disembarks *as part of an action*, under the ruleset's own conditions — the transport in reach, not both in one activation, the exit inside 6". The menu items are the manual override the spec asks for by name (*"put inside a container or take out"*, client shell → The log, undo and the menu), and they are the only path when no Ask is open at all: a transport deploying with units inside (p.14: *"Transports may deploy with units inside"*, and deployment's Ask is a `Destinations` for the transport, not a containment offer), a correction after a misclick, or a game played under `warn`/`silent` enforcement where stepping outside the offer is how the players fix things. `MoveModels` is the same command either path sends, logged with the same actor.

- [ ] **Step 5: Write `MoveRuler.cs` and `AnimationQueue.cs`**

```csharp
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>Distance is the base's displacement from where it started (GF p.7); Remaining is what the allowance leaves.</summary>
public sealed record RulerReadout(double Distance, double? Remaining, bool OverAllowance);

public static class MoveRuler
{
    public static RulerReadout Read(Point2 start, Point2 current, double? allowance)
    {
        var d = start.DistanceTo(current);
        return new RulerReadout(d, allowance is { } a ? Math.Max(0, a - d) : null, allowance is { } b && d > b + 1e-6);
    }

    /// <summary>Distance a model has already moved inside a frame: the sum of its logged table-to-table relocations in the frame's completed steps.</summary>
    public static double UsedInFrame(Frame frame, Guid modelId) =>
        frame.Steps.SelectMany(s => s.Changes).OfType<ModelsMoved>().SelectMany(m => m.Moves)
            .Where(r => r.ModelId == modelId && r.From is OnTable && r.To is OnTable)
            .Sum(r => ((OnTable)r.From).Placement.Position.Horizontal.DistanceTo(((OnTable)r.To).Placement.Position.Horizontal));

    /// <summary>The reachable region: Movement.Region from the Ask's parameters — the allowance, the impassable and difficult volumes resolved by id from the board, and the cap GameRules computed. One frame, no conversion.</summary>
    public static IReadOnlyList<Point2> Region(PlacedModel start, MoveRegion legal, IReadOnlyList<WorldVolume> volumes, TableSize size)
    {
        var impassable = volumes.Where(v => legal.ImpassableVolumeIds.Contains(v.Id)).ToList();
        var difficult = volumes.Where(v => legal.DifficultVolumeIds.Contains(v.Id)).ToList();
        return Movement.Region(start, legal.Allowance, impassable, difficult, legal.DifficultCap, size).Boundary;
    }

    /// <summary>The unit is inside a container, so this move is a disembark and <see cref="Region"/> cannot serve it: there is no start placement to sweep from and Allowance binds nothing (contracts plan Task 8).</summary>
    public static bool IsDisembark(MoveRegion legal) => legal.Containment?.ExitingId is not null;

    /// <summary>
    /// The disembark region: rulebook p.15, "must stay fully within 6\" of it when exiting", measured edge to edge from the
    /// container's base — so it is the container's footprint grown by the exit distance, not a circle about its centre. One
    /// vertex per ray out of the container's base centre, marched outward in Movement.Step increments and bisected at the
    /// first refusal, which is the loop Movement.Region uses and is what stops a ring of impassable terrain re-admitting
    /// placements beyond it. The candidate takes the container's yaw and Z: an occupant has no placement of its own, and the
    /// drop settles the height locally while the server settles it for the log.
    /// </summary>
    public static IReadOnlyList<Point2> ExitRegion(ModelBody body, PlacedModel container, MoveRegion legal, IReadOnlyList<WorldVolume> volumes, TableSize size)
    {
        var within = legal.Containment?.ExitWithin ?? 0;
        var impassable = volumes.Where(v => legal.ImpassableVolumeIds.Contains(v.Id)).ToList();
        var origin = container.Placement.Position.Horizontal;
        var reach = within + Math.Max(body.Base.A, body.Base.B) + Math.Max(container.Body.Base.A, container.Body.Base.B);
        var boundary = new List<Point2>(Movement.Rays);
        for (var r = 0; r < Movement.Rays; r++)
        {
            var angle = 2 * Math.PI * r / Movement.Rays;
            var dir = new Point2(Math.Cos(angle), Math.Sin(angle));
            var reached = 0.0;
            for (var k = 1; ; k++)
            {
                var d = Math.Min(k * Movement.Step, reach);
                if (!Admits(body, container, within, origin + dir * d, impassable, size))
                {
                    var (lo, hi) = (reached, d);                                  // bisect the last step: eight halvings of 0.25" is under 0.001"
                    for (var i = 0; i < 8; i++)
                    {
                        var mid = (lo + hi) / 2;
                        if (Admits(body, container, within, origin + dir * mid, impassable, size)) lo = mid; else hi = mid;
                    }
                    reached = lo;
                    break;
                }
                reached = d;
                if (d >= reach) break;
            }
            boundary.Add(origin + dir * reached);
        }
        return boundary;
    }

    /// <summary>
    /// Is this one placement offered? Fully within the exit distance of the container's base, on the slab, and clear of the
    /// impassable volumes the Ask named. The last two are asked through Movement.Check with the candidate as both start and
    /// destination: the distance is zero, so Check samples exactly that placement and answers CrossesImpassable and
    /// LeavesTable for it — no new geometry primitive, and the same predicates the straight-line region uses.
    /// </summary>
    static bool Admits(ModelBody body, PlacedModel container, double within, Point2 at, IReadOnlyList<WorldVolume> impassable, TableSize size)
    {
        var candidate = new PlacedModel(body, new Placement(new Point3(at.X, at.Y, container.Placement.Position.Z), container.Placement.Yaw));
        if (!Containment.BaseFullyWithin(candidate, container, within)) return false;
        var check = Movement.Check(candidate, at, 0, impassable, [], null, size);
        return !check.CrossesImpassable && !check.LeavesTable;
    }
}
```

```csharp
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>Between the projection and the renderer. The projection has already applied everything; this only paces what the table plays out.</summary>
public sealed class AnimationQueue
{
    readonly Queue<GameEvent> _pending = new();
    public int Count => _pending.Count;
    public void Enqueue(IReadOnlyList<GameEvent> events) { foreach (var e in events) _pending.Enqueue(e); }

    /// <summary>Not animating: everything, at once. Animating: one event per call, so the table plays them out in order.</summary>
    public IReadOnlyList<GameEvent> Take(bool animate)
    {
        if (!animate) { var all = _pending.ToList(); _pending.Clear(); return all; }
        return _pending.Count == 0 ? [] : [_pending.Dequeue()];
    }
}
```

- [ ] **Step 6: Run `dotnet test tests/Tabletop.Client.Tests`; expect 45 passed. Commit: `Add the client view models: log text, the Ask view, the menu, the ruler with its disembark region, the animation queue`.**

### Task 9: `Main`, the dispatcher, screen swapping, the login and update-required screens

**Files:**
- Modify: `client/src/Main.cs`, `client/scenes/Main.tscn`
- Create: `client/scenes/Login.tscn`, `client/scenes/UpdateRequired.tscn`, `client/src/Screens/LoginScreen.cs`, `client/src/Screens/UpdateRequiredScreen.cs`, `client/src/Screens/GodotDispatcher.cs`

**Interfaces:**
- Consumes: `Session`, `SessionPhase`, `ManagementClient`, `ClientGameSocketFactory`, `SessionStore`, `ClientSettings`, `AssetCache`, `IDispatcher`, `ManagementException`, `UpdateRequiredException` (Tasks 2–7).
- Produces (namespace `Tabletop`): `partial class Main : Node` with `static Main Instance`, `Session Session`, `AssetCache Cache`, `HttpClient Http`, `void Show(string scenePath)` (frees the current screen, instantiates the next under `Main`), the 60 s refresh `Timer`; `sealed class GodotDispatcher(Node host) : IDispatcher` (`Callable.From(action).CallDeferred()`); `partial class LoginScreen : Control` (email, password, "Sign in", "Sign up", "Email me a code" → code field → "Verify", "Remember me", the error label); `partial class UpdateRequiredScreen : Control` (the message and a "Quit" button).
- Screen routing is `Session.PhaseChanged`: `LoggedOut → Login.tscn`, `LoggedIn → Home.tscn` (Task 10), `Connecting/Reconnecting/Connected → Game.tscn` (Task 13), `UpdateRequired → UpdateRequired.tscn`. On start `TryResumeAsync()` decides between Login and Home.

Spec: client shell → Structure (three screens, each a Godot scene swapped under the root; one session object beneath), Screens → Login (email and password, or email and a one-time code; remembered in the OS store when chosen), Home ("treats management's update required response as a screen of its own"); No social login; management API → The version check.

- [ ] **Step 1: Rewrite `client/scenes/Main.tscn` and write the two new scenes (minimal, by hand)**

```
[gd_scene load_steps=2 format=3]

[ext_resource type="Script" path="res://src/Main.cs" id="1_main"]

[node name="Main" type="Node"]
script = ExtResource("1_main")
```

`client/scenes/Login.tscn`:
```
[gd_scene load_steps=2 format=3]

[ext_resource type="Script" path="res://src/Screens/LoginScreen.cs" id="1"]

[node name="Login" type="Control"]
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
script = ExtResource("1")
```

`client/scenes/UpdateRequired.tscn` is the same with `UpdateRequiredScreen.cs` and root name `UpdateRequired`. The editor rewrites these on first save and adds `uid` attributes; that is expected, and the `.uid` files it creates beside each script are committed.

- [ ] **Step 2: Write `GodotDispatcher.cs` and `Main.cs`**

```csharp
using Godot;
using Tabletop.Client.Core;

namespace Tabletop.Client.Screens;

/// <summary>Session events arrive on socket and thread-pool threads; Godot nodes may only be touched on the main thread.</summary>
public sealed class GodotDispatcher : IDispatcher
{
    public void Post(Action action) => Callable.From(action).CallDeferred();
}
```

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Screens;

namespace Tabletop;

/// <summary>The root. Owns the session object and the asset cache; swaps one screen at a time beneath itself.</summary>
public partial class Main : Node
{
    public static Main Instance { get; private set; } = null!;
    public Session Session { get; private set; } = null!;
    public AssetCache Cache { get; private set; } = null!;
    public HttpClient Http { get; } = new();
    Node? _screen;
    string _settingsPath = "";

    public override void _Ready()
    {
        Instance = this;
        var userDir = OS.GetUserDataDir();
        _settingsPath = Path.Combine(userDir, "settings.json");
        var settings = ClientSettings.Load(_settingsPath);
        var store = new SessionStore(Path.Combine(userDir, "session.bin"));
        Session = new Session(new ManagementClient(Http, new Uri(settings.ManagementUrl)), new ClientGameSocketFactory(), store, settings, s => s.Save(_settingsPath), new GodotDispatcher());
        Cache = new AssetCache(Path.Combine(userDir, "cache"), Http);
        Session.PhaseChanged += Route;
        var refresh = new Timer { WaitTime = 60, Autostart = true };
        refresh.Timeout += () => _ = Session.RefreshAsync();
        AddChild(refresh);
        _ = Boot();
    }

    async Task Boot()
    {
        try { await Session.TryResumeAsync(); } catch (ManagementException e) { GD.PushWarning($"resume failed: {e.Message}"); }
        Route();
    }

    void Route()
    {
        var path = Session.Phase switch
        {
            SessionPhase.LoggedOut => "res://scenes/Login.tscn",
            SessionPhase.LoggedIn => "res://scenes/Home.tscn",
            SessionPhase.UpdateRequired => "res://scenes/UpdateRequired.tscn",
            _ => "res://scenes/Game.tscn",
        };
        if (_screen is not null && _screen.SceneFilePath == path) return;
        Show(path);
    }

    public void Show(string scenePath)
    {
        _screen?.QueueFree();
        _screen = GD.Load<PackedScene>(scenePath).Instantiate();
        AddChild(_screen);
    }
}
```

Nothing in `Main` is platform-specific. `OS.GetUserDataDir()` resolves under `%APPDATA%` on Windows, `~/.local/share` on Linux and `~/Library/Application Support` on macOS, and `SessionStore` is the same file on all three (Task 2). Windows is the first export target; other desktop platforms are possible and no decision here forecloses them (`architecture.md` → Deliverables, corrected 2026-09-06).

- [ ] **Step 3: Write `LoginScreen.cs` and `UpdateRequiredScreen.cs`**

```csharp
using Godot;
using Tabletop.Client.Core;

namespace Tabletop.Client.Screens;

public partial class LoginScreen : Control
{
    LineEdit _email = null!, _password = null!, _code = null!;
    Label _error = null!;
    CheckBox _remember = null!;
    string? _methodId;

    public override void _Ready()
    {
        var session = Main.Instance.Session;
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) };
        box.SetAnchorsPreset(LayoutPreset.Center); box.GrowHorizontal = GrowDirection.Both; box.GrowVertical = GrowDirection.Both;
        AddChild(box);
        box.AddChild(new Label { Text = "Tabletop", HorizontalAlignment = HorizontalAlignment.Center });
        box.AddChild(_email = new LineEdit { PlaceholderText = "email" });
        box.AddChild(_password = new LineEdit { PlaceholderText = "password", Secret = true });
        box.AddChild(_remember = new CheckBox { Text = "Remember me on this machine", ButtonPressed = session.Settings.RememberMe });
        var row = new HBoxContainer(); box.AddChild(row);
        row.AddChild(Button("Sign in", () => Run(s => s.LoginWithPasswordAsync(_email.Text, _password.Text))));
        row.AddChild(Button("Sign up", () => Run(s => s.SignUpAsync(_email.Text, _password.Text))));
        row.AddChild(Button("Email me a code", () => Run(async s => { _methodId = await s.SendOtpAsync(_email.Text); _code.Visible = true; _error.Text = "Code sent."; })));
        box.AddChild(_code = new LineEdit { PlaceholderText = "one-time code", Visible = false });
        _code.TextSubmitted += _ => Run(s => s.VerifyOtpAsync(_methodId!, _code.Text));
        box.AddChild(_error = new Label { Modulate = Colors.Orange });
    }

    static Button Button(string text, Action onPressed) { var b = new Button { Text = text }; b.Pressed += onPressed; return b; }

    async void Run(Func<Session, Task> action)
    {
        var session = Main.Instance.Session;
        session.UpdateSettings(s => s with { RememberMe = _remember.ButtonPressed });
        try { await action(session); }                                  // PhaseChanged routes to Home on success
        catch (UpdateRequiredException) { }                             // routed to the update screen
        catch (ManagementException e) { _error.Text = e.Status == 401 ? "Wrong email, password or code." : e.Message; }
        catch (HttpRequestException e) { _error.Text = $"Cannot reach {session.Settings.ManagementUrl}: {e.Message}"; }
    }
}
```

```csharp
using Godot;

namespace Tabletop.Client.Screens;

public partial class UpdateRequiredScreen : Control
{
    public override void _Ready()
    {
        var box = new VBoxContainer(); box.SetAnchorsPreset(LayoutPreset.Center); AddChild(box);
        box.AddChild(new Label { Text = "This version of Tabletop is older than the server accepts. Install the current build and start it again." });
        var quit = new Button { Text = "Quit" }; quit.Pressed += () => GetTree().Quit(); box.AddChild(quit);
    }
}
```

- [ ] **Step 4: Run it.** `docker compose up -d`, `./scripts/management` in one WSL shell, then `./scripts/godot`. Expected: the login screen; a wrong password shows *Wrong email, password or code.*; a correct one leaves the screen (Home is Task 10, so the root shows an empty window until then); with "Remember me" ticked, `%APPDATA%\Godot\app_userdata\Tabletop\session.bin` exists and the next launch skips login. Set `MinimumClientVersion` to `2` in `server/management/appsettings.json`, restart management, launch: the update-required screen. Put it back.

- [ ] **Step 5: `./scripts/check-client` prints nothing. Commit: `Add the root, the dispatcher and the login and update-required screens`.**

### Task 10: The Home screen — the game list, create game, join

**Files:**
- Create: `client/scenes/Home.tscn`, `client/src/Screens/HomeScreen.cs`

**Interfaces:**
- Consumes: `Session.Management` (`ListGames`, `ListProperties`, `ListArmies`, `CreateGame`, `JoinGame`), `Session.ConnectAsync`, `Session.SendEditAsync(PickArmy)`, `Session.LogoutAsync`, `Session.LastCloseReason`; `GameSummary`, `PropertiesSummary`, `ArmySummary`, `CreateGameRequest` (Task 3); `SchedulerShape`, `Enforcement`, `GameStatus`, `PickArmy` (Protocol).
- Produces: `partial class HomeScreen : Control` — the game list (`ItemList`) with a status filter, a name filter and "created by me", a "More" button for the next keyset page, "Join", "Create game" (name, pack from `GET /v1/properties`, shape, enforcement, wait-between-steps, army from `GET /v1/armies`), "Sign out". Creating: `CreateGame` → `ConnectAsync(game.Id)` → once `Phase == Connected`, `SendEditAsync(new PickArmy(armyId))`. Joining: `JoinGame` → `ConnectAsync`.
- `POST /v1/games` takes no army (management plan → HTTP surface); the army is a lobby edit after the socket opens (game server plan Task 10) — spec correction 3 records the wording.

Spec: client shell → Screens → Home (the game list from `GET /v1/games`, every status, filtered by the player; create game with name, pack, shape, enforcement, wait and one of the player's armies; creating or joining leads to the same place); accounts → Games are public; setup phase → Joining is allowed at any status.

- [ ] **Step 1: Write `client/scenes/Home.tscn`** as in Task 9 Step 1 with `res://src/Screens/HomeScreen.cs` and root `Home`.

- [ ] **Step 2: Write `HomeScreen.cs`**

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Screens;

public partial class HomeScreen : Control
{
    readonly List<GameSummary> _games = new();
    Guid? _next;
    ItemList _list = null!; OptionButton _status = null!; LineEdit _search = null!; CheckBox _mine = null!; Label _error = null!;

    public override void _Ready()
    {
        var root = new VBoxContainer(); root.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(root);
        var top = new HBoxContainer(); root.AddChild(top);
        top.AddChild(new Label { Text = $"Signed in as {Main.Instance.Session.Account?.Name}" });
        top.AddChild(Btn("Create game", () => _ = CreateDialog()));
        top.AddChild(Btn("Sign out", () => _ = Main.Instance.Session.LogoutAsync()));
        var filters = new HBoxContainer(); root.AddChild(filters);
        filters.AddChild(_status = new OptionButton());
        _status.AddItem("Any status"); foreach (var s in Enum.GetNames<GameStatus>()) _status.AddItem(s);
        _status.ItemSelected += _ => Render();
        filters.AddChild(_search = new LineEdit { PlaceholderText = "name", CustomMinimumSize = new Vector2(200, 0) }); _search.TextChanged += _ => Render();
        filters.AddChild(_mine = new CheckBox { Text = "Created by me" }); _mine.Toggled += _ => Render();
        root.AddChild(_list = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill });
        _list.ItemActivated += i => _ = Join(Visible()[(int)i].Id);
        var bottom = new HBoxContainer(); root.AddChild(bottom);
        bottom.AddChild(Btn("Join", () => { if (_list.GetSelectedItems() is { Length: > 0 } sel) _ = Join(Visible()[sel[0]].Id); }));
        bottom.AddChild(Btn("More", () => _ = LoadPage()));
        bottom.AddChild(Btn("Refresh", () => { _games.Clear(); _next = null; _ = LoadPage(); }));
        root.AddChild(_error = new Label { Modulate = Colors.Orange });
        // Why the last game ended, when it ended without the player asking: a replaced socket, a rejection, or a
        // desync past its bound. Session clears it on the next connect. Without this the drop to Home is silent.
        if (Main.Instance.Session.LastCloseReason is { } why) _error.Text = $"Dropped from the last game: {why}";
        _ = LoadPage();
    }

    static Button Btn(string text, Action a) { var b = new Button { Text = text }; b.Pressed += a; return b; }

    List<GameSummary> Visible()
    {
        var me = Main.Instance.Session.Account?.Id;
        return _games.Where(g => (_status.Selected == 0 || g.Status.ToString() == _status.GetItemText(_status.Selected))
                              && (_search.Text.Length == 0 || g.Name.Contains(_search.Text, StringComparison.OrdinalIgnoreCase))
                              && (!_mine.ButtonPressed || g.CreatedBy == me)).ToList();
    }

    void Render()
    {
        _list.Clear();
        foreach (var g in Visible()) _list.AddItem($"{g.Name}  —  {g.Status.ToString().ToLowerInvariant()}, {g.Participants} in, {g.CreatedAt.LocalDateTime:g}");
    }

    async Task LoadPage()
    {
        try
        {
            var page = await Main.Instance.Session.Management.ListGames(_next, 50);
            _games.AddRange(page.Items.Where(g => _games.All(x => x.Id != g.Id))); _next = page.Next;
            Render();
        }
        catch (ManagementException e) { _error.Text = e.Message; }
    }

    async Task Join(Guid gameId)
    {
        try { await Main.Instance.Session.Management.JoinGame(gameId); await Main.Instance.Session.ConnectAsync(gameId); }
        catch (ManagementException e) { _error.Text = e.Message; }
    }

    async Task CreateDialog()
    {
        var session = Main.Instance.Session;
        var packs = await session.Management.ListProperties();
        var armies = (await session.Management.ListArmies(null)).Items;
        var dialog = new ConfirmationDialog { Title = "Create game", OkButtonText = "Create" };
        var form = new VBoxContainer(); dialog.AddChild(form);
        var name = new LineEdit { PlaceholderText = "game name" }; form.AddChild(name);
        var pack = new OptionButton(); foreach (var p in packs) pack.AddItem($"{p.Name} ({p.GameRules})"); form.AddChild(pack);
        var shape = new OptionButton(); shape.AddItem("Alternating activation"); shape.AddItem("Player turn"); form.AddChild(shape);
        var enforcement = new OptionButton(); foreach (var e in Enum.GetNames<Enforcement>()) enforcement.AddItem(e); enforcement.Select(1); form.AddChild(enforcement);
        var waits = new CheckBox { Text = "Wait between steps" }; form.AddChild(waits);
        var army = new OptionButton(); army.AddItem("No army (watch)"); foreach (var a in armies) army.AddItem($"{a.Name} ({a.Units} units)"); form.AddChild(army);
        dialog.Confirmed += async () =>
        {
            if (packs.Count == 0) { _error.Text = "No Properties set is loaded on the server; GET /v1/properties is empty."; return; }
            try
            {
                var game = await session.Management.CreateGame(new CreateGameRequest(name.Text, packs[pack.Selected].ContentHash,
                    shape.Selected == 0 ? SchedulerShape.AlternatingActivation : SchedulerShape.PlayerTurn, Enum.Parse<Enforcement>(enforcement.GetItemText(enforcement.Selected)), waits.ButtonPressed));
                Guid? armyId = army.Selected == 0 ? null : armies[army.Selected - 1].Id;
                if (armyId is { } id)
                {
                    void PickWhenConnected() { if (session.Phase == SessionPhase.Connected) { session.PhaseChanged -= PickWhenConnected; _ = session.SendEditAsync(new PickArmy(id)); } }
                    session.PhaseChanged += PickWhenConnected;
                }
                await session.ConnectAsync(game.Id);
            }
            catch (ManagementException e) { _error.Text = e.Message; }
        };
        AddChild(dialog); dialog.PopupCentered(new Vector2I(420, 320));
    }
}
```

- [ ] **Step 3: Run it.** With a `properties` row seeded (management plan Task 12's `Seed()` insert, run once by hand: `docker compose exec postgres psql -U tabletop -d tabletop -c "insert into management.properties values ('sha256:gf-dev','Grimdark Future core','grimdark-future') on conflict do nothing"`) and `dotnet run --project server/game` running: Create game → the screen goes to the Game scene (Task 13; until then the Main root shows nothing and the log prints `Connect` sent). `Refresh` lists the game; a second account in a second client instance sees and joins it. `./scripts/check-client` prints nothing. Commit: `Add the Home screen: the game list, create game and join`.

### Task 11: The table view — frame, slab, proxies, terrain, markers, zones, camera, mesh loading

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
            BaseShape.Square => new BoxMesh { Size = new Vector3((float)b.A, h, (float)b.A) },
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

### Task 12: Input and presence — select, drag, drop, the ruler, measure, ping, others' cursors and ghosts

**Files:**
- Create: `client/src/Table/TableInput.cs`, `client/src/Table/RulerNode.cs`, `client/src/Table/PresenceOverlay.cs`

**Interfaces:**
- Consumes: `TableView` (Task 11), `Session` (`SendPresenceAsync`, `SendPingAsync`, `SendCommandAsync`, `SendEditAsync`, `Others`, `PresenceChanged`, `Pinged`, `Game`, `Projection`, `Draft`, `ParticipantId`), `MoveRuler`, `RulerReadout` (Core); `Presence`, `Cursor`, `Selection`, `PresencePlacement`, `PlacementKind`, `Measurement`, `Ping`, `MoveModels`, `ModelRelocation`, `OnTable`, `Inside`, `MoveTerrain`, `MoveSetupMarker`, `MoveMarker`, `GameStatus` (Protocol); `Settling.SurfaceHeightAt(Point3, …)`, `Distance.ClosestPoints`, `PlacedModel`, `ModelBody`, `Placement` (Geometry).
- Produces: `enum TableTool { Select, Measure }`; `partial class TableInput : Node` — `void Bind(TableView, Session)`, `TableTool Tool`, `Selection? Selected`, `event Action<Guid, Vector2>? PieceRightClicked` (a piece id and the screen position, for the menu), `event Action<Vector3>? TableRightClicked`, `event Action? SelectionChanged`, `event Action<Guid>? PieceLeftClicked` (the clicked piece's own id — a model, not the unit `Selected` widens it to — raised after the drag is made so a listener may `CancelDrag()`), `event Action<Guid, Point3>? ModelDragged`, `HashSet<Guid> Proposing`, `double? Allowance`, `void PickNextPiece(Action<Guid>)` and `void PickNextPoint(Action<Point3>)` (the menu's `PieceId` and `TablePoint` items: the next left click answers them instead of selecting or dragging, then clears itself — one field each, checked at the top of `OnLeftDown`); `void CancelDrag()` (drop the drag in progress and hide the ruler, without sending anything — the Ask panel calls it when a click on an enterable container answers the move instead of moving it); left click selects the unit under the cursor (Shift narrows to the model; during setup a terrain placement or marker); left drag moves the selection: every model of the selection follows by the same displacement, each settles with `Settling.SurfaceHeightAt(new Point3(x, y, currentZ), volumes)`, positions stream as `held` placements at 20 Hz (a `Timer`), the release sends one `MoveModels` with start and stop (or, in setup, one `MoveTerrain`/`MoveSetupMarker`; a token in play, one `MoveMarker`); `M` toggles the measure tool (two clicks on pieces, or one press-and-drag between them, → `Distance.ClosestPoints` → `Measurement`, drawn and relayed); `P` pings at the cursor; the cursor's ray point and hovered piece go in every presence sample. `partial class RulerNode : Node3D` — `void Show(Point3 from, Point3 to, RulerReadout readout, Color)`, `void Hide()`; `partial class PresenceOverlay : Node3D` — draws others' cursors (a small cone at the point, hovered piece highlighted), measurements (a line with the length), `held` placements (the piece node moved there, in the team's colour) and `proposed` ghosts (a translucent copy), rulers for others' held placements from the projection's start position, and pings (a ring that fades over two seconds); `void Bind(TableView, Session)`.
- Picking is ray-versus-AABB over `TableView.Pieces` bounds and the slab plane (`Plane(Vector3.Up, 0).IntersectsRay`); no physics. `Pieces` includes a disembark’s exit ghosts (Task 11), which is the whole of what makes an occupant draggable.
- **Nothing here draws a ruler for a piece with no placement.** The ruler is drawn from `Drag.Starts`, which `Position` fills — and for a model `Inside` a container that start is the *container’s* placement, never `(0,0,0)`, because an occupant *"has no table position of its own … and disembarks relative to it"* (game flow → Inside a container). A piece that is neither on the table nor inside a container that is itself on the table has no node, so no drag can begin on it. The same holds for the other participants: `PresenceOverlay.StartOf` returns null for an occupant, so no ruler is drawn against a placement that does not exist, and their disembark ghosts arrive as `PlacementKind.Proposed`, which draws a standalone translucent cylinder needing no node of its own.

Spec: client shell → Input and presence (every bullet), The table view (Godot physics is not in the scene; geometry runs in `Tabletop.Geometry`), Server wins; presence → The presence value, The cursor is where the ray lands, Drag and ghost are one value, A measurement is two points, A ping is silent; setup phase → The draft is the row (live drag is presence; the drop is the edit); table domain model → The one wire decision (start and stop); scheduler → Moving models by hand is not an answer; geometry plan → Interfaces needed → Client shell plan (`Distance.ClosestPoints` for measurements; `Settling.SurfaceHeightAt(new Point3(x, y, currentZ), volumes)` for a dragged model).

- [ ] **Step 1: Write `RulerNode.cs`**

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>A line from where a piece started to where it is, with the distance in inches and, in a declared move, what is left.</summary>
public partial class RulerNode : Node3D
{
    readonly MeshInstance3D _line = new(); readonly Label3D _label = new() { Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 48, PixelSize = 0.01f };
    public override void _Ready() { AddChild(_line); AddChild(_label); Hide(); }

    public void Show(Point3 from, Point3 to, RulerReadout readout, Color colour)
    {
        var a = WorldFrame.ToGodot(from) + Vector3.Up * 0.3f; var b = WorldFrame.ToGodot(to) + Vector3.Up * 0.3f;
        var im = new ImmediateMesh(); im.SurfaceBegin(Mesh.PrimitiveType.Lines); im.SurfaceAddVertex(a); im.SurfaceAddVertex(b); im.SurfaceEnd();
        _line.Mesh = im; _line.MaterialOverride = new StandardMaterial3D { AlbedoColor = readout.OverAllowance ? Colors.Red : colour, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _label.Position = (a + b) / 2 + Vector3.Up * 0.8f;
        _label.Text = readout.Remaining is { } r ? $"{readout.Distance:0.0}\"  ({r:0.0}\" left)" : $"{readout.Distance:0.0}\"";
        _label.Modulate = readout.OverAllowance ? Colors.Red : Colors.White;
        Visible = true;
    }

    public new void Hide() => Visible = false;
}
```

- [ ] **Step 2: Write `TableInput.cs`**

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Table;

public enum TableTool { Select, Measure }

/// <summary>Mouse and keys → presence, commands and edits. Geometry is the library's; Godot only casts the pick ray.</summary>
public partial class TableInput : Node
{
    TableView _view = null!; Session _session = null!; RulerNode _ruler = null!;
    readonly Timer _presenceTimer = new() { WaitTime = 0.05, Autostart = true };   // 20 Hz
    bool _presenceDirty; Cursor? _cursor; Measurement? _measurement; Point3? _measureFrom;
    Drag? _drag;
    public TableTool Tool { get; set; } = TableTool.Select;
    public Selection? Selected { get; private set; }
    /// <summary>Models a declared move is positioning: their drag streams proposed ghosts, and the release sends nothing (the Ask panel commits).</summary>
    public HashSet<Guid> Proposing { get; } = new();
    public double? Allowance { get; set; }
    public event Action<Guid, Point3>? ModelDragged;
    public event Action<Guid, Vector2>? PieceRightClicked;
    public event Action<Vector3>? TableRightClicked;
    public event Action? SelectionChanged;
    public event Action<Guid>? PieceLeftClicked;

    sealed record Drag(Guid PieceId, IReadOnlyList<Guid> ModelIds, Point3 Start, Dictionary<Guid, Point3> Starts, Dictionary<Guid, Point3> Current, Yaw Yaw);
    /// <summary>The volumes a dragged piece settles on: every world volume except the ones the dragged piece itself owns,
    /// so dragging a terrain placement does not make it climb its own hill. Volume ids and piece ids are different ids,
    /// so the exclusion is built from the placement, not by subtracting the drag's piece id.</summary>
    List<WorldVolume> _settleAgainst = [];

    public void Bind(TableView view, Session session)
    {
        _view = view; _session = session;
        view.AddChild(_ruler = new RulerNode()); AddChild(_presenceTimer);
        _presenceTimer.Timeout += () => { if (_presenceDirty) { _presenceDirty = false; _ = _session.SendPresenceAsync(Snapshot()); } };
    }

    Presence Snapshot() => new(_session.ParticipantId ?? Guid.Empty, _cursor, Selected,
        _drag is null ? [] : _drag.Current.Select(kv => new PresencePlacement(kv.Key, kv.Value, _drag.Yaw, Proposing.Contains(kv.Key) ? PlacementKind.Proposed : PlacementKind.Held)).ToList(), _measurement);

    (Point3 Point, Node3D? Piece) Pick(Vector2 screen)
    {
        var from = _view.Camera.ProjectRayOrigin(screen); var dir = _view.Camera.ProjectRayNormal(screen);
        Node3D? best = null; var bestDistance = float.MaxValue; Vector3? hit = null;
        foreach (var piece in _view.Pieces)
        {
            var bounds = piece switch { PieceNode p => p.Bounds, TerrainNode t => t.Bounds, MarkerNode m => m.Bounds, _ => new Aabb() };
            if (bounds.IntersectsRay(from, dir) is { } h && from.DistanceTo(h) < bestDistance) { best = piece; bestDistance = from.DistanceTo(h); hit = h; }
        }
        var ground = new Plane(Vector3.Up, 0).IntersectsRay(from, dir);
        return (WorldFrame.FromGodot(hit ?? ground ?? Vector3.Zero), best);
    }

    static Guid IdOf(Node3D piece) => piece switch { PieceNode p => p.ModelId, TerrainNode t => t.PlacementId, MarkerNode m => m.MarkerId, _ => Guid.Empty };

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion m: OnMotion(m.Position); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } b: OnLeftDown(b.Position, b.ShiftPressed); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }: _ = OnLeftUp(); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } b when !Input.IsMouseButtonPressed(MouseButton.Middle):
                var (point, piece) = Pick(b.Position);
                if (piece is not null && !b.AltPressed) PieceRightClicked?.Invoke(IdOf(piece), b.Position); else if (b.AltPressed) TableRightClicked?.Invoke(WorldFrame.ToGodot(point));
                break;
            case InputEventKey { Pressed: true, Keycode: Key.M }: Tool = Tool == TableTool.Measure ? TableTool.Select : TableTool.Measure; _measureFrom = null; break;
            case InputEventKey { Pressed: true, Keycode: Key.P }: var (at, target) = Pick(GetViewport().GetMousePosition()); _ = _session.SendPingAsync(new Ping(_session.ParticipantId ?? Guid.Empty, at, target is null ? null : IdOf(target))); break;
            case InputEventKey { Pressed: true, Keycode: Key.Escape }: Selected = null; _measurement = null; _presenceDirty = true; SelectionChanged?.Invoke(); break;
        }
    }

    void OnMotion(Vector2 screen)
    {
        var (point, piece) = Pick(screen);
        _cursor = new Cursor(point, piece is null ? null : IdOf(piece)); _presenceDirty = true;
        if (_drag is null) return;
        var delta = new Point2(point.X - _drag.Start.X, point.Y - _drag.Start.Y);
        foreach (var (id, start) in _drag.Starts)
        {
            var x = start.X + delta.X; var y = start.Y + delta.Y;
            var z = Settling.SurfaceHeightAt(new Point3(x, y, _drag.Current[id].Z + 0.5), _settleAgainst);
            _drag.Current[id] = new Point3(x, y, z);
            MoveNode(id, _drag.Current[id]);
            ModelDragged?.Invoke(id, _drag.Current[id]);
        }
        var readout = MoveRuler.Read(_drag.Start.Horizontal, point.Horizontal, Proposing.Contains(_drag.ModelIds[0]) ? Allowance : null);
        var lead = _drag.ModelIds[0];
        _ruler.Show(_drag.Starts[lead], _drag.Current[lead], readout, Colors.White);
    }

    void MoveNode(Guid id, Point3 to)
    {
        Node3D? node = _view.PieceAt(id) ?? (Node3D?)_view.TerrainAt(id) ?? _view.MarkerAt(id);
        if (node is not null) node.Position = WorldFrame.ToGodot(to);
    }

    /// <summary>The menu's deferred inputs: a PieceId item wants the next piece clicked, a TablePoint item the next point.</summary>
    Action<Guid>? _pickPiece; Action<Point3>? _pickPoint;
    public void PickNextPiece(Action<Guid> then) => _pickPiece = then;
    public void PickNextPoint(Action<Point3> then) => _pickPoint = then;

    /// <summary>Abandon the drag in progress and send nothing. The Ask panel calls it when the click that started the drag turned out to be an answer — clicking an enterable container embarks the unit rather than shoving the transport about.</summary>
    public void CancelDrag() { _drag = null; _ruler.Hide(); _presenceDirty = true; _view.Redraw(); }

    void OnLeftDown(Vector2 screen, bool shift)
    {
        var (point, piece) = Pick(screen);
        if (_pickPoint is { } wantPoint) { _pickPoint = null; wantPoint(point); return; }
        if (_pickPiece is { } wantPiece && piece is not null) { _pickPiece = null; wantPiece(IdOf(piece)); return; }
        if (Tool == TableTool.Measure) { Measure(point, piece); return; }
        if (piece is null) { Selected = null; SelectionChanged?.Invoke(); _presenceDirty = true; return; }
        var id = IdOf(piece);
        var projection = _session.Projection;
        IReadOnlyList<Guid> models;
        if (piece is PieceNode pn && projection is not null)
        {
            // The unit's models the view is drawing: the ones OnTable, or — during a disembark — its exit ghosts. An occupant
            // has no node otherwise, so PieceAt is the test rather than the Location: it answers for both cases (Task 11).
            var unitModels = projection.Models.Values.Where(m => m.UnitId == pn.UnitId && _view.PieceAt(m.Id) is not null).Select(m => m.Id).ToList();
            Selected = shift ? new Selection(pn.UnitId, [pn.ModelId]) : new Selection(pn.UnitId, []);
            models = shift ? [pn.ModelId] : unitModels;
        }
        else { Selected = new Selection(id, []); models = [id]; }
        SelectionChanged?.Invoke();
        var starts = models.ToDictionary(m => m, m => Position(m));
        var own = (_session.Projection?.Board.Table ?? _session.Draft)?.Terrain.FirstOrDefault(t => t.Id == id)?.Volumes.Select(v => v.Id).ToHashSet() ?? [];
        _settleAgainst = _view.Volumes.Where(v => !own.Contains(v.Id)).ToList();
        _drag = new Drag(id, models, point, starts, new Dictionary<Guid, Point3>(starts), CurrentYaw(id));
        _presenceDirty = true;
        // Last, with the drag already made, so a listener that treats the click as an answer can CancelDrag() it: the piece's
        // own id, which for a model is the model — not the unit Selection widens it to, and what the Ask panel needs to match
        // against MoveContainment.Enterable (Task 14).
        PieceLeftClicked?.Invoke(id);
    }

    Point3 Position(Guid id)
    {
        var p = _session.Projection; var t = p?.Board.Table ?? _session.Draft;
        if (p?.Models.GetValueOrDefault(id)?.Location is OnTable on) return on.Placement.Position;
        if (Container(id) is { } inside) return inside.Placement.Position;                   // a disembark ghost starts on the container
        if (t?.Terrain.FirstOrDefault(x => x.Id == id) is { } terrain) return terrain.Position;
        return t?.Markers.Concat(p?.Markers.Values ?? []).FirstOrDefault(m => m.Id == id)?.Position ?? new Point3(0, 0, 0);
    }

    Yaw CurrentYaw(Guid id)
    {
        var p = _session.Projection; var t = p?.Board.Table ?? _session.Draft;
        if (p?.Models.GetValueOrDefault(id)?.Location is OnTable on) return on.Placement.Yaw;
        if (Container(id) is { } inside) return inside.Placement.Yaw;                        // and facing the way it did
        return t?.Terrain.FirstOrDefault(x => x.Id == id)?.Yaw ?? t?.Markers.FirstOrDefault(m => m.Id == id)?.Yaw ?? new Yaw(0);
    }

    /// <summary>
    /// Where an occupant is: nowhere of its own (game flow → Inside a container, "an occupant has no table position of its
    /// own … and disembarks relative to it"), so its drag starts on the container's placement. Null unless the model is
    /// Inside something that is itself on the table.
    /// </summary>
    OnTable? Container(Guid id) =>
        _session.Projection is { } p && p.Models.GetValueOrDefault(id)?.Location is Inside i
            ? p.Models.GetValueOrDefault(i.ContainerId)?.Location as OnTable : null;

    async Task OnLeftUp()
    {
        if (Tool == TableTool.Measure)                                   // "two clicks or a drag" (client shell -> Input and presence)
        {
            var (to, hit) = Pick(GetViewport().GetMousePosition());
            if (_measureFrom is { } from && from.Horizontal.DistanceTo(to.Horizontal) > 0.25) Measure(to, hit);
            return;
        }
        if (_drag is null) return;
        var drag = _drag; _drag = null; _ruler.Hide(); _presenceDirty = true;
        var moved = drag.Current.Any(kv => kv.Value != drag.Starts[kv.Key]);
        if (!moved) return;
        var status = _session.Game?.Status;
        var p = _session.Projection; var draft = _session.Draft;
        // A declared move: the Ask panel's Commit sends the Destinations, so nothing goes now and — unlike every other drop —
        // the nodes are left where the player put them. Redrawing here would snap a proposed placement back to the projection,
        // and a disembark ghost has no projected placement to snap to at all.
        if (drag.ModelIds.All(Proposing.Contains)) return;
        if (p is not null && drag.ModelIds.All(p.Models.ContainsKey))
            await _session.SendCommandAsync(new MoveModels(drag.ModelIds.Select(m => new ModelRelocation(m, new OnTable(new Placement(drag.Starts[m], drag.Yaw)), new OnTable(new Placement(drag.Current[m], drag.Yaw)))).ToList()));
        else if (status == GameStatus.Setup && draft is not null && draft.Terrain.Any(t => t.Id == drag.PieceId))
            await _session.SendEditAsync(new MoveTerrain(drag.PieceId, drag.Current[drag.PieceId], drag.Yaw));
        else if (status == GameStatus.Setup && draft is not null && draft.Markers.Any(m => m.Id == drag.PieceId))
            await _session.SendEditAsync(new MoveSetupMarker(drag.PieceId, drag.Current[drag.PieceId], drag.Yaw));
        else if (p is not null && p.Markers.ContainsKey(drag.PieceId))
            await _session.SendCommandAsync(new MoveMarker(drag.PieceId, drag.Current[drag.PieceId], drag.Yaw));
        _view.Redraw();   // server wins: the node snaps back to the projection until the event lands
    }

    void Measure(Point3 point, Node3D? piece)
    {
        var anchor = piece is PieceNode pn && _session.Projection?.Models.GetValueOrDefault(pn.ModelId) is { Location: OnTable on } m
            ? new PlacedModel(new ModelBody(m.Slot.Base, m.Slot.Height), on.Placement) : new PlacedModel(new ModelBody(Base.Circle(0), 0), new Placement(point, new Yaw(0)));
        if (_measureFrom is null) { _measureFrom = point; _pendingAnchor = anchor; return; }
        var (a, b) = Distance.ClosestPoints(_pendingAnchor!.Value, anchor);
        _measurement = new Measurement(a, b); _measureFrom = null; _presenceDirty = true;
        _ruler.Show(a, b, MoveRuler.Read(a.Horizontal, b.Horizontal, null), Colors.Yellow);
    }
    PlacedModel? _pendingAnchor;
}
```

A base of diameter 0 stands in for a bare table point, so measuring piece-to-point is the same edge-to-edge call.

- [ ] **Step 3: Write `PresenceOverlay.cs`**

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Table;

/// <summary>What others are doing, drawn in their team's colour and never animated: cursors, measurements, held pieces, ghosts, pings, and their rulers.</summary>
public partial class PresenceOverlay : Node3D
{
    TableView _view = null!; Session _session = null!;
    readonly Dictionary<Guid, Node3D> _cursors = new(), _measures = new(), _ghosts = new(); readonly Dictionary<Guid, RulerNode> _rulers = new();
    readonly Dictionary<Guid, Presence> _drawn = new();   // what this overlay last drew per participant, so Remove can release it

    public void Bind(TableView view, Session session)
    {
        _view = view; _session = session;
        session.PresenceChanged += Draw; session.Pinged += Ping;
    }

    Color ColourOf(Guid participantId)
    {
        var teamId = _session.Game?.Participants.FirstOrDefault(p => p.Id == participantId)?.TeamId;
        var ordinal = _session.Game?.Teams.FirstOrDefault(t => t.Id == teamId)?.Ordinal ?? 0;
        return ordinal == 0 ? Colors.LightGray : Palette.Team(ordinal);
    }

    void Draw(Guid participantId)
    {
        Remove(participantId);
        var value = _session.Others.Of(participantId);
        if (value is null) { _view.Redraw(); return; }                     // a held piece returns to where the projection has it
        _drawn[participantId] = value;
        var colour = ColourOf(participantId);
        if (value.Cursor is { } c)
        {
            var cone = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0, BottomRadius = 0.25f, Height = 0.8f }, Position = WorldFrame.ToGodot(c.Point) + Vector3.Up * 0.5f, MaterialOverride = Palette.Translucent(colour) };
            AddChild(cone); _cursors[participantId] = cone;
            if (c.Target is { } target) _view.PieceAt(target)?.SetHighlight(colour);
        }
        if (value.Measurement is { } m)
        {
            var ruler = new RulerNode(); AddChild(ruler); ruler.Show(m.From, m.To, MoveRuler.Read(m.From.Horizontal, m.To.Horizontal, null), colour); _measures[participantId] = ruler;
        }
        foreach (var placement in value.Placements)
        {
            if (placement.Kind == PlacementKind.Held)
            {
                Node3D? node = _view.PieceAt(placement.PieceId) ?? (Node3D?)_view.TerrainAt(placement.PieceId) ?? _view.MarkerAt(placement.PieceId);
                if (node is null) continue;
                node.Position = WorldFrame.ToGodot(placement.Position); node.Rotation = new Vector3(0, WorldFrame.Radians(placement.Yaw), 0);
                if (node is PieceNode piece) piece.SetHighlight(colour);
                var start = StartOf(placement.PieceId);
                if (start is { } s) { var ruler = new RulerNode(); AddChild(ruler); ruler.Show(s, placement.Position, MoveRuler.Read(s.Horizontal, placement.Position.Horizontal, null), colour); _rulers[placement.PieceId] = ruler; }
            }
            else
            {
                var ghost = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.6f, BottomRadius = 0.6f, Height = 1.5f }, Position = WorldFrame.ToGodot(placement.Position) + Vector3.Up * 0.75f, MaterialOverride = Palette.Translucent(Palette.Ghost) };
                AddChild(ghost); _ghosts[placement.PieceId] = ghost;
            }
        }
    }

    /// <summary>The start the viewer already holds: the projection's placement, or the draft's (presence → drawn "against the start position already in their projection").</summary>
    Point3? StartOf(Guid pieceId)
    {
        var p = _session.Projection; var t = p?.Board.Table ?? _session.Draft;
        if (p?.Models.GetValueOrDefault(pieceId)?.Location is OnTable on) return on.Placement.Position;
        return t?.Terrain.FirstOrDefault(x => x.Id == pieceId)?.Position ?? t?.Markers.FirstOrDefault(m => m.Id == pieceId)?.Position;
    }

    void Remove(Guid participantId)
    {
        foreach (var d in new[] { _cursors, _measures }) if (d.Remove(participantId, out var n)) n.QueueFree();
        _drawn.Remove(participantId, out var previous);
        foreach (var placement in previous?.Placements ?? [])
        { if (_rulers.Remove(placement.PieceId, out var r)) r.QueueFree(); if (_ghosts.Remove(placement.PieceId, out var g)) g.QueueFree(); _view.PieceAt(placement.PieceId)?.SetHighlight(null); }
        if (previous?.Cursor?.Target is { } target) _view.PieceAt(target)?.SetHighlight(null);
    }

    void Ping(Ping ping)
    {
        var ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.8f, OuterRadius = 1.2f }, Position = WorldFrame.ToGodot(ping.Point) + Vector3.Up * 0.1f, MaterialOverride = Palette.Translucent(ColourOf(ping.Sender)) };
        AddChild(ring);
        var tween = CreateTween(); tween.TweenProperty(ring, "scale", Vector3.One * 3, 2.0); tween.Parallel().TweenProperty(ring, "transparency", 1.0f, 2.0); tween.TweenCallback(Callable.From(ring.QueueFree));
    }
}
```

`Remove` reads `_drawn`, not `Session.Others`: the session has already replaced the value by the time `PresenceChanged` fires, and the overlay must release what it drew last.

- [ ] **Step 4: Run it with two clients** (two desktop accounts on the development machine, or one account plus a second local user): each sees the other's cursor cone in the team colour, a dragged piece moving live with a ruler, a measurement line, and a ping ring; the drop snaps to where the `models_moved` event says; a Home-button disconnect on one client makes its cursor vanish on the other. `./scripts/check-client` prints nothing. Commit: `Add selection, drag with held presence, the ruler, measuring, pings and others' presence`.

### Task 13: The Game screen — status routing, the lobby panel and the setup toolbar

**Files:**
- Create: `client/scenes/Game.tscn`, `client/src/Screens/GameScreen.cs`, `client/src/Panels/LobbyPanel.cs`, `client/src/Panels/SetupToolbar.cs`, `client/src/Panels/TeamPanel.cs`

**Interfaces:**
- Consumes: `Session` (`Game`, `Draft`, `Phase`, `IsCreator`, `ParticipantId`, `GameChanged`, `BoardChanged`, `PhaseChanged`, `RefusedReceived`, `ConnectionLost`, `SendEditAsync`, `DisconnectAsync`, `Management.ListArmies/ListLayouts/GetLayout/SaveLayout/ListAssets/GetAssetVolumes`), `TableView`, `TableInput`, `PresenceOverlay`, `BuiltinContent`, `TerrainGenerators`, `ZoneStyles`; every `LobbyEdit` and `SetupEdit`, `ProjectionDesyncException` (Protocol).
- Produces: `partial class GameScreen : Control` — hosts the `TableView` in a `SubViewportContainer`, `TableInput`, `PresenceOverlay`, the Home button (`DisconnectAsync`), a status line (`{game.name} — {status}`, `Reconnecting…` while `Phase == Reconnecting`), the panels by status: `LobbyPanel` at `lobby` and `setup`; `SetupToolbar` at `setup`; the play panels (Tasks 14–15) at `playing`/`ended`; a toast for `Refused` (`reason: detail`). `partial class LobbyPanel : Control` — teams with their members, "Create team", "Join team N"/"Watch" (`PickTeam(null)`), "Pick army" from `ListArmies` (`PickArmy`), the configuration form (`SetConfiguration` with the current values pre-filled), and for the creator "Start setup" (at `lobby`) and "End setup" (at `setup`, with a note of how many placements are off the slab). `partial class SetupToolbar : Control` — table width/length (`SetTableSize`), texture (`SetTexture` from `BuiltinContent.Textures` or my texture assets), add built-in terrain (a dropdown over `BuiltinContent.Terrain` → `AddTerrain(BuiltinContent.Place(name, beside-the-table, 0))`), add my library terrain (`ListAssets(Terrain)` → `GetAssetVolumes` → `AddTerrain` with `VisualRef.OfAsset` and the asset's volumes), the four generators (`TerrainGenerators.Generate(planet, size, Random.Shared)` → one `SendEditAsync` per add), add objective / token (`AddSetupMarker`), zone style (`SetZoneStyle(style, ZoneStyles.ShapesOf(style))`), swap zones (two ordinals → `SwapZones`), layouts ("Load…" → `ListLayouts` → `GetLayout` → `ReplaceDraft`; "Save as…" → `SaveLayout(Draft with { Name })`), and for the selected placement: scale (`ScaleTerrain`), remove (`RemoveTerrain`/`RemoveSetupMarker`), swap mesh (`SwapTerrainMesh` to a built-in or my asset), edit volumes (each volume's kind, footprint, heights, six declaration checkboxes and notes → `OverrideTerrainVolumes`), and "Reset volumes" (`OverrideTerrainVolumes` with `BuiltinContent.VolumesOf(name)` or the asset's `GetAssetVolumes`). `partial class TeamPanel : Control` — one per team: ordinal, colour, members, score (from `Projection.Scores`), a right-click hook for Task 15.

Spec: client shell → Screens → Game (one screen for all four statuses; a status change is a change inside the screen; Home disconnects); setup phase → The lifecycle (lobby picks and configuration; the creator's two transitions; teams and armies editable during setup), Editing is free for all (the closed set of edits), Layouts (load replaces the whole draft; save is a management call from the client's own copy), Zones (style; anyone swaps), Terrain (built-in or asset; swapping the mesh keeps the volumes; clearing re-copies the source's volumes; generators as a batch of adds beside the table), Joining (a participant with no team watches); geometry plan → `ZoneStyles.ShapesOf`; game server plan Tasks 10–12 (which edits are accepted at which status; `Refused` reasons).

- [ ] **Step 1: Write `client/scenes/Game.tscn`** as in Task 9 Step 1 with `res://src/Screens/GameScreen.cs`, root `Game` of type `Control`.

- [ ] **Step 2: Write `GameScreen.cs`**

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Panels;
using Tabletop.Client.Table;
using Tabletop.Protocol;

namespace Tabletop.Client.Screens;

public partial class GameScreen : Control
{
    Session _session = null!;
    public TableView Table { get; private set; } = null!;
    public TableInput Input { get; private set; } = null!;
    Label _status = null!, _toast = null!; VBoxContainer _side = null!; HBoxContainer _bottom = null!;
    Control? _lobby, _setup, _ask, _log; readonly List<TeamPanel> _teams = new();

    public override void _Ready()
    {
        _session = Main.Instance.Session;
        var root = new HBoxContainer(); root.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(root);
        var viewportContainer = new SubViewportContainer { Stretch = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        var viewport = new SubViewport { HandleInputLocally = true }; viewportContainer.AddChild(viewport); root.AddChild(viewportContainer);
        Table = GD.Load<PackedScene>("res://scenes/TableView.tscn").Instantiate<TableView>(); viewport.AddChild(Table);
        Input = new TableInput(); Table.AddChild(Input);
        var overlay = new PresenceOverlay(); Table.AddChild(overlay);
        _side = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) }; root.AddChild(_side);
        var top = new HBoxContainer(); _side.AddChild(top);
        var home = new Button { Text = "Home" }; home.Pressed += () => _ = _session.DisconnectAsync(); top.AddChild(home);
        top.AddChild(_status = new Label());
        _side.AddChild(_toast = new Label { Modulate = Colors.Orange });
        _bottom = new HBoxContainer(); _side.AddChild(_bottom);
        if (_session.GameId is not null) { Table.Bind(_session); Input.Bind(Table, _session); overlay.Bind(Table, _session); }
        _session.GameChanged += Rebuild; _session.PhaseChanged += Rebuild; _session.BoardChanged += RefreshTeams; _session.EventsApplied += _ => RefreshTeams();
        _session.RefusedReceived += r => Toast($"{r.Reason}: {r.Detail}");
        _session.ConnectionLost += (c, will) => Toast(c.Error is ProjectionDesyncException
            ? will ? "The table fell out of step with the server; rebuilding it from the log…" : $"This game's log cannot be replayed: {c.Reason}"
            : will ? $"Connection lost ({c.Reason}); reconnecting…" : $"Disconnected: {c.Reason}");
        Rebuild();
    }

    public void Toast(string text) { _toast.Text = text; GetTree().CreateTimer(4).Timeout += () => { if (_toast.Text == text) _toast.Text = ""; }; }

    void Rebuild()
    {
        var game = _session.Game;
        _status.Text = game is null ? (_session.Phase == SessionPhase.Reconnecting ? "Reconnecting…" : "Connecting…") : $"{game.Name} — {game.Status.ToString().ToLowerInvariant()}" + (_session.Phase == SessionPhase.Reconnecting ? " (reconnecting…)" : "");
        foreach (var t in _teams) t.QueueFree(); _teams.Clear();
        _lobby?.QueueFree(); _setup?.QueueFree(); _lobby = _setup = null;
        if (game is null) return;
        foreach (var team in game.Teams) { var panel = new TeamPanel(team); _teams.Add(panel); _side.AddChild(panel); }
        RefreshTeams();
        if (game.Status is GameStatus.Lobby or GameStatus.Setup) _side.AddChild(_lobby = new LobbyPanel());
        if (game.Status == GameStatus.Setup) _side.AddChild(_setup = new SetupToolbar(this));
        ShowPlayPanels(game.Status is GameStatus.Playing or GameStatus.Ended);   // Tasks 14–15 fill this in
    }

    void RefreshTeams() { foreach (var t in _teams) t.Refresh(_session); }

    partial void ShowPlayPanels(bool playing);
}
```

`partial void ShowPlayPanels` is declared here and given its body in Task 14 (`GameScreen.Play.cs`); until then the partial method compiles to nothing.

- [ ] **Step 3: Write `TeamPanel.cs` and `LobbyPanel.cs`**

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Table;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

public partial class TeamPanel : PanelContainer
{
    public Team Team { get; }
    readonly Label _text = new();
    public event Action<Vector2>? RightClicked;
    public TeamPanel(Team team) { Team = team; AddChild(_text); SelfModulate = Palette.Team(team.Ordinal); }

    public void Refresh(Session s)
    {
        var members = s.Game?.Participants.Where(p => p.TeamId == Team.Id).Select(p => p.Name + (p.ArmyId is null ? "" : " ⚔")) ?? [];
        var score = s.Projection?.Scores.GetValueOrDefault(Team.Id);
        var active = s.Projection?.Scheduler.ActiveTeamId == Team.Id ? "  ◀ active" : "";
        _text.Text = $"Team {Team.Ordinal}{(score is null ? "" : $"  score {score}")}{active}\n{string.Join(", ", members)}";
    }

    public override void _GuiInput(InputEvent e) { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } b) RightClicked?.Invoke(b.GlobalPosition); }
}

/// <summary>Lobby edits: written to the game's rows, broadcast, never logged. Shown at lobby and setup.</summary>
public partial class LobbyPanel : VBoxContainer
{
    public override void _Ready()
    {
        var s = Main.Instance.Session; var game = s.Game!;
        AddChild(new Label { Text = "Lobby" });
        var teams = new HBoxContainer(); AddChild(teams);
        foreach (var t in game.Teams) teams.AddChild(Btn($"Join team {t.Ordinal}", () => _ = s.SendEditAsync(new PickTeam(t.Id))));
        teams.AddChild(Btn("Watch", () => _ = s.SendEditAsync(new PickTeam(null))));
        teams.AddChild(Btn("Create team", () => _ = s.SendEditAsync(new CreateTeam())));
        var armies = new OptionButton(); armies.AddItem("No army"); AddChild(armies);
        _ = Task.Run(async () => { var list = (await s.Management.ListArmies(null)).Items; Callable.From(() => { foreach (var a in list) armies.AddItem(a.Name, 0); armies.ItemSelected += i => _ = s.SendEditAsync(new PickArmy(i == 0 ? null : list[(int)i - 1].Id)); }).CallDeferred(); });
        var config = new VBoxContainer(); AddChild(config);
        var name = new LineEdit { Text = game.Name }; config.AddChild(name);
        var shape = new OptionButton(); shape.AddItem("Alternating activation"); shape.AddItem("Player turn"); shape.Select(game.SchedulerShape == SchedulerShape.PlayerTurn ? 1 : 0); config.AddChild(shape);
        var enforcement = new OptionButton(); foreach (var e in Enum.GetNames<Enforcement>()) enforcement.AddItem(e); enforcement.Select((int)game.Enforcement); config.AddChild(enforcement);
        var waits = new CheckBox { Text = "Wait between steps", ButtonPressed = game.WaitsBetweenSteps }; config.AddChild(waits);
        config.AddChild(Btn("Apply configuration", () => _ = s.SendEditAsync(new SetConfiguration(name.Text, game.PropertiesHash,
            shape.Selected == 0 ? SchedulerShape.AlternatingActivation : SchedulerShape.PlayerTurn, (Enforcement)enforcement.Selected, waits.ButtonPressed))));
        if (s.IsCreator && game.Status == GameStatus.Lobby) AddChild(Btn("Start setup", () => _ = s.SendEditAsync(new StartSetup())));
        if (s.IsCreator && game.Status == GameStatus.Setup)
        {
            var off = s.Draft?.Terrain.Count(t => !Tabletop.Geometry.Table.ContainsPoint(s.Draft.Size, t.Position.Horizontal)) ?? 0;   // the game server's DropOffSlab rule
            AddChild(Btn(off == 0 ? "End setup" : $"End setup ({off} off-slab pieces will be dropped)", () => _ = s.SendEditAsync(new EndSetup())));
        }
    }

    static Button Btn(string text, Action a) { var b = new Button { Text = text }; b.Pressed += a; return b; }
}
```

`LobbyPanel` is the only control that sends `CreateTeam` or `PickTeam`, and `GameScreen.Rebuild` adds it only at `lobby` and `setup`, which is where the server accepts those edits: teams are locked at the flip to `playing` (owner ruling 1, 2026-09-06; the model is stated in game flow → Teams are frozen at the flip, and a team is a slot). Nothing offers a team pick from `playing` on, and nothing asks for one after a reconnect — a `TeamPanel` lists whoever `Game.Participants` puts on its team, and the `Welcome` that ends a reconnect names the same team it named before, so a returning player reappears in the panel they left. `TeamPanel.Refresh` keys on `Team.Id` and never on `MyTeamId`, so it draws a returning player and one who never dropped identically.

- [ ] **Step 4: Write `SetupToolbar.cs`**

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Screens;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

/// <summary>The closed set of setup edits, one control each. Anyone may use every one of them; the server applies in arrival order.</summary>
public partial class SetupToolbar : VBoxContainer
{
    readonly GameScreen _screen;
    public SetupToolbar(GameScreen screen) => _screen = screen;

    public override void _Ready()
    {
        var s = Main.Instance.Session; var draft = s.Draft!;
        AddChild(new Label { Text = "Setup" });

        var size = new HBoxContainer(); AddChild(size);
        var width = new SpinBox { MinValue = 12, MaxValue = 144, Value = draft.Size.Width, Suffix = "\"" }; var length = new SpinBox { MinValue = 12, MaxValue = 144, Value = draft.Size.Length, Suffix = "\"" };
        size.AddChild(width); size.AddChild(length);
        size.AddChild(Btn("Set size", () => _ = s.SendEditAsync(new SetTableSize(new TableSize(width.Value, length.Value)))));

        var texture = new OptionButton(); foreach (var t in BuiltinContent.Textures) texture.AddItem(t); AddChild(texture);
        texture.ItemSelected += i => _ = s.SendEditAsync(new SetTexture(VisualRef.OfBuiltin(BuiltinContent.Textures[(int)i])));

        var terrain = new OptionButton(); foreach (var t in BuiltinContent.Terrain.Values.OrderBy(t => t.Label)) terrain.AddItem(t.Label, 0); AddChild(terrain);
        var terrainNames = BuiltinContent.Terrain.Values.OrderBy(t => t.Label).Select(t => t.Name).ToList();
        AddChild(Btn("Add built-in terrain (beside the table)", () => _ = s.SendEditAsync(new AddTerrain(BuiltinContent.Place(terrainNames[terrain.Selected], Beside(s.Draft!), new Yaw(0))))));
        AddChild(Btn("Add my terrain…", () => _ = AddLibraryTerrain(s)));

        var generators = new HBoxContainer(); AddChild(generators);
        foreach (var planet in Enum.GetValues<Planet>())
            generators.AddChild(Btn($"{planet} planet", () => _ = Task.Run(async () => { foreach (var add in TerrainGenerators.Generate(planet, s.Draft!.Size, Random.Shared)) await s.SendEditAsync(add); })));

        var markers = new HBoxContainer(); AddChild(markers);
        markers.AddChild(Btn("Add objective", () => _ = s.SendEditAsync(new AddSetupMarker(new Marker(Guid.CreateVersion7(), MarkerKind.Objective, Centre(s.Draft!), new Yaw(0), 3, null, VisualRef.OfBuiltin("objective-ring"))))));
        markers.AddChild(Btn("Add token", () => _ = s.SendEditAsync(new AddSetupMarker(new Marker(Guid.CreateVersion7(), MarkerKind.Token, Centre(s.Draft!), new Yaw(0), 1, "token", VisualRef.OfBuiltin("chip"))))));

        var zones = new HBoxContainer(); AddChild(zones);
        var style = new OptionButton(); foreach (var z in ZoneStyles.All) style.AddItem(z); zones.AddChild(style);
        zones.AddChild(Btn("Set zones", () => _ = s.SendEditAsync(new SetZoneStyle(ZoneStyles.All[style.Selected], ZoneStyles.ShapesOf(ZoneStyles.All[style.Selected])))));
        var a = new SpinBox { MinValue = 1, MaxValue = 8, Value = 1 }; var b = new SpinBox { MinValue = 1, MaxValue = 8, Value = 2 }; zones.AddChild(a); zones.AddChild(b);
        zones.AddChild(Btn("Swap", () => _ = s.SendEditAsync(new SwapZones((int)a.Value, (int)b.Value))));

        var layouts = new HBoxContainer(); AddChild(layouts);
        layouts.AddChild(Btn("Load layout…", () => _ = LoadLayout(s)));
        var layoutName = new LineEdit { PlaceholderText = "layout name" }; layouts.AddChild(layoutName);
        layouts.AddChild(Btn("Save as", () => _ = Guarded(async () => { await s.Management.SaveLayout(s.Draft! with { Name = layoutName.Text }); _screen.Toast("Layout saved."); })));

        AddChild(new Label { Text = "Selected placement" });
        var selected = new VBoxContainer(); AddChild(selected);
        _screen.Input.SelectionChanged += () => RenderSelected(s, selected);
        RenderSelected(s, selected);
    }

    static Point3 Beside(TableState d) => new(d.Size.Width + 4 + Random.Shared.Next(0, 3) * 8, 4 + Random.Shared.Next(0, 4) * 8, 0);
    static Point3 Centre(TableState d) => new(d.Size.Width / 2, d.Size.Length / 2, 0);
    static Button Btn(string text, Action a) { var b = new Button { Text = text }; b.Pressed += a; return b; }
    async Task Guarded(Func<Task> work) { try { await work(); } catch (ManagementException e) { _screen.Toast(e.Message); } }

    async Task AddLibraryTerrain(Session s) => await Guarded(async () =>
    {
        var assets = (await s.Management.ListAssets(AssetKind.Terrain, null)).Items.Where(x => x.Uploaded).ToList();
        if (assets.Count == 0) { _screen.Toast("No terrain in your library; upload some in the web app."); return; }
        var dialog = new ConfirmationDialog { Title = "Add terrain from my library" }; var pick = new OptionButton(); foreach (var x in assets) pick.AddItem(x.Name); dialog.AddChild(pick);
        dialog.Confirmed += () => _ = Guarded(async () =>
        {
            var asset = assets[pick.Selected]; var volumes = await s.Management.GetAssetVolumes(asset.Id);
            await s.SendEditAsync(new AddTerrain(new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfAsset(asset.Id), Beside(s.Draft!), new Yaw(0), asset.Scale, volumes.Select(v => new TerrainVolume(Guid.CreateVersion7(), v.Volume)).ToList())));
        });
        AddChild(dialog); dialog.PopupCentered();
    });

    /// <summary>Setup phase -> Terrain: anyone may swap a piece's built-in mesh for an asset from their library, and the
    /// placement keeps its volumes and declarations. "Reset volumes" is how a user takes the new mesh's own instead.</summary>
    async Task SwapForAsset(Session s, Guid placementId) => await Guarded(async () =>
    {
        var assets = (await s.Management.ListAssets(AssetKind.Terrain, null)).Items.Where(x => x.Uploaded).ToList();
        if (assets.Count == 0) { _screen.Toast("No terrain in your library; upload some in the web app."); return; }
        var dialog = new ConfirmationDialog { Title = "Swap this piece's mesh (its volumes and declarations stay)" };
        var pick = new OptionButton(); foreach (var x in assets) pick.AddItem(x.Name); dialog.AddChild(pick);
        dialog.Confirmed += () => _ = s.SendEditAsync(new SwapTerrainMesh(placementId, VisualRef.OfAsset(assets[pick.Selected].Id)));
        AddChild(dialog); dialog.PopupCentered();
    });

    async Task LoadLayout(Session s) => await Guarded(async () =>
    {
        var layouts = await s.Management.ListLayouts();
        var dialog = new ConfirmationDialog { Title = "Load a layout (replaces the whole draft, size included)" }; var pick = new OptionButton(); foreach (var l in layouts) pick.AddItem($"{l.Name} ({l.Size.Width}×{l.Size.Length}, {l.Terrain} terrain)"); dialog.AddChild(pick);
        dialog.Confirmed += () => _ = Guarded(async () => await s.SendEditAsync(new ReplaceDraft(await s.Management.GetLayout(layouts[pick.Selected].Id))));
        AddChild(dialog); dialog.PopupCentered();
    });

    void RenderSelected(Session s, VBoxContainer box)
    {
        foreach (var c in box.GetChildren()) c.QueueFree();
        var sel = _screen.Input.Selected; var draft = s.Draft;
        if (sel is null || draft is null) return;
        if (draft.Markers.FirstOrDefault(m => m.Id == sel.PieceId) is { } marker) { box.AddChild(Btn("Remove marker", () => _ = s.SendEditAsync(new RemoveSetupMarker(marker.Id)))); return; }
        var t = draft.Terrain.FirstOrDefault(x => x.Id == sel.PieceId); if (t is null) return;
        box.AddChild(new Label { Text = t.Mesh.IsBuiltin ? BuiltinContent.Terrain.GetValueOrDefault(t.Mesh.Builtin!)?.Label ?? t.Mesh.Builtin : $"asset {Names.Short(t.Mesh.AssetId!.Value)}" });
        var scale = new SpinBox { MinValue = 0.25, MaxValue = 4, Step = 0.25, Value = t.Scale }; box.AddChild(scale); scale.ValueChanged += v => _ = s.SendEditAsync(new ScaleTerrain(t.Id, v));
        box.AddChild(Btn("Remove", () => _ = s.SendEditAsync(new RemoveTerrain(t.Id))));
        var swap = new OptionButton(); var names = BuiltinContent.Terrain.Keys.Order().ToList(); foreach (var n in names) swap.AddItem(n); box.AddChild(swap);
        box.AddChild(Btn("Swap mesh for this built-in (keeps volumes)", () => _ = s.SendEditAsync(new SwapTerrainMesh(t.Id, VisualRef.OfBuiltin(names[swap.Selected])))));
        box.AddChild(Btn("Swap mesh for one of my assets (keeps volumes)", () => _ = SwapForAsset(s, t.Id)));
        box.AddChild(Btn("Reset volumes to the source's", () => _ = Guarded(async () =>
            await s.SendEditAsync(new OverrideTerrainVolumes(t.Id, t.Mesh.IsBuiltin ? BuiltinContent.VolumesOf(t.Mesh.Builtin!) : (await s.Management.GetAssetVolumes(t.Mesh.AssetId!.Value)).Select(v => new TerrainVolume(Guid.CreateVersion7(), v.Volume)).ToList())))));
        foreach (var (tv, i) in t.Volumes.Select((v, i) => (v, i)))
        {
            var row = new HBoxContainer(); box.AddChild(row);
            row.AddChild(new Label { Text = $"volume {i + 1} {tv.Volume.Kind} {tv.Volume.Bottom}–{tv.Volume.Top}\"" });
            foreach (var flag in new[] { TerrainDeclarations.Impassable, TerrainDeclarations.BlocksLineOfSight, TerrainDeclarations.SeeInAndOut, TerrainDeclarations.Cover, TerrainDeclarations.Difficult, TerrainDeclarations.Dangerous })
            {
                var cb = new CheckBox { Text = flag.ToString(), ButtonPressed = tv.Volume.Declarations.HasFlag(flag) };
                cb.Toggled += on => _ = s.SendEditAsync(new OverrideTerrainVolumes(t.Id, t.Volumes.Select(v => v.Id != tv.Id ? v : v with { Volume = v.Volume with { Declarations = on ? v.Volume.Declarations | flag : v.Volume.Declarations & ~flag } }).ToList()));
                row.AddChild(cb);
            }
            var notes = new LineEdit { Text = tv.Volume.Notes ?? "", PlaceholderText = "notes" }; row.AddChild(notes);
            notes.TextSubmitted += text => _ = s.SendEditAsync(new OverrideTerrainVolumes(t.Id, t.Volumes.Select(v => v.Id != tv.Id ? v : v with { Volume = v.Volume with { Notes = text.Length == 0 ? null : text } }).ToList()));
        }
    }
}
```

Editing a volume's footprint and heights is the same `OverrideTerrainVolumes` with `SpinBox`es for `Footprint.A/B`, `Bottom`, `Top` and an `OptionButton` for `Kind`; add them to the row in the same pattern as the declaration checkboxes.

- [ ] **Step 5: Run it with two clients.** Lobby: teams, picks, configuration and "Start setup" (creator only; the other client's button is absent and a forged `StartSetup` gets `NotPermitted`). Setup: size, texture, built-in terrain beside the table, a *Hospitable planet* batch of 13–21 pieces beside the slab, dragging pieces on with the ruler, zones with team colours (grey for an ordinal with no team), swap, a layout saved then loaded on a fresh game (size included), a volume's declaration toggled and the proxy recoloured, "End setup" dropping the off-slab count. The desync arm of the `ConnectionLost` toast has no manual trigger, because nothing a player can do makes the server emit an event the applier cannot apply; `SessionTests` covers the resync and the drop to Home, and this toast's two desync arms are read rather than run. `./scripts/check-client` prints nothing. Commit: `Add the game screen with the lobby panel and the setup toolbar`.

### Task 14: The Ask panel and the declared move

**Files:**
- Create: `client/src/Panels/AskPanel.cs`, `client/src/Table/MoveRegionNode.cs`, `client/src/Screens/GameScreen.Play.cs`

**Interfaces:**
- Consumes: `AskViewModel` (`Mine`, `SpendableBy`, `ConfirmLabel`), `AskView`, `AskMode`, `MoveRuler` (`Read`, `UsedInFrame`, `Region`, `ExitRegion`, `IsDisembark`), `Names` (Core); `Session` (`Projection`, `Game`, `MyTeamId`, `Settings.MutedWindowKinds`, `SendCommandAsync`, `EventsApplied`, `Log`); `TableView` (with `ShowExitGhosts`/`ClearExitGhosts`, Task 11), `TableInput` (with `PieceLeftClicked` and `CancelDrag`, Task 12), `RulerNode`, `PieceNode` (Table); `Ask`, `Options`, `PieceSelection`, `Allocation`, `Assignment`, `MoveRegion`, `MoveContainment`, `TokenSpend`, `Confirmation`, `AnswerAsk`, `Chosen`, `SelectedPieces`, `Allocated`, `Assigned`, `Destinations`, `TokensSpent`, `Confirmed`, `Passed`, `ModelRelocation`, `OnTable`, `Inside`, `Placement` (Protocol); `PlacedModel`, `ModelBody`, `Yaw`, `WorldVolume` (Geometry).
- Produces: `partial class AskPanel : PanelContainer` — `void Bind(Session, TableView, TableInput)`; renders `AskViewModel.Mine(...)` by mode: `Choose` → one button per option (highlighting each option's pieces on hover), `PickPieces` → click eligible pieces on the table (highlighted), a counter `n of min–max` that names the piece when `Max` is 1, the consumed click cancelling the drag it started, "Done" → `SelectedPieces`; `Allocate` → a `SpinBox` per target summing to `Amount`, "Done" → `Allocated`; `Assign` → an `OptionButton` per item over the targets, "Done" → `Assigned`; `Move` → the region drawn (`MoveRegionNode`), the models draggable with the ruler showing distance and remaining allowance (`MoveRuler.Read(start, current, Allowance − UsedInFrame)`), positions streamed as `proposed` ghosts, "Commit" → `Destinations` with the settled positions — and, when the legal set's `Containment` says so, two more things: any `Enterable` container is a click target whose click answers `Destinations` with `Inside(container)` for **every** model of the unit, and a non-null `ExitingId` switches the whole mode to a disembark, where the region is `MoveRuler.ExitRegion` about the container's own base, the drag ghosts are `TableView.ShowExitGhosts` on the container's node, and the remaining-allowance readout is suppressed; `TokenSpend` → one private `SpinBox` per entry of `AskViewModel.SpendableBy`, prefixed with the model's name, bounded by that model's own maximum, with no total anywhere (p.13: any number up to what each model holds), "Spend" → `TokensSpent` of the non-zero boxes, plus `AnsweredTeams` as "Team 2 has answered" and, once the server closes the sealed Ask, every team's answer at once from `ask_closed` (`LogText.Answer`); `Confirm` → one button labelled `AskViewModel.ConfirmLabel` ("Continue" for a step gate, "End the round" for the scheduler's `end_round` Ask) → `Confirmed`; a "Pass" button whenever `AllowPass`; an `AutoPass` view sends `Passed` at once and shows "auto-passed (muted: {kind})" with an "Unmute" link; `WaitingFor` renders as "Waiting for Team N…"; `AnsweredBy` shows under a sealed Ask. A `Refused` for the panel's last command shows in the panel (`strict` enforcement: "the server refused: outside the legal set"). `partial class MoveRegionNode : Node3D` — `Show(IReadOnlyList<Point2> boundaryWire, Color)`, `Hide()`. `GameScreen.Play.cs` — `partial void ShowPlayPanels(bool)` creating the `AskPanel` (and Task 15's `LogPanel`) at `playing`/`ended`.
- The move button of the spec is the activate Ask's option (`{unit}:{Action}`): choosing it starts the sequence, whose move step opens the `MoveRegion` Ask (contracts plan spec correction 8). "Hand moves arrange models and never end a turn" is the plain drag of Task 12.
- **The panel keys on the legal set, never on the question name**, which is why a second ruleset question needs no new branch. Two of them arrive carrying a `move_region`: **`move`**, opened by the move step of `hold`/`advance`/`rush`/`charge` (interpreter plan Task 14), and — added 2026-09-06 — **`disembark`**, opened by `transport-destroyed`'s `place` step when a wrecked container's survivors must be placed fully within 6" of it before it is removed (interpreter plan Task 19, rulebook p.15). `disembark` always carries `Containment.ExitingId`, sets `AllowPass` false, and puts the exit distance itself in `Allowance` — which `MoveRuler.IsDisembark` makes the panel ignore, so it renders exactly as a disembark move does and shares every line of `RenderMove`. Neither name belongs in `AskQuestions`: those are the engine's own eight, and a ruleset's question names are plain strings (`move`, `disembark`, `shoot-or-not`, `declare-targets`, `charge-target`, …) that this panel never switches on. The one place a question name is read at all is `AskViewModel.ConfirmLabel`, and it only ever compares against `AskQuestions.EndRound`.

Spec: client shell → Asks and windows (all of it), Input and presence (the move button; the region at the allowance; the checkpoint's remaining distance from the frame; the `proposed` ghost until commit); game flow → Inside a container (an occupant has no table position of its own, moves when the container moves, and disembarks relative to it; enter or exit with any move action, never both in one activation, exit within 6"); rulebook p.15 `Transport(X)` (*"only one model needs to reach the Transport for everyone to embark"*, *"must stay fully within 6” of it when exiting"*); action sequences → Asks (any team member answers), Windows (react or pass; sealed simultaneous), One roll primitive (the confirm gate is an Ask); scheduler → The turn is an Ask, There is no round limit (the `end_round` Ask at the round boundary, ruled 2026-09-06 — one `Confirmation` addressed to every team, rendered by the same panel), Windows (muted kinds per player; the pass is that player's pass); whole-design review ruling 1 (`strict` refuses, the panel says so); geometry plan → *Interfaces needed → Client shell plan* (`Movement.Region(...).Boundary`, `Movement.Check(...).Distance`) and Task 6 (`Containment.BaseFullyWithin`, which `MoveRuler.ExitRegion` turns into the disembark boundary); contracts plan Task 8 (the legal set and answer shapes, `MoveRegion.Containment`, `TokenSpend.MaxByModel`, `TokensSpent.Counts`); interpreter plan Tasks 14 and 19 (what the server offers and expects back); rulebook p.13 `Caster(X)` (the team's simultaneous spend, one input per eligible model, each bounded by what that model holds, no total) and `Takedown` (the shooter picks the model, so the eligible set is model ids and a click on a model answers it).

- [ ] **Step 1: Write `MoveRegionNode.cs`**

```csharp
using Godot;
using Tabletop.Geometry;

namespace Tabletop.Client.Table;

/// <summary>The reachable region as a flat polygon just above the slab: the server's parameters, rendered by Tabletop.Geometry, drawn here.</summary>
public partial class MoveRegionNode : Node3D
{
    MeshInstance3D? _mesh;
    public void Show(IReadOnlyList<Point2> boundaryWire, Color colour)
    {
        Hide();
        var verts = boundaryWire.Select(p => WorldFrame.ToGodot(new Point3(p.X, p.Y, 0.03))).ToArray();
        var centre = verts.Aggregate(Vector3.Zero, (a, v) => a + v) / verts.Length;
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        for (var i = 0; i < verts.Length; i++) { st.AddVertex(centre); st.AddVertex(verts[(i + 1) % verts.Length]); st.AddVertex(verts[i]); }
        _mesh = new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = Palette.Translucent(colour with { A = 0.25f }) };
        AddChild(_mesh);
    }
    public new void Hide() { _mesh?.QueueFree(); _mesh = null; }
}
```

Both regions this node draws are star-shaped about the point the boundary is measured from — the start placement for `MoveRuler.Region` (geometry plan Task 12) and the container's base centre for `MoveRuler.ExitRegion` (each ray marches outward from that centre and stops, which is star-shaped by construction, blockers included) — so a fan from the vertex centroid is exact in both cases and the node needs no variant.

- [ ] **Step 2: Write `AskPanel.cs`**

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Table;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

/// <summary>One panel, always in the same place, rendering whatever is addressed to the player's team.</summary>
public partial class AskPanel : PanelContainer
{
    Session _s = null!; TableView _view = null!; TableInput _input = null!;
    readonly VBoxContainer _box = new(); readonly Label _title = new(), _note = new(); MoveRegionNode? _region;
    Guid? _renderedAsk, _lastCommand; readonly List<Guid> _picked = new(); Dictionary<Guid, Point3>? _proposed;
    readonly HashSet<Guid> _sealed = new(); string? _revealed;

    public void Bind(Session s, TableView view, TableInput input)
    {
        _s = s; _view = view; _input = input;
        AddChild(_box); _box.AddChild(_title); _box.AddChild(_note);
        view.AddChild(_region = new MoveRegionNode());
        s.EventsApplied += events => { Reveal(events); Render(); }; s.GameChanged += Render; s.BoardChanged += Render;
        s.RefusedReceived += r => { if (r.MessageId == _lastCommand) { _note.Text = $"The server refused: {r.Detail}"; _renderedAsk = null; Render(); } };
        Render();
    }

    void Render()
    {
        var p = _s.Projection; var game = _s.Game;
        if (p is null || game is null) return;
        var mine = AskViewModel.Mine(p, game, _s.MyTeamId, _s.Settings.MutedWindowKinds);
        if (mine is null)
        {
            ClearControls(); _renderedAsk = null; _region?.Hide();
            _title.Text = AskViewModel.WaitingFor(p, game, _s.MyTeamId) is { } teams ? $"Waiting for {teams}…" : (p.OpenAsks.Count == 0 ? "" : "Waiting…");
            _note.Text = _revealed ?? "";                                  // a sealed Ask's reveal stays up until the next Ask
            return;
        }
        if (_renderedAsk == mine.Ask.Id) return;
        ClearControls(); _renderedAsk = mine.Ask.Id; _picked.Clear(); _proposed = null; _note.Text = ""; _revealed = null;
        _title.Text = mine.Ask.Prompt;
        foreach (var id in mine.Highlights) _view.PieceAt(id)?.SetHighlight(Palette.Selected);
        if (mine.AutoPass) { _ = Answer(mine.Ask, new Passed()); _note.Text = $"auto-passed (muted: {mine.Ask.WindowKind})"; _box.AddChild(Btn("Unmute", () => _s.UpdateSettings(x => x.Unmute(mine.Ask.WindowKind!)))); return; }
        var names = new Names(game, p);
        switch (mine.Mode)
        {
            case AskMode.Choose: foreach (var c in ((Options)mine.Ask.Legal).Choices) _box.AddChild(Btn(c.Label, () => _ = Answer(mine.Ask, new Chosen(c.Id)))); break;
            case AskMode.PickPieces:
                var sel = (PieceSelection)mine.Ask.Legal; var count = new Label { Text = $"0 of {sel.Min}–{sel.Max}" }; _box.AddChild(count);
                _input.PieceLeftClicked += OnPick;
                // The clicked piece's own id first, then the id selection widened to: an Eligible set holds
                // model ids (a Takedown's chosen model, p.14) or unit ids (a charge target, a deploy), and a bare
                // click on a model selects its unit — so keying on Selected.PieceId alone can never match a
                // model-id set. A click that answered is not also a drag: CancelDrag drops the one OnLeftDown
                // started, which for a model-id set is a drag of the unit the player was only pointing at.
                void OnPick(Guid clicked)
                {
                    var id = sel.Eligible.Contains(clicked) ? clicked
                        : _input.Selected is { } s && sel.Eligible.Contains(s.PieceId) ? s.PieceId : (Guid?)null;
                    if (id is { } pick && !_picked.Contains(pick) && _picked.Count < sel.Max)
                    {
                        _picked.Add(pick); _input.CancelDrag();
                        // One model out of a unit is worth reading back before Done, so a single pick shows the name rather than a tally.
                        count.Text = sel.Max == 1 ? names.Piece(pick) : $"{_picked.Count} of {sel.Min}–{sel.Max}";
                        _view.PieceAt(pick)?.SetHighlight(Palette.Ghost);
                    }
                }
                _box.AddChild(Btn("Done", () => { _input.PieceLeftClicked -= OnPick; _ = Answer(mine.Ask, new SelectedPieces(_picked.ToList())); }));
                break;
            case AskMode.Allocate:
                var alloc = (Allocation)mine.Ask.Legal; var spins = alloc.Targets.ToDictionary(t => t, t => new SpinBox { MinValue = 0, MaxValue = alloc.Amount, Prefix = names.Piece(t) });
                foreach (var sp in spins.Values) _box.AddChild(sp);
                _box.AddChild(Btn($"Done (assign {alloc.Amount})", () => _ = Answer(mine.Ask, new Allocated(spins.ToDictionary(kv => kv.Key, kv => (int)kv.Value.Value)))));
                break;
            case AskMode.Assign:
                var asg = (Assignment)mine.Ask.Legal; var picks = new Dictionary<string, OptionButton>();
                foreach (var item in asg.Items) { var ob = new OptionButton(); foreach (var t in asg.Targets) ob.AddItem(names.Piece(t)); _box.AddChild(new Label { Text = item.Label }); _box.AddChild(ob); picks[item.Id] = ob; }
                _box.AddChild(Btn("Done", () => _ = Answer(mine.Ask, new Assigned(picks.ToDictionary(kv => kv.Key, kv => asg.Targets[kv.Value.Selected])))));
                break;
            case AskMode.Move: RenderMove(mine.Ask, (MoveRegion)mine.Ask.Legal, p, names); break;
            case AskMode.TokenSpend:
                // p.13: "may spend any number of spell tokens", capped per model by what that model holds. So one box per
                // model, each bounded by its own maximum, and no total: the allocation mode's single amount split exactly
                // would make spending one token of a possible five illegal, which is the wrong answer to the wrong question.
                // SpendableBy drops the other team's spenders, which the map carries because the window addresses both.
                var spendable = AskViewModel.SpendableBy(p, _s.Game!, _s.MyTeamId!.Value, (TokenSpend)mine.Ask.Legal);
                var spends = spendable.ToDictionary(kv => kv.Key, kv => new SpinBox { MinValue = 0, MaxValue = kv.Value, Prefix = names.Piece(kv.Key) });
                foreach (var sp in spends.Values) _box.AddChild(sp);
                _box.AddChild(Btn("Spend", () => _ = Answer(mine.Ask,
                    new TokensSpent(spends.Where(kv => kv.Value.Value > 0).ToDictionary(kv => kv.Key, kv => (int)kv.Value.Value)))));
                if (mine.AnsweredTeams.Count > 0) _box.AddChild(new Label { Text = string.Join(", ", mine.AnsweredTeams.Select(t => $"{t} has answered")) });
                break;
            case AskMode.Confirm: _box.AddChild(Btn(AskViewModel.ConfirmLabel(mine.Ask), () => _ = Answer(mine.Ask, new Confirmed()))); break;   // "Continue" for a step gate, "End the round" at the round boundary
        }
        if (mine.Ask.AllowPass) _box.AddChild(Btn("Pass", () => _ = Answer(mine.Ask, new Passed())));
        if (mine.Ask.WindowKind is { } kind) _box.AddChild(Btn($"Mute '{kind}' windows", () => _s.UpdateSettings(x => x.Mute(kind))));
        if (AskViewModel.AnsweredBy(_s.Log, mine.Ask.Id, names) is { } who) _note.Text = $"{who} answered for your team";
    }

    void RenderMove(Ask ask, MoveRegion legal, Projection p, Names names)
    {
        if (legal.ModelIds.Count == 0) return;                                      // nothing to move; the server closes the Ask
        var lead = p.Models[legal.ModelIds[0]];
        var body = new ModelBody(lead.Slot.Base, lead.Slot.Height);
        var ordinal = _s.Game?.Teams.FirstOrDefault(t => t.Id == _s.MyTeamId)?.Ordinal ?? 1;
        _input.Proposing.Clear(); foreach (var id in legal.ModelIds) _input.Proposing.Add(id);
        _input.ModelDragged += OnDragged;

        if (MoveRuler.IsDisembark(legal))
        {
            // The unit is inside, so there is no start placement to reach from and the straight-line region is meaningless:
            // the offer is p.15's "fully within 6" of it when exiting", drawn about the container's own base. Allowance is the
            // action's remaining distance and binds nothing here, so no remaining-allowance readout goes up and the ruler gets none.
            var containerId = legal.Containment!.ExitingId!.Value;
            if (p.Models.GetValueOrDefault(containerId)?.Location is not OnTable at)
            {
                _note.Text = "The container is no longer on the table.";               // it was wrecked and shelved; the server will close this Ask
                return;
            }
            var host = p.Models[containerId];
            var container = new PlacedModel(new ModelBody(host.Slot.Base, host.Slot.Height), at.Placement);
            var within = legal.Containment.ExitWithin ?? 0;
            _region!.Show(MoveRuler.ExitRegion(body, container, legal, _view.Volumes, _view.Size), Palette.Team(ordinal));
            _view.ShowExitGhosts(legal.ModelIds, containerId);                          // Inside carries no Placement: the ghosts start on the container's node
            _note.Text = $"Disembark: place every model fully within {within:0.0}\" of {names.Piece(containerId)}. Drag them out, then commit.";
            _input.Allowance = null;
            _proposed = legal.ModelIds.ToDictionary(id => id, _ => at.Placement.Position);
        }
        else
        {
            if (lead.Location is not OnTable on) return;
            var frame = ask.FrameId is { } f ? p.Stack.FirstOrDefault(x => x.Id == f) : null;
            var used = frame is null ? 0 : MoveRuler.UsedInFrame(frame, lead.Id);
            var remaining = Math.Max(0, legal.Allowance - used);
            _region!.Show(MoveRuler.Region(new PlacedModel(body, on.Placement), legal with { Allowance = remaining }, _view.Volumes, _view.Size), Palette.Team(ordinal));
            _note.Text = $"Move up to {remaining:0.0}\" (used {used:0.0}\"). Drag the models, then commit.";
            _input.Allowance = remaining;
            _proposed = legal.ModelIds.ToDictionary(id => id, id => ((OnTable)p.Models[id].Location).Placement.Position);
            OfferEmbark(ask, legal, p, names);
        }
        _box.AddChild(Btn("Commit move", () => _ = Answer(ask, new Destinations(legal.ModelIds.Select(id =>
            new ModelRelocation(id, p.Models[id].Location, new OnTable(new Placement(_proposed![id], YawOf(p, id))))).ToList()))));
    }

    /// <summary>
    /// p.15: "only one model needs to reach the Transport for everyone to embark". The containers the server listed are the
    /// click targets — already highlighted, because <c>AskViewModel.Mine</c> puts them in <c>Highlights</c> — and clicking one
    /// answers the <b>whole unit</b> into it: one relocation per model of the unit to the same <c>Inside</c>, never one per
    /// reaching model. Nothing is dragged and nothing is measured, so the click cancels the drag it just started on the
    /// container's node. The embark offer sits beside the ordinary move: the player may still drag and commit instead.
    /// </summary>
    void OfferEmbark(Ask ask, MoveRegion legal, Projection p, Names names)
    {
        IReadOnlyList<Guid> enterable = legal.Containment?.Enterable ?? [];
        if (enterable.Count == 0) return;
        _note.Text += $" Or click a container in reach to embark: {string.Join(", ", enterable.Select(names.Piece))}.";
        _input.PieceLeftClicked += OnEmbark;
        void OnEmbark(Guid clicked)
        {
            if (_renderedAsk != ask.Id) { _input.PieceLeftClicked -= OnEmbark; return; }   // a later Ask is up: this closure is stale
            if (!enterable.Contains(clicked)) return;
            _input.PieceLeftClicked -= OnEmbark;
            _input.CancelDrag();
            _ = Answer(ask, new Destinations(legal.ModelIds.Select(id => new ModelRelocation(id, p.Models[id].Location, new Inside(clicked))).ToList()));
        }
    }

    /// <summary>The yaw a destination keeps: the model's own if it has a placement, else the container's — an occupant has none (game flow → Inside a container).</summary>
    static Yaw YawOf(Projection p, Guid modelId) => p.Models[modelId].Location switch
    {
        OnTable on => on.Placement.Yaw,
        Inside i when p.Models.GetValueOrDefault(i.ContainerId)?.Location is OnTable c => c.Placement.Yaw,
        _ => new Yaw(0),
    };

    void OnDragged(Guid modelId, Point3 position) { if (_proposed is not null && _proposed.ContainsKey(modelId)) _proposed[modelId] = position; }

    /// <summary>A sealed Ask withholds every answer until it closes; the closing frame carries them all, and that frame is
    /// the reveal — shown at once, for the teams that answered and for the team that did not (client shell → Asks and windows).</summary>
    void Reveal(IReadOnlyList<GameEvent> events)
    {
        if (_s.Game is null || _s.Projection is null) return;
        var names = new Names(_s.Game, _s.Projection);
        foreach (var payload in events.Select(e => e.Payload))
        {
            if (payload is AskOpened o && o.Ask.Sealed) _sealed.Add(o.Ask.Id);
            if (payload is AskClosed c && _sealed.Remove(c.AskId) && c.Answers.Count > 0)
                _revealed = "Revealed: " + string.Join(", ", c.Answers.Select(a => $"{names.Team(a.TeamId)} {LogText.Answer(a.Answer, names)}"));
        }
    }

    async Task Answer(Ask ask, AskAnswer answer)
    {
        _input.ModelDragged -= OnDragged; _input.Proposing.Clear(); _input.Allowance = null; _proposed = null;
        _view.ClearExitGhosts();                                              // the occupants go back to having no node until the event lands
        _lastCommand = await _s.SendCommandAsync(new AnswerAsk(ask.Id, answer)); _region?.Hide();
    }
    void ClearControls() { foreach (var c in _box.GetChildren()) if (c != _title && c != _note) c.QueueFree(); _view.ClearExitGhosts(); foreach (var n in _view.Pieces.OfType<PieceNode>()) n.SetHighlight(null); }
    static Button Btn(string text, Action a) { var b = new Button { Text = text }; b.Pressed += a; return b; }
}
```

**The round boundary needs no control of its own.** Ruled 2026-09-06: the round does not advance by itself, and the scheduler stops at the boundary with one unframed `Confirmation` Ask (question `AskQuestions.EndRound`, `AllowPass` false) addressed to every team that has a participant. The panel renders it as it stands — `AskViewModel.Mine` matches on `Teams.Contains(myTeam)` and does not care that several teams are named, `Highlights` is empty for a `Confirmation`, `AutoPass` cannot fire (no `WindowKind`), and there is no Pass button because the Ask allows none — so the two words this plan adds are the button's label (`ConfirmLabel`) and the answer's log line ("Clay confirmed for Team 1", with the engine's "started round 2" under it). No end-round button goes on the game screen or the team panel: the spec's *"one Ask panel, always in the same place, rendering whatever Ask is currently addressed to the player's team"* is the whole affordance, a second control would be a second path to the same command, and the menu already has *Set the round* for a player who wants to jump. Because the Ask is unsealed, whoever answers first ends the round and the panel of every other player replaces it with the next round's turn Ask on the same `EventsApplied` tick; the answering player is named in the log line, as with any answer.

While a declared move is open the panel fills `TableInput.Proposing`, so the drag streams `proposed` ghosts and the release sends no `MoveModels`; the panel's Commit sends `Destinations`, whose `To.Z` is the client's settled height (contracts plan Task 8). The ruler during that drag reads `MoveRuler.Read(start, current, remaining)` — `TableInput` passes `Proposing.Contains(lead) ? _screenAllowance : null`, a `double? Allowance` property the panel sets alongside `Proposing`.

**A disembark is the same panel with three things swapped.** `MoveRuler.IsDisembark` splits `RenderMove` in two, and the disembark half differs only in where the region comes from, where the ghosts start, and what the readout says:

| | an ordinary move | a disembark (`Containment.ExitingId` set) |
| --- | --- | --- |
| region | `MoveRuler.Region` from the start placement | `MoveRuler.ExitRegion` about the container's base |
| draggable node | the model's own `PieceNode` | `TableView.ShowExitGhosts` on the container's node |
| `TableInput.Allowance` | `Allowance − UsedInFrame` | `null` — `Allowance` binds nothing here (contracts plan Task 8) |
| the ruler | start → current, with the remaining allowance | start (the container) → current, distance only |
| `ModelRelocation.From` | the model's `OnTable` | the model's `Inside`, passed straight through |
| yaw kept | the model's own | the container's, through `YawOf` |

Everything else is shared: `Proposing`, the `proposed` presence stream, the Commit button, `Answer`, the `Refused` line. The ruler still draws — from the container's placement, because `TableInput.Position` resolves an `Inside` model to its container (Task 12) — and it shows a bare distance rather than a remaining allowance, which is right: p.14 constrains where the model may *end*, not how far it travels. The exit distance is on the panel's note instead.

**Embarking answers with the unit, not the model.** `OfferEmbark` sends one `ModelRelocation` per model of the unit, all to the same `Inside(container)`, because p.15 embarks the whole unit when one model reaches the transport; the server's `MoveStep.Apply` takes that as sent and only drops an `Inside` whose container is not on the table (interpreter plan Task 14). There is no per-reaching-model answer and no partial embark.

- [ ] **Step 3: Write `GameScreen.Play.cs`**

```csharp
using Godot;
using Tabletop.Client.Panels;

namespace Tabletop.Client.Screens;

public partial class GameScreen
{
    AskPanel? _askPanel;

    partial void ShowPlayPanels(bool playing)
    {
        if (!playing) { _askPanel?.QueueFree(); _askPanel = null; return; }
        if (_askPanel is not null) return;
        _askPanel = new AskPanel(); _side.AddChild(_askPanel); _askPanel.Bind(_session, Table, Input);
    }
}
```

- [ ] **Step 4: Run it against the game server skeleton.** With `InertEngine` no Ask ever opens, so exercise the panel with the skeleton's test engine: temporarily register `FixedAskEngineFactory` with an activate Ask in `server/game/Program.cs` (or run the scheduler plan's server once it exists). Expected: the option buttons appear for the addressed team, "Waiting for Team 1…" on the other client, the choice logs `ask_answered` and `ask_closed`, the panel shows who answered; muting `casting` in settings auto-passes a simultaneous token spend. Then, once the interpreter plan's casting is in the server, cast with two token holders in reach: the panel shows one spin box per eligible model of my own team and none for the other team's spenders, each box stops at that model's own token count, there is no total to satisfy, the other team's panel says "Team 2 has answered" without saying what, and the close prints *"Revealed: Team 1 spent 2 tokens (…), Team 2 spent 1 token (…)"* with the same words in the log. A Takedown's shot opens a pick over the target unit's models: the models highlight rather than the unit, one click takes it and drags nothing, the panel names the model it took, and the log line reads *"Clay answered for Team 1: picked"* followed by that model's unit and slot name. Once the scheduler plan's server exists, play a two-team round out: at the boundary both clients show one button reading "End the round", either player's click starts the next round, and the log reads *"… confirmed for Team 2"* then *"the game started round 2"*. Then, once the interpreter plan's `Transport(X)` is in the server, play the two containment paths: with a transport in reach the move Ask's note names it and it is highlighted, one click puts **every** model of the unit inside it and the log reads *"Clay put Battle Brothers inside Bus (Bus)"* on one line, and the occupants' nodes vanish from the table; activating that unit next turn opens the disembark offer instead — the region is a rounded-square band 6" out from the transport's base rather than a circle about its centre, one ghost per model sits on the transport, the note reads *"place every model fully within 6.0\" of Bus (Bus)"* with no remaining-allowance figure, dragging a ghost draws a ruler with a distance and no allowance, and Commit sends `Destinations` whose `From` is `Inside`. A ghost dropped outside the region still commits and the server leaves that model inside — the offer is drawn, not enforced, here. `./scripts/check-client` prints nothing. Commit: `Add the Ask panel and the declared move's region, ghost and commit`.

### Task 15: The log panel with undo-to-here, the right-click menu, animation, Leave

**Files:**
- Create: `client/src/Panels/LogPanel.cs`, `client/src/Panels/PieceMenu.cs`, `client/src/Table/Animator.cs`
- Modify: `client/src/Screens/GameScreen.Play.cs`

**Interfaces:**
- Consumes: `LogText`, `Names`, `MenuModel`, `MenuItem`, `MenuInput`, `AnimationQueue` (Core); `Session` (`Log`, `Projection`, `PendingUndoTo`, `UndoToAsync`, `SendCommandAsync`, `EventsApplied`, `Settings.Animate`, `UpdateSettings`, `LeaveAsync`); `TableView`, `TableInput.PieceRightClicked`, `TeamPanel.RightClicked`, `PieceNode` (Table); `GameEvent`, `ModelsMoved`, `StepCompleted`, `PlaceMarker`, `Marker`, `MarkerKind`, `RerollDice`, `SetDice`, `AddModifier` (Protocol).
- Produces: `partial class LogPanel : PanelContainer` — `Bind(Session, TableView)`; one row per event in `Session.Log` (`#{seq}  {LogText.Describe}`), newest at the bottom, auto-scrolled; the `undone` markers shown and the undone range struck through (`EventApplier.EffectiveLog`); clicking a row highlights `LogText.Touched`; "Undo to here" on a row calls `UndoToAsync(seq)` and greys every row above it until `PendingUndoTo` clears; a row with rolls offers "Reroll all" (`RerollDice(frame, roll, [])`) and "Set die…" (`SetDice`) **per `RollRecord` on the step**, labelled by roll name and threshold when the step carries more than one, plus a single "Add modifier…" (`AddModifier`), which names a roll by name rather than by id and so acts on all of them. A step carries several whenever the ruleset splits one roll: OPR's `roll-to-hit` takes one pool per Quality value the attacking models bring, because p.4 tests each attack against *"the model's Quality value"* and a joined hero brings its own (interpreter question 13). `partial class PieceMenu : PopupMenu` — `Open(IReadOnlyList<MenuItem>, Vector2 screen)`: one entry per item; `Integer`/`Text` items pop a one-field dialog, `PieceId` items wait for the next click on a piece; the built command goes to `SendCommandAsync`; no confirmation dialog anywhere. Wired: right-click on a model → `MenuModel.ForModel` + `ForFrame`; Shift-right-click on a model → `ForUnit`; a marker → `ForMarker`; a team panel → `ForTeam`; Alt-right-click on the table → "Drop a token here" (`PlaceMarker` of a `Token` at the point) and "Leave the game" (`LeaveAsync`). `sealed class Animator(Session, TableView)` — subscribes to `EventsApplied`, enqueues into `AnimationQueue`, and each frame takes `Take(Settings.Animate)`: for a `ModelsMoved` (alone or inside a `StepCompleted`) the piece tweens from its drawn position to the projection's over 0.4 s; for rolls a transient `Label3D` over the actor unit for 1.5 s carrying the whole readout of **every** roll on the step, one block each, stacked — the roll's name, its threshold, the faces with the passing dice in brackets, and each modifier by source — and no die object of any kind (owner ruling, 2026-09-06); everything else redraws at once. Presence is never animated. An "Animate" checkbox in the side panel writes `Settings.Animate`.

Spec: client shell → The log, undo and the menu (every event with actor and sequence; newest at the bottom; clicking highlights; undo-to-here greys until confirmed; presence never appears; the menu is every property editable, each item one command, no admin mode, no confirmation; Leave is a menu action), Animation (the player's setting; a queue between projection and renderer; may lag, never delays an Ask; presence never animated), Screens → Game (Leave calls `/leave`); resolution → Players roll their own dice (reroll per roll; a hand-added modifier); scheduler → Deployment (every roll result editable from the menu); game flow → Nothing is an invariant; whole-design review → Tokens had no home in the log (a token dropped in play is a logged command).

- [ ] **Step 1: Write `LogPanel.cs`**

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Table;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

public partial class LogPanel : PanelContainer
{
    Session _s = null!; TableView _view = null!; readonly ScrollContainer _scroll = new(); readonly VBoxContainer _rows = new();

    public void Bind(Session s, TableView view)
    {
        _s = s; _view = view;
        AddChild(_scroll); _scroll.AddChild(_rows); _scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        s.EventsApplied += _ => Render(); s.BoardChanged += Render;
        Render();
    }

    void Render()
    {
        foreach (var c in _rows.GetChildren()) c.QueueFree();
        var p = _s.Projection; var game = _s.Game;
        if (p is null || game is null) return;
        var names = new Names(game, p);
        var live = EventApplier.EffectiveLog(_s.Log).Select(e => e.Seq).ToHashSet();
        foreach (var e in _s.Log)
        {
            var row = new HBoxContainer(); _rows.AddChild(row);
            var text = new Button { Text = $"#{e.Seq}  {LogText.Describe(e, names)}", Flat = true, Alignment = HorizontalAlignment.Left, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            if (!live.Contains(e.Seq) && e.Payload is not Undone) text.Modulate = new Color(0.5f, 0.5f, 0.5f);
            if (_s.PendingUndoTo is { } to && e.Seq > to) text.Modulate = new Color(0.6f, 0.6f, 0.6f);
            text.Pressed += () => { foreach (var n in _view.Pieces.OfType<PieceNode>()) n.SetHighlight(null); foreach (var id in LogText.Touched(e)) _view.PieceAt(id)?.SetHighlight(Colors.Yellow); };
            row.AddChild(text);
            if (live.Contains(e.Seq) && e.Payload is not Undone) { var undo = new Button { Text = "Undo to here", Flat = true }; undo.Pressed += () => _ = _s.UndoToAsync(e.Seq); row.AddChild(undo); }
            if (e.Payload is StepCompleted { Rolls.Count: > 0 } step)
            {
                // A step holds several rolls whenever the ruleset splits one: OPR's roll-to-hit takes one pool per Quality value the
                // attacking models bring, because p.4 tests each attack against "the model's Quality value" and a joined hero brings
                // its own. Reroll and set-die name a RollRecord, so there is one pair per roll, labelled by threshold when it would
                // otherwise be ambiguous. AddModifier names the roll by name, so it stays one button for the step.
                foreach (var roll in step.Rolls)
                {
                    var which = step.Rolls.Count == 1 ? "" : $" {roll.Roll}{(roll.Threshold is { } t ? $" {t}+" : "")}";
                    var reroll = new Button { Text = $"Reroll all{which}", Flat = true }; reroll.Pressed += () => _ = _s.SendCommandAsync(new RerollDice(step.FrameId, roll.Id, [])); row.AddChild(reroll);
                    var set = new Button { Text = $"Set die 1{which}…", Flat = true }; set.Pressed += () => PieceMenu.AskInteger(this, "Raw value for die 1", v => _ = _s.SendCommandAsync(new SetDice(step.FrameId, roll.Id, [new DieValue(0, v)]))); row.AddChild(set);
                }
                var mod = new Button { Text = "+1", Flat = true }; mod.Pressed += () => _ = _s.SendCommandAsync(new AddModifier(step.FrameId, step.Rolls[0].Roll, 1)); row.AddChild(mod);
            }
        }
        CallDeferred(nameof(ScrollToEnd));
    }

    void ScrollToEnd() => _scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue;
}
```

- [ ] **Step 2: Write `PieceMenu.cs`**

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

/// <summary>The right-click menu: every property editable, each item one ordinary command. No admin mode, no confirmation dialog — the log is the accountability.</summary>
public partial class PieceMenu : PopupMenu
{
    readonly Session _s; IReadOnlyList<MenuItem> _items = [];
    public event Action<Action<Guid>>? PickPieceRequested;    // the table resolves the next click to a piece id
    public event Action<Action<Point3>>? PickPointRequested;  // the table resolves the next click to a wire-frame point

    public PieceMenu(Session s) { _s = s; IdPressed += OnPressed; }

    public void Open(IReadOnlyList<MenuItem> items, Vector2 screen)
    {
        Clear(); _items = items;
        for (var i = 0; i < items.Count; i++) AddItem(items[i].Label, i);
        Position = (Vector2I)screen; Popup();
    }

    void OnPressed(long id)
    {
        var item = _items[(int)id];
        switch (item.Input)
        {
            case MenuInput.None: Send(item.Build(null)); break;
            case MenuInput.Integer: AskInteger(this, item.Label, v => Send(item.Build(v.ToString()))); break;
            case MenuInput.Text: AskText(this, item.Label, t => Send(item.Build(t))); break;
            case MenuInput.PieceId: PickPieceRequested?.Invoke(picked => Send(item.Build(picked.ToString()))); break;
            case MenuInput.TablePoint: PickPointRequested?.Invoke(at => Send(item.Build(FormattableString.Invariant($"{at.X},{at.Y},{at.Z}")))); break;
        }
    }

    void Send(Command? command) { if (command is not null) _ = _s.SendCommandAsync(command); }

    public static void AskInteger(Node host, string title, Action<int> then)
    {
        var d = new ConfirmationDialog { Title = title }; var spin = new SpinBox { MinValue = -99, MaxValue = 99 }; d.AddChild(spin);
        d.Confirmed += () => then((int)spin.Value); host.AddChild(d); d.PopupCentered();
    }

    public static void AskText(Node host, string title, Action<string> then)
    {
        var d = new ConfirmationDialog { Title = title }; var line = new LineEdit(); d.AddChild(line);
        d.Confirmed += () => then(line.Text); host.AddChild(d); d.PopupCentered();
    }
}
```

- [ ] **Step 3: Write `Animator.cs` and extend `GameScreen.Play.cs`**

```csharp
using Godot;
using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Table;

/// <summary>The queue between the projection and the renderer. The projection is already current; this only paces what the table plays out.</summary>
public sealed class Animator
{
    readonly Session _s; readonly TableView _view; readonly AnimationQueue _queue = new();
    public Animator(Session s, TableView view) { _s = s; _view = view; s.EventsApplied += events => { _queue.Enqueue(events); Pump(); }; }

    void Pump()
    {
        foreach (var e in _queue.Take(_s.Settings.Animate))
        {
            List<ModelsMoved> moves = e.Payload switch { ModelsMoved m => [m], StepCompleted s => [.. s.Changes.OfType<ModelsMoved>()], _ => [] };
            foreach (var relocation in moves.SelectMany(m => m.Moves))
                if (_s.Settings.Animate && relocation.To is OnTable to && _view.PieceAt(relocation.ModelId) is { } node)
                {
                    var target = WorldFrame.ToGodot(to.Placement.Position);
                    var tween = node.CreateTween(); tween.TweenProperty(node, "position", target, 0.4); tween.TweenCallback(Callable.From(() => { _view.Redraw(); Pump(); }));
                    return;   // one at a time while animating
                }
            if (_s.Settings.Animate && e.Payload is StepCompleted { Rolls.Count: > 0 } step)
            {
                var actor = _s.Projection?.Stack.FirstOrDefault(f => f.Id == step.FrameId)?.Context.ActorUnitId;
                var anchor = actor is { } a ? _view.Pieces.OfType<PieceNode>().FirstOrDefault(p => p.UnitId == a) : null;
                if (anchor is not null)
                {
                    // The whole readout of a roll, which is all the client ever shows of one (owner ruling, 2026-09-06,
                    // question 5): the faces, which dice passed in brackets, the threshold, and every modifier by source.
                    // No die is placed on the table and nothing tumbles; Godot physics is not in the scene.
                    // One block per roll on the step, stacked, because a step may hold several: OPR's roll-to-hit rolls one
                    // pool per Quality value the attacking models bring, since p.4 tests each attack against "the model's
                    // Quality value" and a joined hero brings its own. The threshold on each block is what tells them apart.
                    var text = string.Join("\n", step.Rolls.Select(roll =>
                    {
                        var faces = string.Join(" ", roll.Dice.Select(d => d.Success ? $"[{d.Raw}]" : $"{d.Raw}"));
                        var mods = roll.Modifiers.Count == 0 ? "" : "\n" + string.Join(", ", roll.Modifiers.Select(m => $"{m.Amount:+#;-#;0} {m.Source}"));
                        return $"{roll.Roll}{(roll.Threshold is { } t ? $" {t}+" : "")}\n{faces}{mods}";
                    }));
                    var label = new Label3D { Text = text, Position = new Vector3(0, 3, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 64, PixelSize = 0.01f };
                    anchor.AddChild(label); anchor.GetTree().CreateTimer(1.5).Timeout += label.QueueFree;
                }
            }
        }
        _view.Redraw();
    }
}
```

In `GameScreen.Play.cs`, `ShowPlayPanels(true)` also creates `_logPanel = new LogPanel()` (bound to `_session`, `Table`), `_animator = new Animator(_session, Table)`, an "Animate" `CheckBox` bound to `_session.Settings.Animate` → `UpdateSettings`, and `_menu = new PieceMenu(_session)`; it wires `Input.PieceRightClicked += (id, at) => _menu.Open(ItemsFor(id, shift: Godot.Input.IsKeyPressed(Key.Shift)), at)`, each `TeamPanel.RightClicked += at => _menu.Open(MenuModel.ForTeam(game, p, team.Id), at)`, `Input.TableRightClicked += at => _menu.Open([Token(at), Leave], GetViewport().GetMousePosition())`, and the two deferred inputs: `_menu.PickPieceRequested += Input.PickNextPiece` and `_menu.PickPointRequested += Input.PickNextPoint`. `ItemsFor(id)` is `MenuModel.ForModel(p, id, MenuModel.KnownActionNames(p, _session.Log)).Concat(MenuModel.ForFrame(p))` for a model (`ForUnit` with Shift), `ForMarker(id)` for a marker in `p.Markers`. `Token(at)` is `new MenuItem("Drop a token here", MenuInput.Text, label => new PlaceMarker(new Marker(Guid.CreateVersion7(), MarkerKind.Token, WorldFrame.FromGodot(at), new Yaw(0), 1, label, VisualRef.OfBuiltin("chip"))))`; `Leave` is `new MenuItem("Leave the game", MenuInput.None, _ => null)` whose selection calls `_session.LeaveAsync()` (handled in `GameScreen`, since it is not a command).

- [ ] **Step 4: Run it.** Menu edits log with the actor and appear in the log at once on both clients; "Undo to here" greys the rows above and the table shows the predicted state, then the server's `undone` confirms; a `strict` game refuses nothing from the menu; a token dropped from the table menu appears for both; "Leave the game" removes the seat (the other client's lobby list loses the name) and returns Home; with Animate on a hand move glides, with it off it snaps. `./scripts/check-client` prints nothing. Commit: `Add the log with undo-to-here, the right-click menu, animation and Leave`.

### Task 16: The run script, `CLAUDE.md`, and the whole-tree check

**Files:**
- Create: `scripts/client`
- Modify: `CLAUDE.md` (opening paragraph, Commands, Layout, Conventions)

**Interfaces:**
- Produces: `./scripts/client` — builds the three libs and the client from WSL, then launches `./scripts/godot`; `CLAUDE.md` names the client's structure, the Core rule, the test project, the check script and the run order.

- [ ] **Step 1: Write `scripts/client`** and `chmod +x` it

```bash
#!/usr/bin/env bash
# Run the client: build in WSL first so the referenced libs are current, then launch the Windows Godot binary on client/.
# Godot's import wipes client/.godot/mono/temp/bin/, so let the editor rebuild if scripts fail to load after an import.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet build client/Tabletop.csproj
./scripts/check-client
exec ./scripts/godot "$@"
```

- [ ] **Step 2: Edit `CLAUDE.md`**

In the opening paragraph replace "The client scaffold and the three `libs/` contracts exist;" (the contracts plan's wording; the clauses after it name the servers and the web app and stay) with: "The client shell exists under `client/` — three screens over a session object, the table view, the setup tools, the Ask panel, the log and the menu — over the three `libs/` contracts;".

In **Commands**, add:

```bash
./scripts/client             # build, run scripts/check-client, launch the Godot client on Windows
./scripts/check-client       # no physics body in a scene, no rules type or Godot in client/src/Core
dotnet test tests/Tabletop.Client.Tests   # the client's Godot-free core
```

In **Layout**, replace the `client/` bullet's children with:

```
  - `src/Core/` — Godot-free: `Session`, `ManagementClient`, `ClientGameSocket`, `AssetCache`, `BuiltinContent`,
    `TerrainGenerators`, the view models. Compiled into `tests/Tabletop.Client.Tests` from source, so a `using Godot`
    here breaks the tests. May reference only the three libs and the BCL.
  - `src/Screens/`, `src/Table/`, `src/Panels/` — the Godot layer; thin over `Core` and `Tabletop.Geometry`.
  - `scenes/` — `Main.tscn` is the root that swaps `Login`, `Home`, `Game`, `UpdateRequired`; `TableView.tscn` is the table.
    Scenes are written minimal by hand and owned by the editor afterwards.
  - `content/builtin/` — the five table textures and the ten grouped terrain meshes; a missing file draws the piece's volumes.
```

In the `tests/` bullet, replace "`Tabletop.Management.Tests`, `Tabletop.GameServer.Tests`." with "`Tabletop.Management.Tests`, `Tabletop.GameServer.Tests`, `Tabletop.Client.Tests` (the client's `src/Core/` compiled from source)." (rewrap the bullet as needed).

In **Conventions**, in the "Tests:" bullet replace "one project per library and per server; test-driven for everything with logic. The Godot client is tested by running it." with "one per library, per server and for the client's Godot-free `src/Core/`; test-driven for everything with logic. The client's Godot layer is tested by running it." Then add: "The table frame's origin is the table's corner (`0 ≤ x ≤ width`, `0 ≤ y ≤ length`, Z up) on the wire and in `Tabletop.Geometry` alike; nothing converts. Godot: x = X, y = Z, z = −Y, `rotation.y` = yaw (`WorldFrame`). No Godot physics; picking is ray-versus-AABB."

- [ ] **Step 3: Run the whole check**

```bash
cd /home/cstupi/src/tabletop
docker compose up -d && dotnet build && dotnet test && ./scripts/check-client
find client libs tests -path '*/obj/*' -maxdepth 3 -type d | grep -v -E 'Unix|Windows_NT'
git status --short
```

Then, with `./scripts/management` and `dotnet run --project server/game` running in WSL, `./scripts/client` on two accounts: log in (remember me), create a game, the second joins, teams and armies picked, setup with a generator batch, a saved layout reloaded, end setup; both see the board, presence, a hand move with its ruler, a menu edit in the log, an undo-to-here, Home and back (reconnect rebuilds, and the returning player is back on their own team with the same Ask controls), Leave. Expected: every test project green (Geometry, Content, Protocol, Management, GameServer, Client — 44 in Client), the check script silent, no `obj/` outside `Unix/` and `Windows_NT/`, and `git status` clean apart from the intended files (never `.env`, never `client/.godot/`).

- [ ] **Step 4: Commit**

```bash
git add scripts/client CLAUDE.md
git commit -m "Record the client shell in CLAUDE.md and add its run script"
```

---

## Interfaces → Produces, per namespace

Later plans (the scheduler and interpreter plans' end-to-end runs, a replay viewer, the web app where it shares a DTO) copy these names verbatim. `IRL<T>` abbreviates `IReadOnlyList<T>`.

### `Tabletop.Client.Core` (Godot-free; `client/src/Core/`, compiled into `tests/Tabletop.Client.Tests`)

| Type | Members |
| --- | --- |
| `sealed record ClientSettings(bool Animate, IReadOnlySet<string> MutedWindowKinds, bool RememberMe, string ManagementUrl, string GameServerUrl)` | `static Default`, `static Load(path)`, `Save(path)`, `Mute(kind)`, `Unmute(kind)` |
| `sealed class SessionStore(string path)` | `string? Read()`, `Write(jwt)`, `Clear()` |
| management DTO mirrors | `AccountRow`, `SessionResponse`, `OtpSentResponse`, `Page<T>`, `GameSummary`, `CreateGameRequest`, `PropertiesSummary`, `ArmySummary`, `LayoutSummary`, `AssetKind`, `AssetDto`, `DownloadResponse`, `ManifestEntry`, `Manifest` (Task 3 shapes); `ManagementException(Status, Title, Detail)`, `UpdateRequiredException` |
| `sealed class ManagementClient(HttpClient, Uri baseUrl)` | `string? Jwt`; `SendOtp`, `VerifyOtp`, `LoginWithPassword`, `SignUp`, `Refresh`, `Logout`, `GetAccount`, `ListGames(after, limit)`, `CreateGame`, `GetGame`, `JoinGame`, `LeaveGame`, `ListProperties`, `ListArmies`, `GetArmy`, `ListLayouts`, `GetLayout`, `SaveLayout`, `GetManifest`, `ListAssets(kind, after)`, `GetAssetVolumes`, `GetDownload` |
| `sealed record SocketClosed(int? Code, string? Reason, Exception? Error)`; `interface IGameSocket`; `interface IGameSocketFactory` | `FrameReceived`, `Closed`, `ConnectAsync(Uri, ct)`, `SendAsync(SocketMessage)`, `CloseAsync()`; `Create()` |
| `sealed class ClientGameSocket : IGameSocket`; `ClientGameSocketFactory` | over `ClientWebSocket`, one `SocketMessage` per text frame |
| `sealed class PresenceTable` | `Set`, `Remove`, `Of`, `All`, `Clear` |
| `interface IDispatcher { Post(Action) }`; `ImmediateDispatcher` | |
| `enum SessionPhase { LoggedOut, LoggedIn, Connecting, Connected, Reconnecting, UpdateRequired }` | |
| `sealed class Session(ManagementClient, IGameSocketFactory, SessionStore, ClientSettings, Action<ClientSettings> persist, IDispatcher, Func<int,TimeSpan>? backoff)` | state: `Phase`, `Account`, `Settings`, `Management`, `GameId`, `ParticipantId`, `Game`, `Draft`, `Applier`, `Projection` (prediction while undo pending), `Log`, `Others`, `PendingUndoTo`, `LastCloseReason`, `MyTeamId`, `IsCreator`; events: `PhaseChanged`, `GameChanged`, `BoardChanged`, `EventsApplied(IRL<GameEvent>)`, `PresenceChanged(Guid)`, `Pinged(Ping)`, `RefusedReceived(Refused)`, `ConnectionLost(SocketClosed, bool willReconnect)`; `LoginWithPasswordAsync`, `SignUpAsync`, `SendOtpAsync`, `VerifyOtpAsync`, `TryResumeAsync`, `RefreshAsync`, `LogoutAsync`, `UpdateSettings`, `ConnectAsync(gameId)`, `DisconnectAsync`, `LeaveAsync`, `SendCommandAsync(Command) : Task<Guid>`, `SendEditAsync(Edit) : Task<Guid>`, `UndoToAsync(seq)`, `SendPresenceAsync`, `SendPingAsync` |
| `sealed record BuiltinTerrain(Name, Label, Volume, MeshFile?)`; `static class BuiltinContent` | `Terrain` (35 names, Task 6), `Textures` (5), `MarkerVisuals`, `TerrainMeshGroups` (10), `ZoneStyleIds`, `Place(name, Point3, Yaw, scale)`, `VolumesOf(name)`, `TextureFile(name)`, `TerrainMeshFile(name)` |
| `enum Planet { Generic, Hospitable, Wasteland, Death }`; `static class TerrainGenerators` | `Generate(Planet, TableSize, Random) : IRL<AddTerrain>`, `Fixed(Planet)`, `Rolled(Planet, int twoD6)` |
| `sealed class AssetCache(root, HttpClient)`; `HashMismatchException(Expected, Actual)` | `PathFor(hash)`, `Has(hash)`, `FetchAsync(hash, url, ct)`, `static Sha256Hex` |
| `sealed class Names(GameInfo, Projection?)` | `Participant(Guid?)`, `Team`, `Unit`, `Model`, `UnitOfModel`, `Piece`, `static Short` |
| `static class LogText` | `Describe(GameEvent, Names)`, `Answer(AskAnswer, Names)`, `Touched(GameEvent)` |
| `enum AskMode`; `sealed record AskView(Ask, Mode, Highlights, AnsweredTeams, AutoPass)`; `static class AskViewModel` | `Mine(Projection, GameInfo, Guid? team, muted)`, `WaitingFor(...)`, `AnsweredBy(log, askId, names)`, `ModeOf(LegalSet)`, `SpendableBy(Projection, GameInfo, Guid team, TokenSpend)`, `ConfirmLabel(Ask)` |
| `enum MenuInput { None, Integer, Text, PieceId, TablePoint }`; `sealed record MenuItem(Label, Input, Build)`; `static class MenuModel` | `ForUnit`, `ForModel`, `ForTeam`, `ForMarker`, `ForFrame`, `KnownActionNames` |
| `sealed record RulerReadout(Distance, Remaining, OverAllowance)`; `static class MoveRuler` | `Read(start, current, allowance)`, `UsedInFrame(Frame, modelId)`, `Region(PlacedModel start, MoveRegion, IRL<WorldVolume> volumes, TableSize) : IRL<Point2>`, `ExitRegion(ModelBody body, PlacedModel container, MoveRegion, IRL<WorldVolume> volumes, TableSize) : IRL<Point2>`, `IsDisembark(MoveRegion) : bool` |
| `sealed class AnimationQueue` | `Enqueue`, `Take(animate)`, `Count` |

### Godot layer (`Tabletop`, `Tabletop.Client.Screens`, `.Table`, `.Panels`)

| Type | Members |
| --- | --- |
| `Main : Node` | `static Instance`, `Session`, `Cache`, `Http`, `Show(scenePath)`; routes on `PhaseChanged` |
| `GodotDispatcher : IDispatcher` | `Post` via `CallDeferred` |
| `LoginScreen`, `UpdateRequiredScreen`, `HomeScreen`, `GameScreen : Control` | `GameScreen.Table`, `.Input`, `Toast(text)`; `partial void ShowPlayPanels(bool)` |
| `static class WorldFrame` | `ToGodot(Point3)`, `FromGodot(Vector3)`, `Radians(Yaw)`, `YawFrom(float)` |
| `static class Palette` | `Team(ordinal)`, `ForDeclarations`, `Proxy`, `Selected`, `Ghost`, `Unassigned`, `Translucent(Color)` |
| `TableView : Node3D` | `Bind(Session)`, `Redraw()`, `PieceAt`, `TerrainAt`, `MarkerAt`, `Pieces`, `Volumes`, `Size`, `Camera`, `ShowExitGhosts(IRL<Guid>, Guid container)`, `ClearExitGhosts()`, `IsExitGhost(Guid)` |
| `SlabNode`, `PieceNode`, `TerrainNode`, `MarkerNode`, `ZoneNode`, `RulerNode`, `MoveRegionNode`, `OrbitCamera`, `PresenceOverlay` | Tasks 11, 12, 14 |
| `sealed class MeshLoader(AssetCache, ManagementClient, Guid gameId)` | `RefreshManifest()`, `LoadAssetAsync(assetId, camera, piece)`, `LoadBuiltin(name, fit)` |
| `TableInput : Node` | `Bind(TableView, Session)`, `Tool`, `Selected`, `Proposing`, `Allowance`, `PieceRightClicked`, `TableRightClicked`, `SelectionChanged`, `PieceLeftClicked`, `ModelDragged`, `PickNextPiece`, `PickNextPoint`, `CancelDrag()` |
| `LobbyPanel`, `SetupToolbar(GameScreen)`, `TeamPanel(Team)`, `AskPanel`, `LogPanel`, `PieceMenu(Session)` | Tasks 13–15 |
| `sealed class Animator(Session, TableView)` | Task 15 |

### Files and scripts

`tests/Tabletop.Client.Tests` (xUnit, compiles `client/src/Core/**` from source); `scripts/check-client`; `scripts/client`; `user://settings.json`, `user://session.bin`, `user://cache/{hash}.bin`; `client/content/builtin/textures/{name}.png`, `client/content/builtin/terrain/{name}.glb` (optional).

---

## Spec corrections proposed

**Applied on 2026-09-08.** These were written into the documents they name, across five passes split
by target file so that no two passes wrote the same file. The list below is kept as the historical
record of what each correction asked for and why, so a *Before* quote here no longer matches the
document it quotes. `docs/handoff.md` → *The spec corrections are applied* records the exceptions:
the corrections that were already applied, the ones folded into a later correction, and the ones
skipped as superseded.

1. **`docs/superpowers/specs/2026-09-04-client-shell-design.md` → Screens → Login.** Current: *"On success the client holds the JWT in memory and, if the player chooses, in the OS credential store so the next launch skips the screen."* Add: *"The stored JWT is the five-minute session token; on launch it goes to `POST /v1/auth/session/refresh`, which accepts an expired JWT while the Stytch session lives, and a `401` shows the login screen. The store is not an OS credential store: it is `user://session.bin`, a file created owner-only where the platform has file modes, because the client may run on more than one operating system (owner ruling, 2026-09-06). It keeps the token from other local accounts, not from anything running as this user."* (The refresh interval is not proposed here: the management API spec already gives *"five minutes, refreshed every minute"*.)
2. **`docs/superpowers/specs/2026-09-04-client-shell-design.md` → The connection and the projection.** Add after *"Reconnect is connect."*: *"The client reconnects with backoff after any close it did not ask for, except a `rejected` frame and a `1000` close after `welcome` — the server closes a replaced socket that way, and reconnecting would fight the newer one. The socket's first frame is `connect`, within the server's ten-second handshake window."*
3. ~~**Client shell → Screens → Home**, that `POST /v1/games` takes no army and the pick is a lobby edit.~~ **Struck: already answered.** [The whole-design review](../specs/2026-09-04-whole-design-review.md) → Gaps filled ruled *"`POST /v1/games` takes name, hash, shape, enforcement and the wait setting"*, and [the setup phase](../specs/2026-09-04-setup-phase-design.md) → Editing is free for all lists *"pick an army"* among the lobby commands sent over the socket. Task 10 implements exactly that; no spec needs changing.
4. **`docs/superpowers/specs/2026-09-04-setup-phase-design.md` → Terrain: imported, built-in, generated** (*"a set of meshes with their volumes and declarations"*). Add: *"A built-in ships as its volume and declarations plus a shared mesh: the 35 pieces group into ten meshes under `client/content/builtin/terrain/`, differing by footprint, height, scale and yaw, and a piece that finds no mesh file draws as its volumes — the same degrade path as a missing asset."* (The ten-way grouping and the fifteen-file art set are the owner's ruling of 2026-09-06; the sentence proposed here is what the spec needs so it does not read as one mesh per name.) (The notes-not-flags half is **not** proposed: the same section already says the declarations are *"as the text gives them … with notes for what the six cannot say, such as* dangerous when using Rush or Charge*"*. Task 6 follows that, and reads *"Difficult terrain when moving through it"* as the `Difficult` flag, since that is what the flag means.)
5. **`docs/superpowers/specs/2026-09-04-setup-phase-design.md` → Zones** and **`2026-09-04-client-shell-design.md` → The table view** (*"deployment zones as flat polygons"*). Add: *"During setup every zone row is drawn — in its team's colour when a team with that ordinal exists, grey otherwise — so a group can see and swap zones before every team has formed; from playing on only zones with a team are drawn."* (The game server plan's spec correction 4 says the client draws zones "whose ordinal has a team"; this narrows that to play.)
6. **`docs/superpowers/specs/2026-09-04-client-shell-design.md` → Testing.** Current: *"The projection applier and the geometry library are headless and get the tests. The Godot layer is thin over them and is tested by running the game."* Add: *"Everything Godot-free in the client — the session object, the management client, the socket seam, the cache, the built-in content and generators, the view models — lives under `client/src/Core/` and is compiled from source into `tests/Tabletop.Client.Tests`, which is what proves it needs no engine."*
7. **`docs/superpowers/specs/2026-09-04-client-shell-design.md` → Deliberately not decided.** Strike *"Which socket frames carry which message kind — the protocol contract."* with: *"Settled 2026-09-05 by the contracts plan (`Tabletop.Protocol.SocketMessage`) and the game server plan (the connect sequence)."*
8. **`docs/superpowers/specs/2026-09-04-client-shell-design.md` → The log, undo and the menu** (*"mark activated or clear the action record"*). Add: *"The action names of the pinned `Properties` are not on the wire; the menu offers the names it has seen — the activate Ask's option labels and recorded actions — and free text."* (Or add the names to `GameInfo`; *Interfaces needed* item 5.)
9. **`docs/superpowers/specs/2026-09-04-client-shell-design.md` → Input and presence, the move-button bullet.** Current: *"The move button on the selected unit's action bar starts the declared action. The Ask's answer returns the allowance and the impassable volume ids; the client draws the reachable region and streams the planned position as a `proposed` ghost until the player commits."* Replace the second sentence with: *"The Ask returns the allowance and the impassable volume ids, and — when the move may enter a container or is leaving one — the containers it may enter, or the one it is leaving with the distance its models must be placed fully within of. The client draws the straight-line reachable region from the first, or the container-relative region from the second, and streams the planned position as a `proposed` ghost until the player commits. Clicking an offered container embarks the whole unit at once, which is one answer and not a move."* Reason: the bullet lists exactly what the Ask carries, and containment is now two more fields on it (contracts plan Task 8, applied 2026-09-06).
10. **`docs/superpowers/specs/2026-09-04-client-shell-design.md` → Input and presence, "Moving shows its distance".** Current: *"a ruler draws from each model's start position to its current one with the distance in inches"* … *"In a declared move the readout also shows the remaining allowance, and the reachable region is drawn at that radius."* Add: *"A model inside a container has no start position of its own, so its ruler draws from the container's placement — the point it is disembarking relative to — and shows a distance with no remaining allowance, because the move's allowance constrains nothing about where an occupant may be placed. The region there is the container's base grown by the exit distance, not a radius about a start."* Reason: the section assumes every moving model has a start placement, and an occupant has none (game flow → Inside a container).
11. **`docs/superpowers/specs/2026-09-04-client-shell-design.md` → The table view**, after *"A missing, deleted or still-downloading mesh leaves a translucent proxy and the game is fully playable"*. Add: *"A model that is not on the table is not drawn at all — nothing for a model on the shelf, and nothing for a model inside a container, since neither location carries a placement to draw at. The single exception lasts as long as one Ask: while a unit is disembarking, the client puts one draggable ghost per model on the container's own node so the player has something to place. Those ghosts are not part of the projection and vanish when the answer is sent."* Reason: the section says how a piece is drawn but not that some are not, and the third location was added to the domain after this spec's table-view section was written.

12. **`docs/superpowers/specs/2026-09-04-client-shell-design.md` → The connection and the projection.** Add after the reconnect sentences: *"A `ProjectionDesyncException` from the applier is a desync of this client's copy of the projection, not a network fault. The client drops the projection, reopens the socket and rebuilds from the log, which is the reconnect path with no backoff. It does not skip the change and carry on, because a client that silently diverges from the server is worse than one that visibly resets. One attempt per sequence position: a fault that has made no progress past the last one is an event in the server's own log, and the player goes to the home screen with the reason rather than replaying it forever."* Reason: owner ruling 2 of 2026-09-06; the spec's connection section covers a lost socket and says nothing about a projection that cannot be applied.
13. **`docs/superpowers/specs/2026-09-04-client-shell-design.md` → Animation**, which names *"dice rolling on the table and models falling"*. Narrow the dice half: *"A roll is a readout, not an object. Text beside the unit gives the faces rolled, the threshold, the modifiers applied and which dice passed; no die is placed on the table and nothing tumbles. Models falling stays as written, by tween."* Reason: owner ruling of 2026-09-06 (question 5). Godot physics is out of the scene (`architecture.md` → The load-bearing seam), so any die on the table would be a canned animation pretending to be a roll whose result the server has already decided; the readout says more, because it shows the threshold and the modifiers a tumbling die cannot. The spec would otherwise assert something the client will not do.

## Interfaces needed from other modules

1. **The table frame — settled.** The compatibility review of 2026-09-05 ruled one frame, the corner (`0 ≤ x ≤ width`, `0 ≤ y ≤ length`, Z up), on the wire and in `Tabletop.Geometry`; the geometry plan's `Table`, `ZoneStyles` and `Movement` tests were changed to it and this plan's `TableFrame` conversion layer was removed. `Table.ContainsPoint` is the client's on-slab check and `ZoneStyles.Resolve` draws without conversion. The owner is asked to confirm the ruling (geometry plan question 1).
2. **Contracts plan, `MoveRegion` and `MoveContainment` — settled.** `MoveRegion(IRL<Guid> ModelIds, double Allowance, IRL<Guid> ImpassableVolumeIds, IRL<Guid> DifficultVolumeIds, double? DifficultCap, MoveContainment? Containment = null)` with `MoveContainment(IRL<Guid> Enterable, Guid? ExitingId, double? ExitWithin)` (contracts plan Task 8; the sixth member added 2026-09-06, optional so every five-argument construction still compiles). `MoveRuler.Region` resolves both id lists against the board's volumes and passes the cap through to `Movement.Region`; `MoveRuler.ExitRegion` reads `Containment.ExitWithin` and draws the container-relative region instead when `ExitingId` is set; `AskPanel.OfferEmbark` turns `Enterable` into click targets that answer `Destinations` with `Inside(container)` for every model of the unit. Consumed exactly as the contracts plan states: `Enterable` and `ExitingId` are never both set (that is where *"never both in one activation"* lives), `Allowance` binds nothing on a disembark, and a null `Containment` means containment plays no part in this move.
3. **Management plan, `ManifestEntry`.** Provided (compatibility review, 2026-09-05): `ManifestEntry(Guid AssetId, AssetKind Kind, string Hash, Uri Url, double Scale, Orientation Orientation)`, read from the owner's `asset` row by management plan Task 12. The client needs the import corrections to draw another participant's mesh, which `GET /v1/assets/{id}` (owner-only) cannot supply; `MeshLoader.Orient` applies them (Task 11).
4. **Management plan, the client's endpoints.** Its *Client-shell plan* obligations now list everything this plan calls: `/v1/auth/*`, `/v1/account`, `/v1/games*` and the manifest, `/v1/properties`, `/v1/armies` (list and get), `/v1/layouts` (list, get, save), plus `GET /v1/assets?kind=terrain|texture` (the setup toolbar's "Add my terrain", own textures), `GET /v1/assets/{id}/volumes` (a library terrain's volumes on add and on "Reset volumes") and `GET /v1/assets/{id}/download` (an own asset not yet in the manifest) — all owner-only. The DTO mirrors in `ManagementDtos.cs` must stay byte-compatible with `Tabletop.Management`'s records under `ContentJson`; a shared DTO assembly is not proposed.
5. **Contracts plan, action names.** The `action` rows (name, distance) never reach the client, so the menu's "mark as having taken …" derives names from `AskOption.Label` on the activate Ask and from `ActionRecorded`. Assumed the activate Ask's option `Label` is the action name (the contracts plan's `AskTests` sample: `AskOption($"{unit}:Advance", "Advance", [unit])`). Proposed for the scheduler plan to honour that shape, or for `GameInfo` to gain `IRL<string> Actions`.
6. **Contracts plan, `AskQuestions.EndRound`.** Needed by the 2026-09-06 ruling that a player ends the round (scheduler plan, question 4): one more engine-owned question name, `EndRound = "end_round"`, beside `Activate`, `ActOrEndPhase`, `Deploy`, `DeployScouts`, `DeployAmbusher`, `Confirm`, `React`, `SpendTokens`. The client reads it in exactly two places — `AskViewModel.ConfirmLabel` (the button says "End the round", not "Continue") and nothing else, since `LogText`'s new line keys on the `Confirmed` answer rather than the question. Nothing else in the Protocol changes: the Ask is the existing `Ask` with a `Confirmation` legal set, `AllowPass` false, `Sealed` false, `FrameId` null, addressed to every team that has a participant.
7. **Game server plan, consumed as stated:** `Connect` first within ten seconds; `Welcome`, then `BoardMessage` from `setup` on, then `EventsMessage`s (an empty one when the log is empty), then live frames; `Rejected` then close; `1000` on a replaced socket; `Refused.MessageId` echoing `CommandId`/`EditId`; `PresenceEnded`; lobby edits refused with `WrongStatus` from `playing` on; `BoardMessage` with the whole draft after every setup edit; the default draft 72×48 with texture `grass` (which `BuiltinContent.Textures` therefore ships).
8. **Management plan, consumed as stated:** `Tabletop-Client-Version` on every request, `426` as `application/problem+json`; `POST /v1/auth/session/refresh` accepting an expired JWT; `Page<T>(Items, Next)` with `?limit&after`; another owner's row is `404`.
9. **Geometry plan, consumed as stated:** `ZoneStyles.ShapesOf(style)` as `SetZoneStyle.Shapes`; `ZoneStyles.Resolve(shape, size)` for drawing; `Distance.ClosestPoints` for measurements; `Movement.Region(...).Boundary`; `Movement.Check(...)`'s `CrossesImpassable` and `LeavesTable`, asked of a single placement by passing that placement as both start and destination; `Containment.BaseFullyWithin(model, container, inches)` — edge to edge from the container's base, not a circle about its centre (geometry plan Task 6), which `MoveRuler.ExitRegion` marches into a boundary; `Settling.SurfaceHeightAt(Point3, volumes)` for a dragged model; yaw 0 faces +Y, counter-clockwise positive; an oval's length along the facing axis.
10. **Sample data, not a contract:** the contracts plan's `TableStateTests.Sample()` names `hill-small`, `objective-ring`, `grass` and the zone shapes `frontline/1`, `frontline/2` (which `ZoneStyles` resolves); the same plan's `Samples` adds `chip` (`Samples.Token`) and `ruin` (its `SwapTerrainMesh` sample); the game server plan's `DraftEditor` tests add the texture `desert` and its default draft the texture `grass`. `BuiltinContent` ships all six so every plan's samples render, and an unknown zone shape (the game server plan's `DraftEditor` tests use `long-edges/*` and `corners/*`, plain strings that name no `ZoneStyles` shape) draws nothing with a warning. `Samples.Game` and `Samples.Board` are expression-bodied properties that mint new Guids per read, so a test that needs one game reads it into a `static readonly` first (Task 5).
11. **Interpreter plan, consumed as stated:** the move step's Ask is question `move` with a `move_region` whose `Containment` is non-null exactly when the move may embark or is disembarking (Task 14); `transport-destroyed`'s `place` step opens a second question, `disembark`, with a `move_region` whose `Containment.ExitingId` is the wreck and whose `Allowance` is the exit distance (Task 19); `GfNames.ExitWithin` is 6" in Grimdark Future and reaches the client only inside `MoveContainment.ExitWithin`, never as a constant here; a `Destinations` answer is applied as sent, with the server dropping an `Inside` whose container is off the table and leaving inside any model placed beyond the exit radius. No ruleset question name is added to `AskQuestions`.

12. **Contracts plan, the two names ruled on 2026-09-06 — settled.** `ProjectionDesyncException`, thrown by `EventApplier` in place of `KeyNotFoundException` and carrying the event kind and the unknown id, is caught in `Session` at the two places that apply events (Task 5). The client reads only the type and `Message`; it does not branch on the kind or the id, and the exception's own members are the contracts plan's to shape. `AtRest(long Seq)`, wire kind `at_rest`, server to client, is a `SocketMessage` this client deserialises and drops: `Handle`'s switch has no default arm. When it is sent, whether it is per connection or broadcast, and what it means when nothing was appended are the game server's to decide, and no client behaviour depends on any of those answers.

13. **Contracts plan, the two shapes the 2026-09-06 rulings need — settled.** A team's spell token spend arrives as `TokenSpend(IReadOnlyDictionary<Guid,int> MaxByModel)`, wire kind `token_spend`, `Sealed`, `AllowPass`, `WindowKind` `casting`, question `spend_tokens`, answered by `TokensSpent(IReadOnlyDictionary<Guid,int> Counts)`, wire kind `tokens_spent` (p.13). Seven legal-set kinds and eight answer kinds, unchanged in number: the contracts plan considered and rejected reusing `Allocation`, because an allocation's counts sum exactly to its amount, which is right for wounds and wrong for *"any number"* up to a maximum, and because one total with no per-target bound cannot distinguish five tokens across two models holding one and four from five across two holding three and two. The client consumes it as stated. The cap is the map's value, the team's maximum is the sum of its own entries, and the map covers every addressed team's models because p.13's window addresses both — the caster's side spends for +1 and the other side for -1. That discloses nothing, since every projection already carries each model's `SpellTokens`; `AskViewModel.SpendableBy` narrows the map to my own team's models for both the spin boxes and the highlights. `AskMode.TokenSpend` therefore stays, and it keeps its own control rather than borrowing `AskMode.Allocate`'s: the allocation widget is one total split exactly across its targets, and this is per-model bounds with no total at all. A Takedown's chosen model arrives as `PieceSelection` over the target unit's model ids with `Min` and `Max` both 1 — no wire change at all, no new type, and no new question name read here, since the panel keys on the legal set and `AskViewModel.ConfirmLabel` is still the only place a question name is read.

## Questions for the project owner

Six of the ten were ruled on 2026-09-06 and are closed in place below: questions 2 and 3 changed
the design, and questions 4, 5, 6 and 7 confirmed the assumption as written. Questions 1, 8, 9
and 10 remain open.

1. **Presence sample rate.** Assumed 20 Hz for the client's own samples (`TableInput`'s timer), matching the game server plan's relay rate and `architecture.md`'s figure — which the whole-design review named a first-pass assertion, and which presence deliberately left open. A different figure changes one `Timer.WaitTime`. **This is the same question as the game server plan's question 4; one answer settles both.**
2. **Where the remembered session lives — ruled 2026-09-06, no longer open.** DPAPI was the assumed answer; the owner ruled *"We may run on other operating systems"*, which rules out a Windows-only mechanism. The store is `user://session.bin`, a plain file created owner-only (`UnixFileMode.UserRead | UserWrite`) where the platform has file modes, and covered by the user profile's ACL on Windows. It protects the token from other local accounts on a shared machine and from a group- or world-readable directory being backed up; it does not protect it from anything running as the same user, nor from the file being copied to another machine, where it refreshes until the Stytch session expires — the one property DPAPI had and this does not. A per-platform store behind one interface was rejected in Task 2 for cost and for libsecret's keyring dependency. If an OS keystore is required later, `SessionStore` is three methods with one call site.
3. **Built-in art — ruled 2026-09-06, no longer open.** The assumption was that no art ships. The owner ruled that a minimal set does, so a generated table looks like a table. **The files are art, and this plan cannot produce them**: ten `.glb` group meshes and five `.png` textures under `client/content/builtin/`, listed with their format and budget in Task 6 Step 4, which is a step for a person with a modelling tool and not a step code performs. What the plan does produce is the grouping (each of the 35 built-ins names one of the ten meshes through `BuiltinTerrain.MeshFile`) and the fit-to-volume scaling in `MeshLoader.LoadBuiltin` (Task 11) that lets one mesh serve a group. Hitbox-first rendering stays as the degraded path for every file that is missing, unchanged.
4. **Generator entries with alternatives** (*"Hill, Forest, or Field"*, *"2 Large Forests / Large Fields"*) **— ruled 2026-09-06, the assumption confirmed, no longer open.** The client's RNG picks one alternative: rolling a planet table produces a finished board in one action, and a player who dislikes a piece swaps or deletes it during setup, which the toolbar already offers (`SwapTerrainMesh`, `RemoveTerrain`). No mechanism changes — `TerrainGenerators.Rolled` keeps returning the alternatives an entry names and `Generate` keeps choosing from them. Had the player been asked instead, `Generate` would have returned the alternatives unresolved and the toolbar would have prompted per entry.
5. **Dice on the table — ruled 2026-09-06, the assumption confirmed, no longer open.** A roll is a readout, not dice objects: text beside the unit gives the faces rolled, the threshold, the modifiers applied and which dice passed, and nothing tumbles. Models still fall by tween. No mechanism changes; `Animator` is unaffected by that ruling, though the joined-hero ruling of the same day later made it read out every roll on the step rather than the first. The client shell spec's Animation section does name *"dice rolling on the table"*, so **spec correction 13** narrows that sentence rather than leaving the spec asserting something the client will not do. Had visible dice been wanted, `Animator` would gain a dice scene with a canned tumble and the queue would pace it the same way, and `Animator` would be the only file that changes.
6. **The game list's filters — ruled 2026-09-06, the assumption confirmed, no longer open.** Status, name and "created by me", all three read off the `GameSummary` rows `GET /v1/games` already returns, so the filtering is client-side over a list the browser has. **"Games I am in" is not built, and the reason is worth stating: `GameSummary` carries no participants**, so the filter would need either a new field on it or a `GET /v1/games/{id}` per row to look the participants up. The first is a one-column change in the management plan and the second is a request per row; neither is taken.
7. **Zones for ordinals with no team — ruled 2026-09-06, the assumption confirmed, no longer open.** Drawn grey during setup (spec correction 5). A four-zone style in a two-player lobby therefore shows two coloured zones and two grey, so the table shows what the style laid out rather than hiding half of it until teams form. No mechanism changes. Hiding them would have `TableView.ZoneColour` return transparent for those ordinals.
8. **The facing a disembarking model takes.** Nothing in the specs or in rulebook p.14 says, and something must: an oval base's footprint depends on its yaw, so the exit region cannot be drawn without one. Assumed **the container's yaw**, for the ghost, for `MoveRuler.ExitRegion`'s candidate placement, and for the `Destinations` answer (`AskPanel.YawOf`) — a unit pouring out of a transport faces the way the transport faces. If a model should instead keep the facing it had when it embarked, `Location.Inside` would have to carry a yaw, which is a wire change in the contracts plan, and `YawOf` would read it; if the player should turn each model, the drag needs a rotate control, which no spec asks for.
9. **Whose base the disembark region is drawn for.** Assumed the **lead** model's, exactly as `MoveRuler.Region` already is: one polygon, from `ModelIds[0]`. A unit with mixed base sizes therefore sees the lead's region, and the server refuses an outlier individually (`GfMoves.ExitPlacement` leaves it inside). If every distinct base should get its own boundary, `MoveRegionNode.Show` takes a list of polygons and nothing else changes.
10. **Whether an offered container is a click target or a button.** Assumed a **click** on the container, which is already highlighted because `AskViewModel.Mine` puts `Enterable` in `Highlights` — *"the legal set from the server becomes the panel's choices and the table's highlights"* — with the container's name in the panel's note so the affordance is discoverable. A button per container in the panel would be the alternative; it was not taken because it is a second control for the same command, which this plan rejected for the round boundary on the same grounds. If buttons are wanted, `OfferEmbark` adds one `Btn` per container and drops the `PieceLeftClicked` hook, and `TableInput.CancelDrag` is then unused.
