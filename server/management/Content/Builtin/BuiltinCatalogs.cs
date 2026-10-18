using System.Text.Json;
using System.Text.RegularExpressions;
using Tabletop.Management.Content.Assets;

namespace Tabletop.Management.Content.Builtin;

/// <summary>A published file of the built-in content: its content hash (the key is <c>builtin/{sha256}</c>) and size.</summary>
public sealed record BuiltinFileRef(string Sha256, long Size);

/// <summary><c>builtin/catalog.json</c> as <c>scripts/release-client builtin</c> writes it (builtin content spec → Publishing).
/// Management reads the version and the files; <c>meshes</c>, <c>textures</c> and <c>terrain</c> pass through unread.</summary>
public sealed record PublishedBuiltin(string Version, JsonElement? Meshes, JsonElement? Textures, JsonElement? Terrain, IReadOnlyDictionary<string, BuiltinFileRef>? Files);

/// <summary>What the last read of <c>builtin/catalog.json</c> found: a catalog, nothing published, or an unreadable object.</summary>
public sealed record BuiltinRead(PublishedBuiltin? Catalog, bool Unreadable);

/// <summary>Reads <c>builtin/catalog.json</c> from the blob store, at most once per <see cref="CacheFor"/>; an absent or
/// unreadable object is cached as well. <see cref="KnownVersion"/> never waits on the store.</summary>
public sealed partial class BuiltinCatalogs(IBlobStore blobs, TimeProvider clock, ILogger<BuiltinCatalogs> log)
{
    public const string Prefix = "builtin/";
    public const string CatalogKey = Prefix + "catalog.json";
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);
    public static string Key(string sha256) => Prefix + sha256;
    [GeneratedRegex("^[0-9a-f]{64}$")] public static partial Regex Sha256();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile Cached? _cached;
    private int _refreshing;
    private sealed record Cached(BuiltinRead Read, DateTimeOffset At);

    public async Task<BuiltinRead> Latest(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var now = clock.GetUtcNow();
            if (_cached is { } c && now - c.At < CacheFor) return c.Read;
            var read = await Read(ct);
            _cached = new(read, now);
            return read;
        }
        finally { _gate.Release(); }
    }

    /// <summary>The version of the catalog last read, for the <c>Tabletop-Builtin-Version</c> header. A stale or missing read
    /// starts one refresh in the background and answers what it has.</summary>
    public string? KnownVersion()
    {
        var c = _cached;
        if ((c is null || clock.GetUtcNow() - c.At >= CacheFor) && Interlocked.CompareExchange(ref _refreshing, 1, 0) == 0)
            _ = Task.Run(async () =>
            {
                try { await Latest(); }
                catch (Exception e) { log.LogWarning(e, "{Key} could not be read", CatalogKey); }
                finally { Volatile.Write(ref _refreshing, 0); }
            });
        return c?.Read.Catalog?.Version;
    }

    private async Task<BuiltinRead> Read(CancellationToken ct)
    {
        await using var blob = await blobs.OpenRead(CatalogKey, ct);
        if (blob is null) return new(null, false);
        try
        {
            var catalog = await JsonSerializer.DeserializeAsync<PublishedBuiltin>(blob.Content, Json, ct);
            if (catalog is { Version.Length: > 0 }) return new(catalog, false);
            log.LogError("{Key} names no version", CatalogKey);
        }
        catch (JsonException e) { log.LogError(e, "{Key} is not a built-in catalog", CatalogKey); }
        return new(null, true);
    }
}
