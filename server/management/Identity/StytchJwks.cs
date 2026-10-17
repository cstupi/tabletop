using Microsoft.IdentityModel.Tokens;
using Tabletop.Management.Configuration;

namespace Tabletop.Management.Identity;

/// <summary>Stytch's public keys, fetched once and again only when a token names a kid we do not hold. One fetch at a time: callers
/// that need it await the one in flight, callers holding their kid never wait, and a failed or slow fetch (FetchTimeout) leaves
/// the cached set in place.</summary>
public sealed class StytchJwks(HttpClient http, ManagementOptions options, TimeSpan? fetchTimeout = null, ILogger<StytchJwks>? log = null)
{
    public static readonly TimeSpan DefaultFetchTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MissFloor = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private readonly TimeSpan _timeout = fetchTimeout ?? DefaultFetchTimeout;
    private JsonWebKeySet? _keys;
    private Task? _inflight;
    // Gates only refetches triggered by an unknown kid, separately from the lazy first load, so a
    // genuine rotation right after startup is not held back by a floor measured from that load.
    private DateTimeOffset _lastMissRefetch = DateTimeOffset.MinValue;

    public string Url => $"{options.StytchBaseUrl.TrimEnd('/')}/v1/sessions/jwks/{options.StytchProjectId}";

    /// <summary>Makes sure the kid is held if Stytch has it, then returns the matching cached keys (all of them for a null kid).</summary>
    public async Task<IReadOnlyList<SecurityKey>> Resolve(string? kid, CancellationToken ct = default)
    {
        await Ensure(kid).WaitAsync(ct);
        return Cached(kid);
    }

    /// <summary>The cached keys only, never a fetch: what the bearer handler's synchronous resolver calls once Ensure has run.</summary>
    public IReadOnlyList<SecurityKey> Cached(string? kid) =>
        _keys is { } k ? k.Keys.Where(key => kid is null || key.Kid == kid).ToList<SecurityKey>() : [];

    /// <summary>Completes at once when the kid is held or the miss floor holds; otherwise joins or starts the single fetch.</summary>
    public Task Ensure(string? kid)
    {
        if (Holds(kid)) return Task.CompletedTask;
        lock (_gate)
        {
            if (Holds(kid)) return Task.CompletedTask;
            if (_inflight is { IsCompleted: false } running) return running;
            var alreadyLoaded = _keys is not null;
            // The floor only throttles a repeat miss once we hold *some* key set; the very first load always goes through.
            if (alreadyLoaded && DateTimeOffset.UtcNow - _lastMissRefetch <= MissFloor) return Task.CompletedTask;
            if (alreadyLoaded) _lastMissRefetch = DateTimeOffset.UtcNow;
            return _inflight = Fetch();
        }
    }

    private bool Holds(string? kid) => _keys is { } k && (kid is null || k.Keys.Any(key => key.Kid == kid));

    private async Task Fetch()
    {
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            _keys = new JsonWebKeySet(await http.GetStringAsync(Url, cts.Token));
        }
        catch (Exception e)
        {
            log?.LogWarning(e, "Stytch JWKS fetch failed; keeping the cached keys");
        }
        finally { lock (_gate) _inflight = null; }
    }
}
