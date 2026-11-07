namespace Tabletop.Client.Core;

/// <summary>
/// Keeps the server's built-in catalog and fetches its files on demand (spec 2026-11-07-assets-on-demand-and-client-releases-design.md):
/// <paramref name="file"/> holds the last catalog fetched, the files sit in the <see cref="AssetCache"/> by hash. A version seen on a
/// management response that differs from the catalog in use starts one sync: fetch <c>GET /v1/builtin</c>, save it and
/// <paramref name="use"/> it; no file is downloaded. A failed sync keeps the catalog in use and waits <see cref="RetryAfter"/> before
/// that version is tried again. <see cref="FileAsync"/> downloads one file by the served hash when a table needs it.
/// </summary>
/// <param name="fetch"><c>GET /v1/builtin</c>'s body; null when nothing is published.</param>
/// <param name="current">The catalog in use.</param>
/// <param name="use">Called with a catalog whose version differs from the one in use.</param>
public sealed class BuiltinSync
{
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);
    /// <summary>File requests (an unknown hash, lapsed URLs) fetch the catalog at most once per this, sharing one fetch.</summary>
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(5);
    readonly string _file; readonly AssetCache _cache; readonly Func<CancellationToken, Task<string?>> _fetch;
    readonly Func<BuiltinCatalog> _current; readonly Action<BuiltinCatalog> _use; readonly TimeProvider _time;
    readonly CoalescedRefresh _refresh;
    readonly object _gate = new();
    Task _running = Task.CompletedTask;
    string? _failed; DateTimeOffset _retryAt;
    bool _lastOk;
    /// <summary>The last <c>GET /v1/builtin</c> answer, with its download URLs.</summary>
    volatile BuiltinCatalog? _served;
    readonly Dictionary<string, Task<string?>> _fetching = new();

    public BuiltinSync(string file, AssetCache cache, Func<CancellationToken, Task<string?>> fetch, Func<BuiltinCatalog> current,
        Action<BuiltinCatalog> use, TimeProvider? time = null)
    {
        (_file, _cache, _fetch, _current, _use, _time) = (file, cache, fetch, current, use, time ?? TimeProvider.System);
        _refresh = new CoalescedRefresh(async () =>
        {
            bool ok;
            try { ok = await SyncAsync(CancellationToken.None); }
            catch (Exception) { ok = false; }
            _lastOk = ok;
        }, RefreshEvery, _time.GetUtcNow, wait => Task.Delay(wait, _time));
    }

    /// <summary>The saved catalog when it parses; else null (the packed one stays). Its files need not be cached: they are fetched on demand.</summary>
    public BuiltinCatalog? LoadCached()
    {
        try
        {
            if (!File.Exists(_file)) return null;
            var catalog = BuiltinCatalog.Parse(File.ReadAllText(_file));
            return catalog.Version is not null ? catalog : null;
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
            if (version == _current().Version) return Task.CompletedTask;
            if (version == _failed && _time.GetUtcNow() < _retryAt) return Task.CompletedTask;
            return _running = Run(version);
        }
    }

    async Task Run(string version)
    {
        await _refresh.Run();
        var done = _lastOk;
        lock (_gate) (_failed, _retryAt) = done ? (null, default) : (version, _time.GetUtcNow() + RetryAfter);
    }

    /// <summary>One catalog fetch. True when it was read and saved; a version other than the one in use is then used.</summary>
    async Task<bool> SyncAsync(CancellationToken ct)
    {
        if (await _fetch(ct) is not { } json) return false;
        var catalog = BuiltinCatalog.Parse(json);
        if (catalog.Version is null) return false;
        _served = catalog;
        var part = _file + ".part";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_file))!);
        await File.WriteAllTextAsync(part, catalog.ToJson(), ct);
        File.Move(part, _file, overwrite: true);
        if (catalog.Version != _current().Version) _use(catalog);
        return true;
    }

    /// <summary>The file at a catalog path: the cached blob of the catalog in use's hash, else downloaded by the last served catalog's
    /// hash and URL (fetching the catalog first when none was served, it does not list the path, or its URLs have lapsed). Null when it
    /// cannot be had. Requests for one path share one download.</summary>
    public Task<string?> FileAsync(string path)
    {
        if (_current().Files.TryGetValue(path, out var f) && _cache.Resolve(f.Sha256) is { } held) return Task.FromResult<string?>(held);
        lock (_gate)
        {
            if (_fetching.TryGetValue(path, out var running)) return running;
            var task = Download(path);
            if (!task.IsCompleted) _fetching[path] = task;
            return task;
        }
    }

    async Task<string?> Download(string path)
    {
        try
        {
            var served = _served;
            if (served?.Files.ContainsKey(path) != true || served.ExpiresAt is { } lapse && _time.GetUtcNow() >= lapse)
            {
                await _refresh.RunIfDue();
                served = _served;
            }
            if (served?.Files.GetValueOrDefault(path) is not { Url: { } url } file) return null;
            return await _cache.FetchAsync(file.Sha256, url, BuiltinCatalog.CapFor(path), CancellationToken.None);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or HashMismatchException or AssetTooLargeException or TaskCanceledException) { return null; }
        finally { lock (_gate) _fetching.Remove(path); }
    }
}
