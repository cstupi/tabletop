using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Tabletop.Management.Content.Builtin;
using Tabletop.Protocol;

namespace Tabletop.Management.Tests;

public class BuiltinTests
{
    private static readonly string Mesh = new('a', 64), Texture = new('b', 64);

    private static string Catalog(string version = "1.3.0", string textureSha = "") => $$"""
        {
          "version": "{{version}}",
          "meshes": ["hill"],
          "textures": ["grass"],
          "terrain": [{ "id": "hill", "label": "Hill", "mesh": "hill", "kind": "solid", "footprint": { "kind": "circle", "a": 3 }, "top": 2, "declarations": ["cover"] }],
          "files": {
            "terrain/hill.glb": { "sha256": "{{Mesh}}", "size": 3 },
            "textures/grass.png": { "sha256": "{{(textureSha == "" ? Texture : textureSha)}}", "size": 2 }
          }
        }
        """;

    private static async Task Publish(InMemoryBlobStore blobs, string catalog)
    {
        await blobs.Put(BuiltinCatalogs.Key(Mesh), [1, 2, 3], "model/gltf-binary");
        await blobs.Put(BuiltinCatalogs.Key(Texture), [4, 5], "image/png");
        await blobs.Put(BuiltinCatalogs.CatalogKey, Encoding.UTF8.GetBytes(catalog), "application/json");
    }

    [Fact]
    public async Task Nothing_published_is_a_404()
    {
        using var f = new ManagementFactory();
        var client = await f.AuthenticatedClient(Guid.CreateVersion7());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/v1/builtin")).StatusCode);
    }

    [Fact]
    public async Task Signed_out_is_a_401()
    {
        using var f = new ManagementFactory();
        await Publish(f.Blobs, Catalog());
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().GetAsync("/v1/builtin")).StatusCode);
    }

    [Fact]
    public async Task The_catalog_comes_with_a_presigned_link_per_file_and_its_terrain_unread()
    {
        using var f = new ManagementFactory();
        await Publish(f.Blobs, Catalog());
        var client = await f.AuthenticatedClient(Guid.CreateVersion7());

        var response = await client.GetAsync("/v1/builtin");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal("1.3.0", root.GetProperty("version").GetString());
        Assert.Equal("hill", root.GetProperty("terrain")[0].GetProperty("id").GetString());
        Assert.Equal("cover", root.GetProperty("terrain")[0].GetProperty("declarations")[0].GetString());
        Assert.Equal("grass", root.GetProperty("textures")[0].GetString());
        var mesh = root.GetProperty("files").GetProperty("terrain/hill.glb");
        Assert.Equal((Mesh, 3), (mesh.GetProperty("sha256").GetString(), mesh.GetProperty("size").GetInt32()));
        using var http = f.Blobs.Client();
        Assert.Equal(new byte[] { 1, 2, 3 }, await http.GetByteArrayAsync(mesh.GetProperty("url").GetString()));
        Assert.InRange(root.GetProperty("expiresAt").GetDateTimeOffset() - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));
        Assert.Equal("1.3.0", response.Headers.GetValues(ProtocolVersion.BuiltinHeader).Single());
    }

    [Fact]
    public async Task A_file_whose_hash_is_not_a_sha256_is_never_presigned()
    {
        using var f = new ManagementFactory();
        await Publish(f.Blobs, Catalog(textureSha: $"../{Guid.CreateVersion7()}/x"));
        var client = await f.AuthenticatedClient(Guid.CreateVersion7());
        using var doc = JsonDocument.Parse(await client.GetStringAsync("/v1/builtin"));
        Assert.Equal(["terrain/hill.glb"], doc.RootElement.GetProperty("files").EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task An_unreadable_catalog_is_a_502()
    {
        using var f = new ManagementFactory();
        await Publish(f.Blobs, "{ not json");
        var client = await f.AuthenticatedClient(Guid.CreateVersion7());
        Assert.Equal(HttpStatusCode.BadGateway, (await client.GetAsync("/v1/builtin")).StatusCode);
    }

    [Fact]
    public async Task Every_response_names_the_published_version_once_it_is_read()
    {
        using var f = new ManagementFactory();
        await Publish(f.Blobs, Catalog());
        var anonymous = f.CreateClient();
        // The header never waits on the store: the first response starts the read, a later one carries it.
        for (var i = 0; i < 50 && !(await anonymous.GetAsync("/healthz")).Headers.Contains(ProtocolVersion.BuiltinHeader); i++) await Task.Delay(20);
        var response = await anonymous.GetAsync("/v1/client/latest");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("1.3.0", response.Headers.GetValues(ProtocolVersion.BuiltinHeader).Single());
    }

    [Fact]
    public async Task No_catalog_no_header()
    {
        using var f = new ManagementFactory();
        var client = f.CreateClient();
        await client.GetAsync("/healthz");
        await Task.Delay(50);
        Assert.False((await client.GetAsync("/healthz")).Headers.Contains(ProtocolVersion.BuiltinHeader));
    }

    [Fact]
    public async Task The_catalog_is_read_at_most_once_a_minute()
    {
        var blobs = new InMemoryBlobStore();
        var clock = new ManualClock();
        var catalogs = new BuiltinCatalogs(blobs, clock, NullLogger<BuiltinCatalogs>.Instance);

        Assert.Null((await catalogs.Latest()).Catalog);
        await Publish(blobs, Catalog());
        Assert.Null((await catalogs.Latest()).Catalog);
        clock.Advance(BuiltinCatalogs.CacheFor);
        Assert.Equal("1.3.0", (await catalogs.Latest()).Catalog?.Version);
        Assert.Equal("1.3.0", catalogs.KnownVersion());
        await blobs.Put(BuiltinCatalogs.CatalogKey, Encoding.UTF8.GetBytes(Catalog("1.4.0")), "application/json");
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal("1.3.0", (await catalogs.Latest()).Catalog?.Version);
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal("1.3.0", catalogs.KnownVersion());                    // stale: answers what it has, refreshes behind
        for (var i = 0; i < 50 && catalogs.KnownVersion() != "1.4.0"; i++) await Task.Delay(20);
        Assert.Equal("1.4.0", catalogs.KnownVersion());
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
