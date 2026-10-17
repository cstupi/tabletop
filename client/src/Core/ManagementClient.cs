using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>The one HTTP client. Every request carries the version header; the bearer is set once a session exists.</summary>
public sealed class ManagementClient(HttpClient http, Uri baseUrl)
{
    public string? Jwt { get; set; }

    // Auth — anonymous (the bearer is not sent).
    public Task<OtpSentResponse> SendOtp(string email) => Post<OtpSentResponse>("/v1/auth/otp", new { email }, anonymous: true);
    public Task<SessionResponse> VerifyOtp(string methodId, string code) => Post<SessionResponse>("/v1/auth/otp/verify", new { methodId, code }, anonymous: true);
    public Task<SessionResponse> LoginWithPassword(string email, string password) => Post<SessionResponse>("/v1/auth/password", new { email, password }, anonymous: true);
    public Task<SessionResponse> SignUp(string email, string password) => Post<SessionResponse>("/v1/auth/password/signup", new { email, password }, anonymous: true);
    public Task<SessionResponse> Refresh(string sessionJwt) => Post<SessionResponse>("/v1/auth/session/refresh", new { sessionJwt }, anonymous: true);
    public Task Logout() => Send(HttpMethod.Delete, "/v1/auth/session", null, false);

    public Task<AccountRow> GetAccount() => Get<AccountRow>("/v1/account");
    public async Task<AccountRow> RenameAccount(string name) => Read<AccountRow>(await Send(HttpMethod.Patch, "/v1/account", new { name }, false));
    public Task<Page<GameSummary>> ListGames(Guid? after, int limit = 50) => Get<Page<GameSummary>>($"/v1/games?limit={limit}" + (after is null ? "" : $"&after={after}"));
    public Task<GameInfo> CreateGame(CreateGameRequest request) => Post<GameInfo>("/v1/games", request);
    public Task<GameInfo> GetGame(Guid id) => Get<GameInfo>($"/v1/games/{id}");
    public Task<GameInfo> JoinGame(Guid id) => Post<GameInfo>($"/v1/games/{id}/join", new { });
    public Task LeaveGame(Guid id) => Send(HttpMethod.Post, $"/v1/games/{id}/leave", null, false);
    /// <summary>The creator's: the game ends for everyone. DeleteGame removes it and its log.</summary>
    public Task EndGame(Guid id) => Send(HttpMethod.Post, $"/v1/games/{id}/end", null, false);
    public Task DeleteGame(Guid id) => Send(HttpMethod.Delete, $"/v1/games/{id}", null, false);
    public Task<IReadOnlyList<PropertiesSummary>> ListProperties() => Get<IReadOnlyList<PropertiesSummary>>("/v1/properties");
    public Task<IReadOnlyList<RuleTextDto>> GetRuleTexts(string propertiesHash) => Get<IReadOnlyList<RuleTextDto>>($"/v1/properties/{propertiesHash}/rules");
    public Task<Page<ArmySummary>> ListArmies(Guid? after) => Get<Page<ArmySummary>>("/v1/armies?limit=200" + (after is null ? "" : $"&after={after}"));
    public Task<Army> GetArmy(Guid id) => Get<Army>($"/v1/armies/{id}");
    public Task<IReadOnlyList<LayoutSummary>> ListLayouts() => Get<IReadOnlyList<LayoutSummary>>("/v1/layouts");
    public Task<TableState> GetLayout(Guid id) => Get<TableState>($"/v1/layouts/{id}");
    public Task<TableState> SaveLayout(TableState layout) => Post<TableState>("/v1/layouts", layout);
    public Task<Manifest> GetManifest(Guid gameId) => Get<Manifest>($"/v1/games/{gameId}/assets");
    public Task<Page<AssetDto>> ListAssets(AssetKind kind, Guid? after) => Get<Page<AssetDto>>($"/v1/assets?limit=200&kind={kind.ToString().ToLowerInvariant()}" + (after is null ? "" : $"&after={after}"));
    public Task<IReadOnlyList<TerrainVolume>> GetAssetVolumes(Guid assetId) => Get<IReadOnlyList<TerrainVolume>>($"/v1/assets/{assetId}/volumes");
    public Task<DownloadResponse> GetDownload(Guid assetId) => Get<DownloadResponse>($"/v1/assets/{assetId}/download");

    async Task<T> Get<T>(string path) => Read<T>(await Send(HttpMethod.Get, path, null, false));
    async Task<T> Post<T>(string path, object body, bool anonymous = false) => Read<T>(await Send(HttpMethod.Post, path, body, anonymous));

    async Task<string> Send(HttpMethod method, string path, object? body, bool anonymous)
    {
        using var request = new HttpRequestMessage(method, new Uri(baseUrl, path));
        request.Headers.Add(ProtocolVersion.Header, ProtocolVersion.Current.ToString());
        if (!anonymous && Jwt is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Jwt);
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body, body.GetType(), ContentJson.Options), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode) return text;
        var (title, detail) = Problem(text, response.ReasonPhrase ?? response.StatusCode.ToString());
        if ((int)response.StatusCode == ProtocolVersion.UpgradeRequiredStatusCode) throw new UpdateRequiredException(title, detail);
        throw new ManagementException((int)response.StatusCode, title, detail);
    }

    static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, ContentJson.Options) ?? throw new ManagementException(0, "Empty response", null);

    static (string Title, string? Detail) Problem(string text, string fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var title = doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() : null;
            var detail = doc.RootElement.TryGetProperty("detail", out var d) ? d.GetString() : null;
            return (title ?? fallback, detail);
        }
        catch (JsonException) { return (fallback, text.Length == 0 ? null : text); }
    }
}
