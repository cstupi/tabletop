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

public interface IJwksSource { Task<JsonWebKeySet> GetAsync(CancellationToken ct); }

/// <summary>Fetches Stytch's public keys and caches them; Verify asks again when a kid is unknown (key rotation).</summary>
public sealed class HttpJwksSource(HttpClient http, GameServerOptions options) : IJwksSource
{
    private JsonWebKeySet? _cached;
    private DateTimeOffset _fetchedAt;

    public async Task<JsonWebKeySet> GetAsync(CancellationToken ct)
    {
        if (_cached is not null && DateTimeOffset.UtcNow - _fetchedAt < TimeSpan.FromMinutes(10)) return _cached;
        var json = await http.GetStringAsync(options.StytchJwksUrl, ct);
        _cached = new JsonWebKeySet(json);
        _fetchedAt = DateTimeOffset.UtcNow;
        return _cached;
    }

    public void Invalidate() => _cached = null;
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
                if (!currentKeys.Keys.Any(k => k.KeyId == headerToken.Kid) && keys is HttpJwksSource http)
                {
                    http.Invalidate();
                    result = await _handler.ValidateTokenAsync(jwt, await ParametersAsync(ct));
                }
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
