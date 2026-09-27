# Task 7: The content-addressed asset cache

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
