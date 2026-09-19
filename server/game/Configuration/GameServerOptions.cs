namespace Tabletop.GameServer.Configuration;

/// <summary>Every setting, one object, registered as a singleton. Bound from TABLETOP_* variables in production; tests build one directly.</summary>
public sealed class GameServerOptions
{
    public required string DbConnection { get; init; }
    public required string StytchProjectId { get; init; }
    public required string StytchJwksUrl { get; init; }
    public int MinimumClientVersion { get; init; } = Tabletop.Protocol.ProtocolVersion.Current;
    public int PresenceRelayHz { get; init; } = 20;
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan KeepAliveInterval { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan KeepAliveTimeout { get; init; } = TimeSpan.FromSeconds(60);
    public double DefaultTableWidth { get; init; } = 72;
    public double DefaultTableLength { get; init; } = 48;
    public string DefaultTextureBuiltin { get; init; } = "grass";

    public static GameServerOptions FromEnvironment(IConfiguration configuration)
    {
        string Require(string key) => configuration[key] ?? throw new InvalidOperationException($"{key} is not set (see .env.example).");
        var projectId = Require("TABLETOP_STYTCH_PROJECT_ID");
        return new GameServerOptions
        {
            DbConnection = Require("TABLETOP_DB_CONNECTION"),
            StytchProjectId = projectId,
            StytchJwksUrl = configuration["TABLETOP_STYTCH_JWKS_URL"]
                ?? $"{(configuration["TABLETOP_STYTCH_BASE_URL"] ?? "https://test.stytch.com").TrimEnd('/')}/v1/sessions/jwks/{projectId}",
            MinimumClientVersion = int.TryParse(configuration["TABLETOP_MIN_CLIENT_VERSION"], out var v) ? v : Tabletop.Protocol.ProtocolVersion.Current,
        };
    }
}
