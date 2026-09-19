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
        var keys = _keys ?? await Fetch(ct);
        var match = keys.Keys.Where(k => kid is null || k.Kid == kid).ToList();
        if (match.Count == 0 && DateTimeOffset.UtcNow - _lastMissRefetch > TimeSpan.FromSeconds(30))
        {
            _lastMissRefetch = DateTimeOffset.UtcNow;
            match = (await Fetch(ct)).Keys.Where(k => k.Kid == kid).ToList();
        }
        return match;
    }

    private async Task<JsonWebKeySet> Fetch(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _keys = new JsonWebKeySet(await http.GetStringAsync(Url, ct));
            return _keys;
        }
        finally { _gate.Release(); }
    }
}
