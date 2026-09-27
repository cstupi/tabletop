# Task 11: Layouts

**Files:**
- Create: `server/management/Content/Layouts/LayoutRepository.cs`, `server/management/Content/Layouts/LayoutEndpoints.cs`
- Modify: `server/management/Program.cs`
- Create: `tests/Tabletop.Management.Tests/LayoutTests.cs`

**Interfaces:**
- Consumes: `Tabletop.Content.TableState`, `TerrainPlacement`, `TerrainVolume`, `Marker`, `MarkerKind`, `DeploymentZone`, `VisualRef`; `Tabletop.Geometry.TableSize`, `Point3`, `Yaw`; `VolumeRow`.
- Produces: `LayoutSummary(Guid Id, string Name, TableSize Size, int Terrain, int Markers)`; `PatchLayoutRequest(string? Name, TableState? State)`; `LayoutRepository(Database)` with `Save(ownerId, TableState) : TableState` (every id regenerated — the rows are copies, never the game's), `Get(ownerId, id) : TableState?`, `List(ownerId) : IReadOnlyList<LayoutSummary>`, `Patch(ownerId, id, PatchLayoutRequest) : TableState?`, `Delete(ownerId, id) : bool`, and `static TableState ReadTree(NpgsqlConnection, Guid tableStateId, string? name, TableSize, VisualRef)` reused by the game manifest; endpoints `GET /v1/layouts → LayoutSummary[]`, `POST /v1/layouts (TableState with Name) → 201 TableState`, `GET /v1/layouts/{id} → TableState`, `PATCH /v1/layouts/{id} → TableState`, `DELETE /v1/layouts/{id} → 204`.
- Column mapping: `table_state(owner_id, name, width, length, texture_builtin | texture_asset_id)`; `terrain_placement(builtin | asset_id, pos_x, pos_y, pos_z, yaw, scale)` + `terrain_volume` rows (always written — a placement's volumes travel with it, contracts plan spec correction 1); `marker(kind, pos_*, yaw, diameter, label, visual_builtin | visual_asset_id)`; `deployment_zone(shape = DeploymentZone.Shape, team_ordinal)`.
- `WriteChildren` writes every `asset_id` — the texture, a placement's mesh, a marker's visual — straight from the request body with no ownership predicate: a waiver on the asset-library spec's argument that a library is private for discovery, not access, so an `asset_id` naming another owner's asset is accepted. An `exists` clause per reference can be added later.

Spec: setup content → The setup phase is snapshotted ("A Layout is this same record, owned by a user rather than a game"); setup phase → Layouts ("Saving copies the draft into a new row owned by the saver, with a name"; "Loading copies, so the layout row is untouched"); whole-design review ruling 4 ("Layouts stay management's"); `schema.md` → `table_state` has two nullable owners.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Content.Layouts;

namespace Tabletop.Management.Tests;

public class LayoutTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public LayoutTests(ManagementFactory f) => _f = f;

    public static TableState Sample(string? name = "Ruins") => new(Guid.CreateVersion7(), name, new TableSize(72, 48), VisualRef.OfBuiltin("grass"),
        [new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("hill-small"), new Point3(12, 12, 0), new Yaw(45), 1.0,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Rect(6, 6), new Point2(0, 0), new Yaw(0), 0, 2, TerrainDeclarations.Cover | TerrainDeclarations.Difficult, "hill"))]),
         new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfAsset(Guid.CreateVersion7()), new Point3(40, 20, 0), new Yaw(0), 1.5, [])],
        [new Marker(Guid.CreateVersion7(), MarkerKind.Objective, new Point3(36, 24, 0), new Yaw(0), 3, "centre", VisualRef.OfBuiltin("objective-ring"))],
        [new DeploymentZone(Guid.CreateVersion7(), "frontline/1", 1), new DeploymentZone(Guid.CreateVersion7(), "frontline/2", 2)]);

    static string Shape(TableState t) => ContentJson.Serialize(t with { Id = Guid.Empty,
        Terrain = t.Terrain.Select(p => p with { Id = Guid.Empty, Volumes = p.Volumes.Select(v => v with { Id = Guid.Empty }).ToList() }).ToList(),
        Markers = t.Markers.Select(m => m with { Id = Guid.Empty }).ToList(), Zones = t.Zones.Select(z => z with { Id = Guid.Empty }).ToList() });

    [Fact]
    public async Task Save_copies_with_new_ids_and_reads_back_the_same_shape()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var input = Sample();
        var saved = await c.PostJson<TableState>("/v1/layouts", input);
        Assert.NotEqual(input.Id, saved.Id);
        Assert.Empty(saved.Terrain.Select(p => p.Id).Intersect(input.Terrain.Select(p => p.Id)));
        Assert.Equal(Shape(input), Shape(saved));
        Assert.Equal(ContentJson.Serialize(saved), ContentJson.Serialize(await c.GetJson<TableState>($"/v1/layouts/{saved.Id}")));
        var list = await c.GetJson<List<LayoutSummary>>("/v1/layouts");
        Assert.Equal([new LayoutSummary(saved.Id, "Ruins", new TableSize(72, 48), 2, 1)], list);
    }

    [Fact]
    public async Task Patch_renames_or_replaces_and_everything_is_owner_only()
    {
        var mine = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var theirs = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var saved = await mine.PostJson<TableState>("/v1/layouts", Sample());
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.GetAsync($"/v1/layouts/{saved.Id}")).StatusCode);
        var renamed = await mine.PatchJson<TableState>($"/v1/layouts/{saved.Id}", new PatchLayoutRequest("Ruins v2", null));
        Assert.Equal("Ruins v2", renamed.Name); Assert.Equal(2, renamed.Terrain.Count);
        var replaced = await mine.PatchJson<TableState>($"/v1/layouts/{saved.Id}", new PatchLayoutRequest(null, Sample() with { Size = new TableSize(44, 30), Terrain = [] }));
        Assert.Equal(("Ruins v2", 44.0, 0), (replaced.Name, replaced.Size.Width, replaced.Terrain.Count));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await mine.PostAsJsonAsync("/v1/layouts", Sample(name: null), ContentJson.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await mine.DeleteAsync($"/v1/layouts/{saved.Id}")).StatusCode);
        Assert.Empty(await mine.GetJson<List<LayoutSummary>>("/v1/layouts"));
    }

    [Fact]
    public async Task Missing_texture_is_rejected_before_any_row_is_written()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        using var doc = System.Text.Json.JsonDocument.Parse(ContentJson.Serialize(Sample()));
        using var stream = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.NameEquals("texture")) continue;
                prop.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        var content = new ByteArrayContent(stream.ToArray());
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        var response = await c.PostAsync("/v1/layouts", content);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Empty(await c.GetJson<List<LayoutSummary>>("/v1/layouts"));
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `LayoutRepository.cs`:**

```csharp
using Dapper;
using Npgsql;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Persistence;

namespace Tabletop.Management.Content.Layouts;

public sealed record LayoutSummary(Guid Id, string Name, TableSize Size, int Terrain, int Markers);
public sealed record PatchLayoutRequest(string? Name, TableState? State);

public sealed class LayoutRepository(Database db)
{
    private sealed record Head(Guid Id, string? Name, double Width, double Length, string? TextureBuiltin, Guid? TextureAssetId);
    private sealed record PlacementRow(Guid Id, string? Builtin, Guid? AssetId, double PosX, double PosY, double PosZ, double Yaw, double Scale);
    private sealed record MarkerRow(Guid Id, string Kind, double PosX, double PosY, double PosZ, double Yaw, double Diameter, string? Label, string? VisualBuiltin, Guid? VisualAssetId);
    private sealed record ZoneRow(Guid Id, string Shape, int TeamOrdinal);

    public async Task<TableState> Save(Guid ownerId, TableState state)
    {
        var id = Guid.CreateVersion7();
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        await c.ExecuteAsync("insert into table_state (id, owner_id, name, width, length, texture_builtin, texture_asset_id) values (@id, @ownerId, @name, @w, @l, @tb, @ta)",
            new { id, ownerId, name = state.Name, w = state.Size.Width, l = state.Size.Length, tb = state.Texture.Builtin, ta = state.Texture.AssetId }, tx);
        await WriteChildren(c, tx, id, state);
        await tx.CommitAsync();
        return (await Get(ownerId, id))!;
    }

    /// <summary>Copies the children with fresh ids. Shared with Patch; the game server writes the game's own tree, never this code.</summary>
    private static async Task WriteChildren(NpgsqlConnection c, NpgsqlTransaction tx, Guid tableStateId, TableState s)
    {
        foreach (var p in s.Terrain)
        {
            var pid = Guid.CreateVersion7();
            await c.ExecuteAsync("insert into terrain_placement (id, table_state_id, builtin, asset_id, pos_x, pos_y, pos_z, yaw, scale) values (@pid, @tableStateId, @b, @a, @x, @y, @z, @yaw, @scale)",
                new { pid, tableStateId, b = p.Mesh.Builtin, a = p.Mesh.AssetId, x = p.Position.X, y = p.Position.Y, z = p.Position.Z, yaw = p.Yaw.Degrees, scale = p.Scale }, tx);
            foreach (var v in p.Volumes)
            {
                var r = VolumeRow.From(Guid.CreateVersion7(), v.Volume);
                await c.ExecuteAsync($"insert into terrain_volume (terrain_placement_id, {VolumeRow.InsertColumns}) values (@Pid, {VolumeRow.InsertValues})",
                    new { Pid = pid, r.Id, r.Kind, r.Footprint, r.DimA, r.DimB, r.OffsetX, r.OffsetY, r.Yaw, r.Bottom, r.Top, r.Impassable, r.BlocksLos, r.SeeInOut, r.Cover, r.Difficult, r.Dangerous, r.Notes }, tx);
            }
        }
        foreach (var m in s.Markers)
            await c.ExecuteAsync("insert into marker (id, table_state_id, kind, pos_x, pos_y, pos_z, yaw, diameter, label, visual_builtin, visual_asset_id) values (@id, @tableStateId, @kind, @x, @y, @z, @yaw, @d, @label, @vb, @va)",
                new { id = Guid.CreateVersion7(), tableStateId, kind = m.Kind.ToString().ToLowerInvariant(), x = m.Position.X, y = m.Position.Y, z = m.Position.Z, yaw = m.Yaw.Degrees, d = m.Diameter, label = m.Label, vb = m.Visual.Builtin, va = m.Visual.AssetId }, tx);
        foreach (var z in s.Zones)
            await c.ExecuteAsync("insert into deployment_zone (id, table_state_id, shape, team_ordinal) values (@id, @tableStateId, @shape, @ord)", new { id = Guid.CreateVersion7(), tableStateId, shape = z.Shape, ord = z.TeamOrdinal }, tx);
    }

    public static async Task<TableState> ReadTree(NpgsqlConnection c, Guid id, string? name, TableSize size, VisualRef texture)
    {
        var placements = (await c.QueryAsync<PlacementRow>("select id as Id, builtin as Builtin, asset_id as AssetId, pos_x::float8 as PosX, pos_y::float8 as PosY, pos_z::float8 as PosZ, yaw::float8 as Yaw, scale::float8 as Scale from terrain_placement where table_state_id = @id order by id", new { id })).ToList();
        var volumes = new Dictionary<Guid, List<TerrainVolume>>();
        foreach (var p in placements)
            volumes[p.Id] = (await c.QueryAsync<VolumeRow>($"select {VolumeRow.Columns} from terrain_volume where terrain_placement_id = @pid order by id", new { pid = p.Id })).Select(v => new TerrainVolume(v.Id, v.ToVolume())).ToList();
        var markers = await c.QueryAsync<MarkerRow>("select id as Id, kind as Kind, pos_x::float8 as PosX, pos_y::float8 as PosY, pos_z::float8 as PosZ, yaw::float8 as Yaw, diameter::float8 as Diameter, label as Label, visual_builtin as VisualBuiltin, visual_asset_id as VisualAssetId from marker where table_state_id = @id order by id", new { id });
        var zones = await c.QueryAsync<ZoneRow>("select id as Id, shape as Shape, team_ordinal as TeamOrdinal from deployment_zone where table_state_id = @id order by team_ordinal", new { id });
        return new TableState(id, name, size, texture,
            placements.Select(p => new TerrainPlacement(p.Id, new VisualRef(p.Builtin, p.AssetId), new Point3(p.PosX, p.PosY, p.PosZ), new Yaw(p.Yaw), p.Scale, volumes[p.Id])).ToList(),
            markers.Select(m => new Marker(m.Id, Enum.Parse<MarkerKind>(m.Kind, true), new Point3(m.PosX, m.PosY, m.PosZ), new Yaw(m.Yaw), m.Diameter, m.Label, new VisualRef(m.VisualBuiltin, m.VisualAssetId))).ToList(),
            zones.Select(z => new DeploymentZone(z.Id, z.Shape, z.TeamOrdinal)).ToList());
    }

    public async Task<TableState?> Get(Guid ownerId, Guid id)
    {
        await using var c = await db.Open();
        var h = await c.QuerySingleOrDefaultAsync<Head>("select id as Id, name as Name, width::float8 as Width, length::float8 as Length, texture_builtin as TextureBuiltin, texture_asset_id as TextureAssetId from table_state where id = @id and owner_id = @ownerId", new { id, ownerId });
        return h is null ? null : await ReadTree(c, h.Id, h.Name, new TableSize(h.Width, h.Length), new VisualRef(h.TextureBuiltin, h.TextureAssetId));
    }

    public async Task<IReadOnlyList<LayoutSummary>> List(Guid ownerId)
    {
        await using var c = await db.Open();
        var rows = await c.QueryAsync<(Guid Id, string Name, double Width, double Length, int Terrain, int Markers)>(@"select t.id, t.name, t.width::float8, t.length::float8,
            (select count(*) from terrain_placement p where p.table_state_id = t.id)::int, (select count(*) from marker m where m.table_state_id = t.id)::int
            from table_state t where t.owner_id = @ownerId order by t.id", new { ownerId });
        return rows.Select(r => new LayoutSummary(r.Id, r.Name, new TableSize(r.Width, r.Length), r.Terrain, r.Markers)).ToList();
    }

    public async Task<TableState?> Patch(Guid ownerId, Guid id, PatchLayoutRequest p)
    {
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        var n = p.State is null
            ? await c.ExecuteAsync("update table_state set name = coalesce(@name, name) where id = @id and owner_id = @ownerId", new { id, ownerId, name = p.Name }, tx)
            : await c.ExecuteAsync("update table_state set name = coalesce(@name, name), width = @w, length = @l, texture_builtin = @tb, texture_asset_id = @ta where id = @id and owner_id = @ownerId",
                new { id, ownerId, name = p.Name, w = p.State.Size.Width, l = p.State.Size.Length, tb = p.State.Texture.Builtin, ta = p.State.Texture.AssetId }, tx);
        if (n == 0) return null;
        if (p.State is not null)
        {
            await c.ExecuteAsync("delete from terrain_placement where table_state_id = @id; delete from marker where table_state_id = @id; delete from deployment_zone where table_state_id = @id", new { id }, tx);
            await WriteChildren(c, tx, id, p.State);
        }
        await tx.CommitAsync();
        return await Get(ownerId, id);
    }

    public async Task<bool> Delete(Guid ownerId, Guid id)
    {
        await using var c = await db.Open();
        return await c.ExecuteAsync("delete from table_state where id = @id and owner_id = @ownerId", new { id, ownerId }) == 1;
    }
}
```

- [ ] **Step 3: Write `LayoutEndpoints.cs`**

```csharp
using System.Security.Claims;
using Tabletop.Content;
using Tabletop.Management.Http;

namespace Tabletop.Management.Content.Layouts;

public static class LayoutEndpoints
{
    /// <summary>A property the JSON omitted lands as default(VisualRef), which is IsValid == false; a texture, mesh or
    /// marker visual in that state must never reach a row.</summary>
    static bool HasValidVisuals(TableState s) =>
        s.Texture.IsValid && s.Terrain.All(p => p.Mesh.IsValid) && s.Markers.All(m => m.Visual.IsValid);

    public static void MapLayouts(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/layouts").RequireAuthorization();
        g.MapGet("", async (ClaimsPrincipal u, LayoutRepository layouts) => Results.Ok(await layouts.List(CurrentAccount.Id(u))));
        g.MapPost("", async (ClaimsPrincipal u, TableState state, LayoutRepository layouts) =>
        {
            if (string.IsNullOrWhiteSpace(state.Name)) return Problems.Unprocessable("A saved layout needs a name.");
            if (!HasValidVisuals(state)) return Problems.Unprocessable("Every texture, terrain mesh and marker visual needs a builtin or an assetId.");
            var saved = await layouts.Save(CurrentAccount.Id(u), state);
            return Results.Created($"/v1/layouts/{saved.Id}", saved);
        });
        g.MapGet("/{id:guid}", async (ClaimsPrincipal u, Guid id, LayoutRepository layouts) => await layouts.Get(CurrentAccount.Id(u), id) is { } t ? Results.Ok(t) : Problems.NotFound());
        g.MapPatch("/{id:guid}", async (ClaimsPrincipal u, Guid id, PatchLayoutRequest p, LayoutRepository layouts) =>
        {
            if (p.State is not null && !HasValidVisuals(p.State)) return Problems.Unprocessable("Every texture, terrain mesh and marker visual needs a builtin or an assetId.");
            return await layouts.Patch(CurrentAccount.Id(u), id, p) is { } t ? Results.Ok(t) : Problems.NotFound();
        });
        g.MapDelete("/{id:guid}", async (ClaimsPrincipal u, Guid id, LayoutRepository layouts) => await layouts.Delete(CurrentAccount.Id(u), id) ? Results.NoContent() : Problems.NotFound());
    }
}
```

- [ ] **Step 4: Wire `Program.cs`** (`AddSingleton<LayoutRepository>()`, `app.MapLayouts()`); run `dotnet test`; expect at least 58 passed. Commit: `Save and load layouts as copies of a table state`.
