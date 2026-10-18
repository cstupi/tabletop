using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class BuiltinSyncTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("tabletop-builtin-sync").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    static string Sha(byte[] b) => Convert.ToHexStringLower(SHA256.HashData(b));

    /// <summary>The blob store: bytes by URL path, every GET counted.</summary>
    sealed class Blobs : HttpMessageHandler
    {
        public readonly Dictionary<string, byte[]> Files = new();
        public readonly List<string> Gets = new();
        public bool Down { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Gets.Add(path);
            if (Down) throw new HttpRequestException("unreachable");
            return Task.FromResult(Files.TryGetValue(path, out var b) ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(b) } : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>A served catalog whose mesh and texture are these bytes, at /m/{sha} and /t/{sha}.</summary>
    static string Served(Blobs blobs, string version, byte[] mesh, byte[] texture)
    {
        blobs.Files[$"/m/{Sha(mesh)}"] = mesh; blobs.Files[$"/t/{Sha(texture)}"] = texture;
        var j = BuiltinCatalogTests.Catalog(version);
        j["files"] = new JsonObject
        {
            ["terrain/hill.glb"] = new JsonObject { ["sha256"] = Sha(mesh), ["size"] = mesh.Length, ["url"] = $"http://blobs/m/{Sha(mesh)}" },
            ["textures/grass.png"] = new JsonObject { ["sha256"] = Sha(texture), ["size"] = texture.Length, ["url"] = $"http://blobs/t/{Sha(texture)}" },
        };
        return j.ToJsonString();
    }

    sealed class Harness
    {
        public readonly Blobs Blobs = new();
        public readonly Clock Clock = new();
        public string? Response;
        public int Fetches;
        public BuiltinCatalog Current = BuiltinContent.Packed;
        public readonly List<BuiltinCatalog> Used = new();
        public AssetCache Cache = null!;
        public BuiltinSync Sync = null!;
        public string File = "";
    }

    Harness Make()
    {
        var h = new Harness();
        h.File = Path.Combine(_dir, "builtin.json");
        h.Cache = new AssetCache(Path.Combine(_dir, "cache"), new HttpClient(h.Blobs));
        h.Sync = new BuiltinSync(h.File, h.Cache, _ => { h.Fetches++; return Task.FromResult(h.Response); }, () => h.Current, c => { h.Current = c; h.Used.Add(c); }, h.Clock);
        return h;
    }

    [Fact]
    public async Task A_new_version_downloads_the_files_uses_the_catalog_and_saves_it()
    {
        var h = Make();
        h.Response = Served(h.Blobs, "1.3.0", [1, 2, 3], [4, 5]);
        await h.Sync.Notice("1.3.0");
        Assert.Equal("1.3.0", Assert.Single(h.Used).Version);
        Assert.Equal(2, h.Blobs.Gets.Count);
        Assert.True(h.Cache.Has(Sha([1, 2, 3])) && h.Cache.Has(Sha([4, 5])));
        Assert.Equal("1.3.0", BuiltinCatalog.Parse(System.IO.File.ReadAllText(h.File)).Version);
    }

    [Fact]
    public async Task The_version_in_use_fetches_nothing()
    {
        var h = Make();
        h.Response = Served(h.Blobs, "1.3.0", [1], [2]);
        await h.Sync.Notice("1.3.0");
        await h.Sync.Notice("1.3.0");
        Assert.Equal(1, h.Fetches);
        Assert.Single(h.Used);
    }

    [Fact]
    public async Task A_release_downloads_only_the_files_whose_hash_is_new()
    {
        var h = Make();
        h.Response = Served(h.Blobs, "1.3.0", [1, 2, 3], [4, 5]);
        await h.Sync.Notice("1.3.0");
        h.Blobs.Gets.Clear();
        h.Response = Served(h.Blobs, "1.4.0", [9, 9, 9], [4, 5]);      // a mesh tweak; the texture is the same bytes
        await h.Sync.Notice("1.4.0");
        Assert.Equal([$"/m/{Sha([9, 9, 9])}"], h.Blobs.Gets);
        Assert.Equal("1.4.0", h.Current.Version);
        Assert.Equal(h.Cache.PathFor(Sha([9, 9, 9])), h.Current.FileFor("terrain/hill.glb", h.Cache));
    }

    [Fact]
    public async Task A_catalog_drops_entries_it_no_longer_lists()
    {
        var h = Make();
        h.Response = Served(h.Blobs, "1.3.0", [1], [2]);
        await h.Sync.Notice("1.3.0");
        var j = JsonNode.Parse(Served(h.Blobs, "1.4.0", [1], [2]))!.AsObject();
        j["terrain"]!.AsArray().RemoveAt(1);
        h.Response = j.ToJsonString();
        await h.Sync.Notice("1.4.0");
        Assert.Equal(["hill"], h.Current.Terrain.Keys);
    }

    [Fact]
    public async Task A_failed_download_keeps_the_catalog_in_use_saves_nothing_and_retries_after_a_minute()
    {
        var h = Make();
        h.Response = Served(h.Blobs, "1.3.0", [1, 2, 3], [4, 5]);
        h.Blobs.Down = true;
        await h.Sync.Notice("1.3.0");
        Assert.Same(BuiltinContent.Packed, h.Current);
        Assert.False(System.IO.File.Exists(h.File));
        await h.Sync.Notice("1.3.0");                                 // within the minute: not tried again
        Assert.Equal(1, h.Fetches);
        h.Blobs.Down = false;
        h.Clock.Now += BuiltinSync.RetryAfter;
        await h.Sync.Notice("1.3.0");
        Assert.Equal(("1.3.0", 2), (h.Current.Version, h.Fetches));
    }

    [Fact]
    public async Task A_corrupt_download_is_a_failure()
    {
        var h = Make();
        h.Response = Served(h.Blobs, "1.3.0", [1, 2, 3], [4, 5]);
        h.Blobs.Files[$"/m/{Sha([1, 2, 3])}"] = [6, 6, 6];
        await h.Sync.Notice("1.3.0");
        Assert.Empty(h.Used);
        Assert.False(h.Cache.Has(Sha([1, 2, 3])));
    }

    [Fact]
    public async Task Nothing_published_or_an_unreadable_catalog_keeps_the_catalog_in_use()
    {
        var h = Make();
        await h.Sync.Notice("1.3.0");
        h.Clock.Now += BuiltinSync.RetryAfter;
        h.Response = "{ not json";
        await h.Sync.Notice("1.3.0");
        Assert.Empty(h.Used);
        Assert.Equal(2, h.Fetches);
    }

    [Fact]
    public async Task At_start_the_saved_catalog_is_used_only_when_every_file_is_cached()
    {
        var h = Make();
        Assert.Null(h.Sync.LoadCached());
        h.Response = Served(h.Blobs, "1.3.0", [1, 2, 3], [4, 5]);
        await h.Sync.Notice("1.3.0");
        Assert.Equal("1.3.0", Make2(h).LoadCached()?.Version);
        System.IO.File.Delete(h.Cache.PathFor(Sha([4, 5])));
        Assert.Null(Make2(h).LoadCached());
        System.IO.File.WriteAllText(h.File, "garbage");
        Assert.Null(Make2(h).LoadCached());
    }

    static BuiltinSync Make2(Harness h) => new(h.File, h.Cache, _ => Task.FromResult<string?>(null), () => BuiltinContent.Packed, _ => { });

    [Fact]
    public async Task Notices_during_a_sync_join_it()
    {
        var h = Make();
        var gate = new TaskCompletionSource<string?>();
        var fetches = 0;
        var sync = new BuiltinSync(h.File, h.Cache, _ => { fetches++; return gate.Task; }, () => h.Current, c => h.Current = c, h.Clock);
        var first = sync.Notice("1.3.0");
        var second = sync.Notice("1.3.0");
        Assert.Same(first, second);
        gate.SetResult(Served(h.Blobs, "1.3.0", [1], [2]));
        await first;
        Assert.Equal((1, "1.3.0"), (fetches, h.Current.Version));
    }
}
