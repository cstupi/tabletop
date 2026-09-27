# Task 10: Armies — import, list, get, patch, delete

**Files:**
- Create: `server/management/Content/Armies/ArmyRepository.cs`, `server/management/Content/Armies/ArmyEndpoints.cs`, `server/management/Content/Armies/CatalogKinds.cs`
- Modify: `server/management/Program.cs`, `tests/Tabletop.Management.Tests/ManagementFactory.cs`
- Create: `tests/Tabletop.Management.Tests/FakeArmyForge.cs`, `tests/Tabletop.Management.Tests/ArmyTests.cs`

**Interfaces:**
- Consumes: `ArmyForgeClient`, `ArmyForgeImport`; `Tabletop.Content.Army` tree; `Keyset`/`Page<T>`.
- Produces: `ArmySummary(Guid Id, string Name, string GameSystem, string Source, int Units, int Models)`; `ImportRequest(string Source)`; `PatchArmyRequest(string? Name, IReadOnlyList<UnitPatch>? Units, IReadOnlyList<SlotPatch>? Slots)`, `UnitPatch(Guid UnitId, Guid? AssetId)` (binds every slot of the unit; the unit default), `SlotPatch(Guid SlotId, Guid? AssetId, bool ClearAsset, Base? Base, double? Height)`; `CatalogKinds(Database).For(string gameRules) : Task<IReadOnlyDictionary<string, PropertyLevel>>` (from `catalog_entry` joined to `properties` on `game_rules`; empty when no set is loaded); `ArmyRepository(Database)` with `Insert(ownerId, Army)`, `Get(ownerId, id) : Army?` (the whole tree), `List(ownerId, Keyset) : IReadOnlyList<ArmySummary>`, `Patch(ownerId, id, PatchArmyRequest) : Army?`, `Delete(ownerId, id) : bool`; endpoints `POST /v1/armies/import {source} → 201 Army`, `GET /v1/armies?limit&after → Page<ArmySummary>`, `GET /v1/armies/{id} → Army`, `PATCH /v1/armies/{id} → Army`, `DELETE /v1/armies/{id} → 204`.
- The soft asset reference is written as given without checking the asset exists (the army holds the hitbox; the asset is appearance) — the army → Assets are a soft reference. `Patch` takes the same waiver on ownership: it writes `asset_id` straight from the request body with no predicate, on the asset-library spec's argument that a library is private for discovery, not access, so an `asset_id` naming another owner's asset is accepted; an `exists` clause per patched slot can be added later. `PATCH` is a patch, not a document: the spec left it open (management API → Deliberately not decided); the client shell never edits armies, and the web app edits one slot or one unit default at a time.
- Import failures: Army Forge unreachable or non-200 → `502`; a source with no id → `422`; another game system → `422`.

Spec: management API → Army import, The rest of the surface (`PATCH /v1/armies/{id} bindings, bases`); the army → Binding ("the default fills every slot in a unit with the same mesh; overriding individual slots is expected"), Base shape and height ("set by the user, per slot"), A changed list means a re-import (no reconciliation; a re-import is a new army); `schema.md` → the ten army tables.

- [ ] **Step 1: Write `FakeArmyForge.cs` and the failing tests**

```csharp
using System.Net;

namespace Tabletop.Management.Tests;

/// <summary>Serves the fixture for id c2-6YsWRH3py and 404 for anything else.</summary>
public sealed class FakeArmyForge : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var id = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["id"];
        if (id != "c2-6YsWRH3py") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "army-forge-c2-6YsWRH3py.json"));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });
    }
}
```

`ManagementFactory.ConfigureTestServices` gains `s.AddHttpClient(Content.Armies.ArmyForgeClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new FakeArmyForge());`.

```csharp
using System.Net;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Content.Armies;
using Tabletop.Management.Http;

namespace Tabletop.Management.Tests;

public class ArmyTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public ArmyTests(ManagementFactory f) => _f = f;
    const string Share = "https://army-forge.onepagerules.com/share?id=c2-6YsWRH3py";

    [Fact]
    public async Task Import_stores_the_whole_tree_and_reads_it_back_identically()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var imported = await c.PostJson<Army>("/v1/armies/import", new ImportRequest(Share));
        Assert.Equal(5, imported.Units.Count); Assert.Equal(Share, imported.Source);
        var fetched = await c.GetJson<Army>($"/v1/armies/{imported.Id}");
        Assert.Equal(ContentJson.Serialize(imported), ContentJson.Serialize(fetched));
        var page = await c.GetJson<Page<ArmySummary>>("/v1/armies");
        Assert.Equal([(imported.Id, 5, 33)], page.Items.Select(a => (a.Id, a.Units, a.Models)));
    }

    [Fact]
    public async Task Patch_binds_a_unit_default_then_overrides_one_slot_and_its_base()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var army = await c.PostJson<Army>("/v1/armies/import", new ImportRequest("c2-6YsWRH3py"));
        var unit = army.Units.Single(u => u.Name == "Beast Riders");
        var mesh = Guid.CreateVersion7(); var special = Guid.CreateVersion7();
        var patched = await c.PatchJson<Army>($"/v1/armies/{army.Id}", new PatchArmyRequest("Daemons", [new UnitPatch(unit.Id, mesh)],
            [new SlotPatch(unit.Models[0].Id, special, false, Base.Circle(1.57), 2.0)]));
        var u = patched.Units.Single(x => x.Id == unit.Id);
        Assert.Equal("Daemons", patched.Name);
        Assert.Equal(special, u.Models[0].AssetId); Assert.Equal(Base.Circle(1.57), u.Models[0].Base); Assert.Equal(2.0, u.Models[0].Height);
        Assert.All(u.Models.Skip(1), m => Assert.Equal(mesh, m.AssetId));
        var cleared = await c.PatchJson<Army>($"/v1/armies/{army.Id}", new PatchArmyRequest(null, null, [new SlotPatch(unit.Models[0].Id, null, true, null, null)]));
        Assert.Null(cleared.Units.Single(x => x.Id == unit.Id).Models[0].AssetId);
    }

    [Fact]
    public async Task Owner_only_and_import_errors()
    {
        var mine = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var theirs = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var army = await mine.PostJson<Army>("/v1/armies/import", new ImportRequest(Share));
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.GetAsync($"/v1/armies/{army.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.DeleteAsync($"/v1/armies/{army.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadGateway, (await mine.PostAsJsonAsync("/v1/armies/import", new ImportRequest("https://army-forge.onepagerules.com/share?id=missing1"), ContentJson.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await mine.PostAsJsonAsync("/v1/armies/import", new ImportRequest("not a link"), ContentJson.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await mine.DeleteAsync($"/v1/armies/{army.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mine.GetAsync($"/v1/armies/{army.Id}")).StatusCode);
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `CatalogKinds.cs` and `ArmyRepository.cs`:**

```csharp
using Dapper;
using Tabletop.Management.Persistence;

namespace Tabletop.Management.Content.Armies;

/// <summary>Which catalog names are model-level, from whatever Properties sets exist for the game rules. Empty until content is loaded.</summary>
public sealed class CatalogKinds(Database db)
{
    public async Task<IReadOnlyDictionary<string, PropertyLevel>> For(string gameRules)
    {
        await using var c = await db.Open();
        var rows = await c.QueryAsync<(string Name, string Kind)>(@"select distinct ce.name, ce.kind from catalog_entry ce join properties p on p.content_hash = ce.properties_hash
            where p.game_rules = @gameRules and ce.kind in ('unit', 'model')", new { gameRules });
        return rows.ToDictionary(r => r.Name, r => r.Kind == "model" ? PropertyLevel.Model : PropertyLevel.Unit);
    }
}
```

```csharp
using Dapper;
using Npgsql;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Persistence;

namespace Tabletop.Management.Content.Armies;

public sealed record ArmySummary(Guid Id, string Name, string GameSystem, string Source, int Units, int Models);
public sealed record ImportRequest(string Source);
public sealed record UnitPatch(Guid UnitId, Guid? AssetId);
public sealed record SlotPatch(Guid SlotId, Guid? AssetId, bool ClearAsset, Base? Base, double? Height);
public sealed record PatchArmyRequest(string? Name, IReadOnlyList<UnitPatch>? Units, IReadOnlyList<SlotPatch>? Slots);

/// <summary>The army tree: army → unit → model_slot → weapon, each level with its stat and property rows.</summary>
public sealed class ArmyRepository(Database db)
{
    private sealed record UnitRow(Guid Id, int Ordinal, string Name);
    private sealed record SlotRow(Guid Id, Guid UnitId, int Ordinal, string Name, string BaseShape, double BaseA, double BaseB, double Height, Guid? AssetId);
    private sealed record WeaponRow(Guid Id, Guid SlotId, string Name);
    private sealed record KV(Guid Owner, string Key, string Value);
    private sealed record Prop(Guid Owner, string Name, int? Parameter);

    public async Task Insert(Guid ownerId, Army army)
    {
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        await c.ExecuteAsync("insert into army (id, owner_id, name, game_system, source) values (@Id, @ownerId, @Name, @GameSystem, @Source)", new { army.Id, ownerId, army.Name, army.GameSystem, army.Source }, tx);
        foreach (var u in army.Units)
        {
            await c.ExecuteAsync("insert into unit (id, army_id, ordinal, name) values (@Id, @armyId, @Ordinal, @Name)", new { u.Id, armyId = army.Id, u.Ordinal, u.Name }, tx);
            await c.ExecuteAsync("insert into unit_stat (unit_id, stat, value) values (@id, @k, @v)", u.Stats.Select(s => new { id = u.Id, k = s.Key, v = s.Value }), tx);
            await c.ExecuteAsync("insert into unit_property (unit_id, name, parameter) values (@id, @Name, @Parameter)", u.Properties.Select(p => new { id = u.Id, p.Name, p.Parameter }), tx);
            foreach (var m in u.Models)
            {
                await c.ExecuteAsync("insert into model_slot (id, unit_id, ordinal, name, base_shape, base_a, base_b, height, asset_id) values (@Id, @unitId, @Ordinal, @Name, @shape, @a, @b, @Height, @AssetId)",
                    new { m.Id, unitId = u.Id, m.Ordinal, m.Name, shape = m.Base.Shape.ToString().ToLowerInvariant(), a = m.Base.A, b = m.Base.B, m.Height, m.AssetId }, tx);
                await c.ExecuteAsync("insert into slot_stat (slot_id, stat, value) values (@id, @k, @v)", m.Stats.Select(s => new { id = m.Id, k = s.Key, v = s.Value }), tx);
                await c.ExecuteAsync("insert into slot_property (slot_id, name, parameter) values (@id, @Name, @Parameter)", m.Properties.Select(p => new { id = m.Id, p.Name, p.Parameter }), tx);
                foreach (var w in m.Weapons)
                {
                    await c.ExecuteAsync("insert into weapon (id, slot_id, name) values (@Id, @slotId, @Name)", new { w.Id, slotId = m.Id, w.Name }, tx);
                    await c.ExecuteAsync("insert into weapon_stat (weapon_id, stat, value) values (@id, @k, @v)", w.Stats.Select(s => new { id = w.Id, k = s.Key, v = s.Value }), tx);
                    await c.ExecuteAsync("insert into weapon_property (weapon_id, name, parameter) values (@id, @Name, @Parameter)", w.Properties.Select(p => new { id = w.Id, p.Name, p.Parameter }), tx);
                }
            }
        }
        await tx.CommitAsync();
    }

    public async Task<Army?> Get(Guid ownerId, Guid id)
    {
        await using var c = await db.Open();
        var head = await c.QuerySingleOrDefaultAsync<(string Name, string GameSystem, string Source)>("select name, game_system, source from army where id = @id and owner_id = @ownerId", new { id, ownerId });
        if (head == default) return null;
        var units = (await c.QueryAsync<UnitRow>("select id as Id, ordinal as Ordinal, name as Name from unit where army_id = @id order by ordinal", new { id })).ToList();
        var slots = (await c.QueryAsync<SlotRow>(@"select s.id as Id, s.unit_id as UnitId, s.ordinal as Ordinal, s.name as Name, s.base_shape as BaseShape, s.base_a::float8 as BaseA, s.base_b::float8 as BaseB, s.height::float8 as Height, s.asset_id as AssetId
            from model_slot s join unit u on u.id = s.unit_id where u.army_id = @id order by s.ordinal", new { id })).ToLookup(s => s.UnitId);
        // Stat, property and weapon rows are written once at import and never updated (Patch touches only model_slot
        // columns), so `ctid` — physical row order — is stable and equals insertion order for as long as the army
        // exists. It stands in for an ordinal column these tables don't have; if a future patch ever writes these
        // tables in place, an explicit ordinal must replace it.
        var weapons = (await c.QueryAsync<WeaponRow>("select w.id as Id, w.slot_id as SlotId, w.name as Name from weapon w join model_slot s on s.id = w.slot_id join unit u on u.id = s.unit_id where u.army_id = @id order by w.ctid", new { id })).ToLookup(w => w.SlotId);
        var unitStats = (await c.QueryAsync<KV>("select us.unit_id as Owner, us.stat as Key, us.value as Value from unit_stat us join unit u on u.id = us.unit_id where u.army_id = @id order by us.ctid", new { id })).ToLookup(x => x.Owner);
        var unitProps = (await c.QueryAsync<Prop>("select up.unit_id as Owner, up.name as Name, up.parameter as Parameter from unit_property up join unit u on u.id = up.unit_id where u.army_id = @id order by up.ctid", new { id })).ToLookup(x => x.Owner);
        var slotStats = (await c.QueryAsync<KV>("select ss.slot_id as Owner, ss.stat as Key, ss.value as Value from slot_stat ss join model_slot s on s.id = ss.slot_id join unit u on u.id = s.unit_id where u.army_id = @id order by ss.ctid", new { id })).ToLookup(x => x.Owner);
        var slotProps = (await c.QueryAsync<Prop>("select sp.slot_id as Owner, sp.name as Name, sp.parameter as Parameter from slot_property sp join model_slot s on s.id = sp.slot_id join unit u on u.id = s.unit_id where u.army_id = @id order by sp.ctid", new { id })).ToLookup(x => x.Owner);
        var weaponStats = (await c.QueryAsync<KV>("select ws.weapon_id as Owner, ws.stat as Key, ws.value as Value from weapon_stat ws join weapon w on w.id = ws.weapon_id join model_slot s on s.id = w.slot_id join unit u on u.id = s.unit_id where u.army_id = @id order by ws.ctid", new { id })).ToLookup(x => x.Owner);
        var weaponProps = (await c.QueryAsync<Prop>("select wp.weapon_id as Owner, wp.name as Name, wp.parameter as Parameter from weapon_property wp join weapon w on w.id = wp.weapon_id join model_slot s on s.id = w.slot_id join unit u on u.id = s.unit_id where u.army_id = @id order by wp.ctid", new { id })).ToLookup(x => x.Owner);

        IReadOnlyDictionary<string, string> Stats(ILookup<Guid, KV> l, Guid owner) => l[owner].ToDictionary(x => x.Key, x => x.Value);
        IReadOnlyList<PropertyInstance> Props(ILookup<Guid, Prop> l, Guid owner) => l[owner].Select(p => new PropertyInstance(p.Name, p.Parameter)).ToList();

        return new Army(id, head.Name, head.GameSystem, head.Source, units.Select(u => new Unit(u.Id, u.Ordinal, u.Name, Stats(unitStats, u.Id), Props(unitProps, u.Id),
            slots[u.Id].Select(s => new ModelSlot(s.Id, s.Ordinal, s.Name, Stats(slotStats, s.Id), Props(slotProps, s.Id),
                new Base(Enum.Parse<BaseShape>(s.BaseShape, true), s.BaseA, s.BaseB), s.Height, s.AssetId,
                weapons[s.Id].Select(w => new Weapon(w.Id, w.Name, Stats(weaponStats, w.Id), Props(weaponProps, w.Id))).ToList())).ToList())).ToList());
    }

    public async Task<IReadOnlyList<ArmySummary>> List(Guid ownerId, Http.Keyset page)
    {
        await using var c = await db.Open();
        return (await c.QueryAsync<ArmySummary>(@"select a.id as Id, a.name as Name, a.game_system as GameSystem, a.source as Source,
            (select count(*) from unit u where u.army_id = a.id)::int as Units,
            (select count(*) from model_slot s join unit u on u.id = s.unit_id where u.army_id = a.id)::int as Models
            from army a where a.owner_id = @ownerId and (@after::uuid is null or a.id > @after::uuid) order by a.id limit @limit", new { ownerId, after = page.After, limit = page.Limit })).ToList();
    }

    public async Task<Army?> Patch(Guid ownerId, Guid id, PatchArmyRequest p)
    {
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        if (await c.ExecuteAsync("update army set name = coalesce(@name, name) where id = @id and owner_id = @ownerId", new { id, ownerId, name = p.Name }, tx) == 0) return null;
        foreach (var u in p.Units ?? [])
            await c.ExecuteAsync("update model_slot set asset_id = @asset where unit_id = @unitId and unit_id in (select id from unit where army_id = @id)", new { id, unitId = u.UnitId, asset = u.AssetId }, tx);
        foreach (var s in p.Slots ?? [])
            await c.ExecuteAsync(@"update model_slot set asset_id = case when @clear then null else coalesce(@asset, asset_id) end,
                base_shape = coalesce(@shape, base_shape), base_a = coalesce(@a, base_a), base_b = coalesce(@b, base_b), height = coalesce(@height, height)
                where id = @slotId and unit_id in (select id from unit where army_id = @id)",
                new { id, slotId = s.SlotId, clear = s.ClearAsset, asset = s.AssetId, shape = s.Base?.Shape.ToString().ToLowerInvariant(), a = s.Base?.A, b = s.Base?.B, height = s.Height }, tx);
        await tx.CommitAsync();
        return await Get(ownerId, id);
    }

    public async Task<bool> Delete(Guid ownerId, Guid id)
    {
        await using var c = await db.Open();
        return await c.ExecuteAsync("delete from army where id = @id and owner_id = @ownerId", new { id, ownerId }) == 1;
    }
}
```

- [ ] **Step 3: Write `ArmyEndpoints.cs`**

```csharp
using System.Security.Claims;
using Tabletop.Management.Http;

namespace Tabletop.Management.Content.Armies;

public static class ArmyEndpoints
{
    public static void MapArmies(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/armies").RequireAuthorization();

        g.MapPost("/import", async (ClaimsPrincipal u, ImportRequest r, ArmyForgeClient forge, CatalogKinds kinds, ArmyRepository armies) =>
        {
            var listId = ArmyForgeClient.ListId(r.Source);
            if (listId is null) return Problems.Unprocessable("source must be an Army Forge share link or list id");
            AfList list;
            try { list = await forge.Fetch(listId); }
            catch (ArmyForgeException e) { return Problems.BadGateway($"Army Forge answered {e.Status} for list {listId}"); }
            catch (HttpRequestException e) { return Problems.BadGateway($"Army Forge unreachable: {e.Message}"); }
            if (!ArmyForgeImport.GameRules.TryGetValue(list.GameSystem, out var gameRules)) return Problems.Unprocessable($"Only Grimdark Future and Age of Fantasy lists import; this list is '{list.GameSystem}'.");
            var army = ArmyForgeImport.ToArmy(list, r.Source, await kinds.For(gameRules));
            await armies.Insert(CurrentAccount.Id(u), army);
            return Results.Created($"/v1/armies/{army.Id}", army);
        });

        g.MapGet("", async (ClaimsPrincipal u, ArmyRepository armies, int? limit, Guid? after) =>
        {
            var page = Keyset.Parse(limit, after);
            return Results.Ok(Page<ArmySummary>.Of(await armies.List(CurrentAccount.Id(u), page), page.Limit, a => a.Id));
        });
        g.MapGet("/{id:guid}", async (ClaimsPrincipal u, Guid id, ArmyRepository armies) => await armies.Get(CurrentAccount.Id(u), id) is { } a ? Results.Ok(a) : Problems.NotFound());
        g.MapPatch("/{id:guid}", async (ClaimsPrincipal u, Guid id, PatchArmyRequest p, ArmyRepository armies) => await armies.Patch(CurrentAccount.Id(u), id, p) is { } a ? Results.Ok(a) : Problems.NotFound());
        g.MapDelete("/{id:guid}", async (ClaimsPrincipal u, Guid id, ArmyRepository armies) => await armies.Delete(CurrentAccount.Id(u), id) ? Results.NoContent() : Problems.NotFound());
    }
}
```

- [ ] **Step 4: Wire `Program.cs`** — `builder.Services.AddHttpClient(ArmyForgeClient.HttpClientName); builder.Services.AddSingleton(sp => new ArmyForgeClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(ArmyForgeClient.HttpClientName))); builder.Services.AddSingleton<CatalogKinds>(); builder.Services.AddSingleton<ArmyRepository>();` and `app.MapArmies();`.

- [ ] **Step 5: Run `dotnet test tests/Tabletop.Management.Tests`; expect at least 55 passed. Commit: `Import armies from Army Forge and edit their bindings and bases`.**
