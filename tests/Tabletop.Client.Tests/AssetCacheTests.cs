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

    /// <summary>A blob of <paramref name="size"/> bytes, last used <paramref name="ago"/> before now.</summary>
    string Blob(AssetCache cache, char c, int size, TimeSpan ago)
    {
        var hash = new string(c, 64);
        File.WriteAllBytes(cache.PathFor(hash), new byte[size]);
        File.SetLastWriteTimeUtc(cache.PathFor(hash), DateTime.UtcNow - ago);
        return hash;
    }

    [Fact]
    public void Resolving_a_held_blob_marks_it_used()
    {
        var cache = new AssetCache(_dir, new HttpClient());
        var hash = Blob(cache, 'a', 1, TimeSpan.FromDays(9));
        Assert.Null(cache.Resolve(new string('b', 64)));
        Assert.Equal(cache.PathFor(hash), cache.Resolve(hash));
        Assert.True(File.GetLastWriteTimeUtc(cache.PathFor(hash)) > DateTime.UtcNow - TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Fetching_a_held_blob_marks_it_used()
    {
        var cache = new AssetCache(_dir, new HttpClient(new BytesHandler(Glb)));
        var hash = Hash(Glb);
        await File.WriteAllBytesAsync(cache.PathFor(hash), Glb);
        File.SetLastWriteTimeUtc(cache.PathFor(hash), DateTime.UtcNow - TimeSpan.FromDays(9));
        await cache.FetchAsync(hash, new Uri("http://localhost:9000/never"), CancellationToken.None);
        Assert.True(File.GetLastWriteTimeUtc(cache.PathFor(hash)) > DateTime.UtcNow - TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Under_the_limit_eviction_deletes_no_blob()
    {
        var cache = new AssetCache(_dir, new HttpClient());
        Blob(cache, 'a', 40, TimeSpan.FromDays(30)); Blob(cache, 'b', 40, TimeSpan.FromDays(1));
        Assert.Equal(0, cache.Evict(new HashSet<string>(), above: 100, to: 50));
        Assert.Equal(2, Directory.GetFiles(_dir, "*.bin").Length);
    }

    [Fact]
    public void Over_the_limit_eviction_deletes_the_least_recently_used_until_under_the_target_and_never_a_kept_one()
    {
        var cache = new AssetCache(_dir, new HttpClient());
        var oldest = Blob(cache, 'a', 30, TimeSpan.FromDays(40));
        var kept = Blob(cache, 'b', 30, TimeSpan.FromDays(30));
        var old = Blob(cache, 'c', 30, TimeSpan.FromDays(20));
        var recent = Blob(cache, 'd', 30, TimeSpan.FromDays(10));
        var newest = Blob(cache, 'e', 30, TimeSpan.FromDays(1));
        Assert.Equal(2, cache.Evict(new HashSet<string> { kept }, above: 100, to: 90));
        Assert.Equal([kept, recent, newest], [.. new[] { oldest, kept, old, recent, newest }.Where(cache.Has)]);
    }

    [Fact]
    public void Eviction_deletes_part_files_older_than_a_day()
    {
        var cache = new AssetCache(_dir, new HttpClient());
        var stale = Path.Combine(_dir, $"{new string('a', 64)}.x.part"); var fresh = Path.Combine(_dir, $"{new string('b', 64)}.y.part");
        File.WriteAllBytes(stale, [1]); File.WriteAllBytes(fresh, [1]);
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow - TimeSpan.FromHours(25));
        cache.Evict(new HashSet<string>());
        Assert.Equal([fresh], Directory.GetFiles(_dir, "*.part"));
    }

    [Fact]
    public void Eviction_of_a_missing_cache_does_nothing()
        => Assert.Equal(0, new AssetCache(Path.Combine(_dir, "none"), new HttpClient()).Evict(new HashSet<string>()));

    [Fact]
    public void Caps_follow_the_asset_kind()
    {
        Assert.Equal(64L << 20, AssetCache.CapFor(AssetKind.Miniature));
        Assert.Equal(64L << 20, AssetCache.CapFor(AssetKind.Terrain));
        Assert.Equal(16L << 20, AssetCache.CapFor(AssetKind.Texture));
        Assert.Equal(64L << 20, AssetCache.CapFor(null));
    }

    [Fact]
    public async Task A_declared_length_over_the_cap_is_refused_before_the_body_is_read()
    {
        var body = new CountingStream(200);
        var handler = new StreamHandler(body, declared: 200);
        var cache = new AssetCache(_dir, new HttpClient(handler));
        await Assert.ThrowsAsync<AssetTooLargeException>(() => cache.FetchAsync(new string('a', 64), new Uri("http://localhost:9000/x"), 100, CancellationToken.None));
        Assert.Equal(0, body.Served);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Theory]
    [InlineData(null)]   // no Content-Length
    [InlineData(50L)]    // a lying one
    public async Task A_body_over_the_cap_is_cut_off_and_nothing_is_kept(long? declared)
    {
        var body = new CountingStream(10_000_000);
        var cache = new AssetCache(_dir, new HttpClient(new StreamHandler(body, declared)));
        await Assert.ThrowsAsync<AssetTooLargeException>(() => cache.FetchAsync(new string('a', 64), new Uri("http://localhost:9000/x"), 100, CancellationToken.None));
        Assert.True(body.Served < 1_000_000, $"read {body.Served} bytes");
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task A_body_at_the_cap_is_kept()
    {
        var bytes = new byte[100];
        var cache = new AssetCache(_dir, new HttpClient(new BytesHandler(bytes)));
        var path = await cache.FetchAsync(Hash(bytes), new Uri("http://localhost:9000/x"), 100, CancellationToken.None);
        Assert.Equal(100, new FileInfo(path).Length);
    }

    [Fact]
    public async Task A_texture_over_sixteen_megabytes_is_refused()
    {
        var body = new CountingStream((16L << 20) + 1);
        var cache = new AssetCache(_dir, new HttpClient(new StreamHandler(body, null)));
        await Assert.ThrowsAsync<AssetTooLargeException>(() => cache.FetchAsync(new string('a', 64), new Uri("http://localhost:9000/x"), AssetCache.CapFor(AssetKind.Texture), CancellationToken.None));
        Assert.Empty(Directory.GetFiles(_dir));
    }

    /// <summary>Zero bytes up to a length, counting what was read.</summary>
    sealed class CountingStream(long length) : Stream
    {
        public long Served;
        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count, length - Served);
            Array.Clear(buffer, offset, n); Served += n; return n;
        }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => Served; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    sealed class StreamHandler(Stream body, long? declared) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var content = new StreamContent(body);
            content.Headers.ContentLength = declared;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    sealed class BytesHandler(byte[] bytes) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }); }
    }
}
