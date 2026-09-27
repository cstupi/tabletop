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

    [Fact]
    public async Task Concurrent_fetches_of_the_same_hash_all_succeed()
    {
        var handler = new BytesHandler(Glb);
        var cache = new AssetCache(_dir, new HttpClient(handler));
        var hash = Hash(Glb);
        var tasks = Enumerable.Range(0, 6).Select(_ => cache.FetchAsync(hash, new Uri("http://localhost:9000/tabletop-blobs/o/h"), CancellationToken.None)).ToList();
        var results = await Task.WhenAll(tasks);
        Assert.All(results, path => Assert.Equal(cache.PathFor(hash), path));
        Assert.Equal(Glb, await File.ReadAllBytesAsync(cache.PathFor(hash)));
        Assert.Empty(Directory.GetFiles(_dir, "*.part"));
    }

    sealed class BytesHandler(byte[] bytes) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }); }
    }
}
