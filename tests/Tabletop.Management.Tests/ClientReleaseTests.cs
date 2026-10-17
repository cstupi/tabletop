using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Tabletop.Management.Releases;

namespace Tabletop.Management.Tests;

public class ClientReleaseTests
{
    private const string Zip = "releases/1.2.0/tabletop-1.2.0-windows-x86_64.zip";

    private static string Manifest(string windowsKey = Zip) => $$"""
        {
          "version": "1.2.0",
          "date": "2026-10-17T12:00:00Z",
          "notes": "Passengers.\nFaster dice.",
          "protocolVersion": 4,
          "platforms": {
            "windows": { "key": "{{windowsKey}}", "size": 3, "sha256": "{{new string('a', 64)}}" },
            "linux":   { "key": "releases/1.2.0/tabletop-1.2.0-linux-x86_64.zip", "size": 4, "sha256": "{{new string('b', 64)}}" },
            "plan9":   { "key": "releases/1.2.0/tabletop-1.2.0-plan9.zip", "size": 5, "sha256": "{{new string('c', 64)}}" }
          }
        }
        """;

    private static async Task Publish(InMemoryBlobStore blobs, string manifest)
    {
        await blobs.Put(Zip, [1, 2, 3], "application/zip");
        await blobs.Put(ClientReleases.LatestKey, Encoding.UTF8.GetBytes(manifest), "application/json");
    }

    [Fact]
    public async Task No_published_release_is_a_404()
    {
        using var f = new ManagementFactory();
        var client = await f.AuthenticatedClient(Guid.CreateVersion7());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/v1/client/latest")).StatusCode);
    }

    [Fact]
    public async Task Signed_out_is_a_401()
    {
        using var f = new ManagementFactory();
        await Publish(f.Blobs, Manifest());
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().GetAsync("/v1/client/latest")).StatusCode);
    }

    [Fact]
    public async Task The_latest_release_comes_with_a_presigned_link_per_known_platform()
    {
        using var f = new ManagementFactory();
        await Publish(f.Blobs, Manifest());
        var client = await f.AuthenticatedClient(Guid.CreateVersion7());

        var latest = await client.GetJson<ClientRelease>("/v1/client/latest");

        Assert.Equal(("1.2.0", "Passengers.\nFaster dice.", 4), (latest.Version, latest.Notes, latest.ProtocolVersion));
        Assert.Equal(new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero), latest.Date);
        Assert.Equal(["windows", "linux"], latest.Downloads.Select(d => d.Platform));            // an unknown platform is dropped
        var windows = latest.Downloads[0];
        Assert.Equal(("tabletop-1.2.0-windows-x86_64.zip", 3L, new string('a', 64)), (windows.FileName, windows.Size, windows.Sha256));
        using var http = f.Blobs.Client();
        Assert.Equal(new byte[] { 1, 2, 3 }, await http.GetByteArrayAsync(windows.Url));
        Assert.InRange(latest.ExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task A_platform_key_outside_releases_is_never_presigned()
    {
        using var f = new ManagementFactory();
        await Publish(f.Blobs, Manifest(windowsKey: $"{Guid.CreateVersion7()}/{new string('d', 64)}"));
        var client = await f.AuthenticatedClient(Guid.CreateVersion7());
        var latest = await client.GetJson<ClientRelease>("/v1/client/latest");
        Assert.Equal(["linux"], latest.Downloads.Select(d => d.Platform));
    }

    [Fact]
    public async Task An_unreadable_manifest_is_a_502()
    {
        using var f = new ManagementFactory();
        await Publish(f.Blobs, "{ not json");
        var client = await f.AuthenticatedClient(Guid.CreateVersion7());
        Assert.Equal(HttpStatusCode.BadGateway, (await client.GetAsync("/v1/client/latest")).StatusCode);
    }

    [Fact]
    public async Task The_manifest_is_read_at_most_once_a_minute()
    {
        var blobs = new InMemoryBlobStore();
        var clock = new ManualClock();
        var releases = new ClientReleases(blobs, clock, NullLogger<ClientReleases>.Instance);

        Assert.Null((await releases.Latest()).Manifest);                                            // nothing yet, and that is cached too
        await Publish(blobs, Manifest());
        Assert.Null((await releases.Latest()).Manifest);
        clock.Advance(ClientReleases.CacheFor);
        Assert.Equal("1.2.0", (await releases.Latest()).Manifest?.Version);
        await blobs.Put(ClientReleases.LatestKey, Encoding.UTF8.GetBytes(Manifest().Replace("1.2.0\"", "1.3.0\"")), "application/json");
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal("1.2.0", (await releases.Latest()).Manifest?.Version);
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal("1.3.0", (await releases.Latest()).Manifest?.Version);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
