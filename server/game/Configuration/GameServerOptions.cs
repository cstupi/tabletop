using Tabletop.GameServer.Session;

namespace Tabletop.GameServer.Configuration;

/// <summary>Every setting, one object, registered as a singleton. Bound from TABLETOP_* variables in production; tests build one directly.</summary>
public sealed class GameServerOptions
{
    public required string DbConnection { get; init; }
    public required string StytchProjectId { get; init; }
    public required string StytchJwksUrl { get; init; }
    public int MinimumClientVersion { get; init; } = Tabletop.Protocol.ProtocolVersion.Current;
    public int PresenceRelayHz { get; init; } = 20;
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan KeepAliveInterval { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan KeepAliveTimeout { get; init; } = TimeSpan.FromSeconds(60);
    public double DefaultTableWidth { get; init; } = 72;
    public double DefaultTableLength { get; init; } = 48;
    public string DefaultTextureBuiltin { get; init; } = "grass";
    /// <summary>Whether the engine takes SetDice, the rules suite's way to fix a roll's faces. On only in dev mode (StartupGuard.DevMode: Development and TABLETOP_DEV_MODE=1; the compose game server the suite's socket driver targets); players never set dice.</summary>
    public bool AcceptsSetDice { get; init; }

    // ---- connection limits (security review: H1, H4, M2) -------------------------------------------------------------
    /// <summary>Kestrel's MaxConcurrentConnections and MaxConcurrentUpgradedConnections.</summary>
    public long MaxConnections { get; init; } = 2000;
    public long MaxUpgradedConnections { get; init; } = 1000;
    /// <summary>Open sockets per account across every game; the oldest is closed when a fifth arrives.</summary>
    public int SocketsPerAccount { get; init; } = 4;
    /// <summary>The Connect frame (a JWT and two numbers), every later frame, and a ReplaceDraft (a whole layout at the draft caps).</summary>
    public int ConnectFrameBytes { get; init; } = 8 * 1024;
    public int FrameBytes { get; init; } = 64 * 1024;
    public int DraftFrameBytes { get; init; } = 512 * 1024;
    public int PresenceFrameBytes { get; init; } = 16 * 1024;
    /// <summary>What a socket may hold for a peer that is not reading. The connect sequence queues the whole log at once, so these bound a reconnect's burst too.</summary>
    public SocketLimits Outbound { get; init; } = new(20_000, 64L * 1024 * 1024);
    /// <summary>Commands, edits and sight queries share one bucket; the read loop waits for a token. Pings and presence over theirs are dropped.</summary>
    public FrameRate ActionRate { get; init; } = new(20, 40);
    public FrameRate PingRate { get; init; } = new(3, 6);
    public FrameRate PresenceRate { get; init; } = new(30, 60);
    /// <summary>What one setup draft may hold (counts, text, sizes).</summary>
    public Setup.DraftLimits Draft { get; init; } = Setup.DraftLimits.Default;
    /// <summary>What a picked army may hold (bases, stats, parameters, counts).</summary>
    public Setup.ArmyLimits Army { get; init; } = Setup.ArmyLimits.Default;
    /// <summary>A game whose log reaches this many events refuses further commands.</summary>
    public long MaxEventsPerGame { get; init; } = 200_000;


    /// <summary>TABLETOP_STYTCH_JWKS_URL, else derived from TABLETOP_STYTCH_BASE_URL (default Stytch's test environment) and the project id.</summary>
    public static string JwksUrl(IConfiguration configuration, string projectId) => configuration["TABLETOP_STYTCH_JWKS_URL"]
        ?? $"{(configuration["TABLETOP_STYTCH_BASE_URL"] ?? "https://test.stytch.com").TrimEnd('/')}/v1/sessions/jwks/{projectId}";

    public static GameServerOptions FromEnvironment(IConfiguration configuration, bool development)
    {
        string Require(string key) => configuration[key] ?? throw new InvalidOperationException($"{key} is not set (see .env.example).");
        long Long(string key, long fallback) => long.TryParse(configuration[key], out var v) && v > 0 ? v : fallback;
        double Double(string key, double fallback) => double.TryParse(configuration[key], System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0 ? v : fallback;
        var projectId = Require("TABLETOP_STYTCH_PROJECT_ID");
        var defaults = new GameServerOptions { DbConnection = "", StytchProjectId = "", StytchJwksUrl = "" };
        return new GameServerOptions
        {
            DbConnection = Require("TABLETOP_DB_CONNECTION"),
            StytchProjectId = projectId,
            StytchJwksUrl = JwksUrl(configuration, projectId),
            MinimumClientVersion = int.TryParse(configuration["TABLETOP_MIN_CLIENT_VERSION"], out var v) ? v : Tabletop.Protocol.ProtocolVersion.Current,
            AcceptsSetDice = development,
            MaxConnections = Long("TABLETOP_MAX_CONNECTIONS", defaults.MaxConnections),
            MaxUpgradedConnections = Long("TABLETOP_MAX_UPGRADED_CONNECTIONS", defaults.MaxUpgradedConnections),
            SocketsPerAccount = (int)Long("TABLETOP_SOCKETS_PER_ACCOUNT", defaults.SocketsPerAccount),
            ActionRate = new FrameRate(Double("TABLETOP_FRAME_RATE", defaults.ActionRate.PerSecond), (int)Long("TABLETOP_FRAME_BURST", defaults.ActionRate.Burst)),
            MaxEventsPerGame = Long("TABLETOP_MAX_EVENTS_PER_GAME", defaults.MaxEventsPerGame),
        };
    }
}
