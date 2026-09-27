using Microsoft.IdentityModel.Tokens;
using Tabletop.Management.Configuration;

namespace Tabletop.Management.Identity;

/// <summary>Stytch's public keys, fetched once and again only when a token names a kid we do not hold.</summary>
public sealed class StytchJwks(HttpClient http, ManagementOptions options)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private JsonWebKeySet? _keys;
    // Gates only refetches triggered by an unknown kid, separately from the lazy first load, so a
    // genuine rotation right after startup is not held back by a floor measured from that load.
    private DateTimeOffset _lastMissRefetch = DateTimeOffset.MinValue;

    public string Url => $"{options.StytchBaseUrl.TrimEnd('/')}/v1/sessions/jwks/{options.StytchProjectId}";

    public async Task<IReadOnlyList<SecurityKey>> Resolve(string? kid, CancellationToken ct = default)
    {
        var keys = await EnsureLoaded(kid, ct);
        return keys.Keys.Where(k => kid is null || k.Kid == kid).ToList();
    }

    private bool Holds(string? kid) => _keys is { } k && (kid is null || k.Keys.Any(key => key.Kid == kid));

    private async Task<JsonWebKeySet> EnsureLoaded(string? kid, CancellationToken ct)
    {
        if (Holds(kid)) return _keys!; // fast path: no lock needed once we already hold the kid

        await _gate.WaitAsync(ct);
        try
        {
            // Re-check now that we hold the lock: a concurrent caller queued ahead of us may
            // already have fetched the kid we need, or may already have hit the floor below.
            if (Holds(kid)) return _keys!;

            var alreadyLoaded = _keys is not null;
            // The floor only throttles a repeat miss once we hold *some* key set; the very
            // first load (nothing cached yet) always goes through regardless of timing.
            if (alreadyLoaded && DateTimeOffset.UtcNow - _lastMissRefetch <= TimeSpan.FromSeconds(30))
                return _keys!;

            _keys = new JsonWebKeySet(await http.GetStringAsync(Url, ct));
            if (alreadyLoaded) _lastMissRefetch = DateTimeOffset.UtcNow;
            return _keys;
        }
        finally { _gate.Release(); }
    }
}
