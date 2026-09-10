# Game Server Skeleton Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `server/game/` as a WebSocket server with no rules: connection and Stytch JWT verification, the in-memory game session hydrated from management's rows plus the event log, the append-only log with replay, the command-dispatch seam the scheduler and interpreter plans plug into, the three message kinds on one socket, and the lobby and setup statuses in which the game server writes the game's own rows.

**Architecture:** One Kestrel process, one `/ws` endpoint. A `GameSession` per game is an actor (a mailbox loop) that owns the `GameInfo`, the draft or `Board`, the `EventApplier`, the connected sockets and their presence values; nobody creates one — the first socket for a game hydrates it. Commands go through `CommandDispatcher`, which does the rules-free part itself (menu edits, hand moves, undo, abandon, Ask-answer mechanics, sealed withholding, enforcement policy) and hands the rest to `IRulesEngine`, whose skeleton implementation is `InertEngine`. Rows are read and written with Npgsql and plain SQL; the log is `game.game_event`.

**Tech Stack:** .NET 10 (`net10.0`, C# 12), ASP.NET Core Kestrel WebSockets, Npgsql 10.0.3, Microsoft.IdentityModel.JsonWebTokens 8.22.0, xUnit 2.9.3, Microsoft.AspNetCore.Mvc.Testing 10.0.11, Docker Postgres 17 from `docker-compose.yml`.

**Spec:** `docs/architecture.md` (The load-bearing seam, Command / Event spine, Rules enforcement, Undo, Two servers → `server/game/`, Transport, Deployment → the three constraints, Game management data model, Persistence, Contracts to freeze → who instantiates a session); `docs/schema.md`; `docs/superpowers/specs/2026-08-29-accounts-design.md`, `2026-08-30-presence-design.md`, `2026-09-04-setup-phase-design.md`, `2026-08-28-game-flow-design.md`, `2026-08-27-table-domain-model-design.md`, `2026-08-29-setup-content-design.md`, `2026-08-29-identity-and-enumerations-design.md`, `2026-09-04-scheduler-design.md` and `2026-09-03-action-sequences-design.md` (seams only), `2026-09-04-client-shell-design.md`, `2026-09-04-whole-design-review.md`, `2026-08-29-management-api-design.md` (auth, join). Earlier plan obeyed: `docs/superpowers/plans/2026-09-05-contracts-and-local-dev.md` — every Protocol, Content and Geometry name below is that plan's. `docs/workstreams/04-game-server-skeleton.md` is stale and not followed. The management API plan does not exist yet; the rows this server writes are taken from `docs/schema.md` and `scripts/postgres/init/001_schema.sql` (contracts plan Task 15), and the assumptions are recorded at the end.

**Requires landed:** the contracts plan. May run alongside the geometry and management plans.

## Global Constraints

- `net10.0`, `LangVersion 12`, `Nullable enable`, `ImplicitUsings enable`, root namespace `Tabletop.GameServer`; `Directory.Build.props` puts `obj/` under `obj/$(OS)/`; `global.json` pins SDK `10.0.400`.
- The rules engine lives only here, never in `libs/` (`architecture.md` → What is actually shared). This plan builds **no rules**: no legality beyond Ask legal-set membership, no dice, no turn order. `GameRules` is an interface stub the scheduler and interpreter plans extend.
- Only OPR's `GameRules` exists: `GrimdarkFutureRules`, id `grimdark-future`. 40k constrains interfaces only.
- Enumeration, not validation: legality is the Ask's server-computed legal set; `CommandDispatcher.EnumerateLegalCommands()` is derived from the open Asks (`architecture.md` → Rules enforcement).
- Enforcement is the dispatch policy: `silent | warn | strict`. Menu edits, hand moves, markers and undo are accepted under every setting (whole-design review ruling 1).
- The game server writes `game.status`, `game.name/properties_hash/scheduler_shape/enforcement/waits_between_steps`, `team`, `participant.team_id`, `participant.army_id`, `participant.army_snapshot` and the game's `table_state` tree while status is `lobby` or `setup`, and is read-only from `playing` on (review ruling 4). It never writes an account's layout, and it never writes `game.rng_seed`: management generates that once at creation and this server only reads it (owner ruling 1, 2026-09-06).
- Events are the only thing written from `playing` on; every event is appended to `game.game_event` **before** it is broadcast. `game_event.kind` = `GameEvent.Kind`, `payload` = `WireJson.Serialize(GameEvent)` (contracts plan → Interfaces needed → Game server plan).
- Presence is relayed at a fixed rate and never persisted or logged; setup edits are applied, broadcast and persisted, never logged (presence; setup phase → A third message kind).
- Identifiers are `Guid.CreateVersion7()`. One world unit = one inch. C# only.
- Local only: Postgres from `docker-compose.yml` at `TABLETOP_DB_CONNECTION`; the server binds `http://0.0.0.0:5200` (contracts plan → Local development, reserved ports). No AWS, no hosting.
- Wire format is `WireJson`; the socket carries one `SocketMessage` per text frame.
- Tests: xUnit in `tests/Tabletop.GameServer.Tests`; the log, replay, legality, the dispatcher, the draft editor and the verifier are unit-tested headlessly; the socket is exercised end to end with a test client against the Docker Postgres (collection `Postgres`).
- Commit after every task, one-line imperative message, with the `Co-Authored-By` and `Claude-Session` trailers the session requires.

---

## File Structure

```
Tabletop.sln                                        modified: server/game + its test project
server/game/
  Tabletop.GameServer.csproj                        Microsoft.NET.Sdk.Web; references the three libs
  Program.cs                                        host, DI, /ws endpoint, shutdown timeout
  appsettings.json                                  Kestrel url 0.0.0.0:5200, logging
  Configuration/GameServerOptions.cs                every setting, bound from TABLETOP_* variables
  Configuration/DotEnv.cs                           loads the repo's .env when present
  Auth/SessionVerification.cs                       ISessionVerifier, VerifiedSession, IJwksSource, HttpJwksSource, StytchJwtVerifier
  Persistence/IGameRows.cs                          the rows the server reads and writes
  Persistence/PostgresGameRows.cs                   reads: game, teams, participants, draft, snapshots, army
  Persistence/PostgresGameRows.Writes.cs            writes: lobby edits, start/end setup, the draft
  Persistence/RowMapping.cs                         TableState/Army <-> rows, enum <-> text
  Log/IEventLog.cs                                  append and read
  Log/PostgresEventLog.cs
  Log/InMemoryEventLog.cs                           for headless tests
  Log/Replay.cs                                     replay to N over a log
  Rules/IGameRules.cs                               the GameRules stub, GameRulesRegistry
  Rules/GrimdarkFuture/GfRules.cs                   GrimdarkFutureRules (partial; the scheduler and interpreter plans add sibling partial files)
  Rules/IRulesEngine.cs                             Actor, GameState, EngineResult, IRulesEngine, IRulesEngineFactory
  Rules/InertEngine.cs                              the skeleton's engine: judges nothing, advances nothing
  Dispatch/LegalSetMembership.cs                    is an answer inside a legal set
  Dispatch/EnforcementPolicy.cs                     Legality, Verdict; silent | warn | strict as a decision
  Dispatch/MenuTranslation.cs                       menu and move commands -> state-change events
  Dispatch/CommandDispatcher.cs                     the seam: authorization, classes, Ask mechanics, delegation
  Dispatch/EventFilter.cs                           per-viewer withholding of sealed answers
  Setup/DraftEditor.cs                              TableState x SetupEdit -> TableState, pure
  Session/ISessionClient.cs                         one connected socket as the session sees it
  Session/SessionServices.cs                        rows, log, engine factory, options, logging
  Session/GameSession.cs                            the actor: mailbox, connections, the log path, broadcast
  Session/GameSession.Frames.cs                     commands, presence, pings
  Session/GameSession.Connect.cs                    the connect sequence
  Session/GameSession.Lobby.cs                      lobby edits -> row writes -> GameInfo; start setup
  Session/GameSession.Setup.cs                      setup edits -> draft -> board -> row write
  Session/GameSession.EndSetup.cs                   the snapshot, the status flip, the log opens
  Session/PresenceTable.cs                          latest value per participant, dirty set
  Session/Hydrator.cs                               rows + log -> GameState
  Session/SessionRegistry.cs                        get-or-hydrate, eviction, drain
  Session/SocketConnection.cs                       one socket: read loop, send queue
  Session/ConnectionHandshake.cs                    Connect -> Welcome | Rejected
  Session/SessionHost.cs                            IHostedService: presence timer, SIGTERM drain
tests/Tabletop.GameServer.Tests/
  Tabletop.GameServer.Tests.csproj
  Infrastructure/PostgresFixture.cs                 seeds and deletes rows in the Docker database
  Infrastructure/TestKeys.cs                        an RSA key and a JWT minter
  Infrastructure/GameServerFactory.cs               WebApplicationFactory<Program> with test options
  Infrastructure/TestClient.cs                      the socket test client
  Infrastructure/FixedAskEngine.cs                  an IRulesEngine that opens one Ask
  Infrastructure/RecordingClient.cs                 an ISessionClient that records what it is sent
  *Tests.cs                                         one file per task
```

`server/game/` is the deliverable `CLAUDE.md` names; `tests/` is the contracts plan's fourth top-level kind. Nothing is added to `libs/`.

---
### Task 1: Project, configuration, the `/ws` endpoint and the test project

**Files:**
- Create: `server/game/Tabletop.GameServer.csproj`, `server/game/Program.cs`, `server/game/appsettings.json`, `server/game/Configuration/GameServerOptions.cs`, `server/game/Configuration/DotEnv.cs`
- Create: `tests/Tabletop.GameServer.Tests/Tabletop.GameServer.Tests.csproj`, `tests/Tabletop.GameServer.Tests/Infrastructure/GameServerFactory.cs`, `tests/Tabletop.GameServer.Tests/EndpointTests.cs`
- Modify: `Tabletop.sln`

**Interfaces:**
- Consumes: `Tabletop.Protocol.ProtocolVersion.Current`.
- Produces: assembly `Tabletop.GameServer` (namespace `Tabletop.GameServer`); `GameServerOptions` with `string DbConnection`, `string StytchProjectId`, `string StytchJwksUrl`, `int MinimumClientVersion`, `int PresenceRelayHz = 20`, `TimeSpan HandshakeTimeout = 10s`, `TimeSpan KeepAliveInterval = 20s`, `TimeSpan KeepAliveTimeout = 60s`, `double DefaultTableWidth = 72`, `double DefaultTableLength = 48`, `string DefaultTextureBuiltin = "grass"`; `GameServerOptions.FromEnvironment(IConfiguration)`; `DotEnv.Load(string startDirectory)`; `public partial class Program` (for `WebApplicationFactory`); the endpoint `GET /ws` that upgrades to a WebSocket and hands it to `ConnectionHandshake` (Task 8; until then it closes the socket with `1000`). Test infrastructure `GameServerFactory : WebApplicationFactory<Program>`.

Spec: `architecture.md` → Two servers (`server/game/` WebSocket), Deployment → the three constraints (heartbeat via WebSocket keep-alive pings — no `SocketMessage` kind exists for a heartbeat and the contracts plan defines none, so the protocol-level ping/pong is the app-level heartbeat), Local development (0.0.0.0:5200). Environment variable names are the contracts plan's plus `TABLETOP_STYTCH_PROJECT_ID`, `TABLETOP_STYTCH_BASE_URL` (the management plan's; default `https://test.stytch.com`), `TABLETOP_STYTCH_JWKS_URL` (derived when absent: `{TABLETOP_STYTCH_BASE_URL}/v1/sessions/jwks/{project_id}`, Stytch's documented JWKS endpoint), `TABLETOP_MIN_CLIENT_VERSION`. The default table size is `architecture.md` → Table and layout's OPR 72×48, which the whole-design review marks a first-pass suggestion rather than a decision (setup content leaves the inventory undecided), and the built-in texture name is the contracts plan's sample `grass` — question 2 at the end.

- [ ] **Step 1: Write the server csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12</LangVersion>
    <RootNamespace>Tabletop.GameServer</RootNamespace>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Npgsql" Version="10.0.3" />
    <PackageReference Include="Microsoft.IdentityModel.JsonWebTokens" Version="8.22.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../libs/Tabletop.Geometry/Tabletop.Geometry.csproj" />
    <ProjectReference Include="../../libs/Tabletop.Content/Tabletop.Content.csproj" />
    <ProjectReference Include="../../libs/Tabletop.Protocol/Tabletop.Protocol.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write the test csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12</LangVersion>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <RootNamespace>Tabletop.GameServer.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.13.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.0.2" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.11" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../server/game/Tabletop.GameServer.csproj" />
    <ProjectReference Include="../Tabletop.Content.Tests/Tabletop.Content.Tests.csproj" />
    <ProjectReference Include="../Tabletop.Protocol.Tests/Tabletop.Protocol.Tests.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

The two test-project references bring `ArmyTests.Sample()`, `TableStateTests.Sample()` and `Samples` in.

- [ ] **Step 3: Write the failing smoke test and its factory**

```csharp
// tests/Tabletop.GameServer.Tests/Infrastructure/GameServerFactory.cs
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Tabletop.GameServer.Configuration;

namespace Tabletop.GameServer.Tests.Infrastructure;

public class GameServerFactory : WebApplicationFactory<Program>
{
    public const string ProjectId = "project-test-437432c7-798d-49d2-9dc4-eff103937e0f";
    public Action<IServiceCollection>? ConfigureTestServices { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureServices(services =>
        {
            services.AddSingleton(new GameServerOptions
            {
                DbConnection = Environment.GetEnvironmentVariable("TABLETOP_DB_CONNECTION")
                    ?? "Host=localhost;Port=5432;Database=tabletop;Username=tabletop;Password=tabletop",
                StytchProjectId = ProjectId,
                StytchJwksUrl = $"https://test.stytch.com/v1/sessions/jwks/{ProjectId}",
                MinimumClientVersion = Tabletop.Protocol.ProtocolVersion.Current,
                PresenceRelayHz = 50,
                HandshakeTimeout = TimeSpan.FromSeconds(2),
            });
            ConfigureTestServices?.Invoke(services);
        });
    }
}
```

```csharp
// tests/Tabletop.GameServer.Tests/EndpointTests.cs
using System.Net.WebSockets;
using Tabletop.GameServer.Tests.Infrastructure;

namespace Tabletop.GameServer.Tests;

public class EndpointTests
{
    [Fact]
    public async Task Ws_endpoint_accepts_a_websocket()
    {
        using var factory = new GameServerFactory();
        var client = factory.Server.CreateWebSocketClient();
        using var socket = await client.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);
        Assert.Equal(WebSocketState.Open, socket.State);
    }

    [Fact]
    public async Task Plain_get_on_ws_is_400()
    {
        using var factory = new GameServerFactory();
        var response = await factory.CreateClient().GetAsync("/ws");
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }
}
```

- [ ] **Step 4: Add to the solution and run; expect compile errors (`GameServerOptions`, `Program` undefined)**

```bash
cd /home/cstupi/src/tabletop
dotnet sln Tabletop.sln add --solution-folder server server/game/Tabletop.GameServer.csproj
dotnet sln Tabletop.sln add --solution-folder tests tests/Tabletop.GameServer.Tests/Tabletop.GameServer.Tests.csproj
dotnet test tests/Tabletop.GameServer.Tests
```

- [ ] **Step 5: Write `GameServerOptions.cs` and `DotEnv.cs`**

```csharp
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
```

```csharp
namespace Tabletop.GameServer.Configuration;

/// <summary>Loads KEY=VALUE lines from the nearest .env above <paramref name="startDirectory"/> into the process environment, without overriding variables already set.</summary>
public static class DotEnv
{
    public static void Load(string startDirectory)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, ".env");
            if (!File.Exists(file)) continue;
            foreach (var raw in File.ReadAllLines(file))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#') || !line.Contains('=')) continue;
                var key = line[..line.IndexOf('=')].Trim();
                var value = line[(line.IndexOf('=') + 1)..].Trim();
                if (Environment.GetEnvironmentVariable(key) is null) Environment.SetEnvironmentVariable(key, value);
            }
            return;
        }
    }
}
```

- [ ] **Step 6: Write `appsettings.json` and `Program.cs`**

```json
{
  "Kestrel": { "Endpoints": { "Http": { "Url": "http://0.0.0.0:5200" } } },
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } }
}
```

```csharp
using Tabletop.GameServer.Configuration;

DotEnv.Load(Directory.GetCurrentDirectory());
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));
if (!builder.Environment.IsEnvironment("Test"))
    builder.Services.AddSingleton(GameServerOptions.FromEnvironment(builder.Configuration));
// Tasks 2-9 register: ISessionVerifier, IGameRows, IEventLog, IRulesEngineFactory, SessionRegistry, ConnectionHandshake, SessionHost.

var app = builder.Build();
var options = app.Services.GetRequiredService<GameServerOptions>();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = options.KeepAliveInterval, KeepAliveTimeout = options.KeepAliveTimeout });

app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    // Task 8 replaces this line with: await context.RequestServices.GetRequiredService<ConnectionHandshake>().RunAsync(socket, context.RequestAborted);
    await socket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "not yet", CancellationToken.None);
});

app.Run();

public partial class Program;
```

`KeepAliveInterval`/`KeepAliveTimeout` are Kestrel's WebSocket ping/pong: the server pings every 20 s and closes a socket that has not answered in 60 s. That is the heartbeat `architecture.md` → Deployment asks for; nothing goes through `SocketMessage`.

- [ ] **Step 7: Run `dotnet test tests/Tabletop.GameServer.Tests`; expect 2 passed. Check `ls server/game/obj` shows only `Unix/`. Commit.**

```bash
git add Tabletop.sln server/game tests/Tabletop.GameServer.Tests
git commit -m "Add the game server project, its options and the /ws endpoint"
```

### Task 2: Stytch session JWT verification

**Files:**
- Create: `server/game/Auth/SessionVerification.cs`
- Test: `tests/Tabletop.GameServer.Tests/Infrastructure/TestKeys.cs`, `tests/Tabletop.GameServer.Tests/SessionVerifierTests.cs`

**Interfaces:**
- Produces: `interface ISessionVerifier { Task<VerifiedSession?> VerifyAsync(string jwt, CancellationToken ct); }`; `sealed record VerifiedSession(Guid AccountId, string StytchUserId, DateTimeOffset ExpiresAt)`; `interface IJwksSource { Task<JsonWebKeySet> GetAsync(CancellationToken ct); }`; `sealed class HttpJwksSource(HttpClient http, GameServerOptions options)` (caches 10 minutes, refetches on an unknown `kid`); `sealed class StytchJwtVerifier(IJwksSource keys, GameServerOptions options) : ISessionVerifier`; `const string StytchJwtVerifier.AccountClaim = "account_id"`. Test infrastructure `TestKeys` (`RsaSecurityKey Key`, `JsonWebKeySet Jwks`, `string Mint(Guid accountId, string? issuer = null, string? audience = null, TimeSpan? lifetime = null, bool includeAccountClaim = true)`) and `StaticJwksSource`.

Spec: accounts → Auth is a module (the JWT carries our account UUID; both services verify locally against Stytch's JWKS; no call on the request path); management API → We issue no tokens of our own. Stytch facts read from its docs and SDK for this plan: JWKS at `https://test.stytch.com/v1/sessions/jwks/{project_id}` (live: `api.stytch.com`), issuer `stytch.com/{project_id}`, audience the project id, `sub` the Stytch `user_id`, RS256, five-minute lifetime, and **custom claims appear at the top level** of the payload. They are put there by the Stytch project's **custom claim template**, `{"account_id": "{{ user.external_id }}"}`, which Stytch evaluates every time it mints a JWT (owner ruling 09-06 on the management plan's question 1); no call from either server passes `session_custom_claims`, and management's only job is keeping `external_id` equal to the account id. `sub` is Stytch's id, not `external_id`, so our account UUID rides as the top-level custom claim `account_id` — the fallback accounts → Auth allowed ("if it ever cannot, it is added as a custom claim"). This is *Interfaces needed from other modules* item 1. The verifier's parameters are the management plan's Task 4 exactly (issuer, audience, RS256, 30 s clock skew, `account_id`), and the JWT is checked **once, at connect**: a socket lives hours and the JWT five minutes, nothing re-verifies an open socket, and a reconnect presents whatever JWT the client then holds (it refreshes once a minute against management). `VerifiedSession.ExpiresAt` is recorded, not enforced.

- [ ] **Step 1: Write `TestKeys.cs`**

```csharp
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Tabletop.GameServer.Auth;

namespace Tabletop.GameServer.Tests.Infrastructure;

public static class TestKeys
{
    public static readonly RsaSecurityKey Key = new(RSA.Create(2048)) { KeyId = "test-kid-1" };
    public static readonly JsonWebKeySet Jwks = new() { Keys = { JsonWebKeyConverter.ConvertFromRSASecurityKey(Key) } };
    public static IJwksSource Source => new StaticJwksSource(Jwks);

    public static string Mint(Guid accountId, string? issuer = null, string? audience = null, TimeSpan? lifetime = null, bool includeAccountClaim = true)
    {
        var claims = new Dictionary<string, object> { ["sub"] = "user-test-" + accountId.ToString("N")[..12] };
        if (includeAccountClaim) claims[StytchJwtVerifier.AccountClaim] = accountId.ToString();
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? $"stytch.com/{GameServerFactory.ProjectId}",
            Audience = audience ?? GameServerFactory.ProjectId,
            Expires = DateTime.UtcNow + (lifetime ?? TimeSpan.FromMinutes(5)),
            IssuedAt = DateTime.UtcNow,
            Claims = claims,
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.RsaSha256),
        });
    }
}

public sealed class StaticJwksSource(JsonWebKeySet jwks) : IJwksSource
{
    public Task<JsonWebKeySet> GetAsync(CancellationToken ct) => Task.FromResult(jwks);
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
using Tabletop.GameServer.Auth;
using Tabletop.GameServer.Configuration;
using Tabletop.GameServer.Tests.Infrastructure;

namespace Tabletop.GameServer.Tests;

public class SessionVerifierTests
{
    static readonly GameServerOptions Options = new() { DbConnection = "", StytchProjectId = GameServerFactory.ProjectId, StytchJwksUrl = "unused" };
    static StytchJwtVerifier Verifier() => new(TestKeys.Source, Options);

    [Fact]
    public async Task A_valid_token_yields_the_account_id()
    {
        var account = Guid.CreateVersion7();
        var session = await Verifier().VerifyAsync(TestKeys.Mint(account), CancellationToken.None);
        Assert.NotNull(session);
        Assert.Equal(account, session.AccountId);
        Assert.StartsWith("user-test-", session.StytchUserId);
    }

    [Theory]
    [InlineData("issuer")] [InlineData("audience")] [InlineData("expired")] [InlineData("no-claim")] [InlineData("garbage")]
    public async Task Bad_tokens_yield_null(string flaw)
    {
        var account = Guid.CreateVersion7();
        var jwt = flaw switch
        {
            "issuer" => TestKeys.Mint(account, issuer: "stytch.com/project-test-other"),
            "audience" => TestKeys.Mint(account, audience: "project-test-other"),
            "expired" => TestKeys.Mint(account, lifetime: TimeSpan.FromMinutes(-10)),
            "no-claim" => TestKeys.Mint(account, includeAccountClaim: false),
            _ => "not.a.jwt",
        };
        Assert.Null(await Verifier().VerifyAsync(jwt, CancellationToken.None));
    }
}
```

- [ ] **Step 3: Run; expect compile errors. Write `SessionVerification.cs`**

```csharp
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
```

- [ ] **Step 4: Register in `Program.cs`** (after the options line): `builder.Services.AddHttpClient<HttpJwksSource>(); builder.Services.AddSingleton<IJwksSource>(sp => sp.GetRequiredService<HttpJwksSource>()); builder.Services.AddSingleton<ISessionVerifier, StytchJwtVerifier>();` and in `GameServerFactory` add `services.AddSingleton<IJwksSource>(TestKeys.Source);` **after** `ConfigureTestServices` so tests never reach Stytch (last registration wins).

- [ ] **Step 5: Run `dotnet test tests/Tabletop.GameServer.Tests`; expect 8 passed. Commit: `Verify Stytch session JWTs locally against the JWKS`.**

### Task 3: Management's rows — the interface, reads, and the Postgres test fixture

**Files:**
- Create: `server/game/Persistence/IGameRows.cs`, `server/game/Persistence/RowMapping.cs`, `server/game/Persistence/PostgresGameRows.cs`
- Test: `tests/Tabletop.GameServer.Tests/Infrastructure/PostgresFixture.cs`, `tests/Tabletop.GameServer.Tests/GameRowsReadTests.cs`

**Interfaces:**
- Consumes: `GameInfo`, `Team`, `Participant`, `GameStatus`, `SchedulerShape`, `Enforcement`, `ArmyInPlay` (Protocol); `TableState`, `TerrainPlacement`, `TerrainVolume`, `Marker`, `DeploymentZone`, `VisualRef`, `Army`, `Unit`, `ModelSlot`, `Weapon`, `PropertyInstance`, `ArmySnapshot`, `ContentJson` (Content); `Volume`, `Footprint`, `Base`, `TerrainDeclarations`, `Point2`, `Point3`, `Yaw`, `TableSize` (Geometry); the tables in `scripts/postgres/init/001_schema.sql`. That file reaches a fresh database two ways that agree: the compose init applies it on first volume creation, and management's DbUp migrator carries the same bytes as its `001_schema.sql`, journals it as applied when it finds the tables already there, and adds `002_blob_uploaded.sql` (columns on `blob` this server never reads). So this server never migrates, and a bare `docker compose up -d` is enough for `PostgresFixture`.
- Produces: `interface IGameRows` — reads `Task<GameInfo?> ReadGameAsync(Guid gameId, CancellationToken ct)`, `Task<Guid?> FindParticipantAsync(Guid gameId, Guid accountId, CancellationToken ct)`, `Task<TableState?> ReadDraftAsync(Guid gameId, CancellationToken ct)`, `Task<IReadOnlyList<ArmyInPlay>> ReadSnapshotsAsync(Guid gameId, CancellationToken ct)`, `Task<OwnedArmy?> ReadArmyAsync(Guid armyId, CancellationToken ct)`, `Task<bool> PropertiesExistAsync(string hash, CancellationToken ct)`; writes (implemented in Tasks 10–12) `Task<Team> CreateTeamAsync(Guid gameId, CancellationToken ct)`, `Task SetParticipantTeamAsync(Guid participantId, Guid? teamId, CancellationToken ct)`, `Task SetParticipantArmyAsync(Guid participantId, Guid? armyId, CancellationToken ct)`, `Task SetConfigurationAsync(Guid gameId, SetConfiguration configuration, CancellationToken ct)`, `Task StartSetupAsync(Guid gameId, TableState draft, CancellationToken ct)`, `Task WriteDraftAsync(Guid gameId, TableState draft, CancellationToken ct)`, `Task EndSetupAsync(Guid gameId, TableState snapshot, IReadOnlyDictionary<Guid, ArmySnapshot?> snapshotsByParticipant, CancellationToken ct)`; `sealed record OwnedArmy(Army Army, Guid OwnerId)`; `sealed class PostgresGameRows(NpgsqlDataSource db) : IGameRows`; `static class RowMapping` with `string EnumText<T>(T value)`, `T EnumFrom<T>(string text)`, `VisualRef Visual(string?, Guid?)`, `Volume VolumeFrom(NpgsqlDataReader, int start)`, `const string VolumeColumns`, `object?[] VolumeValues(Volume)`, `NullableGuid`, `NullableString`; `PostgresGameRows.ReadTableStateAsync(NpgsqlConnection, NpgsqlTransaction?, Guid gameId, CancellationToken)` (internal static). Test infrastructure `PostgresFixture` (`NpgsqlDataSource Db`, `const long DefaultRngSeed`, `Task<SeededGame> SeedGameAsync(GameStatus status, int teams = 2, int participantsPerTeam = 1, int spectators = 0, Enforcement enforcement = Enforcement.Warn, long rngSeed = DefaultRngSeed)`, `Task<Army> SeedArmyAsync(Guid ownerId)`, `Task<string> SeedPropertiesAsync()`), `sealed record SeededGame(Guid GameId, Guid CreatorAccountId, IReadOnlyList<Guid> TeamIds, IReadOnlyList<SeededParticipant> Participants, string PropertiesHash, long RngSeed)`, `sealed record SeededParticipant(Guid ParticipantId, Guid AccountId, Guid? TeamId)`, and `[CollectionDefinition("Postgres")]`.

Spec: `architecture.md` → Game management data model (the game server reads management's schema at hydration; a read is not a join across the boundary — it is one database, two schemas); `schema.md` → the tables, Enumerations are `text` with a check constraint. Enum names on the wire and in the database are the same closed-set spellings, so `RowMapping` gets them from `WireJson` rather than keeping a second table. Army rows are read at `PickArmy`, to check the picker owns the army (Task 10), and at end-setup, to write the snapshot (setup phase → The lifecycle); nothing else reads them. `game.rng_seed` (`bigint not null`, no default) is read with the rest of the game's row into `GameInfo.RngSeed` and never written here — management writes it once on `POST /v1/games` (owner ruling 1, 2026-09-06; contracts plan spec correction 12). Because the column has no default, `PostgresFixture.SeedGameAsync` has to supply one, which is why it takes an `rngSeed` parameter.

- [ ] **Step 1: Write `IGameRows.cs`**

```csharp
using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Persistence;

public sealed record OwnedArmy(Army Army, Guid OwnerId);

/// <summary>
/// Management's rows as this server sees them. Reads at hydration and end-setup; writes only while the game is lobby or setup
/// (whole-design review ruling 4). Nothing here is a join across the schema boundary: the log is IEventLog.
/// </summary>
public interface IGameRows
{
    Task<GameInfo?> ReadGameAsync(Guid gameId, CancellationToken ct);
    Task<Guid?> FindParticipantAsync(Guid gameId, Guid accountId, CancellationToken ct);
    Task<TableState?> ReadDraftAsync(Guid gameId, CancellationToken ct);
    Task<IReadOnlyList<ArmyInPlay>> ReadSnapshotsAsync(Guid gameId, CancellationToken ct);
    Task<OwnedArmy?> ReadArmyAsync(Guid armyId, CancellationToken ct);
    Task<bool> PropertiesExistAsync(string hash, CancellationToken ct);

    Task<Team> CreateTeamAsync(Guid gameId, CancellationToken ct);
    Task SetParticipantTeamAsync(Guid participantId, Guid? teamId, CancellationToken ct);
    Task SetParticipantArmyAsync(Guid participantId, Guid? armyId, CancellationToken ct);
    Task SetConfigurationAsync(Guid gameId, SetConfiguration configuration, CancellationToken ct);
    /// <summary>Inserts the game's table_state tree and flips status to setup, in one transaction.</summary>
    Task StartSetupAsync(Guid gameId, TableState draft, CancellationToken ct);
    /// <summary>Rewrites the whole tree: one write per drop (setup phase → The draft is the row).</summary>
    Task WriteDraftAsync(Guid gameId, TableState draft, CancellationToken ct);
    /// <summary>Rewrites the tree (off-slab terrain already removed), writes each participant's army_snapshot, flips status to playing; one transaction.</summary>
    Task EndSetupAsync(Guid gameId, TableState snapshot, IReadOnlyDictionary<Guid, ArmySnapshot?> snapshotsByParticipant, CancellationToken ct);
}
```

- [ ] **Step 2: Write `PostgresFixture.cs`**

```csharp
using Npgsql;
using Tabletop.GameServer.Persistence;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests.Infrastructure;

[CollectionDefinition("Postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>;

public sealed record SeededParticipant(Guid ParticipantId, Guid AccountId, Guid? TeamId);
public sealed record SeededGame(Guid GameId, Guid CreatorAccountId, IReadOnlyList<Guid> TeamIds, IReadOnlyList<SeededParticipant> Participants, string PropertiesHash, long RngSeed);

/// <summary>Talks to the Docker Postgres from docker-compose.yml. Every row it seeds is deleted on dispose.</summary>
public sealed class PostgresFixture : IAsyncDisposable
{
    public static readonly string ConnectionString = Environment.GetEnvironmentVariable("TABLETOP_DB_CONNECTION")
        ?? "Host=localhost;Port=5432;Database=tabletop;Username=tabletop;Password=tabletop";
    public NpgsqlDataSource Db { get; } = NpgsqlDataSource.Create(ConnectionString);
    private readonly List<Guid> _games = new(), _accounts = new();
    private readonly List<string> _properties = new();

    public async Task<Guid> SeedAccountAsync(string name)
    {
        var id = Guid.CreateVersion7();
        await using var cmd = Db.CreateCommand("insert into management.account (id, name) values ($1, $2)");
        cmd.Parameters.AddWithValue(id); cmd.Parameters.AddWithValue(name);
        await cmd.ExecuteNonQueryAsync();
        _accounts.Add(id);
        return id;
    }

    public async Task<string> SeedPropertiesAsync()
    {
        var hash = "sha256:test-" + Guid.CreateVersion7().ToString("N")[..8];
        await using var cmd = Db.CreateCommand("insert into management.properties (content_hash, name, game_rules) values ($1, 'GF test', 'grimdark-future')");
        cmd.Parameters.AddWithValue(hash);
        await cmd.ExecuteNonQueryAsync();
        _properties.Add(hash);
        return hash;
    }

    /// <summary>The dice seed a seeded game gets. management.game.rng_seed has no default, so every insert names one; a fixed value keeps seeded games' dice identical run to run.</summary>
    public const long DefaultRngSeed = 8675309;

    public async Task<SeededGame> SeedGameAsync(GameStatus status, int teams = 2, int participantsPerTeam = 1, int spectators = 0, Enforcement enforcement = Enforcement.Warn, long rngSeed = DefaultRngSeed)
    {
        var hash = await SeedPropertiesAsync();
        var creator = await SeedAccountAsync("Creator");
        var gameId = Guid.CreateVersion7();
        await using (var cmd = Db.CreateCommand(
            "insert into management.game (id, name, status, game_rules, properties_hash, scheduler_shape, enforcement, waits_between_steps, rng_seed, created_by) " +
            "values ($1, 'Friday', $2, 'grimdark-future', $3, 'alternating-activation', $4, false, $5, $6)"))
        {
            cmd.Parameters.AddWithValue(gameId); cmd.Parameters.AddWithValue(RowMapping.EnumText(status));
            cmd.Parameters.AddWithValue(hash); cmd.Parameters.AddWithValue(RowMapping.EnumText(enforcement)); cmd.Parameters.AddWithValue(rngSeed); cmd.Parameters.AddWithValue(creator);
            await cmd.ExecuteNonQueryAsync();
        }
        _games.Add(gameId);
        var teamIds = new List<Guid>(); var participants = new List<SeededParticipant>();
        for (var t = 1; t <= teams; t++)
        {
            var teamId = Guid.CreateVersion7(); teamIds.Add(teamId);
            await using var cmd = Db.CreateCommand("insert into management.team (id, game_id, ordinal) values ($1, $2, $3)");
            cmd.Parameters.AddWithValue(teamId); cmd.Parameters.AddWithValue(gameId); cmd.Parameters.AddWithValue(t);
            await cmd.ExecuteNonQueryAsync();
            for (var p = 0; p < participantsPerTeam; p++)
                participants.Add(await SeedParticipantAsync(gameId, t == 1 && p == 0 ? creator : await SeedAccountAsync($"Player {t}.{p}"), teamId));
        }
        for (var s = 0; s < spectators; s++) participants.Add(await SeedParticipantAsync(gameId, await SeedAccountAsync($"Watcher {s}"), null));
        return new SeededGame(gameId, creator, teamIds, participants, hash, rngSeed);
    }

    public async Task<SeededParticipant> SeedParticipantAsync(Guid gameId, Guid accountId, Guid? teamId)
    {
        var id = Guid.CreateVersion7();
        await using var cmd = Db.CreateCommand("insert into management.participant (id, game_id, team_id, account_id) values ($1, $2, $3, $4)");
        cmd.Parameters.AddWithValue(id); cmd.Parameters.AddWithValue(gameId);
        cmd.Parameters.AddWithValue((object?)teamId ?? DBNull.Value); cmd.Parameters.AddWithValue(accountId);
        await cmd.ExecuteNonQueryAsync();
        return new SeededParticipant(id, accountId, teamId);
    }

    public async Task<int> CountAsync(string sql, params object[] args)
    {
        await using var cmd = Db.CreateCommand(sql);
        foreach (var a in args) cmd.Parameters.AddWithValue(a);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var g in _games) { await Exec("delete from game.game_event where game_id = $1", g); await Exec("delete from management.game where id = $1", g); }
        foreach (var a in _accounts) await Exec("delete from management.account where id = $1", a);
        foreach (var p in _properties) await Exec("delete from management.properties where content_hash = $1", p);
        await Db.DisposeAsync();
    }

    private async Task Exec(string sql, object arg) { await using var cmd = Db.CreateCommand(sql); cmd.Parameters.AddWithValue(arg); await cmd.ExecuteNonQueryAsync(); }
}
```

`SeedArmyAsync` is added in Step 3 below because it needs the army tables' column order; it inserts `ArmyTests.Sample()` under an owner.

- [ ] **Step 3: Add `SeedArmyAsync` to the fixture**

```csharp
    /// <summary>Inserts ArmyTests.Sample() under owner and returns exactly what it inserted (the sample makes fresh ids per call).</summary>
    public async Task<Tabletop.Content.Army> SeedArmyAsync(Guid ownerId)
    {
        var army = Tabletop.Content.Tests.ArmyTests.Sample();
        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        async Task Run(string sql, params object?[] args)
        {
            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            foreach (var a in args) cmd.Parameters.AddWithValue(a ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
        await Run("insert into management.army (id, owner_id, name, game_system, source) values ($1, $2, $3, $4, $5)", army.Id, ownerId, army.Name, army.GameSystem, army.Source);
        foreach (var u in army.Units)
        {
            await Run("insert into management.unit (id, army_id, ordinal, name) values ($1, $2, $3, $4)", u.Id, army.Id, u.Ordinal, u.Name);
            foreach (var (k, v) in u.Stats) await Run("insert into management.unit_stat values ($1, $2, $3)", u.Id, k, v);
            foreach (var p in u.Properties) await Run("insert into management.unit_property values ($1, $2, $3)", u.Id, p.Name, p.Parameter);
            foreach (var s in u.Models)
            {
                await Run("insert into management.model_slot (id, unit_id, ordinal, name, base_shape, base_a, base_b, height, asset_id) values ($1, $2, $3, $4, $5, $6, $7, $8, $9)",
                    s.Id, u.Id, s.Ordinal, s.Name, RowMapping.EnumText(s.Base.Shape), (float)s.Base.A, (float)s.Base.B, (float)s.Height, s.AssetId);
                foreach (var (k, v) in s.Stats) await Run("insert into management.slot_stat values ($1, $2, $3)", s.Id, k, v);
                foreach (var p in s.Properties) await Run("insert into management.slot_property values ($1, $2, $3)", s.Id, p.Name, p.Parameter);
                foreach (var w in s.Weapons)
                {
                    await Run("insert into management.weapon (id, slot_id, name) values ($1, $2, $3)", w.Id, s.Id, w.Name);
                    foreach (var (k, v) in w.Stats) await Run("insert into management.weapon_stat values ($1, $2, $3)", w.Id, k, v);
                    foreach (var p in w.Properties) await Run("insert into management.weapon_property values ($1, $2, $3)", w.Id, p.Name, p.Parameter);
                }
            }
        }
        await tx.CommitAsync();
        return army;
    }
```

- [ ] **Step 4: Write the failing read tests**

```csharp
using Tabletop.GameServer.Persistence;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

[Collection("Postgres")]
public class GameRowsReadTests(PostgresFixture pg)
{
    readonly IGameRows _rows = new PostgresGameRows(pg.Db);

    [Fact]
    public async Task Reads_a_lobby_game_with_teams_and_participants()
    {
        var seeded = await pg.SeedGameAsync(GameStatus.Lobby, teams: 2, participantsPerTeam: 1, spectators: 1);
        var game = await _rows.ReadGameAsync(seeded.GameId, CancellationToken.None);
        Assert.NotNull(game);
        Assert.Equal((GameStatus.Lobby, "Friday", seeded.CreatorAccountId, "grimdark-future", SchedulerShape.AlternatingActivation, Enforcement.Warn),
            (game.Status, game.Name, game.CreatedBy, game.GameRules, game.SchedulerShape, game.Enforcement));
        Assert.Equal(seeded.RngSeed, game.RngSeed);      // the stored dice seed arrives with the rest of the row; this server never writes it
        Assert.Equal([1, 2], game.Teams.Select(t => t.Ordinal));
        Assert.Equal(3, game.Participants.Count);
        Assert.Contains(game.Participants, p => p.TeamId is null && p.Name == "Watcher 0");
        Assert.Contains(game.Participants, p => p.AccountId == seeded.CreatorAccountId && p.Name == "Creator" && p.TeamId == seeded.TeamIds[0]);
        Assert.Null(await _rows.ReadDraftAsync(seeded.GameId, CancellationToken.None));
        Assert.Empty(await _rows.ReadSnapshotsAsync(seeded.GameId, CancellationToken.None));
        Assert.True(await _rows.PropertiesExistAsync(seeded.PropertiesHash, CancellationToken.None));
        Assert.False(await _rows.PropertiesExistAsync("sha256:nope", CancellationToken.None));
    }

    [Fact]
    public async Task Finds_a_participant_by_game_and_account()
    {
        var seeded = await pg.SeedGameAsync(GameStatus.Lobby);
        Assert.Equal(seeded.Participants[0].ParticipantId, await _rows.FindParticipantAsync(seeded.GameId, seeded.CreatorAccountId, CancellationToken.None));
        Assert.Null(await _rows.FindParticipantAsync(seeded.GameId, Guid.CreateVersion7(), CancellationToken.None));
        Assert.Null(await _rows.ReadGameAsync(Guid.CreateVersion7(), CancellationToken.None));
    }

    [Fact]
    public async Task Reads_an_army_tree_back_to_the_sample()
    {
        var owner = await pg.SeedAccountAsync("Owner");
        var seeded = await pg.SeedArmyAsync(owner);
        var owned = await _rows.ReadArmyAsync(seeded.Id, CancellationToken.None);
        Assert.NotNull(owned);
        Assert.Equal(owner, owned.OwnerId);
        Assert.Equal(Tabletop.Content.ContentJson.Serialize(seeded), Tabletop.Content.ContentJson.Serialize(owned.Army));
    }
}
```

- [ ] **Step 5: Run; expect compile errors. Write `RowMapping.cs`**

```csharp
using Npgsql;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Persistence;

/// <summary>Enum spellings come from the wire format, which is the closed set's one spelling. Reading and writing the table_state tree.</summary>
public static class RowMapping
{
    public static string EnumText<T>(T value) where T : struct, Enum => WireJson.Serialize(value).Trim('"');
    public static T EnumFrom<T>(string text) where T : struct, Enum => WireJson.Deserialize<T>($"\"{text}\"");

    public static VisualRef Visual(string? builtin, Guid? assetId) => builtin is not null ? VisualRef.OfBuiltin(builtin) : VisualRef.OfAsset(assetId!.Value);

    public static Volume VolumeFrom(NpgsqlDataReader r, int start) => new(
        EnumFrom<VolumeKind>(r.GetString(start)), new Footprint(EnumFrom<FootprintKind>(r.GetString(start + 1)), r.GetFloat(start + 2), r.GetFloat(start + 3)),
        new Point2(r.GetFloat(start + 4), r.GetFloat(start + 5)), new Yaw(r.GetFloat(start + 6)), r.GetFloat(start + 7), r.GetFloat(start + 8),
        Declarations(r, start + 9), r.IsDBNull(start + 15) ? null : r.GetString(start + 15));

    static TerrainDeclarations Declarations(NpgsqlDataReader r, int i)
    {
        var d = TerrainDeclarations.None;
        if (r.GetBoolean(i)) d |= TerrainDeclarations.Impassable;
        if (r.GetBoolean(i + 1)) d |= TerrainDeclarations.BlocksLineOfSight;
        if (r.GetBoolean(i + 2)) d |= TerrainDeclarations.SeeInAndOut;
        if (r.GetBoolean(i + 3)) d |= TerrainDeclarations.Cover;
        if (r.GetBoolean(i + 4)) d |= TerrainDeclarations.Difficult;
        if (r.GetBoolean(i + 5)) d |= TerrainDeclarations.Dangerous;
        return d;
    }

    public const string VolumeColumns = "kind, footprint, dim_a, dim_b, offset_x, offset_y, yaw, bottom, top, impassable, blocks_los, see_in_out, cover, difficult, dangerous, notes";

    public static object?[] VolumeValues(Volume v) =>
    [
        EnumText(v.Kind), EnumText(v.Footprint.Kind), (float)v.Footprint.A, (float)v.Footprint.B, (float)v.Offset.X, (float)v.Offset.Y, (float)v.Yaw.Degrees,
        (float)v.Bottom, (float)v.Top, v.Declarations.HasFlag(TerrainDeclarations.Impassable), v.Declarations.HasFlag(TerrainDeclarations.BlocksLineOfSight),
        v.Declarations.HasFlag(TerrainDeclarations.SeeInAndOut), v.Declarations.HasFlag(TerrainDeclarations.Cover), v.Declarations.HasFlag(TerrainDeclarations.Difficult),
        v.Declarations.HasFlag(TerrainDeclarations.Dangerous), v.Notes,
    ];

    public static Guid? NullableGuid(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetGuid(i);
    public static string? NullableString(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
}
```

- [ ] **Step 6: Write `PostgresGameRows.cs` (reads; the writes partial comes in Tasks 10–12)**

```csharp
using Npgsql;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using static Tabletop.GameServer.Persistence.RowMapping;

namespace Tabletop.GameServer.Persistence;

public sealed partial class PostgresGameRows(NpgsqlDataSource db) : IGameRows
{
    public async Task<GameInfo?> ReadGameAsync(Guid gameId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var game = new NpgsqlCommand("select name, status, game_rules, properties_hash, scheduler_shape, enforcement, waits_between_steps, rng_seed, created_by from management.game where id = $1", conn) { Parameters = { new NpgsqlParameter { Value = gameId } } };
        await using var r = await game.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        var (name, status, rules, hash, shape, enforcement, waits, rngSeed, createdBy) =
            (r.GetString(0), EnumFrom<GameStatus>(r.GetString(1)), r.GetString(2), r.GetString(3), EnumFrom<SchedulerShape>(r.GetString(4)), EnumFrom<Enforcement>(r.GetString(5)), r.GetBoolean(6), r.GetInt64(7), r.GetGuid(8));
        await r.CloseAsync();

        var teams = new List<Team>();
        await using (var cmd = new NpgsqlCommand("select id, ordinal from management.team where game_id = $1 order by ordinal", conn) { Parameters = { new NpgsqlParameter { Value = gameId } } })
        await using (var tr = await cmd.ExecuteReaderAsync(ct))
            while (await tr.ReadAsync(ct)) teams.Add(new Team(tr.GetGuid(0), tr.GetInt32(1)));

        var participants = new List<Participant>();
        await using (var cmd = new NpgsqlCommand("select p.id, p.account_id, a.name, p.team_id, p.army_id from management.participant p join management.account a on a.id = p.account_id where p.game_id = $1 order by p.id", conn) { Parameters = { new NpgsqlParameter { Value = gameId } } })
        await using (var pr = await cmd.ExecuteReaderAsync(ct))
            while (await pr.ReadAsync(ct)) participants.Add(new Participant(pr.GetGuid(0), pr.GetGuid(1), pr.GetString(2), NullableGuid(pr, 3), NullableGuid(pr, 4)));

        return new GameInfo(gameId, name, status, createdBy, rules, hash, shape, enforcement, waits, rngSeed, teams, participants);
    }

    public async Task<Guid?> FindParticipantAsync(Guid gameId, Guid accountId, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("select id from management.participant where game_id = $1 and account_id = $2");
        cmd.Parameters.AddWithValue(gameId); cmd.Parameters.AddWithValue(accountId);
        return await cmd.ExecuteScalarAsync(ct) is Guid id ? id : null;
    }

    public async Task<bool> PropertiesExistAsync(string hash, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("select exists (select 1 from management.properties where content_hash = $1)");
        cmd.Parameters.AddWithValue(hash);
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<TableState?> ReadDraftAsync(Guid gameId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await ReadTableStateAsync(conn, null, gameId, ct);
    }

    internal static async Task<TableState?> ReadTableStateAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, Guid gameId, CancellationToken ct)
    {
        Guid id; string? name; TableSize size; VisualRef texture;
        await using (var cmd = new NpgsqlCommand("select id, name, width, length, texture_builtin, texture_asset_id from management.table_state where game_id = $1", conn, tx) { Parameters = { new NpgsqlParameter { Value = gameId } } })
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) return null;
            (id, name, size, texture) = (r.GetGuid(0), NullableString(r, 1), new TableSize(r.GetFloat(2), r.GetFloat(3)), Visual(NullableString(r, 4), NullableGuid(r, 5)));
        }
        var volumes = new Dictionary<Guid, List<TerrainVolume>>();
        await using (var cmd = new NpgsqlCommand($"select v.id, v.terrain_placement_id, {string.Join(", ", VolumeColumns.Split(", ").Select(c => "v." + c))} from management.terrain_volume v join management.terrain_placement p on p.id = v.terrain_placement_id where p.table_state_id = $1 order by v.id", conn, tx) { Parameters = { new NpgsqlParameter { Value = id } } })
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
            {
                var list = volumes.TryGetValue(r.GetGuid(1), out var l) ? l : volumes[r.GetGuid(1)] = new();
                list.Add(new TerrainVolume(r.GetGuid(0), VolumeFrom(r, 2)));
            }
        var terrain = new List<TerrainPlacement>();
        await using (var cmd = new NpgsqlCommand("select id, builtin, asset_id, pos_x, pos_y, pos_z, yaw, scale from management.terrain_placement where table_state_id = $1 order by id", conn, tx) { Parameters = { new NpgsqlParameter { Value = id } } })
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                terrain.Add(new TerrainPlacement(r.GetGuid(0), Visual(NullableString(r, 1), NullableGuid(r, 2)), new Point3(r.GetFloat(3), r.GetFloat(4), r.GetFloat(5)), new Yaw(r.GetFloat(6)), r.GetFloat(7),
                    volumes.TryGetValue(r.GetGuid(0), out var vs) ? vs : []));
        var markers = new List<Marker>();
        await using (var cmd = new NpgsqlCommand("select id, kind, pos_x, pos_y, pos_z, yaw, diameter, label, visual_builtin, visual_asset_id from management.marker where table_state_id = $1 order by id", conn, tx) { Parameters = { new NpgsqlParameter { Value = id } } })
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                markers.Add(new Marker(r.GetGuid(0), EnumFrom<MarkerKind>(r.GetString(1)), new Point3(r.GetFloat(2), r.GetFloat(3), r.GetFloat(4)), new Yaw(r.GetFloat(5)), r.GetFloat(6), NullableString(r, 7), Visual(NullableString(r, 8), NullableGuid(r, 9))));
        var zones = new List<DeploymentZone>();
        await using (var cmd = new NpgsqlCommand("select id, style, team_ordinal from management.deployment_zone where table_state_id = $1 order by team_ordinal", conn, tx) { Parameters = { new NpgsqlParameter { Value = id } } })
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) zones.Add(new DeploymentZone(r.GetGuid(0), r.GetString(1), r.GetInt32(2)));
        return new TableState(id, name, size, texture, terrain, markers, zones);
    }

    public async Task<IReadOnlyList<ArmyInPlay>> ReadSnapshotsAsync(Guid gameId, CancellationToken ct)
    {
        var result = new List<ArmyInPlay>();
        await using var cmd = db.CreateCommand("select id, army_snapshot from management.participant where game_id = $1 and army_snapshot is not null order by id");
        cmd.Parameters.AddWithValue(gameId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) result.Add(new ArmyInPlay(r.GetGuid(0), ContentJson.Deserialize<ArmySnapshot>(r.GetString(1))));
        return result;
    }

    public async Task<OwnedArmy?> ReadArmyAsync(Guid armyId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        Guid owner; string name, system, source;
        await using (var cmd = new NpgsqlCommand("select owner_id, name, game_system, source from management.army where id = $1", conn) { Parameters = { new NpgsqlParameter { Value = armyId } } })
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) return null;
            (owner, name, system, source) = (r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3));
        }
        var stats = new Dictionary<Guid, Dictionary<string, string>>();
        var props = new Dictionary<Guid, List<PropertyInstance>>();
        foreach (var (table, owner_col, parent) in new[] { ("unit_stat", "unit_id", "unit u on u.id = s.unit_id where u.army_id"), ("slot_stat", "slot_id", "model_slot m on m.id = s.slot_id join management.unit u on u.id = m.unit_id where u.army_id"), ("weapon_stat", "weapon_id", "weapon w on w.id = s.weapon_id join management.model_slot m on m.id = w.slot_id join management.unit u on u.id = m.unit_id where u.army_id") })
        await using (var cmd = new NpgsqlCommand($"select s.{owner_col}, s.stat, s.value from management.{table} s join management.{parent} = $1", conn) { Parameters = { new NpgsqlParameter { Value = armyId } } })
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) (stats.TryGetValue(r.GetGuid(0), out var d) ? d : stats[r.GetGuid(0)] = new())[r.GetString(1)] = r.GetString(2);
        foreach (var (table, owner_col, parent) in new[] { ("unit_property", "unit_id", "unit u on u.id = s.unit_id where u.army_id"), ("slot_property", "slot_id", "model_slot m on m.id = s.slot_id join management.unit u on u.id = m.unit_id where u.army_id"), ("weapon_property", "weapon_id", "weapon w on w.id = s.weapon_id join management.model_slot m on m.id = w.slot_id join management.unit u on u.id = m.unit_id where u.army_id") })
        await using (var cmd = new NpgsqlCommand($"select s.{owner_col}, s.name, s.parameter from management.{table} s join management.{parent} = $1 order by s.name", conn) { Parameters = { new NpgsqlParameter { Value = armyId } } })
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) (props.TryGetValue(r.GetGuid(0), out var l) ? l : props[r.GetGuid(0)] = new()).Add(new PropertyInstance(r.GetString(1), r.IsDBNull(2) ? null : r.GetInt32(2)));
        IReadOnlyDictionary<string, string> S(Guid id) => stats.TryGetValue(id, out var d) ? d : new Dictionary<string, string>();
        IReadOnlyList<PropertyInstance> P(Guid id) => props.TryGetValue(id, out var l) ? l : [];

        var weapons = new Dictionary<Guid, List<Weapon>>();
        await using (var cmd = new NpgsqlCommand("select w.id, w.slot_id, w.name from management.weapon w join management.model_slot m on m.id = w.slot_id join management.unit u on u.id = m.unit_id where u.army_id = $1 order by w.id", conn) { Parameters = { new NpgsqlParameter { Value = armyId } } })
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) (weapons.TryGetValue(r.GetGuid(1), out var l) ? l : weapons[r.GetGuid(1)] = new()).Add(new Weapon(r.GetGuid(0), r.GetString(2), S(r.GetGuid(0)), P(r.GetGuid(0))));
        var slots = new Dictionary<Guid, List<ModelSlot>>();
        await using (var cmd = new NpgsqlCommand("select m.id, m.unit_id, m.ordinal, m.name, m.base_shape, m.base_a, m.base_b, m.height, m.asset_id from management.model_slot m join management.unit u on u.id = m.unit_id where u.army_id = $1 order by m.ordinal", conn) { Parameters = { new NpgsqlParameter { Value = armyId } } })
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                (slots.TryGetValue(r.GetGuid(1), out var l) ? l : slots[r.GetGuid(1)] = new()).Add(new ModelSlot(r.GetGuid(0), r.GetInt32(2), r.GetString(3), S(r.GetGuid(0)), P(r.GetGuid(0)),
                    new Base(EnumFrom<BaseShape>(r.GetString(4)), r.GetFloat(5), r.GetFloat(6)), r.GetFloat(7), NullableGuid(r, 8), weapons.TryGetValue(r.GetGuid(0), out var ws) ? ws : []));
        var units = new List<Unit>();
        await using (var cmd = new NpgsqlCommand("select id, ordinal, name from management.unit where army_id = $1 order by ordinal", conn) { Parameters = { new NpgsqlParameter { Value = armyId } } })
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) units.Add(new Unit(r.GetGuid(0), r.GetInt32(1), r.GetString(2), S(r.GetGuid(0)), P(r.GetGuid(0)), slots.TryGetValue(r.GetGuid(0), out var ss) ? ss : []));
        return new OwnedArmy(new Army(armyId, name, system, source, units), owner);
    }
}
```

`real` columns come back as `float`; `TableSize`, `Point3` and `Base` take `double`, so the widening is implicit. The sample's `1.26` base survives `real` only approximately: if the JSON comparison in `Reads_an_army_tree_back_to_the_sample` fails on `1.26` vs `1.2599999904632568`, compare with `Assert.Equal(expected.A, actual.A, precision: 5)` per numeric field instead of the JSON string — do not change the column types.

- [ ] **Step 7: Register `IGameRows` in `Program.cs`**: `builder.Services.AddSingleton(sp => NpgsqlDataSource.Create(sp.GetRequiredService<GameServerOptions>().DbConnection)); builder.Services.AddSingleton<IGameRows, PostgresGameRows>();`

- [ ] **Step 8: Run `docker compose up -d` then `dotnet test tests/Tabletop.GameServer.Tests`; expect 11 passed. Commit: `Read a game's rows from management's schema`.**

`/docker-entrypoint-initdb.d` runs only when the data directory is empty, and `PostgresFixture` neither migrates nor checks. On a volume created before `001_schema.sql` existed the tests fail with `relation "management.game" does not exist`; the reset is `docker compose down -v && docker compose up -d` (contracts plan → Local development).

### Task 4: The event log and replay

**Files:**
- Create: `server/game/Log/IEventLog.cs`, `server/game/Log/InMemoryEventLog.cs`, `server/game/Log/PostgresEventLog.cs`, `server/game/Log/Replay.cs`
- Test: `tests/Tabletop.GameServer.Tests/EventLogContract.cs`, `tests/Tabletop.GameServer.Tests/InMemoryEventLogTests.cs`, `tests/Tabletop.GameServer.Tests/PostgresEventLogTests.cs`, `tests/Tabletop.GameServer.Tests/ReplayTests.cs`

**Interfaces:**
- Consumes: `GameEvent`, `EventPayload`, `Undone`, `EventApplier`, `Projection`, `Board`, `GameInfo`, `WireJson`, `EventKinds` (Protocol); `Samples` (Protocol tests).
- Produces: `interface IEventLog { Task AppendAsync(Guid gameId, IReadOnlyList<GameEvent> events, CancellationToken ct); Task<IReadOnlyList<GameEvent>> ReadAsync(Guid gameId, long afterSeq, CancellationToken ct); Task<long> LastSeqAsync(Guid gameId, CancellationToken ct); }`; `sealed class DuplicateSequenceException(Guid gameId, long seq) : Exception`; `sealed class InMemoryEventLog : IEventLog`; `sealed class PostgresEventLog(NpgsqlDataSource db) : IEventLog`; `static class Replay` with `Projection To(Board board, GameInfo game, IReadOnlyList<GameEvent> log, long seq)` and `bool IsValidUndoTarget(EventApplier applier, long toSeq)`.
- Append is all-or-nothing and the `(game_id, seq)` primary key is the authority on ordering: a second writer loses with `DuplicateSequenceException`, which the session treats as fatal for that game (Task 7).

Spec: `architecture.md` → Persistence (append-only `(game_id, seq, kind, payload jsonb)`, unique `(game_id, seq)`), Undo (replay to N from the initial state; the log stays append-only, `Undone` is itself an event); setup content → The lock is structural (the snapshot is the initial state, so `Replay.To` starts from `Board`).

- [ ] **Step 1: Write the contract tests**

```csharp
using Tabletop.GameServer.Log;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.GameServer.Tests;

public abstract class EventLogContract
{
    protected abstract Task<(IEventLog Log, Guid GameId)> CreateAsync();
    static GameEvent Ev(long seq, EventPayload p) => new(seq, Samples.P1, p, null);

    [Fact]
    public async Task Appends_and_reads_back_in_order_with_payload_intact()
    {
        var (log, game) = await CreateAsync();
        await log.AppendAsync(game, [Ev(1, new TurnStarted(Samples.Team1)), Ev(2, new ScoreSet(Samples.Team1, 3))], CancellationToken.None);
        await log.AppendAsync(game, [new GameEvent(3, null, new Undone(1), "outside the legal set")], CancellationToken.None);
        var all = await log.ReadAsync(game, 0, CancellationToken.None);
        Assert.Equal([1L, 2L, 3L], all.Select(e => e.Seq));
        Assert.Equal(WireJson.Serialize(Ev(2, new ScoreSet(Samples.Team1, 3))), WireJson.Serialize(all[1]));
        Assert.Equal("outside the legal set", all[2].Warning);
        Assert.Null(all[2].Actor);
        Assert.Equal([3L], (await log.ReadAsync(game, 2, CancellationToken.None)).Select(e => e.Seq));
        Assert.Equal(3, await log.LastSeqAsync(game, CancellationToken.None));
    }

    [Fact]
    public async Task Empty_log_reads_empty_and_last_seq_is_zero()
    {
        var (log, game) = await CreateAsync();
        Assert.Empty(await log.ReadAsync(game, 0, CancellationToken.None));
        Assert.Equal(0, await log.LastSeqAsync(game, CancellationToken.None));
    }

    [Fact]
    public async Task A_duplicate_seq_is_refused_and_nothing_of_the_batch_lands()
    {
        var (log, game) = await CreateAsync();
        await log.AppendAsync(game, [Ev(1, new TurnStarted(Samples.Team1))], CancellationToken.None);
        await Assert.ThrowsAsync<DuplicateSequenceException>(() => log.AppendAsync(game, [Ev(2, new TurnStarted(Samples.Team2)), Ev(1, new TurnStarted(Samples.Team1))], CancellationToken.None));
        Assert.Equal(1, await log.LastSeqAsync(game, CancellationToken.None));
    }

    [Fact]
    public async Task Games_do_not_see_each_others_events()
    {
        var (log, game) = await CreateAsync();
        var (_, other) = await CreateAsync();
        await log.AppendAsync(game, [Ev(1, new TurnStarted(Samples.Team1))], CancellationToken.None);
        Assert.Empty(await log.ReadAsync(other, 0, CancellationToken.None));
    }
}

public class InMemoryEventLogTests : EventLogContract
{
    readonly InMemoryEventLog _log = new();
    protected override Task<(IEventLog, Guid)> CreateAsync() => Task.FromResult<(IEventLog, Guid)>((_log, Guid.CreateVersion7()));
}
```

```csharp
using Tabletop.GameServer.Log;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

[Collection("Postgres")]
public class PostgresEventLogTests(PostgresFixture pg) : EventLogContract
{
    protected override async Task<(IEventLog, Guid)> CreateAsync() => (new PostgresEventLog(pg.Db), (await pg.SeedGameAsync(GameStatus.Playing)).GameId);

    [Fact]
    public async Task Kind_column_is_the_registered_discriminator()
    {
        var (log, game) = await CreateAsync();
        await log.AppendAsync(game, [new GameEvent(1, null, new PhaseStarted("deployment"), null)], CancellationToken.None);
        Assert.Equal(1, await pg.CountAsync("select count(*) from game.game_event where game_id = $1 and kind = 'phase_started' and payload->'payload'->>'kind' = 'phase_started'", game));
    }
}
```

- [ ] **Step 2: Write the replay tests**

```csharp
using Tabletop.GameServer.Log;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.GameServer.Tests;

public class ReplayTests
{
    static GameEvent Ev(long seq, EventPayload p) => new(seq, Samples.P1, p, null);
    static readonly GameEvent[] Log =
    [
        Ev(1, new ScoreSet(Samples.Team1, 1)), Ev(2, new ScoreSet(Samples.Team1, 2)), Ev(3, new ScoreSet(Samples.Team1, 3)),
        Ev(4, new Undone(1)), Ev(5, new ScoreSet(Samples.Team2, 9)),
    ];

    [Theory]
    [InlineData(0, 0, 0)] [InlineData(1, 1, 0)] [InlineData(2, 2, 0)] [InlineData(3, 3, 0)] [InlineData(4, 1, 0)] [InlineData(5, 1, 9)]
    public void Replay_to_n_lands_on_the_settled_state_at_n(long seq, int team1, int team2)
    {
        var p = Replay.To(Samples.Board, Samples.Game, Log, seq);
        Assert.Equal((team1, team2), (p.Scores[Samples.Team1], p.Scores[Samples.Team2]));
    }

    [Fact]
    public void Undo_targets_are_zero_or_a_live_seq()
    {
        var applier = new EventApplier(Samples.Board, Samples.Game);
        applier.ApplyAll(Log);
        Assert.True(Replay.IsValidUndoTarget(applier, 0));
        Assert.True(Replay.IsValidUndoTarget(applier, 1));
        Assert.True(Replay.IsValidUndoTarget(applier, 5));
        Assert.False(Replay.IsValidUndoTarget(applier, 2));   // undone
        Assert.False(Replay.IsValidUndoTarget(applier, 4));   // the marker itself
        Assert.False(Replay.IsValidUndoTarget(applier, 6));   // the future
    }
}
```

Replaying to `seq = 4` (the `Undone(1)` marker) yields the state after that undo, which is the state at 1 — the table above says so.

- [ ] **Step 3: Run; expect compile errors. Write `IEventLog.cs` and `InMemoryEventLog.cs`**

```csharp
using Tabletop.Protocol;

namespace Tabletop.GameServer.Log;

/// <summary>The game schema's one table. Append is atomic per batch; seq is assigned by the session and enforced by the primary key.</summary>
public interface IEventLog
{
    Task AppendAsync(Guid gameId, IReadOnlyList<GameEvent> events, CancellationToken ct);
    Task<IReadOnlyList<GameEvent>> ReadAsync(Guid gameId, long afterSeq, CancellationToken ct);
    Task<long> LastSeqAsync(Guid gameId, CancellationToken ct);
}

public sealed class DuplicateSequenceException(Guid gameId, long seq) : Exception($"Game {gameId} already has an event at seq {seq}.")
{
    public Guid GameId { get; } = gameId;
    public long Seq { get; } = seq;
}
```

```csharp
using Tabletop.Protocol;

namespace Tabletop.GameServer.Log;

public sealed class InMemoryEventLog : IEventLog
{
    private readonly Dictionary<Guid, SortedDictionary<long, GameEvent>> _logs = new();
    private readonly object _gate = new();

    public Task AppendAsync(Guid gameId, IReadOnlyList<GameEvent> events, CancellationToken ct)
    {
        lock (_gate)
        {
            var log = _logs.TryGetValue(gameId, out var l) ? l : _logs[gameId] = new();
            var dup = events.FirstOrDefault(e => log.ContainsKey(e.Seq)) ?? events.GroupBy(e => e.Seq).FirstOrDefault(g => g.Count() > 1)?.First();
            if (dup is not null) throw new DuplicateSequenceException(gameId, dup.Seq);
            foreach (var e in events) log[e.Seq] = e;
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GameEvent>> ReadAsync(Guid gameId, long afterSeq, CancellationToken ct)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<GameEvent>>(_logs.TryGetValue(gameId, out var l) ? l.Values.Where(e => e.Seq > afterSeq).ToList() : []);
    }

    public Task<long> LastSeqAsync(Guid gameId, CancellationToken ct)
    {
        lock (_gate) return Task.FromResult(_logs.TryGetValue(gameId, out var l) && l.Count > 0 ? l.Keys.Max() : 0L);
    }
}
```

- [ ] **Step 4: Write `PostgresEventLog.cs` and `Replay.cs`**

```csharp
using Npgsql;
using NpgsqlTypes;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Log;

/// <summary>game.game_event: kind is GameEvent.Kind, payload is WireJson.Serialize(GameEvent) — seq, actor and warning ride inside the jsonb.</summary>
public sealed class PostgresEventLog(NpgsqlDataSource db) : IEventLog
{
    public async Task AppendAsync(Guid gameId, IReadOnlyList<GameEvent> events, CancellationToken ct)
    {
        if (events.Count == 0) return;
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            foreach (var e in events)
            {
                await using var cmd = new NpgsqlCommand("insert into game.game_event (game_id, seq, kind, payload) values ($1, $2, $3, $4)", conn, tx);
                cmd.Parameters.AddWithValue(gameId); cmd.Parameters.AddWithValue(e.Seq); cmd.Parameters.AddWithValue(e.Kind);
                cmd.Parameters.Add(new NpgsqlParameter { Value = WireJson.Serialize(e), NpgsqlDbType = NpgsqlDbType.Jsonb });
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await tx.RollbackAsync(ct);
            var last = await LastSeqAsync(gameId, ct);
            throw new DuplicateSequenceException(gameId, events.Select(e => e.Seq).Where(s => s <= last).DefaultIfEmpty(events[0].Seq).Min());
        }
    }

    public async Task<IReadOnlyList<GameEvent>> ReadAsync(Guid gameId, long afterSeq, CancellationToken ct)
    {
        var result = new List<GameEvent>();
        await using var cmd = db.CreateCommand("select payload::text from game.game_event where game_id = $1 and seq > $2 order by seq");
        cmd.Parameters.AddWithValue(gameId); cmd.Parameters.AddWithValue(afterSeq);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) result.Add(WireJson.Deserialize<GameEvent>(r.GetString(0)));
        return result;
    }

    public async Task<long> LastSeqAsync(Guid gameId, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("select coalesce(max(seq), 0) from game.game_event where game_id = $1");
        cmd.Parameters.AddWithValue(gameId);
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }
}
```

```csharp
using Tabletop.Protocol;

namespace Tabletop.GameServer.Log;

/// <summary>Undo by replay: the initial state is the setup snapshot (Board), and the applier does the rest.</summary>
public static class Replay
{
    public static Projection To(Board board, GameInfo game, IReadOnlyList<GameEvent> log, long seq)
    {
        var applier = new EventApplier(board, game);
        applier.ApplyAll(log.Where(e => e.Seq <= seq));
        return applier.Projection;
    }

    /// <summary>0, or the seq of a live (not undone, not an Undone marker) event. The server refuses anything else (contracts plan → obligations on this plan).</summary>
    public static bool IsValidUndoTarget(EventApplier applier, long toSeq) =>
        toSeq == 0 || EventApplier.EffectiveLog(applier.Log).Any(e => e.Seq == toSeq);
}
```

The in-memory duplicate detection covers the duplicate-within-the-batch case too (`Ev(2), Ev(1)` where 1 exists), which the Postgres path catches through the primary key.

- [ ] **Step 5: Register**: `builder.Services.AddSingleton<IEventLog, PostgresEventLog>();`

- [ ] **Step 6: Run `dotnet test tests/Tabletop.GameServer.Tests`; expect 27 passed. Commit: `Add the append-only event log and replay to a sequence`.**

### Task 5: Legal-set membership, the enforcement policy and per-viewer filtering

**Files:**
- Create: `server/game/Dispatch/LegalSetMembership.cs`, `server/game/Dispatch/EnforcementPolicy.cs`, `server/game/Dispatch/EventFilter.cs`
- Test: `tests/Tabletop.GameServer.Tests/LegalSetMembershipTests.cs`, `tests/Tabletop.GameServer.Tests/EnforcementPolicyTests.cs`, `tests/Tabletop.GameServer.Tests/EventFilterTests.cs`

**Interfaces:**
- Consumes: `Ask`, `LegalSet` and its seven kinds, `AskAnswer` and its eight kinds, `Enforcement`, `RefusalReason`, `GameEvent`, `AskOpened`, `AskAnswered`, `AskClosed` (Protocol).
- Produces: `static class LegalSetMembership { static bool Contains(Ask ask, AskAnswer answer); }`; `enum Legality { AlwaysAccepted, Legal, OutsideLegalSet, Illegal }`; `abstract record Verdict` with `Accept(string? Warning)` and `Refuse(RefusalReason Reason, string Detail)`; `static class EnforcementPolicy { static Verdict Decide(Enforcement enforcement, Legality legality, string detail); }`; `static class EventFilter { static IReadOnlyList<GameEvent> ForViewer(IReadOnlyList<GameEvent> events, Guid? viewerTeamId, IReadOnlySet<Guid> sealedOpenBefore); static IReadOnlySet<Guid> SealedOpenAsks(Projection p); }`.
- `Contains` is rules-free set membership over the closed `LegalSet` shapes. It does **not** check a `MoveRegion` destination's distance — that is geometry the geometry plan implements and the interpreter plan's move step calls; here only the model ids are checked. Recorded as *Interfaces needed* item 3.
- `TokenSpend(IReadOnlyDictionary<Guid,int> MaxByModel)` and `TokensSpent(IReadOnlyDictionary<Guid,int> Counts)` are maps keyed by model (contracts plan Task 8, owner ruling of 2026-09-06): one entry per eligible spender, valued at that model's own token count, with no total and no per-team maximum. `Contains` checks that every key of `Counts` is a key of `MaxByModel` and every value is between zero and that key's cap, and checks **no total**, because p.13 lets a model spend any number up to what it holds. It does **not** check that the answering team owns those keys — no state here answers that, so the interpreter plan's spend step does, on the same division as `MoveRegion`'s model ids against the move step's distance.

Spec: `architecture.md` → Rules enforcement (three positions; menu edits accepted under every setting; strict refuses; warn logs a warning); action sequences → Asks (pass is a real answer, legal only when the Ask allows it), Windows (sealed: answers withheld until the window closes, then revealed); `architecture.md` → per-player filtered event streams; contracts plan Task 9 (`ask_answered.Answer` is null on the stream to teams a sealed answer is withheld from; `ask_closed` reveals all).

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.GameServer.Dispatch;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.GameServer.Tests;

public class LegalSetMembershipTests
{
    static Ask With(LegalSet legal, bool allowPass = false) => new(Guid.CreateVersion7(), "q", "p", [Samples.Team1], legal, allowPass, false, null, null);
    static readonly Guid A = Guid.CreateVersion7(), B = Guid.CreateVersion7(), C = Guid.CreateVersion7();

    [Fact] public void Options_by_id() { var ask = With(new Options([new AskOption("x", "X", [])])); Assert.True(LegalSetMembership.Contains(ask, new Chosen("x"))); Assert.False(LegalSetMembership.Contains(ask, new Chosen("y"))); }
    [Fact] public void Pieces_within_bounds_and_eligible()
    {
        var ask = With(new PieceSelection([A, B], 1, 1));
        Assert.True(LegalSetMembership.Contains(ask, new SelectedPieces([A])));
        Assert.False(LegalSetMembership.Contains(ask, new SelectedPieces([A, B])));
        Assert.False(LegalSetMembership.Contains(ask, new SelectedPieces([C])));
        Assert.False(LegalSetMembership.Contains(ask, new SelectedPieces([])));
    }
    [Fact] public void Allocation_sums_to_amount_over_targets()
    {
        var ask = With(new Allocation([A, B], 3));
        Assert.True(LegalSetMembership.Contains(ask, new Allocated(new Dictionary<Guid, int> { [A] = 2, [B] = 1 })));
        Assert.False(LegalSetMembership.Contains(ask, new Allocated(new Dictionary<Guid, int> { [A] = 3, [B] = 1 })));
        Assert.False(LegalSetMembership.Contains(ask, new Allocated(new Dictionary<Guid, int> { [C] = 3 })));
    }
    [Fact] public void Assignment_covers_every_item_with_at_most_n_distinct_targets()
    {
        var ask = With(new Assignment([new AskOption("g1", "", []), new AskOption("g2", "", [])], [A, B, C], 2));
        Assert.True(LegalSetMembership.Contains(ask, new Assigned(new Dictionary<string, Guid> { ["g1"] = A, ["g2"] = B })));
        Assert.False(LegalSetMembership.Contains(ask, new Assigned(new Dictionary<string, Guid> { ["g1"] = A })));
        Assert.False(LegalSetMembership.Contains(ask, new Assigned(new Dictionary<string, Guid> { ["g1"] = A, ["g2"] = Guid.CreateVersion7() })));
        var one = With(new Assignment([new AskOption("g1", "", []), new AskOption("g2", "", [])], [A, B], 1));
        Assert.False(LegalSetMembership.Contains(one, new Assigned(new Dictionary<string, Guid> { ["g1"] = A, ["g2"] = B })));
    }
    [Fact] public void Move_region_checks_model_ids_only()
    {
        var ask = With(new MoveRegion([Samples.Model], 6, [], [], null));
        Assert.True(LegalSetMembership.Contains(ask, new Destinations([Samples.Deploy])));
        Assert.False(LegalSetMembership.Contains(ask, new Destinations([Samples.Deploy with { ModelId = C }])));
    }
    [Fact] public void Token_spend_by_key_and_per_key_cap_with_no_total_confirmation_and_pass()
    {
        var spend = With(new TokenSpend(new Dictionary<Guid, int> { [A] = 2, [B] = 3 }));
        Assert.True(LegalSetMembership.Contains(spend, new TokensSpent(new Dictionary<Guid, int> { [A] = 2 })));
        Assert.False(LegalSetMembership.Contains(spend, new TokensSpent(new Dictionary<Guid, int> { [C] = 1 })));      // C is not a spender
        Assert.False(LegalSetMembership.Contains(spend, new TokensSpent(new Dictionary<Guid, int> { [A] = 3 })));      // over A's own cap
        Assert.False(LegalSetMembership.Contains(spend, new TokensSpent(new Dictionary<Guid, int> { [A] = -1 })));
        Assert.True(LegalSetMembership.Contains(spend, new TokensSpent(new Dictionary<Guid, int> { [A] = 2, [B] = 3 })));  // five, over either cap: each entry is within its own and there is no total to violate
        Assert.True(LegalSetMembership.Contains(With(new Confirmation()), new Confirmed()));
        Assert.False(LegalSetMembership.Contains(With(new Confirmation()), new Chosen("x")));
        Assert.True(LegalSetMembership.Contains(With(new TokenSpend(new Dictionary<Guid, int> { [A] = 2 }), allowPass: true), new Passed()));
        Assert.False(LegalSetMembership.Contains(With(new TokenSpend(new Dictionary<Guid, int> { [A] = 2 })), new Passed()));
    }
}

public class EnforcementPolicyTests
{
    [Theory]
    [InlineData(Enforcement.Silent, Legality.OutsideLegalSet, true, false)]
    [InlineData(Enforcement.Warn, Legality.OutsideLegalSet, true, true)]
    [InlineData(Enforcement.Strict, Legality.OutsideLegalSet, false, false)]
    [InlineData(Enforcement.Strict, Legality.Illegal, false, false)]
    [InlineData(Enforcement.Warn, Legality.Illegal, true, true)]
    [InlineData(Enforcement.Silent, Legality.Illegal, true, false)]
    [InlineData(Enforcement.Strict, Legality.Legal, true, false)]
    [InlineData(Enforcement.Strict, Legality.AlwaysAccepted, true, false)]
    [InlineData(Enforcement.Warn, Legality.AlwaysAccepted, true, false)]
    public void Three_positions(Enforcement enforcement, Legality legality, bool accepted, bool warned)
    {
        var verdict = EnforcementPolicy.Decide(enforcement, legality, "unit already activated");
        Assert.Equal(accepted, verdict is Accept);
        Assert.Equal(warned, verdict is Accept { Warning: not null });
        if (verdict is Refuse r) Assert.Equal(legality == Legality.Illegal ? RefusalReason.Illegal : RefusalReason.OutsideLegalSet, r.Reason);
    }
}
```

```csharp
using Tabletop.GameServer.Dispatch;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.GameServer.Tests;

public class EventFilterTests
{
    static readonly Guid Sealed = Guid.CreateVersion7(), Open = Guid.CreateVersion7();
    /// <summary>Two model ids out of Samples, so the spend maps are concrete: one key per eligible spender, capped at its own tokens.</summary>
    static readonly Guid M1 = Samples.Model, M2 = Samples.Container;
    static readonly TokensSpent SpentByTeam1 = new(new Dictionary<Guid, int> { [M1] = 2 }), SpentByTeam2 = new(new Dictionary<Guid, int> { [M2] = 1 });
    static GameEvent Ev(long seq, EventPayload p) => new(seq, null, p, null);
    static readonly GameEvent[] Log =
    [
        Ev(1, new AskOpened(new Ask(Sealed, AskQuestions.SpendTokens, "Spend tokens", [Samples.Team1, Samples.Team2], new TokenSpend(new Dictionary<Guid, int> { [M1] = 2, [M2] = 1 }), true, true, "casting", Samples.Frame))),
        Ev(2, new AskOpened(new Ask(Open, AskQuestions.Activate, "Activate", [Samples.Team1], new Options([]), true, false, null, null))),
        Ev(3, new AskAnswered(Sealed, Samples.Team1, SpentByTeam1, false)),
        Ev(4, new AskAnswered(Open, Samples.Team1, new Passed(), false)),
        Ev(5, new AskAnswered(Sealed, Samples.Team2, SpentByTeam2, false)),
        Ev(6, new AskClosed(Sealed, [new TeamAnswer(Samples.Team1, SpentByTeam1), new TeamAnswer(Samples.Team2, SpentByTeam2)])),
    ];

    [Fact]
    public void A_sealed_answer_is_withheld_from_other_teams_while_the_window_is_open()
    {
        var forTeam2 = EventFilter.ForViewer(Log[..5], Samples.Team2, new HashSet<Guid>());
        Assert.Null(((AskAnswered)forTeam2[2].Payload).Answer);        // team 1's spend, hidden from team 2
        Assert.NotNull(((AskAnswered)forTeam2[3].Payload).Answer);     // the unsealed ask is never withheld
        Assert.NotNull(((AskAnswered)forTeam2[4].Payload).Answer);     // team 2's own spend
        var forTeam1 = EventFilter.ForViewer(Log[..5], Samples.Team1, new HashSet<Guid>());
        Assert.NotNull(((AskAnswered)forTeam1[2].Payload).Answer);
        Assert.Null(((AskAnswered)forTeam1[4].Payload).Answer);
        var spectator = EventFilter.ForViewer(Log[..5], null, new HashSet<Guid>());
        Assert.Null(((AskAnswered)spectator[2].Payload).Answer);
        Assert.Null(((AskAnswered)spectator[4].Payload).Answer);
    }

    [Fact]
    public void A_replayed_log_withholds_in_place_and_reveals_through_ask_closed()
    {
        var replayed = EventFilter.ForViewer(Log, Samples.Team2, new HashSet<Guid>());
        Assert.Null(((AskAnswered)replayed[2].Payload).Answer);                 // still hidden where it happened
        Assert.Equal(2, ((AskClosed)replayed[5].Payload).Answers.Count);      // revealed where the window closed
        Assert.Equal(WireJson.Serialize(Log[5]), WireJson.Serialize(replayed[5]));
    }

    [Fact]
    public void A_live_batch_uses_the_asks_open_before_it()
    {
        var batch = new[] { Ev(7, new AskAnswered(Sealed, Samples.Team1, SpentByTeam1, false)) };
        Assert.Null(((AskAnswered)EventFilter.ForViewer(batch, Samples.Team2, new HashSet<Guid> { Sealed })[0].Payload).Answer);
        Assert.NotNull(((AskAnswered)EventFilter.ForViewer(batch, Samples.Team2, new HashSet<Guid>())[0].Payload).Answer);
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `LegalSetMembership.cs`**

```csharp
using Tabletop.Protocol;

namespace Tabletop.GameServer.Dispatch;

/// <summary>Set membership over the closed LegalSet shapes. Rules-free: the set was computed by the engine; this only reads it.</summary>
public static class LegalSetMembership
{
    public static bool Contains(Ask ask, AskAnswer answer) => answer switch
    {
        Passed => ask.AllowPass,
        Chosen c => ask.Legal is Options o && o.Choices.Any(x => x.Id == c.OptionId),
        SelectedPieces s => ask.Legal is PieceSelection p && s.PieceIds.Count >= p.Min && s.PieceIds.Count <= p.Max
                            && s.PieceIds.Distinct().Count() == s.PieceIds.Count && s.PieceIds.All(p.Eligible.Contains),
        Allocated a => ask.Legal is Allocation al && a.Counts.Keys.All(al.Targets.Contains) && a.Counts.Values.All(n => n >= 0) && a.Counts.Values.Sum() == al.Amount,
        Assigned g => ask.Legal is Assignment asg && asg.Items.All(i => g.Targets.ContainsKey(i.Id)) && g.Targets.Keys.All(k => asg.Items.Any(i => i.Id == k))
                      && g.Targets.Values.All(asg.Targets.Contains) && g.Targets.Values.Distinct().Count() <= asg.MaxDistinctTargets,
        Destinations d => ask.Legal is MoveRegion m && d.Moves.All(x => m.ModelIds.Contains(x.ModelId)),
        // Keys and per-key caps, and no total: a spend is any number up to a maximum (p.13). Whether the answering
        // team owns a key is the spend step's, as MoveRegion's distance is the move step's — no state answers it here.
        TokensSpent t => ask.Legal is TokenSpend ts && t.Counts.All(kv => ts.MaxByModel.TryGetValue(kv.Key, out var max) && kv.Value >= 0 && kv.Value <= max),
        Confirmed => ask.Legal is Confirmation,
        _ => false,
    };
}
```

- [ ] **Step 3: Write `EnforcementPolicy.cs`**

```csharp
using Tabletop.Protocol;

namespace Tabletop.GameServer.Dispatch;

/// <summary>AlwaysAccepted: the override path (menu, hand moves, markers, undo). Legal / OutsideLegalSet: an Ask answer against its set. Illegal: the engine's word for a command the rules cannot execute.</summary>
public enum Legality { AlwaysAccepted, Legal, OutsideLegalSet, Illegal }

public abstract record Verdict;
public sealed record Accept(string? Warning) : Verdict;
public sealed record Refuse(RefusalReason Reason, string Detail) : Verdict;

/// <summary>silent | warn | strict as a decision (architecture.md → Rules enforcement; whole-design review ruling 1).</summary>
public static class EnforcementPolicy
{
    public static Verdict Decide(Enforcement enforcement, Legality legality, string detail) => (legality, enforcement) switch
    {
        (Legality.AlwaysAccepted or Legality.Legal, _) => new Accept(null),
        (Legality.OutsideLegalSet, Enforcement.Strict) => new Refuse(RefusalReason.OutsideLegalSet, detail),
        (Legality.Illegal, Enforcement.Strict) => new Refuse(RefusalReason.Illegal, detail),
        (_, Enforcement.Warn) => new Accept(detail),
        _ => new Accept(null),
    };
}
```

- [ ] **Step 4: Write `EventFilter.cs`**

```csharp
using Tabletop.Protocol;

namespace Tabletop.GameServer.Dispatch;

/// <summary>The one per-viewer stream: a sealed window's answers are withheld from other teams until ask_closed reveals them.</summary>
public static class EventFilter
{
    public static IReadOnlySet<Guid> SealedOpenAsks(Projection p) => p.OpenAsks.Values.Where(a => a.Ask.Sealed).Select(a => a.Ask.Id).ToHashSet();

    public static IReadOnlyList<GameEvent> ForViewer(IReadOnlyList<GameEvent> events, Guid? viewerTeamId, IReadOnlySet<Guid> sealedOpenBefore)
    {
        var sealedOpen = new HashSet<Guid>(sealedOpenBefore);
        var result = new List<GameEvent>(events.Count);
        foreach (var e in events)
        {
            switch (e.Payload)
            {
                case AskOpened o when o.Ask.Sealed: sealedOpen.Add(o.Ask.Id); result.Add(e); break;
                case AskClosed c: sealedOpen.Remove(c.AskId); result.Add(e); break;
                case AskAnswered a when sealedOpen.Contains(a.AskId) && a.TeamId != viewerTeamId:
                    result.Add(e with { Payload = a with { Answer = null } }); break;
                default: result.Add(e); break;
            }
        }
        return result;
    }
}
```

- [ ] **Step 5: Run `dotnet test tests/Tabletop.GameServer.Tests`; expect 45 passed. Commit: `Judge Ask answers against their legal set and apply the enforcement setting`.**

### Task 6: The rules seam — `IGameRules`, `IRulesEngine`, `InertEngine` and the command dispatcher

**Files:**
- Create: `server/game/Rules/IGameRules.cs`, `server/game/Rules/GrimdarkFuture/GfRules.cs`, `server/game/Rules/IRulesEngine.cs`, `server/game/Rules/InertEngine.cs`, `server/game/Dispatch/MenuTranslation.cs`, `server/game/Dispatch/CommandDispatcher.cs`
- Test: `tests/Tabletop.GameServer.Tests/Infrastructure/FixedAskEngine.cs`, `tests/Tabletop.GameServer.Tests/CommandDispatcherTests.cs`

**Interfaces:**
- Consumes: Task 5's `Legality`, `Verdict`, `Accept`, `Refuse`, `EnforcementPolicy`, `LegalSetMembership`; Task 4's `Replay.IsValidUndoTarget`; every `Command`, `EventPayload`, `StateChange`, `Ask`, `Projection`, `EventApplier`, `Board`, `GameInfo`, `RefusalReason` (Protocol).
- Produces (namespace `Tabletop.GameServer.Rules`): `interface IGameRules { string Id { get; } }`; `sealed partial class GrimdarkFutureRules : IGameRules` (namespace `Tabletop.GameServer.Rules.GrimdarkFuture`, file `Rules/GrimdarkFuture/GfRules.cs`; `partial` from day one so the scheduler and interpreter plans add members in sibling files without touching this one) with `const string RulesId = "grimdark-future"`, `static GrimdarkFutureRules Instance`; `static class GameRulesRegistry { static IGameRules Resolve(string id); }` (throws `UnknownGameRulesException(string id)`); `sealed record Actor(Guid ParticipantId, Guid? TeamId)`; `sealed class GameState(GameInfo info, Board board, EventApplier applier, IGameRules rules)` with `GameInfo Info { get; set; }`, `Board Board`, `EventApplier Applier`, `Projection Projection => Applier.Projection`, `IGameRules Rules`; `abstract record EngineResult` with `Emit(IReadOnlyList<EventPayload> Events)` and `RefuseCommand(RefusalReason Reason, string Detail)`; `interface IRulesEngine { Legality Judge(GameState state, Command command, Actor actor); EngineResult Execute(GameState state, Command command, Actor actor); IReadOnlyList<EventPayload> Advance(GameState state); }`; `interface IRulesEngineFactory { IRulesEngine Create(IGameRules rules, GameInfo game); }`; `sealed class InertEngine : IRulesEngine`, `sealed class InertEngineFactory : IRulesEngineFactory`.
- Produces (namespace `Tabletop.GameServer.Dispatch`): `enum CommandClass { Override, Undo, Abandon, Answer, Engine }`; `abstract record DispatchOutcome` with `Applied(IReadOnlyList<EventPayload> Events, string? Warning)` and `Refused(RefusalReason Reason, string Detail)`; `sealed class CommandDispatcher(IRulesEngine engine)` with `static CommandClass Classify(Command command)`, `DispatchOutcome Dispatch(GameState state, Command command, Actor actor)`, `IReadOnlyList<Command> EnumerateLegalCommands(GameState state, Guid? teamId)`; `static class MenuTranslation { static EventPayload? Translate(GameState state, Command command, out string? missing); }`.
- **The seam, stated for the scheduler and interpreter plans.** `IGameRules` is the hook surface — add members to it; `GrimdarkFutureRules` implements them. `IRulesEngine` is the generic engine — the scheduler and the interpreter together are its one real implementation, registered through `IRulesEngineFactory` in place of `InertEngineFactory`. The dispatcher calls `Judge` then `Execute` for `CommandClass.Engine` commands (`RerollDice`, `SetDice`, `AddModifier` and anything a later Protocol adds), and after **every** appended batch the session calls `Advance` until it returns an empty list — that is where the scheduler opens the next turn's Ask and the interpreter completes steps or reacts to an `ask_closed`. The dispatcher itself owns: authorization (the answering team must be addressed), the Ask-answer events (`ask_answered`, `ask_closed`, the sealed collection), the menu, hand moves, markers, undo and abandon. `GameState.Projection` is the state both read; the interpreter plan adds the RNG seed to `GameState` as a member of its own.

Spec: `architecture.md` → Command / Event spine, Rules enforcement (enumeration; a modified client gets what an honest one gets), Client and server responsibilities (authorization is a session concern, not a rules one); game flow → Nothing is an invariant (menu and automation write the same fields through the same path); scheduler → Nothing is an invariant (`SetActiveTeam` logs `turn_started`, `SetRound` logs `round_started`), Moving models by hand is not an answer; action sequences → Abandonment (pop the frame and everything above; any participant; logged with an actor), Asks (any team member answers), Windows (sealed collection); contracts plan → obligations (`ask_closed` before `sequence_abandoned`; refuse `Undo` unless live).

- [ ] **Step 1: Write `IGameRules.cs` and `GrimdarkFuture/GfRules.cs`**

```csharp
using Tabletop.GameServer.Rules.GrimdarkFuture;

namespace Tabletop.GameServer.Rules;

/// <summary>
/// The per-ruleset C# behind an interface, selected by game.game_rules. This plan gives it an identity and nothing else;
/// the scheduler plan adds its hooks (first team, activatable units, phases, round-boundary hooks) and the interpreter plan
/// adds its (sequences, rolls' conventions, thresholds, caps). Extend; do not replace. The merged interface is listed in
/// full under Interfaces → Produces.
/// </summary>
public interface IGameRules
{
    string Id { get; }
}

public sealed class UnknownGameRulesException(string id) : Exception($"No GameRules named '{id}' ships with this build.");

public static class GameRulesRegistry
{
    public static IGameRules Resolve(string id) => id == GrimdarkFutureRules.RulesId ? GrimdarkFutureRules.Instance : throw new UnknownGameRulesException(id);
}
```

```csharp
namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>
/// Only OPR's GameRules is built (architecture.md → Rules as data). Partial: identity here; the scheduler plan's members in
/// GfRules.Scheduling.cs and GfRules.Deployment.cs, the interpreter plan's in GfRules.Sequences.cs. RulesId, Instance and Id
/// are declared once, here.
/// </summary>
public sealed partial class GrimdarkFutureRules : IGameRules
{
    public const string RulesId = "grimdark-future";
    public static GrimdarkFutureRules Instance { get; } = new();
    public string Id => RulesId;
}
```

Tests that name `GrimdarkFutureRules` (`CommandDispatcherTests`, `GameSessionTests`) add `using Tabletop.GameServer.Rules.GrimdarkFuture;`.

- [ ] **Step 2: Write `IRulesEngine.cs` and `InertEngine.cs`**

```csharp
using Tabletop.GameServer.Dispatch;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules;

/// <summary>Who sent a command: the participant, and the team they are on (null for a watcher).</summary>
public sealed record Actor(Guid ParticipantId, Guid? TeamId);

/// <summary>What a game is, in memory, from playing on: configuration, the initial state, and the applier holding the projection and the log.</summary>
public sealed class GameState(GameInfo info, Board board, EventApplier applier, IGameRules rules)
{
    public GameInfo Info { get; set; } = info;
    public Board Board { get; } = board;
    public EventApplier Applier { get; } = applier;
    public Projection Projection => Applier.Projection;
    public IGameRules Rules { get; } = rules;
}

public abstract record EngineResult;
/// <summary>Events to append, in order. Empty is a valid answer (the command changed nothing).</summary>
public sealed record Emit(IReadOnlyList<EventPayload> Events) : EngineResult;
/// <summary>Not a legality refusal — the command cannot be carried out (no such frame, no interpreter). Independent of enforcement.</summary>
public sealed record RefuseCommand(RefusalReason Reason, string Detail) : EngineResult;

/// <summary>
/// The generic engine the scheduler and interpreter plans implement. Judge says whether a command is inside the rules;
/// Execute turns an accepted command into events; Advance runs the engine forward with no input and is called after every
/// appended batch until it returns nothing. The engine never appends: the session does, and it never assumes its last output
/// is still true (scheduler → Nothing is an invariant) — it reads state.Projection every call.
/// </summary>
public interface IRulesEngine
{
    Legality Judge(GameState state, Command command, Actor actor);
    EngineResult Execute(GameState state, Command command, Actor actor);
    IReadOnlyList<EventPayload> Advance(GameState state);
}

public interface IRulesEngineFactory
{
    IRulesEngine Create(IGameRules rules, GameInfo game);
}
```

```csharp
using Tabletop.GameServer.Dispatch;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules;

/// <summary>The skeleton's engine: no scheduler, no interpreter. Nothing is judged illegal, nothing can be executed, nothing advances.</summary>
public sealed class InertEngine : IRulesEngine
{
    public Legality Judge(GameState state, Command command, Actor actor) => Legality.Legal;
    public EngineResult Execute(GameState state, Command command, Actor actor) =>
        new RefuseCommand(RefusalReason.Unknown, $"{NameOf(command)} needs the interpreter, which this build does not have.");
    public IReadOnlyList<EventPayload> Advance(GameState state) => [];
    static string NameOf(Command c) => c.GetType().Name;   // not Protocol's EventKinds.Of, which is keyed on EventPayload
}

public sealed class InertEngineFactory : IRulesEngineFactory
{
    public IRulesEngine Create(IGameRules rules, GameInfo game) => new InertEngine();
}
```

- [ ] **Step 3: Write the failing dispatcher tests and `FixedAskEngine`**

```csharp
// tests/Tabletop.GameServer.Tests/Infrastructure/FixedAskEngine.cs
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests.Infrastructure;

/// <summary>Opens one given Ask the first time Advance runs, and never again (it checks the log, not a flag, so it survives hydration).</summary>
public sealed class FixedAskEngine(Ask ask) : IRulesEngine
{
    public Legality Judge(GameState state, Command command, Actor actor) => Legality.Legal;
    public EngineResult Execute(GameState state, Command command, Actor actor) => new RefuseCommand(RefusalReason.Unknown, "test engine");
    public IReadOnlyList<EventPayload> Advance(GameState state) =>
        state.Applier.Log.Any(e => e.Payload is AskOpened o && o.Ask.Id == ask.Id) ? [] : [new AskOpened(ask)];
}

public sealed class FixedAskEngineFactory(Ask ask) : IRulesEngineFactory
{
    public IRulesEngine Create(IGameRules rules, GameInfo game) => new FixedAskEngine(ask);
}
```

```csharp
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using Refused = Tabletop.GameServer.Dispatch.Refused;

namespace Tabletop.GameServer.Tests;

public class CommandDispatcherTests
{
    static readonly Actor Clay = new(Samples.P1, Samples.Team1), Opponent = new(Guid.CreateVersion7(), Samples.Team2), Watcher = new(Guid.CreateVersion7(), null);
    static GameState State(Enforcement e = Enforcement.Warn, params EventPayload[] history)
    {
        var info = Samples.Game with { Enforcement = e };
        var board = Samples.Board;   // a property that builds a fresh instance per access: take one and use it twice
        var applier = new EventApplier(board, info);
        applier.ApplyAll(history.Select((p, i) => new GameEvent(i + 1, null, p, null)));
        return new GameState(info, board, applier, GrimdarkFutureRules.Instance);
    }
    static CommandDispatcher Dispatcher() => new(new InertEngine());
    static readonly Ask Activate = new(Samples.AskId, AskQuestions.Activate, "Activate", [Samples.Team1], new Options([new AskOption("a", "Advance", [Samples.Unit])]), true, false, null, null);
    /// <summary>Two model ids out of Samples, so the spend map is concrete: one key per eligible spender, capped at its own tokens.</summary>
    static readonly Guid M1 = Samples.Model, M2 = Samples.Container;
    static readonly TokensSpent SpentByTeam1 = new(new Dictionary<Guid, int> { [M1] = 2 }), SpentByTeam2 = new(new Dictionary<Guid, int> { [M2] = 1 });
    static readonly Ask SimultaneousSpend = new(Guid.CreateVersion7(), AskQuestions.SpendTokens, "Spend tokens", [Samples.Team1, Samples.Team2], new TokenSpend(new Dictionary<Guid, int> { [M1] = 2, [M2] = 1 }), true, true, "casting", Samples.Frame);

    public static IEnumerable<object[]> Overrides() =>
        new Command[] { new MoveModels([Samples.Deploy]), new SetWounds(Samples.Model, 2), new SetCondition(new Subject(SubjectKind.Unit, Samples.Unit), "shaken", true),
            new RecordAction(Samples.Unit, "Advance"), new ClearActionRecord(Samples.Unit), new AddGrant(Samples.Unit, Samples.Grant), new RemoveGrant(Samples.Unit, Samples.Grant.Id),
            new SetSpellTokens(Samples.Model, 3), new SetWoundsThisActivation(Samples.Unit, 1), new SetScore(Samples.Team1, 5), new SetActiveTeam(Samples.Team2), new SetRound(2),
            new PlaceMarker(Samples.Token) }
        .SelectMany(c => new[] { Enforcement.Silent, Enforcement.Warn, Enforcement.Strict }.Select(e => new object[] { c, e }));

    [Theory, MemberData(nameof(Overrides))]
    public void Menu_edits_and_hand_moves_are_applied_under_every_setting_with_no_warning(Command command, Enforcement enforcement)
    {
        var outcome = Dispatcher().Dispatch(State(enforcement), command, Watcher);
        var applied = Assert.IsType<Applied>(outcome);
        Assert.Single(applied.Events); Assert.Null(applied.Warning);
        var expectedKind = command switch { MoveModels => typeof(ModelsMoved), SetActiveTeam => typeof(TurnStarted), SetRound => typeof(RoundStarted), PlaceMarker => typeof(MarkerPlaced), _ => null };
        if (expectedKind is not null) Assert.IsType(expectedKind, applied.Events[0]);
        else Assert.IsAssignableFrom<StateChange>(applied.Events[0]);
    }

    [Fact]
    public void A_reference_to_nothing_is_refused_as_unknown_regardless_of_setting()
    {
        Assert.Equal(RefusalReason.Unknown, Assert.IsType<Refused>(Dispatcher().Dispatch(State(Enforcement.Silent), new SetWounds(Guid.CreateVersion7(), 1), Clay)).Reason);
        Assert.Equal(RefusalReason.Unknown, Assert.IsType<Refused>(Dispatcher().Dispatch(State(), new MoveMarker(Guid.CreateVersion7(), new(0, 0, 0), new(0)), Clay)).Reason);
        Assert.Equal(RefusalReason.Unknown, Assert.IsType<Refused>(Dispatcher().Dispatch(State(), new AbandonSequence(Guid.CreateVersion7()), Clay)).Reason);
    }

    [Fact]
    public void Undo_is_accepted_for_zero_or_a_live_seq_only()
    {
        var state = State(Enforcement.Strict, new ScoreSet(Samples.Team1, 1), new ScoreSet(Samples.Team1, 2), new Undone(1));
        Assert.IsType<Undone>(Assert.IsType<Applied>(Dispatcher().Dispatch(state, new Undo(1), Watcher)).Events[0]);
        Assert.IsType<Applied>(Dispatcher().Dispatch(state, new Undo(0), Watcher));
        Assert.Equal(RefusalReason.Unknown, Assert.IsType<Refused>(Dispatcher().Dispatch(state, new Undo(2), Watcher)).Reason);
        Assert.Equal(RefusalReason.Unknown, Assert.IsType<Refused>(Dispatcher().Dispatch(state, new Undo(9), Watcher)).Reason);
    }

    [Theory]
    [InlineData(Enforcement.Strict, false, false)] [InlineData(Enforcement.Warn, true, true)] [InlineData(Enforcement.Silent, true, false)]
    public void An_out_of_set_answer_follows_the_enforcement_setting(Enforcement enforcement, bool applied, bool warned)
    {
        var outcome = Dispatcher().Dispatch(State(enforcement, new AskOpened(Activate)), new AnswerAsk(Activate.Id, new Chosen("zzz")), Clay);
        if (!applied) { Assert.Equal(RefusalReason.OutsideLegalSet, Assert.IsType<Refused>(outcome).Reason); return; }
        var ok = Assert.IsType<Applied>(outcome);
        Assert.Equal(warned, ok.Warning is not null);
        Assert.True(Assert.IsType<AskAnswered>(ok.Events[0]).OutsideLegalSet);
        Assert.IsType<AskClosed>(ok.Events[1]);
    }

    [Fact]
    public void An_in_set_answer_answers_and_closes_an_unsealed_ask()
    {
        var ok = Assert.IsType<Applied>(Dispatcher().Dispatch(State(Enforcement.Strict, new AskOpened(Activate)), new AnswerAsk(Activate.Id, new Chosen("a")), Clay));
        Assert.Equal([typeof(AskAnswered), typeof(AskClosed)], ok.Events.Select(e => e.GetType()));
        Assert.Equal(Samples.Team1, ((AskClosed)ok.Events[1]).Answers.Single().TeamId);
        Assert.IsType<Applied>(Dispatcher().Dispatch(State(Enforcement.Strict, new AskOpened(Activate)), new AnswerAsk(Activate.Id, new Passed()), Clay));
    }

    [Fact]
    public void Only_an_addressed_team_may_answer_and_an_unknown_ask_is_unknown()
    {
        var state = State(Enforcement.Silent, new AskOpened(Activate));
        Assert.Equal(RefusalReason.NotPermitted, Assert.IsType<Refused>(Dispatcher().Dispatch(state, new AnswerAsk(Activate.Id, new Chosen("a")), Opponent)).Reason);
        Assert.Equal(RefusalReason.NotPermitted, Assert.IsType<Refused>(Dispatcher().Dispatch(state, new AnswerAsk(Activate.Id, new Chosen("a")), Watcher)).Reason);
        Assert.Equal(RefusalReason.Unknown, Assert.IsType<Refused>(Dispatcher().Dispatch(state, new AnswerAsk(Guid.CreateVersion7(), new Chosen("a")), Clay)).Reason);
    }

    [Fact]
    public void A_sealed_ask_collects_every_team_before_it_closes()
    {
        var state = State(Enforcement.Strict, new AskOpened(SimultaneousSpend));
        var first = Assert.IsType<Applied>(Dispatcher().Dispatch(state, new AnswerAsk(SimultaneousSpend.Id, SpentByTeam1), Clay));
        Assert.Single(first.Events); Assert.IsType<AskAnswered>(first.Events[0]);
        state.Applier.Apply(new GameEvent(2, Clay.ParticipantId, first.Events[0], null));
        Assert.Equal(RefusalReason.NotPermitted, Assert.IsType<Refused>(Dispatcher().Dispatch(state, new AnswerAsk(SimultaneousSpend.Id, SpentByTeam2), Clay)).Reason);
        var second = Assert.IsType<Applied>(Dispatcher().Dispatch(state, new AnswerAsk(SimultaneousSpend.Id, SpentByTeam2), Opponent));
        var closed = Assert.IsType<AskClosed>(second.Events[1]);
        Assert.Equal([Samples.Team1, Samples.Team2], closed.Answers.Select(a => a.TeamId));
        Assert.Equal([2, 1], closed.Answers.Select(a => ((TokensSpent)a.Answer).Counts.Values.Single()));
    }

    [Fact]
    public void Abandoning_closes_the_popped_frames_asks_first()
    {
        var inner = Guid.CreateVersion7();
        var innerAsk = SimultaneousSpend with { Id = Guid.CreateVersion7(), FrameId = inner };
        var state = State(Enforcement.Strict,
            new SequenceStarted(Samples.Frame, "attack-sequence", new SequenceContext(Samples.Unit, [], [], null)),
            new SequenceStarted(inner, "resolve-hits", new SequenceContext(Samples.Unit, [], [], null)),
            new AskOpened(innerAsk), new AskAnswered(innerAsk.Id, Samples.Team1, SpentByTeam1, false));
        var ok = Assert.IsType<Applied>(Dispatcher().Dispatch(state, new AbandonSequence(Samples.Frame), Watcher));
        Assert.Equal([typeof(AskClosed), typeof(SequenceAbandoned)], ok.Events.Select(e => e.GetType()));
        Assert.Single(((AskClosed)ok.Events[0]).Answers);
    }

    [Fact]
    public void Legal_commands_are_enumerated_from_the_open_asks()
    {
        var state = State(Enforcement.Warn, new AskOpened(Activate));
        var legal = Dispatcher().EnumerateLegalCommands(state, Samples.Team1);
        Assert.Equal(2, legal.Count);
        Assert.Contains(legal, c => c is AnswerAsk { Answer: Chosen { OptionId: "a" } });
        Assert.Contains(legal, c => c is AnswerAsk { Answer: Passed });
        Assert.Empty(Dispatcher().EnumerateLegalCommands(state, Samples.Team2));
        Assert.Empty(Dispatcher().EnumerateLegalCommands(state, null));
    }

    [Fact]
    public void Engine_commands_go_to_the_engine_and_the_inert_engine_refuses_them()
    {
        Assert.Equal(CommandClass.Engine, CommandDispatcher.Classify(new RerollDice(Samples.Frame, Samples.Roll, [])));
        Assert.Equal(RefusalReason.Unknown, Assert.IsType<Refused>(Dispatcher().Dispatch(State(Enforcement.Silent), new RerollDice(Samples.Frame, Samples.Roll, []), Clay)).Reason);
    }
}
```

- [ ] **Step 4: Run; expect compile errors. Write `MenuTranslation.cs`**

```csharp
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Dispatch;

/// <summary>
/// The override path: each menu or hand-move command becomes the one event that states the change in full. Nothing here is a
/// rule — a dead model may be put back on the table and a unit may be marked moved twice. Only references are checked: the
/// event must name something the projection has, or applying it would throw.
/// </summary>
public static class MenuTranslation
{
    public static EventPayload? Translate(GameState state, Command command, out string? missing)
    {
        var p = state.Projection;
        string? miss = null;   // a local function may not capture an out parameter (CS8175), so it captures this and copies out at the end
        bool HasModel(Guid id) => p.Models.ContainsKey(id) || Missing(ref miss, $"no model {id}");
        bool HasUnit(Guid id) => p.Units.ContainsKey(id) || Missing(ref miss, $"no unit {id}");
        bool HasTeam(Guid id) => state.Info.Teams.Any(t => t.Id == id) || Missing(ref miss, $"no team {id}");
        bool HasMarker(Guid id) => p.Markers.ContainsKey(id) || Missing(ref miss, $"no marker {id}");
        bool SubjectExists(Subject s) => s.Kind == SubjectKind.Unit ? HasUnit(s.Id) : HasModel(s.Id);

        EventPayload? payload = command switch
        {
            MoveModels m when m.Moves.All(x => HasModel(x.ModelId)) => new ModelsMoved(m.Moves),
            PlaceMarker m when !p.Markers.ContainsKey(m.Marker.Id) || Missing(ref miss, $"marker {m.Marker.Id} already placed") => new MarkerPlaced(m.Marker),
            MoveMarker m when HasMarker(m.MarkerId) => new MarkerMoved(m.MarkerId, m.Position, m.Yaw),
            RemoveMarker m when HasMarker(m.MarkerId) => new MarkerRemoved(m.MarkerId),
            SetWounds w when HasModel(w.ModelId) => new WoundsSet(w.ModelId, w.Wounds),
            SetCondition c when SubjectExists(c.Subject) => new ConditionSet(c.Subject, c.Condition, c.Present),
            RecordAction a when HasUnit(a.UnitId) => new ActionRecorded(a.UnitId, a.Action),
            ClearActionRecord a when HasUnit(a.UnitId) => new ActionRecordCleared(a.UnitId),
            AddGrant g when HasUnit(g.UnitId) => new GrantAdded(g.UnitId, g.Grant),
            RemoveGrant g when HasUnit(g.UnitId) => new GrantRemoved(g.UnitId, g.GrantId),
            SetSpellTokens s when HasModel(s.ModelId) => new SpellTokensSet(s.ModelId, s.Tokens),
            SetWoundsThisActivation w when HasUnit(w.UnitId) => new WoundsThisActivationSet(w.UnitId, w.Wounds),
            SetScore s when HasTeam(s.TeamId) => new ScoreSet(s.TeamId, s.Score),
            SetActiveTeam t when HasTeam(t.TeamId) => new TurnStarted(t.TeamId),
            SetRound r => new RoundStarted(r.Round, p.Scheduler.Order, []),
            _ => null,
        };
        missing = miss;
        return payload;
    }

    static bool Missing(ref string? missing, string detail) { missing ??= detail; return false; }
}
```

- [ ] **Step 5: Write `CommandDispatcher.cs`**

```csharp
using Tabletop.GameServer.Log;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Dispatch;

public enum CommandClass { Override, Undo, Abandon, Answer, Engine }

public abstract record DispatchOutcome;
public sealed record Applied(IReadOnlyList<EventPayload> Events, string? Warning) : DispatchOutcome;
public sealed record Refused(RefusalReason Reason, string Detail) : DispatchOutcome;

/// <summary>Turns a command into the events it produces, or a refusal. Never appends and never broadcasts; the session does both.</summary>
public sealed class CommandDispatcher(IRulesEngine engine)
{
    public static CommandClass Classify(Command command) => command switch
    {
        Undo => CommandClass.Undo,
        AbandonSequence => CommandClass.Abandon,
        AnswerAsk => CommandClass.Answer,
        MoveModels or PlaceMarker or MoveMarker or RemoveMarker or SetWounds or SetCondition or RecordAction or ClearActionRecord
            or AddGrant or RemoveGrant or SetSpellTokens or SetWoundsThisActivation or SetScore or SetActiveTeam or SetRound => CommandClass.Override,
        _ => CommandClass.Engine,
    };

    public DispatchOutcome Dispatch(GameState state, Command command, Actor actor) => Classify(command) switch
    {
        CommandClass.Override => MenuTranslation.Translate(state, command, out var missing) is { } e ? new Applied([e], null) : new Refused(RefusalReason.Unknown, missing ?? "unknown command"),
        CommandClass.Undo => Replay.IsValidUndoTarget(state.Applier, ((Undo)command).ToSeq)
            ? new Applied([new Undone(((Undo)command).ToSeq)], null) : new Refused(RefusalReason.Unknown, $"seq {((Undo)command).ToSeq} is not a live event"),
        CommandClass.Abandon => Abandon(state, ((AbandonSequence)command).FrameId),
        CommandClass.Answer => Answer(state, (AnswerAsk)command, actor),
        _ => Engine(state, command, actor),
    };

    private static DispatchOutcome Abandon(GameState state, Guid frameId)
    {
        var stack = state.Projection.Stack;
        var index = stack.ToList().FindIndex(f => f.Id == frameId);
        if (index < 0) return new Refused(RefusalReason.Unknown, $"no frame {frameId} on the stack");
        var popped = stack.Skip(index).Select(f => f.Id).ToHashSet();
        var events = new List<EventPayload>();
        foreach (var open in state.Projection.OpenAsks.Values.Where(a => a.Ask.FrameId is { } f && popped.Contains(f)))
            events.Add(new AskClosed(open.Ask.Id, open.Answers.Where(kv => kv.Value is not null).Select(kv => new TeamAnswer(kv.Key, kv.Value!)).ToList()));
        events.Add(new SequenceAbandoned(frameId));
        return new Applied(events, null);
    }

    private static DispatchOutcome Answer(GameState state, AnswerAsk command, Actor actor)
    {
        if (!state.Projection.OpenAsks.TryGetValue(command.AskId, out var open)) return new Refused(RefusalReason.Unknown, $"no open ask {command.AskId}");
        var ask = open.Ask;
        if (actor.TeamId is not { } team || !ask.Teams.Contains(team)) return new Refused(RefusalReason.NotPermitted, "this ask is not addressed to your team");
        if (open.Answers.ContainsKey(team)) return new Refused(RefusalReason.NotPermitted, "your team has already answered");
        var inSet = LegalSetMembership.Contains(ask, command.Answer);
        var verdict = EnforcementPolicy.Decide(state.Info.Enforcement, inSet ? Legality.Legal : Legality.OutsideLegalSet, $"answer to '{ask.Question}' is outside the legal set");
        if (verdict is Refuse r) return new Refused(r.Reason, r.Detail);
        var events = new List<EventPayload> { new AskAnswered(ask.Id, team, command.Answer, !inSet) };
        var answered = new Dictionary<Guid, AskAnswer>(open.Answers.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value!)) { [team] = command.Answer };
        if (!ask.Sealed || ask.Teams.All(answered.ContainsKey))
            events.Add(new AskClosed(ask.Id, ask.Teams.Where(answered.ContainsKey).Select(t => new TeamAnswer(t, answered[t])).ToList()));
        return new Applied(events, ((Accept)verdict).Warning);
    }

    private DispatchOutcome Engine(GameState state, Command command, Actor actor)
    {
        var verdict = EnforcementPolicy.Decide(state.Info.Enforcement, engine.Judge(state, command, actor), $"{command.GetType().Name} is illegal here");
        if (verdict is Refuse r) return new Refused(r.Reason, r.Detail);
        return engine.Execute(state, command, actor) switch
        {
            Emit e => new Applied(e.Events, ((Accept)verdict).Warning),
            RefuseCommand rc => new Refused(rc.Reason, rc.Detail),
            var other => throw new NotSupportedException(other.GetType().Name),
        };
    }

    /// <summary>The discrete part of legality, for a team: every option of every open Ask addressed to it, plus the pass where allowed. Continuous legal sets (regions, allocations) are parameters the client renders and are not enumerated here.</summary>
    public IReadOnlyList<Command> EnumerateLegalCommands(GameState state, Guid? teamId)
    {
        if (teamId is not { } team) return [];
        var result = new List<Command>();
        foreach (var open in state.Projection.OpenAsks.Values.Where(a => a.Ask.Teams.Contains(team) && !a.Answers.ContainsKey(team)))
        {
            if (open.Ask.Legal is Options o) result.AddRange(o.Choices.Select(c => new AnswerAsk(open.Ask.Id, new Chosen(c.Id))));
            if (open.Ask.Legal is Confirmation) result.Add(new AnswerAsk(open.Ask.Id, new Confirmed()));
            if (open.Ask.AllowPass) result.Add(new AnswerAsk(open.Ask.Id, new Passed()));
        }
        return result;
    }
}
```

- [ ] **Step 6: Register `builder.Services.AddSingleton<IRulesEngineFactory, InertEngineFactory>();`. Run `dotnet test tests/Tabletop.GameServer.Tests`; expect 95 passed (40 theory rows + 11). Commit: `Add the rules seam and the command dispatcher over it`.**

### Task 7: The game session — the actor, the log path, presence, hydration and the registry

**Files:**
- Create: `server/game/Session/ISessionClient.cs`, `server/game/Session/PresenceTable.cs`, `server/game/Session/SessionServices.cs`, `server/game/Session/GameSession.cs`, `server/game/Session/GameSession.Frames.cs`, `server/game/Session/Hydrator.cs`, `server/game/Session/SessionRegistry.cs`
- Test: `tests/Tabletop.GameServer.Tests/Infrastructure/RecordingClient.cs`, `tests/Tabletop.GameServer.Tests/GameSessionTests.cs`

**Interfaces:**
- Consumes: Tasks 3–6 (`IGameRows`, `IEventLog`, `DuplicateSequenceException`, `GameState`, `Actor`, `IRulesEngine`, `IRulesEngineFactory`, `CommandDispatcher`, `Applied`, `Refused`, `EventFilter`, `GameRulesRegistry`); `SocketMessage` kinds, `AtRest`, `GameEvent`, `ProjectionDesyncException`, `Presence`, `Ping`, `GameStatus` (Protocol); `TableState` (Content).
- Produces: `interface ISessionClient { Guid ConnectionId { get; } Guid ParticipantId { get; } ValueTask SendAsync(SocketMessage message); ValueTask CloseAsync(System.Net.WebSockets.WebSocketCloseStatus status, string reason); }`; `sealed record SessionServices(IGameRows Rows, IEventLog Log, IRulesEngineFactory Engines, GameServerOptions Options, ILoggerFactory Logging)`; `sealed class PresenceTable` with `void Set(Presence value)`, `void Remove(Guid participantId)`, `IReadOnlyList<Presence> TakeDirty()`, `IReadOnlyList<Presence> All`; `sealed class GameSession` with `Guid GameId`, `GameInfo Info`, `IGameRules Rules`, `TableState? Draft`, `GameState? Play`, `IReadOnlyList<ISessionClient> Clients`, `bool Faulted`, `Task StartAsync()`, `Task AttachAsync(ISessionClient)`, `Task DetachAsync(ISessionClient)`, `Task ReceiveAsync(ISessionClient from, SocketMessage frame)`, `Task TickPresenceAsync()`, `Task<T> RunAsync<T>(Func<Task<T>> work)` (posts to the mailbox and awaits), `Task DrainAsync(string reason)`, `Guid? TeamOf(Guid participantId)`, `Actor ActorOf(ISessionClient)`; `static class Hydrator { static Task<GameSession> HydrateAsync(Guid gameId, SessionServices services, CancellationToken ct); }` (throws `GameNotFoundException`); `sealed class SessionRegistry(SessionServices services)` with `Task<GameSession> GetOrHydrateAsync(Guid gameId, CancellationToken ct)`, `Task ReleaseIfEmptyAsync(GameSession session)`, `IReadOnlyCollection<GameSession> Live`, `Task DrainAllAsync(string reason)`, `bool Draining`.
- **The dice seed arrives by hydration and needs no code of its own.** `Hydrator.HydrateAsync` already takes the whole game row through `IGameRows.ReadGameAsync`, so `GameInfo.RngSeed` is populated before the session exists, and the `new GameState(info, board, applier, rules)` in the playing branch carries it in. Nothing here reads the seed or writes it: the value's *use* — `GameState.RngSeed` becoming the stored seed instead of a hash of `Info.Id` — is the **interpreter plan's** change to `GameState`, not this plan's, and it lands with the dice (interpreter plan Task 5). `GameSession.ReloadInfoAsync` (Task 10) and the end-setup re-read (Task 12) both re-read the row, and both get the same unchanged seed back, because nobody ever updates the column.
- Internal to the session and used by Tasks 10–12: `Task AppendAsync(IReadOnlyList<EventPayload> events, Guid? actor, string? warning)` (assigns seq, appends to the log **first**, applies, broadcasts per viewer), `Task AdvanceAsync()` (the `IRulesEngine.Advance` loop, capped at 10 000 iterations), `Task BroadcastAsync(SocketMessage message, ISessionClient? except = null)`, `AtRest Rest()` (the rest frame for the log as it stands).
- **The rest frame, owner ruling 4 of 2026-09-06.** `AtRest(long Seq)` says "nothing further is coming from me right now, and the log ends at `Seq`". `Seq` is always the log's high-water mark, `Play?.Projection.LastSeq ?? 0`, so it is `0` before the log opens and unchanged when the batch appended nothing. The frame follows its batch: it is broadcast when the batch appended at least one event, and sent to the provoking client alone when it appended none, so a refusal still disturbs nobody but its sender. There are three call sites and no others: the end of `HandleCommandAsync` whatever the outcome (Step 3), the end of `HandleEndSetupAsync` after `AdvanceAsync` (Task 12), and the end of `ConnectAsync` at every status (Task 8). Lobby and setup edits do not emit it, because they touch no log, `Seq` would name nothing, and the `GameInfoMessage` or `BoardMessage` carrying the whole settled value is itself the terminator. This is what replaces the simulation plan's 250 ms quiet window.
- **A desync applying the server's own event faults the session, owner ruling 2 of 2026-09-06.** `EventApplier.ApplyAll` throws `ProjectionDesyncException` when an event names an id the projection does not hold. On the log path that event is already durable, so there is nothing to resync from: `FaultAsync` closes every socket with `1011`, and the next hydrate replays the same event and throws again out of `Hydrator`, where `ConnectionHandshake` closes with `1011` (Task 8). A client meeting the same exception drops its projection and rebuilds from the log, which is the client plan's; the two sides differ because a client's projection is one copy of a log it can re-read, while the server's is the only copy and the log it would rebuild from is the log that faulted.
- **Every other exception on the log path is caught as well.** `AppendAsync` today catches `DuplicateSequenceException` and nothing else, so any other database failure propagates out of the mailbox item into `ConnectionHandshake`'s read loop, which does not handle it: the socket dies without a `Refused` and the session keeps running. `AppendAsync` now catches every exception from `IEventLog.AppendAsync` and faults, because a batch whose outcome is unknown makes the next `seq` unknown. Task 8's read loop gains a last-resort catch for anything a handler throws off the log path.

Spec: `architecture.md` → Two servers (live games in memory; the only module holding live state), Contracts to freeze (nobody instantiates a session: the first socket hydrates it; starting, reconnecting and crash recovery are one path), Deployment (a continuous relay at a fixed rate; reconnect rehydrates from the log); setup phase → The draft is the row (one game lives on one task; the server's memory is the one authority; the row write is durability, after the broadcast — for *events* the order is reversed by `architecture.md` → Persistence: the log is the authority, so an event is appended before it is broadcast); presence → Nothing here is written down, Connectedness is not a field, A ping is silent (one-shot); client shell → The connection and the projection; accounts → Ending is a flag (an ended game is served like any other — commands are accepted at `playing` and `ended`, because *"any participant can pick an ended game back up"* and picking one up is *"the identical operation"* to recovering a crashed task; identity and enumerations → `ended` is reversible agrees).

- [ ] **Step 1: Write `ISessionClient.cs`, `SessionServices.cs`, `PresenceTable.cs`**

```csharp
using System.Net.WebSockets;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Session;

/// <summary>One connected socket as the session sees it. SocketConnection (Task 8) is the real one; tests record.</summary>
public interface ISessionClient
{
    Guid ConnectionId { get; }
    Guid ParticipantId { get; }
    ValueTask SendAsync(SocketMessage message);
    ValueTask CloseAsync(WebSocketCloseStatus status, string reason);
}
```

```csharp
using Tabletop.GameServer.Configuration;
using Tabletop.GameServer.Log;
using Tabletop.GameServer.Persistence;
using Tabletop.GameServer.Rules;

namespace Tabletop.GameServer.Session;

public sealed record SessionServices(IGameRows Rows, IEventLog Log, IRulesEngineFactory Engines, GameServerOptions Options, ILoggerFactory Logging);
```

```csharp
using Tabletop.Protocol;

namespace Tabletop.GameServer.Session;

/// <summary>Latest value per connected participant; existence is connectedness. Relayed on the tick, never stored anywhere else.</summary>
public sealed class PresenceTable
{
    private readonly Dictionary<Guid, Presence> _values = new();
    private readonly HashSet<Guid> _dirty = new();

    public void Set(Presence value) { _values[value.ParticipantId] = value; _dirty.Add(value.ParticipantId); }
    public void Remove(Guid participantId) { _values.Remove(participantId); _dirty.Remove(participantId); }
    public IReadOnlyList<Presence> All => _values.Values.ToList();

    /// <summary>Values changed since the last tick, then clears the set. Latest wins: intermediate samples are never relayed.</summary>
    public IReadOnlyList<Presence> TakeDirty()
    {
        var result = _dirty.Select(id => _values[id]).ToList();
        _dirty.Clear();
        return result;
    }
}
```

- [ ] **Step 2: Write `GameSession.cs` — the actor and the log path**

```csharp
using System.Net.WebSockets;
using System.Threading.Channels;
using Tabletop.Content;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Log;
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Session;

/// <summary>
/// One game in memory. Every mutation runs on the mailbox loop, so there is no lock: connections post frames, the loop handles
/// them one at a time. The log is appended before anything is broadcast; the row is written after the broadcast (setup only).
/// </summary>
public sealed partial class GameSession
{
    private readonly Channel<Func<Task>> _mailbox = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });
    private readonly List<ISessionClient> _clients = new();
    private readonly PresenceTable _presence = new();
    private readonly SessionServices _services;
    private readonly ILogger _log;
    private Task? _loop;

    public GameSession(Guid gameId, GameInfo info, IGameRules rules, TableState? draft, GameState? play, IRulesEngine? engine, SessionServices services)
    {
        GameId = gameId; Info = info; Rules = rules; Draft = draft; Play = play; Engine = engine; _services = services;
        _log = services.Logging.CreateLogger<GameSession>();
        Dispatcher = engine is null ? null : new CommandDispatcher(engine);
    }

    public Guid GameId { get; }
    public GameInfo Info { get; internal set; }
    public IGameRules Rules { get; }
    public TableState? Draft { get; internal set; }
    public GameState? Play { get; internal set; }
    public IRulesEngine? Engine { get; internal set; }
    public CommandDispatcher? Dispatcher { get; internal set; }
    public IReadOnlyList<ISessionClient> Clients => _clients;
    public bool Faulted { get; private set; }

    /// <summary>Starts the loop, then catches the engine up (Advance) if the game is playing — crash recovery between append and advance.</summary>
    public Task StartAsync()
    {
        _loop ??= Task.Run(async () => { await foreach (var work in _mailbox.Reader.ReadAllAsync()) await work(); });
        return RunAsync(async () => { if (Play is not null) await AdvanceAsync(); return 0; });
    }

    public Task<T> RunAsync<T>(Func<Task<T>> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_mailbox.Writer.TryWrite(async () => { try { tcs.SetResult(await work()); } catch (Exception ex) { tcs.SetException(ex); } }))
            tcs.SetException(new InvalidOperationException("session is drained"));
        return tcs.Task;
    }
    private Task RunAsync(Func<Task> work) => RunAsync(async () => { await work(); return 0; });

    public Guid? TeamOf(Guid participantId) => Info.Participants.FirstOrDefault(p => p.Id == participantId)?.TeamId;
    public Actor ActorOf(ISessionClient client) => new(client.ParticipantId, TeamOf(client.ParticipantId));

    public Task AttachAsync(ISessionClient client) => RunAsync(() => AttachCoreAsync(client));

    /// <summary>One connection per participant: a newer socket replaces the older one. The newcomer gets everyone else's current presence.</summary>
    private async Task AttachCoreAsync(ISessionClient client)
    {
        var replaced = _clients.FirstOrDefault(c => c.ParticipantId == client.ParticipantId);
        if (replaced is not null) { _clients.Remove(replaced); await replaced.CloseAsync(WebSocketCloseStatus.NormalClosure, "replaced by a newer connection"); }
        _clients.Add(client);
        foreach (var value in _presence.All.Where(v => v.ParticipantId != client.ParticipantId)) await client.SendAsync(new PresenceMessage(value));
    }

    public Task DetachAsync(ISessionClient client) => RunAsync(async () =>
    {
        if (!_clients.Remove(client)) return;
        if (_clients.All(c => c.ParticipantId != client.ParticipantId)) { _presence.Remove(client.ParticipantId); await BroadcastAsync(new PresenceEnded(client.ParticipantId)); }
    });

    public async Task BroadcastAsync(SocketMessage message, ISessionClient? except = null)
    {
        foreach (var c in _clients.ToList()) if (c != except) await c.SendAsync(message);
    }

    /// <summary>The log path: seq assigned here, appended first, applied, then broadcast with each viewer's sealed answers withheld.</summary>
    internal async Task AppendAsync(IReadOnlyList<EventPayload> payloads, Guid? actor, string? warning)
    {
        if (payloads.Count == 0 || Play is null) return;
        var sealedBefore = EventFilter.SealedOpenAsks(Play.Projection);
        var next = Play.Projection.LastSeq;
        var batch = payloads.Select((p, i) => new GameEvent(next + i + 1, actor, p, i == 0 ? warning : null)).ToList();
        try { await _services.Log.AppendAsync(GameId, batch, CancellationToken.None); }
        catch (DuplicateSequenceException ex) { await FaultAsync($"another writer holds this game (seq {ex.Seq})"); return; }
        catch (Exception ex) { await FaultAsync($"the append at seq {batch[0].Seq} failed: {ex.Message}"); return; }
        // The batch is durable and the projection cannot take it: the server's own event names an id nothing created (owner
        // ruling 2). Faulting is the whole response. A resync would replay the same event from the same log and fault again.
        try { Play.Applier.ApplyAll(batch); }
        catch (ProjectionDesyncException ex) { await FaultAsync($"this server's own event does not apply to its own projection: {ex.Message}"); return; }
        foreach (var c in _clients.ToList()) await c.SendAsync(new EventsMessage(EventFilter.ForViewer(batch, TeamOf(c.ParticipantId), sealedBefore)));
    }

    /// <summary>The rest frame for the log as it stands: nothing further is coming right now, and the log ends at Seq (0 before it opens).</summary>
    internal AtRest Rest() => new(Play?.Projection.LastSeq ?? 0);

    /// <summary>Runs the engine until it has nothing to say. Every iteration reads the projection afresh.</summary>
    internal async Task AdvanceAsync()
    {
        if (Play is null || Engine is null || Faulted) return;
        for (var i = 0; i < 10_000; i++)
        {
            var events = Engine.Advance(Play);
            if (events.Count == 0) return;
            await AppendAsync(events, null, null);
            if (Faulted) return;
        }
        _log.LogError("Game {GameId}: the engine advanced 10000 times without resting; stopping.", GameId);
    }

    private async Task FaultAsync(string reason)
    {
        Faulted = true;
        _log.LogError("Game {GameId} faulted: {Reason}", GameId, reason);
        foreach (var c in _clients.ToList()) await c.CloseAsync(WebSocketCloseStatus.InternalServerError, reason);
        _clients.Clear();
    }

    /// <summary>Finishes queued work, closes every socket with 1001, and stops the loop. Everything durable was already written when it was queued.</summary>
    public async Task DrainAsync(string reason)
    {
        await RunAsync(async () => { foreach (var c in _clients.ToList()) await c.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, reason); _clients.Clear(); });
        _mailbox.Writer.TryComplete();
        if (_loop is not null) await _loop;
    }
}
```

- [ ] **Step 3: Write `GameSession.Frames.cs` — commands, presence, pings; edits are Tasks 10–11**

```csharp
using Tabletop.GameServer.Dispatch;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Session;

public sealed partial class GameSession
{
    /// <summary>Every inbound frame after Connect lands here, on the mailbox.</summary>
    public Task ReceiveAsync(ISessionClient from, SocketMessage frame) => RunAsync(() => frame switch
    {
        CommandMessage c => HandleCommandAsync(from, c),
        PresenceMessage p => HandlePresence(from, p.Presence),
        PingMessage p => BroadcastAsync(new PingMessage(p.Ping with { Sender = from.ParticipantId }), except: from),
        EditMessage e => HandleEditAsync(from, e),
        _ => from.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.ProtocolError, $"{frame.GetType().Name} is not a client frame").AsTask(),
    });

    /// <summary>Every command ends at rest. The frame follows its batch: broadcast when the batch appended something, to the sender alone when it did not.</summary>
    private async Task HandleCommandAsync(ISessionClient from, CommandMessage message)
    {
        var before = Play?.Projection.LastSeq ?? 0;
        if (Play is null || Dispatcher is null || Info.Status is not (GameStatus.Playing or GameStatus.Ended))
            await from.SendAsync(new Protocol.Refused(message.CommandId, RefusalReason.WrongStatus, $"commands are not accepted while the game is {Info.Status}"));
        else
            switch (Dispatcher.Dispatch(Play, message.Command, ActorOf(from)))
            {
                case Dispatch.Refused r: await from.SendAsync(new Protocol.Refused(message.CommandId, r.Reason, r.Detail)); break;
                case Applied a: await AppendAsync(a.Events, from.ParticipantId, a.Warning); await AdvanceAsync(); break;
            }
        var rest = Rest();
        if (rest.Seq > before) await BroadcastAsync(rest); else await from.SendAsync(rest);
    }

    /// <summary>Presence is state: the latest value wins and the tick relays it. The server stamps the participant id; a client cannot speak for another.</summary>
    private Task HandlePresence(ISessionClient from, Presence value)
    {
        _presence.Set(value with { ParticipantId = from.ParticipantId });
        return Task.CompletedTask;
    }

    /// <summary>Called at PresenceRelayHz by SessionHost. Relays each changed value to everyone but its owner.</summary>
    public Task TickPresenceAsync() => RunAsync(async () =>
    {
        foreach (var value in _presence.TakeDirty())
            foreach (var c in _clients.ToList()) if (c.ParticipantId != value.ParticipantId) await c.SendAsync(new PresenceMessage(value));
    });

    // Task 10 (lobby edits) and Task 11 (setup edits) replace this body.
    private Task HandleEditAsync(ISessionClient from, EditMessage message) =>
        from.SendAsync(new Protocol.Refused(message.EditId, RefusalReason.WrongStatus, "edits are not handled yet")).AsTask();
}
```

`Refused` exists in both `Tabletop.Protocol` (the frame) and `Tabletop.GameServer.Dispatch` (the outcome); the `Protocol.` qualifier picks the frame. If the ambiguity bites elsewhere, alias `using ProtocolRefused = Tabletop.Protocol.Refused;`.

- [ ] **Step 4: Write `Hydrator.cs` and `SessionRegistry.cs`**

```csharp
using Tabletop.GameServer.Rules;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Session;

public sealed class GameNotFoundException(Guid gameId) : Exception($"No game {gameId}.");
public sealed class GameRowsInconsistentException(Guid gameId, string what) : Exception($"Game {gameId}: {what}.");

/// <summary>The one path: rows, then the draft or the snapshot, then the log. A first join, a rejoin, a reconnect and a crashed task all come through here.</summary>
public static class Hydrator
{
    public static async Task<GameSession> HydrateAsync(Guid gameId, SessionServices services, CancellationToken ct)
    {
        var info = await services.Rows.ReadGameAsync(gameId, ct) ?? throw new GameNotFoundException(gameId);
        var rules = GameRulesRegistry.Resolve(info.GameRules);
        if (info.Status == GameStatus.Lobby) return new GameSession(gameId, info, rules, null, null, null, services);

        var draft = await services.Rows.ReadDraftAsync(gameId, ct);
        // Ended is a flag, not a terminal state, and the last participant leaving ends a game that may never have left the
        // lobby (accounts -> Ending is a flag). That game has no table_state row and no log, so it hydrates boardless.
        if (draft is null)
        {
            if (info.Status == GameStatus.Ended) return new GameSession(gameId, info, rules, null, null, null, services);
            throw new GameRowsInconsistentException(gameId, $"status is {info.Status} but there is no table_state row");
        }
        if (info.Status == GameStatus.Setup) return new GameSession(gameId, info, rules, draft, null, null, services);

        var board = new Board(draft, await services.Rows.ReadSnapshotsAsync(gameId, ct));
        var applier = new EventApplier(board, info);
        applier.ApplyAll(await services.Log.ReadAsync(gameId, 0, ct));
        var engine = services.Engines.Create(rules, info);
        return new GameSession(gameId, info, rules, null, new GameState(info, board, applier, rules), engine, services);
    }
}
```

```csharp
namespace Tabletop.GameServer.Session;

/// <summary>Which games are in memory. Get-or-hydrate is serialised per process so two sockets arriving together hydrate once.</summary>
public sealed class SessionRegistry(SessionServices services)
{
    private readonly Dictionary<Guid, GameSession> _live = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool Draining { get; private set; }
    public IReadOnlyCollection<GameSession> Live { get { lock (_live) return _live.Values.ToList(); } }

    public async Task<GameSession> GetOrHydrateAsync(Guid gameId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (Draining) throw new InvalidOperationException("shutting down");
            if (_live.TryGetValue(gameId, out var existing) && !existing.Faulted) return existing;
            var session = await Hydrator.HydrateAsync(gameId, services, ct);
            await session.StartAsync();
            lock (_live) _live[gameId] = session;
            return session;
        }
        finally { _gate.Release(); }
    }

    /// <summary>When the last socket goes, the game leaves memory; the next socket hydrates it again. Nothing is lost: the log and the rows hold everything.</summary>
    public async Task ReleaseIfEmptyAsync(GameSession session)
    {
        await _gate.WaitAsync();
        try
        {
            if (session.Clients.Count > 0 && !session.Faulted) return;
            lock (_live) if (_live.TryGetValue(session.GameId, out var s) && s == session) _live.Remove(session.GameId);
            await session.DrainAsync("released");
        }
        finally { _gate.Release(); }
    }

    public async Task DrainAllAsync(string reason)
    {
        await _gate.WaitAsync();
        try
        {
            Draining = true;
            foreach (var s in Live) await s.DrainAsync(reason);
            lock (_live) _live.Clear();
        }
        finally { _gate.Release(); }
    }
}
```

- [ ] **Step 5: Write `RecordingClient.cs` and the failing session tests**

```csharp
using System.Net.WebSockets;
using Tabletop.GameServer.Session;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests.Infrastructure;

public sealed class RecordingClient(Guid participantId) : ISessionClient
{
    public Guid ConnectionId { get; } = Guid.CreateVersion7();
    public Guid ParticipantId { get; } = participantId;
    public List<SocketMessage> Received { get; } = new();
    public (WebSocketCloseStatus Status, string Reason)? Closed { get; private set; }
    public ValueTask SendAsync(SocketMessage message) { Received.Add(message); return ValueTask.CompletedTask; }
    public ValueTask CloseAsync(WebSocketCloseStatus status, string reason) { Closed = (status, reason); return ValueTask.CompletedTask; }
    public IEnumerable<GameEvent> Events => Received.OfType<EventsMessage>().SelectMany(m => m.Events);
}
```

```csharp
using System.Net.WebSockets;
using Microsoft.Extensions.Logging.Abstractions;
using Tabletop.GameServer.Configuration;
using Tabletop.GameServer.Log;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Session;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.GameServer.Tests;

public class GameSessionTests
{
    static readonly Guid P2 = Guid.CreateVersion7(), Watcher = Guid.CreateVersion7();
    static readonly GameInfo Info = Samples.Game with { Participants =
        [new Participant(Samples.P1, Guid.CreateVersion7(), "Clay", Samples.Team1, null), new Participant(P2, Guid.CreateVersion7(), "Opponent", Samples.Team2, null), new Participant(Watcher, Guid.CreateVersion7(), "Watcher", null, null)] };
    /// <summary>Two model ids out of Samples, so the spend map is concrete: one key per eligible spender, capped at its own tokens.</summary>
    static readonly Guid M1 = Samples.Model, M2 = Samples.Container;
    static readonly TokensSpent SpentByTeam1 = new(new Dictionary<Guid, int> { [M1] = 2 }), SpentByTeam2 = new(new Dictionary<Guid, int> { [M2] = 1 });
    static readonly Ask SimultaneousSpend = new(Guid.CreateVersion7(), AskQuestions.SpendTokens, "Spend tokens", [Samples.Team1, Samples.Team2], new TokenSpend(new Dictionary<Guid, int> { [M1] = 2, [M2] = 1 }), true, true, "casting", null);
    static readonly GameServerOptions Options = new() { DbConnection = "", StytchProjectId = "p", StytchJwksUrl = "u" };

    // Rows are never touched on the playing path, so the session tests pass none; Tasks 10-12 test the row paths against Postgres.
    static SessionServices Services(IEventLog log, IRulesEngineFactory engines) => new(null!, log, engines, Options, NullLoggerFactory.Instance);

    static async Task<(GameSession Session, InMemoryEventLog Log, RecordingClient Clay, RecordingClient Opponent, RecordingClient Watcher)> PlayingAsync(IRulesEngineFactory? engines = null, GameStatus status = GameStatus.Playing)
    {
        var log = new InMemoryEventLog();
        var info = Info with { Status = status };
        var services = Services(log, engines ?? new InertEngineFactory());
        var session = await Hydrate(info, log, services);
        var (a, b, w) = (new RecordingClient(Samples.P1), new RecordingClient(P2), new RecordingClient(Watcher));
        await session.AttachAsync(a); await session.AttachAsync(b); await session.AttachAsync(w);
        return (session, log, a, b, w);
    }

    static async Task<GameSession> Hydrate(GameInfo info, IEventLog log, SessionServices services)
    {
        GameSession session;
        if (info.Status == GameStatus.Lobby) session = new GameSession(info.Id, info, GrimdarkFutureRules.Instance, null, null, null, services);
        else
        {
            var board = Samples.Board;   // one instance, shared by the applier and the state (the sample is a property)
            var applier = new EventApplier(board, info);
            applier.ApplyAll(await log.ReadAsync(info.Id, 0, CancellationToken.None));
            session = new GameSession(info.Id, info, GrimdarkFutureRules.Instance, null, new GameState(info, board, applier, GrimdarkFutureRules.Instance), services.Engines.Create(GrimdarkFutureRules.Instance, info), services);
        }
        await session.StartAsync();
        return session;
    }

    [Fact]
    public async Task A_command_is_logged_then_applied_then_broadcast_to_everyone()
    {
        var (session, log, clay, opponent, watcher) = await PlayingAsync();
        await session.ReceiveAsync(clay, new CommandMessage(Guid.CreateVersion7(), new SetScore(Samples.Team1, 4)));
        var logged = await log.ReadAsync(session.GameId, 0, CancellationToken.None);
        Assert.Equal([1L], logged.Select(e => e.Seq));
        Assert.Equal(Samples.P1, logged[0].Actor);
        Assert.Equal(4, session.Play!.Projection.Scores[Samples.Team1]);
        foreach (var c in new[] { clay, opponent, watcher }) Assert.Equal(1, c.Events.Single().Seq);
    }

    [Fact]
    public async Task A_refusal_goes_only_to_the_sender_and_logs_nothing()
    {
        var (session, log, clay, opponent, _) = await PlayingAsync();
        var id = Guid.CreateVersion7();
        await session.ReceiveAsync(clay, new CommandMessage(id, new SetWounds(Guid.CreateVersion7(), 1)));
        Assert.Equal(id, Assert.IsType<Protocol.Refused>(clay.Received[0]).MessageId);
        Assert.Equal(0, Assert.IsType<AtRest>(clay.Received[1]).Seq);   // nothing appended: the sender alone, at the unchanged high-water mark
        Assert.Empty(opponent.Received);
        Assert.Empty(await log.ReadAsync(session.GameId, 0, CancellationToken.None));
    }

    [Fact]
    public async Task Commands_are_refused_with_wrong_status_before_playing()
    {
        var log = new InMemoryEventLog();
        var session = await Hydrate(Info with { Status = GameStatus.Lobby }, log, Services(log, new InertEngineFactory()));
        var clay = new RecordingClient(Samples.P1);
        await session.AttachAsync(clay);
        await session.ReceiveAsync(clay, new CommandMessage(Guid.CreateVersion7(), new SetScore(Samples.Team1, 1)));
        Assert.Equal(RefusalReason.WrongStatus, Assert.IsType<Protocol.Refused>(clay.Received[0]).Reason);
        Assert.Equal(0, Assert.IsType<AtRest>(clay.Received[1]).Seq);   // no log at lobby, so the rest frame says 0
    }

    [Fact]
    public async Task A_command_that_appends_ends_at_rest_after_its_events_for_everyone()
    {
        var (session, _, clay, opponent, watcher) = await PlayingAsync();
        await session.ReceiveAsync(clay, new CommandMessage(Guid.CreateVersion7(), new SetScore(Samples.Team1, 4)));
        foreach (var c in new[] { clay, opponent, watcher })
        {
            Assert.Equal([typeof(EventsMessage), typeof(AtRest)], c.Received.Select(m => m.GetType()));   // the events, then rest, never the other way round
            Assert.Equal(1L, c.Received.OfType<AtRest>().Single().Seq);
        }
    }

    [Fact]
    public async Task An_event_the_servers_own_projection_cannot_apply_faults_the_session()
    {
        var (session, log, clay, opponent, _) = await PlayingAsync(new DesyncingEngineFactory());
        await session.ReceiveAsync(clay, new CommandMessage(Guid.CreateVersion7(), new SetScore(Samples.Team1, 4)));
        Assert.True(session.Faulted);
        Assert.Equal(WebSocketCloseStatus.InternalServerError, opponent.Closed!.Value.Status);
        Assert.Empty(session.Clients);
        Assert.Equal([1L, 2L], (await log.ReadAsync(session.GameId, 0, CancellationToken.None)).Select(e => e.Seq));   // the bad event is durable; the next hydrate faults too
    }

    [Fact]
    public async Task Starting_advances_the_engine_and_a_sealed_answer_is_withheld_per_viewer()
    {
        var (session, log, clay, opponent, watcher) = await PlayingAsync(new FixedAskEngineFactory(SimultaneousSpend));
        Assert.IsType<AskOpened>((await log.ReadAsync(session.GameId, 0, CancellationToken.None)).Single().Payload);
        await session.ReceiveAsync(clay, new CommandMessage(Guid.CreateVersion7(), new AnswerAsk(SimultaneousSpend.Id, SpentByTeam1)));
        Assert.NotNull(clay.Events.Select(e => e.Payload).OfType<AskAnswered>().Single().Answer);
        Assert.Null(opponent.Events.Select(e => e.Payload).OfType<AskAnswered>().Single().Answer);
        Assert.Null(watcher.Events.Select(e => e.Payload).OfType<AskAnswered>().Single().Answer);
        await session.ReceiveAsync(opponent, new CommandMessage(Guid.CreateVersion7(), new AnswerAsk(SimultaneousSpend.Id, SpentByTeam2)));
        Assert.Equal(2, watcher.Events.Select(e => e.Payload).OfType<AskClosed>().Single().Answers.Count);
    }

    [Fact]
    public async Task Presence_is_relayed_on_the_tick_pings_at_once_and_ends_with_the_socket()
    {
        var (session, _, clay, opponent, watcher) = await PlayingAsync();
        var value = new Presence(Guid.CreateVersion7(), new Cursor(new(1, 2, 3), null), null, [], null);
        await session.ReceiveAsync(clay, new PresenceMessage(value));
        await session.ReceiveAsync(clay, new PresenceMessage(value with { Cursor = new Cursor(new(4, 5, 6), null) }));
        Assert.Empty(opponent.Received.OfType<PresenceMessage>());
        await session.TickPresenceAsync();
        var relayed = Assert.Single(opponent.Received.OfType<PresenceMessage>()).Presence;
        Assert.Equal((Samples.P1, 4.0), (relayed.ParticipantId, relayed.Cursor!.Point.X));   // stamped with the sender, latest wins
        Assert.Empty(clay.Received.OfType<PresenceMessage>());
        await session.ReceiveAsync(clay, new PingMessage(new Ping(Guid.Empty, new(0, 0, 0), null)));
        Assert.Equal(Samples.P1, Assert.Single(watcher.Received.OfType<PingMessage>()).Ping.Sender);
        var late = new RecordingClient(Guid.CreateVersion7());
        await session.AttachAsync(late);
        Assert.Equal(Samples.P1, Assert.Single(late.Received.OfType<PresenceMessage>()).Presence.ParticipantId);
        await session.DetachAsync(clay);
        Assert.Equal(Samples.P1, Assert.Single(opponent.Received.OfType<PresenceEnded>()).ParticipantId);
    }

    [Fact]
    public async Task A_second_connection_for_the_same_participant_replaces_the_first()
    {
        var (session, _, clay, _, _) = await PlayingAsync();
        var again = new RecordingClient(Samples.P1);
        await session.AttachAsync(again);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, clay.Closed!.Value.Status);
        Assert.Equal(3, session.Clients.Count);
    }

    [Fact]
    public async Task Losing_the_seq_race_faults_the_session_and_closes_every_socket()
    {
        var (session, log, clay, opponent, _) = await PlayingAsync();
        await log.AppendAsync(session.GameId, [new GameEvent(1, null, new PhaseStarted("elsewhere"), null)], CancellationToken.None);
        await session.ReceiveAsync(clay, new CommandMessage(Guid.CreateVersion7(), new SetScore(Samples.Team1, 4)));
        Assert.True(session.Faulted);
        Assert.Equal(WebSocketCloseStatus.InternalServerError, opponent.Closed!.Value.Status);
        Assert.Empty(session.Clients);
    }
}

/// <summary>Emits one event naming a model nothing created, as soon as the log holds anything: the applier's desync, provoked from the server's own side.</summary>
public sealed class DesyncingEngine : IRulesEngine
{
    public Tabletop.GameServer.Dispatch.Legality Judge(GameState state, Command command, Actor actor) => Tabletop.GameServer.Dispatch.Legality.Legal;
    public EngineResult Execute(GameState state, Command command, Actor actor) => new RefuseCommand(RefusalReason.Unknown, "test engine");
    public IReadOnlyList<EventPayload> Advance(GameState state) => state.Applier.Log.Count == 1 ? [new WoundsSet(Guid.CreateVersion7(), 1)] : [];
}

public sealed class DesyncingEngineFactory : IRulesEngineFactory
{
    public IRulesEngine Create(IGameRules rules, GameInfo game) => new DesyncingEngine();
}
```

- [ ] **Step 6: Register in `Program.cs`**: `builder.Services.AddSingleton<SessionServices>(); builder.Services.AddSingleton<SessionRegistry>();` (`SessionServices` is a record with a constructor DI can satisfy). Run `dotnet test tests/Tabletop.GameServer.Tests`; expect 104 passed. Commit: `Add the game session: the mailbox, the log path, presence and hydration`.

### Task 8: The socket — connection, handshake, the connect sequence, and the end-to-end test client

**Files:**
- Create: `server/game/Session/SocketConnection.cs`, `server/game/Session/ConnectionHandshake.cs`, `server/game/Session/GameSession.Connect.cs`
- Modify: `server/game/Program.cs` (the `/ws` body)
- Test: `tests/Tabletop.GameServer.Tests/Infrastructure/TestClient.cs`, `tests/Tabletop.GameServer.Tests/Infrastructure/PostgresFixture.cs` (add `SeedTableStateAsync`), `tests/Tabletop.GameServer.Tests/SocketTests.cs`

**Interfaces:**
- Consumes: Tasks 2, 3, 7; `Connect`, `Welcome`, `Rejected`, `RejectReason`, `BoardMessage`, `EventsMessage`, `Board`, `WireJson`, `ProtocolVersion` (Protocol).
- Produces: `sealed class SocketConnection(WebSocket socket, Guid participantId, ILogger log) : ISessionClient` with `Task RunSendLoopAsync(CancellationToken ct)`, `static Task<SocketMessage?> ReadFrameAsync(WebSocket socket, CancellationToken ct)` (null when the peer closed; throws `ProtocolViolationException` on a binary frame, a frame over 4 MiB, or unparseable JSON); `sealed class ConnectionHandshake(GameServerOptions options, ISessionVerifier verifier, IGameRows rows, SessionRegistry registry, ILogger<ConnectionHandshake> log)` with `Task RunAsync(WebSocket socket, CancellationToken aborted)`; `GameSession.ConnectAsync(ISessionClient client)` — the connect sequence in one mailbox turn: `Welcome`, then `BoardMessage` from `setup` on, then the log as `EventsMessage`s of at most 500 events filtered for the viewer, then attach (presence snapshot). Test infrastructure `TestClient` with `static Task<TestClient> OpenAsync(GameServerFactory factory)`, `Task SendAsync(SocketMessage)`, `Task<SocketMessage> ReceiveAsync(TimeSpan? timeout = null)`, `Task<T> ExpectAsync<T>() where T : SocketMessage` (skips frames of other kinds), `Task<Welcome> ConnectAsync(Guid accountId, Guid gameId, int? version = null, string? jwt = null)`, `Task<WebSocketCloseStatus?> WaitClosedAsync()`, `Task AssertSilentAsync(TimeSpan)`.

Spec: `architecture.md` → Two servers → `server/game/` (the client opens its socket with the session JWT and a game id; the server verifies locally, reads the account UUID, checks for a `participant` row); accounts → A game is a lobby (creating and joining are management endpoints; the socket follows); client shell → The connection and the projection (Welcome with teams, participants, configuration; from `setup` on the draft or the snapshot; then the log from the start; at `lobby` no board; reconnect is connect); management API → The version check (`426`; on the socket this is `Rejected(UpgradeRequired)`, contracts plan Task 12); contracts plan Task 12 (the connect sequence and the `Rejected` frame, after which the server closes the socket).

- [ ] **Step 1: Write `SocketConnection.cs`**

```csharp
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Session;

/// <summary>One socket. Frames are read by the handshake's loop and posted to the session; sends go through a queue so the mailbox never waits on a slow peer.</summary>
public sealed class SocketConnection(WebSocket socket, Guid participantId, ILogger log) : ISessionClient
{
    private const int MaxFrameBytes = 4 * 1024 * 1024;
    private readonly Channel<SocketMessage> _outbound = Channel.CreateUnbounded<SocketMessage>(new UnboundedChannelOptions { SingleReader = true });
    private readonly TaskCompletionSource<(WebSocketCloseStatus, string)?> _close = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Guid ConnectionId { get; } = Guid.CreateVersion7();
    public Guid ParticipantId { get; } = participantId;

    public ValueTask SendAsync(SocketMessage message) { _outbound.Writer.TryWrite(message); return ValueTask.CompletedTask; }

    public ValueTask CloseAsync(WebSocketCloseStatus status, string reason)
    {
        _close.TrySetResult((status, reason));
        _outbound.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    /// <summary>Writes queued frames until closed, then sends the close frame. Runs for the life of the socket.</summary>
    public async Task RunSendLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var message in _outbound.Reader.ReadAllAsync(ct))
                await socket.SendAsync(Encoding.UTF8.GetBytes(WireJson.Serialize(message)), WebSocketMessageType.Text, true, ct);
            var (status, reason) = (await _close.Task) ?? (WebSocketCloseStatus.NormalClosure, "bye");
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived) await socket.CloseOutputAsync(status, reason, CancellationToken.None);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { log.LogDebug(ex, "send loop ended for {Connection}", ConnectionId); }
    }

    public static async Task<SocketMessage?> ReadFrameAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = new ArraySegment<byte>(new byte[64 * 1024]);
        using var frame = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text) throw new ProtocolViolationException("binary frames are not part of the protocol");
            frame.Write(buffer.Array!, 0, result.Count);
            if (frame.Length > MaxFrameBytes) throw new ProtocolViolationException("frame too large");
            if (result.EndOfMessage) break;
        }
        try { return WireJson.Deserialize(Encoding.UTF8.GetString(frame.GetBuffer(), 0, (int)frame.Length)); }
        catch (System.Text.Json.JsonException ex) { throw new ProtocolViolationException($"unreadable frame: {ex.Message}"); }
    }
}
```

- [ ] **Step 2: Write `GameSession.Connect.cs`**

```csharp
using Tabletop.GameServer.Dispatch;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Session;

public sealed partial class GameSession
{
    private const int EventsPerMessage = 500;

    /// <summary>The connect sequence (client shell → The connection and the projection), in one mailbox turn so no event slips between the log and live delivery.</summary>
    public Task ConnectAsync(ISessionClient client) => RunAsync(async () =>
    {
        await client.SendAsync(new Welcome(client.ParticipantId, Info));
        if (Draft is not null) await client.SendAsync(new BoardMessage(new Board(Draft, [])));
        if (Play is not null)
        {
            await client.SendAsync(new BoardMessage(Play.Board));
            var log = EventFilter.ForViewer(Play.Applier.Log, TeamOf(client.ParticipantId), new HashSet<Guid>());
            for (var i = 0; i < log.Count; i += EventsPerMessage) await client.SendAsync(new EventsMessage(log.Skip(i).Take(EventsPerMessage).ToList()));
            if (log.Count == 0) await client.SendAsync(new EventsMessage([]));
        }
        await AttachCoreAsync(client);
        await client.SendAsync(Rest());
    });
}
```

An empty `EventsMessage` at `playing` tells the client the log has been delivered in full; the client shell treats "the log from the start" as one thing, so it must always arrive.

**A rejoin is the ordinary connect, not a special case** (owner ruling 1, 2026-09-06; the model is stated in game flow → Teams are frozen at the flip, and a team is a slot). `FindParticipantAsync(gameId, accountId)` finds the row, and the participant's team is whatever `participant.team_id` already holds: `Welcome` carries it inside `Info.Participants`, and `TeamOf` filters the log by it. So a socket arriving at `playing` for a participant who has a team is that team's player again, with no re-pick, no re-derivation and no branch on status anywhere in `ConnectionHandshake` or `ConnectAsync`. A spectator is a participant whose `team_id` is null, which is a fact about the row rather than about how the socket arrived, so someone who first joined after the flip watches and a returning player does not.

`AtRest` is the last frame of the connect sequence and the one place it is sent per connection rather than broadcast, because what it terminates is this client's delivery. It is sent at every status: at `lobby` and `setup` there is no log and it carries `0`. A client joining an idle game therefore gets one after its initial events without anything else happening in the game, and needs no quiet window to tell "the server is still sending" from "the server has finished".

- [ ] **Step 3: Write `ConnectionHandshake.cs` and wire `/ws`**

```csharp
using System.Net;
using System.Net.WebSockets;
using Tabletop.GameServer.Auth;
using Tabletop.GameServer.Configuration;
using Tabletop.GameServer.Persistence;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Session;

/// <summary>Connect → Welcome | Rejected. Then the read loop, until the peer closes or breaks the protocol.</summary>
public sealed class ConnectionHandshake(GameServerOptions options, ISessionVerifier verifier, IGameRows rows, SessionRegistry registry, ILogger<ConnectionHandshake> log)
{
    public async Task RunAsync(WebSocket socket, CancellationToken aborted)
    {
        SocketMessage? first;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(aborted))
        {
            timeout.CancelAfter(options.HandshakeTimeout);
            try { first = await SocketConnection.ReadFrameAsync(socket, timeout.Token); }
            catch (Exception ex) when (ex is OperationCanceledException or ProtocolViolationException or WebSocketException)
            { await CloseAsync(socket, WebSocketCloseStatus.ProtocolError, "expected a connect frame"); return; }
        }
        if (first is not Connect connect) { await CloseAsync(socket, WebSocketCloseStatus.ProtocolError, "the first frame must be connect"); return; }

        if (connect.ClientVersion < options.MinimumClientVersion) { await RejectAsync(socket, RejectReason.UpgradeRequired); return; }
        var session = await verifier.VerifyAsync(connect.SessionJwt, aborted);
        if (session is null) { await RejectAsync(socket, RejectReason.InvalidSession); return; }
        var participantId = await rows.FindParticipantAsync(connect.GameId, session.AccountId, aborted);
        if (participantId is null) { await RejectAsync(socket, RejectReason.NotAParticipant); return; }
        if (registry.Draining) { await CloseAsync(socket, WebSocketCloseStatus.EndpointUnavailable, "shutting down"); return; }

        GameSession game;
        try { game = await registry.GetOrHydrateAsync(connect.GameId, aborted); }
        // ProjectionDesyncException here means the stored log does not apply to the stored snapshot, which is a server bug and
        // not something a reconnect can clear: every attempt replays the same event (owner ruling 2).
        catch (Exception ex) when (ex is GameNotFoundException or GameRowsInconsistentException or Rules.UnknownGameRulesException or ProjectionDesyncException)
        { log.LogError(ex, "cannot hydrate {GameId}", connect.GameId); await CloseAsync(socket, WebSocketCloseStatus.InternalServerError, "cannot hydrate this game"); return; }

        var connection = new SocketConnection(socket, participantId.Value, log);
        var sending = connection.RunSendLoopAsync(CancellationToken.None);
        await game.ConnectAsync(connection);
        try
        {
            while (await SocketConnection.ReadFrameAsync(socket, aborted) is { } frame) await game.ReceiveAsync(connection, frame);
        }
        catch (ProtocolViolationException ex) { await connection.CloseAsync(WebSocketCloseStatus.ProtocolError, ex.Message); }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { }
        // Last resort: whatever a handler threw, this connection closes with a reason instead of the frame silently ending the
        // loop. Failures the session cannot survive have already called FaultAsync and closed every socket themselves.
        catch (Exception ex) { log.LogError(ex, "handling a frame for {GameId} failed", connect.GameId); await connection.CloseAsync(WebSocketCloseStatus.InternalServerError, "the server failed to handle a frame"); }
        finally
        {
            await connection.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye");
            await game.DetachAsync(connection);
            await sending;
            await registry.ReleaseIfEmptyAsync(game);
        }
    }

    private static async Task RejectAsync(WebSocket socket, RejectReason reason)
    {
        await socket.SendAsync(System.Text.Encoding.UTF8.GetBytes(WireJson.Serialize(new Rejected(reason))), WebSocketMessageType.Text, true, CancellationToken.None);
        await CloseAsync(socket, WebSocketCloseStatus.NormalClosure, reason.ToString());
    }

    private static async Task CloseAsync(WebSocket socket, WebSocketCloseStatus status, string reason)
    {
        try { if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived) await socket.CloseOutputAsync(status, reason, CancellationToken.None); }
        catch (WebSocketException) { }
    }
}
```

In `Program.cs`: `builder.Services.AddSingleton<ConnectionHandshake>();` and replace the `/ws` body's last line with `await context.RequestServices.GetRequiredService<ConnectionHandshake>().RunAsync(socket, context.RequestAborted);`.

- [ ] **Step 4: Write `TestClient.cs` and add `SeedTableStateAsync` to the fixture**

```csharp
using System.Net.WebSockets;
using System.Text;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests.Infrastructure;

public sealed class TestClient(WebSocket socket) : IAsyncDisposable
{
    public static async Task<TestClient> OpenAsync(GameServerFactory factory) =>
        new(await factory.Server.CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None));

    public async Task SendAsync(SocketMessage message) =>
        await socket.SendAsync(Encoding.UTF8.GetBytes(WireJson.Serialize(message)), WebSocketMessageType.Text, true, CancellationToken.None);

    public async Task<SocketMessage> ReceiveAsync(TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
        var buffer = new byte[4 * 1024 * 1024]; var total = 0;
        while (true)
        {
            var r = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, total, buffer.Length - total), cts.Token);
            if (r.MessageType == WebSocketMessageType.Close) throw new InvalidOperationException($"closed: {socket.CloseStatus} {socket.CloseStatusDescription}");
            total += r.Count;
            if (r.EndOfMessage) return WireJson.Deserialize(Encoding.UTF8.GetString(buffer, 0, total));
        }
    }

    public async Task<T> ExpectAsync<T>() where T : SocketMessage
    {
        for (var i = 0; i < 50; i++) if (await ReceiveAsync() is T t) return t;
        throw new InvalidOperationException($"no {typeof(T).Name} within 50 frames");
    }

    public async Task<Welcome> ConnectAsync(Guid accountId, Guid gameId, int? version = null, string? jwt = null)
    {
        await SendAsync(new Connect(jwt ?? TestKeys.Mint(accountId), gameId, version ?? ProtocolVersion.Current));
        return await ExpectAsync<Welcome>();
    }

    public async Task<WebSocketCloseStatus?> WaitClosedAsync()
    {
        var buffer = new byte[1024];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseSent)
        {
            var r = await socket.ReceiveAsync(buffer, cts.Token);
            if (r.MessageType == WebSocketMessageType.Close) break;
        }
        return socket.CloseStatus;
    }

    public async Task AssertSilentAsync(TimeSpan window)
    {
        try { var frame = await ReceiveAsync(window); throw new Xunit.Sdk.XunitException($"expected silence, got {frame.GetType().Name}"); }
        catch (OperationCanceledException) { }
    }

    public ValueTask DisposeAsync() { socket.Dispose(); return ValueTask.CompletedTask; }
}
```

```csharp
    // PostgresFixture: a table_state row for a game seeded at setup/playing/ended, when the test does not go through StartSetup.
    public async Task<Guid> SeedTableStateAsync(Guid gameId, double width = 72, double length = 48)
    {
        var id = Guid.CreateVersion7();
        await using var cmd = Db.CreateCommand("insert into management.table_state (id, game_id, width, length, texture_builtin) values ($1, $2, $3, $4, 'grass')");
        cmd.Parameters.AddWithValue(id); cmd.Parameters.AddWithValue(gameId); cmd.Parameters.AddWithValue((float)width); cmd.Parameters.AddWithValue((float)length);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }
```

- [ ] **Step 5: Write the failing end-to-end tests**

```csharp
using System.Net.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Tabletop.GameServer.Log;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

[Collection("Postgres")]
public class SocketTests(PostgresFixture pg)
{
    [Fact]
    public async Task Connecting_at_lobby_yields_welcome_and_no_board()
    {
        using var factory = new GameServerFactory();
        var seeded = await pg.SeedGameAsync(GameStatus.Lobby, spectators: 1);
        await using var client = await TestClient.OpenAsync(factory);
        var welcome = await client.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        Assert.Equal(seeded.Participants[0].ParticipantId, welcome.ParticipantId);
        Assert.Equal(GameStatus.Lobby, welcome.Game.Status);
        Assert.Equal(3, welcome.Game.Participants.Count);
        Assert.Equal(0, (await client.ExpectAsync<AtRest>()).Seq);   // the connect sequence ends at rest even where there is no log
        await client.AssertSilentAsync(TimeSpan.FromMilliseconds(300));
    }

    [Theory]
    [InlineData("version", RejectReason.UpgradeRequired)] [InlineData("jwt", RejectReason.InvalidSession)] [InlineData("stranger", RejectReason.NotAParticipant)] [InlineData("no-game", RejectReason.NotAParticipant)]
    public async Task A_bad_connect_is_rejected_then_closed(string flaw, RejectReason expected)
    {
        using var factory = new GameServerFactory();
        var seeded = await pg.SeedGameAsync(GameStatus.Lobby);
        var stranger = await pg.SeedAccountAsync("Stranger");
        await using var client = await TestClient.OpenAsync(factory);
        await client.SendAsync(flaw switch
        {
            "version" => new Connect(TestKeys.Mint(seeded.CreatorAccountId), seeded.GameId, 0),
            "jwt" => new Connect("not.a.jwt", seeded.GameId, ProtocolVersion.Current),
            "stranger" => new Connect(TestKeys.Mint(stranger), seeded.GameId, ProtocolVersion.Current),
            _ => new Connect(TestKeys.Mint(seeded.CreatorAccountId), Guid.CreateVersion7(), ProtocolVersion.Current),
        });
        Assert.Equal(expected, Assert.IsType<Rejected>(await client.ReceiveAsync()).Reason);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, await client.WaitClosedAsync());
    }

    [Fact]
    public async Task A_first_frame_that_is_not_connect_is_a_protocol_error()
    {
        using var factory = new GameServerFactory();
        await using var client = await TestClient.OpenAsync(factory);
        await client.SendAsync(new PingMessage(new Ping(Guid.Empty, new(0, 0, 0), null)));
        Assert.Equal(WebSocketCloseStatus.ProtocolError, await client.WaitClosedAsync());
    }

    [Fact]
    public async Task Connecting_at_playing_yields_welcome_board_and_the_log_then_live_events()
    {
        using var factory = new GameServerFactory();
        var seeded = await pg.SeedGameAsync(GameStatus.Playing, teams: 2);
        await pg.SeedTableStateAsync(seeded.GameId);
        await new PostgresEventLog(pg.Db).AppendAsync(seeded.GameId, [new GameEvent(1, null, new PhaseStarted("deployment"), null), new GameEvent(2, null, new ScoreSet(seeded.TeamIds[0], 1), null)], CancellationToken.None);

        await using var a = await TestClient.OpenAsync(factory);
        await using var b = await TestClient.OpenAsync(factory);
        var welcome = await a.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        Assert.Equal(GameStatus.Playing, welcome.Game.Status);
        Assert.Empty((await a.ExpectAsync<BoardMessage>()).Board.Armies);
        Assert.Equal([1L, 2L], (await a.ExpectAsync<EventsMessage>()).Events.Select(e => e.Seq));
        await b.ConnectAsync(seeded.Participants[1].AccountId, seeded.GameId);
        await b.ExpectAsync<BoardMessage>(); await b.ExpectAsync<EventsMessage>();

        await a.SendAsync(new CommandMessage(Guid.CreateVersion7(), new SetScore(seeded.TeamIds[0], 7)));
        var live = await a.ExpectAsync<EventsMessage>();
        Assert.Equal(3, live.Events.Single().Seq);
        Assert.Equal(seeded.Participants[0].ParticipantId, live.Events.Single().Actor);
        Assert.Equal(7, ((ScoreSet)(await b.ExpectAsync<EventsMessage>()).Events.Single().Payload).Score);
        Assert.Equal(1, await pg.CountAsync("select count(*) from game.game_event where game_id = $1 and seq = 3", seeded.GameId));

        await a.SendAsync(new CommandMessage(Guid.CreateVersion7(), new SetWounds(Guid.CreateVersion7(), 1)));
        Assert.Equal(RefusalReason.Unknown, (await a.ExpectAsync<Refused>()).Reason);
        await b.AssertSilentAsync(TimeSpan.FromMilliseconds(200));   // a refusal appends nothing, so b gets neither events nor a rest frame
    }

    [Fact]
    public async Task At_rest_closes_the_connect_sequence_and_follows_every_batch()
    {
        using var factory = new GameServerFactory();
        var seeded = await pg.SeedGameAsync(GameStatus.Playing, teams: 2);
        await pg.SeedTableStateAsync(seeded.GameId);
        await new PostgresEventLog(pg.Db).AppendAsync(seeded.GameId, [new GameEvent(1, null, new PhaseStarted("deployment"), null)], CancellationToken.None);

        await using var a = await TestClient.OpenAsync(factory);
        await a.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        Assert.IsType<BoardMessage>(await a.ReceiveAsync());
        Assert.Equal([1L], Assert.IsType<EventsMessage>(await a.ReceiveAsync()).Events.Select(e => e.Seq));
        Assert.Equal(1L, Assert.IsType<AtRest>(await a.ReceiveAsync()).Seq);           // the whole log, then rest, with nothing between
        await a.AssertSilentAsync(TimeSpan.FromMilliseconds(300));                     // an idle game says nothing more

        await a.SendAsync(new CommandMessage(Guid.CreateVersion7(), new SetScore(seeded.TeamIds[0], 5)));
        Assert.Equal(2L, Assert.IsType<EventsMessage>(await a.ReceiveAsync()).Events.Single().Seq);
        Assert.Equal(2L, Assert.IsType<AtRest>(await a.ReceiveAsync()).Seq);           // after the batch's events, never before them

        await a.SendAsync(new CommandMessage(Guid.CreateVersion7(), new SetWounds(Guid.CreateVersion7(), 1)));
        Assert.Equal(RefusalReason.Unknown, Assert.IsType<Refused>(await a.ReceiveAsync()).Reason);
        Assert.Equal(2L, Assert.IsType<AtRest>(await a.ReceiveAsync()).Seq);           // refused: the high-water mark stands and only the sender hears it
    }

    [Fact]
    public async Task Reconnecting_rebuilds_from_the_log_and_replaces_the_older_socket()
    {
        using var factory = new GameServerFactory();
        var seeded = await pg.SeedGameAsync(GameStatus.Playing);
        await pg.SeedTableStateAsync(seeded.GameId);
        await using var first = await TestClient.OpenAsync(factory);
        await first.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await first.ExpectAsync<EventsMessage>();
        await first.SendAsync(new CommandMessage(Guid.CreateVersion7(), new SetScore(seeded.TeamIds[1], 2)));
        await first.ExpectAsync<EventsMessage>();

        await using var second = await TestClient.OpenAsync(factory);
        var back = await second.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        Assert.Equal(seeded.TeamIds[0], back.Game.Participants.Single(p => p.Id == back.ParticipantId).TeamId);   // the rejoin returns the slot; nothing re-picks
        await second.ExpectAsync<BoardMessage>();
        Assert.Equal([1L], (await second.ExpectAsync<EventsMessage>()).Events.Select(e => e.Seq));
        Assert.Equal(WebSocketCloseStatus.NormalClosure, await first.WaitClosedAsync());
    }

    [Fact]
    public async Task The_last_socket_leaving_releases_the_session_and_the_next_hydrates_again()
    {
        using var factory = new GameServerFactory();
        var seeded = await pg.SeedGameAsync(GameStatus.Playing);
        await pg.SeedTableStateAsync(seeded.GameId);
        var registry = factory.Services.GetRequiredService<Tabletop.GameServer.Session.SessionRegistry>();
        await using (var client = await TestClient.OpenAsync(factory))
        {
            await client.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
            await client.ExpectAsync<EventsMessage>();
            Assert.Contains(registry.Live, s => s.GameId == seeded.GameId);
            await client.SendAsync(new CommandMessage(Guid.CreateVersion7(), new SetScore(seeded.TeamIds[0], 3)));
            await client.ExpectAsync<EventsMessage>();
        }
        await Task.Delay(200);
        Assert.DoesNotContain(registry.Live, s => s.GameId == seeded.GameId);
        await using var again = await TestClient.OpenAsync(factory);
        await again.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await again.ExpectAsync<BoardMessage>();
        Assert.Equal(3, ((ScoreSet)(await again.ExpectAsync<EventsMessage>()).Events.Single().Payload).Score);
    }
}
```

`TestClient.DisposeAsync` disposes the socket without a close handshake; the server's read loop sees a `WebSocketException`, detaches, and releases — which is exactly the crashed-client path.

- [ ] **Step 6: Run `dotnet test tests/Tabletop.GameServer.Tests`; expect 114 passed. Commit: `Accept sockets: the connect handshake, the connect sequence and the read loop`.**

### Task 9: The presence timer and graceful shutdown

**Files:**
- Create: `server/game/Session/SessionHost.cs`
- Modify: `server/game/Program.cs`
- Test: `tests/Tabletop.GameServer.Tests/PresenceAndShutdownTests.cs`

**Interfaces:**
- Consumes: `SessionRegistry`, `GameSession.TickPresenceAsync`, `GameSession.DrainAsync`, `GameServerOptions.PresenceRelayHz`.
- Produces: `sealed class SessionHost(SessionRegistry registry, GameServerOptions options, IHostApplicationLifetime lifetime, ILogger<SessionHost> log) : BackgroundService` — a `PeriodicTimer` at `1000 / PresenceRelayHz` ms calling `TickPresenceAsync` on every live session; on `ApplicationStopping` it calls `registry.DrainAllAsync("server shutting down")` so every socket closes with `1001 Going Away` and every queued row write and log append finishes before the host exits (within `HostOptions.ShutdownTimeout`, 30 s).

Spec: `architecture.md` → Deployment (a continuous relay at a fixed rate, never logged; deploys kill tasks holding live games — drain to the event log on SIGTERM; the heartbeat is Task 1's keep-alive); presence → The presence value (latest wins), Deliberately not decided (the sample rate — presence left it open and `architecture.md`'s "four players at 20Hz" is the standing first-pass figure the whole-design review left untouched, so 20 Hz it is, on `GameServerOptions.PresenceRelayHz`). Because every event is appended before it is broadcast and every draft write is queued on the mailbox, "draining to the log" is finishing the queue; nothing is held in memory that the log does not already have.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tabletop.GameServer.Session;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

[Collection("Postgres")]
public class PresenceAndShutdownTests(PostgresFixture pg)
{
    [Fact]
    public async Task Presence_reaches_the_other_participant_within_a_few_ticks_and_ends_when_they_leave()
    {
        using var factory = new GameServerFactory();
        var seeded = await pg.SeedGameAsync(GameStatus.Lobby);
        await using var a = await TestClient.OpenAsync(factory);
        await using var b = await TestClient.OpenAsync(factory);
        await a.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await b.ConnectAsync(seeded.Participants[1].AccountId, seeded.GameId);
        await a.SendAsync(new PresenceMessage(new Presence(Guid.Empty, new Cursor(new(1, 2, 3), null), null, [], null)));
        var relayed = await b.ExpectAsync<PresenceMessage>();
        Assert.Equal(seeded.Participants[0].ParticipantId, relayed.Presence.ParticipantId);
        await a.SendAsync(new PingMessage(new Ping(Guid.Empty, new(9, 9, 0), null)));
        Assert.Equal(9, (await b.ExpectAsync<PingMessage>()).Ping.Point.X);
        await a.DisposeAsync();
        Assert.Equal(seeded.Participants[0].ParticipantId, (await b.ExpectAsync<PresenceEnded>()).ParticipantId);
    }

    [Fact]
    public async Task Stopping_the_host_closes_every_socket_with_going_away_and_empties_the_registry()
    {
        var factory = new GameServerFactory();
        var seeded = await pg.SeedGameAsync(GameStatus.Lobby);
        await using var a = await TestClient.OpenAsync(factory);
        await a.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        var registry = factory.Services.GetRequiredService<SessionRegistry>();
        Assert.Single(registry.Live);

        var lifetime = factory.Services.GetRequiredService<IHostApplicationLifetime>();
        var stopped = new TaskCompletionSource();
        lifetime.ApplicationStopped.Register(() => stopped.TrySetResult());
        lifetime.StopApplication();
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, await a.WaitClosedAsync());
        Assert.Empty(registry.Live);
        Assert.True(registry.Draining);
    }
}
```

- [ ] **Step 2: Run; expect `SessionHost` missing (the presence test times out). Write `SessionHost.cs` and register it**

```csharp
using Tabletop.GameServer.Configuration;

namespace Tabletop.GameServer.Session;

/// <summary>The fixed-rate presence relay, and the SIGTERM drain. Nothing else in the process runs on a timer.</summary>
public sealed class SessionHost(SessionRegistry registry, GameServerOptions options, IHostApplicationLifetime lifetime, ILogger<SessionHost> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        lifetime.ApplicationStopping.Register(() => DrainAsync().GetAwaiter().GetResult());
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1000.0 / options.PresenceRelayHz));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                foreach (var session in registry.Live)
                    try { await session.TickPresenceAsync(); }
                    catch (InvalidOperationException) { /* drained between Live and Tick */ }
        }
        catch (OperationCanceledException) { }
    }

    private async Task DrainAsync()
    {
        log.LogInformation("Draining {Count} live games", registry.Live.Count);
        await registry.DrainAllAsync("server shutting down");
    }
}
```

`Program.cs`: `builder.Services.AddHostedService<SessionHost>();`. The drain runs synchronously inside `ApplicationStopping` on purpose: the host waits for those callbacks before it stops Kestrel, so every socket gets its `1001` while the listener is still up, and every queued append or draft write completes before the process exits. `HostOptions.ShutdownTimeout` (Task 1) bounds it at 30 s.

- [ ] **Step 3: Run `dotnet test tests/Tabletop.GameServer.Tests`; expect 116 passed. Commit: `Relay presence on a timer and drain every game on shutdown`.**

### Task 10: The lobby — team, army and configuration picks, and starting setup

**Files:**
- Create: `server/game/Persistence/PostgresGameRows.Writes.cs`, `server/game/Session/GameSession.Lobby.cs`
- Modify: `server/game/Session/GameSession.Frames.cs` (`HandleEditAsync`)
- Test: `tests/Tabletop.GameServer.Tests/LobbyTests.cs`

**Interfaces:**
- Consumes: `LobbyEdit` and its six kinds, `SetupEdit`, `GameInfoMessage`, `BoardMessage`, `Refused`, `RefusalReason` (Protocol); `TableState`, `VisualRef`, `TableSize` (Content); Task 3's write members of `IGameRows`.
- Produces: `PostgresGameRows` write members `CreateTeamAsync`, `SetParticipantTeamAsync`, `SetParticipantArmyAsync`, `SetConfigurationAsync`, `StartSetupAsync`, `WriteDraftAsync` (Task 12 adds `EndSetupAsync`), plus `internal static Task WriteTreeAsync(NpgsqlConnection, NpgsqlTransaction, TableState draft)` (delete children, update the row, insert children); `GameSession.HandleLobbyEditAsync(ISessionClient from, Guid editId, LobbyEdit edit)`; `GameSession.NewDraft()` (the default draft from `GameServerOptions`); `GameSession.ReloadInfoAsync()` (re-reads `GameInfo` and broadcasts `GameInfoMessage`).

Spec: setup phase → Joining (a participant has no team until they pick one; teams created and participants moved by lobby commands), The lifecycle (in the lobby everyone picks a team and an army and the configuration may be changed; the creator starts setup, which creates the draft row; teams and armies stay editable during setup), Editing is free for all (lobby commands are the same kind: written to the game's rows, broadcast, never logged), `game.created_by` (read for the two transitions and nothing else); whole-design review rulings 4 and 5; contracts plan Task 11 (`LobbyEdit`) and spec correction 2 (`participant.army_id`). Lobby edits are accepted at `lobby` and `setup` and refused with `WrongStatus` from `playing` on: the team list is locked at the flip and the game server is read-only from there (ruling 4; owner ruling 1, 2026-09-06, whose model is stated in game flow → Teams are frozen at the flip, and a team is a slot). The refusal is about creating and picking a team, never about connecting. A participant whose `participant.team_id` is already set keeps that team when they reconnect at `playing`, because `Welcome` reads the row and no edit is involved. A spectator is a participant with no team row to return to, which is what someone who first joined after the flip has. Spec correction 1 puts the same model into the setup phase spec.

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

[Collection("Postgres")]
public class LobbyTests(PostgresFixture pg)
{
    async Task<(GameServerFactory F, SeededGame G, TestClient Creator, TestClient Other)> LobbyAsync()
    {
        var factory = new GameServerFactory();
        var seeded = await pg.SeedGameAsync(GameStatus.Lobby, teams: 2);
        var creator = await TestClient.OpenAsync(factory); var other = await TestClient.OpenAsync(factory);
        await creator.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await other.ConnectAsync(seeded.Participants[1].AccountId, seeded.GameId);
        return (factory, seeded, creator, other);
    }
    static EditMessage Edit(Edit e) => new(Guid.CreateVersion7(), e);

    [Fact]
    public async Task Creating_a_team_and_picking_it_writes_rows_and_broadcasts_the_game_info()
    {
        var (f, g, creator, other) = await LobbyAsync();
        await other.SendAsync(Edit(new CreateTeam()));
        var info = (await creator.ExpectAsync<GameInfoMessage>()).Game;
        Assert.Equal([1, 2, 3], info.Teams.Select(t => t.Ordinal));
        await other.ExpectAsync<GameInfoMessage>();
        var third = info.Teams[2].Id;
        await other.SendAsync(Edit(new PickTeam(third)));
        Assert.Equal(third, (await creator.ExpectAsync<GameInfoMessage>()).Game.Participants.Single(p => p.AccountId == g.Participants[1].AccountId).TeamId);
        await other.SendAsync(Edit(new PickTeam(null)));
        Assert.Null((await creator.ExpectAsync<GameInfoMessage>()).Game.Participants.Single(p => p.AccountId == g.Participants[1].AccountId).TeamId);
        Assert.Equal(1, await pg.CountAsync("select count(*) from management.participant where id = $1 and team_id is null", g.Participants[1].ParticipantId));
        await other.SendAsync(Edit(new PickTeam(Guid.CreateVersion7())));
        Assert.Equal(RefusalReason.Unknown, (await other.ExpectAsync<Refused>()).Reason);
    }

    [Fact]
    public async Task Picking_an_army_requires_owning_it()
    {
        var (f, g, creator, other) = await LobbyAsync();
        var mine = await pg.SeedArmyAsync(g.CreatorAccountId);
        await creator.SendAsync(Edit(new PickArmy(mine.Id)));
        Assert.Equal(mine.Id, (await other.ExpectAsync<GameInfoMessage>()).Game.Participants.Single(p => p.AccountId == g.CreatorAccountId).ArmyId);
        await other.SendAsync(Edit(new PickArmy(mine.Id)));
        Assert.Equal(RefusalReason.NotPermitted, (await other.ExpectAsync<Refused>()).Reason);
        await other.SendAsync(Edit(new PickArmy(Guid.CreateVersion7())));
        Assert.Equal(RefusalReason.Unknown, (await other.ExpectAsync<Refused>()).Reason);
    }

    [Fact]
    public async Task Configuration_changes_need_a_known_properties_hash()
    {
        var (f, g, creator, other) = await LobbyAsync();
        await other.SendAsync(Edit(new SetConfiguration("Saturday", g.PropertiesHash, SchedulerShape.PlayerTurn, Enforcement.Strict, true)));
        var info = (await creator.ExpectAsync<GameInfoMessage>()).Game;
        Assert.Equal(("Saturday", SchedulerShape.PlayerTurn, Enforcement.Strict, true), (info.Name, info.SchedulerShape, info.Enforcement, info.WaitsBetweenSteps));
        await other.SendAsync(Edit(new SetConfiguration("x", "sha256:nope", SchedulerShape.PlayerTurn, Enforcement.Strict, true)));
        Assert.Equal(RefusalReason.Unknown, (await other.ExpectAsync<Refused>()).Reason);
    }

    [Fact]
    public async Task Only_the_creator_starts_setup_and_it_creates_the_draft_row()
    {
        var (f, g, creator, other) = await LobbyAsync();
        await other.SendAsync(Edit(new StartSetup()));
        Assert.Equal(RefusalReason.NotPermitted, (await other.ExpectAsync<Refused>()).Reason);
        await creator.SendAsync(Edit(new StartSetup()));
        Assert.Equal(GameStatus.Setup, (await other.ExpectAsync<GameInfoMessage>()).Game.Status);
        var board = (await other.ExpectAsync<BoardMessage>()).Board;
        Assert.Equal((72.0, 48.0, "grass"), (board.Table.Size.Width, board.Table.Size.Length, board.Table.Texture.Builtin));
        Assert.Empty(board.Armies);
        Assert.Equal(1, await pg.CountAsync("select count(*) from management.table_state where game_id = $1", g.GameId));
        Assert.Equal(1, await pg.CountAsync("select count(*) from management.game where id = $1 and status = 'setup'", g.GameId));
        await creator.SendAsync(Edit(new StartSetup()));
        Assert.Equal(RefusalReason.WrongStatus, (await creator.ExpectAsync<Refused>()).Reason);
        await other.SendAsync(Edit(new CreateTeam()));                       // still editable during setup
        Assert.Equal(3, (await creator.ExpectAsync<GameInfoMessage>()).Game.Teams.Count);
    }

    [Fact]
    public async Task Lobby_edits_are_refused_from_playing_on()
    {
        using var factory = new GameServerFactory();
        var seeded = await pg.SeedGameAsync(GameStatus.Playing);
        await pg.SeedTableStateAsync(seeded.GameId);
        await using var client = await TestClient.OpenAsync(factory);
        await client.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await client.SendAsync(Edit(new CreateTeam()));
        Assert.Equal(RefusalReason.WrongStatus, (await client.ExpectAsync<Refused>()).Reason);
    }

    // Owner ruling 1, 2026-09-06: the team list is locked at the flip to playing, and a participant
    // who reconnects during play is returned to the slot their participant row already names.
    [Fact]
    public async Task Teams_are_locked_from_playing_on_but_a_rejoining_participant_keeps_their_team()
    {
        using var factory = new GameServerFactory();
        var seeded = await pg.SeedGameAsync(GameStatus.Playing, teams: 2, spectators: 1);
        await pg.SeedTableStateAsync(seeded.GameId);
        await using var player = await TestClient.OpenAsync(factory);
        var welcome = await player.ConnectAsync(seeded.Participants[1].AccountId, seeded.GameId);
        Assert.Equal(seeded.TeamIds[1], welcome.Game.Participants.Single(p => p.Id == welcome.ParticipantId).TeamId);
        await player.SendAsync(Edit(new PickTeam(seeded.TeamIds[0])));
        Assert.Equal(RefusalReason.WrongStatus, (await player.ExpectAsync<Refused>()).Reason);   // no re-picking during play
        Assert.Equal(1, await pg.CountAsync("select count(*) from management.participant where id = $1 and team_id = $2", seeded.Participants[1].ParticipantId, seeded.TeamIds[1]));

        await using var again = await TestClient.OpenAsync(factory);                             // the same account, a new socket
        var back = await again.ConnectAsync(seeded.Participants[1].AccountId, seeded.GameId);
        Assert.Equal(seeded.TeamIds[1], back.Game.Participants.Single(p => p.Id == back.ParticipantId).TeamId);

        await using var watcher = await TestClient.OpenAsync(factory);                           // never had a slot, so watches
        var watching = await watcher.ConnectAsync(seeded.Participants[2].AccountId, seeded.GameId);
        Assert.Null(watching.Game.Participants.Single(p => p.Id == watching.ParticipantId).TeamId);
    }
}
```

- [ ] **Step 2: Run; expect refusals where the tests want rows. Write `PostgresGameRows.Writes.cs`**

```csharp
using Npgsql;
using NpgsqlTypes;
using Tabletop.Content;
using Tabletop.Protocol;
using static Tabletop.GameServer.Persistence.RowMapping;

namespace Tabletop.GameServer.Persistence;

public sealed partial class PostgresGameRows
{
    public async Task<Team> CreateTeamAsync(Guid gameId, CancellationToken ct)
    {
        var id = Guid.CreateVersion7();
        await using var cmd = db.CreateCommand("insert into management.team (id, game_id, ordinal) select $1, $2, coalesce(max(ordinal), 0) + 1 from management.team where game_id = $2 returning ordinal");
        cmd.Parameters.AddWithValue(id); cmd.Parameters.AddWithValue(gameId);
        return new Team(id, (int)(await cmd.ExecuteScalarAsync(ct))!);
    }

    public async Task SetParticipantTeamAsync(Guid participantId, Guid? teamId, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("update management.participant set team_id = $2 where id = $1");
        cmd.Parameters.AddWithValue(participantId); cmd.Parameters.AddWithValue((object?)teamId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SetParticipantArmyAsync(Guid participantId, Guid? armyId, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("update management.participant set army_id = $2 where id = $1");
        cmd.Parameters.AddWithValue(participantId); cmd.Parameters.AddWithValue((object?)armyId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SetConfigurationAsync(Guid gameId, SetConfiguration c, CancellationToken ct)
    {
        // game_rules follows the chosen properties row, as management copies it on create (management plan → Rows by writer: both are rewritten on set_configuration).
        // The six configuration columns and nothing else: rng_seed is management's, written once at creation (owner ruling 1).
        await using var cmd = db.CreateCommand("update management.game set name = $2, properties_hash = $3, game_rules = (select game_rules from management.properties where content_hash = $3), scheduler_shape = $4, enforcement = $5, waits_between_steps = $6 where id = $1");
        cmd.Parameters.AddWithValue(gameId); cmd.Parameters.AddWithValue(c.Name); cmd.Parameters.AddWithValue(c.PropertiesHash);
        cmd.Parameters.AddWithValue(EnumText(c.SchedulerShape)); cmd.Parameters.AddWithValue(EnumText(c.Enforcement)); cmd.Parameters.AddWithValue(c.WaitsBetweenSteps);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task StartSetupAsync(Guid gameId, TableState draft, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using (var cmd = new NpgsqlCommand("insert into management.table_state (id, game_id, name, width, length, texture_builtin, texture_asset_id) values ($1, $2, $3, $4, $5, $6, $7)", conn, tx))
        {
            cmd.Parameters.AddWithValue(draft.Id); cmd.Parameters.AddWithValue(gameId); cmd.Parameters.AddWithValue((object?)draft.Name ?? DBNull.Value);
            cmd.Parameters.AddWithValue((float)draft.Size.Width); cmd.Parameters.AddWithValue((float)draft.Size.Length);
            cmd.Parameters.AddWithValue((object?)draft.Texture.Builtin ?? DBNull.Value); cmd.Parameters.AddWithValue((object?)draft.Texture.AssetId ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await WriteTreeAsync(conn, tx, draft, ct);
        await using (var cmd = new NpgsqlCommand("update management.game set status = 'setup' where id = $1 and status = 'lobby'", conn, tx) { Parameters = { new NpgsqlParameter { Value = gameId } } })
            if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("game is not in lobby");
        await tx.CommitAsync(ct);
    }

    public async Task WriteDraftAsync(Guid gameId, TableState draft, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using (var cmd = new NpgsqlCommand("update management.table_state set name = $3, width = $4, length = $5, texture_builtin = $6, texture_asset_id = $7 where id = $1 and game_id = $2", conn, tx))
        {
            cmd.Parameters.AddWithValue(draft.Id); cmd.Parameters.AddWithValue(gameId); cmd.Parameters.AddWithValue((object?)draft.Name ?? DBNull.Value);
            cmd.Parameters.AddWithValue((float)draft.Size.Width); cmd.Parameters.AddWithValue((float)draft.Size.Length);
            cmd.Parameters.AddWithValue((object?)draft.Texture.Builtin ?? DBNull.Value); cmd.Parameters.AddWithValue((object?)draft.Texture.AssetId ?? DBNull.Value);
            if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("no draft row for this game");
        }
        await WriteTreeAsync(conn, tx, draft, ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>Children are replaced whole: the draft is a value and one drop is one write.</summary>
    internal static async Task WriteTreeAsync(NpgsqlConnection conn, NpgsqlTransaction tx, TableState draft, CancellationToken ct)
    {
        async Task Run(string sql, params object?[] args)
        {
            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            foreach (var a in args) cmd.Parameters.AddWithValue(a ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        foreach (var table in new[] { "terrain_placement", "marker", "deployment_zone" }) await Run($"delete from management.{table} where table_state_id = $1", draft.Id);
        foreach (var t in draft.Terrain)
        {
            await Run("insert into management.terrain_placement (id, table_state_id, builtin, asset_id, pos_x, pos_y, pos_z, yaw, scale) values ($1, $2, $3, $4, $5, $6, $7, $8, $9)",
                t.Id, draft.Id, t.Mesh.Builtin, t.Mesh.AssetId, (float)t.Position.X, (float)t.Position.Y, (float)t.Position.Z, (float)t.Yaw.Degrees, (float)t.Scale);
            foreach (var v in t.Volumes)
                await Run($"insert into management.terrain_volume (id, terrain_placement_id, {VolumeColumns}) values ($1, $2, {string.Join(", ", Enumerable.Range(3, 16).Select(i => "$" + i))})",
                    [v.Id, t.Id, .. VolumeValues(v.Volume)]);
        }
        foreach (var m in draft.Markers)
            await Run("insert into management.marker (id, table_state_id, kind, pos_x, pos_y, pos_z, yaw, diameter, label, visual_builtin, visual_asset_id) values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11)",
                m.Id, draft.Id, EnumText(m.Kind), (float)m.Position.X, (float)m.Position.Y, (float)m.Position.Z, (float)m.Yaw.Degrees, (float)m.Diameter, m.Label, m.Visual.Builtin, m.Visual.AssetId);
        foreach (var z in draft.Zones)
            await Run("insert into management.deployment_zone (id, table_state_id, style, team_ordinal) values ($1, $2, $3, $4)", z.Id, draft.Id, z.Shape, z.TeamOrdinal);
    }
}
```

- [ ] **Step 3: Write `GameSession.Lobby.cs` and route edits**

```csharp
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Session;

public sealed partial class GameSession
{
    private Task HandleEditAsync(ISessionClient from, EditMessage message) => message.Edit switch
    {
        LobbyEdit l => HandleLobbyEditAsync(from, message.EditId, l),
        SetupEdit s => HandleSetupEditAsync(from, message.EditId, s),
        _ => from.SendAsync(new Protocol.Refused(message.EditId, RefusalReason.Unknown, "unknown edit")).AsTask(),
    };

    private Task RefuseEdit(ISessionClient to, Guid editId, RefusalReason reason, string detail) => to.SendAsync(new Protocol.Refused(editId, reason, detail)).AsTask();
    private bool IsCreator(ISessionClient client) => Info.Participants.FirstOrDefault(p => p.Id == client.ParticipantId)?.AccountId == Info.CreatedBy;

    /// <summary>The default draft the creator's StartSetup creates. Size and texture come from configuration (question 2).</summary>
    internal TableState NewDraft() => new(Guid.CreateVersion7(), null, new TableSize(_services.Options.DefaultTableWidth, _services.Options.DefaultTableLength),
        VisualRef.OfBuiltin(_services.Options.DefaultTextureBuiltin), [], [], []);

    internal async Task ReloadInfoAsync()
    {
        Info = await _services.Rows.ReadGameAsync(GameId, CancellationToken.None) ?? throw new GameNotFoundException(GameId);
        if (Play is not null) Play.Info = Info;
        await BroadcastAsync(new GameInfoMessage(Info));
    }

    /// <summary>Written to the game's rows, broadcast as the new GameInfo, never logged. Accepted while lobby or setup; read-only from playing on.</summary>
    private async Task HandleLobbyEditAsync(ISessionClient from, Guid editId, LobbyEdit edit)
    {
        if (Info.Status is not (GameStatus.Lobby or GameStatus.Setup)) { await RefuseEdit(from, editId, RefusalReason.WrongStatus, $"the game is {Info.Status}"); return; }
        var rows = _services.Rows; var ct = CancellationToken.None;
        var me = Info.Participants.First(p => p.Id == from.ParticipantId);
        switch (edit)
        {
            case CreateTeam: await rows.CreateTeamAsync(GameId, ct); break;
            case PickTeam t when t.TeamId is { } id && Info.Teams.All(x => x.Id != id): await RefuseEdit(from, editId, RefusalReason.Unknown, $"no team {id}"); return;
            case PickTeam t: await rows.SetParticipantTeamAsync(me.Id, t.TeamId, ct); break;
            case PickArmy { ArmyId: { } armyId }:
                var owned = await rows.ReadArmyAsync(armyId, ct);
                if (owned is null) { await RefuseEdit(from, editId, RefusalReason.Unknown, $"no army {armyId}"); return; }
                if (owned.OwnerId != me.AccountId) { await RefuseEdit(from, editId, RefusalReason.NotPermitted, "not your army"); return; }
                await rows.SetParticipantArmyAsync(me.Id, armyId, ct); break;
            case PickArmy: await rows.SetParticipantArmyAsync(me.Id, null, ct); break;
            case SetConfiguration c when !await rows.PropertiesExistAsync(c.PropertiesHash, ct): await RefuseEdit(from, editId, RefusalReason.Unknown, $"no properties {c.PropertiesHash}"); return;
            case SetConfiguration c: await rows.SetConfigurationAsync(GameId, c, ct); break;
            case StartSetup when !IsCreator(from): await RefuseEdit(from, editId, RefusalReason.NotPermitted, "only the creator starts setup"); return;
            case StartSetup when Info.Status != GameStatus.Lobby: await RefuseEdit(from, editId, RefusalReason.WrongStatus, "setup has already started"); return;
            case StartSetup:
                var draft = NewDraft();
                await rows.StartSetupAsync(GameId, draft, ct);
                Draft = draft;
                await ReloadInfoAsync();
                await BroadcastAsync(new BoardMessage(new Board(draft, [])));
                return;
            case EndSetup: await HandleEndSetupAsync(from, editId); return;   // Task 12
        }
        await ReloadInfoAsync();
    }

    // Task 11 and Task 12 write these.
    private Task HandleSetupEditAsync(ISessionClient from, Guid editId, SetupEdit edit) => RefuseEdit(from, editId, RefusalReason.WrongStatus, "setup edits are not handled yet");
    private Task HandleEndSetupAsync(ISessionClient from, Guid editId) => RefuseEdit(from, editId, RefusalReason.WrongStatus, "end setup is not handled yet");
}
```

Delete the placeholder `HandleEditAsync` from `GameSession.Frames.cs` (Task 7 Step 3); this file owns it now.

- [ ] **Step 4: Run `dotnet test tests/Tabletop.GameServer.Tests`; expect 122 passed. Commit: `Write the game's rows for lobby picks and start setup`.**

### Task 11: Setup edits — the draft editor, the row write, the board broadcast

**Files:**
- Create: `server/game/Setup/DraftEditor.cs`, `server/game/Session/GameSession.Setup.cs`
- Modify: `server/game/Session/GameSession.Lobby.cs` (remove the `HandleSetupEditAsync` placeholder)
- Test: `tests/Tabletop.GameServer.Tests/DraftEditorTests.cs`, `tests/Tabletop.GameServer.Tests/SetupEditTests.cs`

**Interfaces:**
- Consumes: `SetupEdit` and its fourteen kinds, `BoardMessage`, `Board` (Protocol); `TableState`, `TerrainPlacement`, `Marker`, `DeploymentZone` (Content); `IGameRows.WriteDraftAsync`.
- Produces: `static class DraftEditor { static TableState? Apply(TableState draft, SetupEdit edit, out string? missing); static TableState DropOffSlab(TableState draft); static bool OnSlab(TableState draft, Point3 position); }`; `GameSession.HandleSetupEditAsync(ISessionClient from, Guid editId, SetupEdit edit)`.
- Zones are rows per **shape**, keyed by team ordinal: `SetZoneStyle(style, shapes)` writes one `DeploymentZone` per shape with ordinals `1..n` whether or not a team with that ordinal exists yet, so "zones re-derive when the team list changes" is a join at render time and the server keeps no style state. `SwapZones(a, b)` swaps the two ordinals' shapes. Spec correction 4 at the end records this reading.
- The table's coordinate origin is its corner: a position is on the slab when `0 ≤ x ≤ width` and `0 ≤ y ≤ length` (the contracts plan's samples place terrain at `(12, 12)` on a 72×48 table). No spec states the origin; spec correction 3 proposes it, and the compatibility review of 2026-09-05 ruled it the one frame — the geometry plan's `Tabletop.Geometry.Table.ContainsPoint(TableSize, Point2)` (its Task 13, added by its spec correction 4) is the same rule, so `OnSlab` may delegate to it once that plan has landed; it is written out here because nothing in the contracts plan produces `Table` and this plan must build against the libraries as the contracts plan leaves them.

Spec: setup phase → Editing is free for all (the closed set; arrival order; no per-team permission), The draft is the row (apply, broadcast, then write; one write per drop), Layouts (loading replaces the whole draft, size included; the layout wins), Zones (style creates zones in ordinal order; anyone may swap), Terrain (swapping the mesh keeps the volumes and declarations; overriding is one of the edits), Terrain generators (pieces off the slab are dropped when setup ends — `DropOffSlab`, used by Task 12); client shell → The connection and the projection (a setup edit replaces the draft); contracts plan Task 11 and spec correction 7 (`SetZoneStyle` carries shape ids).

- [ ] **Step 1: Write the failing editor tests**

```csharp
using Tabletop.Content;
using Tabletop.Content.Tests;
using Tabletop.GameServer.Setup;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

public class DraftEditorTests
{
    static readonly TableState Empty = new(Guid.CreateVersion7(), null, new TableSize(72, 48), VisualRef.OfBuiltin("grass"), [], [], []);
    static TableState Apply(TableState d, SetupEdit e) { var r = DraftEditor.Apply(d, e, out var missing); Assert.Null(missing); return r!; }
    static void RefusedEdit(TableState d, SetupEdit e) { Assert.Null(DraftEditor.Apply(d, e, out var missing)); Assert.NotNull(missing); }
    static TerrainPlacement Hill(double x, double y) => TableStateTests.Sample().Terrain[0] with { Id = Guid.CreateVersion7(), Position = new Point3(x, y, 0) };

    [Fact]
    public void Size_and_texture()
    {
        var d = Apply(Apply(Empty, new SetTableSize(new TableSize(48, 48))), new SetTexture(VisualRef.OfBuiltin("desert")));
        Assert.Equal((48.0, "desert"), (d.Size.Width, d.Texture.Builtin));
    }

    [Fact]
    public void Terrain_add_move_scale_override_swap_remove()
    {
        var hill = Hill(10, 10);
        var d = Apply(Empty, new AddTerrain(hill));
        RefusedEdit(d, new AddTerrain(hill));                                            // same id twice
        d = Apply(d, new MoveTerrain(hill.Id, new Point3(20, 20, 0), new Yaw(90)));
        d = Apply(d, new ScaleTerrain(hill.Id, 2));
        var overridden = hill.Volumes[0] with { Volume = hill.Volumes[0].Volume with { Declarations = TerrainDeclarations.Impassable } };
        d = Apply(d, new OverrideTerrainVolumes(hill.Id, [overridden]));
        d = Apply(d, new SwapTerrainMesh(hill.Id, VisualRef.OfBuiltin("ruin")));
        var t = d.Terrain.Single();
        Assert.Equal((20.0, 90.0, 2.0, "ruin", TerrainDeclarations.Impassable), (t.Position.X, t.Yaw.Degrees, t.Scale, t.Mesh.Builtin, t.Volumes[0].Volume.Declarations));
        Assert.Empty(Apply(d, new RemoveTerrain(hill.Id)).Terrain);
        RefusedEdit(d, new MoveTerrain(Guid.CreateVersion7(), new Point3(0, 0, 0), new Yaw(0)));
        RefusedEdit(d, new RemoveTerrain(Guid.CreateVersion7()));
    }

    [Fact]
    public void Markers_add_move_remove()
    {
        var m = new Marker(Guid.CreateVersion7(), MarkerKind.Objective, new Point3(36, 24, 0), new Yaw(0), 3, null, VisualRef.OfBuiltin("objective-ring"));
        var d = Apply(Empty, new AddSetupMarker(m));
        d = Apply(d, new MoveSetupMarker(m.Id, new Point3(30, 24, 0), new Yaw(0)));
        Assert.Equal(30, d.Markers.Single().Position.X);
        Assert.Empty(Apply(d, new RemoveSetupMarker(m.Id)).Markers);
        RefusedEdit(d, new MoveSetupMarker(Guid.CreateVersion7(), new Point3(0, 0, 0), new Yaw(0)));
    }

    [Fact]
    public void Zones_follow_the_style_by_ordinal_and_swap()
    {
        var d = Apply(Empty, new SetZoneStyle("long-edges", ["long-edges/1", "long-edges/2", "long-edges/3"]));
        Assert.Equal([(1, "long-edges/1"), (2, "long-edges/2"), (3, "long-edges/3")], d.Zones.Select(z => (z.TeamOrdinal, z.Shape)));
        d = Apply(d, new SwapZones(1, 2));
        Assert.Equal([(1, "long-edges/2"), (2, "long-edges/1"), (3, "long-edges/3")], d.Zones.Select(z => (z.TeamOrdinal, z.Shape)));
        RefusedEdit(d, new SwapZones(1, 9));
        Assert.Equal(2, Apply(d, new SetZoneStyle("corners", ["corners/1", "corners/2"])).Zones.Count);
    }

    [Fact]
    public void Replacing_the_draft_takes_the_layout_whole_but_keeps_the_games_row_id()
    {
        var layout = TableStateTests.Sample();
        var d = Apply(Empty, new ReplaceDraft(layout));
        Assert.Equal(Empty.Id, d.Id);
        Assert.Equal(layout.Size, d.Size);
        Assert.Equal(layout.Terrain.Count + layout.Markers.Count + layout.Zones.Count, d.Terrain.Count + d.Markers.Count + d.Zones.Count);
    }

    [Fact]
    public void Off_slab_terrain_is_dropped_by_the_corner_origin_rule()
    {
        var d = Apply(Apply(Apply(Empty, new AddTerrain(Hill(10, 10))), new AddTerrain(Hill(80, 10))), new AddTerrain(Hill(72, -1)));
        Assert.True(DraftEditor.OnSlab(d, new Point3(72, 48, 0)));
        Assert.False(DraftEditor.OnSlab(d, new Point3(72.5, 48, 0)));
        Assert.Single(DraftEditor.DropOffSlab(d).Terrain);
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `DraftEditor.cs`**

```csharp
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Setup;

/// <summary>TableState × SetupEdit → TableState. Pure, so the setup phase is headless logic: edits in, draft out. Null with a reason when the edit names nothing.</summary>
public static class DraftEditor
{
    public static TableState? Apply(TableState d, SetupEdit edit, out string? missing)
    {
        missing = null;
        switch (edit)
        {
            case SetTableSize s: return d with { Size = s.Size };
            case SetTexture t: return d with { Texture = t.Texture };
            case AddTerrain a: return d.Terrain.Any(t => t.Id == a.Placement.Id) ? Miss(out missing, $"terrain {a.Placement.Id} already placed") : d with { Terrain = [.. d.Terrain, a.Placement] };
            case MoveTerrain m: return Terrain(d, m.PlacementId, t => t with { Position = m.Position, Yaw = m.Yaw }, out missing);
            case ScaleTerrain s: return Terrain(d, s.PlacementId, t => t with { Scale = s.Scale }, out missing);
            case OverrideTerrainVolumes o: return Terrain(d, o.PlacementId, t => t with { Volumes = o.Volumes }, out missing);
            case SwapTerrainMesh s: return Terrain(d, s.PlacementId, t => t with { Mesh = s.Mesh }, out missing);
            case RemoveTerrain r: return d.Terrain.Any(t => t.Id == r.PlacementId) ? d with { Terrain = d.Terrain.Where(t => t.Id != r.PlacementId).ToList() } : Miss(out missing, $"no terrain {r.PlacementId}");
            case AddSetupMarker a: return d.Markers.Any(m => m.Id == a.Marker.Id) ? Miss(out missing, $"marker {a.Marker.Id} already placed") : d with { Markers = [.. d.Markers, a.Marker] };
            case MoveSetupMarker m: return d.Markers.Any(x => x.Id == m.MarkerId) ? d with { Markers = d.Markers.Select(x => x.Id == m.MarkerId ? x with { Position = m.Position, Yaw = m.Yaw } : x).ToList() } : Miss(out missing, $"no marker {m.MarkerId}");
            case RemoveSetupMarker r: return d.Markers.Any(x => x.Id == r.MarkerId) ? d with { Markers = d.Markers.Where(x => x.Id != r.MarkerId).ToList() } : Miss(out missing, $"no marker {r.MarkerId}");
            case SetZoneStyle z: return d with { Zones = z.Shapes.Select((shape, i) => new DeploymentZone(Guid.CreateVersion7(), shape, i + 1)).ToList() };
            case SwapZones s:
                var a = d.Zones.FirstOrDefault(z => z.TeamOrdinal == s.TeamOrdinalA); var b = d.Zones.FirstOrDefault(z => z.TeamOrdinal == s.TeamOrdinalB);
                if (a is null || b is null) return Miss(out missing, "no zone for one of the ordinals");
                return d with { Zones = d.Zones.Select(z => z.TeamOrdinal == a.TeamOrdinal ? z with { Shape = b.Shape } : z.TeamOrdinal == b.TeamOrdinal ? z with { Shape = a.Shape } : z).ToList() };
            case ReplaceDraft r: return r.Draft with { Id = d.Id, Name = d.Name };
            default: return Miss(out missing, $"unknown edit {edit.GetType().Name}");
        }
    }

    /// <summary>The corner is the origin: a position is on the slab when 0 ≤ x ≤ width and 0 ≤ y ≤ length.</summary>
    public static bool OnSlab(TableState d, Point3 p) => p.X >= 0 && p.X <= d.Size.Width && p.Y >= 0 && p.Y <= d.Size.Length;

    /// <summary>End-setup's rule: a generated piece never dragged onto the slab is dropped (setup phase → Terrain generators; rulebook p.17, the Random style: "if it's impossible to place the terrain piece, simply remove it").</summary>
    public static TableState DropOffSlab(TableState d) => d with { Terrain = d.Terrain.Where(t => OnSlab(d, t.Position)).ToList() };

    static TableState? Terrain(TableState d, Guid id, Func<TerrainPlacement, TerrainPlacement> change, out string? missing)
    {
        missing = null;
        return d.Terrain.Any(t => t.Id == id) ? d with { Terrain = d.Terrain.Select(t => t.Id == id ? change(t) : t).ToList() } : Miss(out missing, $"no terrain {id}");
    }

    static TableState? Miss(out string? missing, string detail) { missing = detail; return null; }
}
```

- [ ] **Step 3: Write the failing end-to-end setup test**

```csharp
using Tabletop.Content;
using Tabletop.Content.Tests;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

[Collection("Postgres")]
public class SetupEditTests(PostgresFixture pg)
{
    static EditMessage Edit(Edit e) => new(Guid.CreateVersion7(), e);

    async Task<(GameServerFactory F, SeededGame G, TestClient Creator, TestClient Watcher)> InSetupAsync()
    {
        var factory = new GameServerFactory();
        var seeded = await pg.SeedGameAsync(GameStatus.Lobby, teams: 2, spectators: 1);
        var creator = await TestClient.OpenAsync(factory); var watcher = await TestClient.OpenAsync(factory);
        await creator.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await watcher.ConnectAsync(seeded.Participants[2].AccountId, seeded.GameId);
        await creator.SendAsync(Edit(new StartSetup()));
        await creator.ExpectAsync<BoardMessage>(); await watcher.ExpectAsync<BoardMessage>();
        return (factory, seeded, creator, watcher);
    }

    [Fact]
    public async Task Anyone_edits_everyone_sees_the_whole_draft_and_the_row_follows()
    {
        var (f, g, creator, watcher) = await InSetupAsync();
        var hill = TableStateTests.Sample().Terrain[0] with { Id = Guid.CreateVersion7(), Position = new Point3(10, 10, 0) };
        await watcher.SendAsync(Edit(new AddTerrain(hill)));                       // a participant with no team edits freely
        var board = (await creator.ExpectAsync<BoardMessage>()).Board;
        Assert.Equal(hill.Id, board.Table.Terrain.Single().Id);
        Assert.Equal(hill.Id, (await watcher.ExpectAsync<BoardMessage>()).Board.Table.Terrain.Single().Id);
        await Task.Delay(200);
        Assert.Equal(1, await pg.CountAsync("select count(*) from management.terrain_placement where id = $1", hill.Id));
        Assert.Equal(1, await pg.CountAsync("select count(*) from management.terrain_volume where terrain_placement_id = $1", hill.Id));

        await creator.SendAsync(Edit(new SetZoneStyle("long-edges", ["long-edges/1", "long-edges/2"])));
        Assert.Equal(2, (await watcher.ExpectAsync<BoardMessage>()).Board.Table.Zones.Count);
        await creator.ExpectAsync<BoardMessage>();
        await watcher.SendAsync(Edit(new SwapZones(1, 2)));
        Assert.Equal("long-edges/2", (await creator.ExpectAsync<BoardMessage>()).Board.Table.Zones.Single(z => z.TeamOrdinal == 1).Shape);

        await watcher.SendAsync(Edit(new MoveTerrain(Guid.CreateVersion7(), new Point3(0, 0, 0), new Yaw(0))));
        Assert.Equal(RefusalReason.Unknown, (await watcher.ExpectAsync<Refused>()).Reason);
    }

    [Fact]
    public async Task Loading_a_layout_replaces_the_draft_size_included()
    {
        var (f, g, creator, watcher) = await InSetupAsync();
        var layout = TableStateTests.Sample() with { Size = new TableSize(44, 30) };
        await creator.SendAsync(Edit(new ReplaceDraft(layout)));
        var table = (await watcher.ExpectAsync<BoardMessage>()).Board.Table;
        Assert.Equal((44.0, layout.Terrain.Count, layout.Markers.Count, layout.Zones.Count), (table.Size.Width, table.Terrain.Count, table.Markers.Count, table.Zones.Count));
        Assert.NotEqual(layout.Id, table.Id);
        await Task.Delay(200);
        Assert.Equal(1, await pg.CountAsync("select count(*) from management.table_state where game_id = $1 and width = 44", g.GameId));
        Assert.Equal(layout.Markers.Count, await pg.CountAsync("select count(*) from management.marker m join management.table_state t on t.id = m.table_state_id where t.game_id = $1", g.GameId));
    }

    [Fact]
    public async Task Setup_edits_are_refused_outside_setup()
    {
        using var factory = new GameServerFactory();
        var seeded = await pg.SeedGameAsync(GameStatus.Lobby);
        await using var client = await TestClient.OpenAsync(factory);
        await client.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await client.SendAsync(Edit(new SetTableSize(new TableSize(1, 1))));
        Assert.Equal(RefusalReason.WrongStatus, (await client.ExpectAsync<Refused>()).Reason);
    }
}
```

- [ ] **Step 4: Write `GameSession.Setup.cs` and delete the `HandleSetupEditAsync` placeholder in `GameSession.Lobby.cs`**

```csharp
using Tabletop.GameServer.Setup;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Session;

public sealed partial class GameSession
{
    /// <summary>Apply in arrival order, broadcast the whole draft, then write the row. Nothing waits on the database (setup phase → The draft is the row).</summary>
    private async Task HandleSetupEditAsync(ISessionClient from, Guid editId, SetupEdit edit)
    {
        if (Info.Status != GameStatus.Setup || Draft is null) { await RefuseEdit(from, editId, RefusalReason.WrongStatus, $"the game is {Info.Status}"); return; }
        var next = DraftEditor.Apply(Draft, edit, out var missing);
        if (next is null) { await RefuseEdit(from, editId, RefusalReason.Unknown, missing!); return; }
        Draft = next;
        await BroadcastAsync(new BoardMessage(new Board(next, [])));
        try { await _services.Rows.WriteDraftAsync(GameId, next, CancellationToken.None); }
        catch (Exception ex) { _log.LogError(ex, "Game {GameId}: the draft row write failed", GameId); await FaultAsync("the draft could not be saved"); }
    }
}
```

A failed row write faults the session: memory and row would otherwise disagree, and the next hydrate is what shows the truth. Clients reconnect and see the row.

- [ ] **Step 5: Run `dotnet test tests/Tabletop.GameServer.Tests`; expect 131 passed. Commit: `Apply setup edits to the draft, broadcast the board and write the row`.**

### Task 12: Ending setup — the snapshot, the status flip, the log opens

**Files:**
- Create: `server/game/Session/GameSession.EndSetup.cs`
- Modify: `server/game/Persistence/PostgresGameRows.Writes.cs` (`EndSetupAsync`), `server/game/Session/GameSession.Lobby.cs` (remove the `HandleEndSetupAsync` placeholder)
- Test: `tests/Tabletop.GameServer.Tests/EndSetupTests.cs`

**Interfaces:**
- Consumes: `DraftEditor.DropOffSlab`, `IGameRows.ReadArmyAsync`, `IGameRows.EndSetupAsync`, `IRulesEngineFactory`, `GameState`, `EventApplier`, `ArmySnapshot.Of`, `ArmyInPlay`, `Board`.
- Produces: `PostgresGameRows.EndSetupAsync` (one transaction: rewrite the tree, set every participant's `army_snapshot` from the dictionary — null stays null — flip `status` from `setup` to `playing`); `GameSession.HandleEndSetupAsync(ISessionClient from, Guid editId)` — after the commit: `Draft = null`, `Play = new GameState(...)` over `new Board(snapshot, armies)` and a fresh `EventApplier`, `Engine = Engines.Create(Rules, Info)`, `Dispatcher = new(Engine)`, broadcast `GameInfoMessage`, `BoardMessage`, an empty `EventsMessage`, then `AdvanceAsync()`, then `Rest()`.

Spec: setup phase → The lifecycle (setup to playing: off-slab terrain rows deleted, each participant's `army_snapshot` written from their army as it stands, the draft becomes the snapshot by the status flip, the log opens with it as initial state, the scheduler begins deployment — here, `Advance`; this is where the scheduler shape, the team list, the armies and the layout freeze; creator only; no way back); `architecture.md` → Game management data model (an army snapshot is a frozen copy written once when setup ends; a participant who joins after that has none); `schema.md` → `participant.army_snapshot` is `jsonb` and opaque (`ContentJson.Serialize(ArmySnapshot)`); setup content → The lock is structural.

- [ ] **Step 1: Write the failing test**

```csharp
using Tabletop.Content.Tests;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Microsoft.Extensions.DependencyInjection;

namespace Tabletop.GameServer.Tests;

[Collection("Postgres")]
public class EndSetupTests(PostgresFixture pg)
{
    static EditMessage Edit(Edit e) => new(Guid.CreateVersion7(), e);
    static readonly Ask Deploy = new(Guid.CreateVersion7(), AskQuestions.Deploy, "Deploy", [], new PieceSelection([], 1, 1), false, false, null, null);

    [Fact]
    public async Task Ending_setup_freezes_armies_drops_off_slab_terrain_flips_status_opens_the_log_and_advances()
    {
        using var factory = new GameServerFactory { ConfigureTestServices = s => s.AddSingleton<IRulesEngineFactory>(new FixedAskEngineFactory(Deploy)) };
        var seeded = await pg.SeedGameAsync(GameStatus.Lobby, teams: 2);
        var army = await pg.SeedArmyAsync(seeded.CreatorAccountId);
        await using var creator = await TestClient.OpenAsync(factory); await using var other = await TestClient.OpenAsync(factory);
        await creator.ConnectAsync(seeded.CreatorAccountId, seeded.GameId); await other.ConnectAsync(seeded.Participants[1].AccountId, seeded.GameId);
        await creator.SendAsync(Edit(new PickArmy(army.Id))); await creator.ExpectAsync<GameInfoMessage>(); await other.ExpectAsync<GameInfoMessage>();

        await other.SendAsync(Edit(new EndSetup()));
        Assert.Equal(RefusalReason.WrongStatus, (await other.ExpectAsync<Refused>()).Reason);       // still lobby
        await creator.SendAsync(Edit(new StartSetup())); await creator.ExpectAsync<BoardMessage>(); await other.ExpectAsync<BoardMessage>();
        var on = TableStateTests.Sample().Terrain[0] with { Id = Guid.CreateVersion7(), Position = new Point3(10, 10, 0) };
        var off = TableStateTests.Sample().Terrain[0] with { Id = Guid.CreateVersion7(), Position = new Point3(-6, 10, 0) };
        await creator.SendAsync(Edit(new AddTerrain(on))); await creator.SendAsync(Edit(new AddTerrain(off)));
        await creator.ExpectAsync<BoardMessage>(); await creator.ExpectAsync<BoardMessage>(); await other.ExpectAsync<BoardMessage>(); await other.ExpectAsync<BoardMessage>();

        await other.SendAsync(Edit(new EndSetup()));
        Assert.Equal(RefusalReason.NotPermitted, (await other.ExpectAsync<Refused>()).Reason);
        await creator.SendAsync(Edit(new EndSetup()));
        Assert.Equal(GameStatus.Playing, (await other.ExpectAsync<GameInfoMessage>()).Game.Status);
        var board = (await other.ExpectAsync<BoardMessage>()).Board;
        Assert.Equal([on.Id], board.Table.Terrain.Select(t => t.Id));
        var inPlay = Assert.Single(board.Armies);
        Assert.Equal((seeded.Participants[0].ParticipantId, army.Name, army.Units.Count), (inPlay.ParticipantId, inPlay.Snapshot.Name, inPlay.Snapshot.Units.Count));
        Assert.Empty((await other.ExpectAsync<EventsMessage>()).Events);
        Assert.IsType<AskOpened>((await other.ExpectAsync<EventsMessage>()).Events.Single().Payload);   // the engine advanced after the flip
        Assert.Equal(1L, (await other.ExpectAsync<AtRest>()).Seq);                                     // and the transition ends at rest, on the opened log

        Assert.Equal(1, await pg.CountAsync("select count(*) from management.game where id = $1 and status = 'playing'", seeded.GameId));
        Assert.Equal(1, await pg.CountAsync("select count(*) from management.participant where game_id = $1 and army_snapshot is not null", seeded.GameId));
        Assert.Equal(0, await pg.CountAsync("select count(*) from management.terrain_placement where id = $1", off.Id));

        await creator.SendAsync(Edit(new SetTableSize(new TableSize(1, 1))));
        Assert.Equal(RefusalReason.WrongStatus, (await creator.ExpectAsync<Refused>()).Reason);         // no way back
        await creator.SendAsync(new CommandMessage(Guid.CreateVersion7(), new SetScore(seeded.TeamIds[0], 1)));
        Assert.Equal(2, (await creator.ExpectAsync<EventsMessage>()).Events.Single().Seq);
    }

    [Fact]
    public async Task A_fresh_process_hydrates_the_played_game_from_rows_and_log()
    {
        var seeded = await pg.SeedGameAsync(GameStatus.Lobby, teams: 2);
        var army = await pg.SeedArmyAsync(seeded.CreatorAccountId);
        using (var first = new GameServerFactory())
        {
            await using var c = await TestClient.OpenAsync(first);
            await c.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
            await c.SendAsync(Edit(new PickArmy(army.Id))); await c.ExpectAsync<GameInfoMessage>();
            await c.SendAsync(Edit(new StartSetup())); await c.ExpectAsync<BoardMessage>();
            await c.SendAsync(Edit(new EndSetup())); await c.ExpectAsync<BoardMessage>(); await c.ExpectAsync<EventsMessage>();
            await c.SendAsync(new CommandMessage(Guid.CreateVersion7(), new MoveModels([new ModelRelocation(army.Units[0].Models[0].Id, new OnShelf(), new OnTable(new Placement(new Point3(12, 12, 0), new Yaw(0))))])));
            await c.ExpectAsync<EventsMessage>();
        }
        using var second = new GameServerFactory();
        await using var again = await TestClient.OpenAsync(second);
        var welcome = await again.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        Assert.Equal(GameStatus.Playing, welcome.Game.Status);
        Assert.Single((await again.ExpectAsync<BoardMessage>()).Board.Armies);
        var moved = Assert.IsType<ModelsMoved>((await again.ExpectAsync<EventsMessage>()).Events.Single().Payload);
        Assert.IsType<OnTable>(moved.Moves.Single().To);
    }
}
```

- [ ] **Step 2: Run; expect refusals (`end setup is not handled yet`). Add `EndSetupAsync` to `PostgresGameRows.Writes.cs`**

```csharp
    public async Task EndSetupAsync(Guid gameId, TableState snapshot, IReadOnlyDictionary<Guid, ArmySnapshot?> snapshotsByParticipant, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using (var cmd = new NpgsqlCommand("update management.table_state set width = $3, length = $4, texture_builtin = $5, texture_asset_id = $6 where id = $1 and game_id = $2", conn, tx))
        {
            cmd.Parameters.AddWithValue(snapshot.Id); cmd.Parameters.AddWithValue(gameId);
            cmd.Parameters.AddWithValue((float)snapshot.Size.Width); cmd.Parameters.AddWithValue((float)snapshot.Size.Length);
            cmd.Parameters.AddWithValue((object?)snapshot.Texture.Builtin ?? DBNull.Value); cmd.Parameters.AddWithValue((object?)snapshot.Texture.AssetId ?? DBNull.Value);
            if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("no draft row for this game");
        }
        await WriteTreeAsync(conn, tx, snapshot, ct);
        foreach (var (participantId, army) in snapshotsByParticipant)
        {
            await using var cmd = new NpgsqlCommand("update management.participant set army_snapshot = $2 where id = $1 and game_id = $3", conn, tx);
            cmd.Parameters.AddWithValue(participantId);
            cmd.Parameters.Add(new NpgsqlParameter { Value = army is null ? DBNull.Value : ContentJson.Serialize(army), NpgsqlDbType = NpgsqlDbType.Jsonb });
            cmd.Parameters.AddWithValue(gameId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await using (var cmd = new NpgsqlCommand("update management.game set status = 'playing' where id = $1 and status = 'setup'", conn, tx) { Parameters = { new NpgsqlParameter { Value = gameId } } })
            if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("game is not in setup");
        await tx.CommitAsync(ct);
    }
```

- [ ] **Step 3: Write `GameSession.EndSetup.cs` and delete the placeholder in `GameSession.Lobby.cs`**

```csharp
using Tabletop.Content;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Setup;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Session;

public sealed partial class GameSession
{
    /// <summary>Setup → playing. Off-slab terrain dropped, every picked army snapshotted as it stands, the status flipped, the draft thereby the snapshot, the log opened on it, the engine advanced.</summary>
    private async Task HandleEndSetupAsync(ISessionClient from, Guid editId)
    {
        if (Info.Status != GameStatus.Setup || Draft is null) { await RefuseEdit(from, editId, RefusalReason.WrongStatus, $"the game is {Info.Status}"); return; }
        if (!IsCreator(from)) { await RefuseEdit(from, editId, RefusalReason.NotPermitted, "only the creator ends setup"); return; }
        var rows = _services.Rows; var ct = CancellationToken.None;

        var snapshot = DraftEditor.DropOffSlab(Draft);
        var armies = new Dictionary<Guid, ArmySnapshot?>();
        foreach (var p in Info.Participants)
        {
            if (p.ArmyId is not { } armyId) { armies[p.Id] = null; continue; }
            var owned = await rows.ReadArmyAsync(armyId, ct);
            armies[p.Id] = owned is null ? null : ArmySnapshot.Of(owned.Army, Guid.CreateVersion7());
        }
        await rows.EndSetupAsync(GameId, snapshot, armies, ct);

        Draft = null;
        Info = await rows.ReadGameAsync(GameId, ct) ?? throw new GameNotFoundException(GameId);
        var board = new Board(snapshot, armies.Where(kv => kv.Value is not null).Select(kv => new ArmyInPlay(kv.Key, kv.Value!)).ToList());
        Play = new GameState(Info, board, new EventApplier(board, Info), Rules);
        Engine = _services.Engines.Create(Rules, Info);
        Dispatcher = new CommandDispatcher(Engine);
        await BroadcastAsync(new GameInfoMessage(Info));
        await BroadcastAsync(new BoardMessage(board));
        await BroadcastAsync(new EventsMessage([]));
        await AdvanceAsync();
        await BroadcastAsync(Rest());   // the log has opened and the scheduler's first events are out: the transition ends at rest
    }
}
```

A participant on a team with no army picked freezes with `army_snapshot` null and contributes nothing to any legal set — the scheduler skips a team with an empty legal set (scheduler → The turn is an Ask). A participant with a deleted army (`ReadArmyAsync` null) is treated the same.

The flip freezes the team list along with the scheduler shape, the armies and the layout (owner ruling 1, 2026-09-06). `HandleLobbyEditAsync` refuses `CreateTeam` and `PickTeam` with `WrongStatus` from here on, so no `team` row and no `participant.team_id` changes for the rest of the game, and `Projection.Scores` needs no entry beyond the ones `Projection.Initial` built from the frozen list. The freeze does not touch connecting: a participant's `team_id` outlives their socket, so reconnecting during play returns them to the same team, and nothing here needs to distinguish that from a first connect.

- [ ] **Step 4: Run `dotnet test tests/Tabletop.GameServer.Tests`; expect 133 passed. Commit: `End setup: snapshot the armies, flip the status, open the log`.**

### Task 13: `CLAUDE.md`, running the server, and the whole-tree check

**Files:**
- Modify: `CLAUDE.md` (Commands, Layout, Environment), `.env.example` (one comment line)

**Interfaces:**
- Consumes: everything Tasks 1–12 produced, as the facts `CLAUDE.md` now records. Every edit is to a line this plan owns or an append, so no other plan need have landed.
- Produces: no code. Step 2's whole-tree check is this plan's exit gate.

- [ ] **Step 1: Edit `CLAUDE.md` and `.env.example`**

**Ownership rule for `CLAUDE.md`** (2026-09-09, for parallel execution): a plan edits only the lines it owns — its own Layout bullet, its own appended Commands lines, its own appended Conventions or Environment text — and never the opening paragraph, the closing note, or another plan's bullet. No *Before* quote below is another plan's text. Match each anchor by its fixed prefix, not by exact wording.

In **Commands**, append at the end of the block:

```bash
dotnet run --project server/game   # the game server on http://0.0.0.0:5200, /ws; reads .env (TABLETOP_DB_CONNECTION, TABLETOP_STYTCH_PROJECT_ID)
```

In **Layout**, replace the `server/game/` bullet (the line beginning "- `server/game/` —", currently the scaffold's stub) with a parent whose children later plans append to and never rewrite:

```
- `server/game/` — the WebSocket game server (`Tabletop.GameServer`, `net10.0`): the connect handshake and Stytch JWT
  verification, a `GameSession` per game hydrated from management's rows plus `game.game_event`, the append-only log and
  replay, `CommandDispatcher` over the `IRulesEngine` seam, the lobby and setup row writes. Tested in
  `tests/Tabletop.GameServer.Tests`. What sits behind the rules seam is listed by the children below; with none, the
  engine is `InertEngine`.
```

The interpreter and scheduler plans each append one child bullet under it and touch the parent's text no further.

In **Environment**, append: "`.env` needs `TABLETOP_STYTCH_PROJECT_ID` (the test project id from `docs/handoff.md`, in `.env.example`); the game server derives the JWKS URL from it and `TABLETOP_STYTCH_BASE_URL`. Tests in `tests/Tabletop.GameServer.Tests` marked `[Collection(\"Postgres\")]` need the Docker Postgres up."

In `.env.example`, append at the end of the file (not at an anchor — the management plan adds the Stytch lines and may or may not have landed): `# TABLETOP_STYTCH_JWKS_URL= (derived from TABLETOP_STYTCH_BASE_URL and the project id when unset)`.

- [ ] **Step 2: Run the whole check**

```bash
cd /home/cstupi/src/tabletop
docker compose up -d && dotnet build && dotnet test
find server tests -path '*/obj/*' -maxdepth 3 -type d | grep -v Unix
(dotnet run --project server/game &) ; sleep 8; curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5200/ws; kill %1 2>/dev/null
git status --short
```

Expected: build clean; every test green (Geometry, Content, Protocol from the contracts plan, Management from its plan, plus 133 here); the `find` prints nothing; `curl` prints `400` (a plain GET on the WebSocket endpoint). From Windows PowerShell, `Test-NetConnection localhost -Port 5200` reports `TcpTestSucceeded : True` while the server runs, which is the client shell plan's connection target.

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md .env.example
git commit -m "Record the game server skeleton in CLAUDE.md and the environment"
```

---

## Interfaces → Produces, per namespace

Later plans copy these names verbatim. The scheduler and interpreter plans **extend** `IGameRules` and **implement** `IRulesEngine`; nothing below is renamed or reshaped by them.

### `Tabletop.GameServer.Rules`

| Type | Members |
| --- | --- |
| `interface IGameRules` | `string Id` — the hook surface; later plans add members |
| `sealed partial class GrimdarkFutureRules : IGameRules` (namespace `Tabletop.GameServer.Rules.GrimdarkFuture`, `Rules/GrimdarkFuture/GfRules.cs`) | `const string RulesId = "grimdark-future"`, `static GrimdarkFutureRules Instance`, `Id` — declared once, here; the scheduler plan adds `GfRules.Scheduling.cs` and `GfRules.Deployment.cs`, the interpreter plan `GfRules.Sequences.cs`, and neither redeclares these three |
| `static class GameRulesRegistry` | `IGameRules Resolve(string id)` (throws `UnknownGameRulesException`) |

**`IGameRules` after both later plans have added their members** — the one hook surface, reconciled 2026-09-05 across the scheduler and interpreter plans. The scheduler plan adds the first block (Task 2), the interpreter plan the second (Task 8); each hook exists once, with this signature. `TurnAsk`, `Activation`, `TurnContext`, `HookResult`, `FirstTeamDecision` live in `Tabletop.GameServer.Scheduler` and are **declared by the interpreter plan's Task 8** (it runs first and references them; the scheduler plan reads them); `Sequence`, `FrameView` are the interpreter plan's (`Tabletop.GameServer.Interpreter`); `RollSpec`, `Reaction`, `SequenceStart`, `UnknownSequenceException` sit beside the interface in `Tabletop.GameServer.Rules`; `PropertySet` is `Tabletop.GameServer.Properties`.

```csharp
public interface IGameRules
{
    string Id { get; }

    // Scheduler plan, Task 2.
    IReadOnlyList<string> PhasesBeforeRounds { get; }                                                   // OPR: deployment, scouts
    IReadOnlyList<string> PhasesInTurn { get; }                                                         // OPR: none
    FirstTeamDecision FirstTeam(GameState state, int round, IReadOnlyList<Guid> teams);                 // OrderedTeams(order) | RollOffNeeded(sequence)
    TurnAsk? LegalTurn(GameState state, PropertySet properties, Guid teamId);                          // null: empty legal set; action rows = properties.Actions
    Activation? ReadAnswer(GameState state, Guid teamId, AskAnswer answer);                            // null: names nothing (a pass)
    bool ActivationComplete(GameState state, Activation activation);                                    // sequence-less activations (deploy)
    HookResult RoundBoundaryStep(GameState state, PropertySet properties, int round, IReadOnlyList<Guid> order);   // OPR: Ambushers
    IReadOnlyList<Guid>? OrderReactions(GameState state, Guid? actorTeamId, IReadOnlyList<Guid> reactingTeams);   // null: Windows.ReactionOrder's default

    // Interpreter plan, Task 8 (bodies in Task 11 and after).
    Sequence Sequence(string name);                                                                     // throws UnknownSequenceException
    string? ActionSequence(string actionName);                                                          // null: no sequence for this action
    RollSpec RollSpec(string roll);
    string HitRoll { get; }
    IReadOnlyList<Reaction> ReactionsAt(GameState state, PropertySet properties, string point, FrameView frame);
    IReadOnlyList<SequenceStart> EndOfTurnSequences(GameState state, PropertySet properties, TurnContext turn);   // OPR: end-of-activation morale
    IReadOnlyList<StateChange> OnRoundStarted(GameState state, PropertySet properties, int round);      // OPR: fatigue clears, tokens; round 0 → []
    IReadOnlyList<StateChange> OnTurnEnded(GameState state, PropertySet properties, TurnContext turn);  // OPR: wounds-this-activation to 0; Idle clears Shaken (an Aircraft's Advance too, p.13)
}
```
| `sealed record Actor(Guid ParticipantId, Guid? TeamId)` | |
| `sealed class GameState(GameInfo, Board, EventApplier, IGameRules)` | `GameInfo Info { get; set; }`, `Board Board`, `EventApplier Applier`, `Projection Projection`, `IGameRules Rules` — the interpreter plan adds `RngSeed` here, from `Info.RngSeed` (the stored column), which is that plan's change and not this one's |
| `abstract record EngineResult` | `Emit(IReadOnlyList<EventPayload> Events)`, `RefuseCommand(RefusalReason Reason, string Detail)` |
| `interface IRulesEngine` | `Legality Judge(GameState, Command, Actor)`, `EngineResult Execute(GameState, Command, Actor)`, `IReadOnlyList<EventPayload> Advance(GameState)` |
| `interface IRulesEngineFactory` | `IRulesEngine Create(IGameRules rules, GameInfo game)` — replace the `InertEngineFactory` registration |
| `sealed class InertEngine`, `sealed class InertEngineFactory` | the skeleton's implementation |

### `Tabletop.GameServer.Dispatch`

| Type | Members |
| --- | --- |
| `enum Legality { AlwaysAccepted, Legal, OutsideLegalSet, Illegal }` | |
| `abstract record Verdict` | `Accept(string? Warning)`, `Refuse(RefusalReason Reason, string Detail)` |
| `static class EnforcementPolicy` | `Verdict Decide(Enforcement, Legality, string detail)` |
| `static class LegalSetMembership` | `bool Contains(Ask ask, AskAnswer answer)` |
| `static class EventFilter` | `IReadOnlyList<GameEvent> ForViewer(IReadOnlyList<GameEvent>, Guid? viewerTeamId, IReadOnlySet<Guid> sealedOpenBefore)`, `IReadOnlySet<Guid> SealedOpenAsks(Projection)` |
| `static class MenuTranslation` | `EventPayload? Translate(GameState, Command, out string? missing)` |
| `enum CommandClass { Override, Undo, Abandon, Answer, Engine }` | |
| `abstract record DispatchOutcome` | `Applied(IReadOnlyList<EventPayload> Events, string? Warning)`, `Refused(RefusalReason Reason, string Detail)` |
| `sealed class CommandDispatcher(IRulesEngine)` | `static CommandClass Classify(Command)`, `DispatchOutcome Dispatch(GameState, Command, Actor)`, `IReadOnlyList<Command> EnumerateLegalCommands(GameState, Guid? teamId)` |

### `Tabletop.GameServer.Session`, `.Log`, `.Persistence`, `.Auth`, `.Setup`, `.Configuration`

| Type | Members |
| --- | --- |
| `interface ISessionClient` | `Guid ConnectionId`, `Guid ParticipantId`, `ValueTask SendAsync(SocketMessage)`, `ValueTask CloseAsync(WebSocketCloseStatus, string)` |
| `sealed class GameSession` | `Guid GameId`, `GameInfo Info`, `IGameRules Rules`, `TableState? Draft`, `GameState? Play`, `IRulesEngine? Engine`, `CommandDispatcher? Dispatcher`, `IReadOnlyList<ISessionClient> Clients`, `bool Faulted`, `Task StartAsync()`, `Task ConnectAsync(ISessionClient)`, `Task AttachAsync(ISessionClient)`, `Task DetachAsync(ISessionClient)`, `Task ReceiveAsync(ISessionClient, SocketMessage)`, `Task TickPresenceAsync()`, `Task<T> RunAsync<T>(Func<Task<T>>)`, `Task DrainAsync(string)`, `Task BroadcastAsync(SocketMessage, ISessionClient? except)`, `Guid? TeamOf(Guid participantId)`, `Actor ActorOf(ISessionClient)`; internal `AppendAsync(IReadOnlyList<EventPayload>, Guid? actor, string? warning)`, `AdvanceAsync()`, `AtRest Rest()`, `ReloadInfoAsync()`, `NewDraft()` |
| `sealed record SessionServices(IGameRows Rows, IEventLog Log, IRulesEngineFactory Engines, GameServerOptions Options, ILoggerFactory Logging)` | |
| `sealed class PresenceTable` | `Set(Presence)`, `Remove(Guid)`, `IReadOnlyList<Presence> TakeDirty()`, `IReadOnlyList<Presence> All` |
| `static class Hydrator` | `Task<GameSession> HydrateAsync(Guid gameId, SessionServices, CancellationToken)` (`GameNotFoundException`, `GameRowsInconsistentException`) |
| `sealed class SessionRegistry(SessionServices)` | `Task<GameSession> GetOrHydrateAsync(Guid, CancellationToken)`, `Task ReleaseIfEmptyAsync(GameSession)`, `IReadOnlyCollection<GameSession> Live`, `Task DrainAllAsync(string)`, `bool Draining` |
| `sealed class SocketConnection(WebSocket, Guid participantId, ILogger) : ISessionClient` | `Task RunSendLoopAsync(CancellationToken)`, `static Task<SocketMessage?> ReadFrameAsync(WebSocket, CancellationToken)` |
| `sealed class ConnectionHandshake(...)` | `Task RunAsync(WebSocket, CancellationToken)` |
| `sealed class SessionHost : BackgroundService` | the presence timer and the SIGTERM drain |
| `interface IEventLog` | `Task AppendAsync(Guid, IReadOnlyList<GameEvent>, CancellationToken)`, `Task<IReadOnlyList<GameEvent>> ReadAsync(Guid, long afterSeq, CancellationToken)`, `Task<long> LastSeqAsync(Guid, CancellationToken)`; `DuplicateSequenceException(Guid GameId, long Seq)` |
| `sealed class PostgresEventLog(NpgsqlDataSource)`, `sealed class InMemoryEventLog` | |
| `static class Replay` | `Projection To(Board, GameInfo, IReadOnlyList<GameEvent>, long seq)`, `bool IsValidUndoTarget(EventApplier, long toSeq)` |
| `interface IGameRows` | the six reads and seven writes in Task 3; `sealed record OwnedArmy(Army Army, Guid OwnerId)` |
| `sealed class PostgresGameRows(NpgsqlDataSource) : IGameRows`; `static class RowMapping` | `EnumText<T>`, `EnumFrom<T>`, `VolumeColumns`, `VolumeValues(Volume)`, `VolumeFrom(NpgsqlDataReader, int)` |
| `interface ISessionVerifier` | `Task<VerifiedSession?> VerifyAsync(string jwt, CancellationToken)`; `sealed record VerifiedSession(Guid AccountId, string StytchUserId, DateTimeOffset ExpiresAt)` |
| `interface IJwksSource`; `sealed class HttpJwksSource`; `sealed class StytchJwtVerifier` | `const string AccountClaim = "account_id"` |
| `static class DraftEditor` | `TableState? Apply(TableState, SetupEdit, out string? missing)`, `TableState DropOffSlab(TableState)`, `bool OnSlab(TableState, Point3)` |
| `sealed class GameServerOptions` | Task 1's members; `static FromEnvironment(IConfiguration)` |

### Test infrastructure (`tests/Tabletop.GameServer.Tests/Infrastructure`)

`PostgresFixture` (collection `Postgres`; `DefaultRngSeed`, `SeedGameAsync` — whose `rngSeed` parameter fills the no-default `management.game.rng_seed` — `SeedAccountAsync`, `SeedParticipantAsync`, `SeedPropertiesAsync`, `SeedArmyAsync`, `SeedTableStateAsync`, `CountAsync`), `GameServerFactory` (`ConfigureTestServices`, `ProjectId`), `TestKeys` (`Mint`, `Source`), `TestClient`, `RecordingClient`, `FixedAskEngine` / `FixedAskEngineFactory`. The scheduler and interpreter plans reuse these for their end-to-end tests.

---

## Spec corrections proposed

**Applied on 2026-09-08.** These were written into the documents they name, across five passes split
by target file so that no two passes wrote the same file. The list below is kept as the historical
record of what each correction asked for and why, so a *Before* quote here no longer matches the
document it quotes. `docs/handoff.md` → *The spec corrections are applied* records the exceptions:
the corrections that were already applied, the ones folded into a later correction, and the ones
skipped as superseded.

1. **`docs/superpowers/specs/2026-09-04-setup-phase-design.md` → Joining is allowed at any status; taking a team is not** (the heading as corrected), and **`2026-09-04-whole-design-review.md` → ruling 5**, and **`2026-09-04-scheduler-design.md` → Setup configuration is fixed once playing (the correction note)**. Current wording (setup phase): *"Teams are created, and participants moved between them, by lobby commands over the socket, at any status."* Replace with: *"Teams are created, and participants moved between them, by lobby commands over the socket while the game is `lobby` or `setup`. At the flip to `playing` the team list freezes with the scheduler shape, the armies and the layout, and the game server is read-only from there (ruling 4), so an account joining the game then has no team, brings no army, and watches. A participant who already has a team keeps it: reconnecting during play returns them to their slot, with no re-pick and nothing re-derived."* Ruling 5's *"teams are created by lobby commands at any status"* becomes *"while `lobby` or `setup`"*, and the scheduler's *"anyone may join an existing team at any time"* becomes *"anyone may join at any time; a team may be picked while `lobby` or `setup`, and a participant who has one rejoins it"*. **Ruled by the owner on 2026-09-06** (ruling 1), which settles the contradiction rather than proposing a way out of it: the specs are corrected in place by their owners, and the canonical statement is game flow → Teams are frozen at the flip, and a team is a slot. Reason for the original correction: the two sentences contradicted ruling 4, and every plan that touched this had to guess.
2. **`docs/superpowers/specs/2026-08-29-accounts-design.md` → Auth is a module, not a vendor** ("a Stytch session JWT carrying our account UUID … `external_id` is our GUID, so the token names the account in our own terms") and **`2026-08-29-management-api-design.md` → We issue no tokens of our own** ("Confirmed 2026-08-30: a Stytch session JWT can carry `external_id`, and if it ever cannot, it is added as a custom claim"). Replace the confirmation with: *"Stytch's session JWT puts its own `user_id` in `sub` and does not carry `external_id`; our account UUID arrives as the top-level custom claim `account_id`, rendered by the project's Stytch custom claim template from `{{ user.external_id }}` on every mint (Stytch reserves `sub` and the `https://stytch.com/*` names; custom claims are merged at the top level). The game server reads `account_id` and nothing else."* Reason: read from Stytch's custom-claims documentation and its Node SDK while planning; the verifier needs one exact claim name.
3. **`docs/superpowers/specs/2026-08-29-geometry-values-design.md` → Height, position and facing** (or **`2026-08-29-setup-content-design.md` → The table is a slab**). Add (the same wording as the geometry plan's spec correction 4, aligned 2026-09-05 by the compatibility review): *"**The frame** (settled 2026-09-05): the table's origin is a corner — `x` runs along the width from `0` to `width`, `y` along the length from `0` to `length`, `z` up from the slab's top face; a position is on the slab when `0 ≤ x ≤ width` and `0 ≤ y ≤ length`. Yaw 0 faces +Y and positive yaw turns counter-clockwise seen from above. An oval's length lies along the facing axis; a rect footprint's width along the piece's local X and its length along local Y."* Reason: no document states the origin, and end-setup's off-slab rule and the client's rendering both need it; the contracts plan's samples already place pieces this way, and `Tabletop.Geometry` (`Table`, `ZoneStyles`, `Movement`) now uses the same frame.
4. **`docs/superpowers/specs/2026-09-04-setup-phase-design.md` → Zones.** Current: *"Setting a zone style creates its zones and assigns them to team ordinals in order: team one gets zone one."* and (Joining) *"zones re-derive from the zone style whenever the team list changes."* Add: *"A `deployment_zone` row exists per shape the style has, keyed by team ordinal `1..n`, whether or not a team with that ordinal exists yet; the client draws the zones whose ordinal has a team. So a team created during setup finds its zone by ordinal and nothing re-derives; swapping two teams' zones swaps the two ordinals' shapes. A style with fewer shapes than there are teams leaves the later ordinals without a zone until a client sends `set_zone_style` again, which is what re-deriving means on a server that keeps no style state — the style name itself is never stored, only its expansion."*
5. **`docs/superpowers/specs/2026-09-04-setup-phase-design.md` → The lifecycle** ("The creator starts setup, which creates the game's `table_state` draft row"). Add: *"created with the server's configured default size and texture (72×48 and the built-in `grass` until an inventory exists); the first edit — typically a layout load — replaces them."* Reason: `table_state.width/length/texture` are non-null and `StartSetup` carries nothing.
6. **`docs/architecture.md` → Deployment**, the constraints table row *"ALB idle timeout drops long-idle sockets — App-level ping/pong heartbeat"*. Replace the resolution with: *"WebSocket ping/pong control frames from Kestrel (`KeepAliveInterval` 20 s, a socket that misses a pong for `KeepAliveTimeout` 60 s is closed); no protocol message kind is involved."* Reason: `Tabletop.Protocol` has no heartbeat frame and needs none.
7. **`docs/superpowers/plans/2026-09-05-contracts-and-local-dev.md` → Interfaces needed from other modules → Game server plan**, the clause *"own the `Properties` catalog types"*. Replace with *"the interpreter plan owns the `Properties` catalog types"*. Reason: the interpreter module prompt loads `Properties`; this skeleton never reads the eight tables.
8. **`docs/superpowers/specs/2026-08-30-presence-design.md` → Deliberately not decided**, the item *"How a client is told that a presence value has stopped, versus timing it out"* and *"Whether a participant can have two connections at once"*. Strike both with: *"Settled 2026-09-05 by the game server plan: the server sends `presence_ended` when the participant's last socket closes; a participant has one connection — a newer socket replaces the older, which is closed with `1000`."*

## Interfaces needed from other modules

1. **Management API plan**: the Stytch project carries a session custom claim template of exactly `{"account_id": "{{ user.external_id }}"}`, and management keeps every Stytch user's `external_id` set to its `Account.id`, so every session JWT the client hands the game server carries `account_id` at the top level as a GUID string. Agreed exact claim name: `StytchJwtVerifier.AccountClaim = "account_id"` — the template's JSON key is ours to choose, so it must stay spelled this way, and if the template is ever missing this server rejects every connection with no Stytch error to show for it (management plan → *Interfaces needed from other modules → Stytch dashboard*). Also: `POST /v1/games` inserts `game` with `status = 'lobby'`, `created_by`, the six configuration columns (`name`, `game_rules`, `properties_hash`, `scheduler_shape`, `enforcement`, `waits_between_steps`) and `rng_seed` — generated there, in application code, because the column is `bigint not null` with no default; this server reads that column at hydration and never writes it, and no management endpoint updates it (owner ruling 1, 2026-09-06); `POST /v1/games/{id}/join` inserts `participant (id, game_id, team_id = null, account_id, army_id = null, army_snapshot = null)`; `POST /v1/games/{id}/end` sets `status = 'ended'`, as does the last `POST /v1/games/{id}/leave`; `leave` deletes the participant row; `DELETE /v1/games/{id}` deletes the game's rows and its `game.game_event` log. Management never writes `status` to `setup` or `playing`, never writes `team`, `participant.team_id`, `participant.army_id`, `participant.army_snapshot`, or a game's `table_state` tree — those are this server's while `lobby`/`setup` (management plan → Rows by writer agrees column for column). `TABLETOP_STYTCH_PROJECT_ID` and `TABLETOP_STYTCH_BASE_URL` in `.env` are shared by both servers; management verifies the same way (its Task 4) and confirms the JWT is checked at connect only.
2. **Scheduler and interpreter plans**: one `IRulesEngine` implementation between them (the scheduler opens the turn Ask and reacts to `ask_closed`/`sequence_finished` in `Advance`; the interpreter handles `RerollDice`, `SetDice`, `AddModifier` in `Judge`/`Execute` and completes steps in `Advance`), registered by replacing `InertEngineFactory` as `IRulesEngineFactory`; members added to `IGameRules` and implemented on `GrimdarkFutureRules`; the RNG seed as a member the interpreter plan adds to `GameState`, read from `GameInfo.RngSeed` — the stored `management.game.rng_seed` this plan reads at hydration — rather than hashed from `Info.Id` (owner ruling 1, 2026-09-06). The dispatcher already emits `ask_answered`/`ask_closed` and closes a popped frame's Asks before `sequence_abandoned`; the engine must not emit those for answers (it reads `ask_closed`) — the one exception is the scheduler's `ask_closed` with an empty answer list for one of its own unframed Asks superseded by a menu edit (`set_active_team`, `set_round`). The engine must not append or broadcast: it returns payloads. **Reconciled 2026-09-05:** the one implementation is the interpreter plan's `Tabletop.GameServer.Engine.RulesEngine` (Task 10), registered by its `RulesEngineFactory`; the scheduler plugs in as `Engine.IScheduler` through `Engine.ISchedulerFactory` (`Create(IGameRules, GameInfo, PropertySet)`), registered in place of `NoSchedulerFactory`. The interpreter plan also modifies this plan's files: `ConnectionHandshake.RunAsync` gains `Engine.PropertiesNotFoundException` in the hydrate catch (close `1011`), `PostgresFixture` gains `DeletePropertiesAsync(string hash)`, `Program.cs` gains `IPropertiesStore`, `IDiceSource`, `ISchedulerFactory` registrations. `GrimdarkFutureRules` already lives in `Rules/GrimdarkFuture/GfRules.cs` as `partial` (Task 6), so neither plan moves it. Also: `LegalSetMembership` checks a `TokensSpent` by the keys and per-key caps of `TokenSpend.MaxByModel` and by no total, so the interpreter plan's spend step owns the one check left — that each key the answering team paid with is one of **its own** models — reading the caps back off the Ask the log holds.
3. **Geometry plan** (via the interpreter plan): `MoveRegion` destinations are checked here for model ids only; the distance check against `Allowance` and the impassable ids is the move step's, using `Distance.EdgeToEdge` and `Containment.VolumesContaining`, and settling the server-side `Z` with `Settling.SurfaceHeightAt`. Hand moves (`MoveModels`) are copied as sent, including the client's `Z`.
4. **Client shell plan**: send `Connect` as the first frame within 10 s of opening `ws://localhost:5200/ws`; expect `Welcome`, then `BoardMessage` from `setup` on, then at `playing`/`ended` one or more `EventsMessage`s ending with the whole log delivered (an empty `EventsMessage` when the log is empty), then live frames — except a game ended before setup ever ran, which has no `table_state` row and no log, so `Welcome` is the whole sequence and no board arrives; a `Rejected` frame is followed by a close; a second socket for the same participant closes the first with `1000`; a reconnecting participant is returned to the team their `participant` row names, carried in `Welcome`'s `Info.Participants`, so the client never re-picks a team after a reconnect and never renders a returning player as a spectator (owner ruling 1, 2026-09-06); `Refused.MessageId` echoes `CommandId`/`EditId`; `presence_ended` arrives when another participant's last socket closes; the JWT in `Connect` is verified once — an open socket is never re-authenticated, so the client's once-a-minute refresh matters only for the next connect. The connect sequence ends with `AtRest`, at every status, and every command is answered by one `AtRest` after its events or its `Refused`; a client that has no use for the frame must ignore it, as it must ignore any kind it does not recognise, rather than treat it as a protocol error.
5. **Contracts plan**: `AtRest(long Seq)` as a `SocketMessage` kind, wire kind `at_rest`, server to client (owner ruling 4, 2026-09-06); `ProjectionDesyncException` thrown by `EventApplier` in place of the `KeyNotFoundException` an unknown id raises today, carrying the event kind and the unknown id (owner ruling 2). This server catches the second in two places, `GameSession.AppendAsync` and `Hydrator` by way of `ConnectionHandshake`, and faults or refuses the connection rather than resyncing. The contracts plan owns the two types; **when the frame is sent is this plan's**, because ruling 4 left "when exactly it is sent, whether it is per connection or broadcast" to the module that sends it. Where that plan's connect-sequence note reads "each of those rounds of processing ends with `AtRest(Seq)`, broadcast to every socket", the rule to implement is the one above: nothing after a lobby or setup edit, and the sender alone when the batch appended nothing.

## Questions for the project owner

1. ~~**May teams be created and picked after `playing`?**~~ **Answered by the owner, 2026-09-06 (ruling 1).** Teams are created only while the game is `lobby` or `setup`; at the flip to `playing` the team list is frozen along with the scheduler shape, the armies and the layout, and no team is created, renamed or removed after that. A participant's team is their slot, recorded on their `participant` row, chosen before play and unchanged during it. **Reconnecting returns a player to their own slot**: they are not a spectator, they do not re-pick, and nothing about their team is re-derived — this is the ordinary case, and the reason hydration reads the participant rows. Someone who never joined before play began has no slot, so they have no team, are never Asked, and watch; that is the whole definition of a spectator. The assumption was right about the refusal and said nothing about the rejoin, so the two halves are now stated separately wherever the plan touches them: Task 8 (the connect sequence), Task 10 (the refusal and its test `Teams_are_locked_from_playing_on_but_a_rejoining_participant_keeps_their_team`) and Task 12 (the freeze). Spec correction 1 carries the wording, and the canonical statement is game flow → Teams are frozen at the flip, and a team is a slot.
2. **What does the draft start as? Ruled 2026-09-09 by the owner, as assumed: the server's configured default**, 72×48 with the built-in `grass` texture ([closing the open questions](../specs/2026-09-09-open-questions-design.md)). 72×48 is the size every deployment style assumes, and the creator gets a legal table to edit without a round trip. Assumed the server's configured default, 72×48 with the built-in texture `grass`, replaced by the first edit. If the creator's client should choose, `StartSetup` gains fields (a Protocol change owned by the contracts plan) or the client sends `ReplaceDraft` right after; if the default should be a layout, it needs a management endpoint to read one from here.
3. **One connection per participant. Ruled 2026-09-09 by the owner, as assumed: a newer socket replaces the older one** ([closing the open questions](../specs/2026-09-09-open-questions-design.md)). Presence stays one value per participant and `AttachCoreAsync`/`DetachAsync` replace cleanly; a reconnection after a blip works and a second window kills the first. Assumed a newer socket replaces the older one (spec correction 8). If two are allowed, presence stays one value per participant (both sockets write the same slot, latest wins) and `AttachCoreAsync`/`DetachAsync` stop replacing and stop sending `presence_ended` until the last socket closes — a small change, but presence's open item then closes differently.

Four questions this plan first asked are answered by the specs and struck: **commands while `ended`** (yes — accounts → Ending is a flag, *"any participant can pick an ended game back up"*, and identity and enumerations' reversible `ended`; there is no un-end endpoint for the alternative to use); **the presence relay rate** (20 Hz — presence left the rate open and `architecture.md`'s "four players at 20Hz" is the first-pass figure the whole-design review deliberately left standing; a different figure changes one default and nothing else); **session eviction** (accounts makes hydrate-from-the-log the one path for a first join, a rejoin, a reconnect and a crashed task, so a game leaves memory with its last socket and nothing keeps it warm); and **the account claim** (the two plans agree on the claim name, issuer, audience and JWKS URL; management's own question about where the claim comes from was ruled 09-06 in favour of the Stytch dashboard claim template, which changes nothing here — the claim keeps its name `account_id` and its top-level position, so `StytchJwtVerifier` and its tests are untouched).
