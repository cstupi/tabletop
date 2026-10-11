# Task 6: Deployment zones in play

**Wave:** 2 · **Depends on:** — · **Model:** opus

**Files:**
- Modify: `client/src/Table/TableView.cs` (which zones are drawn: setup every ordinal; play the teams' zones through
  round 0, none from round 1; `ShowGhosts` for the deployment placement)
- Modify: `client/src/Table/ZoneNode.cs` (only if finding F5 holds)
- Modify: `server/game/Session/GameSession.Lobby.cs` (`NewDraft` starts with the core deployment), when finding F1 holds
- Test: `tests/Tabletop.GameServer.Tests/ZonesInPlayTests.cs` (new), `tests/Tabletop.Client.Tests/ZonesInPlayTests.cs` (new)

**Interfaces:**
- Consumes: `Projection.Board.Table.Zones`, `Projection.Scheduler.Round`, `GameInfo.Teams`, `ZoneStyles.ShapesOf/Resolve`.
- Produces: `TableView.ShowGhosts(IReadOnlyList<ModelRelocation> placements)` for Task 16a: one ghost per relocation,
  in the ghost child, cleared by `ClearExitGhosts`. `TableView.Redraw` keeps its signature.

Spec: *Table interaction → Deployment zones*. Each team's zone is drawn in its colour through round 0 (deployment,
then Scouts) and removed when round 1 starts. Today no zone shows during deployment. This task finds why first
(superpowers:systematic-debugging): no fix before the finding that explains it.

## What the code says at `b70c068`

| Path | Reading |
| --- | --- |
| `GameSession.NewDraft()` | `new TableState(…, [], [], [])`: a new draft has **no zones**. Only the setup toolbar's *Set zones* (`SetZoneStyle`) or a loaded layout (`ReplaceDraft`) adds them, and nothing requires either before *End setup*. |
| `Rules/GrimdarkFuture/` | Names no zone: deployment is legal anywhere, so a game deploys without complaint on a board with none. |
| `DraftEditor.DropOffSlab` | Filters `Terrain` only; `Zones` pass through to the board in play. |
| `PostgresGameRows.EndSetupAsync` → `WriteTreeAsync` | Deletes and rewrites `deployment_zone` with the same ids. `ReadTableAsync` reads them back (`Hydrator`). |
| `Session.Handle(BoardMessage)` at playing | `Applier = new EventApplier(b.Board, Game)`: the board, zones included, becomes `Projection.Board`. |
| `TableView.Redraw` | `Table => Projection?.Board.Table ?? Draft`, syncs `ZoneNode`s from `table.Zones` in setup and play alike. |
| `ZoneNode.Set` | Fan at Z = 0.02". The slab's top face is Z = 0, its texture plane Z = 0.002. `Palette.Translucent` sets `CullMode = Disabled`, so winding cannot hide a face (and `ZoneStyles` polygons are not all wound one way: `Tri`s vary, and a table longer in Y swaps u and v). |

The likeliest cause is therefore H1: the game never had zones. The hypotheses below run in that order; each has a
diagnostic, and each finding has its fix.

| # | Hypothesis | Diagnostic | Finding → fix |
| --- | --- | --- | --- |
| H1 | The board has no zones: setup ended without *Set zones* | Step 3 SQL; Step 1 test `A_new_draft_starts_with_the_core_rules_deployment` | F1: `NewDraft` starts with the core deployment (Step 4) |
| H2 | The server drops zones between setup and play, or on hydration | Step 1 test `Zones_set_in_setup_reach_the_board_in_play_and_a_fresh_process` | F2: none expected; the test pins the reading above |
| H3 | The client's `Session` loses them from `Projection.Board` | Step 1 test `The_board_in_play_keeps_the_zones` | F3: none expected; the test pins it |
| H4 | A zone's shape id does not resolve | Godot output `Unknown zone shape '<id>'; nothing drawn.` (Step 6) | F4: data, not drawing (Step 6) |
| H5 | `ZoneNode`s exist with meshes but nothing shows | Step 6 print: zone nodes with mesh children while none is visible | F5: lift, unshaded, render priority (Step 6) |

- [ ] **Step 1: Write the diagnostic tests**

Create `tests/Tabletop.GameServer.Tests/ZonesInPlayTests.cs`:

```csharp
using Tabletop.Content;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

[Collection("Postgres")]
public class ZonesInPlayTests(PostgresFixture pg)
{
    static EditMessage Edit(Edit e) => new(Guid.CreateVersion7(), e);
    static IEnumerable<(Guid, string, int)> Keys(IReadOnlyList<DeploymentZone> zones) => zones.Select(z => (z.Id, z.Shape, z.TeamOrdinal));

    // H2: the zones set in setup are the zones of the board in play, and a fresh process reads the same ones back.
    [Fact]
    public async Task Zones_set_in_setup_reach_the_board_in_play_and_a_fresh_process()
    {
        var seeded = await pg.SeedGameAsync(GameStatus.Lobby, teams: 2);
        IReadOnlyList<DeploymentZone> set;
        using (var first = GameServerFactory.WithoutScheduler())
        {
            await using var c = await TestClient.OpenAsync(first);
            await c.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
            await c.SendAsync(Edit(new StartSetup())); await c.ExpectAsync<BoardMessage>();
            await c.SendAsync(Edit(new SetZoneStyle(ZoneStyles.Spearhead, ZoneStyles.ShapesOf(ZoneStyles.Spearhead))));
            set = (await c.ExpectAsync<BoardMessage>()).Board.Table.Zones;
            Assert.Equal(2, set.Count);
            await c.SendAsync(Edit(new EndSetup()));
            Assert.Equal(Keys(set), Keys((await c.ExpectAsync<BoardMessage>()).Board.Table.Zones));
        }
        using var second = GameServerFactory.WithoutScheduler();
        await using var again = await TestClient.OpenAsync(second);
        await again.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        Assert.Equal(Keys(set), Keys((await again.ExpectAsync<BoardMessage>()).Board.Table.Zones));
    }

    // H1 and F1: a game whose setup never touched the zones still deploys into the core rules' zones (p.6: "fully within
    // 12" of their table edge", one long edge each), which is ZoneStyles' Frontline.
    [Fact]
    public async Task A_new_draft_starts_with_the_core_rules_deployment()
    {
        var seeded = await pg.SeedGameAsync(GameStatus.Lobby, teams: 2);
        using var f = GameServerFactory.WithoutScheduler();
        await using var c = await TestClient.OpenAsync(f);
        await c.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await c.SendAsync(Edit(new StartSetup()));
        var zones = (await c.ExpectAsync<BoardMessage>()).Board.Table.Zones;
        Assert.Equal(ZoneStyles.ShapesOf(ZoneStyles.Frontline).Select((shape, i) => (shape, i + 1)), zones.Select(z => (z.Shape, z.TeamOrdinal)));
        Assert.Equal(2, await pg.CountAsync("select count(*) from management.deployment_zone z join management.table_state t on t.id = z.table_state_id where t.game_id = $1", seeded.GameId));
    }
}
```

Create `tests/Tabletop.Client.Tests/ZonesInPlayTests.cs`:

```csharp
using System.Net;
using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

/// <summary>H3: the client's projection carries the zones of the board it was sent at playing.</summary>
public class ZonesInPlayTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("tabletop-zones-tests").FullName;
    readonly FakeManagement _http = new();
    readonly FakeSocketFactory _sockets = new();
    readonly Session _s;

    public ZonesInPlayTests() => _s = new Session(new ManagementClient(new HttpClient(_http), new Uri("http://localhost:5100")), _sockets,
        new SessionStore(Path.Combine(_dir, "session.bin")), ClientSettings.Default, _ => { }, new ImmediateDispatcher(), _ => TimeSpan.Zero);
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public async Task The_board_in_play_keeps_the_zones()
    {
        var game = Samples.Game; var board = Samples.Board;            // each read mints fresh ids: read once
        _http.Answer(HttpStatusCode.OK, new SessionResponse("a.jwt.z", new AccountRow(game.Participants[0].AccountId, "Clay")));
        await _s.LoginWithPasswordAsync("c@example.com", "pw");
        await _s.ConnectAsync(game.Id);
        _sockets.Last.Deliver(new Welcome(Samples.P1, game));
        _sockets.Last.Deliver(new BoardMessage(board));

        Assert.Null(_s.Draft);
        Assert.Equal(2, board.Table.Zones.Count);
        Assert.Equal(board.Table.Zones.Select(z => (z.Id, z.Shape, z.TeamOrdinal)), _s.Projection!.Board.Table.Zones.Select(z => (z.Id, z.Shape, z.TeamOrdinal)));
    }
}
```

`Samples.Game` is at `Playing`, and `Samples.Board`'s table is `TableStateTests.Sample()`, which has `frontline/1` and
`frontline/2`.

- [ ] **Step 2: Run them**

Run: `docker compose up -d && dotnet test tests/Tabletop.GameServer.Tests --filter ZonesInPlayTests && dotnet test tests/Tabletop.Client.Tests --filter ZonesInPlayTests`
Expected from the reading above:
- `Zones_set_in_setup_reach_the_board_in_play_and_a_fresh_process` passes: H2 is ruled out.
- `The_board_in_play_keeps_the_zones` passes: H3 is ruled out.
- `A_new_draft_starts_with_the_core_rules_deployment` fails: `Assert.Equal() Failure: Collections differ`, expected
  `[("frontline/1", 1), ("frontline/2", 2)]`, actual `[]`.

If the H2 test fails, its failing assertion names the path (the end-setup board or the hydrated one): stop, report
the assertion, and fix that path in `server/game/` before going on. If the H3 test fails, the fault is in
`Session.Handle(BoardMessage)`: stop and report it, since `Session.cs` is Task 12's.

- [ ] **Step 3: Confirm H1 on the owner's game**

Run against the dev database:

```bash
docker compose exec -T postgres psql -U tabletop -d tabletop -c "
  select g.id, g.name, g.status, count(z.id) as zones, string_agg(z.shape, ', ' order by z.team_ordinal) as shapes
  from management.game g join management.table_state t on t.game_id = g.id
  left join management.deployment_zone z on z.table_state_id = t.id
  where g.status in ('playing', 'ended') group by g.id order by g.id desc limit 10;"
```

Expected: the game the owner played shows `zones = 0`. That confirms H1 and Step 4 applies. A row with
`zones = 2` for a game in which none showed rules H1 out for that game: skip Step 4 and go to Step 6.

- [ ] **Step 4: Fix F1 — a new draft starts with the core deployment**

p.6, *Deploying Armies*: "The winning player first chooses one long table edge to deploy on and then places one unit
fully within 12” of their table edge. … the opposing player places one unit fully within 12” of the opposite table
edge." That is `ZoneStyles.Frontline` ("also the core rules' deployment, p.6"). Setup's *Set zones*, *Swap* and a
loaded layout still replace it.

Replace `NewDraft` in `server/game/Session/GameSession.Lobby.cs`:

```csharp
    /// <summary>The default draft the creator's StartSetup creates. Size and texture come from configuration (ruling 2); the zones are the
    /// core rules' deployment (p.6: one long table edge each, fully within 12"), ZoneStyles' Frontline, until setup sets another style.</summary>
    internal TableState NewDraft() => new(Guid.CreateVersion7(), null, new TableSize(_services.Options.DefaultTableWidth, _services.Options.DefaultTableLength),
        VisualRef.OfBuiltin(_services.Options.DefaultTextureBuiltin), [], [],
        ZoneStyles.ShapesOf(ZoneStyles.Frontline).Select((shape, i) => new DeploymentZone(Guid.CreateVersion7(), shape, i + 1)).ToList());
```

The ordinals follow `DraftEditor`'s `SetZoneStyle` (`i + 1`). `Tabletop.Geometry` and `Tabletop.Content` are already
imported. Games already past setup keep their empty zones; nothing migrates them.

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter "ZonesInPlayTests|SetupEditTests|EndSetupTests"`
Expected: PASS. `SetupEditTests` replaces the default zones with `SetZoneStyle` and `ReplaceDraft`, so its counts hold.

- [ ] **Step 6: H4 and H5 — only if a game with zones on its board still draws none**

Skip this step when Step 3 showed `zones = 0` for the game in question and Step 5 passed; go to Step 7.

Add a temporary line at the end of `TableView.Redraw` in `client/src/Table/TableView.cs`:

```csharp
        GD.Print($"zones: board {table.Zones.Count} [{string.Join(", ", table.Zones.Select(z => z.Shape))}], nodes {_zones.GetChildCount()}, meshes {_zones.GetChildren().Sum(z => z.GetChildCount())}");
```

Run `./scripts/client`, open the game in deployment, and read the Godot output:

| Output | Finding |
| --- | --- |
| `board 0` | H1 for this game: back to Step 3. |
| `Unknown zone shape '<id>'; nothing drawn.` next to `board N` | H4: the row names an id `ZoneStyles.Resolve` does not know, from a layout or a client other than this one. F4 is data, not drawing: `ZoneNode` already warns and draws nothing, which is its designed behaviour (`docs/design/client/design.md`, *An unknown zone shape id*). Report the id; in a game still in setup, *Set zones* replaces it. |
| `board N`, `nodes N`, `meshes ≥ N`, nothing visible | H5: apply F5 below. |

F5, in `client/src/Table/ZoneNode.cs`: lift the fan clear of the slab's depth precision, draw it unlit so the light
angle cannot darken it to the slab's colour, and sort it after the slab's texture plane. Replace the body of the
`foreach (var polygon in polygons)` loop:

```csharp
        foreach (var polygon in polygons)
        {
            var verts = polygon.Vertices.Select(p => WorldFrame.ToGodot(new Point3(p.X, p.Y, Lift))).ToArray();   // ZoneStyles answers in the wire frame
            var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
            for (var i = 1; i + 1 < verts.Length; i++) { st.AddVertex(verts[0]); st.AddVertex(verts[i + 1]); st.AddVertex(verts[i]); }   // fan: zone polygons are convex
            var material = Palette.Translucent(colour);
            material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            material.RenderPriority = 1;
            AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
```

and add to the class, above `Set`:

```csharp
    /// <summary>Height of the zone over the slab's top face (Z = 0), clear of the texture plane at 0.002".</summary>
    const double Lift = 0.05;
```

Update the class summary's "just above the slab" to "0.05\" above the slab, unlit". Run `./scripts/client` again: the
print shows the same counts and the zones now show. Then delete the temporary `GD.Print` line.

- [ ] **Step 7: Zones through round 0, none from round 1**

`Projection.Scheduler.Round` is 0 from the log's first `RoundStarted(0)` through deployment and Scouts
(`SchedulerBase.BeginRound`: round 0 when the rules have phases before the rounds), and 1 from round 1's
`RoundStarted`. An undo preview (`Session.Projection` is the `ProjectTo` prefix) back into round 0 shows the zones
again, which is right.

Setup keeps drawing every ordinal, grey without a team (`docs/design/client/design.md`, setup → Zones). In play only
zones whose ordinal has a team are drawn.

In `client/src/Table/TableView.cs`, `Redraw`, replace the zone line:

```csharp
        Sync(_zones, table.Zones, z => z.Id, z => { var n = new ZoneNode(); n.Set(z, Size, ZoneColour(z, teams)); return n; }, (ZoneNode n, DeploymentZone z) => n.Set(z, Size, ZoneColour(z, teams)));
```

with

```csharp
        Sync(_zones, ZonesShown(table, _session.Projection, teams), z => z.Id, z => { var n = new ZoneNode(); n.Set(z, Size, ZoneColour(z, teams)); return n; }, (ZoneNode n, DeploymentZone z) => n.Set(z, Size, ZoneColour(z, teams)));
```

and add beside `ZoneColour`:

```csharp
    /// <summary>
    /// Setup (no projection): every ordinal, grey without a team. Play: each team's zone through round 0 — deployment, then the
    /// phases before the first round — and none once round 1 has started.
    /// </summary>
    static IReadOnlyList<DeploymentZone> ZonesShown(TableState table, Projection? p, IReadOnlyList<Team> teams) =>
        p is null ? table.Zones
        : p.Scheduler.Round >= 1 ? []
        : table.Zones.Where(z => teams.Any(t => t.Ordinal == z.TeamOrdinal)).ToList();
```

`TableState`, `DeploymentZone` (`Tabletop.Content`), `Projection` and `Team` (`Tabletop.Protocol`) are already imported.
`Sync` frees the zone nodes no longer wanted, so the zones go on the `EventsApplied` that carries round 1's
`RoundStarted`.

- [ ] **Step 8: Ghosts for the deployment placement**

Spec → *Deployment placement*: "a click on the table drops its shelved models there as ghosts in a row". A shelved
model has no piece node, and `TableView`'s only ghosts are the disembark's, stacked on a container. Task 16a calls this
with `DeployView.Layout`'s relocations. In `client/src/Table/TableView.cs`, after `ShowExitGhosts`:

```csharp
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
```

Godot code with no logic of its own; Task 19's launch list exercises it.

- [ ] **Step 9: Build, check and look**

Run: `dotnet build client/Tabletop.csproj && ./scripts/check-client`
Expected: build succeeds; `check-client` prints nothing.

Run `./scripts/client` with the stack up, two participants in two teams, and check:
1. A new game's setup shows the two Frontline zones without pressing *Set zones*, in the teams' colours (grey for an
   ordinal with no team).
2. After *End setup*, through deployment (and Scouts, if a unit has it) each team's zone shows in its colour.
3. When round 1 starts the zones disappear.
4. Undo to an event in deployment: the zones return; redo past round 1's start: they go.

- [ ] **Step 10: Commit**

Add `server/game/Session/GameSession.Lobby.cs` only if Step 4 ran, `client/src/Table/ZoneNode.cs` only if F5 was
applied:

```bash
git add client/src/Table/TableView.cs tests/Tabletop.GameServer.Tests/ZonesInPlayTests.cs tests/Tabletop.Client.Tests/ZonesInPlayTests.cs
git add server/game/Session/GameSession.Lobby.cs   # F1 only
git add client/src/Table/ZoneNode.cs                # F5 only
git commit -m "Draw each team's deployment zone through round 0, ghosts for the deployment placement; a new draft starts with the core deployment" -m "Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

Name the finding in the commit body when it was not H1 (`-m "Cause: H5, …"`).
