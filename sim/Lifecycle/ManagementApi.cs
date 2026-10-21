using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Tabletop.Content;
using Tabletop.Protocol;
using Tabletop.Sim.Drivers;

namespace Tabletop.Sim.Lifecycle;

// Mirrors of Tabletop.Management's DTOs, as the client mirrors them (client/src/Core/ManagementDtos.cs), read under ContentJson.
public sealed record PropertiesSummary(string ContentHash, string Name, string GameRules);
public sealed record RuleTextDto(string Name, string Kind, bool TakesParameter, string RuleText, bool Played = true);
public sealed record CreateGameRequest(string Name, string PropertiesHash, SchedulerShape SchedulerShape, Enforcement Enforcement, bool WaitsBetweenSteps);
public sealed record GameSummary(Guid Id, string Name, GameStatus Status, Guid CreatedBy, DateTimeOffset CreatedAt, int Participants,
    IReadOnlyList<string> ParticipantNames, bool Joined);
public sealed record ArmySummary(Guid Id, string Name, string GameSystem, string Source, int Units, int Models);
public sealed record ApiPage<T>(IReadOnlyList<T> Items, Guid? Next);

/// <summary>An unexpected status from management: the method, path, status and body.</summary>
public sealed class ManagementCallException(string message) : Exception(message);

/// <summary>
/// One account's calls to management, the requests the client's ManagementClient sends, with the session the dev mint gives the account.
/// Army import goes through the dev-mode body route (DevArmyImportEndpoint), so nothing calls Army Forge.
/// </summary>
public sealed class ManagementApi : IDisposable
{
    readonly HttpClient _http;

    ManagementApi(HttpClient http, string name, MintedSession session)
    {
        _http = http;
        Name = name;
        Session = session;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Jwt);
        _http.DefaultRequestHeaders.Add(ProtocolVersion.Header, ProtocolVersion.Current.ToString());
    }

    public string Name { get; }
    public MintedSession Session { get; }
    public Guid AccountId => Session.AccountId;

    public static async Task<ManagementApi> SignInAsync(Uri management, string name, CancellationToken ct)
    {
        var http = new HttpClient { BaseAddress = management, Timeout = TimeSpan.FromSeconds(30) };
        try { return new ManagementApi(http, name, await SocketCredentials.MintOneAsync(http, name, ct)); }
        catch { http.Dispose(); throw; }
    }

    public Task<IReadOnlyList<PropertiesSummary>> PropertiesAsync(CancellationToken ct) => GetAsync<IReadOnlyList<PropertiesSummary>>("/v1/properties", ct);
    public Task<IReadOnlyList<RuleTextDto>> RulesAsync(string hash, CancellationToken ct) => GetAsync<IReadOnlyList<RuleTextDto>>($"/v1/properties/{hash}/rules", ct);
    public Task<GameInfo> CreateGameAsync(CreateGameRequest request, CancellationToken ct) => SendAsync<GameInfo>(HttpMethod.Post, "/v1/games", request, HttpStatusCode.Created, ct);
    public Task<GameInfo> JoinAsync(Guid game, CancellationToken ct) => SendAsync<GameInfo>(HttpMethod.Post, $"/v1/games/{game}/join", new { }, HttpStatusCode.OK, ct);
    public Task<Army> ImportArmyAsync(JsonObject list, CancellationToken ct) =>
        SendAsync<Army>(HttpMethod.Post, "/v1/dev/armies/import", new { source = LifecycleArmy.Source, list }, HttpStatusCode.Created, ct);

    /// <summary>The game, or null on 404.</summary>
    public async Task<GameInfo?> GameAsync(Guid game, CancellationToken ct)
    {
        using var response = await _http.GetAsync($"/v1/games/{game}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        return await ReadAsync<GameInfo>(response, HttpStatusCode.OK, ct);
    }

    /// <summary>Every game the account holds a participant row in, at every status (the client's replay list), all pages.</summary>
    public async Task<IReadOnlyList<GameSummary>> JoinedAsync(CancellationToken ct)
    {
        var all = new List<GameSummary>();
        for (Guid? after = null; ;)
        {
            var page = await GetAsync<ApiPage<GameSummary>>("/v1/games?limit=200&joined=true" + (after is null ? "" : $"&after={after}"), ct);
            all.AddRange(page.Items);
            if (page.Next is not { } next) return all;
            after = next;
        }
    }

    public async Task<IReadOnlyList<ArmySummary>> ArmiesAsync(CancellationToken ct)
    {
        var all = new List<ArmySummary>();
        for (Guid? after = null; ;)
        {
            var page = await GetAsync<ApiPage<ArmySummary>>("/v1/armies?limit=200" + (after is null ? "" : $"&after={after}"), ct);
            all.AddRange(page.Items);
            if (page.Next is not { } next) return all;
            after = next;
        }
    }

    /// <summary>The status code alone: a leave or delete is judged by the caller (204, or 404 for someone not allowed).</summary>
    public async Task<HttpStatusCode> LeaveAsync(Guid game, CancellationToken ct)
    {
        using var response = await _http.PostAsync($"/v1/games/{game}/leave", null, ct);
        return response.StatusCode;
    }

    public async Task<HttpStatusCode> DeleteGameAsync(Guid game, CancellationToken ct)
    {
        using var response = await _http.DeleteAsync($"/v1/games/{game}", ct);
        return response.StatusCode;
    }

    public async Task<HttpStatusCode> DeleteArmyAsync(Guid army, CancellationToken ct)
    {
        using var response = await _http.DeleteAsync($"/v1/armies/{army}", ct);
        return response.StatusCode;
    }

    async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct);
        return await ReadAsync<T>(response, HttpStatusCode.OK, ct);
    }

    async Task<T> SendAsync<T>(HttpMethod method, string path, object body, HttpStatusCode expected, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body, body.GetType(), options: ContentJson.Options) };
        using var response = await _http.SendAsync(request, ct);
        return await ReadAsync<T>(response, expected, ct);
    }

    static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected, CancellationToken ct)
    {
        if (response.StatusCode != expected)
            throw new ManagementCallException($"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.PathAndQuery} answered {(int)response.StatusCode}, not {(int)expected}: {await response.Content.ReadAsStringAsync(ct)}");
        return await response.Content.ReadFromJsonAsync<T>(ContentJson.Options, ct)
            ?? throw new ManagementCallException($"{response.RequestMessage?.RequestUri?.PathAndQuery} returned an empty body");
    }

    public void Dispose() => _http.Dispose();
}
