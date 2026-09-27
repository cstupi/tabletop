# Task 6: The blob store and the GLB header read

**Files:**
- Create: `server/management/Content/Assets/IBlobStore.cs`, `server/management/Content/Assets/S3BlobStore.cs`, `server/management/Content/Assets/GlbHeader.cs`
- Modify: `server/management/Program.cs`
- Create: `tests/Tabletop.Management.Tests/TestGlb.cs`, `tests/Tabletop.Management.Tests/GlbHeaderTests.cs`, `tests/Tabletop.Management.Tests/BlobStoreTests.cs`

**Interfaces:**
- Consumes: `TABLETOP_S3_*` from `.env` (MinIO at `http://localhost:9000`, bucket `tabletop-blobs`, path-style — contracts plan Task 15).
- Produces: `IBlobStore` with `static string Key(Guid ownerId, string hash) => $"{ownerId}/{hash}"`, `Task<Uri> PresignUpload(string key, string contentType, TimeSpan ttl)`, `Task<Uri> PresignDownload(string key, TimeSpan ttl)`, `Task<long?> Size(string key)` (null when absent), `Task<byte[]> ReadRange(string key, long first, long lastInclusive)`, `Task Delete(IReadOnlyList<string> keys)`; `S3BlobStore(ManagementOptions)`; `GlbHeader.IsGlb(ReadOnlySpan<byte> first12)`, `GlbHeader.JsonChunkLength(ReadOnlySpan<byte> first20) : int`, `GlbHeader.CountTriangles(ReadOnlySpan<byte> jsonChunk) : long`, `GlbHeader.HeaderBytes = 20`, `GlbHeader.WarnAbove = 100_000`, `GlbHeader.MaxJsonChunkBytes = 8 MiB`.
- The triangle total is the sum over every mesh primitive of `accessors[indices ?? attributes.POSITION].count` converted by `mode` (4 → `count/3`; 5 and 6 → `count − 2`; points and lines → 0). Meshes are counted once each, not per node, because that is what a client loads into memory.
- `MaxJsonChunkBytes` is 8 MiB: the JSON chunk is buffered whole to be parsed, so a chunk declaring more than that is refused rather than read — `422` at confirm (Task 7).
- Presigned URLs are minted against `TABLETOP_S3_ENDPOINT` exactly, so the same URL works from a WSL process and the Windows client at `localhost:9000` (contracts plan → Local development). Upload URLs are valid 15 minutes; download URLs 60.
- `PresignUpload` takes no byte size. An S3 query-string presigned `PUT` signs a fixed set of headers and carries no content-length condition — `content-length-range` exists only for POST-policy uploads — so the confirm handler's size check (Task 7) is the only point where the uploaded length is enforced.
- **Single-object `PUT` only.** The management API's *Still open* — *"whether asset uploads need multipart for large files, which presigned `PUT` does not cover on its own"* — stays open and is not pre-empted here: `IBlobStore` has no multipart member, and adding one later is a second presign method plus a complete-upload call, neither of which changes the three endpoints. A GLB the converter produced is a few megabytes, well inside a single `PUT`.

Spec: management API → Asset bytes never pass through the API, The triangle count is a ranged read of the GLB header ("A GLB's first chunk is its JSON … the triangle total is a sum over the index accessors"), GLB only; mesh conversion → The threshold (100,000; never blocks); `architecture.md` → GLB only.

- [ ] **Step 1: Write `TestGlb.cs` and the failing unit tests**

```csharp
using System.Text;

namespace Tabletop.Management.Tests;

/// <summary>Builds a structurally valid GLB whose JSON chunk declares the given primitives. The binary chunk is empty.</summary>
public static class TestGlb
{
    public static byte[] Build(params (int count, int? mode, bool indexed)[] primitives)
    {
        var accessors = string.Join(",", primitives.Select(p => $"{{\"componentType\":5123,\"count\":{p.count},\"type\":\"SCALAR\"}}"));
        var prims = string.Join(",", primitives.Select((p, i) =>
            (p.indexed ? $"{{\"attributes\":{{\"POSITION\":{i}}},\"indices\":{i}" : $"{{\"attributes\":{{\"POSITION\":{i}}}") + (p.mode is null ? "}" : $",\"mode\":{p.mode}}}")));
        var json = Encoding.UTF8.GetBytes($"{{\"asset\":{{\"version\":\"2.0\"}},\"accessors\":[{accessors}],\"meshes\":[{{\"primitives\":[{prims}]}}]}}");
        var padded = json.Length % 4 == 0 ? json : json.Concat(Enumerable.Repeat((byte)0x20, 4 - json.Length % 4)).ToArray();
        using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
        w.Write(0x46546C67u); w.Write(2u); w.Write((uint)(12 + 8 + padded.Length));
        w.Write((uint)padded.Length); w.Write(0x4E4F534Au); w.Write(padded);
        return ms.ToArray();
    }
}
```

```csharp
using Tabletop.Management.Content.Assets;

namespace Tabletop.Management.Tests;

public class GlbHeaderTests
{
    [Fact]
    public void Recognises_the_magic_and_reads_the_json_length()
    {
        var glb = TestGlb.Build((36, null, true));
        Assert.True(GlbHeader.IsGlb(glb.AsSpan(0, 12)));
        Assert.False(GlbHeader.IsGlb("solid cube\n\n"u8));
        Assert.Equal(BitConverter.ToInt32(glb, 12), GlbHeader.JsonChunkLength(glb.AsSpan(0, 20)));
    }

    [Theory]
    [InlineData(36, null, true, 12)]      // indexed triangles
    [InlineData(30, 4, false, 10)]        // unindexed triangles from POSITION count
    [InlineData(10, 5, true, 8)]          // triangle strip
    [InlineData(10, 6, true, 8)]          // triangle fan
    [InlineData(10, 1, true, 0)]          // lines count nothing
    public void Counts_triangles_per_mode(int count, int? mode, bool indexed, long expected)
    {
        var glb = TestGlb.Build((count, mode, indexed));
        var len = GlbHeader.JsonChunkLength(glb.AsSpan(0, 20));
        Assert.Equal(expected, GlbHeader.CountTriangles(glb.AsSpan(20, len)));
    }

    [Fact]
    public void Sums_over_primitives()
    {
        var glb = TestGlb.Build((300_003, null, true), (3, null, true));
        Assert.Equal(100_002, GlbHeader.CountTriangles(glb.AsSpan(20, GlbHeader.JsonChunkLength(glb.AsSpan(0, 20)))));
        Assert.True(100_002 > GlbHeader.WarnAbove);
    }
}
```

- [ ] **Step 2: Write the failing MinIO test**

```csharp
using Microsoft.Extensions.Configuration;
using Tabletop.Management.Configuration;
using Tabletop.Management.Content.Assets;

namespace Tabletop.Management.Tests;

public class BlobStoreTests : IAsyncLifetime
{
    private readonly S3BlobStore _store;
    private readonly string _key = IBlobStore.Key(Guid.CreateVersion7(), "sha256-test");
    public BlobStoreTests() { DotEnv.Load(); _store = new S3BlobStore(ManagementOptions.From(new ConfigurationBuilder().AddEnvironmentVariables("TABLETOP_").AddInMemoryCollection(new Dictionary<string, string?> { ["STYTCH_PROJECT_ID"] = "p", ["STYTCH_SECRET"] = "s" }).Build())); }

    [Fact]
    public async Task Presigned_put_then_ranged_read_then_presigned_get()
    {
        var glb = TestGlb.Build((36, null, true));
        Assert.Null(await _store.Size(_key));
        using var http = new HttpClient();
        var put = await http.PutAsync(await _store.PresignUpload(_key, "model/gltf-binary", TimeSpan.FromMinutes(5)), new ByteArrayContent(glb) { Headers = { ContentType = new("model/gltf-binary") } });
        Assert.True(put.IsSuccessStatusCode, await put.Content.ReadAsStringAsync());
        Assert.Equal(glb.Length, await _store.Size(_key));
        var head = await _store.ReadRange(_key, 0, GlbHeader.HeaderBytes - 1);
        Assert.Equal(GlbHeader.HeaderBytes, head.Length);
        Assert.True(GlbHeader.IsGlb(head.AsSpan(0, 12)));
        var got = await http.GetByteArrayAsync(await _store.PresignDownload(_key, TimeSpan.FromMinutes(5)));
        Assert.Equal(glb, got);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => _store.Delete([_key]);
}
```

`Tabletop.Management.Tests.csproj` gains `<PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="10.0.11" />` (already transitive via Mvc.Testing; the explicit reference keeps the `using` resolvable).

- [ ] **Step 3: Run; expect compile errors. Write `GlbHeader.cs`:**

```csharp
using System.Buffers.Binary;
using System.Text.Json;

namespace Tabletop.Management.Content.Assets;

/// <summary>The only server-side look at an upload: the 12-byte header, the 8-byte chunk header, then the JSON chunk.</summary>
public static class GlbHeader
{
    public const uint Magic = 0x46546C67;       // "glTF"
    public const uint JsonChunkType = 0x4E4F534A; // "JSON"
    public const int HeaderBytes = 20;
    public const long WarnAbove = 100_000;
    /// <summary>A glTF JSON chunk has no business being bigger than this; above it we refuse rather than buffer it.</summary>
    public const int MaxJsonChunkBytes = 8 * 1024 * 1024;

    public static bool IsGlb(ReadOnlySpan<byte> first12) =>
        first12.Length >= 12 && BinaryPrimitives.ReadUInt32LittleEndian(first12) == Magic && BinaryPrimitives.ReadUInt32LittleEndian(first12[4..]) == 2;

    public static int JsonChunkLength(ReadOnlySpan<byte> first20)
    {
        if (first20.Length < HeaderBytes || BinaryPrimitives.ReadUInt32LittleEndian(first20[16..]) != JsonChunkType)
            throw new FormatException("First chunk is not JSON.");
        return checked((int)BinaryPrimitives.ReadUInt32LittleEndian(first20[12..]));
    }

    public static long CountTriangles(ReadOnlySpan<byte> jsonChunk)
    {
        using var doc = JsonDocument.Parse(jsonChunk.ToArray());
        var root = doc.RootElement;
        if (!root.TryGetProperty("meshes", out var meshes) || !root.TryGetProperty("accessors", out var accessors)) return 0;
        long total = 0;
        foreach (var mesh in meshes.EnumerateArray())
            foreach (var prim in mesh.GetProperty("primitives").EnumerateArray())
            {
                var mode = prim.TryGetProperty("mode", out var m) ? m.GetInt32() : 4;
                int accessor = prim.TryGetProperty("indices", out var idx) ? idx.GetInt32()
                    : prim.TryGetProperty("attributes", out var attrs) && attrs.TryGetProperty("POSITION", out var pos) ? pos.GetInt32() : -1;
                if (accessor < 0) continue;
                long count = accessors[accessor].GetProperty("count").GetInt64();
                total += mode switch { 4 => count / 3, 5 or 6 => Math.Max(0, count - 2), _ => 0 };
            }
        return total;
    }
}
```

- [ ] **Step 4: Write `IBlobStore.cs` and `S3BlobStore.cs`**

```csharp
namespace Tabletop.Management.Content.Assets;

/// <summary>Content-addressed bytes, per owner. Presigned URLs both ways; the server itself reads only ranges.</summary>
public interface IBlobStore
{
    static string Key(Guid ownerId, string hash) => $"{ownerId}/{hash}";
    Task<Uri> PresignUpload(string key, string contentType, TimeSpan ttl);
    Task<Uri> PresignDownload(string key, TimeSpan ttl);
    Task<long?> Size(string key);
    Task<byte[]> ReadRange(string key, long first, long lastInclusive);
    Task Delete(IReadOnlyList<string> keys);
}
```

```csharp
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Tabletop.Management.Configuration;

namespace Tabletop.Management.Content.Assets;

public sealed class S3BlobStore(ManagementOptions options) : IBlobStore
{
    private readonly AmazonS3Client _s3 = new(new BasicAWSCredentials(options.S3AccessKey, options.S3SecretKey),
        new AmazonS3Config { ServiceURL = options.S3Endpoint, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
    private readonly bool _http = options.S3Endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    // The upload size is not enforced here: S3 query-string (SigV4) presigned PUT URLs sign a fixed set of
    // headers and cannot carry a content-length condition — that mechanism (content-length-range)
    // exists only for POST-policy uploads. The confirm handler's size check is the only guard.
    public async Task<Uri> PresignUpload(string key, string contentType, TimeSpan ttl) => new(await _s3.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        { BucketName = options.S3Bucket, Key = key, Verb = HttpVerb.PUT, ContentType = contentType, Expires = DateTime.UtcNow.Add(ttl), Protocol = _http ? Amazon.S3.Protocol.HTTP : Amazon.S3.Protocol.HTTPS }));

    public async Task<Uri> PresignDownload(string key, TimeSpan ttl) => new(await _s3.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        { BucketName = options.S3Bucket, Key = key, Verb = HttpVerb.GET, Expires = DateTime.UtcNow.Add(ttl), Protocol = _http ? Amazon.S3.Protocol.HTTP : Amazon.S3.Protocol.HTTPS }));

    public async Task<long?> Size(string key)
    {
        try { return (await _s3.GetObjectMetadataAsync(options.S3Bucket, key)).ContentLength; }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound) { return null; }
    }

    public async Task<byte[]> ReadRange(string key, long first, long lastInclusive)
    {
        using var response = await _s3.GetObjectAsync(new GetObjectRequest { BucketName = options.S3Bucket, Key = key, ByteRange = new ByteRange(first, lastInclusive) });
        using var ms = new MemoryStream();
        await response.ResponseStream.CopyToAsync(ms);
        return ms.ToArray();
    }

    public async Task Delete(IReadOnlyList<string> keys)
    {
        foreach (var chunk in keys.Chunk(1000))
            await _s3.DeleteObjectsAsync(new DeleteObjectsRequest { BucketName = options.S3Bucket, Objects = chunk.Select(k => new KeyVersion { Key = k }).ToList() });
    }
}
```

- [ ] **Step 5: Wire `Program.cs`** — the store takes `ManagementOptions` from DI, which Task 1 registers as a factory and resolves once after `builder.Build()`:

```csharp
builder.Services.AddSingleton<Tabletop.Management.Content.Assets.IBlobStore>(sp => new Tabletop.Management.Content.Assets.S3BlobStore(sp.GetRequiredService<ManagementOptions>()));
```

- [ ] **Step 6: Run `dotnet test tests/Tabletop.Management.Tests`; expect at least 37 passed (MinIO must be up). Commit: `Add the S3 blob store over MinIO and the GLB header triangle count`.**
