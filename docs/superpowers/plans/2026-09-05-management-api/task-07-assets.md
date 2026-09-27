# Task 7: Assets — create, confirm, download, library CRUD, volumes

**Files:**
- Create: `server/management/Content/Assets/AssetDtos.cs`, `server/management/Content/VolumeRows.cs`, `server/management/Content/Assets/AssetRepository.cs`, `server/management/Content/Assets/AssetEndpoints.cs`
- Modify: `server/management/Program.cs`
- Create: `tests/Tabletop.Management.Tests/AssetTests.cs`

**Interfaces:**
- Consumes: `IBlobStore`, `GlbHeader`, `Keyset`, `Page<T>`, `Problems`, `CurrentAccount`; `Tabletop.Geometry.Base`, `Orientation`, `Volume`, `VolumeKind`, `Footprint`, `FootprintKind`, `TerrainDeclarations`; `Tabletop.Content.TerrainVolume`.
- Produces: `AssetKind { Miniature, Terrain, Texture }`; `AssetDto(Guid Id, string Name, AssetKind Kind, string Hash, long ByteSize, string ContentType, bool Uploaded, double Scale, Orientation Orientation, Base? ModelledBase)`; `CreateAssetRequest(string Name, AssetKind Kind, string Hash, long ByteSize, string ContentType, double Scale, Orientation Orientation, Base? ModelledBase)`; `CreateAssetResponse(AssetDto Asset, Uri? UploadUrl)` (null URL = the bytes are already there); `ConfirmResponse(AssetDto Asset, long? Triangles, string? Warning)`; `PatchAssetRequest(string? Name, double? Scale, Orientation? Orientation, Base? ModelledBase)`; `DownloadResponse(Uri Url, DateTimeOffset ExpiresAt)`; `VolumeRow` with `Volume ToVolume()` and `static VolumeRow From(Guid id, Volume v)` (shared with layouts); `AssetRepository(Database)` with `Create`, `Get`, `List`, `Patch`, `MarkUploaded`, `Delete` (returns the S3 key when the blob became unreferenced), `GetVolumes`, `PutVolumes`, `BlobKey`; the nine endpoints below.
- Endpoints: `GET /v1/assets?limit&after&kind → Page<AssetDto>`; `POST /v1/assets → 201 CreateAssetResponse`; `GET /v1/assets/{id}`; `PATCH /v1/assets/{id}`; `DELETE /v1/assets/{id} → 204`; `POST /v1/assets/{id}/confirm → ConfirmResponse`; `GET /v1/assets/{id}/download → DownloadResponse` (`409` until confirmed); `GET /v1/assets/{id}/volumes → TerrainVolume[]`; `PUT /v1/assets/{id}/volumes → TerrainVolume[]` (`409` unless kind is terrain).
- Rules: `hash` is 64 lowercase hex (SHA-256 the client computed); miniature and terrain accept only `model/gltf-binary`, texture `image/png`, `image/jpeg` or `image/webp`, anything else `415`; a miniature carries a `ModelledBase`, the other kinds do not (`schema.md`'s check) — `POST` reads that from the request, `PATCH` from the stored asset, and a `modelledBase` patched onto terrain or a texture is `422`. Confirm: `Size(key)` null → `409`; size ≠ `byteSize` → the object is deleted and `422`; for a GLB, a wrong magic → the object is deleted and `422` (an STL renamed `.glb` is rejected here); then the JSON chunk is read and `Triangles` counted, a declared chunk length above `GlbHeader.MaxJsonChunkBytes` and anything malformed past the magic going the same way — delete the object and `422`, because everything after the header read is uploader-controlled bytes parsed as a length-prefixed JSON document; `Warning` is set above `GlbHeader.WarnAbove` and nothing is refused. Confirm on an already-uploaded blob (the dedup case) still runs the header read so the caller gets the count.

Spec: management API → Asset bytes never pass through the API (the three endpoints; "a duplicate upload be short-circuited before any bytes move"), The triangle count, GLB only, The rest of the surface (assets, volumes); asset library → Two records, The modelled base suggests; identity and enumerations → `ASSET.kind`, profiles per kind ("texture: nothing"); geometry values via the contracts plan's `Volume`; mesh conversion → What it does not do (the browser writes the bounding-box volume with `PUT /v1/assets/{id}/volumes` at confirm).

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using System.Net.Http.Json;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Http;

namespace Tabletop.Management.Tests;

public class AssetTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public AssetTests(ManagementFactory f) => _f = f;

    static string Sha(byte[] b) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(b));
    static CreateAssetRequest Mini(byte[] glb, string name = "Marine") =>
        new(name, AssetKind.Miniature, Sha(glb), glb.Length, "model/gltf-binary", 1.0, new Orientation(0, 0, 0), Base.Circle(1.26));

    async Task<AssetDto> Upload(HttpClient c, byte[] glb, string name = "Marine")
    {
        var created = await c.PostJson<CreateAssetResponse>("/v1/assets", Mini(glb, name));
        if (created!.UploadUrl is not null)
            using (var http = new HttpClient()) Assert.True((await http.PutAsync(created.UploadUrl, new ByteArrayContent(glb) { Headers = { ContentType = new("model/gltf-binary") } })).IsSuccessStatusCode);
        var confirmed = await c.PostJson<ConfirmResponse>($"/v1/assets/{created.Asset.Id}/confirm", new { });
        return confirmed!.Asset;
    }

    [Fact]
    public async Task Create_upload_confirm_download()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var glb = TestGlb.Build((36, null, true));
        var created = await c.PostJson<CreateAssetResponse>("/v1/assets", Mini(glb));
        Assert.NotNull(created!.UploadUrl); Assert.False(created.Asset.Uploaded);
        Assert.Equal(HttpStatusCode.Conflict, (await c.GetAsync($"/v1/assets/{created.Asset.Id}/download")).StatusCode);
        using (var http = new HttpClient()) await http.PutAsync(created.UploadUrl, new ByteArrayContent(glb) { Headers = { ContentType = new("model/gltf-binary") } });
        var confirmed = await c.PostJson<ConfirmResponse>($"/v1/assets/{created.Asset.Id}/confirm", new { });
        Assert.Equal(12, confirmed!.Triangles); Assert.Null(confirmed.Warning); Assert.True(confirmed.Asset.Uploaded);
        var dl = await c.GetJson<DownloadResponse>($"/v1/assets/{created.Asset.Id}/download");
        using var http2 = new HttpClient();
        Assert.Equal(glb, await http2.GetByteArrayAsync(dl!.Url));
    }

    [Fact]
    public async Task Same_bytes_twice_short_circuits_the_upload_and_deleting_the_last_reference_removes_the_blob()
    {
        var owner = Guid.CreateVersion7();
        var c = await _f.AuthenticatedClient(owner);
        var glb = TestGlb.Build((3, null, true));
        var first = await Upload(c, glb, "A");
        var second = await c.PostJson<CreateAssetResponse>("/v1/assets", Mini(glb, "B"));
        Assert.Null(second!.UploadUrl); Assert.True(second.Asset.Uploaded);
        var store = _f.Services.GetRequiredService<IBlobStore>();
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/v1/assets/{first.Id}")).StatusCode);
        Assert.NotNull(await store.Size(IBlobStore.Key(owner, first.Hash)));
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/v1/assets/{second.Asset.Id}")).StatusCode);
        Assert.Null(await store.Size(IBlobStore.Key(owner, first.Hash)));
    }

    [Fact]
    public async Task Stl_is_rejected_and_a_dense_glb_only_warns()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var stl = "solid cube\nendsolid cube\n"u8.ToArray();
        var req = Mini(stl) with { Name = "not a glb" };
        var created = await c.PostJson<CreateAssetResponse>("/v1/assets", req);
        using (var http = new HttpClient()) await http.PutAsync(created!.UploadUrl, new ByteArrayContent(stl) { Headers = { ContentType = new("model/gltf-binary") } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsync($"/v1/assets/{created.Asset.Id}/confirm", null)).StatusCode);
        var dense = await c.PostJson<CreateAssetResponse>("/v1/assets", Mini(TestGlb.Build((300_003, null, true)), "dense"));
        using (var http = new HttpClient()) await http.PutAsync(dense!.UploadUrl, new ByteArrayContent(TestGlb.Build((300_003, null, true))) { Headers = { ContentType = new("model/gltf-binary") } });
        var confirmed = await c.PostJson<ConfirmResponse>($"/v1/assets/{dense.Asset.Id}/confirm", new { });
        Assert.Equal(100_001, confirmed!.Triangles); Assert.Contains("100,000", confirmed.Warning); Assert.True(confirmed.Asset.Uploaded);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await c.PostAsJsonAsync("/v1/assets", Mini(stl) with { ContentType = "model/stl" }, ContentJson.Options)).StatusCode);
    }

    [Fact]
    public async Task Library_is_owner_only_and_paged()
    {
        var mine = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var theirs = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++) ids.Add((await Upload(mine, TestGlb.Build((3 * (i + 1), null, true)), $"m{i}")).Id);
        var page1 = await mine.GetJson<Page<AssetDto>>("/v1/assets?limit=2");
        Assert.Equal(2, page1!.Items.Count); Assert.Equal(ids[1], page1.Next);
        var page2 = await mine.GetJson<Page<AssetDto>>($"/v1/assets?limit=2&after={page1.Next}");
        Assert.Equal([ids[2]], page2!.Items.Select(a => a.Id)); Assert.Null(page2.Next);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.GetAsync($"/v1/assets/{ids[0]}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.PatchJson($"/v1/assets/{ids[0]}", new PatchAssetRequest("x", null, null, null))).StatusCode);
        var patched = await mine.PatchJson<AssetDto>($"/v1/assets/{ids[0]}", new PatchAssetRequest("Renamed", 1.5, null, null));
        Assert.Equal(("Renamed", 1.5), (patched!.Name, patched.Scale));
    }

    [Fact]
    public async Task Terrain_volumes_round_trip_and_other_kinds_refuse_them()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var glb = TestGlb.Build((30, null, true));
        var terrain = await c.PostJson<CreateAssetResponse>("/v1/assets", Mini(glb, "Ruin") with { Kind = AssetKind.Terrain, ModelledBase = null });
        var volumes = new[] { new Volume(VolumeKind.Solid, Footprint.Rect(6, 4), new Point2(0, 0), new Yaw(0), 0, 3, TerrainDeclarations.BlocksLineOfSight | TerrainDeclarations.Cover, "ruined wall") };
        var saved = await c.PutJson<List<TerrainVolume>>($"/v1/assets/{terrain!.Asset.Id}/volumes", volumes);
        Assert.Single(saved!); Assert.Equal(volumes[0], saved[0].Volume);
        Assert.Equal(saved, await c.GetJson<List<TerrainVolume>>($"/v1/assets/{terrain.Asset.Id}/volumes"));
        var mini = await c.PostJson<CreateAssetResponse>("/v1/assets", Mini(glb, "Mini"));
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/v1/assets/{mini!.Asset.Id}/volumes", volumes, ContentJson.Options)).StatusCode);
    }
}
```

The tests above use `HttpJson.cs`, written now and used by every later test — the API's JSON conventions are `ContentJson.Options`, and the defaults would not read `"kind":"miniature"` into `AssetKind`:

```csharp
using System.Net.Http.Json;
using Tabletop.Content;

namespace Tabletop.Management.Tests;

public static class HttpJson
{
    public static async Task<T> PostJson<T>(this HttpClient c, string url, object body) => await Read<T>(await c.PostAsJsonAsync(url, body, ContentJson.Options));
    public static async Task<T> PutJson<T>(this HttpClient c, string url, object body) => await Read<T>(await c.PutAsJsonAsync(url, body, ContentJson.Options));
    public static async Task<T> PatchJson<T>(this HttpClient c, string url, object body) => await Read<T>(await c.PatchAsJsonAsync(url, body, ContentJson.Options));
    public static Task<HttpResponseMessage> PatchJson(this HttpClient c, string url, object body) => c.PatchAsJsonAsync(url, body, ContentJson.Options);
    public static async Task<T> GetJson<T>(this HttpClient c, string url) => await Read<T>(await c.GetAsync(url));

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(ContentJson.Options))!;
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `AssetDtos.cs` and `Content/VolumeRows.cs`:**

```csharp
using Tabletop.Geometry;

namespace Tabletop.Management.Content.Assets;

public enum AssetKind { Miniature, Terrain, Texture }

public sealed record AssetDto(Guid Id, string Name, AssetKind Kind, string Hash, long ByteSize, string ContentType, bool Uploaded, double Scale, Orientation Orientation, Base? ModelledBase);
public sealed record CreateAssetRequest(string Name, AssetKind Kind, string Hash, long ByteSize, string ContentType, double Scale, Orientation Orientation, Base? ModelledBase);
public sealed record CreateAssetResponse(AssetDto Asset, Uri? UploadUrl);
public sealed record ConfirmResponse(AssetDto Asset, long? Triangles, string? Warning);
public sealed record PatchAssetRequest(string? Name, double? Scale, Orientation? Orientation, Base? ModelledBase);
public sealed record DownloadResponse(Uri Url, DateTimeOffset ExpiresAt);

public static class AssetRules
{
    public static readonly IReadOnlyDictionary<AssetKind, string[]> ContentTypes = new Dictionary<AssetKind, string[]>
    {
        [AssetKind.Miniature] = ["model/gltf-binary"], [AssetKind.Terrain] = ["model/gltf-binary"], [AssetKind.Texture] = ["image/png", "image/jpeg", "image/webp"],
    };
    public static bool IsSha256(string hash) => hash.Length == 64 && hash.All(ch => ch is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static string Kind(AssetKind k) => k.ToString().ToLowerInvariant();
    public static AssetKind Kind(string k) => Enum.Parse<AssetKind>(k, ignoreCase: true);
    public static string Shape(BaseShape s) => s.ToString().ToLowerInvariant();
    public static BaseShape Shape(string s) => Enum.Parse<BaseShape>(s, ignoreCase: true);
}
```

```csharp
using Tabletop.Geometry;

namespace Tabletop.Management.Content;

/// <summary>asset_volume and terrain_volume share these columns. real columns are cast to float8 in SQL so they map to double.</summary>
public sealed record VolumeRow(Guid Id, string Kind, string Footprint, double DimA, double DimB, double OffsetX, double OffsetY, double Yaw, double Bottom, double Top,
    bool Impassable, bool BlocksLos, bool SeeInOut, bool Cover, bool Difficult, bool Dangerous, string? Notes)
{
    public const string Columns = "id as Id, kind as Kind, footprint as Footprint, dim_a::float8 as DimA, dim_b::float8 as DimB, offset_x::float8 as OffsetX, offset_y::float8 as OffsetY, yaw::float8 as Yaw, bottom::float8 as Bottom, top::float8 as Top, impassable as Impassable, blocks_los as BlocksLos, see_in_out as SeeInOut, cover as Cover, difficult as Difficult, dangerous as Dangerous, notes as Notes";
    public const string InsertColumns = "id, kind, footprint, dim_a, dim_b, offset_x, offset_y, yaw, bottom, top, impassable, blocks_los, see_in_out, cover, difficult, dangerous, notes";
    public const string InsertValues = "@Id, @Kind, @Footprint, @DimA, @DimB, @OffsetX, @OffsetY, @Yaw, @Bottom, @Top, @Impassable, @BlocksLos, @SeeInOut, @Cover, @Difficult, @Dangerous, @Notes";

    public Volume ToVolume()
    {
        var d = TerrainDeclarations.None;
        if (Impassable) d |= TerrainDeclarations.Impassable; if (BlocksLos) d |= TerrainDeclarations.BlocksLineOfSight; if (SeeInOut) d |= TerrainDeclarations.SeeInAndOut;
        if (Cover) d |= TerrainDeclarations.Cover; if (Difficult) d |= TerrainDeclarations.Difficult; if (Dangerous) d |= TerrainDeclarations.Dangerous;
        return new Volume(Kind == "solid" ? VolumeKind.Solid : VolumeKind.Area, new Footprint(Footprint == "circle" ? FootprintKind.Circle : FootprintKind.Rect, DimA, DimB),
            new Point2(OffsetX, OffsetY), new Yaw(Yaw), Bottom, Top, d, Notes);
    }

    public static VolumeRow From(Guid id, Volume v) => new(id, v.Kind == VolumeKind.Solid ? "solid" : "area", v.Footprint.Kind == FootprintKind.Circle ? "circle" : "rect",
        v.Footprint.A, v.Footprint.B, v.Offset.X, v.Offset.Y, v.Yaw.Degrees, v.Bottom, v.Top,
        v.Declarations.HasFlag(TerrainDeclarations.Impassable), v.Declarations.HasFlag(TerrainDeclarations.BlocksLineOfSight), v.Declarations.HasFlag(TerrainDeclarations.SeeInAndOut),
        v.Declarations.HasFlag(TerrainDeclarations.Cover), v.Declarations.HasFlag(TerrainDeclarations.Difficult), v.Declarations.HasFlag(TerrainDeclarations.Dangerous), v.Notes);
}
```

- [ ] **Step 3: Write `AssetRepository.cs`**

```csharp
using Dapper;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Persistence;

namespace Tabletop.Management.Content.Assets;

public sealed class AssetRepository(Database db)
{
    private sealed record Row(Guid Id, Guid OwnerId, string Name, string Kind, string BlobHash, long ByteSize, string ContentType, bool Uploaded, double Scale,
        double OrientationPitch, double OrientationYaw, double OrientationRoll, string? ModelledBaseShape, double? ModelledBaseA, double? ModelledBaseB)
    {
        public AssetDto ToDto() => new(Id, Name, AssetRules.Kind(Kind), BlobHash, ByteSize, ContentType, Uploaded, Scale, new Orientation(OrientationPitch, OrientationYaw, OrientationRoll),
            ModelledBaseShape is null ? null : new Base(AssetRules.Shape(ModelledBaseShape), ModelledBaseA!.Value, ModelledBaseB!.Value));
    }

    private const string Select = @"select a.id as Id, a.owner_id as OwnerId, a.name as Name, a.kind as Kind, a.blob_hash as BlobHash, b.byte_size as ByteSize, b.content_type as ContentType,
        b.uploaded as Uploaded, a.scale::float8 as Scale, a.orientation_pitch::float8 as OrientationPitch, a.orientation_yaw::float8 as OrientationYaw, a.orientation_roll::float8 as OrientationRoll,
        a.modelled_base_shape as ModelledBaseShape, a.modelled_base_a::float8 as ModelledBaseA, a.modelled_base_b::float8 as ModelledBaseB
        from asset a join blob b on b.owner_id = a.owner_id and b.hash = a.blob_hash";

    public async Task<AssetDto> Create(Guid ownerId, CreateAssetRequest r)
    {
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        await c.ExecuteAsync("insert into blob (owner_id, hash, s3_key, byte_size, content_type) values (@ownerId, @hash, @key, @size, @ct) on conflict (owner_id, hash) do nothing",
            new { ownerId, hash = r.Hash, key = IBlobStore.Key(ownerId, r.Hash), size = r.ByteSize, ct = r.ContentType }, tx);
        var id = Guid.CreateVersion7();
        await c.ExecuteAsync(@"insert into asset (id, owner_id, blob_hash, name, kind, scale, orientation_pitch, orientation_yaw, orientation_roll, modelled_base_shape, modelled_base_a, modelled_base_b)
            values (@id, @ownerId, @hash, @name, @kind, @scale, @p, @y, @r, @shape, @a, @b)",
            new { id, ownerId, hash = r.Hash, name = r.Name, kind = AssetRules.Kind(r.Kind), scale = r.Scale, p = r.Orientation.Pitch, y = r.Orientation.Yaw, r = r.Orientation.Roll,
                  shape = r.ModelledBase is { } mb ? AssetRules.Shape(mb.Shape) : null, a = r.ModelledBase?.A, b = r.ModelledBase?.B }, tx);
        await tx.CommitAsync();
        return (await Get(ownerId, id))!;
    }

    public async Task<AssetDto?> Get(Guid ownerId, Guid id)
    {
        await using var c = await db.Open();
        return (await c.QuerySingleOrDefaultAsync<Row>(Select + " where a.id = @id and a.owner_id = @ownerId", new { id, ownerId }))?.ToDto();
    }

    public async Task<IReadOnlyList<AssetDto>> List(Guid ownerId, Http.Keyset page, AssetKind? kind)
    {
        await using var c = await db.Open();
        var rows = await c.QueryAsync<Row>(Select + " where a.owner_id = @ownerId and (@after::uuid is null or a.id > @after::uuid) and (@kind::text is null or a.kind = @kind::text) order by a.id limit @limit",
            new { ownerId, after = page.After, kind = kind is null ? null : AssetRules.Kind(kind.Value), limit = page.Limit });
        return rows.Select(r => r.ToDto()).ToList();
    }

    public async Task<AssetDto?> Patch(Guid ownerId, Guid id, PatchAssetRequest p)
    {
        await using var c = await db.Open();
        var n = await c.ExecuteAsync(@"update asset set name = coalesce(@name, name), scale = coalesce(@scale, scale),
            orientation_pitch = coalesce(@p, orientation_pitch), orientation_yaw = coalesce(@y, orientation_yaw), orientation_roll = coalesce(@r, orientation_roll),
            modelled_base_shape = coalesce(@shape, modelled_base_shape), modelled_base_a = coalesce(@a, modelled_base_a), modelled_base_b = coalesce(@b, modelled_base_b)
            where id = @id and owner_id = @ownerId",
            new { id, ownerId, name = p.Name, scale = p.Scale, p = p.Orientation?.Pitch, y = p.Orientation?.Yaw, r = p.Orientation?.Roll,
                  shape = p.ModelledBase is { } mb ? AssetRules.Shape(mb.Shape) : null, a = p.ModelledBase?.A, b = p.ModelledBase?.B });
        return n == 1 ? await Get(ownerId, id) : null;
    }

    public async Task MarkUploaded(Guid ownerId, string hash)
    {
        await using var c = await db.Open();
        await c.ExecuteAsync("update blob set uploaded = true where owner_id = @ownerId and hash = @hash", new { ownerId, hash });
    }

    /// <summary>Deletes the asset; when it was the owner's last reference to the blob, deletes the blob row too and returns its S3 key.</summary>
    public async Task<(bool Found, string? OrphanedKey)> Delete(Guid ownerId, Guid id)
    {
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        var hash = await c.ExecuteScalarAsync<string?>("delete from asset where id = @id and owner_id = @ownerId returning blob_hash", new { id, ownerId }, tx);
        if (hash is null) return (false, null);
        var key = await c.ExecuteScalarAsync<string?>(@"delete from blob where owner_id = @ownerId and hash = @hash
            and not exists (select 1 from asset where owner_id = @ownerId and blob_hash = @hash) returning s3_key", new { ownerId, hash }, tx);
        await tx.CommitAsync();
        return (true, key);
    }

    public async Task<IReadOnlyList<TerrainVolume>> GetVolumes(Guid ownerId, Guid assetId)
    {
        await using var c = await db.Open();
        var rows = await c.QueryAsync<VolumeRow>($"select {VolumeRow.Columns} from asset_volume where asset_id = @assetId and exists (select 1 from asset where id = @assetId and owner_id = @ownerId) order by id", new { assetId, ownerId });
        return rows.Select(r => new TerrainVolume(r.Id, r.ToVolume())).ToList();
    }

    public async Task<IReadOnlyList<TerrainVolume>> PutVolumes(Guid assetId, IReadOnlyList<Volume> volumes)
    {
        var rows = volumes.Select(v => VolumeRow.From(Guid.CreateVersion7(), v)).ToList();
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        await c.ExecuteAsync("delete from asset_volume where asset_id = @assetId", new { assetId }, tx);
        foreach (var r in rows)
            await c.ExecuteAsync($"insert into asset_volume (asset_id, {VolumeRow.InsertColumns}) values (@AssetId, {VolumeRow.InsertValues})", new { AssetId = assetId, r.Id, r.Kind, r.Footprint, r.DimA, r.DimB, r.OffsetX, r.OffsetY, r.Yaw, r.Bottom, r.Top, r.Impassable, r.BlocksLos, r.SeeInOut, r.Cover, r.Difficult, r.Dangerous, r.Notes }, tx);
        await tx.CommitAsync();
        return rows.Select(r => new TerrainVolume(r.Id, r.ToVolume())).ToList();
    }
}
```

- [ ] **Step 4: Write `AssetEndpoints.cs`**

```csharp
using System.Security.Claims;
using System.Text.Json;
using Tabletop.Geometry;
using Tabletop.Management.Http;

namespace Tabletop.Management.Content.Assets;

public static class AssetEndpoints
{
    private static readonly TimeSpan UploadTtl = TimeSpan.FromMinutes(15), DownloadTtl = TimeSpan.FromMinutes(60);

    public static void MapAssets(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/assets").RequireAuthorization();

        g.MapGet("", async (ClaimsPrincipal user, AssetRepository assets, int? limit, Guid? after, AssetKind? kind) =>
        {
            var page = Keyset.Parse(limit, after);
            return Results.Ok(Page<AssetDto>.Of(await assets.List(CurrentAccount.Id(user), page, kind), page.Limit, a => a.Id));
        });

        g.MapPost("", async (ClaimsPrincipal user, CreateAssetRequest r, AssetRepository assets, IBlobStore blobs) =>
        {
            if (string.IsNullOrWhiteSpace(r.Name) || r.ByteSize <= 0 || !AssetRules.IsSha256(r.Hash)) return Problems.Unprocessable("name, byteSize and a 64-hex sha256 hash are required.");
            if (!AssetRules.ContentTypes[r.Kind].Contains(r.ContentType)) return Results.Problem(title: "Unsupported media type", detail: $"{r.Kind} accepts {string.Join(", ", AssetRules.ContentTypes[r.Kind])}. STL is not accepted; convert it first.", statusCode: StatusCodes.Status415UnsupportedMediaType);
            if ((r.Kind == AssetKind.Miniature) != (r.ModelledBase is not null)) return Problems.Unprocessable("A miniature carries a modelledBase; terrain and textures do not.");
            var owner = CurrentAccount.Id(user);
            var asset = await assets.Create(owner, r);
            var url = asset.Uploaded ? null : await blobs.PresignUpload(IBlobStore.Key(owner, r.Hash), r.ContentType, UploadTtl);
            return Results.Created($"/v1/assets/{asset.Id}", new CreateAssetResponse(asset, url));
        });

        g.MapGet("/{id:guid}", async (ClaimsPrincipal user, Guid id, AssetRepository assets) =>
            await assets.Get(CurrentAccount.Id(user), id) is { } a ? Results.Ok(a) : Problems.NotFound());

        g.MapPatch("/{id:guid}", async (ClaimsPrincipal user, Guid id, PatchAssetRequest p, AssetRepository assets) =>
        {
            var owner = CurrentAccount.Id(user);
            var existing = await assets.Get(owner, id);
            if (existing is null) return Problems.NotFound();
            if (p.ModelledBase is not null && existing.Kind != AssetKind.Miniature) return Problems.Unprocessable("A miniature carries a modelledBase; terrain and textures do not.");
            return await assets.Patch(owner, id, p) is { } a ? Results.Ok(a) : Problems.NotFound();
        });

        g.MapDelete("/{id:guid}", async (ClaimsPrincipal user, Guid id, AssetRepository assets, IBlobStore blobs) =>
        {
            var (found, orphaned) = await assets.Delete(CurrentAccount.Id(user), id);
            if (!found) return Problems.NotFound();
            if (orphaned is not null) await blobs.Delete([orphaned]);
            return Results.NoContent();
        });

        g.MapPost("/{id:guid}/confirm", async (ClaimsPrincipal user, Guid id, AssetRepository assets, IBlobStore blobs) =>
        {
            var owner = CurrentAccount.Id(user);
            var asset = await assets.Get(owner, id);
            if (asset is null) return Problems.NotFound();
            var key = IBlobStore.Key(owner, asset.Hash);
            var size = await blobs.Size(key);
            if (size is null) return Problems.Conflict("The bytes have not been uploaded yet.");
            if (size != asset.ByteSize) { await blobs.Delete([key]); return Problems.Unprocessable($"Uploaded {size} bytes, expected {asset.ByteSize}."); }
            long? triangles = null; string? warning = null;
            if (asset.ContentType == "model/gltf-binary")
            {
                try
                {
                    var head = await blobs.ReadRange(key, 0, GlbHeader.HeaderBytes - 1);
                    if (!GlbHeader.IsGlb(head)) { await blobs.Delete([key]); return Problems.Unprocessable("Not a GLB. STL and .gltf-with-external-buffers are not accepted."); }
                    var len = GlbHeader.JsonChunkLength(head);
                    if (len > GlbHeader.MaxJsonChunkBytes) { await blobs.Delete([key]); return Problems.Unprocessable($"The JSON chunk declares {len:N0} bytes, above the {GlbHeader.MaxJsonChunkBytes:N0} this server will read."); }
                    triangles = GlbHeader.CountTriangles(await blobs.ReadRange(key, GlbHeader.HeaderBytes, GlbHeader.HeaderBytes + len - 1));
                    if (triangles > GlbHeader.WarnAbove) warning = $"{triangles:N0} triangles is above the 100,000 this table renders comfortably; every participant downloads it.";
                }
                // The header read told us this is a GLB; everything past that is attacker-controlled bytes
                // read as a length-prefixed JSON document. Anything malformed here is a bad upload, not a bug.
                catch (Exception e) when (e is FormatException or JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or OverflowException)
                {
                    await blobs.Delete([key]);
                    return Problems.Unprocessable($"Malformed GLB: {e.Message}");
                }
            }
            await assets.MarkUploaded(owner, asset.Hash);
            return Results.Ok(new ConfirmResponse((await assets.Get(owner, id))!, triangles, warning));
        });

        g.MapGet("/{id:guid}/download", async (ClaimsPrincipal user, Guid id, AssetRepository assets, IBlobStore blobs) =>
        {
            var owner = CurrentAccount.Id(user);
            var asset = await assets.Get(owner, id);
            if (asset is null) return Problems.NotFound();
            if (!asset.Uploaded) return Problems.Conflict("Not confirmed yet.");
            return Results.Ok(new DownloadResponse(await blobs.PresignDownload(IBlobStore.Key(owner, asset.Hash), DownloadTtl), DateTimeOffset.UtcNow.Add(DownloadTtl)));
        });

        g.MapGet("/{id:guid}/volumes", async (ClaimsPrincipal user, Guid id, AssetRepository assets) =>
            await assets.Get(CurrentAccount.Id(user), id) is null ? Problems.NotFound() : Results.Ok(await assets.GetVolumes(CurrentAccount.Id(user), id)));

        g.MapPut("/{id:guid}/volumes", async (ClaimsPrincipal user, Guid id, IReadOnlyList<Volume> volumes, AssetRepository assets) =>
        {
            var asset = await assets.Get(CurrentAccount.Id(user), id);
            if (asset is null) return Problems.NotFound();
            if (asset.Kind != AssetKind.Terrain) return Problems.Conflict("Only terrain carries volumes.");
            return Results.Ok(await assets.PutVolumes(id, volumes));
        });
    }
}
```

- [ ] **Step 5: Wire `Program.cs`** — `builder.Services.AddSingleton<Content.Assets.AssetRepository>();` and `app.MapAssets();`.

- [ ] **Step 6: Run `dotnet test tests/Tabletop.Management.Tests`; expect at least 42 passed. Commit: `Add the asset library: presigned upload and download, confirm with the header read, volumes`.**
