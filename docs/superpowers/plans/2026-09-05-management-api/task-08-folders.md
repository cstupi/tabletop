# Task 8: Folders

**Files:**
- Create: `server/management/Content/Assets/FolderRepository.cs`, `server/management/Content/Assets/FolderEndpoints.cs`
- Modify: `server/management/Program.cs`
- Create: `tests/Tabletop.Management.Tests/FolderTests.cs`

**Interfaces:**
- Produces: `FolderDto(Guid Id, Guid? ParentId, string Name, IReadOnlyList<Guid> AssetIds)`; `FolderRequest(string Name, Guid? ParentId)`; `FolderRepository(Database)` with `List(ownerId) : IReadOnlyList<FolderDto>` (whole tree, unpaged), `Create`, `Patch`, `Delete`, `AddAsset(ownerId, folderId, assetId) : bool`, `RemoveAsset`; endpoints `GET /v1/folders`, `POST /v1/folders → 201`, `PATCH /v1/folders/{id}` (name and/or parent), `DELETE /v1/folders/{id} → 204` (children cascade in the schema; assets are untouched), `PUT /v1/folders/{id}/assets/{assetId} → 204`, `DELETE /v1/folders/{id}/assets/{assetId} → 204`.
- A blank name is `422` on `POST` and on `PATCH` alike. A parent must be the owner's own folder and not a descendant of the folder being moved (`422` otherwise). An asset can be in several folders; membership is `asset_folder`.

Spec: asset library → Folders ("Folders nest, and an asset can be in several of them … an asset in no folder is fine"); management API → The rest of the surface (folders, "small enough to return whole"); accounts → What an account owns ("Folders are the asset library's, and only the asset library's").

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using Tabletop.Management.Content.Assets;

namespace Tabletop.Management.Tests;

public class FolderTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public FolderTests(ManagementFactory f) => _f = f;

    [Fact]
    public async Task Nest_move_and_file_an_asset_in_two_folders()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var orks = await c.PostJson<FolderDto>("/v1/folders", new FolderRequest("Orks", null));
        var winter = await c.PostJson<FolderDto>("/v1/folders", new FolderRequest("Winter", null));
        var boyz = await c.PostJson<FolderDto>("/v1/folders", new FolderRequest("Boyz", orks.Id));
        var glb = TestGlb.Build((3, null, true));
        var asset = (await c.PostJson<CreateAssetResponse>("/v1/assets", new CreateAssetRequest("Boy", AssetKind.Miniature, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(glb)), glb.Length, "model/gltf-binary", 1, new(0, 0, 0), Tabletop.Geometry.Base.Circle(1.26)))).Asset;
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsync($"/v1/folders/{boyz.Id}/assets/{asset.Id}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsync($"/v1/folders/{winter.Id}/assets/{asset.Id}", null)).StatusCode);
        var tree = await c.GetJson<List<FolderDto>>("/v1/folders");
        Assert.Equal(2, tree.Count(f => f.AssetIds.Contains(asset.Id)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PatchJson($"/v1/folders/{orks.Id}", new FolderRequest("Orks", boyz.Id))).StatusCode);   // cycle
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/v1/folders/{orks.Id}")).StatusCode);
        tree = await c.GetJson<List<FolderDto>>("/v1/folders");
        Assert.Equal([winter.Id], tree.Select(f => f.Id));                       // Boyz cascaded with Orks
        Assert.True((await c.GetAsync($"/v1/assets/{asset.Id}")).IsSuccessStatusCode);  // the asset is untouched
    }

    [Fact]
    public async Task Folders_are_owner_only()
    {
        var mine = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var theirs = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var f = await mine.PostJson<FolderDto>("/v1/folders", new FolderRequest("Mine", null));
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.DeleteAsync($"/v1/folders/{f.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await theirs.PostAsJsonAsync("/v1/folders", new FolderRequest("Under yours", f.Id), Tabletop.Content.ContentJson.Options)).StatusCode);
    }
}
```

`HttpJson.cs` (test helper introduced in Task 7): `PostJson<T>(url, body)`, `PatchJson(url, body)`, `GetJson<T>(url)`, `PutJson<T>(url, body)` — thin wrappers over `System.Net.Http.Json` passing `ContentJson.Options` and asserting success where a body is read.

- [ ] **Step 2: Run; expect compile errors. Write `FolderRepository.cs`:**

```csharp
using Dapper;
using Tabletop.Management.Persistence;

namespace Tabletop.Management.Content.Assets;

public sealed record FolderDto(Guid Id, Guid? ParentId, string Name, IReadOnlyList<Guid> AssetIds);
public sealed record FolderRequest(string Name, Guid? ParentId);

public sealed class FolderRepository(Database db)
{
    private sealed record Row(Guid Id, Guid? ParentId, string Name);

    public async Task<IReadOnlyList<FolderDto>> List(Guid ownerId)
    {
        await using var c = await db.Open();
        var folders = (await c.QueryAsync<Row>("select id as Id, parent_id as ParentId, name as Name from folder where owner_id = @ownerId order by id", new { ownerId })).ToList();
        var members = (await c.QueryAsync<(Guid FolderId, Guid AssetId)>("select af.folder_id, af.asset_id from asset_folder af join folder f on f.id = af.folder_id where f.owner_id = @ownerId", new { ownerId }))
            .ToLookup(m => m.FolderId, m => m.AssetId);
        return folders.Select(f => new FolderDto(f.Id, f.ParentId, f.Name, members[f.Id].ToList())).ToList();
    }

    /// <summary>Null when the parent is not the owner's, or would make a cycle.</summary>
    public async Task<FolderDto?> Create(Guid ownerId, FolderRequest r)
    {
        await using var c = await db.Open();
        if (r.ParentId is { } p && !await Owns(c, ownerId, p)) return null;
        var id = Guid.CreateVersion7();
        await c.ExecuteAsync("insert into folder (id, owner_id, parent_id, name) values (@id, @ownerId, @parent, @name)", new { id, ownerId, parent = r.ParentId, name = r.Name });
        return new FolderDto(id, r.ParentId, r.Name, []);
    }

    public async Task<(bool Found, bool Valid)> Patch(Guid ownerId, Guid id, FolderRequest r)
    {
        await using var c = await db.Open();
        if (!await Owns(c, ownerId, id)) return (false, false);
        if (r.ParentId is { } p && (p == id || !await Owns(c, ownerId, p) || await IsDescendant(c, ancestor: id, candidate: p))) return (true, false);
        await c.ExecuteAsync("update folder set name = @name, parent_id = @parent where id = @id", new { id, name = r.Name, parent = r.ParentId });
        return (true, true);
    }

    public async Task<bool> Delete(Guid ownerId, Guid id)
    {
        await using var c = await db.Open();
        return await c.ExecuteAsync("delete from folder where id = @id and owner_id = @ownerId", new { id, ownerId }) == 1;
    }

    public async Task<bool> AddAsset(Guid ownerId, Guid folderId, Guid assetId)
    {
        await using var c = await db.Open();
        return await c.ExecuteAsync(@"insert into asset_folder (asset_id, folder_id) select a.id, f.id from asset a, folder f
            where a.id = @assetId and a.owner_id = @ownerId and f.id = @folderId and f.owner_id = @ownerId on conflict do nothing", new { ownerId, folderId, assetId }) >= 0
            && await Owns(c, ownerId, folderId) && await c.ExecuteScalarAsync<bool>("select exists(select 1 from asset where id = @assetId and owner_id = @ownerId)", new { assetId, ownerId });
    }

    public async Task<bool> RemoveAsset(Guid ownerId, Guid folderId, Guid assetId)
    {
        await using var c = await db.Open();
        return await c.ExecuteAsync("delete from asset_folder af using folder f where f.id = af.folder_id and f.owner_id = @ownerId and af.folder_id = @folderId and af.asset_id = @assetId", new { ownerId, folderId, assetId }) == 1;
    }

    private static Task<bool> Owns(Npgsql.NpgsqlConnection c, Guid ownerId, Guid folderId) =>
        c.ExecuteScalarAsync<bool>("select exists(select 1 from folder where id = @folderId and owner_id = @ownerId)", new { folderId, ownerId });

    private static Task<bool> IsDescendant(Npgsql.NpgsqlConnection c, Guid ancestor, Guid candidate) =>
        c.ExecuteScalarAsync<bool>(@"with recursive tree as (select id from folder where parent_id = @ancestor union all select f.id from folder f join tree t on f.parent_id = t.id)
            select exists(select 1 from tree where id = @candidate)", new { ancestor, candidate });
}
```

- [ ] **Step 3: Write `FolderEndpoints.cs`**

```csharp
using System.Security.Claims;
using Tabletop.Management.Http;

namespace Tabletop.Management.Content.Assets;

public static class FolderEndpoints
{
    public static void MapFolders(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/folders").RequireAuthorization();
        g.MapGet("", async (ClaimsPrincipal u, FolderRepository folders) => Results.Ok(await folders.List(CurrentAccount.Id(u))));
        g.MapPost("", async (ClaimsPrincipal u, FolderRequest r, FolderRepository folders) =>
            string.IsNullOrWhiteSpace(r.Name) ? Problems.Unprocessable("name is required")
            : await folders.Create(CurrentAccount.Id(u), r) is { } f ? Results.Created($"/v1/folders/{f.Id}", f) : Problems.Unprocessable("parentId is not one of your folders"));
        g.MapPatch("/{id:guid}", async (ClaimsPrincipal u, Guid id, FolderRequest r, FolderRepository folders) =>
        {
            if (string.IsNullOrWhiteSpace(r.Name)) return Problems.Unprocessable("name is required");
            var (found, valid) = await folders.Patch(CurrentAccount.Id(u), id, r);
            return !found ? Problems.NotFound() : !valid ? Problems.Unprocessable("parentId must be your folder and not this folder or one below it") : Results.NoContent();
        });
        g.MapDelete("/{id:guid}", async (ClaimsPrincipal u, Guid id, FolderRepository folders) => await folders.Delete(CurrentAccount.Id(u), id) ? Results.NoContent() : Problems.NotFound());
        g.MapPut("/{id:guid}/assets/{assetId:guid}", async (ClaimsPrincipal u, Guid id, Guid assetId, FolderRepository folders) =>
            await folders.AddAsset(CurrentAccount.Id(u), id, assetId) ? Results.NoContent() : Problems.NotFound());
        g.MapDelete("/{id:guid}/assets/{assetId:guid}", async (ClaimsPrincipal u, Guid id, Guid assetId, FolderRepository folders) =>
            await folders.RemoveAsset(CurrentAccount.Id(u), id, assetId) ? Results.NoContent() : Problems.NotFound());
    }
}
```

- [ ] **Step 4: Wire `Program.cs`** (`AddSingleton<FolderRepository>()`, `app.MapFolders()`), run `dotnet test`; expect at least 44 passed. Commit: `Add nested folders over the asset library`.
