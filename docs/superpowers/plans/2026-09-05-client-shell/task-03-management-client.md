# Task 3: The management client

**Files:**
- Create: `client/src/Core/ManagementDtos.cs`, `client/src/Core/ManagementClient.cs`
- Test: `tests/Tabletop.Client.Tests/FakeManagement.cs`, `tests/Tabletop.Client.Tests/ManagementClientTests.cs`

**Interfaces:**
- Consumes: `ProtocolVersion.Header/Current/UpgradeRequiredStatusCode`, `GameInfo`, `GameStatus`, `SchedulerShape`, `Enforcement` (Protocol); `Army`, `TableState`, `TerrainVolume`, `ContentJson` (Content); `TableSize`, `Orientation`, `Base` (Geometry); the management plan's HTTP surface (its *Interfaces → Produces → HTTP surface* table).
- Produces (namespace `Tabletop.Client.Core`): mirrors of the management DTOs the client reads, byte-compatible under `ContentJson` — `AccountRow(Guid Id, string Name)`, `SessionResponse(string SessionJwt, AccountRow Account)`, `OtpSentResponse(string MethodId)`, `Page<T>(IReadOnlyList<T> Items, Guid? Next)`, `GameSummary(Guid Id, string Name, GameStatus Status, Guid CreatedBy, DateTimeOffset CreatedAt, int Participants)`, `CreateGameRequest(string Name, string PropertiesHash, SchedulerShape SchedulerShape, Enforcement Enforcement, bool WaitsBetweenSteps)`, `PropertiesSummary(string ContentHash, string Name, string GameRules)`, `ArmySummary(Guid Id, string Name, string GameSystem, string Source, int Units, int Models)`, `LayoutSummary(Guid Id, string Name, TableSize Size, int Terrain, int Markers)`, `AssetKind { Miniature, Terrain, Texture }`, `AssetDto(Guid Id, string Name, AssetKind Kind, string Hash, long ByteSize, string ContentType, bool Uploaded, double Scale, Orientation Orientation, Base? ModelledBase)`, `DownloadResponse(Uri Url, DateTimeOffset ExpiresAt)`, `ManifestEntry(Guid AssetId, AssetKind Kind, string Hash, Uri Url, double Scale, Orientation Orientation)` (the owner's import corrections ride on the manifest — management plan Task 12), `Manifest(IReadOnlyList<ManifestEntry> Assets, DateTimeOffset ExpiresAt)`; `ManagementException(int Status, string Title, string? Detail)`; `UpdateRequiredException : ManagementException`.
- `sealed class ManagementClient(HttpClient http, Uri baseUrl)` with `string? Jwt { get; set; }` and: `SendOtp(email)`, `VerifyOtp(methodId, code)`, `LoginWithPassword(email, password)`, `SignUp(email, password)`, `Refresh(jwt)`, `Logout()`, `GetAccount()`, `ListGames(Guid? after, int limit = 50)`, `CreateGame(CreateGameRequest)`, `GetGame(Guid)`, `JoinGame(Guid)`, `LeaveGame(Guid)`, `ListProperties()`, `ListArmies(Guid? after)`, `GetArmy(Guid)`, `ListLayouts()`, `GetLayout(Guid)`, `SaveLayout(TableState)`, `GetManifest(Guid gameId)`, `ListAssets(AssetKind kind, Guid? after)`, `GetAssetVolumes(Guid)`, `GetDownload(Guid)` — all `Task<T>`, all throwing `ManagementException` on a non-2xx with the problem body's title and detail, `UpdateRequiredException` on `426`.

Spec: client shell → Screens (login by password or one-time code through management; Home lists `GET /v1/games`, creates with `GET /v1/properties` and `GET /v1/armies`; the version header on every request; Leave calls `POST /v1/games/{id}/leave`); setup phase → Layouts (`POST /v1/layouts`, `GET /v1/layouts/{id}`); management API → The version check; management plan → HTTP surface (the exact paths and shapes; `Page<T>` with `?limit&after`; problem+json errors; `POST /v1/auth/session/refresh` accepts an expired JWT).

- [ ] **Step 1: Write `FakeManagement.cs` — a handler that records requests and answers from a script**

```csharp
using System.Net;
using System.Text;
using Tabletop.Content;

namespace Tabletop.Client.Tests;

public sealed class FakeManagement : HttpMessageHandler
{
    public readonly List<HttpRequestMessage> Requests = new();
    public readonly List<string> Bodies = new();
    readonly Queue<HttpResponseMessage> _answers = new();

    public FakeManagement Answer(HttpStatusCode status, object? body = null, string mediaType = "application/json")
    {
        var content = body is null ? null : new StringContent(body as string ?? ContentJson.Serialize(body), Encoding.UTF8, mediaType);
        _answers.Enqueue(new HttpResponseMessage(status) { Content = content });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
        return _answers.Count > 0 ? _answers.Dequeue() : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
    }
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
using System.Net;
using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

public class ManagementClientTests
{
    static (ManagementClient Client, FakeManagement Fake) Make() { var f = new FakeManagement(); return (new ManagementClient(new HttpClient(f), new Uri("http://localhost:5100")), f); }

    [Fact]
    public async Task Every_request_carries_the_version_header_and_the_bearer_when_logged_in()
    {
        var (c, f) = Make();
        f.Answer(HttpStatusCode.OK, new AccountRow(Guid.CreateVersion7(), "Clay"));
        c.Jwt = "jwt-1";
        await c.GetAccount();
        var r = f.Requests.Single();
        Assert.Equal(ProtocolVersion.Current.ToString(), r.Headers.GetValues(ProtocolVersion.Header).Single());
        Assert.Equal("Bearer jwt-1", r.Headers.Authorization!.ToString());
        Assert.Equal("http://localhost:5100/v1/account", r.RequestUri!.ToString());
    }

    [Fact]
    public async Task Login_paths_and_shapes()
    {
        var (c, f) = Make();
        var account = new AccountRow(Guid.CreateVersion7(), "clay");
        f.Answer(HttpStatusCode.OK, new OtpSentResponse("email-1")).Answer(HttpStatusCode.OK, new SessionResponse("jwt-2", account))
         .Answer(HttpStatusCode.OK, new SessionResponse("jwt-3", account)).Answer(HttpStatusCode.Created, new SessionResponse("jwt-4", account))
         .Answer(HttpStatusCode.OK, new SessionResponse("jwt-5", account)).Answer(HttpStatusCode.NoContent);
        Assert.Equal("email-1", (await c.SendOtp("clay@example.com")).MethodId);
        Assert.Equal("jwt-2", (await c.VerifyOtp("email-1", "123456")).SessionJwt);
        Assert.Equal("jwt-3", (await c.LoginWithPassword("clay@example.com", "pw")).SessionJwt);
        Assert.Equal("jwt-4", (await c.SignUp("clay@example.com", "pw")).SessionJwt);
        Assert.Equal("jwt-5", (await c.Refresh("jwt-old")).SessionJwt);
        c.Jwt = "jwt-5"; await c.Logout();
        Assert.Equal(["/v1/auth/otp", "/v1/auth/otp/verify", "/v1/auth/password", "/v1/auth/password/signup", "/v1/auth/session/refresh", "/v1/auth/session"], f.Requests.Select(r => r.RequestUri!.AbsolutePath));
        Assert.Equal(HttpMethod.Delete, f.Requests[5].Method);
        Assert.Contains("\"methodId\":\"email-1\"", f.Bodies[1]);
        Assert.Null(f.Requests[0].Headers.Authorization);
    }

    [Fact]
    public async Task A_426_is_the_update_required_exception_and_problems_carry_their_title()
    {
        var (c, f) = Make();
        f.Answer((HttpStatusCode)426, "{\"title\":\"Update required\",\"detail\":\"This server accepts Tabletop-Client-Version >= 2.\",\"status\":426}", "application/problem+json");
        var up = await Assert.ThrowsAsync<UpdateRequiredException>(() => c.ListProperties());
        Assert.Equal(426, up.Status);
        f.Answer(HttpStatusCode.Unauthorized, "{\"title\":\"Unauthorized\",\"detail\":\"unauthorized_credentials\",\"status\":401}", "application/problem+json");
        var ex = await Assert.ThrowsAsync<ManagementException>(() => c.LoginWithPassword("a", "b"));
        Assert.Equal((401, "Unauthorized", "unauthorized_credentials"), (ex.Status, ex.Title, ex.Detail));
    }

    [Fact]
    public async Task Games_layouts_and_the_manifest_round_trip_contract_types()
    {
        var (c, f) = Make();
        var game = Samples.Game;
        var layout = Tabletop.Content.Tests.TableStateTests.Sample();
        f.Answer(HttpStatusCode.OK, new Page<GameSummary>([new GameSummary(game.Id, "Friday", GameStatus.Lobby, game.CreatedBy, DateTimeOffset.UnixEpoch, 1)], game.Id))
         .Answer(HttpStatusCode.Created, game).Answer(HttpStatusCode.OK, game).Answer(HttpStatusCode.NoContent)
         .Answer(HttpStatusCode.Created, layout).Answer(HttpStatusCode.OK, new Manifest([new ManifestEntry(Guid.CreateVersion7(), AssetKind.Miniature, new string('a', 64), new Uri("http://localhost:9000/b/k"), 2.5, new Tabletop.Geometry.Orientation(90, 0, 0))], DateTimeOffset.UnixEpoch));
        var page = await c.ListGames(null, 25);
        Assert.Equal("http://localhost:5100/v1/games?limit=25", f.Requests[0].RequestUri!.ToString());
        Assert.Equal(game.Id, page.Next);
        Assert.Equal(ContentJson.Serialize(game), ContentJson.Serialize(await c.CreateGame(new CreateGameRequest("Friday", "sha256:abc", SchedulerShape.AlternatingActivation, Enforcement.Warn, false))));
        Assert.Contains("\"schedulerShape\":\"alternating-activation\"", f.Bodies[1]);
        await c.JoinGame(game.Id); await c.LeaveGame(game.Id);
        Assert.Equal($"/v1/games/{game.Id}/leave", f.Requests[3].RequestUri!.AbsolutePath);
        Assert.Equal(ContentJson.Serialize(layout), ContentJson.Serialize(await c.SaveLayout(layout)));
        var entry = (await c.GetManifest(game.Id)).Assets.Single();
        Assert.Equal((AssetKind.Miniature, 2.5, new Tabletop.Geometry.Orientation(90, 0, 0)), (entry.Kind, entry.Scale, entry.Orientation));
        Assert.Equal($"/v1/games/{game.Id}/assets", f.Requests[5].RequestUri!.AbsolutePath);
    }
}
```

- [ ] **Step 3: Run; expect compile errors. Write `ManagementDtos.cs`**

```csharp
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

// Mirrors of Tabletop.Management's DTOs (management plan → Interfaces → Produces). Same property names, same JSON under ContentJson.
public sealed record AccountRow(Guid Id, string Name);
public sealed record SessionResponse(string SessionJwt, AccountRow Account);
public sealed record OtpSentResponse(string MethodId);
public sealed record Page<T>(IReadOnlyList<T> Items, Guid? Next);
public sealed record GameSummary(Guid Id, string Name, GameStatus Status, Guid CreatedBy, DateTimeOffset CreatedAt, int Participants);
public sealed record CreateGameRequest(string Name, string PropertiesHash, SchedulerShape SchedulerShape, Enforcement Enforcement, bool WaitsBetweenSteps);
public sealed record PropertiesSummary(string ContentHash, string Name, string GameRules);
public sealed record ArmySummary(Guid Id, string Name, string GameSystem, string Source, int Units, int Models);
public sealed record LayoutSummary(Guid Id, string Name, TableSize Size, int Terrain, int Markers);
public enum AssetKind { Miniature, Terrain, Texture }
public sealed record AssetDto(Guid Id, string Name, AssetKind Kind, string Hash, long ByteSize, string ContentType, bool Uploaded, double Scale, Orientation Orientation, Base? ModelledBase);
public sealed record DownloadResponse(Uri Url, DateTimeOffset ExpiresAt);
public sealed record ManifestEntry(Guid AssetId, AssetKind Kind, string Hash, Uri Url, double Scale, Orientation Orientation);
public sealed record Manifest(IReadOnlyList<ManifestEntry> Assets, DateTimeOffset ExpiresAt);

/// <summary>A non-2xx answer: the problem+json title and detail when the body had them.</summary>
public class ManagementException(int status, string title, string? detail) : Exception(detail is null ? title : $"{title}: {detail}")
{
    public int Status { get; } = status;
    public string Title { get; } = title;
    public string? Detail { get; } = detail;
}

/// <summary>426: this build is older than the server accepts. The client shows the update screen and stops.</summary>
public sealed class UpdateRequiredException(string title, string? detail) : ManagementException(ProtocolVersion.UpgradeRequiredStatusCode, title, detail);
```

- [ ] **Step 4: Write `ManagementClient.cs`**

```csharp
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
    public Task<Page<GameSummary>> ListGames(Guid? after, int limit = 50) => Get<Page<GameSummary>>($"/v1/games?limit={limit}" + (after is null ? "" : $"&after={after}"));
    public Task<GameInfo> CreateGame(CreateGameRequest request) => Post<GameInfo>("/v1/games", request);
    public Task<GameInfo> GetGame(Guid id) => Get<GameInfo>($"/v1/games/{id}");
    public Task<GameInfo> JoinGame(Guid id) => Post<GameInfo>($"/v1/games/{id}/join", new { });
    public Task LeaveGame(Guid id) => Send(HttpMethod.Post, $"/v1/games/{id}/leave", null, false);
    public Task<IReadOnlyList<PropertiesSummary>> ListProperties() => Get<IReadOnlyList<PropertiesSummary>>("/v1/properties");
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
```

- [ ] **Step 5: Run `dotnet test tests/Tabletop.Client.Tests`; expect 11 passed. Commit: `Add the management client with the version header and problem mapping`.**
