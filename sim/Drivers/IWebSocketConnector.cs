using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
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
        var socket = new ClientWebSocket();
        try { await socket.ConnectAsync(uri, ct); }
        catch { socket.Dispose(); throw; }
        return socket;
    }
}

/// <summary>A real Stytch session JWT and the account it names.</summary>
public sealed record MintedSession(string Jwt, Guid AccountId);

/// <summary>
/// One session per participant, in team-ordinal order with the recorder last. Nothing here verifies a
/// token: the game server does that against Stytch's JWKS. From the console the sessions come from
/// management's development-only mint; in tests TestKeys mints them against the fake JWKS.
/// </summary>
public sealed record SocketCredentials(IReadOnlyList<MintedSession> Sessions)
{
    public const string ManagementUrlVariable = "TABLETOP_MANAGEMENT_URL";
    public const string MintPath = "/v1/dev/sessions";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record MintAccount(Guid Id, string Name);
    private sealed record MintResponse(string SessionJwt, MintAccount Account);

    /// <summary>Stable across runs and distinct within a scenario, so a re-run mints for the same accounts.</summary>
    public static string NameFor(CompiledScenario scenario, int index) => $"sim-{scenario.Scenario.Name}-{index}";

    public static async Task<SocketCredentials> MintAsync(CompiledScenario scenario, HttpClient http, CancellationToken ct)
    {
        var sessions = new List<MintedSession>();
        for (var i = 0; i < scenario.Info.Participants.Count; i++)
        {
            using var response = await http.PostAsJsonAsync(MintPath, new { name = NameFor(scenario, i) }, Json, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new InvalidOperationException(
                    $"{http.BaseAddress}{MintPath.TrimStart('/')} answered 404; the management API must run as a development environment (./scripts/management sets ASPNETCORE_ENVIRONMENT=Development)");
            response.EnsureSuccessStatusCode();
            var minted = await response.Content.ReadFromJsonAsync<MintResponse>(Json, ct)
                ?? throw new InvalidOperationException("the mint returned an empty body");
            sessions.Add(new MintedSession(minted.SessionJwt, minted.Account.Id));
        }
        return new SocketCredentials(sessions);
    }

    public string For(int index) => At(index).Jwt;
    public Guid AccountOf(int index) => At(index).AccountId;

    private MintedSession At(int index) => index < Sessions.Count
        ? Sessions[index]
        : throw new InvalidOperationException($"{Sessions.Count} sessions were minted; participant {index} needs one of its own");
}
