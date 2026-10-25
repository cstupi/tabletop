using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Drivers;

/// <summary>Where the socket comes from: a real server over TCP, or a test server in memory.</summary>
public interface IWebSocketConnector
{
    Task<WebSocket> ConnectAsync(CancellationToken ct);
}

public sealed class ClientWebSocketConnector(Uri uri) : IWebSocketConnector
{
    public async Task<WebSocket> ConnectAsync(CancellationToken ct)
    {
        var socket = new ClientWebSocket { Options = { DangerousDeflateOptions = SocketCompression.Offer() } };   // as the Godot client offers it
        try { await socket.ConnectAsync(uri, ct); }
        catch { socket.Dispose(); throw; }
        return socket;
    }
}

/// <summary>A real Stytch session JWT and the account it names.</summary>
public sealed record MintedSession(string Jwt, Guid AccountId)
{
    /// <summary>The token's `exp`, read without verifying it; null when it has none or is not a JWT.</summary>
    public static DateTimeOffset? ExpiryOf(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length != 3) return null;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
            return doc.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException) { return null; }
    }
}

/// <summary>
/// One session per participant, in team-ordinal order with the recorder last. Nothing here verifies a
/// token: the game server does that against Stytch's JWKS. From the console the sessions come from
/// management's development-only mint; in tests TestKeys mints them against the fake JWKS.
/// </summary>
public sealed record SocketCredentials(IReadOnlyList<MintedSession> Sessions)
{
    public const string ManagementUrlVariable = "TABLETOP_SIM_MANAGEMENT";
    public const string MintPath = "/v1/dev/sessions";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record MintAccount(Guid Id, string Name);
    private sealed record MintResponse(string SessionJwt, MintAccount Account);

    /// <summary>
    /// The accounts every socket game plays on: participant i of any game is sim-player-i, so concurrent games and every run share
    /// them. As many as the most participants any scenario has (the recorder included).
    /// </summary>
    public const int PoolSize = 9;
    public static IReadOnlyList<string> Pool { get; } = Enumerable.Range(0, PoolSize).Select(i => $"sim-player-{i}").ToList();

    /// <summary>A fresh session for participant i's account, which the socket driver sends as a Reauth before the one it holds expires. None: no renewal.</summary>
    public Func<int, CancellationToken, Task<MintedSession>>? Refresh { get; init; }

    public static string NameFor(int index) => index >= 0 && index < PoolSize
        ? Pool[index]
        : throw new ArgumentOutOfRangeException(nameof(index), index, $"the account pool has {PoolSize} accounts");

    public static async Task<SocketCredentials> MintAsync(CompiledScenario scenario, HttpClient http, CancellationToken ct)
    {
        var sessions = new List<MintedSession>();
        for (var i = 0; i < scenario.Info.Participants.Count; i++)
            sessions.Add(await MintOneAsync(http, NameFor(i), ct));
        var management = http.BaseAddress;
        return new SocketCredentials(sessions)
        {
            Refresh = async (i, token) =>
            {
                using var fresh = new HttpClient { BaseAddress = management };
                return await MintOneAsync(fresh, NameFor(i), token);
            },
        };
    }

    /// <summary>
    /// A session for the named account. The mint makes the account and its Stytch user on first use; two first uses at once race on
    /// the Stytch create and the loser answers 409, so a 409 is asked once more, when the user exists.
    /// </summary>
    public static async Task<MintedSession> MintOneAsync(HttpClient http, string name, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var response = await http.PostAsJsonAsync(MintPath, new { name }, Json, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new InvalidOperationException(
                    $"{http.BaseAddress}{MintPath.TrimStart('/')} answered 404; the management API must run in dev mode (ASPNETCORE_ENVIRONMENT=Development and TABLETOP_DEV_MODE=1, as ./scripts/management and compose set)");
            if (response.StatusCode == HttpStatusCode.Conflict && attempt == 0) continue;
            response.EnsureSuccessStatusCode();
            var minted = await response.Content.ReadFromJsonAsync<MintResponse>(Json, ct)
                ?? throw new InvalidOperationException("the mint returned an empty body");
            return new MintedSession(minted.SessionJwt, minted.Account.Id);
        }
    }

    public string For(int index) => At(index).Jwt;
    public Guid AccountOf(int index) => At(index).AccountId;

    private MintedSession At(int index) => index < Sessions.Count
        ? Sessions[index]
        : throw new InvalidOperationException($"{Sessions.Count} sessions were minted; participant {index} needs one of its own");
}
