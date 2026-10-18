namespace Tabletop.Client.Core;

/// <summary>
/// Keeps the server's built-in catalog (spec 2026-10-18-builtin-content-from-server-design.md → Client): <paramref name="file"/>
/// holds the last complete one, the files sit in the <see cref="AssetCache"/> by hash. A version seen on a management response
/// that differs from the catalog in use starts one sync: fetch, download what the cache lacks, then <paramref name="use"/>.
/// A failed sync keeps the catalog in use and waits <see cref="RetryAfter"/> before that version is tried again.
/// </summary>
/// <param name="fetch"><c>GET /v1/builtin</c>'s body; null when nothing is published.</param>
/// <param name="current">The catalog in use.</param>
/// <param name="use">Called with the new catalog, once every file it names is cached.</param>
public sealed class BuiltinSync(string file, AssetCache cache, Func<CancellationToken, Task<string?>> fetch, Func<BuiltinCatalog> current,
    Action<BuiltinCatalog> use, TimeProvider? time = null)
{
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);
    readonly TimeProvider _time = time ?? TimeProvider.System;
    readonly object _gate = new();
    Task _running = Task.CompletedTask;
    string? _failed; DateTimeOffset _retryAt;

    /// <summary>The saved catalog, when it parses and the cache holds every file it names; else null (the packed one stays).</summary>
    public BuiltinCatalog? LoadCached()
    {
        try
        {
            if (!File.Exists(file)) return null;
            var catalog = BuiltinCatalog.Parse(File.ReadAllText(file));
            return catalog.Version is not null && catalog.Downloads.All(d => cache.Has(d.File.Sha256)) ? catalog : null;
        }
        catch (Exception e) when (e is IOException or FormatException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The server names <paramref name="version"/>: sync unless it is in use, a sync runs, or it failed under a minute ago.
    /// Returns the sync in flight (or a completed task).</summary>
    public Task Notice(string version)
    {
        lock (_gate)
        {
            if (!_running.IsCompleted) return _running;
            if (version == current().Version) return Task.CompletedTask;
            if (version == _failed && _time.GetUtcNow() < _retryAt) return Task.CompletedTask;
            return _running = Run(version);
        }
    }

    async Task Run(string version)
    {
        bool done;
        try { done = await SyncAsync(CancellationToken.None); }
        catch (Exception) { done = false; }
        lock (_gate) (_failed, _retryAt) = done ? (null, default) : (version, _time.GetUtcNow() + RetryAfter);
    }

    /// <summary>One sync. True when the server's catalog is in use afterwards (or nothing is published).</summary>
    public async Task<bool> SyncAsync(CancellationToken ct)
    {
        if (await fetch(ct) is not { } json) return false;
        var catalog = BuiltinCatalog.Parse(json);
        if (catalog.Version is null) return false;
        if (catalog.Version == current().Version) return true;
        var complete = true;
        foreach (var (_, f, cap) in catalog.Downloads)
        {
            if (cache.Has(f.Sha256)) continue;
            if (f.Url is null) { complete = false; continue; }
            try { await cache.FetchAsync(f.Sha256, f.Url, cap, ct); }
            catch (Exception e) when (e is HttpRequestException or IOException or HashMismatchException or AssetTooLargeException or TaskCanceledException) { complete = false; }
        }
        if (!complete) return false;
        var part = file + ".part";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        await File.WriteAllTextAsync(part, catalog.ToJson(), ct);
        File.Move(part, file, overwrite: true);
        use(catalog);
        return true;
    }
}
