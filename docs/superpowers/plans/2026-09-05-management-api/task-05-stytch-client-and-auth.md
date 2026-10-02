# Task 5: The Stytch client, session completion, `/v1/auth/*` and the development-only mint

**Files:**
- Create: `server/management/Identity/StytchClient.cs`, `server/management/Identity/Sessions.cs`, `server/management/Identity/AuthEndpoints.cs`, `server/management/Identity/DevSessions.cs`
- Modify: `server/management/Program.cs`
- Create: `tests/Tabletop.Management.Tests/FakeStytch.cs`, `tests/Tabletop.Management.Tests/AuthTests.cs`, `tests/Tabletop.Management.Tests/DevSessionTests.cs`
- Modify: `tests/Tabletop.Management.Tests/ManagementFactory.cs`

**Interfaces:**
- Consumes: `AccountRepository.Insert/Get`; `StytchAuthentication.AccountClaim`; `ManagementOptions.StytchBaseUrl/StytchProjectId/StytchSecret/SessionMinutes/WebUrl`.
- Produces: `StytchClient(HttpClient, ManagementOptions)` over the named client `StytchClient.HttpClientName = "stytch"` with `SendEmailOtp(email) : Task<OtpSent(string MethodId, string UserId, bool UserCreated)>`, `AuthenticateOtp(methodId, code)`, `AuthenticatePassword(email, password)`, `CreatePassword(email, password)`, `StartPasswordReset(email, redirectUrl) : Task`, `CompletePasswordReset(token, password)`, `RefreshSession(sessionJwt)`, `RevokeSession(sessionJwt) : Task`, `SetExternalId(userId, externalId) : Task`, `DeleteUser(externalId) : Task`, and four used only by the development mint below — `FindUserId(externalId) : Task<string?>`, `CreateUser(email, externalId) : Task<string>`, `CreateEmbeddableToken(userId) : Task<string>`, `AuthenticateMagicLink(token) : Task<StytchSession>` — each authenticate returns `StytchSession(string SessionJwt, StytchUser User)`, `StytchUser(string UserId, string? ExternalId, IReadOnlyList<StytchEmail> Emails)`, `StytchEmail(string Email)`; failures throw `StytchException(int Status, string ErrorType, string Message)`.
- `Sessions(StytchClient, AccountRepository).Complete(StytchSession) : Task<SessionResponse>` — the one path every login takes: read `user.external_id`; when present, make sure the account row exists; when absent, link first with `PUT /v1/users/{user_id} {external_id}` for a fresh `Guid.CreateVersion7()`, then insert the account row (name = the email's local part). Then read the minted JWT's `account_id` claim: when it is the account id, hand the JWT back; otherwise call `StytchClient.StampAccount` — `POST /v1/sessions/authenticate` with `session_jwt`, `session_duration_minutes` and `session_custom_claims: {"account_id": "<uuid>"}` — and hand back that JWT. The order is what makes a partial failure harmless: a Stytch failure at the link leaves no row behind, and a failure after it self-heals on the next login, which finds the user linked and takes the branch above, whose `Insert` writes the row that is missing. `SessionResponse(string SessionJwt, AccountRow Account)`.
- **Why the stamp, on every new session** — a session minted by the call that consumes the OTP code or the signup password carries no `account_id`: on a brand-new account that call runs *before* we know the account id, and for a linked account nothing has put the claim on the new session. A JWT cannot be modified once minted, so one `sessions/authenticate` with `session_custom_claims` sets `account_id` on the session. Stytch keeps session custom claims on the session, so every later JWT it mints, including each refresh, carries the claim with no further call. The alternative — never mint until the user is linked — cannot work for OTP: the code is single-use, so the session must be minted by the call that spends it, and linking earlier at `POST /v1/auth/otp` would create an account row for any email anyone types. A Stytch dashboard claim template is rejected: it is configuration outside the repo whose absence fails silently. The cost is one extra Stytch call per new session (signup, password login, OTP login, password-reset completion, dev session mint) and none per refresh.
- Endpoints (anonymous unless noted): `POST /v1/auth/otp {email} → {methodId}`; `POST /v1/auth/otp/verify {methodId, code} → SessionResponse`; `POST /v1/auth/password {email, password} → SessionResponse`; `POST /v1/auth/password/signup {email, password} → 201 SessionResponse`; `POST /v1/auth/password/reset {email} → 202`; `POST /v1/auth/password/reset/complete {token, password} → SessionResponse`; `POST /v1/auth/session/refresh {sessionJwt} → SessionResponse` (an expired JWT is accepted while the Stytch session lives); `DELETE /v1/auth/session` (bearer) `→ 204` whatever Stytch answers, since a logout has nothing to roll back. Stytch `401/404` → `401`; `duplicate_email` → `409`; `429` → `429`; `weak_password`/other `4xx` → `422`; `5xx`, a transport failure and an unparseable body → `502`.
- **`POST /v1/dev/sessions` — the development-only mint.** Body `DevSessionRequest(string Name)`, response the ordinary `SessionResponse(SessionJwt, Account)`. The simulator's socket driver needs a real session token per team, hand-pasting them is rejected, and management is the only process holding the Stytch secret, so the mint lives here and returns a **real Stytch session JWT** (simulation design → Two drivers behind one interface). `DevSessions.Mint(name)` does four things: derives the account id from the name (`AccountIdFor`, a name-based UUIDv8 — SHA-256 over a fixed namespace and the name, the first sixteen bytes, version and variant stamped), inserts the `account` row (`on conflict do nothing`, so a repeated name is the same row), finds the Stytch user by `GET /v1/users/{accountId}` and creates it with `POST /v1/users {email, external_id = accountId}` when that is `404`, then mints by `POST /v1/magic_links` followed by `POST /v1/magic_links/authenticate` and hands the session to `Sessions.Complete` like every other login. Because `external_id` is set at user creation, `Complete` skips the link and stamps `account_id` on the new session: one `StampAccount` call per mint.
  - **Gating is the hosting environment, not a flag.** The route is registered inside `if (app.Environment.IsDevelopment())` and the service inside `if (builder.Environment.IsDevelopment())`. `IHostEnvironment.EnvironmentName` defaults to `Production` when neither `ASPNETCORE_ENVIRONMENT` nor `DOTNET_ENVIRONMENT` is set, so a published build serves no such route unless someone deliberately names the environment `Development`. There is no configuration key, so no deployment can turn it on by mistake, and the test host's environment is `Testing`, which is also not development.
  - **An account that does not exist is created.** The name is the input and the account id is a function of it, so the endpoint has nothing to look up and nothing to refuse: the row is inserted if absent and reused if present. That is what lets `SocketRowSeeder` create its own accounts instead of requiring three to exist first.
  - **Idempotent for a repeated name.** Same name → same account id → same `account` row → same Stytch user, found by `external_id` rather than created again. Only the JWT differs, because each mint is a fresh session. A first mint costs five Stytch calls, every later one four.
  - **The email is derived and never used.** `EmailFor(name)` is the name lowercased with every non-alphanumeric character replaced by `-`, at `example.com` (RFC 2606, reserved and undeliverable). Nothing in this path sends mail: `POST /v1/users` does not, and an embeddable magic link is explicitly the endpoint that hands the token back instead of emailing it. Two different names that slug to the same address collide as Stytch `duplicate_email` → `409`.
  - **No second signing key and no local trust anchor.** The JWT is minted by Stytch, signed by Stytch's key, and verified by both servers against Stytch's JWKS exactly as a player's is. That is the whole point: a bypass in the verifier would leave the socket driver testing something the product does not do.
- Stytch endpoints, from the Consumer API docs: `POST /v1/otps/email/login_or_create` (`email`, `expiration_minutes` 1–10, default 2; returns `user_id`, **`email_id`**, `user_created` — there is no `method_id` in the response, and `email_id` is what the next call sends as `method_id`); `POST /v1/otps/authenticate` (`method_id`, `code`, `session_duration_minutes`, range 5–527040); `POST /v1/passwords/authenticate` (`email`, `password`); `POST /v1/passwords` (`email`, `password`; error types `duplicate_email`, `weak_password` for a strength score ≤ 2, and `breached_password` for a leaked one — two distinct errors, both `4xx`, both `422` here); `POST /v1/passwords/email/reset/start` (`email`, `reset_password_redirect_url`); `POST /v1/passwords/email/reset` (`token`, `password`); `POST /v1/sessions/authenticate` (`session_jwt`, `session_duration_minutes` — we send no `session_custom_claims`; "You may provide a JWT that needs to be refreshed and is expired according to its `exp` claim", which is what `/session/refresh` relies on, and the response's `session_jwt` is freshly minted and carries the session's custom claims, so a refresh needs none); `POST /v1/sessions/revoke` (**exactly one** of `session_id`, `session_token`, `session_jwt` — sending more than one is an error, so only `session_jwt` goes); `PUT /v1/users/{user_id}` (`external_id`: alphanumeric, `.`, `_`, `-`, `|`, ≤128 — a GUID string fits); `DELETE /v1/users/{user_id}` ("You may use an `external_id` here if one is set for the user"). All Basic-auth `project_id:secret` against `https://test.stytch.com`.
- Four more, used only by `POST /v1/dev/sessions`: `GET /v1/users/{user_id}` (the same external-id substitution as the delete, so the account id is the lookup key and `404` is `user_not_found`); `POST /v1/users` (`email` required, `external_id` optional and accepting a GUID string, `create_user_as_pending` left false so the user is `active`; returns `user_id`, `email_id`, `status`, and `duplicate_email` when the address is taken); `POST /v1/magic_links` (embeddable magic links, **Consumer API only, absent from the B2B API** — `user_id`, which "You may substitute an `external_id`" for, plus `expiration_minutes` 5–10080 default 60; returns `token` and **sends no email**, which is what makes it usable from a server); `POST /v1/magic_links/authenticate` (`token`, `session_duration_minutes`, and no `session_custom_claims` for the same reason the other authenticates send none; returns `session_jwt`, `session_token` and the user). The pair is Stytch's documented server-side way to mint a session for a user you already know, and the authenticate is the same endpoint a real emailed magic link would hit.
- Signup, refresh and reset-complete are in the spec's list (management API → Authentication).

Spec: management API → Authentication (the five endpoints; "OTP and password are Basic-auth Stytch endpoints requiring the project secret, so they run server-side"); accounts → Auth is a module ("a Stytch session JWT carrying our account UUID comes back"); client shell → No social login; `architecture.md` → Game management data model ("Stytch's `external_id` is set to `Account.id`").

- [ ] **Step 1: Write `FakeStytch.cs` — an in-memory Stytch behind an `HttpMessageHandler`**

```csharp
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tabletop.Management.Tests;

/// <summary>Enough of Stytch to log in: users by email, one OTP code, passwords, external ids, sessions minted with TestKeys. No claim template: a session carries account_id only once session_custom_claims set it, and keeps it across refreshes, as the real project does.</summary>
public sealed class FakeStytch : HttpMessageHandler
{
    public const string Code = "123456";
    public sealed class User { public string Id = "user-test-" + Guid.NewGuid().ToString("N")[..8]; public string Email = ""; public string? ExternalId; public string? Password; }
    public readonly Dictionary<string, User> Users = new();   // by email
    public readonly List<string> Revoked = new();
    public readonly List<string> Deleted = new();
    public readonly List<string> ResetEmails = new();
    public readonly List<string> Calls = new();               // every path, so tests can count round trips
    public int Authenticates => Calls.Count(p => p == "/v1/sessions/authenticate");
    public (string Path, int Status, string Body)? Break;   // the next request to this path answers raw, possibly non-JSON; consumed on the first match

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        Assert.StartsWith("Basic ", req.Headers.Authorization?.ToString());
        var body = req.Content is null ? new JsonObject() : JsonNode.Parse(await req.Content.ReadAsStringAsync(ct))!.AsObject();
        var path = req.RequestUri!.AbsolutePath;
        Calls.Add(path);
        if (Break is { } brk && brk.Path == path) { Break = null; return new HttpResponseMessage((HttpStatusCode)brk.Status) { Content = new StringContent(brk.Body, System.Text.Encoding.UTF8, "text/html") }; }
        string S(string k) => body[k]?.GetValue<string>() ?? "";
        switch (path)
        {
            case "/v1/otps/email/login_or_create":
            {
                var created = !Users.ContainsKey(S("email"));
                var u = created ? Users[S("email")] = new User { Email = S("email") } : Users[S("email")];
                return Json(200, new { user_id = u.Id, email_id = "email-" + u.Id, user_created = created });
            }
            case "/v1/otps/authenticate":
            {
                var u = Users.Values.FirstOrDefault(x => "email-" + x.Id == S("method_id"));
                return u is null || S("code") != Code ? Error(401, "unauthorized_credentials") : Session(u, body);
            }
            case "/v1/passwords/authenticate":
                return Users.TryGetValue(S("email"), out var pu) && pu.Password == S("password") ? Session(pu, body) : Error(401, "unauthorized_credentials");
            case "/v1/passwords":
                if (Users.ContainsKey(S("email"))) return Error(400, "duplicate_email");
                if (S("password").Length < 8) return Error(400, "weak_password");
                return Session(Users[S("email")] = new User { Email = S("email"), Password = S("password") }, body);
            case "/v1/passwords/email/reset/start":
                ResetEmails.Add(S("email")); return Json(200, new { user_id = "x", email_id = "y" });
            case "/v1/passwords/email/reset":
            {
                var u = Users.Values.FirstOrDefault(x => "reset-" + x.Id == S("token"));
                if (u is null) return Error(401, "unable_to_auth_magic_link");
                u.Password = S("password"); return Session(u, body);
            }
            case "/v1/sessions/authenticate":
            {
                var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(S("session_jwt"));
                var u = Users.Values.First(x => x.Id == jwt.Subject);
                return Session(u, body, jwt.Claims.FirstOrDefault(c => c.Type == Identity.StytchAuthentication.AccountClaim)?.Value);
            }
            case "/v1/sessions/revoke": Revoked.Add(S("session_jwt")); return Json(200, new { });
            case "/v1/users":
            {
                if (Users.ContainsKey(S("email"))) return Error(400, "duplicate_email");
                var u = Users[S("email")] = new User { Email = S("email"), ExternalId = S("external_id") };
                return Json(200, new { user_id = u.Id, email_id = "email-" + u.Id, status = "active" });
            }
            case "/v1/magic_links":
            {
                var u = Find(S("user_id"));
                return u is null ? Error(404, "user_not_found") : Json(200, new { user_id = u.Id, token = "eml-" + u.Id });
            }
            case "/v1/magic_links/authenticate":
            {
                var u = Users.Values.FirstOrDefault(x => "eml-" + x.Id == S("token"));
                return u is null ? Error(401, "unable_to_auth_magic_link") : Session(u, body);
            }
        }
        if (path.StartsWith("/v1/users/") && req.Method == HttpMethod.Get)
        {
            var u = Find(path["/v1/users/".Length..]);
            return u is null ? Error(404, "user_not_found")
                : Json(200, new { user_id = u.Id, external_id = u.ExternalId, emails = new[] { new { email_id = "email-" + u.Id, email = u.Email, verified = true } } });
        }
        if (path.StartsWith("/v1/users/") && req.Method == HttpMethod.Put)
        {
            var u = Users.Values.First(x => x.Id == path["/v1/users/".Length..]);
            u.ExternalId = S("external_id"); return Json(200, new { user_id = u.Id });
        }
        if (path.StartsWith("/v1/users/") && req.Method == HttpMethod.Delete)
        {
            var key = path["/v1/users/".Length..];
            var u = Users.Values.FirstOrDefault(x => x.Id == key || x.ExternalId == key);
            if (u is null) return Error(404, "user_not_found");
            Users.Remove(u.Email); Deleted.Add(key); return Json(200, new { user_id = u.Id });
        }
        return Error(404, "unknown_route");
    }

    /// <summary>By Stytch user id or by external id, which the get, the delete and the embeddable magic link all accept interchangeably.</summary>
    private User? Find(string key) => Users.Values.FirstOrDefault(x => x.Id == key || x.ExternalId == key);

    /// <summary>A session's account_id is whatever session_custom_claims set on it, carried over from the incoming JWT on a refresh.</summary>
    private HttpResponseMessage Session(User u, JsonObject body, string? carried = null)
    {
        var claim = body["session_custom_claims"]?[Identity.StytchAuthentication.AccountClaim]?.GetValue<string>() ?? carried;
        var jwt = TestKeys.MintForUser(u.Id, Guid.TryParse(claim, out var stamped) ? stamped : null);
        return Json(200, new { user_id = u.Id, session_jwt = jwt, session_token = "tok-" + u.Id,
            user = new { user_id = u.Id, external_id = u.ExternalId, emails = new[] { new { email_id = "email-" + u.Id, email = u.Email, verified = true } } } });
    }

    private static HttpResponseMessage Json(int status, object o) =>
        new((HttpStatusCode)status) { Content = new StringContent(JsonSerializer.Serialize(o), System.Text.Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Error(int status, string type) => Json(status, new { status_code = status, error_type = type, error_message = type });
}
```

`TestKeys` gains `MintForUser(string userId, Guid? accountId)`: as `Mint`, with `sub = userId` and the `account_id` claim only when `accountId` is given. `ManagementFactory` gains `public FakeStytch Stytch { get; } = new();` and, in `ConfigureTestServices`, `s.AddHttpClient(Identity.StytchClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Stytch);`. It also gains `public string HostEnvironment { get; init; } = "Testing";`, and `ConfigureWebHost` calls `builder.UseEnvironment(HostEnvironment)` instead of the literal — the only way a test can stand the host up as a development one, which `DevSessionTests` needs and every other test must not get.

- [ ] **Step 2: Write the failing tests**

```csharp
using System.Net;
using System.Net.Http.Json;
using Tabletop.Management.Identity;

namespace Tabletop.Management.Tests;

public class AuthTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public AuthTests(ManagementFactory f) => _f = f;

    [Fact]
    public async Task Otp_login_creates_the_account_and_the_first_jwt_carries_the_claim()
    {
        var c = _f.CreateClient();
        var sent = await (await c.PostAsJsonAsync("/v1/auth/otp", new { email = "clay@example.com" })).Content.ReadFromJsonAsync<OtpSentResponse>();
        var before = _f.Stytch.Authenticates;
        var r = await c.PostAsJsonAsync("/v1/auth/otp/verify", new { methodId = sent!.MethodId, code = FakeStytch.Code });
        var session = await r.Content.ReadFromJsonAsync<SessionResponse>();
        Assert.Equal(1, _f.Stytch.Authenticates - before);        // exactly one: the call that adds account_id to the session
        Assert.Equal("clay", session!.Account.Name);
        Assert.Equal(session.Account.Id.ToString(), _f.Stytch.Users["clay@example.com"].ExternalId);
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(session.SessionJwt);
        Assert.Equal(session.Account.Id.ToString(), jwt.Claims.Single(x => x.Type == StytchAuthentication.AccountClaim).Value);
        c.DefaultRequestHeaders.Authorization = new("Bearer", session.SessionJwt);
        Assert.Equal(session.Account, await c.GetFromJsonAsync<AccountRow>("/v1/account"));
    }

    [Fact]
    public async Task A_second_login_reuses_the_account()
    {
        var c = _f.CreateClient();
        var first = await (await c.PostAsJsonAsync("/v1/auth/password/signup", new { email = "a@example.com", password = "correct horse battery" })).Content.ReadFromJsonAsync<SessionResponse>();
        var again = await (await c.PostAsJsonAsync("/v1/auth/password", new { email = "a@example.com", password = "correct horse battery" })).Content.ReadFromJsonAsync<SessionResponse>();
        Assert.Equal(first!.Account.Id, again!.Account.Id);
    }

    [Fact]
    public async Task A_linked_login_and_a_refresh_cost_one_stytch_call_each()
    {
        var c = _f.CreateClient();
        await c.PostAsJsonAsync("/v1/auth/password/signup", new { email = "once@example.com", password = "long enough password" });

        var beforeLogin = _f.Stytch.Authenticates;
        var again = await (await c.PostAsJsonAsync("/v1/auth/password", new { email = "once@example.com", password = "long enough password" })).Content.ReadFromJsonAsync<SessionResponse>();
        Assert.Equal(1, _f.Stytch.Authenticates - beforeLogin);    // every new session gets account_id added once
        Assert.Equal(again!.Account.Id.ToString(), Claim(again.SessionJwt));

        var beforeRefresh = _f.Stytch.Authenticates;
        var refreshed = await (await c.PostAsJsonAsync("/v1/auth/session/refresh", new { sessionJwt = again.SessionJwt })).Content.ReadFromJsonAsync<SessionResponse>();
        Assert.Equal(1, _f.Stytch.Authenticates - beforeRefresh);   // the refresh itself: the session already carries the claim
        Assert.Equal(again.Account.Id.ToString(), Claim(refreshed!.SessionJwt));
    }

    private static string Claim(string jwt) =>
        new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(jwt).Claims.Single(x => x.Type == StytchAuthentication.AccountClaim).Value;

    [Fact]
    public async Task Failures_map_to_401_409_422()
    {
        var c = _f.CreateClient();
        await c.PostAsJsonAsync("/v1/auth/password/signup", new { email = "dup@example.com", password = "long enough password" });
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/v1/auth/password/signup", new { email = "dup@example.com", password = "long enough password" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsJsonAsync("/v1/auth/password/signup", new { email = "w@example.com", password = "short" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/v1/auth/password", new { email = "dup@example.com", password = "wrong" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/v1/auth/otp/verify", new { methodId = "email-nobody", code = "000000" })).StatusCode);
    }

    [Fact]
    public async Task Reset_refresh_and_logout()
    {
        var c = _f.CreateClient();
        var s = await (await c.PostAsJsonAsync("/v1/auth/password/signup", new { email = "r@example.com", password = "long enough password" })).Content.ReadFromJsonAsync<SessionResponse>();
        Assert.Equal(HttpStatusCode.Accepted, (await c.PostAsJsonAsync("/v1/auth/password/reset", new { email = "r@example.com" })).StatusCode);
        Assert.Contains("r@example.com", _f.Stytch.ResetEmails);
        var token = "reset-" + _f.Stytch.Users["r@example.com"].Id;
        var after = await (await c.PostAsJsonAsync("/v1/auth/password/reset/complete", new { token, password = "another long password" })).Content.ReadFromJsonAsync<SessionResponse>();
        Assert.Equal(s!.Account.Id, after!.Account.Id);
        var refreshed = await (await c.PostAsJsonAsync("/v1/auth/session/refresh", new { sessionJwt = after.SessionJwt })).Content.ReadFromJsonAsync<SessionResponse>();
        Assert.Equal(s.Account.Id, refreshed!.Account.Id);
        c.DefaultRequestHeaders.Authorization = new("Bearer", refreshed.SessionJwt);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync("/v1/auth/session")).StatusCode);
        Assert.Contains(refreshed.SessionJwt, _f.Stytch.Revoked);
    }
}
```

- [ ] **Step 3: Run; expect compile errors. Write `StytchClient.cs`:**

```csharp
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tabletop.Management.Configuration;

namespace Tabletop.Management.Identity;

public sealed record StytchEmail(string Email);
public sealed record StytchUser(string UserId, string? ExternalId, IReadOnlyList<StytchEmail> Emails);
public sealed record StytchSession(string SessionJwt, StytchUser User);
public sealed record OtpSent(string MethodId, string UserId, bool UserCreated);

public sealed class StytchException(int status, string errorType, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string ErrorType { get; } = errorType;
}

/// <summary>The only code in the project that knows Stytch exists. Basic auth, snake_case JSON, one method per endpoint.</summary>
public sealed class StytchClient(HttpClient http, ManagementOptions options)
{
    public const string HttpClientName = "stytch";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public async Task<OtpSent> SendEmailOtp(string email)
    {
        var r = await Call(HttpMethod.Post, "/v1/otps/email/login_or_create", new { email, expiration_minutes = 10 });
        return new OtpSent(r.GetProperty("email_id").GetString()!, r.GetProperty("user_id").GetString()!, r.GetProperty("user_created").GetBoolean());
    }

    public Task<StytchSession> AuthenticateOtp(string methodId, string code) =>
        Session(HttpMethod.Post, "/v1/otps/authenticate", new { method_id = methodId, code, session_duration_minutes = options.SessionMinutes });
    public Task<StytchSession> AuthenticatePassword(string email, string password) =>
        Session(HttpMethod.Post, "/v1/passwords/authenticate", new { email, password, session_duration_minutes = options.SessionMinutes });
    public Task<StytchSession> CreatePassword(string email, string password) =>
        Session(HttpMethod.Post, "/v1/passwords", new { email, password, session_duration_minutes = options.SessionMinutes });
    public Task StartPasswordReset(string email, string redirectUrl) =>
        Call(HttpMethod.Post, "/v1/passwords/email/reset/start", new { email, reset_password_redirect_url = redirectUrl });
    public Task<StytchSession> CompletePasswordReset(string token, string password) =>
        Session(HttpMethod.Post, "/v1/passwords/email/reset", new { token, password, session_duration_minutes = options.SessionMinutes });
    /// <summary>Extends the session and mints a fresh JWT. The session keeps the custom claims set on it, so a refresh passes none.</summary>
    public Task<StytchSession> RefreshSession(string sessionJwt) =>
        Session(HttpMethod.Post, "/v1/sessions/authenticate", new { session_jwt = sessionJwt, session_duration_minutes = options.SessionMinutes });
    /// <summary>As RefreshSession, adding account_id to the session's custom claims; every JWT the session mints from here on carries it.</summary>
    public Task<StytchSession> StampAccount(string sessionJwt, Guid accountId) =>
        Session(HttpMethod.Post, "/v1/sessions/authenticate", new { session_jwt = sessionJwt, session_duration_minutes = options.SessionMinutes,
            session_custom_claims = new Dictionary<string, string> { [StytchAuthentication.AccountClaim] = accountId.ToString() } });
    public Task RevokeSession(string sessionJwt) => Call(HttpMethod.Post, "/v1/sessions/revoke", new { session_jwt = sessionJwt });
    public Task SetExternalId(string userId, string externalId) => Call(HttpMethod.Put, $"/v1/users/{userId}", new { external_id = externalId });
    public Task DeleteUser(string externalId) => Call(HttpMethod.Delete, $"/v1/users/{externalId}", null);

    // The four below are reached only from POST /v1/dev/sessions, which exists only in a development environment.
    // They live here because this class holds the Basic-auth credential and nothing else may.
    public async Task<string?> FindUserId(string externalId)
    {
        try { return (await Call(HttpMethod.Get, $"/v1/users/{externalId}", null)).GetProperty("user_id").GetString(); }
        catch (StytchException e) when (e.Status == 404) { return null; }
    }
    public async Task<string> CreateUser(string email, string externalId) =>
        (await Call(HttpMethod.Post, "/v1/users", new { email, external_id = externalId })).GetProperty("user_id").GetString()!;
    /// <summary>An embeddable magic link: Stytch returns the token instead of emailing it. Consumer API only.</summary>
    public async Task<string> CreateEmbeddableToken(string userId) =>
        (await Call(HttpMethod.Post, "/v1/magic_links", new { user_id = userId, expiration_minutes = 5 })).GetProperty("token").GetString()!;
    public Task<StytchSession> AuthenticateMagicLink(string token) =>
        Session(HttpMethod.Post, "/v1/magic_links/authenticate", new { token, session_duration_minutes = options.SessionMinutes });

    private async Task<StytchSession> Session(HttpMethod method, string path, object body)
    {
        var r = await Call(method, path, body);
        var user = r.GetProperty("user");
        var emails = user.TryGetProperty("emails", out var e) ? e.EnumerateArray().Select(x => new StytchEmail(x.GetProperty("email").GetString()!)).ToList() : [];
        var externalId = user.TryGetProperty("external_id", out var ext) && ext.ValueKind == JsonValueKind.String ? ext.GetString() : null;
        return new StytchSession(r.GetProperty("session_jwt").GetString()!, new StytchUser(user.GetProperty("user_id").GetString()!, string.IsNullOrEmpty(externalId) ? null : externalId, emails));
    }

    private async Task<JsonElement> Call(HttpMethod method, string path, object? body)
    {
        using var request = new HttpRequestMessage(method, options.StytchBaseUrl.TrimEnd('/') + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.StytchProjectId}:{options.StytchSecret}")));
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
        // A transport failure never reaches Stytch, and a non-JSON body (a proxy's HTML error page) carries no error_type: both fail as an upstream 5xx rather than as a 500.
        HttpResponseMessage response;
        try { response = await http.SendAsync(request); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or SocketException) { throw new StytchException(502, "transport", e.Message); }
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync();
            JsonElement json;
            try { json = text.Length == 0 ? JsonDocument.Parse("{}").RootElement : JsonDocument.Parse(text).RootElement.Clone(); }
            catch (JsonException) { throw new StytchException((int)response.StatusCode >= 500 ? (int)response.StatusCode : 502, "unparseable_response", text.Length > 0 ? text : "empty body"); }
            if (response.IsSuccessStatusCode) return json;
            var type = json.TryGetProperty("error_type", out var t) ? t.GetString() ?? "unknown" : "unknown";
            var message = json.TryGetProperty("error_message", out var m) ? m.GetString() ?? type : type;
            throw new StytchException((int)response.StatusCode, type, message);
        }
    }
}
```

- [ ] **Step 4: Write `Sessions.cs`**

```csharp
using Tabletop.Management.Http;

namespace Tabletop.Management.Identity;

public sealed record SessionResponse(string SessionJwt, AccountRow Account);
public sealed record OtpSentResponse(string MethodId);

/// <summary>After any Stytch authenticate: make sure the account exists, is linked to the Stytch user, and rides in the session's account_id claim.</summary>
public sealed class Sessions(StytchClient stytch, AccountRepository accounts)
{
    public async Task<SessionResponse> Complete(StytchSession session)
    {
        var name = session.User.Emails.FirstOrDefault()?.Email.Split('@')[0] ?? "player";
        Guid accountId;
        if (Guid.TryParse(session.User.ExternalId, out var linked))
        {
            accountId = linked;
            await accounts.Insert(accountId, name);                      // no-op when the row exists
        }
        else
        {
            accountId = Guid.CreateVersion7();
            // Link first: a throw here inserts no row, so a retry (a fresh v7 id) orphans nothing; a throw after it leaves the user linked, and the next login's branch above writes the missing row.
            await stytch.SetExternalId(session.User.UserId, accountId.ToString());
            await accounts.Insert(accountId, name);
        }
        // A new session carries no account_id until it is stamped; a refreshed one keeps its stamp, so this skips.
        var jwt = AccountClaim(session.SessionJwt) == accountId.ToString()
            ? session.SessionJwt
            : (await stytch.StampAccount(session.SessionJwt, accountId)).SessionJwt;
        var account = await accounts.Get(accountId) ?? throw new InvalidOperationException("account vanished");
        return new SessionResponse(jwt, account);
    }

    /// <summary>Read without verifying: the JWT came straight from Stytch over the authenticated call.</summary>
    private static string? AccountClaim(string jwt) =>
        new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(jwt).TryGetPayloadValue<string>(StytchAuthentication.AccountClaim, out var v) ? v : null;

    public static IResult Problem(StytchException e) => e.Status switch
    {
        401 or 404 => Results.Problem(title: "Unauthorized", detail: e.ErrorType, statusCode: 401),
        _ when e.ErrorType == "duplicate_email" => Problems.Conflict("An account with that email already exists."),
        429 => Problems.TooManyRequests(e.ErrorType),
        >= 500 => Problems.BadGateway($"Stytch: {e.ErrorType}"),
        _ => Problems.Unprocessable(e.ErrorType),
    };
}
```

- [ ] **Step 5: Write `AuthEndpoints.cs`**

```csharp
using Tabletop.Management.Configuration;

namespace Tabletop.Management.Identity;

public sealed record EmailRequest(string Email);
public sealed record OtpVerifyRequest(string MethodId, string Code);
public sealed record PasswordRequest(string Email, string Password);
public sealed record ResetCompleteRequest(string Token, string Password);
public sealed record RefreshRequest(string SessionJwt);

public static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/auth");
        g.MapPost("/otp", Guard(async (EmailRequest r, StytchClient s) => Results.Ok(new OtpSentResponse((await s.SendEmailOtp(r.Email)).MethodId))));
        g.MapPost("/otp/verify", Guard(async (OtpVerifyRequest r, StytchClient s, Sessions x) => Results.Ok(await x.Complete(await s.AuthenticateOtp(r.MethodId, r.Code)))));
        g.MapPost("/password", Guard(async (PasswordRequest r, StytchClient s, Sessions x) => Results.Ok(await x.Complete(await s.AuthenticatePassword(r.Email, r.Password)))));
        g.MapPost("/password/signup", Guard(async (PasswordRequest r, StytchClient s, Sessions x) => Results.Created("/v1/account", await x.Complete(await s.CreatePassword(r.Email, r.Password)))));
        g.MapPost("/password/reset", Guard(async (EmailRequest r, StytchClient s, ManagementOptions o) => { await s.StartPasswordReset(r.Email, $"{o.WebUrl.TrimEnd('/')}/reset-password"); return Results.Accepted(); }));
        g.MapPost("/password/reset/complete", Guard(async (ResetCompleteRequest r, StytchClient s, Sessions x) => Results.Ok(await x.Complete(await s.CompletePasswordReset(r.Token, r.Password)))));
        g.MapPost("/session/refresh", Guard(async (RefreshRequest r, StytchClient s, Sessions x) => Results.Ok(await x.Complete(await s.RefreshSession(r.SessionJwt)))));
        g.MapDelete("/session", async (HttpContext ctx, StytchClient s) =>
        {
            var jwt = ctx.Request.Headers.Authorization.ToString().Replace("Bearer ", "");
            // Nothing to roll back: whatever Stytch says (already-revoked, rate-limited, down), the client's session is over as far as this API is concerned.
            try { await s.RevokeSession(jwt); } catch (StytchException) { }
            return Results.NoContent();
        }).RequireAuthorization();
    }

    private static Delegate Guard<T1, T2>(Func<T1, T2, Task<IResult>> h) => async (T1 a, T2 b) => { try { return await h(a, b); } catch (StytchException e) { return Sessions.Problem(e); } };
    private static Delegate Guard<T1, T2, T3>(Func<T1, T2, T3, Task<IResult>> h) => async (T1 a, T2 b, T3 c) => { try { return await h(a, b, c); } catch (StytchException e) { return Sessions.Problem(e); } };
}
```

Refresh and login are one path because `Complete` is the same for both: a refresh presents a JWT whose user is already linked, so `Complete` takes the first branch, does no second Stytch call, and hands back the JWT `sessions/authenticate` just minted with `account_id` on it. Refresh runs once a minute per client and costs exactly one Stytch call.

- [ ] **Step 6: Write `DevSessions.cs`**

```csharp
using System.Security.Cryptography;
using System.Text;
using Tabletop.Management.Http;

namespace Tabletop.Management.Identity;

public sealed record DevSessionRequest(string Name);

/// <summary>Development only. A real Stytch session JWT for a named account, so the simulator's socket driver runs the real verification path.</summary>
public sealed class DevSessions(StytchClient stytch, AccountRepository accounts, Sessions sessions)
{
    private static readonly Guid Namespace = new("6f2b1c5e-0d3a-4f18-9b7c-2a5d8e4f1b60");

    /// <summary>A name-based UUIDv8 (RFC 9562 5.8): SHA-256 over the namespace and the name, first sixteen bytes, version and variant stamped.</summary>
    public static Guid AccountIdFor(string name)
    {
        var bytes = SHA256.HashData([.. Namespace.ToByteArray(), .. Encoding.UTF8.GetBytes(name)])[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>example.com is reserved by RFC 2606 and undeliverable. Nothing on this path sends mail.</summary>
    public static string EmailFor(string name) =>
        new string([.. name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')]).Trim('-') + "@example.com";

    public async Task<SessionResponse> Mint(string name)
    {
        var accountId = AccountIdFor(name);
        await accounts.Insert(accountId, name);                                              // no-op when the row exists
        var externalId = accountId.ToString();
        var userId = await stytch.FindUserId(externalId) ?? await stytch.CreateUser(EmailFor(name), externalId);
        var token = await stytch.CreateEmbeddableToken(userId);
        return await sessions.Complete(await stytch.AuthenticateMagicLink(token));
    }
}

public static class DevSessionEndpoint
{
    /// <summary>Mapped only under IHostEnvironment.IsDevelopment(). A production build serves no such route.</summary>
    public static void MapDevSession(this IEndpointRouteBuilder app) =>
        app.MapPost("/v1/dev/sessions", async (DevSessionRequest r, DevSessions dev) =>
        {
            if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length > 64) return Problems.Unprocessable("A name of 1 to 64 characters is required.");
            try { return Results.Ok(await dev.Mint(r.Name.Trim())); }
            catch (StytchException e) { return Sessions.Problem(e); }
        });
}
```

And `DevSessionTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Tabletop.Management.Identity;

namespace Tabletop.Management.Tests;

public class DevSessionTests(ManagementFactory f) : IClassFixture<ManagementFactory>, IDisposable
{
    private readonly ManagementFactory _dev = new() { HostEnvironment = "Development" };
    public void Dispose() => _dev.Dispose();

    [Fact]
    public async Task The_mint_is_absent_outside_a_development_environment()
    {
        var r = await f.CreateClient().PostAsJsonAsync("/v1/dev/sessions", new { name = "team-1" });   // the shared host is "Testing"
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Empty(f.Stytch.Calls);
    }

    [Fact]
    public async Task The_mint_returns_a_usable_session_and_repeats_for_one_name()
    {
        var c = _dev.CreateClient();
        var first = await c.PostJson<SessionResponse>("/v1/dev/sessions", new { name = "team-1" });
        var second = await c.PostJson<SessionResponse>("/v1/dev/sessions", new { name = "team-1" });
        Assert.Equal(DevSessions.AccountIdFor("team-1"), first.Account.Id);
        Assert.Equal(first.Account.Id, second.Account.Id);
        Assert.Single(_dev.Stytch.Users);                                                              // found by external_id, not created twice
        Assert.Equal(1, _dev.Stytch.Calls.Count(p => p == "/v1/users"));
        Assert.Equal(2, _dev.Stytch.Authenticates);                                                    // one stamp per mint: each is a new session
        c.DefaultRequestHeaders.Authorization = new("Bearer", second.SessionJwt);
        Assert.Equal(first.Account.Id, (await c.GetFromJsonAsync<AccountRow>("/v1/account"))!.Id);
    }
}
```

The second test is what proves the endpoint is on the real path: the JWT it returns is verified by the same `JwtBearer` handler against the same JWKS as a player's, and `GET /v1/account` accepts it without knowing where it came from.

- [ ] **Step 7: Wire `Program.cs`** — `builder.Services.AddHttpClient(StytchClient.HttpClientName); builder.Services.AddSingleton(sp => new StytchClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(StytchClient.HttpClientName), sp.GetRequiredService<ManagementOptions>())); builder.Services.AddSingleton<Sessions>(); if (builder.Environment.IsDevelopment()) builder.Services.AddSingleton<DevSessions>();` and `app.MapAuth(); if (app.Environment.IsDevelopment()) app.MapDevSession();`.

- [ ] **Step 8: Run `dotnet test tests/Tabletop.Management.Tests`; expect at least 27 passed. Then the real thing once: `dotnet user-secrets set STYTCH_SECRET <secret>` in `server/management`, `dotnet run --project server/management`, `curl -X POST localhost:5100/v1/auth/otp -H 'content-type: application/json' -d '{"email":"<your address>"}'`, verify with the emailed code, and check the JWT's `account_id` claim is the account id (the `RESET_PASSWORD` redirect URL is the only dashboard setting; *Interfaces needed from other modules → Stytch dashboard*). While the secret is set, mint once too: `ASPNETCORE_ENVIRONMENT=Development dotnet run --project server/management`, then `curl -X POST localhost:5100/v1/dev/sessions -H 'content-type: application/json' -d '{"name":"team-1"}'`, and check the returned JWT the same way. Repeat it and confirm the same `account.id` comes back. Commit: `Log in through Stytch by email code or password, and mint a development session for a named account`.**

**What the mint costs.** A `--driver socket` simulator run needs network access to the Stytch test project, because every token it uses is minted there. The in-process driver needs none, and neither does the rest of this suite, which runs against `FakeStytch`. That is the price of keeping the socket driver on the real verification path. It falls on console runs only: the simulator's own tests mint with `TestKeys` against a fake JWKS and reach no network, as does every test here.
