# Task 4: Stytch JWT verification, the current account, `GET /v1/account`

**Files:**
- Create: `server/management/Identity/StytchJwks.cs`, `server/management/Identity/StytchAuthentication.cs`, `server/management/Http/CurrentAccount.cs`, `server/management/Identity/AccountRepository.cs`, `server/management/Identity/AccountEndpoints.cs`
- Modify: `server/management/Program.cs`
- Create: `tests/Tabletop.Management.Tests/TestKeys.cs`, `tests/Tabletop.Management.Tests/JwtTests.cs`
- Modify: `tests/Tabletop.Management.Tests/ManagementFactory.cs`

**Interfaces:**
- Produces: `StytchJwks(HttpClient, ManagementOptions)` (singleton over the named client `StytchAuthentication.JwksClient`, both resolved from DI) with `Task<IReadOnlyList<SecurityKey>> Resolve(string kid)` (cached; refetches on an unknown `kid`, and at most once every 30 s after such a refetch); `StytchAuthentication.AddStytchJwt(IServiceCollection)`; `StytchAuthentication.AccountClaim = "account_id"`, `StytchAuthentication.SessionClaim = "https://stytch.com/session"` (Stytch's own session payload, declared and read by nothing), `StytchAuthentication.Issuer(projectId) = $"stytch.com/{projectId}"`; `CurrentAccount.Id(ClaimsPrincipal) : Guid`; `AccountRepository(Database)` with `Task<AccountRow?> Get(Guid id)`, `Task Insert(Guid id, string name)`, `Task<bool> Rename(Guid id, string name)`; `AccountRow(Guid Id, string Name)`; `GET /v1/account → AccountRow`.
- Test helpers: `TestKeys.Mint(Guid accountId, string projectId, DateTimeOffset? expires = null, string? issuer = null, string? kid = null)`, `TestKeys.JwksHandler` (serves the current key set), `TestKeys.Rotate()`; `ManagementFactory.AuthenticatedClient(Guid accountId)` (inserts the account row, mints a JWT, sets the bearer header).
- **The game server must verify the same way**: JWKS at `{StytchBaseUrl}/v1/sessions/jwks/{project_id}` (**no auth** — Stytch's reference page lists a `basicAuth` scheme and passes `-u project:secret` in its example, but `curl https://test.stytch.com/v1/sessions/jwks/project-test-7a0ad7e7-906a-434a-a3d2-bf90668f9f43` with no credentials answers `200` with the key set, so `StytchJwks` sends no `Authorization` header and the game server needs no Stytch secret to verify), `iss = stytch.com/{project_id}`, `aud = project_id`, RS256, account id in the top-level claim `account_id`, Stytch's own session data under `https://stytch.com/session`. Recorded under *Interfaces needed from other modules* for the game-server plan.

Spec: management API → We issue no tokens of our own ("Both services verify it locally, against Stytch's public JWKS"); accounts → Auth is a module ("no Stytch identifier is stored anywhere in our schema"); `schema.md` → ACCOUNT (id, name).

- [ ] **Step 1: Write `TestKeys.cs`**

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Tabletop.Management.Tests;

/// <summary>Stands in for Stytch's signing key and its JWKS endpoint.</summary>
public static class TestKeys
{
    public const string ProjectId = "project-test-00000000-0000-0000-0000-000000000000";
    private static RsaSecurityKey _key = NewKey("kid-1");
    public static int Fetches;

    private static RsaSecurityKey NewKey(string kid) => new(RSA.Create(2048)) { KeyId = kid };
    public static void Rotate() => _key = NewKey("kid-" + Guid.NewGuid().ToString("N")[..6]);

    public static string Mint(Guid accountId, string projectId = ProjectId, DateTimeOffset? expires = null, string? issuer = null, string? kid = null)
    {
        var key = kid is null ? _key : new RsaSecurityKey(RSA.Create(2048)) { KeyId = kid };
        var exp = expires ?? DateTimeOffset.UtcNow.AddMinutes(5);
        var nbf = exp < DateTimeOffset.UtcNow ? exp.AddMinutes(-1) : DateTimeOffset.UtcNow.AddMinutes(-1);
        var token = new JwtSecurityToken(
            issuer: issuer ?? $"stytch.com/{projectId}", audience: projectId,
            claims: [new Claim("sub", "user-test-1"), new Claim(Identity.StytchAuthentication.AccountClaim, accountId.ToString())],
            notBefore: nbf.UtcDateTime, expires: exp.UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static HttpMessageHandler JwksHandler => new Handler();

    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Fetches++;
            var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(_key);
            var body = JsonSerializer.Serialize(new { keys = new[] { new { kty = "RSA", alg = "RS256", use = "sig", kid = jwk.Kid, n = jwk.N, e = jwk.E } } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
```

`Tabletop.Management.Tests.csproj` gains `<PackageReference Include="System.IdentityModel.Tokens.Jwt" Version="8.22.0" />`.

- [ ] **Step 2: Write the failing tests and the factory additions**

```csharp
using System.Net.Http.Json;

namespace Tabletop.Management.Tests;

public class JwtTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public JwtTests(ManagementFactory f) => _f = f;

    [Fact]
    public async Task A_valid_jwt_reads_the_account()
    {
        var id = Guid.CreateVersion7();
        var client = await _f.AuthenticatedClient(id, "Clay");
        var account = await client.GetFromJsonAsync<Identity.AccountRow>("/v1/account");
        Assert.Equal(new Identity.AccountRow(id, "Clay"), account);
    }

    [Fact]
    public async Task No_token_wrong_issuer_and_expired_are_401()
    {
        var anon = _f.CreateClient();
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await anon.GetAsync("/v1/account")).StatusCode);
        foreach (var jwt in new[] { TestKeys.Mint(Guid.CreateVersion7(), issuer: "stytch.com/other"), TestKeys.Mint(Guid.CreateVersion7(), expires: DateTimeOffset.UtcNow.AddMinutes(-10)) })
        {
            var c = _f.CreateClient();
            c.DefaultRequestHeaders.Authorization = new("Bearer", jwt);
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await c.GetAsync("/v1/account")).StatusCode);
        }
    }

    [Fact]
    public async Task An_unknown_kid_refetches_the_jwks_once()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7(), "A");
        Assert.True((await c.GetAsync("/v1/account")).IsSuccessStatusCode);
        var before = TestKeys.Fetches;
        TestKeys.Rotate();
        var rotated = await _f.AuthenticatedClient(Guid.CreateVersion7(), "B");
        Assert.True((await rotated.GetAsync("/v1/account")).IsSuccessStatusCode);
        Assert.Equal(before + 1, TestKeys.Fetches);
    }
}
```

`ManagementFactory` additions: in `ConfigureWebHost`, `["STYTCH_PROJECT_ID"] = TestKeys.ProjectId`, and `builder.ConfigureTestServices(s => s.AddHttpClient(Identity.StytchAuthentication.JwksClient).ConfigurePrimaryHttpMessageHandler(() => TestKeys.JwksHandler));` plus:

```csharp
public async Task<HttpClient> AuthenticatedClient(Guid accountId, string name = "Test")
{
    await Services.GetRequiredService<Identity.AccountRepository>().Insert(accountId, name);
    var client = CreateClient();
    client.DefaultRequestHeaders.Authorization = new("Bearer", TestKeys.Mint(accountId));
    return client;
}
```

- [ ] **Step 3: Run; expect compile errors. Write `StytchJwks.cs` and `StytchAuthentication.cs`:**

```csharp
using Microsoft.IdentityModel.Tokens;
using Tabletop.Management.Configuration;

namespace Tabletop.Management.Identity;

/// <summary>Stytch's public keys, fetched once and again only when a token names a kid we do not hold.</summary>
public sealed class StytchJwks(HttpClient http, ManagementOptions options)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private JsonWebKeySet? _keys;
    // Gates only refetches triggered by an unknown kid, separately from the lazy first load, so a
    // genuine rotation right after startup is not held back by a floor measured from that load.
    private DateTimeOffset _lastMissRefetch = DateTimeOffset.MinValue;

    public string Url => $"{options.StytchBaseUrl.TrimEnd('/')}/v1/sessions/jwks/{options.StytchProjectId}";

    public async Task<IReadOnlyList<SecurityKey>> Resolve(string? kid, CancellationToken ct = default)
    {
        var keys = await EnsureLoaded(kid, ct);
        return keys.Keys.Where(k => kid is null || k.Kid == kid).ToList();
    }

    private bool Holds(string? kid) => _keys is { } k && (kid is null || k.Keys.Any(key => key.Kid == kid));

    private async Task<JsonWebKeySet> EnsureLoaded(string? kid, CancellationToken ct)
    {
        if (Holds(kid)) return _keys!; // fast path: no lock needed once we already hold the kid

        await _gate.WaitAsync(ct);
        try
        {
            // Re-check now that we hold the lock: a concurrent caller queued ahead of us may
            // already have fetched the kid we need, or may already have hit the floor below.
            if (Holds(kid)) return _keys!;

            var alreadyLoaded = _keys is not null;
            // The floor only throttles a repeat miss once we hold *some* key set; the very
            // first load (nothing cached yet) always goes through regardless of timing.
            if (alreadyLoaded && DateTimeOffset.UtcNow - _lastMissRefetch <= TimeSpan.FromSeconds(30))
                return _keys!;

            _keys = new JsonWebKeySet(await http.GetStringAsync(Url, ct));
            if (alreadyLoaded) _lastMissRefetch = DateTimeOffset.UtcNow;
            return _keys;
        }
        finally { _gate.Release(); }
    }
}
```

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Tabletop.Management.Configuration;

namespace Tabletop.Management.Identity;

public static class StytchAuthentication
{
    /// <summary>Our account UUID. Sessions.Complete adds it to each new session as a session custom claim; refreshes keep it.</summary>
    public const string AccountClaim = "account_id";
    /// <summary>Stytch's own session payload. Declared for the wire shape; no code here reads it.</summary>
    public const string SessionClaim = "https://stytch.com/session";
    /// <summary>Named client so tests can swap its handler; the cache must be a singleton, which a typed client is not.</summary>
    public const string JwksClient = "stytch-jwks";
    public static string Issuer(string projectId) => $"stytch.com/{projectId}";

    public static IServiceCollection AddStytchJwt(this IServiceCollection services)
    {
        services.AddHttpClient(JwksClient);
        services.AddSingleton(sp => new StytchJwks(sp.GetRequiredService<IHttpClientFactory>().CreateClient(JwksClient), sp.GetRequiredService<ManagementOptions>()));
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.MapInboundClaims = false;
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                ClockSkew = TimeSpan.FromSeconds(30),
            };
        });
        // The key resolver and the issuer/audience are configured here, rather than in the inline
        // lambda above, because ManagementOptions is not safe to read before builder.Build().
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<StytchJwks, ManagementOptions>((o, jwks, mo) =>
        {
            o.TokenValidationParameters.ValidIssuer = Issuer(mo.StytchProjectId);
            o.TokenValidationParameters.ValidAudience = mo.StytchProjectId;
            o.TokenValidationParameters.IssuerSigningKeyResolver = (_, _, kid, _) => jwks.Resolve(kid).GetAwaiter().GetResult();
        });
        services.AddAuthorization();
        return services;
    }
}
```

- [ ] **Step 4: Write `CurrentAccount.cs`, `AccountRepository.cs`, `AccountEndpoints.cs`**

```csharp
using System.Security.Claims;

namespace Tabletop.Management.Http;

public static class CurrentAccount
{
    public static Guid Id(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(Identity.StytchAuthentication.AccountClaim), out var id) ? id
            : throw new UnauthorizedAccessException("The session carries no account_id claim.");
}
```

```csharp
using Dapper;
using Tabletop.Management.Persistence;

namespace Tabletop.Management.Identity;

public sealed record AccountRow(Guid Id, string Name);

public sealed class AccountRepository(Database db)
{
    public async Task<AccountRow?> Get(Guid id)
    {
        await using var c = await db.Open();
        return await c.QuerySingleOrDefaultAsync<AccountRow>("select id as Id, name as Name from account where id = @id", new { id });
    }

    public async Task Insert(Guid id, string name)
    {
        await using var c = await db.Open();
        await c.ExecuteAsync("insert into account (id, name) values (@id, @name) on conflict (id) do nothing", new { id, name });
    }

    public async Task<bool> Rename(Guid id, string name)
    {
        await using var c = await db.Open();
        return await c.ExecuteAsync("update account set name = @name where id = @id", new { id, name }) == 1;
    }
}
```

```csharp
using Tabletop.Management.Http;

namespace Tabletop.Management.Identity;

public static class AccountEndpoints
{
    public static void MapAccount(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/account").RequireAuthorization();
        group.MapGet("", async (System.Security.Claims.ClaimsPrincipal user, AccountRepository accounts) =>
            await accounts.Get(CurrentAccount.Id(user)) is { } row ? Results.Ok(row) : Results.Unauthorized());
    }
}
```

- [ ] **Step 5: Wire `Program.cs`** — `builder.Services.AddStytchJwt(); builder.Services.AddSingleton<AccountRepository>();` before `Build()`, with no `ManagementOptions` argument: `StytchJwks` and the issuer/audience take it from DI, so nothing added here reads configuration ahead of the eager `var options = app.Services.GetRequiredService<ManagementOptions>()` that follows `Build()`. After the version middleware: `app.UseAuthentication(); app.UseAuthorization(); app.MapAccount();`. The exception handler from Task 3 maps `UnauthorizedAccessException` to 401.

- [ ] **Step 6: Run `dotnet test tests/Tabletop.Management.Tests`; expect 19 passed. Commit: `Verify Stytch session JWTs locally and read the current account`.**
