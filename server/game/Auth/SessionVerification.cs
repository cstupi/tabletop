using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Tabletop.GameServer.Configuration;

namespace Tabletop.GameServer.Auth;

/// <summary>Is this session valid, and whose is it. Null means no: the socket is rejected with invalid_session.</summary>
public interface ISessionVerifier { Task<VerifiedSession?> VerifyAsync(string jwt, CancellationToken ct); }

public sealed record VerifiedSession(Guid AccountId, string StytchUserId, DateTimeOffset ExpiresAt);

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
    private readonly JsonWebTokenHandler _handler = new();

    public async Task<VerifiedSession?> VerifyAsync(string jwt, CancellationToken ct)
    {
        if (!_handler.CanReadToken(jwt)) return null;
        var result = await _handler.ValidateTokenAsync(jwt, await ParametersAsync(ct));
        if (!result.IsValid)
        {
            if (keys is HttpJwksSource http) { http.Invalidate(); result = await _handler.ValidateTokenAsync(jwt, await ParametersAsync(ct)); }
            if (!result.IsValid) return null;
        }
        var token = (JsonWebToken)result.SecurityToken;
        if (!token.TryGetPayloadValue<string>(AccountClaim, out var accountText) || !Guid.TryParse(accountText, out var accountId)) return null;
        return new VerifiedSession(accountId, token.Subject, token.ValidTo);
    }

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
