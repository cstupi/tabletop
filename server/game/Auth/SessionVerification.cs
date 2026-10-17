using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Tabletop.GameServer.Configuration;

namespace Tabletop.GameServer.Auth;

/// <summary>Is this session valid, and whose is it. Null means no: the socket is rejected with invalid_session.</summary>
public interface ISessionVerifier { Task<VerifiedSession?> VerifyAsync(string jwt, CancellationToken ct); }

/// <param name="Roles">The Stytch role ids management stamped in the roles claim; empty when the token has none.</param>
public sealed record VerifiedSession(Guid AccountId, string StytchUserId, DateTimeOffset ExpiresAt, IReadOnlySet<string> Roles)
{
    public const string AdminRole = "admin";
    /// <summary>An admin may watch any game it is not a participant of.</summary>
    public bool IsAdmin => Roles.Contains(AdminRole);
}

public interface IJwksSource
{
    Task<JsonWebKeySet> GetAsync(CancellationToken ct);
    /// <summary>Asked when a token names a kid the keys lack (a rotation). A source may answer from its cache.</summary>
    Task<JsonWebKeySet> RefreshAsync(CancellationToken ct) => GetAsync(ct);
}

/// <summary>
/// Fetches Stytch's public keys and caches them for ten minutes. A refresh for an unknown kid fetches at most once per 30 s, every
/// caller in that window sharing one fetch, so a stream of tokens with made-up kids costs one request per 30 s. A failed fetch keeps
/// the keys already held; only a source that has never fetched throws. Each fetch has 5 s (the HttpClient's timeout).
/// </summary>
public sealed class HttpJwksSource(HttpClient http, GameServerOptions options, TimeProvider time, ILogger<HttpJwksSource> log) : IJwksSource
{
    public static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan RefetchAfter = TimeSpan.FromSeconds(30);
    private readonly object _gate = new();
    private JsonWebKeySet? _cached;
    private DateTimeOffset _fetchedAt, _attemptedAt = DateTimeOffset.MinValue;
    private Task<JsonWebKeySet>? _inflight;

    public Task<JsonWebKeySet> GetAsync(CancellationToken ct)
    {
        lock (_gate) if (_cached is not null && time.GetUtcNow() - _fetchedAt < CacheFor) return Task.FromResult(_cached);
        return FetchAsync(ct);
    }

    public Task<JsonWebKeySet> RefreshAsync(CancellationToken ct) => FetchAsync(ct);

    private Task<JsonWebKeySet> FetchAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_inflight is not null) return _inflight.WaitAsync(ct);
            if (_cached is not null && time.GetUtcNow() - _attemptedAt < RefetchAfter) return Task.FromResult(_cached);
            _attemptedAt = time.GetUtcNow();
            var fetch = FetchOnceAsync();
            if (!fetch.IsCompleted) _inflight = fetch;   // a fetch that finished synchronously has already cleared the slot
            return fetch.WaitAsync(ct);
        }
    }

    private async Task<JsonWebKeySet> FetchOnceAsync()
    {
        try
        {
            var keys = new JsonWebKeySet(await http.GetStringAsync(options.StytchJwksUrl, CancellationToken.None));
            lock (_gate) { _cached = keys; _fetchedAt = time.GetUtcNow(); }
            return keys;
        }
        catch (Exception ex) when (_cached is { } stale)
        {
            log.LogWarning(ex, "JWKS fetch failed; keeping the keys fetched at {FetchedAt}", _fetchedAt);
            return stale;
        }
        finally { lock (_gate) _inflight = null; }
    }
}

/// <summary>Local verification against Stytch's JWKS: RS256, issuer stytch.com/{project}, audience the project id, and our account UUID in the top-level custom claim.</summary>
public sealed class StytchJwtVerifier(IJwksSource keys, GameServerOptions options) : ISessionVerifier
{
    public const string AccountClaim = "account_id";
    public const string RolesClaim = "roles";
    private readonly JsonWebTokenHandler _handler = new();

    public async Task<VerifiedSession?> VerifyAsync(string jwt, CancellationToken ct)
    {
        if (!_handler.CanReadToken(jwt)) return null;
        var result = await _handler.ValidateTokenAsync(jwt, await ParametersAsync(ct));
        if (!result.IsValid)
        {
            // Refetch only if the kid is unknown; all other failures return null without refetch
            JsonWebToken? headerToken = null;
            try
            {
                headerToken = _handler.ReadJsonWebToken(jwt);
            }
            catch
            {
                // Malformed JWT header — can't check if kid is unknown, so don't refetch
            }

            if (headerToken != null)
            {
                var currentKeys = await keys.GetAsync(ct);
                if (!currentKeys.Keys.Any(k => k.KeyId == headerToken.Kid)
                    && (await keys.RefreshAsync(ct)).Keys.Any(k => k.KeyId == headerToken.Kid))
                    result = await _handler.ValidateTokenAsync(jwt, await ParametersAsync(ct));
            }

            if (!result.IsValid) return null;
        }
        var token = (JsonWebToken)result.SecurityToken;
        if (!token.TryGetPayloadValue<string>(AccountClaim, out var accountText) || !Guid.TryParse(accountText, out var accountId)) return null;
        return new VerifiedSession(accountId, token.Subject, token.ValidTo, Roles(token));
    }

    /// <summary>A JSON array of role ids, or a lone string; anything else is no roles.</summary>
    private static IReadOnlySet<string> Roles(JsonWebToken token) =>
        token.TryGetPayloadValue<string[]>(RolesClaim, out var many) && many is not null ? many.ToHashSet()
        : token.TryGetPayloadValue<string>(RolesClaim, out var one) && one is not null ? new HashSet<string> { one }
        : new HashSet<string>();

    private async Task<TokenValidationParameters> ParametersAsync(CancellationToken ct) => new()
    {
        ValidIssuer = $"stytch.com/{options.StytchProjectId}",
        ValidAudience = options.StytchProjectId,
        IssuerSigningKeys = (await keys.GetAsync(ct)).Keys,
        ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
        ClockSkew = TimeSpan.FromSeconds(30),
        RequireExpirationTime = true,
    };
}
