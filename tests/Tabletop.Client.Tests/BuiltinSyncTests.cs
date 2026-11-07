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

    /// <summary>A served catalog whose mesh and texture are these bytes, at /m/{sha} and /t/{sha}, its URLs good until <paramref name="expires"/>.</summary>
    static string Served(Blobs blobs, string version, byte[] mesh, byte[] texture, DateTimeOffset? expires = null)
    {
        blobs.Files[$"/m/{Sha(mesh)}"] = mesh; blobs.Files[$"/t/{Sha(texture)}"] = texture;
        var j = BuiltinCatalogTests.Catalog(version);
        j["files"] = new JsonObject
        {
            ["terrain/hill.glb"] = new JsonObject { ["sha256"] = Sha(mesh), ["size"] = mesh.Length, ["url"] = $"http://blobs/m/{Sha(mesh)}" },
            ["textures/grass.png"] = new JsonObject { ["sha256"] = Sha(texture), ["size"] = texture.Length, ["url"] = $"http://blobs/t/{Sha(texture)}" },
        };
        j["expiresAt"] = (expires ?? new DateTimeOffset(2026, 10, 18, 0, 15, 0, TimeSpan.Zero)).ToString("O");
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
        /// <summary>Holds each catalog fetch until set, when not null.</summary>
        public TaskCompletionSource? Gate;
    }

    Harness Make()
    {
        var h = new Harness();
        h.File = Path.Combine(_dir, "builtin.json");
        h.Cache = new AssetCache(Path.Combine(_dir, "cache"), new HttpClient(h.Blobs));
        h.Sync = new BuiltinSync(h.File, h.Cache, async _ => { h.Fetches++; if (h.Gate is { } g) await g.Task; return h.Response; }, () => h.Current,
            c => { h.Current = c; h.Used.Add(c); }, h.Clock);
        return h;
    }

    [Fact]
    public async Task A_new_version_uses_and_saves_the_catalog_and_downloads_no_file()
    {
        var h = Make();
        h.Response = Served(h.Blobs, "1.3.0", [1, 2, 3], [4, 5]);
        await h.Sync.Notice("1.3.0");
        Assert.Equal("1.3.0", Assert.Single(h.Used).Version);
        Assert.Empty(h.Blobs.Gets);
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
    public async Task A_failed_fetch_keeps_the_catalog_in_use_saves_nothing_and_retries_after_a_minute()
    {
        var h = Make();
        await h.Sync.Notice("1.3.0");                                 // nothing answered
        Assert.Same(BuiltinContent.Packed, h.Current);
        Assert.False(System.IO.File.Exists(h.File));
        h.Response = Served(h.Blobs, "1.3.0", [1, 2, 3], [4, 5]);
        await h.Sync.Notice("1.3.0");                                 // within the minute: not tried again
        Assert.Equal(1, h.Fetches);
        h.Clock.Now += BuiltinSync.RetryAfter;
        await h.Sync.Notice("1.3.0");
        Assert.Equal(("1.3.0", 2), (h.Current.Version, h.Fetches));
    }

    [Fact]
    public async Task An_unreadable_catalog_keeps_the_catalog_in_use()
    {
        var h = Make();
        h.Response = "{ not json";
        await h.Sync.Notice("1.3.0");
        Assert.Empty(h.Used);
        Assert.Equal(1, h.Fetches);
    }

    [Fact]
    public async Task At_start_the_saved_catalog_is_used_whether_or_not_its_files_are_cached()
    {
        var h = Make();
        Assert.Null(h.Sync.LoadCached());
        h.Response = Served(h.Blobs, "1.3.0", [1, 2, 3], [4, 5]);
        await h.Sync.Notice("1.3.0");
        Assert.Equal("1.3.0", Make2(h).LoadCached()?.Version);
        System.IO.File.WriteAllText(h.File, "garbage");
        Assert.Null(Make2(h).LoadCached());
    }

    static BuiltinSync Make2(Harness h) => new(h.File, h.Cache, _ => Task.FromResult<string?>(null), () => BuiltinContent.Packed, _ => { });

    [Fact]
    public async Task Notices_during_a_sync_join_it()
    {
        var h = Make();
        h.Gate = new();
        h.Response = Served(h.Blobs, "1.3.0", [1], [2]);
        var first = h.Sync.Notice("1.3.0");
        var second = h.Sync.Notice("1.3.0");
        Assert.Same(first, second);
        h.Gate.SetResult();
        await first;
        Assert.Equal((1, "1.3.0"), (h.Fetches, h.Current.Version));
    }

    [Fact]
    public async Task A_file_not_on_hand_downloads_by_the_served_hash_once_and_then_comes_from_the_cache()
    {
        var h = Make();
        h.Response = Served(h.Blobs, "1.3.0", [1, 2, 3], [4, 5]);
        await h.Sync.Notice("1.3.0");
        Assert.Equal(h.Cache.PathFor(Sha([1, 2, 3])), await h.Sync.FileAsync("terrain/hill.glb"));
        Assert.Equal(h.Cache.PathFor(Sha([1, 2, 3])), await h.Sync.FileAsync("terrain/hill.glb"));
        Assert.Equal([$"/m/{Sha([1, 2, 3])}"], h.Blobs.Gets);
        Assert.Equal(1, h.Fetches);
    }

    [Fact]
    public async Task Before_any_catalog_fetch_a_file_fetches_the_catalog_first()
    {
        var h = Make();                                               // the packed catalog: no hashes
        h.Response = Served(h.Blobs, "1.3.0", [1, 2, 3], [4, 5]);
        Assert.Equal(h.Cache.PathFor(Sha([4, 5])), await h.Sync.FileAsync("textures/grass.png"));
        Assert.Equal(1, h.Fetches);
        Assert.Equal("1.3.0", h.Current.Version);
    }

    [Fact]
    public async Task Requests_for_one_file_share_one_download()
    {
        var h = Make();
        h.Response = Served(h.Blobs, "1.3.0", [1, 2, 3], [4, 5]);
        h.Gate = new();
        var files = Enumerable.Range(0, 5).Select(_ => h.Sync.FileAsync("terrain/hill.glb")).ToList();
        h.Gate.SetResult();
        Assert.All(await Task.WhenAll(files), f => Assert.Equal(h.Cache.PathFor(Sha([1, 2, 3])), f));
        Assert.Equal(1, h.Fetches);
        Assert.Single(h.Blobs.Gets);
    }

    [Fact]
    public async Task Lapsed_urls_fetch_the_catalog_again()
    {
        var h = Make();
        h.Response = Served(h.Blobs, "1.3.0", [1, 2, 3], [4, 5], expires: h.Clock.Now + TimeSpan.FromMinutes(15));
        await h.Sync.Notice("1.3.0");
        h.Clock.Now += TimeSpan.FromMinutes(20);
        h.Response = Served(h.Blobs, "1.3.0", [1, 2, 3], [4, 5], expires: h.Clock.Now + TimeSpan.FromMinutes(15));
        Assert.NotNull(await h.Sync.FileAsync("terrain/hill.glb"));
        Assert.Equal(2, h.Fetches);
    }

    [Fact]
    public async Task A_file_that_cannot_be_had_is_null()
    {
        var h = Make();
        Assert.Null(await h.Sync.FileAsync("terrain/hill.glb"));      // nothing published
        h.Response = Served(h.Blobs, "1.3.0", [1, 2, 3], [4, 5]);
        h.Blobs.Files[$"/m/{Sha([1, 2, 3])}"] = [6, 6, 6];             // corrupt
        h.Clock.Now += TimeSpan.FromSeconds(10);
        Assert.Null(await h.Sync.FileAsync("terrain/hill.glb"));
        Assert.False(h.Cache.Has(Sha([1, 2, 3])));
        h.Blobs.Down = true;
        Assert.Null(await h.Sync.FileAsync("textures/grass.png"));
    }

    [Fact]
    public async Task A_changed_hash_downloads_only_the_new_file()
    {
        var h = Make();
        h.Response = Served(h.Blobs, "1.3.0", [1, 2, 3], [4, 5]);
        await h.Sync.FileAsync("terrain/hill.glb"); await h.Sync.FileAsync("textures/grass.png");
        h.Blobs.Gets.Clear();
        h.Response = Served(h.Blobs, "1.4.0", [9, 9, 9], [4, 5]);      // a mesh tweak; the texture is the same bytes
        h.Clock.Now += BuiltinSync.RetryAfter;
        await h.Sync.Notice("1.4.0");
        Assert.Equal(h.Cache.PathFor(Sha([4, 5])), await h.Sync.FileAsync("textures/grass.png"));
        Assert.Equal(h.Cache.PathFor(Sha([9, 9, 9])), await h.Sync.FileAsync("terrain/hill.glb"));
        Assert.Equal([$"/m/{Sha([9, 9, 9])}"], h.Blobs.Gets);
    }
}
