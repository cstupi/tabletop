using System.Text.Json;
using Tabletop.Management.Content.Assets;

namespace Tabletop.Management.Releases;

/// <summary>A published client build per platform, as <c>scripts/release-client</c> writes it (client releases spec → Manifest).</summary>
public sealed record ReleaseManifest(string Version, DateTimeOffset Date, string? Notes, int? ProtocolVersion, IReadOnlyDictionary<string, ReleaseFile>? Platforms);

public sealed record ReleaseFile(string Key, long Size, string Sha256);

/// <summary>What the last read of <c>releases/latest.json</c> found: a manifest, nothing published, or an unreadable object.</summary>
public sealed record ReleaseRead(ReleaseManifest? Manifest, bool Unreadable);

/// <summary>Reads <c>releases/latest.json</c> from the blob store, at most once per <see cref="CacheFor"/>; an absent or
/// unreadable object is cached as well.</summary>
public sealed class ClientReleases(IBlobStore blobs, TimeProvider clock, ILogger<ClientReleases> log)
{
    public const string Prefix = "releases/";
    public const string LatestKey = Prefix + "latest.json";
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);
    /// <summary>Known platforms, in the order the Download page lists them.</summary>
    public static readonly string[] Platforms = ["windows", "macos", "linux"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (ReleaseRead Read, DateTimeOffset At)? _cached;

    public async Task<ReleaseRead> Latest(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var now = clock.GetUtcNow();
            if (_cached is { } c && now - c.At < CacheFor) return c.Read;
            var read = await Read(ct);
            _cached = (read, now);
            return read;
        }
        finally { _gate.Release(); }
    }

    private async Task<ReleaseRead> Read(CancellationToken ct)
    {
        await using var blob = await blobs.OpenRead(LatestKey, ct);
        if (blob is null) return new(null, false);
        try
        {
            var manifest = await JsonSerializer.DeserializeAsync<ReleaseManifest>(blob.Content, Json, ct);
            if (manifest is { Version.Length: > 0 }) return new(manifest, false);
            log.LogError("{Key} names no version", LatestKey);
        }
        catch (JsonException e) { log.LogError(e, "{Key} is not a release manifest", LatestKey); }
        return new(null, true);
    }
}
