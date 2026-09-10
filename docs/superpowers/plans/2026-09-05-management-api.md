# Management API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `server/management/` — the ASP.NET Core HTTP API over `docs/schema.md`: Stytch-backed login and accounts, the asset library with presigned MinIO upload and download, armies imported from Army Forge, layouts, games and the public game browser — with its migrations, its integration tests against the Docker stack, and unit tests for everything with logic.

**Architecture:** One minimal-API project, `Tabletop.Management`, in four modules — Identity, Content (assets, folders, armies, layouts), Games, and the HTTP plumbing (version check, Stytch JWT verification, keyset paging, ProblemDetails). Persistence is hand-written SQL over Npgsql with Dapper for row mapping; migrations are DbUp scripts embedded in the assembly, the first of which is the contracts plan's `001_schema.sql`. `Tabletop.Content` and `Tabletop.Protocol` types are the response bodies wherever a contract type already exists (`Army`, `TableState`, `GameInfo`, `ProtocolVersion`); everything else is a DTO of this project. Blob bytes never pass through the API: the S3 client mints presigned URLs against MinIO and the only server-side read is a ranged GET of the GLB header.

**Tech Stack:** .NET 10 (`net10.0`, C# 12) minimal APIs; Npgsql 10.0.3 + Dapper 2.1.79; dbup-postgresql 6.1.5; AWSSDK.S3 4.0.102.5 (MinIO, path-style); Microsoft.AspNetCore.Authentication.JwtBearer 10.0.11; xUnit 2.9.3 + Microsoft.AspNetCore.Mvc.Testing 10.0.11; Stytch Consumer API (test project `project-test-437432c7-798d-49d2-9dc4-eff103937e0f`, base `https://test.stytch.com`); Army Forge `GET https://army-forge.onepagerules.com/api/tts?id={id}`.

**Spec:** `docs/superpowers/specs/2026-08-29-management-api-design.md` (primary); `2026-08-29-accounts-design.md`; `2026-08-28-army-design.md`; `2026-08-28-asset-library-design.md`; `2026-08-29-setup-content-design.md`; `2026-08-29-web-app-design.md`; `2026-09-04-mesh-conversion-design.md`; `2026-09-04-client-shell-design.md`; `2026-09-04-setup-phase-design.md`; `2026-08-29-identity-and-enumerations-design.md`; `2026-08-29-content-values-design.md`; `2026-09-04-whole-design-review.md` (newest, wins); `docs/schema.md`; `docs/architecture.md`. Earlier plan obeyed: `docs/superpowers/plans/2026-09-05-contracts-and-local-dev.md` (its *Interfaces → Produces* names are used verbatim). `docs/workstreams/03-management-identity.md` and `09-management-collections.md` are stale and not followed.

## Global Constraints

- `net10.0`, `LangVersion 12`, `Nullable enable`, `ImplicitUsings enable`; root namespace `Tabletop.Management` (matches `client/Tabletop.csproj` conventions); `Directory.Build.props` puts `obj/` under `obj/$(OS)/`; `global.json` pins SDK `10.0.400`.
- Identifiers are `Guid.CreateVersion7()` everywhere; no integer ids; ids are generated in application code before any external system hears of them (`architecture.md` → Game management data model).
- `libs/` stays persistence-free: no ORM entities, attributes or `DbContext`. All row mapping lives in this project (`architecture.md` → Persistence).
- No rules engine code, no legality, no `Properties` interpretation here. Management stores `properties` rows and serves `(content_hash, name, game_rules)`; the game server owns the catalog types (contracts plan → *`Properties` types are not in `libs/`*).
- Management is the only thing that talks to Stytch; no Stytch identifier is stored in our schema; `external_id` = `account.id` (accounts → Auth is a module, not a vendor). We issue no token of our own (management API → We issue no tokens of our own).
- Auth on every non-`/v1/auth/*` request is a Stytch session JWT verified locally against `GET {StytchBaseUrl}/v1/sessions/jwks/{project_id}` (RS256, `iss = stytch.com/{project_id}`, `aud = {project_id}`); the account id rides in the custom claim `account_id`. No call to Stytch on the request path.
- The `account_id` claim is put there by the project's **Stytch custom claim template**, `{"account_id": "{{ user.external_id }}"}`, evaluated by Stytch every time it mints a JWT — not by our code (owner ruling 09-06, question 1). Management passes `session_custom_claims` nowhere; its only job is to keep `external_id` set to the account id.
- Version check: header `Tabletop-Client-Version` (`ProtocolVersion.Header`); below the minimum → `426` (`ProtocolVersion.UpgradeRequiredStatusCode`), no body work done (management API → The version check).
- Authorisation is owner-only throughout, except `GET /v1/games` (public) and `GET /v1/games/{id}/assets` (participants). A row another account owns is `404`, never `403` (management API → The rest of the surface).
- Asset bytes never pass through the API: presigned PUT and GET against `TABLETOP_S3_ENDPOINT`, path-style, key `{owner_id}/{hash}`; GLB accepted, STL rejected; the triangle check warns above **100,000** and never blocks (management API → Asset bytes never pass through the API; mesh conversion → The threshold).
- Dedup is per user: `blob` is keyed `(owner_id, hash)`; the client computes the SHA-256 first (`architecture.md` → Assets are a library).
- Pagination is keyset: `limit` + `after` (a UUIDv7); on assets, armies, the game browser only (management API → Pagination is keyset).
- Local only: Postgres and MinIO from `docker-compose.yml`; configuration from `TABLETOP_*` environment variables and `dotnet user-secrets`; Stytch secret never committed (hosting ruling 09-04).
- The game server writes a game's rows while `status` is `lobby` or `setup` — `game.status` (the two transitions), `team`, `participant.team_id`, `participant.army_id`, `participant.army_snapshot`, and the `table_state` tree where `game_id` is set. Management never writes those columns except: `game.status = 'ended'` on `/end` and on last leave; participant insert on create/join and delete on leave; whole-game delete (whole-design review ruling 4; setup phase → The lifecycle). Everything else in the management schema is management's alone.
- Test-driven throughout: unit tests for logic (GLB header, import mapping, version check, JWT verification, dedup, authorisation), integration tests through `WebApplicationFactory` against the Docker Postgres and MinIO. Every task ends green and committed (imperative one-line message, `Co-Authored-By` and `Claude-Session` trailers).

---

## File Structure

```
Tabletop.sln                                      modified: + Tabletop.Management, + Tabletop.Management.Tests
.env.example                                      modified: + TABLETOP_STYTCH_*, TABLETOP_MANAGEMENT_URL, TABLETOP_WEB_URL
CLAUDE.md                                         modified: commands, layout, environment
server/management/
  Tabletop.Management.csproj                      UserSecretsId, embedded Migrations/*.sql
  Program.cs                                      composition root; `public partial class Program` for tests
  appsettings.json                                non-secret defaults (MinimumClientVersion, StytchBaseUrl, SessionMinutes)
  Configuration/ManagementOptions.cs              typed options bound from TABLETOP_* keys
  Configuration/DotEnv.cs                         loads the repo-root .env into the process (Development only)
  Http/ClientVersionMiddleware.cs                 426 below the minimum
  Http/Keyset.cs                                  limit/after parsing and the Page<T> envelope
  Http/CurrentAccount.cs                          account id from the JWT claim
  Http/Problems.cs                                ProblemDetails helpers (404/409/422)
  Persistence/Database.cs                         NpgsqlDataSource + Dapper settings
  Persistence/Migrator.cs                         DbUp over embedded scripts
  Persistence/Migrations/001_schema.sql           copy of scripts/postgres/init/001_schema.sql
  Persistence/Migrations/002_blob_uploaded.sql    blob.uploaded, blob.content_type
  Identity/StytchJwks.cs                          fetch + cache the JWKS; key resolver
  Identity/StytchAuthentication.cs                JwtBearer wiring
  Identity/StytchClient.cs                        the fourteen Stytch calls (typed HTTP)
  Identity/AccountRepository.cs                   account rows
  Identity/Sessions.cs                            ensure-account, link external_id, re-mint on the first login
  Identity/AuthEndpoints.cs                       /v1/auth/*
  Identity/DevSessions.cs                         POST /v1/dev/sessions; mapped only in a development environment
  Identity/AccountEndpoints.cs                    /v1/account
  Identity/AccountDeleter.cs                      total deletion
  Content/Assets/IBlobStore.cs, S3BlobStore.cs    presign, ranged read, delete
  Content/Assets/GlbHeader.cs                     triangle count from the JSON chunk
  Content/Assets/AssetRepository.cs, AssetEndpoints.cs, AssetDtos.cs
  Content/Assets/FolderRepository.cs, FolderEndpoints.cs
  Content/Armies/ArmyForgeClient.cs               GET api/tts?id=
  Content/Armies/ArmyForgeList.cs                 the export's JSON shape
  Content/Armies/ArmyForgeImport.cs               list → Tabletop.Content.Army
  Content/Armies/ArmyRepository.cs, ArmyEndpoints.cs
  Content/Layouts/LayoutRepository.cs, LayoutEndpoints.cs
  Games/GameRepository.cs, GameEndpoints.cs, GameDtos.cs, ManifestEndpoint.cs, PropertiesEndpoint.cs
tests/Tabletop.Management.Tests/
  Tabletop.Management.Tests.csproj
  TestDatabase.cs                                 one database per test run, migrated, dropped
  TestKeys.cs                                     RSA key + JWT minting + fake JWKS
  FakeStytch.cs                                   HttpMessageHandler stub for Stytch
  ManagementFactory.cs                            WebApplicationFactory<Program> with the fakes
  Fixtures/army-forge-c2-6YsWRH3py.json           a real export
  *Tests.cs                                       per task
```

Files are split by module and by responsibility (repository = SQL, endpoints = HTTP, client = an external system). `server/management/` is the project root so the SDK's `**/*.cs` glob covers every module folder without `.csproj` edits.

---
### Task 1: Project, solution, configuration and the test host

**Files:**
- Create: `server/management/Tabletop.Management.csproj`, `server/management/Program.cs`, `server/management/appsettings.json`, `server/management/Configuration/ManagementOptions.cs`, `server/management/Configuration/DotEnv.cs`
- Create: `tests/Tabletop.Management.Tests/Tabletop.Management.Tests.csproj`, `tests/Tabletop.Management.Tests/ManagementFactory.cs`, `tests/Tabletop.Management.Tests/HealthTests.cs`
- Modify: `Tabletop.sln`, `.env.example`

**Interfaces:**
- Consumes: `Tabletop.Protocol.ProtocolVersion.Current`; `.env.example` variables from the contracts plan (`TABLETOP_DB_CONNECTION`, `TABLETOP_S3_ENDPOINT`, `TABLETOP_S3_BUCKET`, `TABLETOP_S3_ACCESS_KEY`, `TABLETOP_S3_SECRET_KEY`).
- Produces: `ManagementOptions` (`DbConnection`, `S3Endpoint`, `S3Bucket`, `S3AccessKey`, `S3SecretKey`, `StytchProjectId`, `StytchSecret`, `StytchBaseUrl`, `WebUrl`, `MinimumClientVersion`, `SessionMinutes`) with `static ManagementOptions From(IConfiguration)`; `DotEnv.Load()`; `Program` (public partial) listening on `TABLETOP_MANAGEMENT_URL` (default `http://0.0.0.0:5100`); `GET /healthz`; `ManagementFactory : WebApplicationFactory<Program>` for every later test.
- `SessionMinutes` is Stytch's `session_duration_minutes` — the life of the **session**, defaulted to 43200 (30 days) so a player is not logged out mid-campaign. It is not the JWT's lifetime: Stytch's session JWT expires five minutes after it is issued whatever the session duration, which is exactly the management API's *"a JWT good for five minutes, refreshed every minute"* working figure and the whole reason `POST /v1/auth/session/refresh` exists (management API → We issue no tokens of our own). Deleting an account revokes the Stytch user, so refresh fails and the last JWT lapses within five minutes — the spec's revocation argument, unchanged.
- Configuration keys are the `.env` names with the `TABLETOP_` prefix stripped (`AddEnvironmentVariables("TABLETOP_")`), so `dotnet user-secrets set STYTCH_SECRET …` and `TABLETOP_STYTCH_SECRET=…` are the same key. The secret is never in a committed file (accounts → Auth is a module; CLAUDE.md → Environment).

Spec: `architecture.md` → Two servers (`server/management/`, ASP.NET Core, HTTP), Local development (both servers in WSL, Windows over forwarded localhost; port 5100 reserved by the contracts plan).

- [ ] **Step 1: Write the project file**

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12</LangVersion>
    <RootNamespace>Tabletop.Management</RootNamespace>
    <UserSecretsId>tabletop-management</UserSecretsId>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Npgsql" Version="10.0.3" />
    <PackageReference Include="Dapper" Version="2.1.79" />
    <PackageReference Include="dbup-postgresql" Version="6.1.5" />
    <PackageReference Include="AWSSDK.S3" Version="4.0.102.5" />
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.11" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../libs/Tabletop.Content/Tabletop.Content.csproj" />
    <ProjectReference Include="../../libs/Tabletop.Protocol/Tabletop.Protocol.csproj" />
  </ItemGroup>
  <ItemGroup>
    <EmbeddedResource Include="Persistence/Migrations/*.sql" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write the test project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12</LangVersion>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <RootNamespace>Tabletop.Management.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.13.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.0.2" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.11" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../server/management/Tabletop.Management.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
    <Content Include="Fixtures/**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Write the failing smoke test and the factory**

```csharp
// tests/Tabletop.Management.Tests/ManagementFactory.cs
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Tabletop.Management.Tests;

/// <summary>The test host. Later tasks add the test database, the fake JWKS and the fake Stytch here.</summary>
public class ManagementFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["STYTCH_PROJECT_ID"] = "project-test-00000000-0000-0000-0000-000000000000",
            ["STYTCH_SECRET"] = "secret-test",
        }));
    }
}
```

```csharp
// tests/Tabletop.Management.Tests/HealthTests.cs
namespace Tabletop.Management.Tests;

public class HealthTests : IClassFixture<ManagementFactory>
{
    private readonly HttpClient _client;
    public HealthTests(ManagementFactory f) => _client = f.CreateClient();

    [Fact]
    public async Task Healthz_answers_ok()
    {
        var response = await _client.GetAsync("/healthz");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }
}
```

- [ ] **Step 4: Add both projects to the solution and run; expect a compile failure (`Program` undefined)**

```bash
cd /home/cstupi/src/tabletop
dotnet sln Tabletop.sln add --solution-folder server server/management/Tabletop.Management.csproj
dotnet sln Tabletop.sln add --solution-folder tests tests/Tabletop.Management.Tests/Tabletop.Management.Tests.csproj
dotnet test tests/Tabletop.Management.Tests
```

- [ ] **Step 5: Write `Configuration/ManagementOptions.cs` and `Configuration/DotEnv.cs`**

```csharp
using Tabletop.Protocol;

namespace Tabletop.Management.Configuration;

/// <summary>Every setting the server reads, by the .env name without its TABLETOP_ prefix.</summary>
public sealed record ManagementOptions(
    string DbConnection, string S3Endpoint, string S3Bucket, string S3AccessKey, string S3SecretKey,
    string StytchProjectId, string StytchSecret, string StytchBaseUrl, string WebUrl,
    int MinimumClientVersion, int SessionMinutes)
{
    public static ManagementOptions From(IConfiguration c) => new(
        Required(c, "DB_CONNECTION"), Required(c, "S3_ENDPOINT"), Required(c, "S3_BUCKET"),
        Required(c, "S3_ACCESS_KEY"), Required(c, "S3_SECRET_KEY"),
        Required(c, "STYTCH_PROJECT_ID"), Required(c, "STYTCH_SECRET"),
        c["STYTCH_BASE_URL"] ?? "https://test.stytch.com",
        c["WEB_URL"] ?? "http://localhost:5173",
        int.Parse(c["MIN_CLIENT_VERSION"] ?? c["MinimumClientVersion"] ?? ProtocolVersion.Current.ToString()),   // TABLETOP_MIN_CLIENT_VERSION: the same variable the game server reads
        int.Parse(c["SessionMinutes"] ?? "43200"));

    private static string Required(IConfiguration c, string key) =>
        c[key] ?? throw new InvalidOperationException($"Missing configuration TABLETOP_{key} (env, .env, or dotnet user-secrets).");
}
```

```csharp
namespace Tabletop.Management.Configuration;

/// <summary>Reads the repo-root .env (the same file docker compose reads) into the process environment. Existing variables win.</summary>
public static class DotEnv
{
    public static void Load()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, ".env");
            if (!File.Exists(path)) continue;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#') || !line.Contains('=')) continue;
                var (key, value) = (line[..line.IndexOf('=')].Trim(), line[(line.IndexOf('=') + 1)..].Trim());
                if (Environment.GetEnvironmentVariable(key) is null) Environment.SetEnvironmentVariable(key, value);
            }
            return;
        }
    }
}
```

- [ ] **Step 6: Write `Program.cs` and `appsettings.json`**

```csharp
using Tabletop.Management.Configuration;

DotEnv.Load();
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables("TABLETOP_");
builder.WebHost.UseUrls(builder.Configuration["MANAGEMENT_URL"] ?? "http://0.0.0.0:5100");
var options = ManagementOptions.From(builder.Configuration);
builder.Services.AddSingleton(options);

var app = builder.Build();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.Run();

public partial class Program { }
```

```json
{
  "MinimumClientVersion": 1,
  "SessionMinutes": 43200,
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } }
}
```

`.env.example` gains (project id is public; the secret is not):

```bash
TABLETOP_MANAGEMENT_URL=http://0.0.0.0:5100
TABLETOP_WEB_URL=http://localhost:5173
TABLETOP_STYTCH_BASE_URL=https://test.stytch.com
TABLETOP_STYTCH_PROJECT_ID=project-test-437432c7-798d-49d2-9dc4-eff103937e0f
# TABLETOP_STYTCH_SECRET is never written here: `cd server/management && dotnet user-secrets set STYTCH_SECRET <secret>`
```

- [ ] **Step 7: Run `dotnet test tests/Tabletop.Management.Tests`; expect 1 passed (the `.env` copied from `.env.example` supplies the DB and S3 keys). Commit.**

```bash
git add Tabletop.sln .env.example server/management tests/Tabletop.Management.Tests
git commit -m "Add the management API project, its configuration and the test host"
```

### Task 2: Migrations, the data source and the test database

**Files:**
- Create: `server/management/Persistence/Migrator.cs`, `server/management/Persistence/Database.cs`, `server/management/Persistence/Migrations/001_schema.sql` (byte-for-byte copy of `scripts/postgres/init/001_schema.sql`), `server/management/Persistence/Migrations/002_blob_uploaded.sql`
- Modify: `server/management/Program.cs`
- Create: `tests/Tabletop.Management.Tests/TestDatabase.cs`, `tests/Tabletop.Management.Tests/MigrationTests.cs`
- Modify: `tests/Tabletop.Management.Tests/ManagementFactory.cs`

**Interfaces:**
- Consumes: `scripts/postgres/init/001_schema.sql` (contracts plan Task 15); `TABLETOP_DB_CONNECTION`.
- Produces: `Migrator.Run(string connectionString)` (idempotent; baselines a database the compose init already created); `Database` with `Task<NpgsqlConnection> Open(CancellationToken)` and `NpgsqlDataSource Source`; `TestDatabase` (one fresh database per factory, migrated, dropped on dispose); every later repository takes `Database`.
- Decision — **Dapper over Npgsql with hand-written SQL**, no ORM: the schema is normalised and hand-written already, the army tree and table state are loaded whole with a handful of queries, and every constraint lives in the database rather than in mapping attributes (`schema.md` → Normalised, not documents). Rows are `sealed record`s mapped by constructor, so SQL aliases columns to the parameter names (`owner_id as OwnerId`). **DbUp** applies embedded `NNN_*.sql` scripts once each, journalled in `public.schema_version`; the compose init SQL stays for a bare `docker compose up`, and the migrator recognises that database and records 001 as applied (contracts plan → Local development: "the management API plan adopts it as migration 001").
- `002` adds `blob.uploaded boolean` (false between `POST /v1/assets` and `/confirm`; a manifest and a download skip unuploaded blobs) and `blob.content_type text` (`model/gltf-binary`, `image/png`, `image/jpeg`, `image/webp`) — spec correction 1, with WebP added by the ruling of 2026-09-09.

Spec: `schema.md` in full; management API → Asset bytes never pass through the API (`confirm` is a distinct step, so the row exists before the bytes).

- [ ] **Step 1: Write the failing test**

```csharp
using Dapper;
using Npgsql;

namespace Tabletop.Management.Tests;

public class MigrationTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public MigrationTests(ManagementFactory f) { _f = f; _f.CreateClient(); }

    [Fact]
    public async Task Schema_has_every_table_and_the_upload_columns()
    {
        await using var c = new NpgsqlConnection(_f.Database.ConnectionString);
        var tables = await c.ExecuteScalarAsync<int>("select count(*) from information_schema.tables where table_schema in ('management','game')");
        Assert.Equal(33, tables);
        var cols = (await c.QueryAsync<string>("select column_name from information_schema.columns where table_schema='management' and table_name='blob'")).ToList();
        Assert.Contains("uploaded", cols);
        Assert.Contains("content_type", cols);
    }

    [Fact]
    public void Running_the_migrator_twice_is_a_no_op() => Persistence.Migrator.Run(_f.Database.ConnectionString);
}
```

- [ ] **Step 2: Write `TestDatabase.cs` and extend the factory**

```csharp
using Dapper;
using Npgsql;

namespace Tabletop.Management.Tests;

/// <summary>A database of its own per factory, on the Docker Postgres from .env. Dropped on dispose.</summary>
public sealed class TestDatabase : IDisposable
{
    private readonly string _admin;
    public string Name { get; } = "tabletop_test_" + Guid.CreateVersion7().ToString("N")[..12];
    public string ConnectionString { get; }

    public TestDatabase()
    {
        Configuration.DotEnv.Load();
        var dev = Environment.GetEnvironmentVariable("TABLETOP_DB_CONNECTION") ?? throw new InvalidOperationException("TABLETOP_DB_CONNECTION not set; copy .env.example to .env");
        _admin = new NpgsqlConnectionStringBuilder(dev) { Database = "postgres" }.ConnectionString;
        ConnectionString = new NpgsqlConnectionStringBuilder(dev) { Database = Name }.ConnectionString;
        using var c = new NpgsqlConnection(_admin);
        c.Execute($"create database \"{Name}\"");
        Persistence.Migrator.Run(ConnectionString);
    }

    public void Dispose()
    {
        NpgsqlConnection.ClearAllPools();
        using var c = new NpgsqlConnection(_admin);
        c.Execute($"drop database if exists \"{Name}\" with (force)");
    }
}
```

In `ManagementFactory`: add `public TestDatabase Database { get; } = new();`, add `["DB_CONNECTION"] = Database.ConnectionString` to the in-memory configuration, and override `Dispose(bool disposing)` to call `Database.Dispose()` after `base.Dispose(disposing)`.

- [ ] **Step 3: Run; expect compile errors (`Migrator`, `Database` undefined).**

- [ ] **Step 4: Write `Migrator.cs`, `Database.cs`, the two scripts**

```csharp
using Dapper;
using DbUp;
using Npgsql;

namespace Tabletop.Management.Persistence;

public static class Migrator
{
    private const string Journal = "schema_version";
    private const string FirstScript = "Tabletop.Management.Persistence.Migrations.001_schema.sql";

    public static void Run(string connectionString)
    {
        Baseline(connectionString);
        var result = DeployChanges.To.PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(typeof(Migrator).Assembly, s => s.EndsWith(".sql"))
            .JournalToPostgresqlTable("public", Journal)
            .WithTransactionPerScript()
            .LogToNowhere()
            .Build()
            .PerformUpgrade();
        if (!result.Successful) throw new InvalidOperationException("Migration failed", result.Error);
    }

    /// <summary>A database created by docker compose's init SQL already holds 001; record it instead of re-running it.</summary>
    private static void Baseline(string connectionString)
    {
        using var c = new NpgsqlConnection(connectionString);
        var hasSchema = c.ExecuteScalar<bool>("select exists(select 1 from information_schema.tables where table_schema='management' and table_name='account')");
        var hasJournal = c.ExecuteScalar<bool>($"select exists(select 1 from information_schema.tables where table_schema='public' and table_name='{Journal}')");
        if (!hasSchema || hasJournal) return;
        c.Execute($"create table public.{Journal} (schemaversionsid serial primary key, scriptname varchar(255) not null, applied timestamp not null)");
        c.Execute($"insert into public.{Journal} (scriptname, applied) values (@name, now())", new { name = FirstScript });
    }
}
```

```csharp
using Npgsql;

namespace Tabletop.Management.Persistence;

/// <summary>One data source for the process. search_path covers both schemas so SQL names tables bare.</summary>
public sealed class Database : IAsyncDisposable
{
    public Database(string connectionString)
    {
        var b = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = "management,game,public" };
        Source = new NpgsqlDataSourceBuilder(b.ConnectionString).Build();
    }

    public NpgsqlDataSource Source { get; }
    public Task<NpgsqlConnection> Open(CancellationToken ct = default) => Source.OpenConnectionAsync(ct).AsTask();
    public ValueTask DisposeAsync() => Source.DisposeAsync();
}
```

```sql
-- 002_blob_uploaded.sql: the upload state between POST /v1/assets and /confirm, and what the bytes are.
alter table management.blob
    add column uploaded     boolean not null default false,
    add column content_type text    not null default 'model/gltf-binary'
        check (content_type in ('model/gltf-binary', 'image/png', 'image/jpeg', 'image/webp'));
```

`001_schema.sql` is copied with `cp scripts/postgres/init/001_schema.sql server/management/Persistence/Migrations/001_schema.sql` and never edited afterwards — later changes are new scripts.

- [ ] **Step 5: Wire `Program.cs`** — after `AddSingleton(options)`:

```csharp
Persistence.Migrator.Run(options.DbConnection);
builder.Services.AddSingleton(new Tabletop.Management.Persistence.Database(options.DbConnection));
```

- [ ] **Step 6: Run `dotnet test tests/Tabletop.Management.Tests` with the compose stack up; expect 3 passed. Confirm the dev database also migrates: `TABLETOP_DB_CONNECTION` from `.env` → `dotnet run --project server/management` logs no error and `psql -c '\dt public.*'` shows `schema_version` with two rows. Commit: `Add migrations, the data source and a per-run test database`.**

### Task 3: HTTP plumbing — the version check, keyset paging, problems, JSON

**Files:**
- Create: `server/management/Http/ClientVersionMiddleware.cs`, `server/management/Http/Keyset.cs`, `server/management/Http/Problems.cs`
- Modify: `server/management/Program.cs`
- Create: `tests/Tabletop.Management.Tests/ClientVersionTests.cs`, `tests/Tabletop.Management.Tests/KeysetTests.cs`, `tests/Tabletop.Management.Tests/CorsTests.cs`

**Interfaces:**
- Consumes: `ProtocolVersion.Header`, `ProtocolVersion.UpgradeRequiredStatusCode`, `ProtocolVersion.Current`; `ContentJson.Configure(JsonSerializerOptions)`; `ManagementOptions.WebUrl` (Task 1).
- Produces: `ClientVersion.Decide(string? header, int minimum) : ClientVersionVerdict { Accept, UpgradeRequired, Malformed }`; `ClientVersionMiddleware`; `Keyset.Parse(int? limit, Guid? after) : Keyset(int Limit, Guid? After)` (default 50, max 200); `Page<T>(IReadOnlyList<T> Items, Guid? Next)` with `static Page<T>.Of(IReadOnlyList<T> items, int limit, Func<T, Guid> id)`; `Problems.NotFound()`, `Problems.Conflict(string detail)`, `Problems.Unprocessable(string detail)` returning `IResult`s with `application/problem+json`.
- Every response body is serialised with `ContentJson.Configure` (camelCase, snake_case enums, nulls omitted), so a `TableState` or `GameInfo` on HTTP is byte-identical to the same value on the socket.
- A missing header is accepted: the web app is served from the same repo and tracks the API directly (management API → `/v1` in the path; client shell → Home: "the client sends its version on every request"). The version-check question is struck below: the specs answer it, and spec correction 7 records the answer.
- CORS: one policy, origin `ManagementOptions.WebUrl` (`http://localhost:5173` by default), headers `Authorization` and `Content-Type`, methods `GET, POST, PUT, PATCH, DELETE`, no credentials (the JWT is a bearer header, not a cookie). The web app reaches the API through its Vite proxy in development, but a built SPA served from any other origin needs the preflight answered (web-app plan → Interfaces needed → Management API plan). Only `Tabletop-Client-Version` is added to the allowed headers beyond those two, so a browser client that chose to send it could.

Spec: management API → The version check ("No endpoint, one status code"); whole-design review → Gaps filled ("The version check is a request header answered by 426 Upgrade Required"); management API → Pagination is keyset ("UUIDv7 is time-ordered by construction, so the id is the cursor").

- [ ] **Step 1: Write the failing tests**

```csharp
using Tabletop.Management.Http;
using Tabletop.Protocol;

namespace Tabletop.Management.Tests;

public class ClientVersionTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public ClientVersionTests(ManagementFactory f) => _f = f;

    [Theory]
    [InlineData(null, 1, ClientVersionVerdict.Accept)]
    [InlineData("1", 1, ClientVersionVerdict.Accept)]
    [InlineData("2", 1, ClientVersionVerdict.Accept)]
    [InlineData("0", 1, ClientVersionVerdict.UpgradeRequired)]
    [InlineData("abc", 1, ClientVersionVerdict.Malformed)]
    public void Decides_from_the_header(string? header, int minimum, ClientVersionVerdict expected) =>
        Assert.Equal(expected, ClientVersion.Decide(header, minimum));

    [Fact]
    public async Task An_old_client_gets_426_before_anything_else_runs()
    {
        var client = _f.CreateClient();
        client.DefaultRequestHeaders.Add(ProtocolVersion.Header, "0");
        var response = await client.GetAsync("/healthz");
        Assert.Equal(ProtocolVersion.UpgradeRequiredStatusCode, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_current_client_passes()
    {
        var client = _f.CreateClient();
        client.DefaultRequestHeaders.Add(ProtocolVersion.Header, ProtocolVersion.Current.ToString());
        Assert.True((await client.GetAsync("/healthz")).IsSuccessStatusCode);
    }
}
```

```csharp
using Tabletop.Management.Http;

namespace Tabletop.Management.Tests;

public class KeysetTests
{
    [Fact] public void Defaults_to_fifty() => Assert.Equal(new Keyset(50, null), Keyset.Parse(null, null));
    [Fact] public void Caps_at_two_hundred() => Assert.Equal(200, Keyset.Parse(999, null).Limit);
    [Fact] public void Rejects_non_positive_limits() => Assert.Throws<ArgumentOutOfRangeException>(() => Keyset.Parse(0, null));

    [Fact]
    public void Next_is_the_last_id_only_when_the_page_is_full()
    {
        var ids = Enumerable.Range(0, 3).Select(_ => Guid.CreateVersion7()).ToList();
        Assert.Equal(ids[2], Page<Guid>.Of(ids, 3, x => x).Next);
        Assert.Null(Page<Guid>.Of(ids, 4, x => x).Next);
    }
}
```

```csharp
using System.Net;

namespace Tabletop.Management.Tests;

public class CorsTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public CorsTests(ManagementFactory f) => _f = f;

    [Fact]
    public async Task The_web_origin_gets_its_preflight_answered_with_the_bearer_and_json_headers()
    {
        var client = _f.CreateClient();
        var preflight = new HttpRequestMessage(HttpMethod.Options, "/v1/account");
        preflight.Headers.Add("Origin", "http://localhost:5173");
        preflight.Headers.Add("Access-Control-Request-Method", "PATCH");
        preflight.Headers.Add("Access-Control-Request-Headers", "authorization, content-type");
        var response = await client.SendAsync(preflight);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("http://localhost:5173", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("PATCH", response.Headers.GetValues("Access-Control-Allow-Methods").Single().Split(',').Select(s => s.Trim()));
        Assert.Equal(["authorization", "content-type"], response.Headers.GetValues("Access-Control-Allow-Headers").Single().Split(',').Select(s => s.Trim().ToLowerInvariant()).Order());
    }

    [Fact]
    public async Task Another_origin_gets_no_allow_origin_header()
    {
        var client = _f.CreateClient();
        var preflight = new HttpRequestMessage(HttpMethod.Options, "/v1/account");
        preflight.Headers.Add("Origin", "http://evil.example");
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        var response = await client.SendAsync(preflight);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `ClientVersionMiddleware.cs`:**

```csharp
using Tabletop.Protocol;

namespace Tabletop.Management.Http;

public enum ClientVersionVerdict { Accept, UpgradeRequired, Malformed }

public static class ClientVersion
{
    public static ClientVersionVerdict Decide(string? header, int minimum) => header switch
    {
        null => ClientVersionVerdict.Accept,
        _ when int.TryParse(header, out var v) => v >= minimum ? ClientVersionVerdict.Accept : ClientVersionVerdict.UpgradeRequired,
        _ => ClientVersionVerdict.Malformed,
    };
}

/// <summary>Reads Tabletop-Client-Version on every request; a client below the minimum is told to update and nothing else runs.</summary>
public sealed class ClientVersionMiddleware(RequestDelegate next, Configuration.ManagementOptions options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var verdict = ClientVersion.Decide(context.Request.Headers[ProtocolVersion.Header].FirstOrDefault(), options.MinimumClientVersion);
        if (verdict == ClientVersionVerdict.Accept) { await next(context); return; }
        var status = verdict == ClientVersionVerdict.UpgradeRequired ? ProtocolVersion.UpgradeRequiredStatusCode : StatusCodes.Status400BadRequest;
        await Results.Problem(
            title: verdict == ClientVersionVerdict.UpgradeRequired ? "Update required" : "Malformed client version",
            detail: $"This server accepts {ProtocolVersion.Header} >= {options.MinimumClientVersion}.",
            statusCode: status).ExecuteAsync(context);
    }
}
```

- [ ] **Step 3: Write `Keyset.cs` and `Problems.cs`**

```csharp
namespace Tabletop.Management.Http;

/// <summary>limit + after. The id is the cursor because UUIDv7 is time-ordered.</summary>
public readonly record struct Keyset(int Limit, Guid? After)
{
    public const int Default = 50, Max = 200;
    public static Keyset Parse(int? limit, Guid? after)
    {
        var l = limit ?? Default;
        if (l <= 0) throw new ArgumentOutOfRangeException(nameof(limit), "limit must be positive");
        return new Keyset(Math.Min(l, Max), after);
    }
}

public sealed record Page<T>(IReadOnlyList<T> Items, Guid? Next)
{
    public static Page<T> Of(IReadOnlyList<T> items, int limit, Func<T, Guid> id) =>
        new(items, items.Count >= limit && items.Count > 0 ? id(items[^1]) : null);
}
```

```csharp
namespace Tabletop.Management.Http;

/// <summary>RFC 7807 bodies. Another owner's row is a 404 like a missing one; existence is not disclosed.</summary>
public static class Problems
{
    public static IResult NotFound() => Results.Problem(title: "Not found", statusCode: StatusCodes.Status404NotFound);
    public static IResult Conflict(string detail) => Results.Problem(title: "Conflict", detail: detail, statusCode: StatusCodes.Status409Conflict);
    public static IResult Unprocessable(string detail) => Results.Problem(title: "Unprocessable", detail: detail, statusCode: StatusCodes.Status422UnprocessableEntity);
    public static IResult BadGateway(string detail) => Results.Problem(title: "Upstream failure", detail: detail, statusCode: StatusCodes.Status502BadGateway);
}
```

- [ ] **Step 4: Wire `Program.cs`** — before `Build()`:

```csharp
builder.Services.ConfigureHttpJsonOptions(o => Tabletop.Content.ContentJson.Configure(o.SerializerOptions));
builder.Services.AddProblemDetails();
builder.Services.AddCors(c => c.AddDefaultPolicy(p => p
    .WithOrigins(options.WebUrl.TrimEnd('/'))
    .WithHeaders("Authorization", "Content-Type", Tabletop.Protocol.ProtocolVersion.Header)
    .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")));
```

and after `Build()`, in this order: the exception handler, `app.UseCors();`, then `app.UseMiddleware<Tabletop.Management.Http.ClientVersionMiddleware>();`. CORS runs before the version check so a preflight (which carries no `Tabletop-Client-Version`) is answered whatever the client's version. `Keyset.Parse` throwing `ArgumentOutOfRangeException` is turned into a 400 by an exception handler added here: `app.UseExceptionHandler(h => h.Run(ctx => Results.Problem(statusCode: ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error is ArgumentOutOfRangeException ? 400 : 500).ExecuteAsync(ctx)));` placed before the version middleware.

- [ ] **Step 5: Run `dotnet test tests/Tabletop.Management.Tests`; expect 16 passed. Commit: `Add the version check, keyset paging, problem responses and CORS for the web app`.**

### Task 4: Stytch JWT verification, the current account, `GET /v1/account`

**Files:**
- Create: `server/management/Identity/StytchJwks.cs`, `server/management/Identity/StytchAuthentication.cs`, `server/management/Http/CurrentAccount.cs`, `server/management/Identity/AccountRepository.cs`, `server/management/Identity/AccountEndpoints.cs`
- Modify: `server/management/Program.cs`
- Create: `tests/Tabletop.Management.Tests/TestKeys.cs`, `tests/Tabletop.Management.Tests/JwtTests.cs`
- Modify: `tests/Tabletop.Management.Tests/ManagementFactory.cs`

**Interfaces:**
- Produces: `StytchJwks(HttpClient, ManagementOptions)` (singleton over the named client `StytchAuthentication.JwksClient`) with `Task<IReadOnlyList<SecurityKey>> Resolve(string kid)` (cached; refetches once on an unknown `kid`, at most every 30 s); `StytchAuthentication.AddStytchJwt(IServiceCollection, ManagementOptions)`; `StytchAuthentication.AccountClaim = "account_id"`, `StytchAuthentication.Issuer(projectId) = $"stytch.com/{projectId}"`; `CurrentAccount.Id(ClaimsPrincipal) : Guid`; `AccountRepository(Database)` with `Task<AccountRow?> Get(Guid id)`, `Task Insert(Guid id, string name)`, `Task<bool> Rename(Guid id, string name)`; `AccountRow(Guid Id, string Name)`; `GET /v1/account → AccountRow`.
- Test helpers: `TestKeys.Mint(Guid accountId, string projectId, DateTimeOffset? expires = null, string? issuer = null, string kid = "kid-1")`, `TestKeys.JwksHandler` (serves the current key set), `TestKeys.Rotate()`; `ManagementFactory.AuthenticatedClient(Guid accountId)` (inserts the account row, mints a JWT, sets the bearer header).
- **The game server must verify the same way**: JWKS at `{StytchBaseUrl}/v1/sessions/jwks/{project_id}` (**no auth** — Stytch's reference page lists a `basicAuth` scheme and passes `-u project:secret` in its example, but `curl https://test.stytch.com/v1/sessions/jwks/project-test-437432c7-798d-49d2-9dc4-eff103937e0f` with no credentials answers `200` with the key set; verified 09-05 and again 09-06, so `StytchJwks` sends no `Authorization` header and the game server needs no Stytch secret to verify), `iss = stytch.com/{project_id}`, `aud = project_id`, RS256, account id in the top-level claim `account_id`, Stytch's own session data under `https://stytch.com/session`. Recorded under *Interfaces needed from other modules* for the game-server plan.

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
        var token = new JwtSecurityToken(
            issuer: issuer ?? $"stytch.com/{projectId}", audience: projectId,
            claims: [new Claim("sub", "user-test-1"), new Claim(Identity.StytchAuthentication.AccountClaim, accountId.ToString())],
            notBefore: DateTime.UtcNow.AddMinutes(-1), expires: (expires ?? DateTimeOffset.UtcNow.AddMinutes(5)).UtcDateTime,
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
    private DateTimeOffset _fetched = DateTimeOffset.MinValue;

    public string Url => $"{options.StytchBaseUrl.TrimEnd('/')}/v1/sessions/jwks/{options.StytchProjectId}";

    public async Task<IReadOnlyList<SecurityKey>> Resolve(string? kid, CancellationToken ct = default)
    {
        var keys = _keys ?? await Fetch(ct);
        var match = keys.Keys.Where(k => kid is null || k.Kid == kid).ToList();
        if (match.Count == 0 && DateTimeOffset.UtcNow - _fetched > TimeSpan.FromSeconds(30))
            match = (await Fetch(ct)).Keys.Where(k => k.Kid == kid).ToList();
        return match;
    }

    private async Task<JsonWebKeySet> Fetch(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _keys = new JsonWebKeySet(await http.GetStringAsync(Url, ct));
            _fetched = DateTimeOffset.UtcNow;
            return _keys;
        }
        finally { _gate.Release(); }
    }
}
```

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Tabletop.Management.Configuration;

namespace Tabletop.Management.Identity;

public static class StytchAuthentication
{
    /// <summary>Our account UUID. Stytch's custom claim template maps {{ user.external_id }} to this name on every mint; we never send it per call.</summary>
    public const string AccountClaim = "account_id";
    public const string SessionClaim = "https://stytch.com/session";
    /// <summary>Named client so tests can swap its handler; the cache must be a singleton, which a typed client is not.</summary>
    public const string JwksClient = "stytch-jwks";
    public static string Issuer(string projectId) => $"stytch.com/{projectId}";

    public static IServiceCollection AddStytchJwt(this IServiceCollection services, ManagementOptions options)
    {
        services.AddHttpClient(JwksClient);
        services.AddSingleton(sp => new StytchJwks(sp.GetRequiredService<IHttpClientFactory>().CreateClient(JwksClient), options));
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.MapInboundClaims = false;
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = Issuer(options.StytchProjectId),
                ValidAudience = options.StytchProjectId,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                ClockSkew = TimeSpan.FromSeconds(30),
            };
        });
        // The key resolver is configured here rather than inline so it receives the app's own StytchJwks singleton.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<StytchJwks>((o, jwks) =>
            o.TokenValidationParameters.IssuerSigningKeyResolver = (_, _, kid, _) => jwks.Resolve(kid).GetAwaiter().GetResult());
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

- [ ] **Step 5: Wire `Program.cs`** — `builder.Services.AddStytchJwt(options); builder.Services.AddSingleton<AccountRepository>();` before `Build()`; after the version middleware: `app.UseAuthentication(); app.UseAuthorization(); app.MapAccount();`. The exception handler from Task 3 maps `UnauthorizedAccessException` to 401.

- [ ] **Step 6: Run `dotnet test tests/Tabletop.Management.Tests`; expect 19 passed. Commit: `Verify Stytch session JWTs locally and read the current account`.**

### Task 5: The Stytch client, session completion, `/v1/auth/*` and the development-only mint

**Files:**
- Create: `server/management/Identity/StytchClient.cs`, `server/management/Identity/Sessions.cs`, `server/management/Identity/AuthEndpoints.cs`, `server/management/Identity/DevSessions.cs`
- Modify: `server/management/Program.cs`
- Create: `tests/Tabletop.Management.Tests/FakeStytch.cs`, `tests/Tabletop.Management.Tests/AuthTests.cs`, `tests/Tabletop.Management.Tests/DevSessionTests.cs`
- Modify: `tests/Tabletop.Management.Tests/ManagementFactory.cs`

**Interfaces:**
- Consumes: `AccountRepository.Insert/Get`; `StytchAuthentication.AccountClaim`; `ManagementOptions.StytchBaseUrl/StytchProjectId/StytchSecret/SessionMinutes/WebUrl`.
- Produces: `StytchClient(HttpClient, ManagementOptions)` over the named client `StytchClient.HttpClientName = "stytch"` with `SendEmailOtp(email) : Task<OtpSent(string MethodId, string UserId, bool UserCreated)>`, `AuthenticateOtp(methodId, code)`, `AuthenticatePassword(email, password)`, `CreatePassword(email, password)`, `StartPasswordReset(email, redirectUrl) : Task`, `CompletePasswordReset(token, password)`, `RefreshSession(sessionJwt)`, `RevokeSession(sessionJwt) : Task`, `SetExternalId(userId, externalId) : Task`, `DeleteUser(externalId) : Task`, and four used only by the development mint below — `FindUserId(externalId) : Task<string?>`, `CreateUser(email, externalId) : Task<string>`, `CreateEmbeddableToken(userId) : Task<string>`, `AuthenticateMagicLink(token) : Task<StytchSession>` — each authenticate returns `StytchSession(string SessionJwt, StytchUser User)`, `StytchUser(string UserId, string? ExternalId, IReadOnlyList<StytchEmail> Emails)`, `StytchEmail(string Email)`; failures throw `StytchException(int Status, string ErrorType, string Message)`.
- `Sessions(StytchClient, AccountRepository).Complete(StytchSession) : Task<SessionResponse>` — the one path every login takes: read `user.external_id`; when present, make sure the account row exists and hand back the JWT Stytch just minted, which the claim template already stamped — **one Stytch call, no more**; when absent, create the account row (`Guid.CreateVersion7()`, name = the email's local part), `PUT /v1/users/{user_id} {external_id}`, and then re-mint the JWT once with `POST /v1/sessions/authenticate` (no body beyond `session_jwt` and `session_duration_minutes`). `SessionResponse(string SessionJwt, AccountRow Account)`.
- **Why the re-mint, and only there** — the template reads `user.external_id`, and on a brand-new account the session is minted by the very call that consumes the OTP code or the signup password, *before* we know the account id, so that first JWT has no `account_id`. Stytch's custom-claim-template page is explicit that this is fixable and how: *"Updates to the custom claim template or user metadata propagate to existing sessions the next time a JWT is minted"* and *"A JWT cannot be modified once it is minted. You must authenticate again to update claims."* So one `sessions/authenticate` after the link re-runs the template against the now-linked user. The alternative — never mint until the user is linked — cannot work for OTP: the code is single-use, so the session must be minted by the call that spends it, and linking earlier at `POST /v1/auth/otp` would create an account row for any email anyone types. The cost is one extra call on the first login of an account and never again: a returning login and a refresh are one Stytch call each.
- Endpoints (anonymous unless noted): `POST /v1/auth/otp {email} → {methodId}`; `POST /v1/auth/otp/verify {methodId, code} → SessionResponse`; `POST /v1/auth/password {email, password} → SessionResponse`; `POST /v1/auth/password/signup {email, password} → 201 SessionResponse`; `POST /v1/auth/password/reset {email} → 202`; `POST /v1/auth/password/reset/complete {token, password} → SessionResponse`; `POST /v1/auth/session/refresh {sessionJwt} → SessionResponse` (an expired JWT is accepted while the Stytch session lives); `DELETE /v1/auth/session` (bearer) `→ 204`. Stytch `401/404` → `401`; `duplicate_email` → `409`; `weak_password`/other `4xx` → `422`; `5xx` → `502`.
- **`POST /v1/dev/sessions` — the development-only mint.** Body `DevSessionRequest(string Name)`, response the ordinary `SessionResponse(SessionJwt, Account)`. Owner ruling 4 of 2026-09-06: the simulator's socket driver needs a real session token per team, hand-pasting them is rejected, and management is the only process holding the Stytch secret, so the mint lives here and returns a **real Stytch session JWT**. `DevSessions.Mint(name)` does four things: derives the account id from the name (`AccountIdFor`, a name-based UUIDv8 — SHA-256 over a fixed namespace and the name, the first sixteen bytes, version and variant stamped), inserts the `account` row (`on conflict do nothing`, so a repeated name is the same row), finds the Stytch user by `GET /v1/users/{accountId}` and creates it with `POST /v1/users {email, external_id = accountId}` when that is `404`, then mints by `POST /v1/magic_links` followed by `POST /v1/magic_links/authenticate` and hands the session to `Sessions.Complete` like every other login. Because `external_id` is set at user creation, the claim template renders `account_id` on the very first mint and `Complete` takes its one-call branch — the re-mint below never fires here.
  - **Gating is the hosting environment, not a flag.** The route is registered inside `if (app.Environment.IsDevelopment())` and the service inside `if (builder.Environment.IsDevelopment())`. `IHostEnvironment.EnvironmentName` defaults to `Production` when neither `ASPNETCORE_ENVIRONMENT` nor `DOTNET_ENVIRONMENT` is set, so a published build serves no such route unless someone deliberately names the environment `Development`. There is no configuration key, so no deployment can turn it on by mistake, and the test host's environment is `Testing`, which is also not development.
  - **An account that does not exist is created.** The name is the input and the account id is a function of it, so the endpoint has nothing to look up and nothing to refuse: the row is inserted if absent and reused if present. That is what lets `SocketRowSeeder` create its own accounts instead of requiring three to exist first.
  - **Idempotent for a repeated name.** Same name → same account id → same `account` row → same Stytch user, found by `external_id` rather than created again. Only the JWT differs, because each mint is a fresh session. A first mint costs four Stytch calls, every later one three.
  - **The email is derived and never used.** `EmailFor(name)` is the name lowercased with every non-alphanumeric character replaced by `-`, at `example.com` (RFC 2606, reserved and undeliverable). Nothing in this path sends mail: `POST /v1/users` does not, and an embeddable magic link is explicitly the endpoint that hands the token back instead of emailing it. Two different names that slug to the same address collide as Stytch `duplicate_email` → `409`.
  - **No second signing key and no local trust anchor.** The JWT is minted by Stytch, signed by Stytch's key, and verified by both servers against Stytch's JWKS exactly as a player's is. That is the whole point: a bypass in the verifier would leave the socket driver testing something the product does not do.
- Stytch endpoints, read from the Consumer API docs on 09-05 and re-checked page by page on 09-06: `POST /v1/otps/email/login_or_create` (`email`, `expiration_minutes` 1–10, default 2; returns `user_id`, **`email_id`**, `user_created` — there is no `method_id` in the response, and `email_id` is what the next call sends as `method_id`); `POST /v1/otps/authenticate` (`method_id`, `code`, `session_duration_minutes`, range 5–527040); `POST /v1/passwords/authenticate` (`email`, `password`); `POST /v1/passwords` (`email`, `password`; error types `duplicate_email`, `weak_password` for a strength score ≤ 2, and `breached_password` for a leaked one — two distinct errors, both `4xx`, both `422` here); `POST /v1/passwords/email/reset/start` (`email`, `reset_password_redirect_url`); `POST /v1/passwords/email/reset` (`token`, `password`); `POST /v1/sessions/authenticate` (`session_jwt`, `session_duration_minutes` — we send no `session_custom_claims`; "You may provide a JWT that needs to be refreshed and is expired according to its `exp` claim", which is what `/session/refresh` relies on, and the response's `session_jwt` is freshly minted, so the claim template runs again); `POST /v1/sessions/revoke` (**exactly one** of `session_id`, `session_token`, `session_jwt` — sending more than one is an error, so only `session_jwt` goes); `PUT /v1/users/{user_id}` (`external_id`: alphanumeric, `.`, `_`, `-`, `|`, ≤128 — a GUID string fits); `DELETE /v1/users/{user_id}` ("You may use an `external_id` here if one is set for the user"). All Basic-auth `project_id:secret` against `https://test.stytch.com`.
- Four more, read on 09-06 and used only by `POST /v1/dev/sessions`: `GET /v1/users/{user_id}` (the same external-id substitution as the delete, so the account id is the lookup key and `404` is `user_not_found`); `POST /v1/users` (`email` required, `external_id` optional and accepting a GUID string, `create_user_as_pending` left false so the user is `active`; returns `user_id`, `email_id`, `status`, and `duplicate_email` when the address is taken); `POST /v1/magic_links` (embeddable magic links, **Consumer API only, absent from the B2B API** — `user_id`, which "You may substitute an `external_id`" for, plus `expiration_minutes` 5–10080 default 60; returns `token` and **sends no email**, which is what makes it usable from a server); `POST /v1/magic_links/authenticate` (`token`, `session_duration_minutes`, and no `session_custom_claims` for the same reason the other authenticates send none; returns `session_jwt`, `session_token` and the user). The pair is Stytch's documented server-side way to mint a session for a user you already know, and the authenticate is the same endpoint a real emailed magic link would hit.
- Signup, refresh and reset-complete are three endpoints the spec's list lacks — spec correction 2.

Spec: management API → Authentication (the five endpoints; "OTP and password are Basic-auth Stytch endpoints requiring the project secret, so they run server-side"); accounts → Auth is a module ("a Stytch session JWT carrying our account UUID comes back"); client shell → No social login; `architecture.md` → Game management data model ("Stytch's `external_id` is set to `Account.id`").

- [ ] **Step 1: Write `FakeStytch.cs` — an in-memory Stytch behind an `HttpMessageHandler`**

```csharp
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tabletop.Management.Tests;

/// <summary>Enough of Stytch to log in: users by email, one OTP code, passwords, external ids, sessions minted with TestKeys — including the project's claim template.</summary>
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

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        Assert.StartsWith("Basic ", req.Headers.Authorization?.ToString());
        var body = req.Content is null ? new JsonObject() : JsonNode.Parse(await req.Content.ReadAsStringAsync(ct))!.AsObject();
        var path = req.RequestUri!.AbsolutePath;
        Calls.Add(path);
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
                return Session(u, body);
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

    /// <summary>Minting runs the project's custom claim template: account_id = user.external_id, or no claim when the user is not linked yet.</summary>
    private HttpResponseMessage Session(User u, JsonObject body)
    {
        Assert.Null(body["session_custom_claims"]);   // the template stamps the claim; nothing may send it per call
        var jwt = TestKeys.MintForUser(u.Id, Guid.TryParse(u.ExternalId, out var linked) ? linked : null);
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
        Assert.Equal(1, _f.Stytch.Authenticates - before);        // exactly one re-mint, because external_id was written on this login
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
        Assert.Equal(0, _f.Stytch.Authenticates - beforeLogin);    // no re-mint: the user was already linked, so the template stamped the claim
        Assert.Equal(again!.Account.Id.ToString(), Claim(again.SessionJwt));

        var beforeRefresh = _f.Stytch.Authenticates;
        var refreshed = await (await c.PostAsJsonAsync("/v1/auth/session/refresh", new { sessionJwt = again.SessionJwt })).Content.ReadFromJsonAsync<SessionResponse>();
        Assert.Equal(1, _f.Stytch.Authenticates - beforeRefresh);   // the refresh itself and nothing after it
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
    /// <summary>Extends the session and mints a fresh JWT; the project's claim template runs on every mint, so nothing is passed here.</summary>
    public Task<StytchSession> RefreshSession(string sessionJwt) =>
        Session(HttpMethod.Post, "/v1/sessions/authenticate", new { session_jwt = sessionJwt, session_duration_minutes = options.SessionMinutes });
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
        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        var json = text.Length == 0 ? JsonDocument.Parse("{}").RootElement : JsonDocument.Parse(text).RootElement.Clone();
        if (response.IsSuccessStatusCode) return json;
        var type = json.TryGetProperty("error_type", out var t) ? t.GetString() ?? "unknown" : "unknown";
        var message = json.TryGetProperty("error_message", out var m) ? m.GetString() ?? type : type;
        throw new StytchException((int)response.StatusCode, type, message);
    }
}
```

- [ ] **Step 4: Write `Sessions.cs`**

```csharp
using Tabletop.Management.Http;

namespace Tabletop.Management.Identity;

public sealed record SessionResponse(string SessionJwt, AccountRow Account);
public sealed record OtpSentResponse(string MethodId);

/// <summary>After any Stytch authenticate: make sure the account exists and is linked to the Stytch user. The JWT's account_id comes from the project's claim template.</summary>
public sealed class Sessions(StytchClient stytch, AccountRepository accounts)
{
    public async Task<SessionResponse> Complete(StytchSession session)
    {
        var name = session.User.Emails.FirstOrDefault()?.Email.Split('@')[0] ?? "player";
        var jwt = session.SessionJwt;
        Guid accountId;
        if (Guid.TryParse(session.User.ExternalId, out var linked))
        {
            accountId = linked;
            await accounts.Insert(accountId, name);                      // no-op when the row exists
        }
        else
        {
            accountId = Guid.CreateVersion7();
            await accounts.Insert(accountId, name);
            await stytch.SetExternalId(session.User.UserId, accountId.ToString());
            // The session above was minted before the user was linked, so its JWT has no account_id.
            // Re-minting is the documented way to pick the template up ("You must authenticate again to update claims").
            // First login only; every later login and every refresh skips this branch.
            jwt = (await stytch.RefreshSession(jwt)).SessionJwt;
        }
        var account = await accounts.Get(accountId) ?? throw new InvalidOperationException("account vanished");
        return new SessionResponse(jwt, account);
    }

    public static IResult Problem(StytchException e) => e.Status switch
    {
        401 or 404 => Results.Problem(title: "Unauthorized", detail: e.ErrorType, statusCode: 401),
        _ when e.ErrorType == "duplicate_email" => Problems.Conflict("An account with that email already exists."),
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
            try { await s.RevokeSession(jwt); } catch (StytchException e) when (e.Status is 401 or 404) { }
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
        // external_id was set at creation, so the claim template stamps account_id on this first mint and Complete makes no second call.
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
        Assert.Equal(0, _dev.Stytch.Authenticates);                                                    // no re-mint: the template stamped the claim
        c.DefaultRequestHeaders.Authorization = new("Bearer", second.SessionJwt);
        Assert.Equal(first.Account.Id, (await c.GetFromJsonAsync<AccountRow>("/v1/account"))!.Id);
    }
}
```

The second test is what proves the endpoint is on the real path: the JWT it returns is verified by the same `JwtBearer` handler against the same JWKS as a player's, and `GET /v1/account` accepts it without knowing where it came from.

- [ ] **Step 7: Wire `Program.cs`** — `builder.Services.AddHttpClient(StytchClient.HttpClientName); builder.Services.AddSingleton(sp => new StytchClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(StytchClient.HttpClientName), options)); builder.Services.AddSingleton<Sessions>(); if (builder.Environment.IsDevelopment()) builder.Services.AddSingleton<DevSessions>();` and `app.MapAuth(); if (app.Environment.IsDevelopment()) app.MapDevSession();`.

- [ ] **Step 8: Run `dotnet test tests/Tabletop.Management.Tests`; expect 26 passed. Then the real thing once: `dotnet user-secrets set STYTCH_SECRET <secret>` in `server/management`, `dotnet run --project server/management`, `curl -X POST localhost:5100/v1/auth/otp -H 'content-type: application/json' -d '{"email":"<your address>"}'`, verify with the emailed code, and check the JWT at jwt.io carries `account_id`. That last check is the only thing that proves the dashboard claim template is in place — set it before running this (*Interfaces needed from other modules → Stytch dashboard*), and if `account_id` is absent it is the template, not the code. While the secret is set, mint once too: `ASPNETCORE_ENVIRONMENT=Development dotnet run --project server/management`, then `curl -X POST localhost:5100/v1/dev/sessions -H 'content-type: application/json' -d '{"name":"team-1"}'`, and check the returned JWT the same way. Repeat it and confirm the same `account.id` comes back. Commit: `Log in through Stytch by email code or password, and mint a development session for a named account`.**

**What the mint costs.** A `--driver socket` simulator run now needs network access to the Stytch test project, because every token it uses is minted there. The in-process driver needs none, and neither does the rest of this suite, which runs against `FakeStytch`. That is the price of keeping the socket driver on the real verification path. It falls on console runs only: the simulator's own tests mint with `TestKeys` against a fake JWKS and reach no network, as does every test here.

### Task 6: The blob store and the GLB header read

**Files:**
- Create: `server/management/Content/Assets/IBlobStore.cs`, `server/management/Content/Assets/S3BlobStore.cs`, `server/management/Content/Assets/GlbHeader.cs`
- Modify: `server/management/Program.cs`
- Create: `tests/Tabletop.Management.Tests/TestGlb.cs`, `tests/Tabletop.Management.Tests/GlbHeaderTests.cs`, `tests/Tabletop.Management.Tests/BlobStoreTests.cs`

**Interfaces:**
- Consumes: `TABLETOP_S3_*` from `.env` (MinIO at `http://localhost:9000`, bucket `tabletop-blobs`, path-style — contracts plan Task 15).
- Produces: `IBlobStore` with `static string Key(Guid ownerId, string hash) => $"{ownerId}/{hash}"`, `Task<Uri> PresignUpload(string key, string contentType, long byteSize, TimeSpan ttl)`, `Task<Uri> PresignDownload(string key, TimeSpan ttl)`, `Task<long?> Size(string key)` (null when absent), `Task<byte[]> ReadRange(string key, long first, long lastInclusive)`, `Task Delete(IReadOnlyList<string> keys)`; `S3BlobStore(ManagementOptions)`; `GlbHeader.IsGlb(ReadOnlySpan<byte> first12)`, `GlbHeader.JsonChunkLength(ReadOnlySpan<byte> first20) : int`, `GlbHeader.CountTriangles(ReadOnlySpan<byte> jsonChunk) : long`, `GlbHeader.HeaderBytes = 20`, `GlbHeader.WarnAbove = 100_000`.
- The triangle total is the sum over every mesh primitive of `accessors[indices ?? attributes.POSITION].count` converted by `mode` (4 → `count/3`; 5 and 6 → `count − 2`; points and lines → 0). Meshes are counted once each, not per node, because that is what a client loads into memory.
- Presigned URLs are minted against `TABLETOP_S3_ENDPOINT` exactly, so the same URL works from a WSL process and the Windows client at `localhost:9000` (contracts plan → Local development). Upload URLs are valid 15 minutes; download URLs 60.
- **Single-object `PUT` only.** The management API's *Still open* — *"whether asset uploads need multipart for large files, which presigned `PUT` does not cover on its own"* — stays open and is not pre-empted here: `IBlobStore` has no multipart member, and adding one later is a second presign method plus a complete-upload call, neither of which changes the three endpoints. A GLB the converter produced is a few megabytes, well inside a single `PUT`.

Spec: management API → Asset bytes never pass through the API, The triangle count is a ranged read of the GLB header ("A GLB's first chunk is its JSON … the triangle total is a sum over the index accessors"), GLB only; mesh conversion → The threshold (100,000; never blocks); `architecture.md` → GLB only.

- [ ] **Step 1: Write `TestGlb.cs` and the failing unit tests**

```csharp
using System.Text;

namespace Tabletop.Management.Tests;

/// <summary>Builds a structurally valid GLB whose JSON chunk declares the given primitives. The binary chunk is empty.</summary>
public static class TestGlb
{
    public static byte[] Build(params (int count, int? mode, bool indexed)[] primitives)
    {
        var accessors = string.Join(",", primitives.Select(p => $"{{\"componentType\":5123,\"count\":{p.count},\"type\":\"SCALAR\"}}"));
        var prims = string.Join(",", primitives.Select((p, i) =>
            (p.indexed ? $"{{\"attributes\":{{\"POSITION\":{i}}},\"indices\":{i}" : $"{{\"attributes\":{{\"POSITION\":{i}}}") + (p.mode is null ? "}" : $",\"mode\":{p.mode}}}")));
        var json = Encoding.UTF8.GetBytes($"{{\"asset\":{{\"version\":\"2.0\"}},\"accessors\":[{accessors}],\"meshes\":[{{\"primitives\":[{prims}]}}]}}");
        var padded = json.Length % 4 == 0 ? json : json.Concat(Enumerable.Repeat((byte)0x20, 4 - json.Length % 4)).ToArray();
        using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
        w.Write(0x46546C67u); w.Write(2u); w.Write((uint)(12 + 8 + padded.Length));
        w.Write((uint)padded.Length); w.Write(0x4E4F534Au); w.Write(padded);
        return ms.ToArray();
    }
}
```

```csharp
using Tabletop.Management.Content.Assets;

namespace Tabletop.Management.Tests;

public class GlbHeaderTests
{
    [Fact]
    public void Recognises_the_magic_and_reads_the_json_length()
    {
        var glb = TestGlb.Build((36, null, true));
        Assert.True(GlbHeader.IsGlb(glb.AsSpan(0, 12)));
        Assert.False(GlbHeader.IsGlb("solid cube\n\n"u8));
        Assert.Equal(BitConverter.ToInt32(glb, 12), GlbHeader.JsonChunkLength(glb.AsSpan(0, 20)));
    }

    [Theory]
    [InlineData(36, null, true, 12)]      // indexed triangles
    [InlineData(30, 4, false, 10)]        // unindexed triangles from POSITION count
    [InlineData(10, 5, true, 8)]          // triangle strip
    [InlineData(10, 6, true, 8)]          // triangle fan
    [InlineData(10, 1, true, 0)]          // lines count nothing
    public void Counts_triangles_per_mode(int count, int? mode, bool indexed, long expected)
    {
        var glb = TestGlb.Build((count, mode, indexed));
        var len = GlbHeader.JsonChunkLength(glb.AsSpan(0, 20));
        Assert.Equal(expected, GlbHeader.CountTriangles(glb.AsSpan(20, len)));
    }

    [Fact]
    public void Sums_over_primitives()
    {
        var glb = TestGlb.Build((300_003, null, true), (3, null, true));
        Assert.Equal(100_002, GlbHeader.CountTriangles(glb.AsSpan(20, GlbHeader.JsonChunkLength(glb.AsSpan(0, 20)))));
        Assert.True(100_002 > GlbHeader.WarnAbove);
    }
}
```

- [ ] **Step 2: Write the failing MinIO test**

```csharp
using Microsoft.Extensions.Configuration;
using Tabletop.Management.Configuration;
using Tabletop.Management.Content.Assets;

namespace Tabletop.Management.Tests;

public class BlobStoreTests : IAsyncLifetime
{
    private readonly S3BlobStore _store;
    private readonly string _key = IBlobStore.Key(Guid.CreateVersion7(), "sha256-test");
    public BlobStoreTests() { DotEnv.Load(); _store = new S3BlobStore(ManagementOptions.From(new ConfigurationBuilder().AddEnvironmentVariables("TABLETOP_").AddInMemoryCollection(new Dictionary<string, string?> { ["STYTCH_PROJECT_ID"] = "p", ["STYTCH_SECRET"] = "s" }).Build())); }

    [Fact]
    public async Task Presigned_put_then_ranged_read_then_presigned_get()
    {
        var glb = TestGlb.Build((36, null, true));
        Assert.Null(await _store.Size(_key));
        using var http = new HttpClient();
        var put = await http.PutAsync(await _store.PresignUpload(_key, "model/gltf-binary", glb.Length, TimeSpan.FromMinutes(5)), new ByteArrayContent(glb) { Headers = { ContentType = new("model/gltf-binary") } });
        Assert.True(put.IsSuccessStatusCode, await put.Content.ReadAsStringAsync());
        Assert.Equal(glb.Length, await _store.Size(_key));
        var head = await _store.ReadRange(_key, 0, GlbHeader.HeaderBytes - 1);
        Assert.Equal(GlbHeader.HeaderBytes, head.Length);
        Assert.True(GlbHeader.IsGlb(head.AsSpan(0, 12)));
        var got = await http.GetByteArrayAsync(await _store.PresignDownload(_key, TimeSpan.FromMinutes(5)));
        Assert.Equal(glb, got);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => _store.Delete([_key]);
}
```

`Tabletop.Management.Tests.csproj` gains `<PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="10.0.11" />` (already transitive via Mvc.Testing; the explicit reference keeps the `using` resolvable).

- [ ] **Step 3: Run; expect compile errors. Write `GlbHeader.cs`:**

```csharp
using System.Buffers.Binary;
using System.Text.Json;

namespace Tabletop.Management.Content.Assets;

/// <summary>The only server-side look at an upload: the 12-byte header, the 8-byte chunk header, then the JSON chunk.</summary>
public static class GlbHeader
{
    public const uint Magic = 0x46546C67;       // "glTF"
    public const uint JsonChunkType = 0x4E4F534A; // "JSON"
    public const int HeaderBytes = 20;
    public const long WarnAbove = 100_000;

    public static bool IsGlb(ReadOnlySpan<byte> first12) =>
        first12.Length >= 12 && BinaryPrimitives.ReadUInt32LittleEndian(first12) == Magic && BinaryPrimitives.ReadUInt32LittleEndian(first12[4..]) == 2;

    public static int JsonChunkLength(ReadOnlySpan<byte> first20)
    {
        if (first20.Length < HeaderBytes || BinaryPrimitives.ReadUInt32LittleEndian(first20[16..]) != JsonChunkType)
            throw new FormatException("First chunk is not JSON.");
        return checked((int)BinaryPrimitives.ReadUInt32LittleEndian(first20[12..]));
    }

    public static long CountTriangles(ReadOnlySpan<byte> jsonChunk)
    {
        using var doc = JsonDocument.Parse(jsonChunk.ToArray());
        var root = doc.RootElement;
        if (!root.TryGetProperty("meshes", out var meshes) || !root.TryGetProperty("accessors", out var accessors)) return 0;
        long total = 0;
        foreach (var mesh in meshes.EnumerateArray())
            foreach (var prim in mesh.GetProperty("primitives").EnumerateArray())
            {
                var mode = prim.TryGetProperty("mode", out var m) ? m.GetInt32() : 4;
                int accessor = prim.TryGetProperty("indices", out var idx) ? idx.GetInt32()
                    : prim.TryGetProperty("attributes", out var attrs) && attrs.TryGetProperty("POSITION", out var pos) ? pos.GetInt32() : -1;
                if (accessor < 0) continue;
                long count = accessors[accessor].GetProperty("count").GetInt64();
                total += mode switch { 4 => count / 3, 5 or 6 => Math.Max(0, count - 2), _ => 0 };
            }
        return total;
    }
}
```

- [ ] **Step 4: Write `IBlobStore.cs` and `S3BlobStore.cs`**

```csharp
namespace Tabletop.Management.Content.Assets;

/// <summary>Content-addressed bytes, per owner. Presigned URLs both ways; the server itself reads only ranges.</summary>
public interface IBlobStore
{
    static string Key(Guid ownerId, string hash) => $"{ownerId}/{hash}";
    Task<Uri> PresignUpload(string key, string contentType, long byteSize, TimeSpan ttl);
    Task<Uri> PresignDownload(string key, TimeSpan ttl);
    Task<long?> Size(string key);
    Task<byte[]> ReadRange(string key, long first, long lastInclusive);
    Task Delete(IReadOnlyList<string> keys);
}
```

```csharp
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Tabletop.Management.Configuration;

namespace Tabletop.Management.Content.Assets;

public sealed class S3BlobStore(ManagementOptions options) : IBlobStore
{
    private readonly AmazonS3Client _s3 = new(new BasicAWSCredentials(options.S3AccessKey, options.S3SecretKey),
        new AmazonS3Config { ServiceURL = options.S3Endpoint, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
    private readonly bool _http = options.S3Endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    public async Task<Uri> PresignUpload(string key, string contentType, long byteSize, TimeSpan ttl) => new(await _s3.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        { BucketName = options.S3Bucket, Key = key, Verb = HttpVerb.PUT, ContentType = contentType, Expires = DateTime.UtcNow.Add(ttl), Protocol = _http ? Protocol.HTTP : Protocol.HTTPS }));

    public async Task<Uri> PresignDownload(string key, TimeSpan ttl) => new(await _s3.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        { BucketName = options.S3Bucket, Key = key, Verb = HttpVerb.GET, Expires = DateTime.UtcNow.Add(ttl), Protocol = _http ? Protocol.HTTP : Protocol.HTTPS }));

    public async Task<long?> Size(string key)
    {
        try { return (await _s3.GetObjectMetadataAsync(options.S3Bucket, key)).ContentLength; }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound) { return null; }
    }

    public async Task<byte[]> ReadRange(string key, long first, long lastInclusive)
    {
        using var response = await _s3.GetObjectAsync(new GetObjectRequest { BucketName = options.S3Bucket, Key = key, ByteRange = new ByteRange(first, lastInclusive) });
        using var ms = new MemoryStream();
        await response.ResponseStream.CopyToAsync(ms);
        return ms.ToArray();
    }

    public async Task Delete(IReadOnlyList<string> keys)
    {
        foreach (var chunk in keys.Chunk(1000))
            await _s3.DeleteObjectsAsync(new DeleteObjectsRequest { BucketName = options.S3Bucket, Objects = chunk.Select(k => new KeyVersion { Key = k }).ToList() });
    }
}
```

- [ ] **Step 5: Wire `Program.cs`** — `builder.Services.AddSingleton<Content.Assets.IBlobStore>(new Content.Assets.S3BlobStore(options));`.

- [ ] **Step 6: Run `dotnet test tests/Tabletop.Management.Tests`; expect 34 passed (MinIO must be up). Commit: `Add the S3 blob store over MinIO and the GLB header triangle count`.**

### Task 7: Assets — create, confirm, download, library CRUD, volumes

**Files:**
- Create: `server/management/Content/Assets/AssetDtos.cs`, `server/management/Content/VolumeRows.cs`, `server/management/Content/Assets/AssetRepository.cs`, `server/management/Content/Assets/AssetEndpoints.cs`
- Modify: `server/management/Program.cs`
- Create: `tests/Tabletop.Management.Tests/AssetTests.cs`

**Interfaces:**
- Consumes: `IBlobStore`, `GlbHeader`, `Keyset`, `Page<T>`, `Problems`, `CurrentAccount`; `Tabletop.Geometry.Base`, `Orientation`, `Volume`, `VolumeKind`, `Footprint`, `FootprintKind`, `TerrainDeclarations`; `Tabletop.Content.TerrainVolume`.
- Produces: `AssetKind { Miniature, Terrain, Texture }`; `AssetDto(Guid Id, string Name, AssetKind Kind, string Hash, long ByteSize, string ContentType, bool Uploaded, double Scale, Orientation Orientation, Base? ModelledBase)`; `CreateAssetRequest(string Name, AssetKind Kind, string Hash, long ByteSize, string ContentType, double Scale, Orientation Orientation, Base? ModelledBase)`; `CreateAssetResponse(AssetDto Asset, Uri? UploadUrl)` (null URL = the bytes are already there); `ConfirmResponse(AssetDto Asset, long? Triangles, string? Warning)`; `PatchAssetRequest(string? Name, double? Scale, Orientation? Orientation, Base? ModelledBase)`; `DownloadResponse(Uri Url, DateTimeOffset ExpiresAt)`; `VolumeRow` with `Volume ToVolume()` and `static VolumeRow From(Guid id, Volume v)` (shared with layouts); `AssetRepository(Database)` with `Create`, `Get`, `List`, `Patch`, `MarkUploaded`, `Delete` (returns the S3 key when the blob became unreferenced), `GetVolumes`, `PutVolumes`, `BlobKey`; the nine endpoints below.
- Endpoints: `GET /v1/assets?limit&after&kind → Page<AssetDto>`; `POST /v1/assets → 201 CreateAssetResponse`; `GET /v1/assets/{id}`; `PATCH /v1/assets/{id}`; `DELETE /v1/assets/{id} → 204`; `POST /v1/assets/{id}/confirm → ConfirmResponse`; `GET /v1/assets/{id}/download → DownloadResponse` (`409` until confirmed); `GET /v1/assets/{id}/volumes → TerrainVolume[]`; `PUT /v1/assets/{id}/volumes → TerrainVolume[]` (`409` unless kind is terrain).
- Rules: `hash` is 64 lowercase hex (SHA-256 the client computed); miniature and terrain accept only `model/gltf-binary`, texture `image/png`, `image/jpeg` or `image/webp`, anything else `415`; a miniature carries a `ModelledBase`, the other kinds do not (`schema.md`'s check). Confirm: `Size(key)` null → `409`; size ≠ `byteSize` → `422`; for a GLB, a wrong magic → the object is deleted and `422` (an STL renamed `.glb` is rejected here); then the JSON chunk is read and `Triangles` counted; `Warning` is set above `GlbHeader.WarnAbove` and nothing is refused. Confirm on an already-uploaded blob (the dedup case) still runs the header read so the caller gets the count.

Spec: management API → Asset bytes never pass through the API (the three endpoints; "a duplicate upload be short-circuited before any bytes move"), The triangle count, GLB only, The rest of the surface (assets, volumes); asset library → Two records, The modelled base suggests; identity and enumerations → `ASSET.kind`, profiles per kind ("texture: nothing"); geometry values via the contracts plan's `Volume`; mesh conversion → What it does not do (the browser writes the bounding-box volume with `PUT /v1/assets/{id}/volumes` at confirm).

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using System.Net.Http.Json;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Http;

namespace Tabletop.Management.Tests;

public class AssetTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public AssetTests(ManagementFactory f) => _f = f;

    static string Sha(byte[] b) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(b));
    static CreateAssetRequest Mini(byte[] glb, string name = "Marine") =>
        new(name, AssetKind.Miniature, Sha(glb), glb.Length, "model/gltf-binary", 1.0, new Orientation(0, 0, 0), Base.Circle(1.26));

    async Task<AssetDto> Upload(HttpClient c, byte[] glb, string name = "Marine")
    {
        var created = await c.PostJson<CreateAssetResponse>("/v1/assets", Mini(glb, name));
        if (created!.UploadUrl is not null)
            using (var http = new HttpClient()) Assert.True((await http.PutAsync(created.UploadUrl, new ByteArrayContent(glb) { Headers = { ContentType = new("model/gltf-binary") } })).IsSuccessStatusCode);
        var confirmed = await c.PostJson<ConfirmResponse>($"/v1/assets/{created.Asset.Id}/confirm", new { });
        return confirmed!.Asset;
    }

    [Fact]
    public async Task Create_upload_confirm_download()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var glb = TestGlb.Build((36, null, true));
        var created = await c.PostJson<CreateAssetResponse>("/v1/assets", Mini(glb));
        Assert.NotNull(created!.UploadUrl); Assert.False(created.Asset.Uploaded);
        Assert.Equal(HttpStatusCode.Conflict, (await c.GetAsync($"/v1/assets/{created.Asset.Id}/download")).StatusCode);
        using (var http = new HttpClient()) await http.PutAsync(created.UploadUrl, new ByteArrayContent(glb) { Headers = { ContentType = new("model/gltf-binary") } });
        var confirmed = await c.PostJson<ConfirmResponse>($"/v1/assets/{created.Asset.Id}/confirm", new { });
        Assert.Equal(12, confirmed!.Triangles); Assert.Null(confirmed.Warning); Assert.True(confirmed.Asset.Uploaded);
        var dl = await c.GetJson<DownloadResponse>($"/v1/assets/{created.Asset.Id}/download");
        using var http2 = new HttpClient();
        Assert.Equal(glb, await http2.GetByteArrayAsync(dl!.Url));
    }

    [Fact]
    public async Task Same_bytes_twice_short_circuits_the_upload_and_deleting_the_last_reference_removes_the_blob()
    {
        var owner = Guid.CreateVersion7();
        var c = await _f.AuthenticatedClient(owner);
        var glb = TestGlb.Build((3, null, true));
        var first = await Upload(c, glb, "A");
        var second = await c.PostJson<CreateAssetResponse>("/v1/assets", Mini(glb, "B"));
        Assert.Null(second!.UploadUrl); Assert.True(second.Asset.Uploaded);
        var store = _f.Services.GetRequiredService<IBlobStore>();
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/v1/assets/{first.Id}")).StatusCode);
        Assert.NotNull(await store.Size(IBlobStore.Key(owner, first.Hash)));
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/v1/assets/{second.Asset.Id}")).StatusCode);
        Assert.Null(await store.Size(IBlobStore.Key(owner, first.Hash)));
    }

    [Fact]
    public async Task Stl_is_rejected_and_a_dense_glb_only_warns()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var stl = "solid cube\nendsolid cube\n"u8.ToArray();
        var req = Mini(stl) with { Name = "not a glb" };
        var created = await c.PostJson<CreateAssetResponse>("/v1/assets", req);
        using (var http = new HttpClient()) await http.PutAsync(created!.UploadUrl, new ByteArrayContent(stl) { Headers = { ContentType = new("model/gltf-binary") } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsync($"/v1/assets/{created.Asset.Id}/confirm", null)).StatusCode);
        var dense = await c.PostJson<CreateAssetResponse>("/v1/assets", Mini(TestGlb.Build((300_003, null, true)), "dense"));
        using (var http = new HttpClient()) await http.PutAsync(dense!.UploadUrl, new ByteArrayContent(TestGlb.Build((300_003, null, true))) { Headers = { ContentType = new("model/gltf-binary") } });
        var confirmed = await c.PostJson<ConfirmResponse>($"/v1/assets/{dense.Asset.Id}/confirm", new { });
        Assert.Equal(100_001, confirmed!.Triangles); Assert.Contains("100,000", confirmed.Warning); Assert.True(confirmed.Asset.Uploaded);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await c.PostAsJsonAsync("/v1/assets", Mini(stl) with { ContentType = "model/stl" }, ContentJson.Options)).StatusCode);
    }

    [Fact]
    public async Task Library_is_owner_only_and_paged()
    {
        var mine = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var theirs = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++) ids.Add((await Upload(mine, TestGlb.Build((3 * (i + 1), null, true)), $"m{i}")).Id);
        var page1 = await mine.GetJson<Page<AssetDto>>("/v1/assets?limit=2");
        Assert.Equal(2, page1!.Items.Count); Assert.Equal(ids[1], page1.Next);
        var page2 = await mine.GetJson<Page<AssetDto>>($"/v1/assets?limit=2&after={page1.Next}");
        Assert.Equal([ids[2]], page2!.Items.Select(a => a.Id)); Assert.Null(page2.Next);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.GetAsync($"/v1/assets/{ids[0]}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.PatchJson($"/v1/assets/{ids[0]}", new PatchAssetRequest("x", null, null, null))).StatusCode);
        var patched = await mine.PatchJson<AssetDto>($"/v1/assets/{ids[0]}", new PatchAssetRequest("Renamed", 1.5, null, null));
        Assert.Equal(("Renamed", 1.5), (patched!.Name, patched.Scale));
    }

    [Fact]
    public async Task Terrain_volumes_round_trip_and_other_kinds_refuse_them()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var glb = TestGlb.Build((30, null, true));
        var terrain = await c.PostJson<CreateAssetResponse>("/v1/assets", Mini(glb, "Ruin") with { Kind = AssetKind.Terrain, ModelledBase = null });
        var volumes = new[] { new Volume(VolumeKind.Solid, Footprint.Rect(6, 4), new Point2(0, 0), new Yaw(0), 0, 3, TerrainDeclarations.BlocksLineOfSight | TerrainDeclarations.Cover, "ruined wall") };
        var saved = await c.PutJson<List<TerrainVolume>>($"/v1/assets/{terrain!.Asset.Id}/volumes", volumes);
        Assert.Single(saved!); Assert.Equal(volumes[0], saved[0].Volume);
        Assert.Equal(saved, await c.GetJson<List<TerrainVolume>>($"/v1/assets/{terrain.Asset.Id}/volumes"));
        var mini = await c.PostJson<CreateAssetResponse>("/v1/assets", Mini(glb, "Mini"));
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/v1/assets/{mini!.Asset.Id}/volumes", volumes, ContentJson.Options)).StatusCode);
    }
}
```

The tests above use `HttpJson.cs`, written now and used by every later test — the API's JSON conventions are `ContentJson.Options`, and the defaults would not read `"kind":"miniature"` into `AssetKind`:

```csharp
using System.Net.Http.Json;
using Tabletop.Content;

namespace Tabletop.Management.Tests;

public static class HttpJson
{
    public static async Task<T> PostJson<T>(this HttpClient c, string url, object body) => await Read<T>(await c.PostAsJsonAsync(url, body, ContentJson.Options));
    public static async Task<T> PutJson<T>(this HttpClient c, string url, object body) => await Read<T>(await c.PutAsJsonAsync(url, body, ContentJson.Options));
    public static async Task<T> PatchJson<T>(this HttpClient c, string url, object body) => await Read<T>(await c.PatchAsJsonAsync(url, body, ContentJson.Options));
    public static Task<HttpResponseMessage> PatchJson(this HttpClient c, string url, object body) => c.PatchAsJsonAsync(url, body, ContentJson.Options);
    public static async Task<T> GetJson<T>(this HttpClient c, string url) => await Read<T>(await c.GetAsync(url));

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(ContentJson.Options))!;
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `AssetDtos.cs` and `Content/VolumeRows.cs`:**

```csharp
using Tabletop.Geometry;

namespace Tabletop.Management.Content.Assets;

public enum AssetKind { Miniature, Terrain, Texture }

public sealed record AssetDto(Guid Id, string Name, AssetKind Kind, string Hash, long ByteSize, string ContentType, bool Uploaded, double Scale, Orientation Orientation, Base? ModelledBase);
public sealed record CreateAssetRequest(string Name, AssetKind Kind, string Hash, long ByteSize, string ContentType, double Scale, Orientation Orientation, Base? ModelledBase);
public sealed record CreateAssetResponse(AssetDto Asset, Uri? UploadUrl);
public sealed record ConfirmResponse(AssetDto Asset, long? Triangles, string? Warning);
public sealed record PatchAssetRequest(string? Name, double? Scale, Orientation? Orientation, Base? ModelledBase);
public sealed record DownloadResponse(Uri Url, DateTimeOffset ExpiresAt);

public static class AssetRules
{
    public static readonly IReadOnlyDictionary<AssetKind, string[]> ContentTypes = new Dictionary<AssetKind, string[]>
    {
        [AssetKind.Miniature] = ["model/gltf-binary"], [AssetKind.Terrain] = ["model/gltf-binary"], [AssetKind.Texture] = ["image/png", "image/jpeg", "image/webp"],
    };
    public static bool IsSha256(string hash) => hash.Length == 64 && hash.All(ch => ch is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static string Kind(AssetKind k) => k.ToString().ToLowerInvariant();
    public static AssetKind Kind(string k) => Enum.Parse<AssetKind>(k, ignoreCase: true);
    public static string Shape(BaseShape s) => s.ToString().ToLowerInvariant();
    public static BaseShape Shape(string s) => Enum.Parse<BaseShape>(s, ignoreCase: true);
}
```

```csharp
using Tabletop.Geometry;

namespace Tabletop.Management.Content;

/// <summary>asset_volume and terrain_volume share these columns. real columns are cast to float8 in SQL so they map to double.</summary>
public sealed record VolumeRow(Guid Id, string Kind, string Footprint, double DimA, double DimB, double OffsetX, double OffsetY, double Yaw, double Bottom, double Top,
    bool Impassable, bool BlocksLos, bool SeeInOut, bool Cover, bool Difficult, bool Dangerous, string? Notes)
{
    public const string Columns = "id as Id, kind as Kind, footprint as Footprint, dim_a::float8 as DimA, dim_b::float8 as DimB, offset_x::float8 as OffsetX, offset_y::float8 as OffsetY, yaw::float8 as Yaw, bottom::float8 as Bottom, top::float8 as Top, impassable as Impassable, blocks_los as BlocksLos, see_in_out as SeeInOut, cover as Cover, difficult as Difficult, dangerous as Dangerous, notes as Notes";
    public const string InsertColumns = "id, kind, footprint, dim_a, dim_b, offset_x, offset_y, yaw, bottom, top, impassable, blocks_los, see_in_out, cover, difficult, dangerous, notes";
    public const string InsertValues = "@Id, @Kind, @Footprint, @DimA, @DimB, @OffsetX, @OffsetY, @Yaw, @Bottom, @Top, @Impassable, @BlocksLos, @SeeInOut, @Cover, @Difficult, @Dangerous, @Notes";

    public Volume ToVolume()
    {
        var d = TerrainDeclarations.None;
        if (Impassable) d |= TerrainDeclarations.Impassable; if (BlocksLos) d |= TerrainDeclarations.BlocksLineOfSight; if (SeeInOut) d |= TerrainDeclarations.SeeInAndOut;
        if (Cover) d |= TerrainDeclarations.Cover; if (Difficult) d |= TerrainDeclarations.Difficult; if (Dangerous) d |= TerrainDeclarations.Dangerous;
        return new Volume(Kind == "solid" ? VolumeKind.Solid : VolumeKind.Area, new Footprint(Footprint == "circle" ? FootprintKind.Circle : FootprintKind.Rect, DimA, DimB),
            new Point2(OffsetX, OffsetY), new Yaw(Yaw), Bottom, Top, d, Notes);
    }

    public static VolumeRow From(Guid id, Volume v) => new(id, v.Kind == VolumeKind.Solid ? "solid" : "area", v.Footprint.Kind == FootprintKind.Circle ? "circle" : "rect",
        v.Footprint.A, v.Footprint.B, v.Offset.X, v.Offset.Y, v.Yaw.Degrees, v.Bottom, v.Top,
        v.Declarations.HasFlag(TerrainDeclarations.Impassable), v.Declarations.HasFlag(TerrainDeclarations.BlocksLineOfSight), v.Declarations.HasFlag(TerrainDeclarations.SeeInAndOut),
        v.Declarations.HasFlag(TerrainDeclarations.Cover), v.Declarations.HasFlag(TerrainDeclarations.Difficult), v.Declarations.HasFlag(TerrainDeclarations.Dangerous), v.Notes);
}
```

- [ ] **Step 3: Write `AssetRepository.cs`**

```csharp
using Dapper;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Persistence;

namespace Tabletop.Management.Content.Assets;

public sealed class AssetRepository(Database db)
{
    private sealed record Row(Guid Id, Guid OwnerId, string Name, string Kind, string BlobHash, long ByteSize, string ContentType, bool Uploaded, double Scale,
        double OrientationPitch, double OrientationYaw, double OrientationRoll, string? ModelledBaseShape, double? ModelledBaseA, double? ModelledBaseB)
    {
        public AssetDto ToDto() => new(Id, Name, AssetRules.Kind(Kind), BlobHash, ByteSize, ContentType, Uploaded, Scale, new Orientation(OrientationPitch, OrientationYaw, OrientationRoll),
            ModelledBaseShape is null ? null : new Base(AssetRules.Shape(ModelledBaseShape), ModelledBaseA!.Value, ModelledBaseB!.Value));
    }

    private const string Select = @"select a.id as Id, a.owner_id as OwnerId, a.name as Name, a.kind as Kind, a.blob_hash as BlobHash, b.byte_size as ByteSize, b.content_type as ContentType,
        b.uploaded as Uploaded, a.scale::float8 as Scale, a.orientation_pitch::float8 as OrientationPitch, a.orientation_yaw::float8 as OrientationYaw, a.orientation_roll::float8 as OrientationRoll,
        a.modelled_base_shape as ModelledBaseShape, a.modelled_base_a::float8 as ModelledBaseA, a.modelled_base_b::float8 as ModelledBaseB
        from asset a join blob b on b.owner_id = a.owner_id and b.hash = a.blob_hash";

    public async Task<AssetDto> Create(Guid ownerId, CreateAssetRequest r)
    {
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        await c.ExecuteAsync("insert into blob (owner_id, hash, s3_key, byte_size, content_type) values (@ownerId, @hash, @key, @size, @ct) on conflict (owner_id, hash) do nothing",
            new { ownerId, hash = r.Hash, key = IBlobStore.Key(ownerId, r.Hash), size = r.ByteSize, ct = r.ContentType }, tx);
        var id = Guid.CreateVersion7();
        await c.ExecuteAsync(@"insert into asset (id, owner_id, blob_hash, name, kind, scale, orientation_pitch, orientation_yaw, orientation_roll, modelled_base_shape, modelled_base_a, modelled_base_b)
            values (@id, @ownerId, @hash, @name, @kind, @scale, @p, @y, @r, @shape, @a, @b)",
            new { id, ownerId, hash = r.Hash, name = r.Name, kind = AssetRules.Kind(r.Kind), scale = r.Scale, p = r.Orientation.Pitch, y = r.Orientation.Yaw, r = r.Orientation.Roll,
                  shape = r.ModelledBase is { } mb ? AssetRules.Shape(mb.Shape) : null, a = r.ModelledBase?.A, b = r.ModelledBase?.B }, tx);
        await tx.CommitAsync();
        return (await Get(ownerId, id))!;
    }

    public async Task<AssetDto?> Get(Guid ownerId, Guid id)
    {
        await using var c = await db.Open();
        return (await c.QuerySingleOrDefaultAsync<Row>(Select + " where a.id = @id and a.owner_id = @ownerId", new { id, ownerId }))?.ToDto();
    }

    public async Task<IReadOnlyList<AssetDto>> List(Guid ownerId, Http.Keyset page, AssetKind? kind)
    {
        await using var c = await db.Open();
        var rows = await c.QueryAsync<Row>(Select + " where a.owner_id = @ownerId and (@after::uuid is null or a.id > @after::uuid) and (@kind::text is null or a.kind = @kind::text) order by a.id limit @limit",
            new { ownerId, after = page.After, kind = kind is null ? null : AssetRules.Kind(kind.Value), limit = page.Limit });
        return rows.Select(r => r.ToDto()).ToList();
    }

    public async Task<AssetDto?> Patch(Guid ownerId, Guid id, PatchAssetRequest p)
    {
        await using var c = await db.Open();
        var n = await c.ExecuteAsync(@"update asset set name = coalesce(@name, name), scale = coalesce(@scale, scale),
            orientation_pitch = coalesce(@p, orientation_pitch), orientation_yaw = coalesce(@y, orientation_yaw), orientation_roll = coalesce(@r, orientation_roll),
            modelled_base_shape = coalesce(@shape, modelled_base_shape), modelled_base_a = coalesce(@a, modelled_base_a), modelled_base_b = coalesce(@b, modelled_base_b)
            where id = @id and owner_id = @ownerId",
            new { id, ownerId, name = p.Name, scale = p.Scale, p = p.Orientation?.Pitch, y = p.Orientation?.Yaw, r = p.Orientation?.Roll,
                  shape = p.ModelledBase is { } mb ? AssetRules.Shape(mb.Shape) : null, a = p.ModelledBase?.A, b = p.ModelledBase?.B });
        return n == 1 ? await Get(ownerId, id) : null;
    }

    public async Task MarkUploaded(Guid ownerId, string hash)
    {
        await using var c = await db.Open();
        await c.ExecuteAsync("update blob set uploaded = true where owner_id = @ownerId and hash = @hash", new { ownerId, hash });
    }

    /// <summary>Deletes the asset; when it was the owner's last reference to the blob, deletes the blob row too and returns its S3 key.</summary>
    public async Task<(bool Found, string? OrphanedKey)> Delete(Guid ownerId, Guid id)
    {
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        var hash = await c.ExecuteScalarAsync<string?>("delete from asset where id = @id and owner_id = @ownerId returning blob_hash", new { id, ownerId }, tx);
        if (hash is null) return (false, null);
        var key = await c.ExecuteScalarAsync<string?>(@"delete from blob where owner_id = @ownerId and hash = @hash
            and not exists (select 1 from asset where owner_id = @ownerId and blob_hash = @hash) returning s3_key", new { ownerId, hash }, tx);
        await tx.CommitAsync();
        return (true, key);
    }

    public async Task<IReadOnlyList<TerrainVolume>> GetVolumes(Guid ownerId, Guid assetId)
    {
        await using var c = await db.Open();
        var rows = await c.QueryAsync<VolumeRow>($"select {VolumeRow.Columns} from asset_volume where asset_id = @assetId and exists (select 1 from asset where id = @assetId and owner_id = @ownerId) order by id", new { assetId, ownerId });
        return rows.Select(r => new TerrainVolume(r.Id, r.ToVolume())).ToList();
    }

    public async Task<IReadOnlyList<TerrainVolume>> PutVolumes(Guid assetId, IReadOnlyList<Volume> volumes)
    {
        var rows = volumes.Select(v => VolumeRow.From(Guid.CreateVersion7(), v)).ToList();
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        await c.ExecuteAsync("delete from asset_volume where asset_id = @assetId", new { assetId }, tx);
        foreach (var r in rows)
            await c.ExecuteAsync($"insert into asset_volume (asset_id, {VolumeRow.InsertColumns}) values (@AssetId, {VolumeRow.InsertValues})", new { AssetId = assetId, r.Id, r.Kind, r.Footprint, r.DimA, r.DimB, r.OffsetX, r.OffsetY, r.Yaw, r.Bottom, r.Top, r.Impassable, r.BlocksLos, r.SeeInOut, r.Cover, r.Difficult, r.Dangerous, r.Notes }, tx);
        await tx.CommitAsync();
        return rows.Select(r => new TerrainVolume(r.Id, r.ToVolume())).ToList();
    }
}
```

- [ ] **Step 4: Write `AssetEndpoints.cs`**

```csharp
using System.Security.Claims;
using Tabletop.Geometry;
using Tabletop.Management.Http;

namespace Tabletop.Management.Content.Assets;

public static class AssetEndpoints
{
    private static readonly TimeSpan UploadTtl = TimeSpan.FromMinutes(15), DownloadTtl = TimeSpan.FromMinutes(60);

    public static void MapAssets(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/assets").RequireAuthorization();

        g.MapGet("", async (ClaimsPrincipal user, AssetRepository assets, int? limit, Guid? after, AssetKind? kind) =>
        {
            var page = Keyset.Parse(limit, after);
            return Results.Ok(Page<AssetDto>.Of(await assets.List(CurrentAccount.Id(user), page, kind), page.Limit, a => a.Id));
        });

        g.MapPost("", async (ClaimsPrincipal user, CreateAssetRequest r, AssetRepository assets, IBlobStore blobs) =>
        {
            if (string.IsNullOrWhiteSpace(r.Name) || r.ByteSize <= 0 || !AssetRules.IsSha256(r.Hash)) return Problems.Unprocessable("name, byteSize and a 64-hex sha256 hash are required.");
            if (!AssetRules.ContentTypes[r.Kind].Contains(r.ContentType)) return Results.Problem(title: "Unsupported media type", detail: $"{r.Kind} accepts {string.Join(", ", AssetRules.ContentTypes[r.Kind])}. STL is not accepted; convert it first.", statusCode: StatusCodes.Status415UnsupportedMediaType);
            if ((r.Kind == AssetKind.Miniature) != (r.ModelledBase is not null)) return Problems.Unprocessable("A miniature carries a modelledBase; terrain and textures do not.");
            var owner = CurrentAccount.Id(user);
            var asset = await assets.Create(owner, r);
            var url = asset.Uploaded ? null : await blobs.PresignUpload(IBlobStore.Key(owner, r.Hash), r.ContentType, r.ByteSize, UploadTtl);
            return Results.Created($"/v1/assets/{asset.Id}", new CreateAssetResponse(asset, url));
        });

        g.MapGet("/{id:guid}", async (ClaimsPrincipal user, Guid id, AssetRepository assets) =>
            await assets.Get(CurrentAccount.Id(user), id) is { } a ? Results.Ok(a) : Problems.NotFound());

        g.MapPatch("/{id:guid}", async (ClaimsPrincipal user, Guid id, PatchAssetRequest p, AssetRepository assets) =>
            await assets.Patch(CurrentAccount.Id(user), id, p) is { } a ? Results.Ok(a) : Problems.NotFound());

        g.MapDelete("/{id:guid}", async (ClaimsPrincipal user, Guid id, AssetRepository assets, IBlobStore blobs) =>
        {
            var (found, orphaned) = await assets.Delete(CurrentAccount.Id(user), id);
            if (!found) return Problems.NotFound();
            if (orphaned is not null) await blobs.Delete([orphaned]);
            return Results.NoContent();
        });

        g.MapPost("/{id:guid}/confirm", async (ClaimsPrincipal user, Guid id, AssetRepository assets, IBlobStore blobs) =>
        {
            var owner = CurrentAccount.Id(user);
            var asset = await assets.Get(owner, id);
            if (asset is null) return Problems.NotFound();
            var key = IBlobStore.Key(owner, asset.Hash);
            var size = await blobs.Size(key);
            if (size is null) return Problems.Conflict("The bytes have not been uploaded yet.");
            if (size != asset.ByteSize) return Problems.Unprocessable($"Uploaded {size} bytes, expected {asset.ByteSize}.");
            long? triangles = null; string? warning = null;
            if (asset.ContentType == "model/gltf-binary")
            {
                var head = await blobs.ReadRange(key, 0, GlbHeader.HeaderBytes - 1);
                if (!GlbHeader.IsGlb(head)) { await blobs.Delete([key]); return Problems.Unprocessable("Not a GLB. STL and .gltf-with-external-buffers are not accepted."); }
                var len = GlbHeader.JsonChunkLength(head);
                triangles = GlbHeader.CountTriangles(await blobs.ReadRange(key, GlbHeader.HeaderBytes, GlbHeader.HeaderBytes + len - 1));
                if (triangles > GlbHeader.WarnAbove) warning = $"{triangles:N0} triangles is above the 100,000 this table renders comfortably; every participant downloads it.";
            }
            await assets.MarkUploaded(owner, asset.Hash);
            return Results.Ok(new ConfirmResponse((await assets.Get(owner, id))!, triangles, warning));
        });

        g.MapGet("/{id:guid}/download", async (ClaimsPrincipal user, Guid id, AssetRepository assets, IBlobStore blobs) =>
        {
            var owner = CurrentAccount.Id(user);
            var asset = await assets.Get(owner, id);
            if (asset is null) return Problems.NotFound();
            if (!asset.Uploaded) return Problems.Conflict("Not confirmed yet.");
            return Results.Ok(new DownloadResponse(await blobs.PresignDownload(IBlobStore.Key(owner, asset.Hash), DownloadTtl), DateTimeOffset.UtcNow.Add(DownloadTtl)));
        });

        g.MapGet("/{id:guid}/volumes", async (ClaimsPrincipal user, Guid id, AssetRepository assets) =>
            await assets.Get(CurrentAccount.Id(user), id) is null ? Problems.NotFound() : Results.Ok(await assets.GetVolumes(CurrentAccount.Id(user), id)));

        g.MapPut("/{id:guid}/volumes", async (ClaimsPrincipal user, Guid id, IReadOnlyList<Volume> volumes, AssetRepository assets) =>
        {
            var asset = await assets.Get(CurrentAccount.Id(user), id);
            if (asset is null) return Problems.NotFound();
            if (asset.Kind != AssetKind.Terrain) return Problems.Conflict("Only terrain carries volumes.");
            return Results.Ok(await assets.PutVolumes(id, volumes));
        });
    }
}
```

- [ ] **Step 5: Wire `Program.cs`** — `builder.Services.AddSingleton<Content.Assets.AssetRepository>();` and `app.MapAssets();`.

- [ ] **Step 6: Run `dotnet test tests/Tabletop.Management.Tests`; expect 39 passed. Commit: `Add the asset library: presigned upload and download, confirm with the header read, volumes`.**

### Task 8: Folders

**Files:**
- Create: `server/management/Content/Assets/FolderRepository.cs`, `server/management/Content/Assets/FolderEndpoints.cs`
- Modify: `server/management/Program.cs`
- Create: `tests/Tabletop.Management.Tests/FolderTests.cs`

**Interfaces:**
- Produces: `FolderDto(Guid Id, Guid? ParentId, string Name, IReadOnlyList<Guid> AssetIds)`; `FolderRequest(string Name, Guid? ParentId)`; `FolderRepository(Database)` with `List(ownerId) : IReadOnlyList<FolderDto>` (whole tree, unpaged), `Create`, `Patch`, `Delete`, `AddAsset(ownerId, folderId, assetId) : bool`, `RemoveAsset`; endpoints `GET /v1/folders`, `POST /v1/folders → 201`, `PATCH /v1/folders/{id}` (name and/or parent), `DELETE /v1/folders/{id} → 204` (children cascade in the schema; assets are untouched), `PUT /v1/folders/{id}/assets/{assetId} → 204`, `DELETE /v1/folders/{id}/assets/{assetId} → 204`.
- A parent must be the owner's own folder and not a descendant of the folder being moved (`422` otherwise). An asset can be in several folders; membership is `asset_folder`.

Spec: asset library → Folders ("Folders nest, and an asset can be in several of them … an asset in no folder is fine"); management API → The rest of the surface (folders, "small enough to return whole"); accounts → What an account owns ("Folders are the asset library's, and only the asset library's").

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using Tabletop.Management.Content.Assets;

namespace Tabletop.Management.Tests;

public class FolderTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public FolderTests(ManagementFactory f) => _f = f;

    [Fact]
    public async Task Nest_move_and_file_an_asset_in_two_folders()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var orks = await c.PostJson<FolderDto>("/v1/folders", new FolderRequest("Orks", null));
        var winter = await c.PostJson<FolderDto>("/v1/folders", new FolderRequest("Winter", null));
        var boyz = await c.PostJson<FolderDto>("/v1/folders", new FolderRequest("Boyz", orks.Id));
        var glb = TestGlb.Build((3, null, true));
        var asset = (await c.PostJson<CreateAssetResponse>("/v1/assets", new CreateAssetRequest("Boy", AssetKind.Miniature, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(glb)), glb.Length, "model/gltf-binary", 1, new(0, 0, 0), Tabletop.Geometry.Base.Circle(1.26)))).Asset;
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsync($"/v1/folders/{boyz.Id}/assets/{asset.Id}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsync($"/v1/folders/{winter.Id}/assets/{asset.Id}", null)).StatusCode);
        var tree = await c.GetJson<List<FolderDto>>("/v1/folders");
        Assert.Equal(2, tree.Count(f => f.AssetIds.Contains(asset.Id)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PatchJson($"/v1/folders/{orks.Id}", new FolderRequest("Orks", boyz.Id))).StatusCode);   // cycle
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/v1/folders/{orks.Id}")).StatusCode);
        tree = await c.GetJson<List<FolderDto>>("/v1/folders");
        Assert.Equal([winter.Id], tree.Select(f => f.Id));                       // Boyz cascaded with Orks
        Assert.True((await c.GetAsync($"/v1/assets/{asset.Id}")).IsSuccessStatusCode);  // the asset is untouched
    }

    [Fact]
    public async Task Folders_are_owner_only()
    {
        var mine = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var theirs = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var f = await mine.PostJson<FolderDto>("/v1/folders", new FolderRequest("Mine", null));
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.DeleteAsync($"/v1/folders/{f.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await theirs.PostAsJsonAsync("/v1/folders", new FolderRequest("Under yours", f.Id), Tabletop.Content.ContentJson.Options)).StatusCode);
    }
}
```

`HttpJson.cs` (test helper introduced in Task 7): `PostJson<T>(url, body)`, `PatchJson(url, body)`, `GetJson<T>(url)`, `PutJson<T>(url, body)` — thin wrappers over `System.Net.Http.Json` passing `ContentJson.Options` and asserting success where a body is read.

- [ ] **Step 2: Run; expect compile errors. Write `FolderRepository.cs`:**

```csharp
using Dapper;
using Tabletop.Management.Persistence;

namespace Tabletop.Management.Content.Assets;

public sealed record FolderDto(Guid Id, Guid? ParentId, string Name, IReadOnlyList<Guid> AssetIds);
public sealed record FolderRequest(string Name, Guid? ParentId);

public sealed class FolderRepository(Database db)
{
    private sealed record Row(Guid Id, Guid? ParentId, string Name);

    public async Task<IReadOnlyList<FolderDto>> List(Guid ownerId)
    {
        await using var c = await db.Open();
        var folders = (await c.QueryAsync<Row>("select id as Id, parent_id as ParentId, name as Name from folder where owner_id = @ownerId order by id", new { ownerId })).ToList();
        var members = (await c.QueryAsync<(Guid FolderId, Guid AssetId)>("select af.folder_id, af.asset_id from asset_folder af join folder f on f.id = af.folder_id where f.owner_id = @ownerId", new { ownerId }))
            .ToLookup(m => m.FolderId, m => m.AssetId);
        return folders.Select(f => new FolderDto(f.Id, f.ParentId, f.Name, members[f.Id].ToList())).ToList();
    }

    /// <summary>Null when the parent is not the owner's, or would make a cycle.</summary>
    public async Task<FolderDto?> Create(Guid ownerId, FolderRequest r)
    {
        await using var c = await db.Open();
        if (r.ParentId is { } p && !await Owns(c, ownerId, p)) return null;
        var id = Guid.CreateVersion7();
        await c.ExecuteAsync("insert into folder (id, owner_id, parent_id, name) values (@id, @ownerId, @parent, @name)", new { id, ownerId, parent = r.ParentId, name = r.Name });
        return new FolderDto(id, r.ParentId, r.Name, []);
    }

    public async Task<(bool Found, bool Valid)> Patch(Guid ownerId, Guid id, FolderRequest r)
    {
        await using var c = await db.Open();
        if (!await Owns(c, ownerId, id)) return (false, false);
        if (r.ParentId is { } p && (p == id || !await Owns(c, ownerId, p) || await IsDescendant(c, ancestor: id, candidate: p))) return (true, false);
        await c.ExecuteAsync("update folder set name = @name, parent_id = @parent where id = @id", new { id, name = r.Name, parent = r.ParentId });
        return (true, true);
    }

    public async Task<bool> Delete(Guid ownerId, Guid id)
    {
        await using var c = await db.Open();
        return await c.ExecuteAsync("delete from folder where id = @id and owner_id = @ownerId", new { id, ownerId }) == 1;
    }

    public async Task<bool> AddAsset(Guid ownerId, Guid folderId, Guid assetId)
    {
        await using var c = await db.Open();
        return await c.ExecuteAsync(@"insert into asset_folder (asset_id, folder_id) select a.id, f.id from asset a, folder f
            where a.id = @assetId and a.owner_id = @ownerId and f.id = @folderId and f.owner_id = @ownerId on conflict do nothing", new { ownerId, folderId, assetId }) >= 0
            && await Owns(c, ownerId, folderId) && await c.ExecuteScalarAsync<bool>("select exists(select 1 from asset where id = @assetId and owner_id = @ownerId)", new { assetId, ownerId });
    }

    public async Task<bool> RemoveAsset(Guid ownerId, Guid folderId, Guid assetId)
    {
        await using var c = await db.Open();
        return await c.ExecuteAsync("delete from asset_folder af using folder f where f.id = af.folder_id and f.owner_id = @ownerId and af.folder_id = @folderId and af.asset_id = @assetId", new { ownerId, folderId, assetId }) == 1;
    }

    private static Task<bool> Owns(Npgsql.NpgsqlConnection c, Guid ownerId, Guid folderId) =>
        c.ExecuteScalarAsync<bool>("select exists(select 1 from folder where id = @folderId and owner_id = @ownerId)", new { folderId, ownerId });

    private static Task<bool> IsDescendant(Npgsql.NpgsqlConnection c, Guid ancestor, Guid candidate) =>
        c.ExecuteScalarAsync<bool>(@"with recursive tree as (select id from folder where parent_id = @ancestor union all select f.id from folder f join tree t on f.parent_id = t.id)
            select exists(select 1 from tree where id = @candidate)", new { ancestor, candidate });
}
```

- [ ] **Step 3: Write `FolderEndpoints.cs`**

```csharp
using System.Security.Claims;
using Tabletop.Management.Http;

namespace Tabletop.Management.Content.Assets;

public static class FolderEndpoints
{
    public static void MapFolders(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/folders").RequireAuthorization();
        g.MapGet("", async (ClaimsPrincipal u, FolderRepository folders) => Results.Ok(await folders.List(CurrentAccount.Id(u))));
        g.MapPost("", async (ClaimsPrincipal u, FolderRequest r, FolderRepository folders) =>
            string.IsNullOrWhiteSpace(r.Name) ? Problems.Unprocessable("name is required")
            : await folders.Create(CurrentAccount.Id(u), r) is { } f ? Results.Created($"/v1/folders/{f.Id}", f) : Problems.Unprocessable("parentId is not one of your folders"));
        g.MapPatch("/{id:guid}", async (ClaimsPrincipal u, Guid id, FolderRequest r, FolderRepository folders) =>
        {
            var (found, valid) = await folders.Patch(CurrentAccount.Id(u), id, r);
            return !found ? Problems.NotFound() : !valid ? Problems.Unprocessable("parentId must be your folder and not this folder or one below it") : Results.NoContent();
        });
        g.MapDelete("/{id:guid}", async (ClaimsPrincipal u, Guid id, FolderRepository folders) => await folders.Delete(CurrentAccount.Id(u), id) ? Results.NoContent() : Problems.NotFound());
        g.MapPut("/{id:guid}/assets/{assetId:guid}", async (ClaimsPrincipal u, Guid id, Guid assetId, FolderRepository folders) =>
            await folders.AddAsset(CurrentAccount.Id(u), id, assetId) ? Results.NoContent() : Problems.NotFound());
        g.MapDelete("/{id:guid}/assets/{assetId:guid}", async (ClaimsPrincipal u, Guid id, Guid assetId, FolderRepository folders) =>
            await folders.RemoveAsset(CurrentAccount.Id(u), id, assetId) ? Results.NoContent() : Problems.NotFound());
    }
}
```

- [ ] **Step 4: Wire `Program.cs`** (`AddSingleton<FolderRepository>()`, `app.MapFolders()`), run `dotnet test`; expect 41 passed. Commit: `Add nested folders over the asset library`.

### Task 9: Army Forge — the export's shape and the import mapping

**Files:**
- Create: `server/management/Content/Armies/ArmyForgeList.cs`, `server/management/Content/Armies/ArmyForgeClient.cs`, `server/management/Content/Armies/ArmyForgeImport.cs`
- Create: `tests/Tabletop.Management.Tests/Fixtures/army-forge-c2-6YsWRH3py.json`, `tests/Tabletop.Management.Tests/ArmyForgeImportTests.cs`

**Interfaces:**
- Consumes: `Tabletop.Content.Army`, `Unit`, `ModelSlot`, `Weapon`, `PropertyInstance`; `Tabletop.Geometry.Base`.
- Produces: `AfList(string Id, string Name, string GameSystem, List<AfUnit> Units)`, `AfUnit(string SelectionId, string Name, int Size, JsonElement Quality, JsonElement Defense, List<AfRule>? Rules, List<AfLoadoutItem>? Loadout, AfBases? Bases, bool Combined, string? JoinToUnit)`, `AfRule(string Name, JsonElement? Rating)`, `AfLoadoutItem(string Name, string Type, int Count, JsonElement? Range, int? Attacks, List<AfRule>? SpecialRules, List<AfRule>? Content)`, `AfBases(string? Round, string? Square)`; `ArmyForgeClient(HttpClient)` with `HttpClientName = "army-forge"`, `static string? ListId(string source)` (a share URL's `id=` or a bare id), `Task<AfList> Fetch(string listId)` throwing `ArmyForgeException(int Status)`; `PropertyLevel { Unit, Model }`; `ArmyForgeImport.ToArmy(AfList list, string source, IReadOnlyDictionary<string, PropertyLevel> catalog) : Army` throwing `UnsupportedGameSystemException(string gameSystem)`; `ArmyForgeImport.GameRules = { ["gf"] = "grimdark-future", ["aof"] = "grimdark-future" }`; `ArmyForgeImport.DefaultBody = ModelBody(Base.Rect(221 / 25.4, 119 / 25.4), 113 / 25.4)`, `HeightScale = 1.5`, `static double HeightFromBase(Base) => HeightScale * b.A`.
- Read from a real export, re-verified live on 2026-09-06 (`GET https://army-forge.onepagerules.com/api/tts?id=c2-6YsWRH3py` → `200`, 26 KB, `access-control-allow-origin: *`; saved as the fixture). What the export actually holds, and what import therefore reads:
  - Top level: `id`, `name`, `gameSystem` (`"gf"` on this list), `units`, plus `points`, `pointsLimit`, `listPoints`, `specialRules`, `modified` and eight more this project ignores. **`gameSystem` is the list's own field and the only signal import uses** — a list may carry content from another army book and still report `"gf"`, and nothing here resolves the book.
  - A unit carries 27 keys. Read: `selectionId` (the unique one — `id` repeats across duplicated units), `name`, `size`, `quality`, `defense` (**plain integers**, `4` not `"4+"` and never objects), `rules`, `loadout`, `bases`, `combined`, `joinToUnit`. Ignored: `id`, `cost`, `items`, `weapons`, `upgrades`, `selectedUpgrades`, `xp`, `traits`, `notes`, `armyId`, `genericName`, `valid` and the rest. **There is no `customName`** — `name` is the only name field import uses.
  - **`loadout`, not `weapons`.** `weapons` is the base profile before upgrades; `loadout` is the resolved post-upgrade list (unit 0's `weapons[0]` is "Blood Sword" where its `loadout[0]` is the "Great Weapon" that replaced it).
  - A rule is `{id, name, label}` plus `rating` **only when it takes one** — absent, not null, on most rules. `label` is the pre-rendered display string (`"AP(3)"`) and is not stored.
  - A loadout entry has only `name`, `type` and `count` universally. `ArmyBookWeapon` adds `range`, `attacks`, `specialRules`, `weaponId`, `attacksMultiplier`; `ArmyBookItem` adds `content[]` of `ArmyBookRule` entries (`{name, rating, count}`). **`range` is `0` on some melee weapons and `null` on others in the same payload**, so the melee test is `(range ?? 0) == 0`; ranged is an integer of inches. `attacksMultiplier` is `1` on every entry observed and is **not** read — the `Attacks` stat is `attacks` alone; if a list ever carries a multiplier other than 1 the stat is wrong and the fix is one term in `Unit`.
  - `bases` is `{round, square}`, both **strings** of millimetres, both always present: a bare number is a circle (`"32"`), `WxH` an oval (`"60x35"`, `"120x92"`). `bases` also appears on loadout entries — a different field, not read.
  - Two entries with `combined: true` where one's `joinToUnit` names the other's `selectionId` are one unit of double size. `joinToUnit` is also how a hero joins, so it does not imply `combined`: an entry that carries `joinToUnit` and is not a `combined` partner is a hero joining, and its models become models of the unit it names (ruled 2026-09-06). This list has no joined hero — the four `combined` entries are the two Blood Warriors pairs, and the other three carry `joinToUnit: null` — so the join path is tested against hand-written lists.
- Mapping decisions (the spec leaves import mechanics open; questions 3–5 and the 09-06 ruling on question 2 record them): `gf` and `aof` → `game_system = "grimdark-future"`, any other system → `422` (ruled 2026-09-06: one rules implementation serves both, so both list kinds carry the same `GameRules` identifier); combined pairs merge, and then a joining entry becomes a **section** of the entry it names, its slots appended to that unit; `Quality`/`Defense` become unit stats `"4+"` from the host entry, and a joined entry's own `Quality`/`Defense` become slot stats on the slots it contributes; every unit rule and every item rule with `count >= size` attaches at unit level unless the catalog says the name is a `model` property, in which case it goes onto every slot; on a joined entry every such rule goes onto that entry's own slots whatever level the catalog gives it, because the unit's properties stay the host's; a rule with `count < size` goes onto `count` slots of its own section; weapons with `count >= size` go on every slot of their section, otherwise onto the next `count` slots from a cursor that advances within that section (so `9× Rifle, 1× Flamer` puts the flamer on slot 10, not slot 1, and a joined hero's weapon can never land on a host slot); weapon stats are `Attacks` and, when `range > 0`, `Range = "{range}\""`; a melee weapon has no `Range` (content values → A weapon lives on the model slot); base from each section's own `bases.round` — `"32"` → `Base.Circle(32/25.4)`, `"60x35"` → `Base.Oval(60/25.4, 35/25.4)`; absent → `DefaultBody`; height `HeightFromBase(base)` = `1.5 ×` the base's longest dimension (ruled 2026-09-06), so a 25.4 mm circle is 1.5" as before, a 32 mm circle 1.89", a 60×35 mm oval 3.54" and a 120×92 mm oval 7.09" — no ruleset supplies a height and Army Forge publishes none, so this is a starting guess scaled off the one physical dimension the export does give, and the player corrects it per slot with `SlotPatch.Height`; slot name `"{entry name} {index within that entry}"`, and `Ordinal` counts over the merged unit, so the two coincide unless something joined; `AssetId` null.
- Join resolution runs as one pass over what the combined pass left. An absorbed `combined` partner's `selectionId` resolves to the entry that absorbed it, so a hero naming either half of a combined pair joins the merged unit of ten. **A `joinToUnit` no surviving entry answers, and one whose entry itself carries `joinToUnit`, both leave the joining entry as its own unit** — the pre-ruling behaviour, and no `422`, because an unresolvable reference still describes a model the user paid for. Joins are therefore never chained and a cycle needs no walk: each side of a cycle names an entry that itself joins.
- Stat names `Quality`, `Defense`, `Range`, `Attacks` must match the OPR `stat_definition` names the game-server plan seeds. A joined entry's slots carry `Quality` and `Defense` as **slot** stat keys, a level the OPR properties document does not declare them at, and every rule a joined hero carries is a slot property whatever its catalog kind — both recorded under *Interfaces needed from other modules*.

Spec: management API → Army import ("Management fetches the list … The parser lives in one place"); the army → Where the values come from, Binding, Base shape and height ("defaulting to whatever the ruleset says" — no ruleset default exists yet, hence `DefaultBody` for an entry with none and a height scaled off a published base); content values → Stats are typed, A weapon lives on the model slot, `upgrades` is dropped, `list_entry` is dropped, The army's own two fields; data model → "Nothing carries a count … Import does that expansion once, at the boundary."

- [ ] **Step 1: Save the fixture and write the failing tests**

```bash
curl -s 'https://army-forge.onepagerules.com/api/tts?id=c2-6YsWRH3py' -o tests/Tabletop.Management.Tests/Fixtures/army-forge-c2-6YsWRH3py.json
```

```csharp
using System.Text.Json;
using Tabletop.Geometry;
using Tabletop.Management.Content.Armies;

namespace Tabletop.Management.Tests;

public class ArmyForgeImportTests
{
    static AfList Fixture() => ArmyForgeClient.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "army-forge-c2-6YsWRH3py.json")));
    static readonly Dictionary<string, PropertyLevel> Catalog = new() { ["Tough"] = PropertyLevel.Model, ["Fast"] = PropertyLevel.Unit, ["Impact"] = PropertyLevel.Unit };

    [Fact]
    public void Parses_the_real_export()
    {
        var list = Fixture();
        Assert.Equal(("Wormhole Daemons of War", "gf", 7), (list.Name, list.GameSystem, list.Units.Count));
        Assert.Equal("60x35", list.Units.Single(u => u.Name == "Beast Riders").Bases!.Round);
    }

    [Fact]
    public void Combined_pairs_merge_and_everything_else_is_its_own_unit()
    {
        var army = ArmyForgeImport.ToArmy(Fixture(), "https://army-forge.onepagerules.com/share?id=c2-6YsWRH3py", Catalog);
        Assert.Equal(("grimdark-future", "Wormhole Daemons of War", 5), (army.GameSystem, army.Name, army.Units.Count));
        var warriors = army.Units.Where(u => u.Name == "Blood Warriors").ToList();
        Assert.Equal(2, warriors.Count);
        Assert.All(warriors, u => { Assert.Equal(10, u.Models.Count); Assert.All(u.Models, m => Assert.Equal(["Great Weapon"], m.Weapons.Select(w => w.Name))); });
        Assert.Equal(Enumerable.Range(1, 5), army.Units.Select(u => u.Ordinal));
    }

    [Fact]
    public void Stats_properties_and_bases_land_where_the_catalog_says()
    {
        var army = ArmyForgeImport.ToArmy(Fixture(), "x", Catalog);
        var riders = army.Units.Single(u => u.Name == "Beast Riders");
        Assert.Equal(new Dictionary<string, string> { ["Quality"] = "4+", ["Defense"] = "3+" }, riders.Stats);
        Assert.Equal(["Fast", "Impact", "Warbound"], riders.Properties.Select(p => p.Name));
        Assert.Equal(2, riders.Properties.Single(p => p.Name == "Impact").Parameter);
        Assert.All(riders.Models, m =>
        {
            Assert.Equal([new Tabletop.Content.PropertyInstance("Tough", 3)], m.Properties);
            Assert.Equal(Base.Oval(60 / 25.4, 35 / 25.4), m.Base);
            Assert.Equal(1.5 * 60 / 25.4, m.Height);                                            // scaled off the base's long axis, not a constant
            Assert.Null(m.AssetId);
        });
        var claws = riders.Models[0].Weapons.Single(w => w.Name == "Heavy Claws");
        Assert.Equal(new Dictionary<string, string> { ["Attacks"] = "1" }, claws.Stats);        // melee: no Range
        Assert.Equal([new Tabletop.Content.PropertyInstance("AP", 1)], claws.Properties);
        var hounds = army.Units.First(u => u.Name == "Blood Hounds");
        Assert.Contains(hounds.Properties, p => p.Name == "Fast");                            // item content, count == size → unit level
        Assert.Contains(hounds.Properties, p => p is { Name: "Ravage", Parameter: 1 });
    }

    [Fact]
    public void Partial_weapons_fill_from_a_cursor_and_ranged_weapons_carry_range()
    {
        var json = """{"id":"t","name":"T","gameSystem":"gf","units":[{"selectionId":"a","name":"Squad","size":5,"quality":"4+","defense":"5+","rules":[],"combined":false,"joinToUnit":null,
          "bases":{"round":"25","square":"20"},"loadout":[
          {"name":"Rifle","type":"ArmyBookWeapon","count":4,"range":24,"attacks":1,"specialRules":[]},
          {"name":"Flamer","type":"ArmyBookWeapon","count":1,"range":12,"attacks":1,"specialRules":[{"name":"Blast","rating":3}]},
          {"name":"CCW","type":"ArmyBookWeapon","count":5,"range":0,"attacks":1,"specialRules":[]},
          {"name":"Shield","type":"ArmyBookItem","count":2,"content":[{"name":"Shielded","type":"ArmyBookRule"}]}]}]}""";
        var army = ArmyForgeImport.ToArmy(ArmyForgeClient.Parse(json), "x", new Dictionary<string, PropertyLevel>());
        var m = army.Units[0].Models;
        Assert.Equal(["Rifle", "CCW"], m[0].Weapons.Select(w => w.Name));
        Assert.Equal(["Flamer", "CCW"], m[4].Weapons.Select(w => w.Name));
        Assert.Equal("24\"", m[0].Weapons[0].Stats["Range"]);
        Assert.Equal([new Tabletop.Content.PropertyInstance("Blast", 3)], m[4].Weapons[0].Properties);
        Assert.Equal(2, m.Count(x => x.Properties.Any(p => p.Name == "Shielded")));
        Assert.Equal("4+", army.Units[0].Stats["Quality"]);
        Assert.Equal(Base.Circle(25 / 25.4), m[0].Base);
    }

    [Fact]
    public void A_joined_hero_becomes_a_model_of_the_unit_it_joined()
    {
        var json = """{"id":"t","name":"T","gameSystem":"gf","units":[
          {"selectionId":"sq","name":"Squad","size":5,"quality":4,"defense":5,"rules":[{"name":"Fearless"}],"combined":false,"joinToUnit":null,
           "bases":{"round":"25","square":"20"},"loadout":[{"name":"Rifle","type":"ArmyBookWeapon","count":5,"range":24,"attacks":1,"specialRules":[]}]},
          {"selectionId":"champ","name":"Champion","size":1,"quality":3,"defense":4,"combined":false,"joinToUnit":"sq",
           "rules":[{"name":"Hero"},{"name":"Tough","rating":3},{"name":"Fast"}],"bases":{"round":"40","square":"40"},
           "loadout":[{"name":"Great Weapon","type":"ArmyBookWeapon","count":1,"range":0,"attacks":3,"specialRules":[{"name":"AP","rating":2}]}]}]}""";
        var unit = Assert.Single(ArmyForgeImport.ToArmy(ArmyForgeClient.Parse(json), "x", Catalog).Units);
        Assert.Equal(("Squad", 6), (unit.Name, unit.Models.Count));
        Assert.Equal(new Dictionary<string, string> { ["Quality"] = "4+", ["Defense"] = "5+" }, unit.Stats);         // the squad's, not the hero's
        Assert.Equal(["Fearless"], unit.Properties.Select(p => p.Name));
        Assert.Equal(Enumerable.Range(1, 6), unit.Models.Select(m => m.Ordinal));
        Assert.Equal(["Squad 1", "Squad 2", "Squad 3", "Squad 4", "Squad 5", "Champion 1"], unit.Models.Select(m => m.Name));
        var hero = unit.Models[5];
        Assert.Equal(new Dictionary<string, string> { ["Quality"] = "3+", ["Defense"] = "4+" }, hero.Stats);        // its own, on its slot
        Assert.Empty(unit.Models[0].Stats);
        Assert.Equal(["Hero", "Tough", "Fast"], hero.Properties.Select(p => p.Name));                               // Fast is `unit` in Catalog and still lands here
        Assert.Equal(3, hero.Properties.Single(p => p.Name == "Tough").Parameter);
        Assert.Empty(unit.Models[0].Properties);
        Assert.Equal((Base.Circle(40 / 25.4), 1.5 * 40 / 25.4), (hero.Base, hero.Height));                           // base and height from the hero's own entry
        Assert.Equal((Base.Circle(25 / 25.4), 1.5 * 25 / 25.4), (unit.Models[0].Base, unit.Models[0].Height));
        Assert.Equal(["Great Weapon"], hero.Weapons.Select(w => w.Name));                                           // a cursor per section
        Assert.Equal(["Rifle"], unit.Models[0].Weapons.Select(w => w.Name));
        Assert.False(hero.Weapons[0].Stats.ContainsKey("Range"));
    }

    [Fact]
    public void A_join_resolves_through_a_combined_pair_and_an_unresolvable_join_stays_its_own_unit()
    {
        var json = """{"id":"t","name":"T","gameSystem":"gf","units":[
          {"selectionId":"a","name":"A","size":2,"quality":4,"defense":4,"rules":[],"combined":true,"joinToUnit":null,"bases":{"round":"25","square":"20"},"loadout":[]},
          {"selectionId":"b","name":"A","size":2,"quality":4,"defense":4,"rules":[],"combined":true,"joinToUnit":"a","bases":{"round":"25","square":"20"},"loadout":[]},
          {"selectionId":"h1","name":"H1","size":1,"quality":3,"defense":3,"rules":[],"combined":false,"joinToUnit":"b","bases":{"round":"40","square":"40"},"loadout":[]},
          {"selectionId":"h2","name":"H2","size":1,"quality":3,"defense":3,"rules":[],"combined":false,"joinToUnit":"gone","bases":{"round":"40","square":"40"},"loadout":[]},
          {"selectionId":"h3","name":"H3","size":1,"quality":3,"defense":3,"rules":[],"combined":false,"joinToUnit":"h2","bases":{"round":"40","square":"40"},"loadout":[]}]}""";
        var army = ArmyForgeImport.ToArmy(ArmyForgeClient.Parse(json), "x", Catalog);
        Assert.Equal(["A", "H2", "H3"], army.Units.Select(u => u.Name));                        // nothing answers "gone", and h3 names an entry that itself joins
        Assert.Equal(Enumerable.Range(1, 3), army.Units.Select(u => u.Ordinal));
        Assert.Equal(["A 1", "A 2", "A 3", "A 4", "H1 1"], army.Units[0].Models.Select(m => m.Name));   // h1 named the absorbed partner
        Assert.Equal("4+", army.Units[0].Stats["Quality"]);
        Assert.Equal("3+", army.Units[0].Models[4].Stats["Quality"]);
        Assert.All(army.Units.Skip(1), u => Assert.Single(u.Models));
    }

    [Fact]
    public void Other_game_systems_are_refused_and_ids_are_read_from_share_links()
    {
        Assert.Equal("grimdark-future", ArmyForgeImport.ToArmy(Fixture() with { GameSystem = "aof" }, "x", Catalog).GameSystem);   // one rules implementation, two list kinds
        Assert.Throws<UnsupportedGameSystemException>(() => ArmyForgeImport.ToArmy(Fixture() with { GameSystem = "aofs" }, "x", Catalog));
        Assert.Equal("c2-6YsWRH3py", ArmyForgeClient.ListId("https://army-forge.onepagerules.com/share?id=c2-6YsWRH3py&name=Wormhole"));
        Assert.Equal("c2-6YsWRH3py", ArmyForgeClient.ListId("c2-6YsWRH3py"));
        Assert.Null(ArmyForgeClient.ListId("https://example.com/nothing"));
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `ArmyForgeList.cs` and `ArmyForgeClient.cs`:**

```csharp
using System.Text.Json;

namespace Tabletop.Management.Content.Armies;

// The subset of Army Forge's TTS export this project reads. Field names are the export's.
// Only `name`, `type` and `count` are present on every loadout entry, and `rating` is absent (not null) on a rule that takes no
// parameter, so every optional member is nullable and nothing is `required`. A required-field model throws on the real export.
public sealed record AfList(string Id, string Name, string GameSystem, List<AfUnit> Units);
public sealed record AfUnit(string SelectionId, string Name, int Size, JsonElement Quality, JsonElement Defense,
    List<AfRule>? Rules, List<AfLoadoutItem>? Loadout, AfBases? Bases, bool Combined, string? JoinToUnit);
public sealed record AfRule(string Name, JsonElement? Rating);
public sealed record AfLoadoutItem(string Name, string Type, int Count, JsonElement? Range, int? Attacks, List<AfRule>? SpecialRules, List<AfRule>? Content);
public sealed record AfBases(string? Round, string? Square);
```

```csharp
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tabletop.Management.Content.Armies;

public sealed class ArmyForgeException(int status) : Exception($"Army Forge answered {status}") { public int Status { get; } = status; }

public sealed partial class ArmyForgeClient(HttpClient http)
{
    public const string HttpClientName = "army-forge";
    public const string BaseUrl = "https://army-forge.onepagerules.com";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    [GeneratedRegex(@"[?&]id=([A-Za-z0-9_-]+)")] private static partial Regex IdParam();
    [GeneratedRegex(@"^[A-Za-z0-9_-]{4,64}$")] private static partial Regex BareId();

    /// <summary>The list id from a share link (the id= parameter) or a bare id; null when neither.</summary>
    public static string? ListId(string source) =>
        IdParam().Match(source) is { Success: true } m ? m.Groups[1].Value : BareId().IsMatch(source) ? source : null;

    public static AfList Parse(string json) => JsonSerializer.Deserialize<AfList>(json, Json) ?? throw new JsonException("empty export");

    public async Task<AfList> Fetch(string listId)
    {
        using var response = await http.GetAsync($"{BaseUrl}/api/tts?id={Uri.EscapeDataString(listId)}");
        if (!response.IsSuccessStatusCode) throw new ArmyForgeException((int)response.StatusCode);
        return Parse(await response.Content.ReadAsStringAsync());
    }
}
```

- [ ] **Step 3: Write `ArmyForgeImport.cs`**

```csharp
using System.Globalization;
using System.Text.Json;
using Tabletop.Content;
using Tabletop.Geometry;

namespace Tabletop.Management.Content.Armies;

public enum PropertyLevel { Unit, Model }

public sealed class UnsupportedGameSystemException(string gameSystem) : Exception($"Only Grimdark Future and Age of Fantasy lists import; this list is '{gameSystem}'.")
{
    public string GameSystem { get; } = gameSystem;
}

/// <summary>Army Forge's list → a self-contained Army. Expansion to one slot per model happens here and nowhere else.</summary>
public static class ArmyForgeImport
{
    // Grimdark Future and Age of Fantasy are the same mechanics on different content, so both map to the one rules implementation (ruled 2026-09-06).
    public static readonly IReadOnlyDictionary<string, string> GameRules = new Dictionary<string, string> { ["gf"] = "grimdark-future", ["aof"] = "grimdark-future" };
    /// <summary>What an entry with no published base gets: a vehicle hull, 221 x 119 x 113 mm. Army Forge publishes bases.round
    /// reliably for infantry, so a missing base means a vehicle or monster in practice, and the old 32 mm circle was wrong for
    /// exactly those models — a tank measured from a 1.26" circle at its hull centre. Ruled 2026-09-09. The player corrects it
    /// per slot, and the height comes with the base because HeightFromBase would scale 221 mm into a 13" tall tank.</summary>
    public static readonly ModelBody DefaultBody = new(Base.Rect(221 / 25.4, 119 / 25.4), 113 / 25.4);
    public const double HeightScale = 1.5;

    /// <summary>A starting height for a base that *was* published, scaled off its long axis because nothing publishes a height:
    /// 1.5" for a 25.4 mm circle, 3.54" for a 60x35 mm oval. The player corrects it per slot (ruled 2026-09-06).</summary>
    public static double HeightFromBase(Base b) => HeightScale * b.A;

    public static Army ToArmy(AfList list, string source, IReadOnlyDictionary<string, PropertyLevel> catalog)
    {
        if (!GameRules.TryGetValue(list.GameSystem, out var gameRules)) throw new UnsupportedGameSystemException(list.GameSystem);
        var units = Merge(list.Units).Select((m, i) => Unit(m, i + 1, catalog)).ToList();
        return new Army(Guid.CreateVersion7(), list.Name, gameRules, source, units);
    }

    /// <summary>A unit as it will be built: the host entry, then the entries whose models join it.</summary>
    private sealed record Merged(AfUnit Host, List<AfUnit> Joined);

    /// <summary>Two combined entries are one unit: sizes add, loadout counts add. Then a joining entry becomes a section of the entry
    /// it names, an absorbed partner's id resolving to the entry that absorbed it. A name nothing answers, and a name whose entry
    /// itself joins, leave the joining entry as its own unit — which is also what breaks a cycle without walking one.</summary>
    private static List<Merged> Merge(List<AfUnit> units)
    {
        var survivors = new List<AfUnit>();
        var absorbed = new Dictionary<string, string>();          // a combined partner's selectionId → the id that absorbed it
        foreach (var u in units)
        {
            if (absorbed.ContainsKey(u.SelectionId)) continue;
            var partner = u.Combined ? units.FirstOrDefault(o => o.Combined && o.SelectionId != u.SelectionId && (o.JoinToUnit == u.SelectionId || u.JoinToUnit == o.SelectionId)) : null;
            if (partner is null) { survivors.Add(u); continue; }
            absorbed[partner.SelectionId] = u.SelectionId;
            var loadout = (u.Loadout ?? []).Concat(partner.Loadout ?? [])
                .GroupBy(l => (l.Type, l.Name))
                .Select(g => g.First() with { Count = g.Sum(l => l.Count) }).ToList();
            survivors.Add(u with { Size = u.Size + partner.Size, Loadout = loadout, JoinToUnit = null });
        }

        var byId = survivors.ToDictionary(u => u.SelectionId);
        AfUnit? Host(string id) => byId.GetValueOrDefault(absorbed.GetValueOrDefault(id, id));
        bool Joins(AfUnit u) => u.JoinToUnit is not null && Host(u.JoinToUnit) is { JoinToUnit: null };

        var result = survivors.Where(u => !Joins(u)).Select(u => new Merged(u, [])).ToList();
        var hosts = result.ToDictionary(m => m.Host.SelectionId);
        foreach (var j in survivors.Where(Joins)) hosts[Host(j.JoinToUnit!)!.SelectionId].Joined.Add(j);
        return result;
    }

    private static Unit Unit(Merged m, int ordinal, IReadOnlyDictionary<string, PropertyLevel> catalog)
    {
        var host = m.Host;
        var stats = new Dictionary<string, string> { ["Quality"] = TargetNumber(host.Quality), ["Defense"] = TargetNumber(host.Defense) };
        var unitProps = new List<PropertyInstance>();
        var models = new List<ModelSlot>();
        Section(host, catalog, unitProps, models);
        foreach (var joined in m.Joined) Section(joined, catalog, null, models);
        return new Unit(Guid.CreateVersion7(), ordinal, host.Name, stats, unitProps, models);
    }

    /// <summary>One entry's slots, appended to the unit being built. `unitProps` is null for a joined entry: every rule of it goes on
    /// its own slots whatever level the catalog gives the name, and its Quality and Defense become slot stats, because the unit's own
    /// stats and properties stay the host's. The cursor is per section, so a joined hero's loadout never reaches a host slot.</summary>
    private static void Section(AfUnit u, IReadOnlyDictionary<string, PropertyLevel> catalog, List<PropertyInstance>? unitProps, List<ModelSlot> into)
    {
        var slotProps = Enumerable.Range(0, u.Size).Select(_ => new List<PropertyInstance>()).ToList();
        var slotWeapons = Enumerable.Range(0, u.Size).Select(_ => new List<Weapon>()).ToList();
        var cursor = 0;

        void Place(PropertyInstance p, int count)
        {
            if (count >= u.Size)
            {
                if (unitProps is null || catalog.GetValueOrDefault(p.Name) == PropertyLevel.Model) slotProps.ForEach(s => s.Add(p));
                else unitProps.Add(p);
                return;
            }
            for (var i = 0; i < count; i++) slotProps[(cursor + i) % u.Size].Add(p);
            cursor += count;
        }

        foreach (var r in u.Rules ?? []) Place(new PropertyInstance(r.Name, Rating(r.Rating)), u.Size);
        foreach (var item in u.Loadout ?? [])
        {
            if (item.Type == "ArmyBookWeapon")
            {
                var wstats = new Dictionary<string, string> { ["Attacks"] = (item.Attacks ?? 1).ToString(CultureInfo.InvariantCulture) };
                // Melee is `"range": 0` on some entries and `"range": null` on others in the same export; `is { } range and > 0` excludes both.
                if (Range(item.Range) is { } range and > 0) wstats["Range"] = $"{range.ToString(CultureInfo.InvariantCulture)}\"";
                var props = (item.SpecialRules ?? []).Select(r => new PropertyInstance(r.Name, Rating(r.Rating))).ToList();
                Weapon W() => new(Guid.CreateVersion7(), item.Name, new Dictionary<string, string>(wstats), props);
                if (item.Count >= u.Size) slotWeapons.ForEach(s => s.Add(W()));
                else { for (var i = 0; i < item.Count; i++) slotWeapons[(cursor + i) % u.Size].Add(W()); cursor += item.Count; }
            }
            else if (item.Type == "ArmyBookItem")
                foreach (var rule in item.Content ?? []) Place(new PropertyInstance(rule.Name, Rating(rule.Rating)), item.Count);
        }

        var body = ParseBase(u.Bases?.Round) is { } published ? new ModelBody(published, HeightFromBase(published)) : DefaultBody;
        var slotStats = unitProps is null
            ? new Dictionary<string, string> { ["Quality"] = TargetNumber(u.Quality), ["Defense"] = TargetNumber(u.Defense) }
            : new Dictionary<string, string>();
        for (var i = 0; i < u.Size; i++)
            into.Add(new ModelSlot(Guid.CreateVersion7(), into.Count + 1, $"{u.Name} {i + 1}", new Dictionary<string, string>(slotStats),
                slotProps[i], body.Base, body.Height, null, slotWeapons[i]));
    }

    /// <summary>"32" → circle 32 mm; "60x35" → oval 60×35 mm; anything else → null. Inches out.</summary>
    public static Base? ParseBase(string? round)
    {
        if (string.IsNullOrWhiteSpace(round)) return null;
        var parts = round.ToLowerInvariant().Split('x');
        if (parts.Length == 1 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return Base.Circle(d / 25.4);
        if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var l) && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var w)) return Base.Oval(l / 25.4, w / 25.4);
        return null;
    }

    private static string TargetNumber(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number => $"{e.GetInt32()}+",
        JsonValueKind.String => e.GetString()!.TrimEnd('+') + "+",
        _ => throw new JsonException("quality/defense must be a number or a string"),
    };

    private static int? Rating(JsonElement? e) => e switch
    {
        { ValueKind: JsonValueKind.Number } r when r.TryGetInt32(out var n) => n,
        { ValueKind: JsonValueKind.String } r when int.TryParse(r.GetString(), out var n) => n,
        _ => null,
    };

    private static double? Range(JsonElement? e) => e is { ValueKind: JsonValueKind.Number } r ? r.GetDouble() : null;
}
```

- [ ] **Step 4: Run `dotnet test tests/Tabletop.Management.Tests --filter ArmyForgeImportTests`; expect 7 passed. Commit: `Parse Army Forge exports and expand them into self-contained armies`.**

### Task 10: Armies — import, list, get, patch, delete

**Files:**
- Create: `server/management/Content/Armies/ArmyRepository.cs`, `server/management/Content/Armies/ArmyEndpoints.cs`, `server/management/Content/Armies/CatalogKinds.cs`
- Modify: `server/management/Program.cs`, `tests/Tabletop.Management.Tests/ManagementFactory.cs`
- Create: `tests/Tabletop.Management.Tests/FakeArmyForge.cs`, `tests/Tabletop.Management.Tests/ArmyTests.cs`

**Interfaces:**
- Consumes: `ArmyForgeClient`, `ArmyForgeImport`; `Tabletop.Content.Army` tree; `Keyset`/`Page<T>`.
- Produces: `ArmySummary(Guid Id, string Name, string GameSystem, string Source, int Units, int Models)`; `ImportRequest(string Source)`; `PatchArmyRequest(string? Name, IReadOnlyList<UnitPatch>? Units, IReadOnlyList<SlotPatch>? Slots)`, `UnitPatch(Guid UnitId, Guid? AssetId)` (binds every slot of the unit; the unit default), `SlotPatch(Guid SlotId, Guid? AssetId, bool ClearAsset, Base? Base, double? Height)`; `CatalogKinds(Database).For(string gameRules) : Task<IReadOnlyDictionary<string, PropertyLevel>>` (from `catalog_entry` joined to `properties` on `game_rules`; empty when no set is loaded); `ArmyRepository(Database)` with `Insert(ownerId, Army)`, `Get(ownerId, id) : Army?` (the whole tree), `List(ownerId, Keyset) : IReadOnlyList<ArmySummary>`, `Patch(ownerId, id, PatchArmyRequest) : Army?`, `Delete(ownerId, id) : bool`; endpoints `POST /v1/armies/import {source} → 201 Army`, `GET /v1/armies?limit&after → Page<ArmySummary>`, `GET /v1/armies/{id} → Army`, `PATCH /v1/armies/{id} → Army`, `DELETE /v1/armies/{id} → 204`.
- The soft asset reference is written as given without checking the asset exists (the army holds the hitbox; the asset is appearance) — the army → Assets are a soft reference. `PATCH` is a patch, not a document: the spec left it open (management API → Deliberately not decided); the client shell never edits armies, and the web app edits one slot or one unit default at a time.
- Import failures: Army Forge unreachable or non-200 → `502`; a source with no id → `422`; another game system → `422`.

Spec: management API → Army import, The rest of the surface (`PATCH /v1/armies/{id} bindings, bases`); the army → Binding ("the default fills every slot in a unit with the same mesh; overriding individual slots is expected"), Base shape and height ("set by the user, per slot"), A changed list means a re-import (no reconciliation; a re-import is a new army); `schema.md` → the ten army tables.

- [ ] **Step 1: Write `FakeArmyForge.cs` and the failing tests**

```csharp
using System.Net;

namespace Tabletop.Management.Tests;

/// <summary>Serves the fixture for id c2-6YsWRH3py and 404 for anything else.</summary>
public sealed class FakeArmyForge : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var id = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["id"];
        if (id != "c2-6YsWRH3py") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "army-forge-c2-6YsWRH3py.json"));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });
    }
}
```

`ManagementFactory.ConfigureTestServices` gains `s.AddHttpClient(Content.Armies.ArmyForgeClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new FakeArmyForge());`.

```csharp
using System.Net;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Content.Armies;
using Tabletop.Management.Http;

namespace Tabletop.Management.Tests;

public class ArmyTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public ArmyTests(ManagementFactory f) => _f = f;
    const string Share = "https://army-forge.onepagerules.com/share?id=c2-6YsWRH3py";

    [Fact]
    public async Task Import_stores_the_whole_tree_and_reads_it_back_identically()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var imported = await c.PostJson<Army>("/v1/armies/import", new ImportRequest(Share));
        Assert.Equal(5, imported.Units.Count); Assert.Equal(Share, imported.Source);
        var fetched = await c.GetJson<Army>($"/v1/armies/{imported.Id}");
        Assert.Equal(ContentJson.Serialize(imported), ContentJson.Serialize(fetched));
        var page = await c.GetJson<Page<ArmySummary>>("/v1/armies");
        Assert.Equal([(imported.Id, 5, 33)], page.Items.Select(a => (a.Id, a.Units, a.Models)));
    }

    [Fact]
    public async Task Patch_binds_a_unit_default_then_overrides_one_slot_and_its_base()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var army = await c.PostJson<Army>("/v1/armies/import", new ImportRequest("c2-6YsWRH3py"));
        var unit = army.Units.Single(u => u.Name == "Beast Riders");
        var mesh = Guid.CreateVersion7(); var special = Guid.CreateVersion7();
        var patched = await c.PatchJson<Army>($"/v1/armies/{army.Id}", new PatchArmyRequest("Daemons", [new UnitPatch(unit.Id, mesh)],
            [new SlotPatch(unit.Models[0].Id, special, false, Base.Circle(1.57), 2.0)]));
        var u = patched.Units.Single(x => x.Id == unit.Id);
        Assert.Equal("Daemons", patched.Name);
        Assert.Equal(special, u.Models[0].AssetId); Assert.Equal(Base.Circle(1.57), u.Models[0].Base); Assert.Equal(2.0, u.Models[0].Height);
        Assert.All(u.Models.Skip(1), m => Assert.Equal(mesh, m.AssetId));
        var cleared = await c.PatchJson<Army>($"/v1/armies/{army.Id}", new PatchArmyRequest(null, null, [new SlotPatch(unit.Models[0].Id, null, true, null, null)]));
        Assert.Null(cleared.Units.Single(x => x.Id == unit.Id).Models[0].AssetId);
    }

    [Fact]
    public async Task Owner_only_and_import_errors()
    {
        var mine = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var theirs = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var army = await mine.PostJson<Army>("/v1/armies/import", new ImportRequest(Share));
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.GetAsync($"/v1/armies/{army.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.DeleteAsync($"/v1/armies/{army.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadGateway, (await mine.PostAsJsonAsync("/v1/armies/import", new ImportRequest("https://army-forge.onepagerules.com/share?id=missing1"), ContentJson.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await mine.PostAsJsonAsync("/v1/armies/import", new ImportRequest("not a link"), ContentJson.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await mine.DeleteAsync($"/v1/armies/{army.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mine.GetAsync($"/v1/armies/{army.Id}")).StatusCode);
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `CatalogKinds.cs` and `ArmyRepository.cs`:**

```csharp
using Dapper;
using Tabletop.Management.Persistence;

namespace Tabletop.Management.Content.Armies;

/// <summary>Which catalog names are model-level, from whatever Properties sets exist for the game rules. Empty until content is loaded.</summary>
public sealed class CatalogKinds(Database db)
{
    public async Task<IReadOnlyDictionary<string, PropertyLevel>> For(string gameRules)
    {
        await using var c = await db.Open();
        var rows = await c.QueryAsync<(string Name, string Kind)>(@"select distinct ce.name, ce.kind from catalog_entry ce join properties p on p.content_hash = ce.properties_hash
            where p.game_rules = @gameRules and ce.kind in ('unit', 'model')", new { gameRules });
        return rows.ToDictionary(r => r.Name, r => r.Kind == "model" ? PropertyLevel.Model : PropertyLevel.Unit);
    }
}
```

```csharp
using Dapper;
using Npgsql;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Persistence;

namespace Tabletop.Management.Content.Armies;

public sealed record ArmySummary(Guid Id, string Name, string GameSystem, string Source, int Units, int Models);
public sealed record ImportRequest(string Source);
public sealed record UnitPatch(Guid UnitId, Guid? AssetId);
public sealed record SlotPatch(Guid SlotId, Guid? AssetId, bool ClearAsset, Base? Base, double? Height);
public sealed record PatchArmyRequest(string? Name, IReadOnlyList<UnitPatch>? Units, IReadOnlyList<SlotPatch>? Slots);

/// <summary>The army tree: army → unit → model_slot → weapon, each level with its stat and property rows.</summary>
public sealed class ArmyRepository(Database db)
{
    private sealed record UnitRow(Guid Id, int Ordinal, string Name);
    private sealed record SlotRow(Guid Id, Guid UnitId, int Ordinal, string Name, string BaseShape, double BaseA, double BaseB, double Height, Guid? AssetId);
    private sealed record WeaponRow(Guid Id, Guid SlotId, string Name);
    private sealed record KV(Guid Owner, string Key, string Value);
    private sealed record Prop(Guid Owner, string Name, int? Parameter);

    public async Task Insert(Guid ownerId, Army army)
    {
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        await c.ExecuteAsync("insert into army (id, owner_id, name, game_system, source) values (@Id, @ownerId, @Name, @GameSystem, @Source)", new { army.Id, ownerId, army.Name, army.GameSystem, army.Source }, tx);
        foreach (var u in army.Units)
        {
            await c.ExecuteAsync("insert into unit (id, army_id, ordinal, name) values (@Id, @armyId, @Ordinal, @Name)", new { u.Id, armyId = army.Id, u.Ordinal, u.Name }, tx);
            await c.ExecuteAsync("insert into unit_stat (unit_id, stat, value) values (@id, @k, @v)", u.Stats.Select(s => new { id = u.Id, k = s.Key, v = s.Value }), tx);
            await c.ExecuteAsync("insert into unit_property (unit_id, name, parameter) values (@id, @Name, @Parameter)", u.Properties.Select(p => new { id = u.Id, p.Name, p.Parameter }), tx);
            foreach (var m in u.Models)
            {
                await c.ExecuteAsync("insert into model_slot (id, unit_id, ordinal, name, base_shape, base_a, base_b, height, asset_id) values (@Id, @unitId, @Ordinal, @Name, @shape, @a, @b, @Height, @AssetId)",
                    new { m.Id, unitId = u.Id, m.Ordinal, m.Name, shape = m.Base.Shape.ToString().ToLowerInvariant(), a = m.Base.A, b = m.Base.B, m.Height, m.AssetId }, tx);
                await c.ExecuteAsync("insert into slot_stat (slot_id, stat, value) values (@id, @k, @v)", m.Stats.Select(s => new { id = m.Id, k = s.Key, v = s.Value }), tx);
                await c.ExecuteAsync("insert into slot_property (slot_id, name, parameter) values (@id, @Name, @Parameter)", m.Properties.Select(p => new { id = m.Id, p.Name, p.Parameter }), tx);
                foreach (var w in m.Weapons)
                {
                    await c.ExecuteAsync("insert into weapon (id, slot_id, name) values (@Id, @slotId, @Name)", new { w.Id, slotId = m.Id, w.Name }, tx);
                    await c.ExecuteAsync("insert into weapon_stat (weapon_id, stat, value) values (@id, @k, @v)", w.Stats.Select(s => new { id = w.Id, k = s.Key, v = s.Value }), tx);
                    await c.ExecuteAsync("insert into weapon_property (weapon_id, name, parameter) values (@id, @Name, @Parameter)", w.Properties.Select(p => new { id = w.Id, p.Name, p.Parameter }), tx);
                }
            }
        }
        await tx.CommitAsync();
    }

    public async Task<Army?> Get(Guid ownerId, Guid id)
    {
        await using var c = await db.Open();
        var head = await c.QuerySingleOrDefaultAsync<(string Name, string GameSystem, string Source)>("select name, game_system, source from army where id = @id and owner_id = @ownerId", new { id, ownerId });
        if (head == default) return null;
        var units = (await c.QueryAsync<UnitRow>("select id as Id, ordinal as Ordinal, name as Name from unit where army_id = @id order by ordinal", new { id })).ToList();
        var slots = (await c.QueryAsync<SlotRow>(@"select s.id as Id, s.unit_id as UnitId, s.ordinal as Ordinal, s.name as Name, s.base_shape as BaseShape, s.base_a::float8 as BaseA, s.base_b::float8 as BaseB, s.height::float8 as Height, s.asset_id as AssetId
            from model_slot s join unit u on u.id = s.unit_id where u.army_id = @id order by s.ordinal", new { id })).ToLookup(s => s.UnitId);
        var weapons = (await c.QueryAsync<WeaponRow>("select w.id as Id, w.slot_id as SlotId, w.name as Name from weapon w join model_slot s on s.id = w.slot_id join unit u on u.id = s.unit_id where u.army_id = @id order by w.id", new { id })).ToLookup(w => w.SlotId);
        var unitStats = (await c.QueryAsync<KV>("select us.unit_id as Owner, us.stat as Key, us.value as Value from unit_stat us join unit u on u.id = us.unit_id where u.army_id = @id", new { id })).ToLookup(x => x.Owner);
        var unitProps = (await c.QueryAsync<Prop>("select up.unit_id as Owner, up.name as Name, up.parameter as Parameter from unit_property up join unit u on u.id = up.unit_id where u.army_id = @id order by up.name", new { id })).ToLookup(x => x.Owner);
        var slotStats = (await c.QueryAsync<KV>("select ss.slot_id as Owner, ss.stat as Key, ss.value as Value from slot_stat ss join model_slot s on s.id = ss.slot_id join unit u on u.id = s.unit_id where u.army_id = @id", new { id })).ToLookup(x => x.Owner);
        var slotProps = (await c.QueryAsync<Prop>("select sp.slot_id as Owner, sp.name as Name, sp.parameter as Parameter from slot_property sp join model_slot s on s.id = sp.slot_id join unit u on u.id = s.unit_id where u.army_id = @id order by sp.name", new { id })).ToLookup(x => x.Owner);
        var weaponStats = (await c.QueryAsync<KV>("select ws.weapon_id as Owner, ws.stat as Key, ws.value as Value from weapon_stat ws join weapon w on w.id = ws.weapon_id join model_slot s on s.id = w.slot_id join unit u on u.id = s.unit_id where u.army_id = @id", new { id })).ToLookup(x => x.Owner);
        var weaponProps = (await c.QueryAsync<Prop>("select wp.weapon_id as Owner, wp.name as Name, wp.parameter as Parameter from weapon_property wp join weapon w on w.id = wp.weapon_id join model_slot s on s.id = w.slot_id join unit u on u.id = s.unit_id where u.army_id = @id order by wp.name", new { id })).ToLookup(x => x.Owner);

        IReadOnlyDictionary<string, string> Stats(ILookup<Guid, KV> l, Guid owner) => l[owner].ToDictionary(x => x.Key, x => x.Value);
        IReadOnlyList<PropertyInstance> Props(ILookup<Guid, Prop> l, Guid owner) => l[owner].Select(p => new PropertyInstance(p.Name, p.Parameter)).ToList();

        return new Army(id, head.Name, head.GameSystem, head.Source, units.Select(u => new Unit(u.Id, u.Ordinal, u.Name, Stats(unitStats, u.Id), Props(unitProps, u.Id),
            slots[u.Id].Select(s => new ModelSlot(s.Id, s.Ordinal, s.Name, Stats(slotStats, s.Id), Props(slotProps, s.Id),
                new Base(Enum.Parse<BaseShape>(s.BaseShape, true), s.BaseA, s.BaseB), s.Height, s.AssetId,
                weapons[s.Id].Select(w => new Weapon(w.Id, w.Name, Stats(weaponStats, w.Id), Props(weaponProps, w.Id))).ToList())).ToList())).ToList());
    }

    public async Task<IReadOnlyList<ArmySummary>> List(Guid ownerId, Http.Keyset page)
    {
        await using var c = await db.Open();
        return (await c.QueryAsync<ArmySummary>(@"select a.id as Id, a.name as Name, a.game_system as GameSystem, a.source as Source,
            (select count(*) from unit u where u.army_id = a.id)::int as Units,
            (select count(*) from model_slot s join unit u on u.id = s.unit_id where u.army_id = a.id)::int as Models
            from army a where a.owner_id = @ownerId and (@after::uuid is null or a.id > @after::uuid) order by a.id limit @limit", new { ownerId, after = page.After, limit = page.Limit })).ToList();
    }

    public async Task<Army?> Patch(Guid ownerId, Guid id, PatchArmyRequest p)
    {
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        if (await c.ExecuteAsync("update army set name = coalesce(@name, name) where id = @id and owner_id = @ownerId", new { id, ownerId, name = p.Name }, tx) == 0) return null;
        foreach (var u in p.Units ?? [])
            await c.ExecuteAsync("update model_slot set asset_id = @asset where unit_id = @unitId and unit_id in (select id from unit where army_id = @id)", new { id, unitId = u.UnitId, asset = u.AssetId }, tx);
        foreach (var s in p.Slots ?? [])
            await c.ExecuteAsync(@"update model_slot set asset_id = case when @clear then null else coalesce(@asset, asset_id) end,
                base_shape = coalesce(@shape, base_shape), base_a = coalesce(@a, base_a), base_b = coalesce(@b, base_b), height = coalesce(@height, height)
                where id = @slotId and unit_id in (select id from unit where army_id = @id)",
                new { id, slotId = s.SlotId, clear = s.ClearAsset, asset = s.AssetId, shape = s.Base?.Shape.ToString().ToLowerInvariant(), a = s.Base?.A, b = s.Base?.B, height = s.Height }, tx);
        await tx.CommitAsync();
        return await Get(ownerId, id);
    }

    public async Task<bool> Delete(Guid ownerId, Guid id)
    {
        await using var c = await db.Open();
        return await c.ExecuteAsync("delete from army where id = @id and owner_id = @ownerId", new { id, ownerId }) == 1;
    }
}
```

- [ ] **Step 3: Write `ArmyEndpoints.cs`**

```csharp
using System.Security.Claims;
using Tabletop.Management.Http;

namespace Tabletop.Management.Content.Armies;

public static class ArmyEndpoints
{
    public static void MapArmies(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/armies").RequireAuthorization();

        g.MapPost("/import", async (ClaimsPrincipal u, ImportRequest r, ArmyForgeClient forge, CatalogKinds kinds, ArmyRepository armies) =>
        {
            var listId = ArmyForgeClient.ListId(r.Source);
            if (listId is null) return Problems.Unprocessable("source must be an Army Forge share link or list id");
            AfList list;
            try { list = await forge.Fetch(listId); }
            catch (ArmyForgeException e) { return Problems.BadGateway($"Army Forge answered {e.Status} for list {listId}"); }
            catch (HttpRequestException e) { return Problems.BadGateway($"Army Forge unreachable: {e.Message}"); }
            if (!ArmyForgeImport.GameRules.TryGetValue(list.GameSystem, out var gameRules)) return Problems.Unprocessable($"Only Grimdark Future and Age of Fantasy lists import; this list is '{list.GameSystem}'.");
            var army = ArmyForgeImport.ToArmy(list, r.Source, await kinds.For(gameRules));
            await armies.Insert(CurrentAccount.Id(u), army);
            return Results.Created($"/v1/armies/{army.Id}", army);
        });

        g.MapGet("", async (ClaimsPrincipal u, ArmyRepository armies, int? limit, Guid? after) =>
        {
            var page = Keyset.Parse(limit, after);
            return Results.Ok(Page<ArmySummary>.Of(await armies.List(CurrentAccount.Id(u), page), page.Limit, a => a.Id));
        });
        g.MapGet("/{id:guid}", async (ClaimsPrincipal u, Guid id, ArmyRepository armies) => await armies.Get(CurrentAccount.Id(u), id) is { } a ? Results.Ok(a) : Problems.NotFound());
        g.MapPatch("/{id:guid}", async (ClaimsPrincipal u, Guid id, PatchArmyRequest p, ArmyRepository armies) => await armies.Patch(CurrentAccount.Id(u), id, p) is { } a ? Results.Ok(a) : Problems.NotFound());
        g.MapDelete("/{id:guid}", async (ClaimsPrincipal u, Guid id, ArmyRepository armies) => await armies.Delete(CurrentAccount.Id(u), id) ? Results.NoContent() : Problems.NotFound());
    }
}
```

- [ ] **Step 4: Wire `Program.cs`** — `builder.Services.AddHttpClient(ArmyForgeClient.HttpClientName); builder.Services.AddSingleton(sp => new ArmyForgeClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(ArmyForgeClient.HttpClientName))); builder.Services.AddSingleton<CatalogKinds>(); builder.Services.AddSingleton<ArmyRepository>();` and `app.MapArmies();`.

- [ ] **Step 5: Run `dotnet test tests/Tabletop.Management.Tests`; expect 51 passed. Commit: `Import armies from Army Forge and edit their bindings and bases`.**

### Task 11: Layouts

**Files:**
- Create: `server/management/Content/Layouts/LayoutRepository.cs`, `server/management/Content/Layouts/LayoutEndpoints.cs`
- Modify: `server/management/Program.cs`
- Create: `tests/Tabletop.Management.Tests/LayoutTests.cs`

**Interfaces:**
- Consumes: `Tabletop.Content.TableState`, `TerrainPlacement`, `TerrainVolume`, `Marker`, `MarkerKind`, `DeploymentZone`, `VisualRef`; `Tabletop.Geometry.TableSize`, `Point3`, `Yaw`; `VolumeRow`.
- Produces: `LayoutSummary(Guid Id, string Name, TableSize Size, int Terrain, int Markers)`; `PatchLayoutRequest(string? Name, TableState? State)`; `LayoutRepository(Database)` with `Save(ownerId, TableState) : TableState` (every id regenerated — the rows are copies, never the game's), `Get(ownerId, id) : TableState?`, `List(ownerId) : IReadOnlyList<LayoutSummary>`, `Patch(ownerId, id, PatchLayoutRequest) : TableState?`, `Delete(ownerId, id) : bool`, and `static TableState ReadTree(NpgsqlConnection, Guid tableStateId, string? name, TableSize, VisualRef)` reused by the game manifest; endpoints `GET /v1/layouts → LayoutSummary[]`, `POST /v1/layouts (TableState with Name) → 201 TableState`, `GET /v1/layouts/{id} → TableState`, `PATCH /v1/layouts/{id} → TableState`, `DELETE /v1/layouts/{id} → 204`.
- Column mapping: `table_state(owner_id, name, width, length, texture_builtin | texture_asset_id)`; `terrain_placement(builtin | asset_id, pos_x, pos_y, pos_z, yaw, scale)` + `terrain_volume` rows (always written — a placement's volumes travel with it, contracts plan spec correction 1); `marker(kind, pos_*, yaw, diameter, label, visual_builtin | visual_asset_id)`; `deployment_zone(style = DeploymentZone.Shape, team_ordinal)`.

Spec: setup content → The setup phase is snapshotted ("A Layout is this same record, owned by a user rather than a game"); setup phase → Layouts ("Saving copies the draft into a new row owned by the saver, with a name"; "Loading copies, so the layout row is untouched"); whole-design review ruling 4 ("Layouts stay management's"); `schema.md` → `table_state` has two nullable owners.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Content.Layouts;

namespace Tabletop.Management.Tests;

public class LayoutTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public LayoutTests(ManagementFactory f) => _f = f;

    public static TableState Sample(string? name = "Ruins") => new(Guid.CreateVersion7(), name, new TableSize(72, 48), VisualRef.OfBuiltin("grass"),
        [new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("hill-small"), new Point3(12, 12, 0), new Yaw(45), 1.0,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Rect(6, 6), new Point2(0, 0), new Yaw(0), 0, 2, TerrainDeclarations.Cover | TerrainDeclarations.Difficult, "hill"))]),
         new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfAsset(Guid.CreateVersion7()), new Point3(40, 20, 0), new Yaw(0), 1.5, [])],
        [new Marker(Guid.CreateVersion7(), MarkerKind.Objective, new Point3(36, 24, 0), new Yaw(0), 3, "centre", VisualRef.OfBuiltin("objective-ring"))],
        [new DeploymentZone(Guid.CreateVersion7(), "frontline/1", 1), new DeploymentZone(Guid.CreateVersion7(), "frontline/2", 2)]);

    static string Shape(TableState t) => ContentJson.Serialize(t with { Id = Guid.Empty,
        Terrain = t.Terrain.Select(p => p with { Id = Guid.Empty, Volumes = p.Volumes.Select(v => v with { Id = Guid.Empty }).ToList() }).ToList(),
        Markers = t.Markers.Select(m => m with { Id = Guid.Empty }).ToList(), Zones = t.Zones.Select(z => z with { Id = Guid.Empty }).ToList() });

    [Fact]
    public async Task Save_copies_with_new_ids_and_reads_back_the_same_shape()
    {
        var c = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var input = Sample();
        var saved = await c.PostJson<TableState>("/v1/layouts", input);
        Assert.NotEqual(input.Id, saved.Id);
        Assert.Empty(saved.Terrain.Select(p => p.Id).Intersect(input.Terrain.Select(p => p.Id)));
        Assert.Equal(Shape(input), Shape(saved));
        Assert.Equal(ContentJson.Serialize(saved), ContentJson.Serialize(await c.GetJson<TableState>($"/v1/layouts/{saved.Id}")));
        var list = await c.GetJson<List<LayoutSummary>>("/v1/layouts");
        Assert.Equal([new LayoutSummary(saved.Id, "Ruins", new TableSize(72, 48), 2, 1)], list);
    }

    [Fact]
    public async Task Patch_renames_or_replaces_and_everything_is_owner_only()
    {
        var mine = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var theirs = await _f.AuthenticatedClient(Guid.CreateVersion7());
        var saved = await mine.PostJson<TableState>("/v1/layouts", Sample());
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.GetAsync($"/v1/layouts/{saved.Id}")).StatusCode);
        var renamed = await mine.PatchJson<TableState>($"/v1/layouts/{saved.Id}", new PatchLayoutRequest("Ruins v2", null));
        Assert.Equal("Ruins v2", renamed.Name); Assert.Equal(2, renamed.Terrain.Count);
        var replaced = await mine.PatchJson<TableState>($"/v1/layouts/{saved.Id}", new PatchLayoutRequest(null, Sample() with { Size = new TableSize(44, 30), Terrain = [] }));
        Assert.Equal(("Ruins v2", 44.0, 0), (replaced.Name, replaced.Size.Width, replaced.Terrain.Count));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await mine.PostAsJsonAsync("/v1/layouts", Sample(name: null), ContentJson.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await mine.DeleteAsync($"/v1/layouts/{saved.Id}")).StatusCode);
        Assert.Empty(await mine.GetJson<List<LayoutSummary>>("/v1/layouts"));
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `LayoutRepository.cs`:**

```csharp
using Dapper;
using Npgsql;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Management.Persistence;

namespace Tabletop.Management.Content.Layouts;

public sealed record LayoutSummary(Guid Id, string Name, TableSize Size, int Terrain, int Markers);
public sealed record PatchLayoutRequest(string? Name, TableState? State);

public sealed class LayoutRepository(Database db)
{
    private sealed record Head(Guid Id, string? Name, double Width, double Length, string? TextureBuiltin, Guid? TextureAssetId);
    private sealed record PlacementRow(Guid Id, string? Builtin, Guid? AssetId, double PosX, double PosY, double PosZ, double Yaw, double Scale);
    private sealed record MarkerRow(Guid Id, string Kind, double PosX, double PosY, double PosZ, double Yaw, double Diameter, string? Label, string? VisualBuiltin, Guid? VisualAssetId);
    private sealed record ZoneRow(Guid Id, string Style, int TeamOrdinal);

    public async Task<TableState> Save(Guid ownerId, TableState state)
    {
        var id = Guid.CreateVersion7();
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        await c.ExecuteAsync("insert into table_state (id, owner_id, name, width, length, texture_builtin, texture_asset_id) values (@id, @ownerId, @name, @w, @l, @tb, @ta)",
            new { id, ownerId, name = state.Name, w = state.Size.Width, l = state.Size.Length, tb = state.Texture.Builtin, ta = state.Texture.AssetId }, tx);
        await WriteChildren(c, tx, id, state);
        await tx.CommitAsync();
        return (await Get(ownerId, id))!;
    }

    /// <summary>Copies the children with fresh ids. Shared with Patch; the game server writes the game's own tree, never this code.</summary>
    private static async Task WriteChildren(NpgsqlConnection c, NpgsqlTransaction tx, Guid tableStateId, TableState s)
    {
        foreach (var p in s.Terrain)
        {
            var pid = Guid.CreateVersion7();
            await c.ExecuteAsync("insert into terrain_placement (id, table_state_id, builtin, asset_id, pos_x, pos_y, pos_z, yaw, scale) values (@pid, @tableStateId, @b, @a, @x, @y, @z, @yaw, @scale)",
                new { pid, tableStateId, b = p.Mesh.Builtin, a = p.Mesh.AssetId, x = p.Position.X, y = p.Position.Y, z = p.Position.Z, yaw = p.Yaw.Degrees, scale = p.Scale }, tx);
            foreach (var v in p.Volumes)
            {
                var r = VolumeRow.From(Guid.CreateVersion7(), v.Volume);
                await c.ExecuteAsync($"insert into terrain_volume (terrain_placement_id, {VolumeRow.InsertColumns}) values (@Pid, {VolumeRow.InsertValues})",
                    new { Pid = pid, r.Id, r.Kind, r.Footprint, r.DimA, r.DimB, r.OffsetX, r.OffsetY, r.Yaw, r.Bottom, r.Top, r.Impassable, r.BlocksLos, r.SeeInOut, r.Cover, r.Difficult, r.Dangerous, r.Notes }, tx);
            }
        }
        foreach (var m in s.Markers)
            await c.ExecuteAsync("insert into marker (id, table_state_id, kind, pos_x, pos_y, pos_z, yaw, diameter, label, visual_builtin, visual_asset_id) values (@id, @tableStateId, @kind, @x, @y, @z, @yaw, @d, @label, @vb, @va)",
                new { id = Guid.CreateVersion7(), tableStateId, kind = m.Kind.ToString().ToLowerInvariant(), x = m.Position.X, y = m.Position.Y, z = m.Position.Z, yaw = m.Yaw.Degrees, d = m.Diameter, label = m.Label, vb = m.Visual.Builtin, va = m.Visual.AssetId }, tx);
        foreach (var z in s.Zones)
            await c.ExecuteAsync("insert into deployment_zone (id, table_state_id, style, team_ordinal) values (@id, @tableStateId, @style, @ord)", new { id = Guid.CreateVersion7(), tableStateId, style = z.Shape, ord = z.TeamOrdinal }, tx);
    }

    public static async Task<TableState> ReadTree(NpgsqlConnection c, Guid id, string? name, TableSize size, VisualRef texture)
    {
        var placements = (await c.QueryAsync<PlacementRow>("select id as Id, builtin as Builtin, asset_id as AssetId, pos_x::float8 as PosX, pos_y::float8 as PosY, pos_z::float8 as PosZ, yaw::float8 as Yaw, scale::float8 as Scale from terrain_placement where table_state_id = @id order by id", new { id })).ToList();
        var volumes = new Dictionary<Guid, List<TerrainVolume>>();
        foreach (var p in placements)
            volumes[p.Id] = (await c.QueryAsync<VolumeRow>($"select {VolumeRow.Columns} from terrain_volume where terrain_placement_id = @pid order by id", new { pid = p.Id })).Select(v => new TerrainVolume(v.Id, v.ToVolume())).ToList();
        var markers = await c.QueryAsync<MarkerRow>("select id as Id, kind as Kind, pos_x::float8 as PosX, pos_y::float8 as PosY, pos_z::float8 as PosZ, yaw::float8 as Yaw, diameter::float8 as Diameter, label as Label, visual_builtin as VisualBuiltin, visual_asset_id as VisualAssetId from marker where table_state_id = @id order by id", new { id });
        var zones = await c.QueryAsync<ZoneRow>("select id as Id, style as Style, team_ordinal as TeamOrdinal from deployment_zone where table_state_id = @id order by team_ordinal", new { id });
        return new TableState(id, name, size, texture,
            placements.Select(p => new TerrainPlacement(p.Id, new VisualRef(p.Builtin, p.AssetId), new Point3(p.PosX, p.PosY, p.PosZ), new Yaw(p.Yaw), p.Scale, volumes[p.Id])).ToList(),
            markers.Select(m => new Marker(m.Id, Enum.Parse<MarkerKind>(m.Kind, true), new Point3(m.PosX, m.PosY, m.PosZ), new Yaw(m.Yaw), m.Diameter, m.Label, new VisualRef(m.VisualBuiltin, m.VisualAssetId))).ToList(),
            zones.Select(z => new DeploymentZone(z.Id, z.Style, z.TeamOrdinal)).ToList());
    }

    public async Task<TableState?> Get(Guid ownerId, Guid id)
    {
        await using var c = await db.Open();
        var h = await c.QuerySingleOrDefaultAsync<Head>("select id as Id, name as Name, width::float8 as Width, length::float8 as Length, texture_builtin as TextureBuiltin, texture_asset_id as TextureAssetId from table_state where id = @id and owner_id = @ownerId", new { id, ownerId });
        return h is null ? null : await ReadTree(c, h.Id, h.Name, new TableSize(h.Width, h.Length), new VisualRef(h.TextureBuiltin, h.TextureAssetId));
    }

    public async Task<IReadOnlyList<LayoutSummary>> List(Guid ownerId)
    {
        await using var c = await db.Open();
        var rows = await c.QueryAsync<(Guid Id, string Name, double Width, double Length, int Terrain, int Markers)>(@"select t.id, t.name, t.width::float8, t.length::float8,
            (select count(*) from terrain_placement p where p.table_state_id = t.id)::int, (select count(*) from marker m where m.table_state_id = t.id)::int
            from table_state t where t.owner_id = @ownerId order by t.id", new { ownerId });
        return rows.Select(r => new LayoutSummary(r.Id, r.Name, new TableSize(r.Width, r.Length), r.Terrain, r.Markers)).ToList();
    }

    public async Task<TableState?> Patch(Guid ownerId, Guid id, PatchLayoutRequest p)
    {
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        var n = p.State is null
            ? await c.ExecuteAsync("update table_state set name = coalesce(@name, name) where id = @id and owner_id = @ownerId", new { id, ownerId, name = p.Name }, tx)
            : await c.ExecuteAsync("update table_state set name = coalesce(@name, name), width = @w, length = @l, texture_builtin = @tb, texture_asset_id = @ta where id = @id and owner_id = @ownerId",
                new { id, ownerId, name = p.Name, w = p.State.Size.Width, l = p.State.Size.Length, tb = p.State.Texture.Builtin, ta = p.State.Texture.AssetId }, tx);
        if (n == 0) return null;
        if (p.State is not null)
        {
            await c.ExecuteAsync("delete from terrain_placement where table_state_id = @id; delete from marker where table_state_id = @id; delete from deployment_zone where table_state_id = @id", new { id }, tx);
            await WriteChildren(c, tx, id, p.State);
        }
        await tx.CommitAsync();
        return await Get(ownerId, id);
    }

    public async Task<bool> Delete(Guid ownerId, Guid id)
    {
        await using var c = await db.Open();
        return await c.ExecuteAsync("delete from table_state where id = @id and owner_id = @ownerId", new { id, ownerId }) == 1;
    }
}
```

- [ ] **Step 3: Write `LayoutEndpoints.cs`**

```csharp
using System.Security.Claims;
using Tabletop.Content;
using Tabletop.Management.Http;

namespace Tabletop.Management.Content.Layouts;

public static class LayoutEndpoints
{
    public static void MapLayouts(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/layouts").RequireAuthorization();
        g.MapGet("", async (ClaimsPrincipal u, LayoutRepository layouts) => Results.Ok(await layouts.List(CurrentAccount.Id(u))));
        g.MapPost("", async (ClaimsPrincipal u, TableState state, LayoutRepository layouts) =>
        {
            if (string.IsNullOrWhiteSpace(state.Name)) return Problems.Unprocessable("A saved layout needs a name.");
            var saved = await layouts.Save(CurrentAccount.Id(u), state);
            return Results.Created($"/v1/layouts/{saved.Id}", saved);
        });
        g.MapGet("/{id:guid}", async (ClaimsPrincipal u, Guid id, LayoutRepository layouts) => await layouts.Get(CurrentAccount.Id(u), id) is { } t ? Results.Ok(t) : Problems.NotFound());
        g.MapPatch("/{id:guid}", async (ClaimsPrincipal u, Guid id, PatchLayoutRequest p, LayoutRepository layouts) => await layouts.Patch(CurrentAccount.Id(u), id, p) is { } t ? Results.Ok(t) : Problems.NotFound());
        g.MapDelete("/{id:guid}", async (ClaimsPrincipal u, Guid id, LayoutRepository layouts) => await layouts.Delete(CurrentAccount.Id(u), id) ? Results.NoContent() : Problems.NotFound());
    }
}
```

- [ ] **Step 4: Wire `Program.cs`** (`AddSingleton<LayoutRepository>()`, `app.MapLayouts()`); run `dotnet test`; expect 53 passed. Commit: `Save and load layouts as copies of a table state`.

### Task 12: Properties, games, the public browser and the manifest

**Files:**
- Create: `server/management/Games/GameDtos.cs`, `server/management/Games/GameRepository.cs`, `server/management/Games/GameEndpoints.cs`, `server/management/Games/PropertiesEndpoint.cs`, `server/management/Games/ManifestEndpoint.cs`
- Modify: `server/management/Program.cs`
- Create: `tests/Tabletop.Management.Tests/GameTests.cs`, `tests/Tabletop.Management.Tests/ManifestTests.cs`

**Interfaces:**
- Consumes: `Tabletop.Protocol.GameInfo`, `Team`, `Participant`, `GameStatus`, `SchedulerShape`, `Enforcement`; `Tabletop.Content.ArmySnapshot`, `ContentJson`; `IBlobStore`; `LayoutRepository.ReadTree` is not needed — the manifest reads asset ids straight from the rows.
- Produces: `PropertiesSummary(string ContentHash, string Name, string GameRules)`; `CreateGameRequest(string Name, string PropertiesHash, SchedulerShape SchedulerShape, Enforcement Enforcement, bool WaitsBetweenSteps)`; `GameSummary(Guid Id, string Name, GameStatus Status, Guid CreatedBy, DateTimeOffset CreatedAt, int Participants)`; `ManifestEntry(Guid AssetId, Content.Assets.AssetKind Kind, string Hash, Uri Url, double Scale, Geometry.Orientation Orientation)` (scale and orientation are the owner's import corrections from the `asset` row — the client draws another participant's mesh from the manifest alone, since `GET /v1/assets/{id}` is owner-only); `Manifest(IReadOnlyList<ManifestEntry> Assets, DateTimeOffset ExpiresAt)`; `ManifestRow(Guid AssetId, Guid OwnerId, string Kind, string Hash, double Scale, double Pitch, double Yaw, double Roll)`; `GameRepository(Database)` with `ListProperties()`, `Create(creator, CreateGameRequest) : GameInfo?` (null: unknown hash), `List(Keyset) : IReadOnlyList<GameSummary>`, `Get(id) : GameInfo?`, `IsParticipant(id, accountId)`, `Join(id, accountId) : GameInfo?`, `Leave(id, accountId) : bool` (ends the game when the last participant leaves), `End(id) : bool`, `Delete(id) : bool` (game rows and `game.game_event`), `ManifestAssets(id) : IReadOnlyList<ManifestRow>`; endpoints `GET /v1/properties → PropertiesSummary[]`; `GET /v1/games?limit&after → Page<GameSummary>` (every status); `POST /v1/games → 201 GameInfo`; `GET /v1/games/{id} → GameInfo`; `POST /v1/games/{id}/join → GameInfo`; `POST /v1/games/{id}/leave → 204`; `POST /v1/games/{id}/end → 204`; `DELETE /v1/games/{id} → 204`; `GET /v1/games/{id}/assets → Manifest`.
- Rows management writes here: `game` insert (`status = 'lobby'`, `game_rules` copied from the chosen `properties` row, `rng_seed` generated here, `created_by`, `created_at`); `participant` insert (`team_id null`, `army_id null`, `army_snapshot null`) on create and join, delete on leave; `game.status = 'ended'` on `/end` and on the last leave; whole-game delete. Management never inserts `team`, never sets `participant.team_id/army_id/army_snapshot`, never writes a `table_state` with `game_id` — the game server does, while status is `lobby` or `setup`.
- **`rng_seed` is generated here, in `Create`, and never written again.** It is not in `CreateGameRequest`: the client does not choose a game's dice. The value is `Random.Shared.NextInt64()` — any `bigint` is valid and the seed is not a secret, since editing a roll is already a first-class command — and it goes into the same `insert` as the rest of the configuration, inside the same transaction, so a game row cannot exist without one. `management.game.rng_seed` is `bigint not null` with **no default** (contracts plan Task 15), so an insert that forgets it fails rather than quietly seeding every such game alike. `Get` reads it back into `GameInfo.RngSeed`, which is how it reaches the game server at hydration and the interpreter after that. No endpoint updates it: not `/join`, not `/leave`, not `/end`, and there is no configuration `PATCH` on a game — the game server rewrites the other configuration columns on `set_configuration` and leaves this one alone. Owner ruling 1 of 2026-09-06.
- **`POST /v1/games/{id}/join` joins the game. It does not take a team.** The two are separate operations on separate paths, and conflating them is what has kept the question alive. Joining is this endpoint and it inserts a `participant` row with `team_id null`; it is allowed at any status, including `playing` and `ended`, and that is unchanged. Taking a team is a lobby edit on the game server's socket, allowed only while the game is `lobby` or `setup`, and there is no route for it here. So an account that joins a `playing` game gets a row and no team, which is the definition of a spectator, while a player who already had a slot rejoins it by reconnecting rather than by joining again. The canonical statement of the team model is game flow → *Teams are frozen at the flip, and a team is a slot*.
- `/end` and `DELETE` are the creator's alone, and remain so after the creator leaves (question 6, ruled 2026-09-06); `/leave` still needs only a participant row. `GET /v1/games/{id}` needs only a login: games are public. The manifest is the one cross-owner read: for every participant it takes the slots' `asset_id` from the picked army (`participant.army_id`, before the snapshot) or from the frozen `army_snapshot` (after), plus the game's `table_state` texture, terrain `asset_id`s and marker `visual_asset_id`s, joins `asset`/`blob` where `uploaded`, and presigns one GET per asset for 60 minutes. Nothing is copied and no row is created.

Spec: management API → The game manifest ("Authorised by being a participant. Returns presigned GET URLs for every mesh and texture in play, whoever owns them"), The rest of the surface (games; `POST /v1/games name, properties_hash, scheduler_shape, enforcement, waits_between_steps`; `GET /v1/properties`); accounts → A game is a lobby ("Games are public … anyone logged in may join"; "creates the game and the participant row that puts them in it — on no team until they pick one"), Ending is a flag ("A game ends when someone ends it explicitly, or when the last participant leaves"), Still open ("joinable at every status"); setup phase → Joining is allowed at any status, The lifecycle (`game.created_by`); whole-design review rulings 4 and 5; `schema.md` → GAME, TEAM, PARTICIPANT, "Deleting an account is application logic" (the same cross-schema delete, per game); owner ruling 1 of 2026-09-06 and contracts-plan spec correction 12 (`game.rng_seed`, written once here).

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using Dapper;
using Npgsql;
using Tabletop.Management.Games;
using Tabletop.Management.Http;
using Tabletop.Protocol;

namespace Tabletop.Management.Tests;

public class GameTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public GameTests(ManagementFactory f) => _f = f;
    const string Hash = "sha256:test-properties";

    async Task Seed()
    {
        await using var c = new NpgsqlConnection(_f.Database.ConnectionString);
        await c.ExecuteAsync("insert into management.properties (content_hash, name, game_rules) values (@Hash, 'Grimdark Future core', 'grimdark-future') on conflict do nothing", new { Hash });
    }
    static CreateGameRequest Friday() => new("Friday", Hash, SchedulerShape.AlternatingActivation, Enforcement.Warn, false);

    [Fact]
    public async Task Properties_lists_the_pinned_sets_and_create_copies_game_rules_from_the_chosen_one()
    {
        await Seed();
        var creator = Guid.CreateVersion7();
        var c = await _f.AuthenticatedClient(creator, "Clay");
        Assert.Contains(await c.GetJson<List<PropertiesSummary>>("/v1/properties"), p => p == new PropertiesSummary(Hash, "Grimdark Future core", "grimdark-future"));
        var game = await c.PostJson<GameInfo>("/v1/games", Friday());
        Assert.Equal(("Friday", GameStatus.Lobby, creator, "grimdark-future", Hash, SchedulerShape.AlternatingActivation, Enforcement.Warn, false),
            (game.Name, game.Status, game.CreatedBy, game.GameRules, game.PropertiesHash, game.SchedulerShape, game.Enforcement, game.WaitsBetweenSteps));
        Assert.Empty(game.Teams);
        Assert.Equal([(creator, "Clay", (Guid?)null, (Guid?)null)], game.Participants.Select(p => (p.AccountId, p.Name, p.TeamId, p.ArmyId)));
        Assert.NotEqual(0L, game.RngSeed);                                                                      // generated at creation, not defaulted: a 0 seed would mean shared dice
        Assert.Equal(game.RngSeed, (await c.GetJson<GameInfo>($"/v1/games/{game.Id}")).RngSeed);                 // stored, and read back unchanged
        Assert.NotEqual(game.RngSeed, (await c.PostJson<GameInfo>("/v1/games", Friday())).RngSeed);              // per game, not per properties set
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsJsonAsync("/v1/games", Friday() with { PropertiesHash = "sha256:nope" }, Tabletop.Content.ContentJson.Options)).StatusCode);
    }

    [Fact]
    public async Task Browser_is_public_join_is_open_at_any_status_and_the_last_leave_ends_the_game()
    {
        await Seed();
        var a = await _f.AuthenticatedClient(Guid.CreateVersion7(), "A");
        var b = await _f.AuthenticatedClient(Guid.CreateVersion7(), "B");
        var game = await a.PostJson<GameInfo>("/v1/games", Friday());
        Assert.Contains((await b.GetJson<Page<GameSummary>>("/v1/games?limit=200")).Items, g => g.Id == game.Id && g.Participants == 1);
        Assert.Equal(HttpStatusCode.NoContent, (await a.PostAsync($"/v1/games/{game.Id}/end", null)).StatusCode);
        var joined = await b.PostJson<GameInfo>($"/v1/games/{game.Id}/join", new { });
        Assert.Equal(GameStatus.Ended, joined.Status);
        Assert.Equal(2, joined.Participants.Count);
        Assert.Equal(2, (await b.PostJson<GameInfo>($"/v1/games/{game.Id}/join", new { })).Participants.Count);   // idempotent
        Assert.Equal(HttpStatusCode.NoContent, (await a.PostAsync($"/v1/games/{game.Id}/leave", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsync($"/v1/games/{game.Id}/leave", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/v1/games/{game.Id}")).StatusCode);         // a participant, not the creator
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync($"/v1/games/{game.Id}/end", null)).StatusCode);  // same rule on both routes
        Assert.Equal(HttpStatusCode.NoContent, (await b.PostAsync($"/v1/games/{game.Id}/leave", null)).StatusCode);
        Assert.Equal(GameStatus.Ended, (await a.GetJson<GameInfo>($"/v1/games/{game.Id}")).Status);
        await using var c = new NpgsqlConnection(_f.Database.ConnectionString);
        await c.ExecuteAsync("insert into game.game_event (game_id, seq, kind, payload) values (@id, 1, 'turn_started', '{}')", new { id = game.Id });
        var again = await b.PostJson<GameInfo>($"/v1/games/{game.Id}/join", new { });
        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync($"/v1/games/{game.Id}")).StatusCode);         // the creator, who left, still may
        Assert.Equal(0, await c.ExecuteScalarAsync<int>("select count(*) from game.game_event where game_id = @id", new { id = game.Id }));
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/v1/games/{game.Id}")).StatusCode);
    }
}
```

```csharp
using System.Net;
using Dapper;
using Npgsql;
using Tabletop.Content;
using Tabletop.Management.Content.Armies;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Games;
using Tabletop.Protocol;

namespace Tabletop.Management.Tests;

public class ManifestTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public ManifestTests(ManagementFactory f) => _f = f;

    [Fact]
    public async Task Participants_get_every_uploaded_asset_in_play_whoever_owns_it()
    {
        await using var c = new NpgsqlConnection(_f.Database.ConnectionString);
        await c.ExecuteAsync("insert into management.properties (content_hash, name, game_rules) values ('sha256:m', 'GF', 'grimdark-future') on conflict do nothing");
        var (aId, bId, outsiderId) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var a = await _f.AuthenticatedClient(aId, "A"); var b = await _f.AuthenticatedClient(bId, "B"); var outsider = await _f.AuthenticatedClient(outsiderId, "O");
        var game = await a.PostJson<GameInfo>("/v1/games", new CreateGameRequest("M", "sha256:m", SchedulerShape.PlayerTurn, Enforcement.Silent, false));
        await b.PostJson<GameInfo>($"/v1/games/{game.Id}/join", new { });

        async Task<AssetDto> Upload(HttpClient who, byte[] glb, AssetKind kind, string name)
        {
            var req = new CreateAssetRequest(name, kind, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(glb)), glb.Length, kind == AssetKind.Texture ? "image/png" : "model/gltf-binary", 1, new(0, 0, 0), kind == AssetKind.Miniature ? Tabletop.Geometry.Base.Circle(1.26) : null);
            var created = await who.PostJson<CreateAssetResponse>("/v1/assets", req);
            using var http = new HttpClient();
            await http.PutAsync(created.UploadUrl, new ByteArrayContent(glb) { Headers = { ContentType = new(req.ContentType) } });
            return (await who.PostJson<ConfirmResponse>($"/v1/assets/{created.Asset.Id}/confirm", new { })).Asset;
        }
        var aMesh = await Upload(a, TestGlb.Build((3, null, true)), AssetKind.Miniature, "a-mesh");
        var bMesh = await Upload(b, TestGlb.Build((6, null, true)), AssetKind.Miniature, "b-mesh");
        var texture = await Upload(b, [0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4], AssetKind.Texture, "board");
        var unconfirmed = await a.PostJson<CreateAssetResponse>("/v1/assets", new CreateAssetRequest("pending", AssetKind.Miniature, new string('a', 64), 10, "model/gltf-binary", 1, new(0, 0, 0), Tabletop.Geometry.Base.Circle(1)));

        var aArmy = await a.PostJson<Army>("/v1/armies/import", new ImportRequest("c2-6YsWRH3py"));
        var aUnit = aArmy.Units[0];
        await a.PatchJson<Army>($"/v1/armies/{aArmy.Id}", new PatchArmyRequest(null, [new UnitPatch(aUnit.Id, aMesh.Id)], [new SlotPatch(aUnit.Models[1].Id, unconfirmed.Asset.Id, false, null, null)]));
        var bArmy = await b.PostJson<Army>("/v1/armies/import", new ImportRequest("c2-6YsWRH3py"));
        var bSnapshot = ArmySnapshot.Of(bArmy with { Units = [bArmy.Units[0] with { Models = [bArmy.Units[0].Models[0] with { AssetId = bMesh.Id }] }] }, Guid.CreateVersion7());

        // What the game server writes while the game is in lobby/setup: A's pick, B's frozen snapshot, the draft with B's texture.
        await c.ExecuteAsync("update management.participant set army_id = @army where game_id = @game and account_id = @acct", new { army = aArmy.Id, game = game.Id, acct = aId });
        await c.ExecuteAsync("update management.participant set army_snapshot = @snap::jsonb where game_id = @game and account_id = @acct", new { snap = ContentJson.Serialize(bSnapshot), game = game.Id, acct = bId });
        await c.ExecuteAsync("insert into management.table_state (id, game_id, width, length, texture_asset_id) values (@id, @game, 72, 48, @tex)", new { id = Guid.CreateVersion7(), game = game.Id, tex = texture.Id });

        await b.PatchJson<AssetDto>($"/v1/assets/{bMesh.Id}", new PatchAssetRequest(null, 2.5, new Tabletop.Geometry.Orientation(90, 0, 0), null));   // B's import corrections

        var manifest = await a.GetJson<Manifest>($"/v1/games/{game.Id}/assets");
        Assert.Equal(new HashSet<Guid> { aMesh.Id, bMesh.Id, texture.Id }, manifest.Assets.Select(m => m.AssetId).ToHashSet());   // the unconfirmed one is absent
        var bEntry = manifest.Assets.Single(m => m.AssetId == bMesh.Id);
        Assert.Equal((2.5, new Tabletop.Geometry.Orientation(90, 0, 0)), (bEntry.Scale, bEntry.Orientation));                  // A can draw B's mesh as B corrected it
        var aEntry = manifest.Assets.Single(m => m.AssetId == aMesh.Id);
        Assert.Equal((1.0, new Tabletop.Geometry.Orientation(0, 0, 0)), (aEntry.Scale, aEntry.Orientation));                   // untouched: the row's defaults
        using var http = new HttpClient();
        Assert.Equal(TestGlb.Build((6, null, true)), await http.GetByteArrayAsync(manifest.Assets.Single(m => m.AssetId == bMesh.Id).Url));
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/v1/games/{game.Id}/assets")).StatusCode);
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `GameDtos.cs` and `GameRepository.cs`:**

```csharp
using Tabletop.Geometry;
using Tabletop.Management.Content.Assets;
using Tabletop.Protocol;

namespace Tabletop.Management.Games;

public sealed record PropertiesSummary(string ContentHash, string Name, string GameRules);
public sealed record CreateGameRequest(string Name, string PropertiesHash, SchedulerShape SchedulerShape, Enforcement Enforcement, bool WaitsBetweenSteps);
public sealed record GameSummary(Guid Id, string Name, GameStatus Status, Guid CreatedBy, DateTimeOffset CreatedAt, int Participants);
/// <summary>One asset in play. Scale and Orientation are the owner's import corrections: the client cannot read another participant's asset row, so they ride here.</summary>
public sealed record ManifestEntry(Guid AssetId, AssetKind Kind, string Hash, Uri Url, double Scale, Orientation Orientation);
public sealed record Manifest(IReadOnlyList<ManifestEntry> Assets, DateTimeOffset ExpiresAt);
/// <summary>What the manifest query returns per asset, before presigning.</summary>
public sealed record ManifestRow(Guid AssetId, Guid OwnerId, string Kind, string Hash, double Scale, double Pitch, double Yaw, double Roll);
```

```csharp
using Dapper;
using Tabletop.Content;
using Tabletop.Management.Persistence;
using Tabletop.Protocol;

namespace Tabletop.Management.Games;

public sealed class GameRepository(Database db)
{
    private sealed record GameRow(Guid Id, string Name, string Status, string GameRules, string PropertiesHash, string SchedulerShape, string Enforcement, bool WaitsBetweenSteps, long RngSeed, Guid CreatedBy);
    private sealed record ParticipantRow(Guid Id, Guid AccountId, string Name, Guid? TeamId, Guid? ArmyId);
    private sealed record SummaryRow(Guid Id, string Name, string Status, Guid CreatedBy, DateTimeOffset CreatedAt, int Participants);
    private static readonly IReadOnlyDictionary<string, SchedulerShape> Shapes = new Dictionary<string, SchedulerShape> { ["player-turn"] = SchedulerShape.PlayerTurn, ["alternating-activation"] = SchedulerShape.AlternatingActivation };
    private static string Wire(SchedulerShape s) => Shapes.First(kv => kv.Value == s).Key;

    public async Task<IReadOnlyList<PropertiesSummary>> ListProperties()
    {
        await using var c = await db.Open();
        return (await c.QueryAsync<PropertiesSummary>("select content_hash as ContentHash, name as Name, game_rules as GameRules from properties order by game_rules, name")).ToList();
    }

    public async Task<GameInfo?> Create(Guid creator, CreateGameRequest r)
    {
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        var gameRules = await c.ExecuteScalarAsync<string?>("select game_rules from properties where content_hash = @hash", new { hash = r.PropertiesHash }, tx);
        if (gameRules is null) return null;
        var id = Guid.CreateVersion7();
        // The game's dice seed, generated once here and never written again. The column has no default, so this is the only place it can come from.
        var rngSeed = Random.Shared.NextInt64();
        await c.ExecuteAsync(@"insert into game (id, name, status, game_rules, properties_hash, scheduler_shape, enforcement, waits_between_steps, rng_seed, created_by)
            values (@id, @name, 'lobby', @gameRules, @hash, @shape, @enforcement, @waits, @rngSeed, @creator)",
            new { id, name = r.Name, gameRules, hash = r.PropertiesHash, shape = Wire(r.SchedulerShape), enforcement = r.Enforcement.ToString().ToLowerInvariant(), waits = r.WaitsBetweenSteps, rngSeed, creator }, tx);
        await c.ExecuteAsync("insert into participant (id, game_id, account_id) values (@pid, @id, @creator)", new { pid = Guid.CreateVersion7(), id, creator }, tx);
        await tx.CommitAsync();
        return await Get(id);
    }

    public async Task<IReadOnlyList<GameSummary>> List(Http.Keyset page)
    {
        await using var c = await db.Open();
        // A named record, not a ValueTuple: Dapper maps to constructor parameters by column name and cannot map tuple element names.
        var rows = await c.QueryAsync<SummaryRow>(@"select g.id as Id, g.name as Name, g.status as Status, g.created_by as CreatedBy, g.created_at as CreatedAt,
            (select count(*) from participant p where p.game_id = g.id)::int as Participants
            from game g where (@after::uuid is null or g.id < @after::uuid) order by g.id desc limit @limit", new { after = page.After, limit = page.Limit });
        return rows.Select(r => new GameSummary(r.Id, r.Name, Enum.Parse<GameStatus>(r.Status, true), r.CreatedBy, r.CreatedAt, r.Participants)).ToList();
    }

    public async Task<GameInfo?> Get(Guid id)
    {
        await using var c = await db.Open();
        var g = await c.QuerySingleOrDefaultAsync<GameRow>(@"select id as Id, name as Name, status as Status, game_rules as GameRules, properties_hash as PropertiesHash, scheduler_shape as SchedulerShape,
            enforcement as Enforcement, waits_between_steps as WaitsBetweenSteps, rng_seed as RngSeed, created_by as CreatedBy from game where id = @id", new { id });
        if (g is null) return null;
        var teams = (await c.QueryAsync<Team>("select id as Id, ordinal as Ordinal from team where game_id = @id order by ordinal", new { id })).ToList();
        var participants = (await c.QueryAsync<ParticipantRow>(@"select p.id as Id, p.account_id as AccountId, a.name as Name, p.team_id as TeamId, p.army_id as ArmyId
            from participant p join account a on a.id = p.account_id where p.game_id = @id order by p.id", new { id })).Select(p => new Participant(p.Id, p.AccountId, p.Name, p.TeamId, p.ArmyId)).ToList();
        return new GameInfo(g.Id, g.Name, Enum.Parse<GameStatus>(g.Status, true), g.CreatedBy, g.GameRules, g.PropertiesHash, Shapes[g.SchedulerShape], Enum.Parse<Enforcement>(g.Enforcement, true), g.WaitsBetweenSteps, g.RngSeed, teams, participants);
    }

    public async Task<bool> IsParticipant(Guid id, Guid accountId)
    {
        await using var c = await db.Open();
        return await c.ExecuteScalarAsync<bool>("select exists(select 1 from participant where game_id = @id and account_id = @accountId)", new { id, accountId });
    }

    /// <summary>Ending and deleting are the creator's, and stay the creator's after they leave: created_by has no on-delete action and outlives the participant row (ruled 2026-09-06).</summary>
    public async Task<bool> IsCreator(Guid id, Guid accountId)
    {
        await using var c = await db.Open();
        return await c.ExecuteScalarAsync<bool>("select exists(select 1 from game where id = @id and created_by = @accountId)", new { id, accountId });
    }

    /// <summary>Joining the game is a participant row with no team, at any status; joining twice is the same row (unique (game_id, account_id)).</summary>
    /// <remarks>Taking a team is a game-server lobby edit before play, not this. See the game-flow spec, "Teams are frozen at the flip, and a team is a slot".</remarks>
    public async Task<GameInfo?> Join(Guid id, Guid accountId)
    {
        await using var c = await db.Open();
        var n = await c.ExecuteAsync("insert into participant (id, game_id, account_id) select @pid, @id, @accountId where exists (select 1 from game where id = @id) on conflict (game_id, account_id) do nothing",
            new { pid = Guid.CreateVersion7(), id, accountId });
        return await Get(id);
    }

    public async Task<bool> Leave(Guid id, Guid accountId)
    {
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        if (await c.ExecuteAsync("delete from participant where game_id = @id and account_id = @accountId", new { id, accountId }, tx) == 0) return false;
        await c.ExecuteAsync("update game set status = 'ended' where id = @id and not exists (select 1 from participant where game_id = @id)", new { id }, tx);
        await tx.CommitAsync();
        return true;
    }

    public async Task<bool> End(Guid id)
    {
        await using var c = await db.Open();
        return await c.ExecuteAsync("update game set status = 'ended' where id = @id", new { id }) == 1;
    }

    /// <summary>The game's rows cascade in the management schema; the log is a separate delete because no FK crosses the schema boundary.</summary>
    public async Task<bool> Delete(Guid id)
    {
        await using var c = await db.Open();
        await using var tx = await c.BeginTransactionAsync();
        await c.ExecuteAsync("delete from game.game_event where game_id = @id", new { id }, tx);
        var n = await c.ExecuteAsync("delete from game where id = @id", new { id }, tx);
        await tx.CommitAsync();
        return n == 1;
    }

    public async Task<IReadOnlyList<ManifestRow>> ManifestAssets(Guid id)
    {
        await using var c = await db.Open();
        var ids = new HashSet<Guid>();
        ids.UnionWith(await c.QueryAsync<Guid>(@"select s.asset_id from participant p join unit u on u.army_id = p.army_id join model_slot s on s.unit_id = u.id
            where p.game_id = @id and p.army_snapshot is null and s.asset_id is not null", new { id }));
        foreach (var json in await c.QueryAsync<string>("select army_snapshot::text from participant where game_id = @id and army_snapshot is not null", new { id }))
            ids.UnionWith(ContentJson.Deserialize<ArmySnapshot>(json).Units.SelectMany(u => u.Models).Where(m => m.AssetId is not null).Select(m => m.AssetId!.Value));
        ids.UnionWith(await c.QueryAsync<Guid>(@"select texture_asset_id from table_state where game_id = @id and texture_asset_id is not null
            union select tp.asset_id from terrain_placement tp join table_state t on t.id = tp.table_state_id where t.game_id = @id and tp.asset_id is not null
            union select m.visual_asset_id from marker m join table_state t on t.id = m.table_state_id where t.game_id = @id and m.visual_asset_id is not null", new { id }));
        if (ids.Count == 0) return [];
        return (await c.QueryAsync<ManifestRow>(@"select a.id as AssetId, a.owner_id as OwnerId, a.kind as Kind, a.blob_hash as Hash, a.scale::float8 as Scale,
            a.orientation_pitch::float8 as Pitch, a.orientation_yaw::float8 as Yaw, a.orientation_roll::float8 as Roll
            from asset a join blob b on b.owner_id = a.owner_id and b.hash = a.blob_hash
            where a.id = any(@ids) and b.uploaded", new { ids = ids.ToArray() })).ToList();
    }
}
```

- [ ] **Step 3: Write `PropertiesEndpoint.cs`, `GameEndpoints.cs`, `ManifestEndpoint.cs`**

```csharp
namespace Tabletop.Management.Games;

public static class PropertiesEndpoint
{
    public static void MapProperties(this IEndpointRouteBuilder app) =>
        app.MapGet("/v1/properties", async (GameRepository games) => Results.Ok(await games.ListProperties())).RequireAuthorization();
}
```

```csharp
using System.Security.Claims;
using Tabletop.Management.Http;

namespace Tabletop.Management.Games;

public static class GameEndpoints
{
    public static void MapGames(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/games").RequireAuthorization();
        g.MapGet("", async (GameRepository games, int? limit, Guid? after) =>
        {
            var page = Keyset.Parse(limit, after);
            return Results.Ok(Page<GameSummary>.Of(await games.List(page), page.Limit, x => x.Id));
        });
        g.MapPost("", async (ClaimsPrincipal u, CreateGameRequest r, GameRepository games) =>
            string.IsNullOrWhiteSpace(r.Name) ? Problems.Unprocessable("name is required")
            : await games.Create(CurrentAccount.Id(u), r) is { } game ? Results.Created($"/v1/games/{game.Id}", game) : Problems.Unprocessable($"No Properties set with hash {r.PropertiesHash}. GET /v1/properties lists the pinned sets."));
        g.MapGet("/{id:guid}", async (Guid id, GameRepository games) => await games.Get(id) is { } game ? Results.Ok(game) : Problems.NotFound());
        g.MapPost("/{id:guid}/join", async (ClaimsPrincipal u, Guid id, GameRepository games) => await games.Join(id, CurrentAccount.Id(u)) is { } game ? Results.Ok(game) : Problems.NotFound());
        g.MapPost("/{id:guid}/leave", async (ClaimsPrincipal u, Guid id, GameRepository games) => await games.Leave(id, CurrentAccount.Id(u)) ? Results.NoContent() : Problems.NotFound());
        g.MapPost("/{id:guid}/end", async (ClaimsPrincipal u, Guid id, GameRepository games) =>
            await games.IsCreator(id, CurrentAccount.Id(u)) && await games.End(id) ? Results.NoContent() : Problems.NotFound());
        g.MapDelete("/{id:guid}", async (ClaimsPrincipal u, Guid id, GameRepository games) =>
            await games.IsCreator(id, CurrentAccount.Id(u)) && await games.Delete(id) ? Results.NoContent() : Problems.NotFound());
    }
}
```

```csharp
using System.Security.Claims;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Http;

namespace Tabletop.Management.Games;

public static class ManifestEndpoint
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(60);

    public static void MapManifest(this IEndpointRouteBuilder app) =>
        app.MapGet("/v1/games/{id:guid}/assets", async (ClaimsPrincipal u, Guid id, GameRepository games, IBlobStore blobs) =>
        {
            if (!await games.IsParticipant(id, CurrentAccount.Id(u))) return Problems.NotFound();
            var entries = new List<ManifestEntry>();
            foreach (var r in await games.ManifestAssets(id))
                entries.Add(new ManifestEntry(r.AssetId, AssetRules.Kind(r.Kind), r.Hash, await blobs.PresignDownload(IBlobStore.Key(r.OwnerId, r.Hash), Ttl), r.Scale, new Tabletop.Geometry.Orientation(r.Pitch, r.Yaw, r.Roll)));
            return Results.Ok(new Manifest(entries, DateTimeOffset.UtcNow.Add(Ttl)));
        }).RequireAuthorization();
}
```

- [ ] **Step 4: Wire `Program.cs`** (`AddSingleton<GameRepository>()`, `app.MapProperties(); app.MapGames(); app.MapManifest();`); run `dotnet test`; expect 56 passed. Commit: `Add games, the public browser, join and leave, and the asset manifest`.

### Task 13: The account — rename, and total deletion

**Files:**
- Create: `server/management/Identity/AccountDeleter.cs`
- Modify: `server/management/Identity/AccountEndpoints.cs`, `server/management/Program.cs`
- Create: `tests/Tabletop.Management.Tests/AccountTests.cs`

**Interfaces:**
- Consumes: `AccountRepository`, `GameRepository.Delete`, `IBlobStore.Delete`, `StytchClient.DeleteUser`.
- Produces: `RenameRequest(string Name)`; `AccountDeleter(Database, GameRepository, IBlobStore, StytchClient).Delete(Guid accountId) : Task<bool>`; `PATCH /v1/account {name} → AccountRow`; `DELETE /v1/account → 204`.
- Order of deletion, and why: (1) read the account's blob keys; (2) read every game the account touches — the `game_id`s it participates in **and** the `game.created_by` rows it created — and delete each one; its rows cascade in the management schema and `game.game_event` is deleted explicitly, because no FK crosses the schema boundary; (3) delete the account row, cascading armies, assets, blobs, folders and layouts; (4) delete the S3 objects; (5) delete the Stytch user by `external_id`. Database first so that a Stytch failure leaves no orphaned content; a Stytch `404` is ignored; another Stytch failure answers `502` after the content is already gone, and logging in again recreates an empty account with the same id (`Sessions.Complete` inserts on conflict).
- **`created_by` is why the game set is a union, not just the participant rows.** `game.created_by` is `not null references account(id)` with no `on delete` action (contracts plan, `001_schema.sql`), and `/leave` deletes the participant row while the game survives — so an account that created a game and then left is still named by `created_by`, and `delete from account` would fail on that FK. `schema.md` → A game records its creator asserts *"Account deletion already destroys every game the account played in, so a deleted creator leaves no orphan"*; the union is what makes that true.

Spec: accounts → Deletion is total ("destroys its armies, assets, blobs, folders and layouts, and deletes every game it participated in — the game record and its event log — and then the account row"; "The collateral is real and accepted"); `schema.md` → Deleting an account is application logic ("a deliberate multi-step operation in the delete path rather than a constraint or a trigger"); management API → The rest of the surface (`PATCH /v1/account name`, `DELETE /v1/account the full cascade`).

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using Dapper;
using Npgsql;
using Tabletop.Content;
using Tabletop.Management.Content.Armies;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Content.Layouts;
using Tabletop.Management.Games;
using Tabletop.Management.Identity;
using Tabletop.Protocol;

namespace Tabletop.Management.Tests;

public class AccountTests : IClassFixture<ManagementFactory>
{
    private readonly ManagementFactory _f;
    public AccountTests(ManagementFactory f) => _f = f;

    [Fact]
    public async Task Rename()
    {
        var id = Guid.CreateVersion7();
        var c = await _f.AuthenticatedClient(id, "Old");
        Assert.Equal(new AccountRow(id, "New"), await c.PatchJson<AccountRow>("/v1/account", new RenameRequest("New")));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PatchAsJsonAsync("/v1/account", new RenameRequest("  "), ContentJson.Options)).StatusCode);
    }

    [Fact]
    public async Task Deletion_removes_content_blobs_every_game_played_in_and_the_stytch_user()
    {
        await using var db = new NpgsqlConnection(_f.Database.ConnectionString);
        await db.ExecuteAsync("insert into management.properties (content_hash, name, game_rules) values ('sha256:d', 'GF', 'grimdark-future') on conflict do nothing");
        var (meId, otherId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var me = await _f.AuthenticatedClient(meId, "Me"); var other = await _f.AuthenticatedClient(otherId, "Other");
        _f.Stytch.Users["me@example.com"] = new FakeStytch.User { Email = "me@example.com", ExternalId = meId.ToString() };

        var glb = TestGlb.Build((3, null, true));
        var created = await me.PostJson<CreateAssetResponse>("/v1/assets", new CreateAssetRequest("m", AssetKind.Miniature, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(glb)), glb.Length, "model/gltf-binary", 1, new(0, 0, 0), Tabletop.Geometry.Base.Circle(1.26)));
        using (var http = new HttpClient()) await http.PutAsync(created.UploadUrl, new ByteArrayContent(glb) { Headers = { ContentType = new("model/gltf-binary") } });
        await me.PostJson<ConfirmResponse>($"/v1/assets/{created.Asset.Id}/confirm", new { });
        await me.PostJson<Army>("/v1/armies/import", new ImportRequest("c2-6YsWRH3py"));
        await me.PostJson<FolderDto>("/v1/folders", new FolderRequest("F", null));
        await me.PostJson<TableState>("/v1/layouts", LayoutTests.Sample());
        var mine = await me.PostJson<GameInfo>("/v1/games", new CreateGameRequest("Mine", "sha256:d", SchedulerShape.PlayerTurn, Enforcement.Warn, false));
        var left = await me.PostJson<GameInfo>("/v1/games", new CreateGameRequest("Left", "sha256:d", SchedulerShape.PlayerTurn, Enforcement.Warn, false));
        await other.PostJson<GameInfo>($"/v1/games/{left.Id}/join", new { });                        // so the game outlives my leaving
        Assert.Equal(HttpStatusCode.NoContent, (await me.PostAsync($"/v1/games/{left.Id}/leave", null)).StatusCode);   // created_by me, no participant row of mine
        var theirs = await other.PostJson<GameInfo>("/v1/games", new CreateGameRequest("Theirs", "sha256:d", SchedulerShape.PlayerTurn, Enforcement.Warn, false));
        await me.PostJson<GameInfo>($"/v1/games/{theirs.Id}/join", new { });
        var untouched = await other.PostJson<GameInfo>("/v1/games", new CreateGameRequest("Untouched", "sha256:d", SchedulerShape.PlayerTurn, Enforcement.Warn, false));
        await db.ExecuteAsync("insert into game.game_event (game_id, seq, kind, payload) values (@id, 1, 'turn_started', '{}')", new { id = theirs.Id });

        Assert.Equal(HttpStatusCode.NoContent, (await me.DeleteAsync("/v1/account")).StatusCode);

        Assert.Equal(0, await db.ExecuteScalarAsync<int>("select count(*) from management.account where id = @meId", new { meId }));
        foreach (var table in new[] { "asset", "blob", "army", "folder", "table_state" })
            Assert.Equal(0, await db.ExecuteScalarAsync<int>($"select count(*) from management.{table} where owner_id = @meId", new { meId }));
        Assert.Null(await _f.Services.GetRequiredService<IBlobStore>().Size(IBlobStore.Key(meId, created.Asset.Hash)));
        Assert.Equal(0, await db.ExecuteScalarAsync<int>("select count(*) from management.game where id in (@a, @b, @c)", new { a = mine.Id, b = theirs.Id, c = left.Id }));
        Assert.Equal(0, await db.ExecuteScalarAsync<int>("select count(*) from game.game_event where game_id = @id", new { id = theirs.Id }));
        Assert.Equal(1, await db.ExecuteScalarAsync<int>("select count(*) from management.game where id = @id", new { id = untouched.Id }));
        Assert.Equal(1, await db.ExecuteScalarAsync<int>("select count(*) from management.account where id = @otherId", new { otherId }));
        Assert.Contains(meId.ToString(), _f.Stytch.Deleted);
        Assert.Equal(HttpStatusCode.Unauthorized, (await me.GetAsync("/v1/account")).StatusCode);
    }
}
```

- [ ] **Step 2: Run; expect compile errors. Write `AccountDeleter.cs`:**

```csharp
using Dapper;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Games;
using Tabletop.Management.Persistence;

namespace Tabletop.Management.Identity;

/// <summary>Deletion is total. Cascades handle what hangs below the account; games hang above it and are deleted one by one, log included.</summary>
public sealed class AccountDeleter(Database db, GameRepository games, IBlobStore blobs, StytchClient stytch, ILogger<AccountDeleter> log)
{
    public async Task<bool> Delete(Guid accountId)
    {
        IReadOnlyList<string> keys; IReadOnlyList<Guid> gameIds;
        await using (var c = await db.Open())
        {
            if (!await c.ExecuteScalarAsync<bool>("select exists(select 1 from account where id = @accountId)", new { accountId })) return false;
            keys = (await c.QueryAsync<string>("select s3_key from blob where owner_id = @accountId", new { accountId })).ToList();
            // Games it plays in, plus games it created and has since left: game.created_by has no on-delete action, so a game it created must go too.
            gameIds = (await c.QueryAsync<Guid>(@"select game_id from participant where account_id = @accountId
                                                 union select id from game where created_by = @accountId", new { accountId })).ToList();
        }
        foreach (var gameId in gameIds) await games.Delete(gameId);
        await using (var c = await db.Open())
            await c.ExecuteAsync("delete from account where id = @accountId", new { accountId });
        if (keys.Count > 0) await blobs.Delete(keys);
        try { await stytch.DeleteUser(accountId.ToString()); }
        catch (StytchException e) when (e.Status == 404) { log.LogWarning("Stytch had no user for account {AccountId}", accountId); }
        return true;
    }
}
```

- [ ] **Step 3: Extend `AccountEndpoints.cs`**

```csharp
public sealed record RenameRequest(string Name);

// inside MapAccount, after the GET:
group.MapPatch("", async (ClaimsPrincipal user, RenameRequest r, AccountRepository accounts) =>
{
    if (string.IsNullOrWhiteSpace(r.Name)) return Problems.Unprocessable("name is required");
    var id = CurrentAccount.Id(user);
    return await accounts.Rename(id, r.Name.Trim()) ? Results.Ok(await accounts.Get(id)) : Results.Unauthorized();
});
group.MapDelete("", async (ClaimsPrincipal user, AccountDeleter deleter) =>
{
    try { return await deleter.Delete(CurrentAccount.Id(user)) ? Results.NoContent() : Results.Unauthorized(); }
    catch (StytchException e) { return Sessions.Problem(e); }
});
```

- [ ] **Step 4: Wire `Program.cs`** (`AddSingleton<AccountDeleter>()`); run `dotnet test`; expect 58 passed. Commit: `Rename the account, and delete it with everything it owns and every game it played in`.

### Task 14: Run script, CLAUDE.md, and the whole-tree check

**Files:**
- Create: `scripts/management` (shell)
- Modify: `CLAUDE.md`

**Interfaces:**
- Produces: `./scripts/management` runs the API from WSL on `http://0.0.0.0:5100` reading `.env`; `CLAUDE.md` names the project, its tests and the secret handling.

- [ ] **Step 1: Write `scripts/management`** and `chmod +x` it

```bash
#!/usr/bin/env bash
# Run the management API from WSL. Reads .env (docker compose's file) for Postgres, MinIO and Stytch settings;
# the Stytch secret comes from `dotnet user-secrets` (server/management) or TABLETOP_STYTCH_SECRET.
# Development is what maps POST /v1/dev/sessions, which the simulator's socket driver calls for its tokens.
set -euo pipefail
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"
cd "$(dirname "$0")/.."
exec dotnet run --project server/management/Tabletop.Management.csproj "$@"
```

- [ ] **Step 2: Edit `CLAUDE.md`**

In the opening paragraph, replace "the servers and web app do not yet." with "the management API exists under `server/management/`; the game server and web app do not yet."

In **Commands**, replace the `dotnet test` line the contracts plan wrote, `dotnet test                  # the three libs' xUnit projects under tests/`, with `dotnet test                  # every xUnit project under tests/; the server tests need docker compose up -d`, and add after the `docker compose up -d` line:

```bash
./scripts/management         # the management API on http://localhost:5100 (needs docker compose up and the Stytch secret)
cd server/management && dotnet user-secrets set STYTCH_SECRET <secret>   # once per machine; never in a file
```

In **Layout**, replace the `server/management/` bullet with: "`server/management/` — `Tabletop.Management`, ASP.NET Core minimal APIs: `Identity/` (Stytch login, JWT verification, the account), `Content/` (assets over MinIO, folders, armies from Army Forge, layouts), `Games/`, `Http/` (version check, keyset paging), `Persistence/` (Npgsql + Dapper, DbUp migrations in `Persistence/Migrations/`). Tests in `tests/Tabletop.Management.Tests` run against the Docker Postgres and MinIO, one throwaway database per test class." Replace "`server/` and `web/` are empty on purpose." with "`server/game/` and `web/` are empty on purpose." Replace the `tests/` bullet the contracts plan wrote with:

```
- `tests/` — one xUnit project per library and per server: `Tabletop.Geometry.Tests`, `Tabletop.Content.Tests`,
  `Tabletop.Protocol.Tests`, `Tabletop.Management.Tests`. `dotnet test` from the root; the server tests need
  `docker compose up -d`.
```

In the closing note under the list, replace "`server/` and `web/` are empty on purpose." (the contracts plan's wording) with "`server/game/` and `web/` are empty on purpose." — the game server skeleton plan's Task 13 quotes this narrowed sentence as its *Before*, so it must exist by then (added 2026-09-09; the chain had a gap here).

In **Conventions**, in the "Tests:" bullet replace "one project per library;" with "one project per library and per server;", and add: "Schema changes are new `NNN_*.sql` files under `server/management/Persistence/Migrations/`; `001_schema.sql` there is a copy of `scripts/postgres/init/001_schema.sql` and neither is edited after the fact. The game server writes a game's rows only while it is in `lobby` or `setup` — see the plan's Global Constraints for the exact columns."

- [ ] **Step 3: Run the whole check**

```bash
docker compose up -d && dotnet build && dotnet test
./scripts/management &   # then, from Windows PowerShell: Invoke-WebRequest http://localhost:5100/healthz
git status --short
find server tests -path '*/obj/*' -maxdepth 4 -type d | grep -v Unix
```

Expected: build clean; every test project green (Geometry, Content, Protocol, Management — 53 in Management); `/healthz` reachable from Windows over forwarded localhost; no `obj/` outside `obj/Unix/`; `git status` shows only the intended files and never `.env`.

- [ ] **Step 4: Commit**

```bash
git add scripts/management CLAUDE.md
git commit -m "Record the management API in CLAUDE.md and add its run script"
```

---
## Interfaces → Produces, whole project

Namespace `Tabletop.Management` unless stated. Later plans (game server, web app, client shell) copy these verbatim.

### HTTP surface

| Method and path | Body → response | Auth |
| --- | --- | --- |
| `GET /healthz` | → `{status}` | none |
| `POST /v1/auth/otp` | `EmailRequest` → `OtpSentResponse(MethodId)` | none |
| `POST /v1/auth/otp/verify` | `OtpVerifyRequest(MethodId, Code)` → `SessionResponse(SessionJwt, Account)` | none |
| `POST /v1/auth/password` | `PasswordRequest(Email, Password)` → `SessionResponse` | none |
| `POST /v1/auth/password/signup` | `PasswordRequest` → `201 SessionResponse` | none |
| `POST /v1/auth/password/reset` | `EmailRequest` → `202` | none |
| `POST /v1/auth/password/reset/complete` | `ResetCompleteRequest(Token, Password)` → `SessionResponse` | none |
| `POST /v1/auth/session/refresh` | `RefreshRequest(SessionJwt)` → `SessionResponse` | none (the JWT may be expired) |
| `DELETE /v1/auth/session` | → `204` | bearer |
| `GET /v1/account`, `PATCH /v1/account` (`RenameRequest`), `DELETE /v1/account` | `AccountRow(Id, Name)` / `204` | bearer |
| `GET /v1/assets?limit&after&kind` | → `Page<AssetDto>` | owner |
| `POST /v1/assets` | `CreateAssetRequest` → `201 CreateAssetResponse(Asset, UploadUrl?)` | owner |
| `GET/PATCH/DELETE /v1/assets/{id}` | `PatchAssetRequest` → `AssetDto` / `204` | owner |
| `POST /v1/assets/{id}/confirm` | → `ConfirmResponse(Asset, Triangles?, Warning?)` | owner |
| `GET /v1/assets/{id}/download` | → `DownloadResponse(Url, ExpiresAt)` | owner |
| `GET/PUT /v1/assets/{id}/volumes` | `Volume[]` → `TerrainVolume[]` | owner |
| `GET /v1/folders`, `POST` | `FolderRequest(Name, ParentId)` → `FolderDto(Id, ParentId, Name, AssetIds)` (`GET` the whole tree as a list; `POST` → `201`) | owner |
| `PATCH /v1/folders/{id}`, `DELETE /{id}`, `PUT/DELETE /{id}/assets/{assetId}` | `FolderRequest` for `PATCH`; all four → `204`, no body | owner |
| `POST /v1/armies/import` | `ImportRequest(Source)` → `201 Tabletop.Content.Army` | owner |
| `GET /v1/armies?limit&after` | → `Page<ArmySummary>` | owner |
| `GET/PATCH/DELETE /v1/armies/{id}` | `PatchArmyRequest(Name?, Units?, Slots?)` → `Army` / `204` | owner |
| `GET /v1/layouts`, `POST` (`TableState` with `Name`), `GET/PATCH/DELETE /{id}` | `PatchLayoutRequest(Name?, State?)` → `LayoutSummary[]` / `TableState` / `204` | owner |
| `GET /v1/properties` | → `PropertiesSummary(ContentHash, Name, GameRules)[]` | bearer |
| `GET /v1/games?limit&after` | → `Page<GameSummary>` | bearer (public) |
| `POST /v1/games` | `CreateGameRequest(Name, PropertiesHash, SchedulerShape, Enforcement, WaitsBetweenSteps)` → `201 Tabletop.Protocol.GameInfo` (`RngSeed` is generated server-side, not requested) | bearer |
| `GET /v1/games/{id}` | → `GameInfo` | bearer (public) |
| `POST /v1/games/{id}/join` | → `GameInfo` | bearer |
| `POST /v1/games/{id}/leave` | → `204` | participant |
| `POST /v1/games/{id}/end`, `DELETE /v1/games/{id}` | → `204` | creator |
| `GET /v1/games/{id}/assets` | → `Manifest(Assets: ManifestEntry(AssetId, Kind, Hash, Url, Scale, Orientation)[], ExpiresAt)` | participant |
| **`POST /v1/dev/sessions`** — **development only, not a product endpoint** | `DevSessionRequest(Name)` → `SessionResponse` | none, and the route exists only when `IHostEnvironment.IsDevelopment()` |

The last row is the simulator's token source and nothing else calls it. It is mapped inside `if (app.Environment.IsDevelopment())`, so a build whose environment is unset — the default, `Production` — has no such route and answers `404`. No client, no web app and no product feature may use it.

Conventions: JSON per `ContentJson.Configure` (camelCase, snake_case enums, nulls omitted); errors are `application/problem+json`; `Page<T>(Items, Next)` with `?limit=` (default 50, max 200) and `?after=<uuid>`; `Tabletop-Client-Version` below `MinimumClientVersion` → `426` (a missing header is accepted); another owner's row → `404`; CORS for the origin `TABLETOP_WEB_URL` with `Authorization`, `Content-Type` and `Tabletop-Client-Version` allowed and the five methods above (Task 3).

### Types and services

| Type | Members |
| --- | --- |
| `Configuration.ManagementOptions` | `DbConnection, S3Endpoint, S3Bucket, S3AccessKey, S3SecretKey, StytchProjectId, StytchSecret, StytchBaseUrl, WebUrl, MinimumClientVersion, SessionMinutes`; `static From(IConfiguration)` |
| `Configuration.DotEnv` | `static Load()` |
| `Http.ClientVersion` / `ClientVersionMiddleware` | `Decide(string?, int) : ClientVersionVerdict` |
| `Http.Keyset`, `Http.Page<T>`, `Http.Problems`, `Http.CurrentAccount` | `Keyset.Parse(int?, Guid?)`; `Page<T>.Of(items, limit, id)`; `NotFound/Conflict/Unprocessable/BadGateway`; `CurrentAccount.Id(ClaimsPrincipal)` |
| `Persistence.Database`, `Persistence.Migrator` | `Open()`, `Source`; `Migrator.Run(connectionString)` |
| `Identity.StytchJwks`, `Identity.StytchAuthentication` | `Resolve(kid)`; `AddStytchJwt`, `AccountClaim = "account_id"`, `SessionClaim`, `Issuer(projectId)`, `JwksClient` |
| `Identity.StytchClient` | the fourteen calls listed in Task 5, four of them reached only by the development mint; `HttpClientName = "stytch"`; `StytchException(Status, ErrorType)` |
| `Identity.Sessions` | `Complete(StytchSession) : SessionResponse`; `static Problem(StytchException)` |
| `Identity.DevSessions`, `Identity.DevSessionEndpoint` | `Mint(name) : SessionResponse`, `static AccountIdFor(name) : Guid`, `static EmailFor(name) : string`; `MapDevSession` — registered only under a development environment |
| `Identity.AccountRepository`, `Identity.AccountDeleter` | `Get/Insert/Rename`; `Delete(accountId)` |
| `Content.VolumeRow` | `ToVolume()`, `From(id, Volume)`, `Columns/InsertColumns/InsertValues` |
| `Content.Assets.IBlobStore`, `S3BlobStore` | `Key(owner, hash)`, `PresignUpload`, `PresignDownload`, `Size`, `ReadRange`, `Delete` |
| `Content.Assets.GlbHeader` | `IsGlb`, `JsonChunkLength`, `CountTriangles`, `HeaderBytes = 20`, `WarnAbove = 100_000` |
| `Content.Assets.AssetRepository`, `FolderRepository` | per Tasks 7 and 8 |
| `Content.Armies.ArmyForgeClient`, `ArmyForgeImport`, `CatalogKinds`, `ArmyRepository` | `ListId`, `Parse`, `Fetch`; `ToArmy(list, source, catalog)`, `ParseBase`, `GameRules`, `DefaultBody`, `HeightScale`, `HeightFromBase`; `For(gameRules)`; `Insert/Get/List/Patch/Delete` |
| `Content.Layouts.LayoutRepository` | `Save/Get/List/Patch/Delete`, `static ReadTree` |
| `Games.GameRepository` | `ListProperties/Create/List/Get/IsParticipant/IsCreator/Join/Leave/End/Delete/ManifestAssets` |

### Migrations

`001_schema.sql` (the contracts plan's schema, baselined when the compose init already applied it) and `002_blob_uploaded.sql` (`blob.uploaded boolean`, `blob.content_type text`). Journal: `public.schema_version`.

### Rows by writer

| Table / column | Management | Game server (`lobby`/`setup` only) |
| --- | --- | --- |
| `account`, `blob`, `asset`, `asset_volume`, `folder`, `asset_folder`, the ten army tables, `table_state` with `owner_id` and its children, `properties` and the seven tables under it (read here; loaded by the content import) | writes | reads |
| `game` insert; `game.name/game_rules/properties_hash/scheduler_shape/enforcement/waits_between_steps/created_by/created_at` | writes on create | rewrites configuration on `set_configuration` |
| `game.rng_seed` | writes once on create (`Random.Shared.NextInt64()`); no endpoint updates it | never — reads it at hydration into `GameInfo.RngSeed` |
| `game.status` | `'lobby'` on create; `'ended'` on `/end` and on the last leave | `lobby → setup → playing` |
| `team` | never | inserts (`create_team`), while `lobby` or `setup` only |
| `participant` insert / delete — **joining the game**, which is a row with no team | create, join / leave | never |
| `participant.team_id` — **taking a team**, which is not joining | never | writes (`pick_team`), while `lobby` or `setup` only |
| `participant.army_id`, `participant.army_snapshot` | never (null on insert) | writes |
| `table_state` with `game_id` and its `terrain_placement`, `terrain_volume`, `marker`, `deployment_zone` | never | writes (the draft), then frozen |
| `game.game_event` | deletes with the game | appends |

---

## Spec corrections proposed

**Applied on 2026-09-08.** These were written into the documents they name, across five passes split
by target file so that no two passes wrote the same file. The list below is kept as the historical
record of what each correction asked for and why, so a *Before* quote here no longer matches the
document it quotes. `docs/handoff.md` → *The spec corrections are applied* records the exceptions:
the corrections that were already applied, the ones folded into a later correction, and the ones
skipped as superseded.

1. **`docs/schema.md` → `BLOB` (diagram) and the "Identifiers" section's note on `blob`.** Add two columns and a sentence: `uploaded boolean not null default false`, `content_type text not null check (content_type in ('model/gltf-binary', 'image/png', 'image/jpeg', 'image/webp'))`. Wording to add after *"`blob`'s primary key is `(owner_id, hash)` rather than a surrogate id …"*: *"`uploaded` is false between `POST /v1/assets` and `/confirm`; a download or a game manifest skips a blob that is not uploaded. `content_type` records what the bytes are — GLB for miniatures and terrain, PNG or JPEG for textures — so the presigned PUT and the confirm check know what to expect."*
2. **`docs/superpowers/specs/2026-08-29-management-api-design.md` → Authentication.** Current list ends at `DELETE /v1/auth/session log out`. Replace the block with:
   ```
   POST   /v1/auth/otp                      send a code to an email address → method id
   POST   /v1/auth/otp/verify               method id + code → session
   POST   /v1/auth/password                 email and password → session
   POST   /v1/auth/password/signup          email and password → new user, session
   POST   /v1/auth/password/reset           email → Stytch sends the reset link to the web app
   POST   /v1/auth/password/reset/complete  token + password → session
   POST   /v1/auth/session/refresh          an expiring or expired session JWT → a fresh one
   DELETE /v1/auth/session                  log out
   ```
   and add after it: *"Every session-returning call ends the same way: the account row is created or found by `external_id`. The JWT carries our id because the project's Stytch custom claim template maps `{{ user.external_id }}` to the claim `account_id` and Stytch evaluates it on every mint — Stytch's own JWT names the user by its `sub` and does not carry `external_id`. Management sends `session_custom_claims` nowhere. On the one login where `external_id` is first written the session was minted a moment too early, so that login re-mints the JWT once; no other login and no refresh does. Refresh exists because the JWT lives five minutes and only management holds the project secret."*
3. **`docs/superpowers/specs/2026-08-29-management-api-design.md` → "Deliberately not decided".** Strike *"Whether `PATCH` on an army takes a whole document or a patch."* with: *"Settled 2026-09-05 by the management API plan: a patch — a name, unit-default bindings (`UnitPatch`), and per-slot binding, base and height (`SlotPatch`)."* Strike *"Error shapes and status code conventions."* with: *"RFC 7807 `application/problem+json`; another owner's row is `404`."*
4. **`docs/superpowers/specs/2026-08-29-management-api-design.md` → Army import.** Add after *"a change to Army Forge's format is a server deploy rather than a client release"*: *"The source is a share link (`https://army-forge.onepagerules.com/share?id=…`) or a bare list id; management fetches `GET /api/tts?id={id}`. Grimdark Future (`gameSystem: "gf"`) and Age of Fantasy (`"aof"`) both import and both carry `game_system = "grimdark-future"`, because one rules implementation serves both and only the content differs; the skirmish and regiment variants are refused. Two `combined` entries are one unit, and an entry whose `joinToUnit` names another becomes models of that unit, carrying its own quality, defence and rules on its own slots; a `joinToUnit` that names nothing, or names an entry which itself joins, imports as its own unit. Weapons with a count below the unit's size are dealt onto successive models from an advancing cursor; a rule's level comes from the `catalog_entry` rows for the game rules (model-level rules go onto every slot), and a name no catalog knows is a unit rule."*
5. **`docs/superpowers/specs/2026-08-28-army-design.md` → Base shape and height** (*"defaulting to whatever the ruleset says"*). Add: *"Army Forge publishes a recommended round base per unit (`bases.round`, e.g. `32` or `60x35` mm), which import uses as the default; a unit without one gets a 32 mm circle. No ruleset supplies a height and Army Forge publishes none, so import scales one off the base — `1.5 ×` the base's longest dimension, giving 1.5" for a 25.4 mm circle, 1.89" for a 32 mm circle and 3.54" for a 60×35 mm oval — and the user corrects it per slot (ruled 2026-09-06)."*
6. **`docs/superpowers/specs/2026-08-29-accounts-design.md` → Ending is a flag.** Add: *"`/end` and `DELETE /v1/games/{id}` are the creator's alone, and stay the creator's after they leave the game; anyone else sees `404`. An account deletion destroys every game the account created, so a game whose creator is gone is gone with them."*
7. **`docs/superpowers/specs/2026-08-29-management-api-design.md` → The version check.** Add: *"A request with no version header is accepted — the web app is deployed with the API and does not send one; the Godot client always does."*

## Interfaces needed from other modules

- **Game-server plan** — must match, not consume: verify the session JWT exactly as Task 4 does (JWKS `GET {StytchBaseUrl}/v1/sessions/jwks/{project_id}`, no auth; `iss = stytch.com/{project_id}`; `aud = project_id`; RS256; 30 s clock skew; the account id is the top-level claim `account_id`, a GUID string — `StytchAuthentication.AccountClaim`), reading `TABLETOP_STYTCH_PROJECT_ID` and `TABLETOP_STYTCH_BASE_URL` from the same `.env`; it verifies at connect only — a socket outlives the five-minute JWT and is never re-verified, and a reconnect presents whatever JWT the client then holds; write only the rows in *Rows by writer* above and set `participant.army_id` on `pick_army` (the manifest reads it before the snapshot exists); read `game.rng_seed` into `GameInfo.RngSeed` at hydration and never write it — this project generates it once in `GameRepository.Create` and the column has no default, so it is a value the game server can rely on being there; on `replace_draft` from a loaded layout, regenerate every child id, because `terrain_placement`, `terrain_volume`, `marker` and `deployment_zone` ids are global primary keys and the layout's rows keep theirs; write `army_snapshot` as `ContentJson.Serialize(ArmySnapshot)` (the manifest deserialises it with `ContentJson`); seed the OPR `stat_definition` names `Quality` and `Defense` (unit, and at model level too — the interpreter bullet below says why) and `Range` and `Attacks` (weapon), which import writes as stat keys; load at least one `properties` row, without which `POST /v1/games` answers `422` and no game can be created.
- **Interpreter plan** — **no Age of Fantasy content set exists.** `aof` lists import as of the 2026-09-06 ruling, and a game pins a content set by `properties.content_hash`, so a player can import an Age of Fantasy army and then have nothing to play it with: the only authored document is `grimdark-future-core.properties.json`, whose catalog is Grimdark Future's army books and spells, and that plan owns both the documents and `PropertiesSeeder`. Age of Fantasy's units, rules and spells differ even though its mechanics do not, so the set is content to author, not code to write, and nothing in this plan can stand in for it. This project does not detect the mismatch either: `army.game_system` and `properties.game_rules` are both `grimdark-future` for an Age of Fantasy army, which is exactly what makes one rules implementation serve both, so `pick_army` sees no conflict. Recorded, not solved.
- **Interpreter plan** — two consequences of the joined-hero ruling for the OPR properties document (`grimdark-future-core.properties.json`, its Task 3) and the readers over it. First, `Quality` and `Defense` are declared there at `unit` level only, and a joined hero's slots now carry both as slot stats, so the document should declare the same two names at `model` level as well: `stat_definition` is where a stat's kind at a level is declared, and `slot_stat` has no foreign key to it, so nothing catches the gap. A slot's `Quality` is read from 2026-09-06, when the owner ruled that a joined hero attacks at its own Quality and blocks on the unit's Defense until every other model is dead; the interpreter plan's `GfRolls.HitQuality`, `GfAttack.Attacks` and `GfRolls.DefensePlan` implement it and its question 13 records the mechanism. The declaration is what keeps a stored stat a declared one either way. Second, every rule a joined hero carries lands on its slot whatever its catalog kind, and a `unit`-kind entry sitting on a slot is read two ways: `LiveProperties.Gather` treats it as live for the whole unit (its aggregation switch falls to `_ => true`), while `Lookup.UnitHas` answers false (`_ => false`, the case its `TestArmies` doc-comment warns about). `Aircraft`, `Ambush` and `Scout` are the only `unit`-kind entries in the OPR core set, so a joined hero with `Ambush` is invisible to the scheduler's deployment set. Import already produced that shape for a rule with `count < size`; a hero merge makes it ordinary.
- **Web-app plan** — consumes the HTTP surface above; hosts `/reset-password` at `TABLETOP_WEB_URL` and reads `?token=` from Stytch's redirect, then calls `POST /v1/auth/password/reset/complete`; computes the SHA-256 before `POST /v1/assets`; PUTs the bytes to `UploadUrl` with the same `Content-Type` it declared; calls `/confirm` and shows `Warning`; writes the bounding-box volume with `PUT /v1/assets/{id}/volumes`. Gets CORS for `TABLETOP_WEB_URL` from Task 3 (its request). Does not get a way to rename units or model slots: the army spec gives the user "everything about appearance and physical presence" and the list "composition and loadout"; names are the list's, so `UnitPatch` and `SlotPatch` stay without `Name?` and the editor shows them read-only (compatibility review ruling, 2026-09-05; a one-column change in `ArmyRepository.Patch` if the owner wants it).
- **Client-shell plan** — consumes `/v1/auth/*`, `/v1/account`, `/v1/games*` including the manifest `GET /v1/games/{id}/assets` (whose `ManifestEntry.Scale/Orientation` it applies to another participant's mesh), `/v1/properties`, `/v1/armies` (list and get only), `/v1/layouts` (list, get, `POST` to save), and three owner-only asset routes for the setup toolbar: `GET /v1/assets?kind=terrain|texture&limit=200`, `GET /v1/assets/{id}/volumes`, `GET /v1/assets/{id}/download`; sends `Tabletop-Client-Version: {ProtocolVersion.Current}` on every request; refreshes with `POST /v1/auth/session/refresh` before the five-minute JWT lapses (once a minute, per the management API's working figures). Its `ManagementDtos.cs` mirrors `AccountRow`, `SessionResponse`, `OtpSentResponse`, `Page<T>`, `GameSummary`, `CreateGameRequest`, `PropertiesSummary`, `ArmySummary`, `LayoutSummary`, `AssetKind`, `AssetDto`, `DownloadResponse`, `ManifestEntry`, `Manifest` member-for-member; a change to any of these here is a change there.
- **Simulation plan** — consumes `POST /v1/dev/sessions` for `--driver socket`. `SocketCredentials` calls it once per team plus once for the recorder, and `SocketRowSeeder` inserts `participant` rows for the account ids it comes back with. It therefore needs the management API running under `ASPNETCORE_ENVIRONMENT=Development` with the Stytch secret set, which `./scripts/management` does, and it needs network access to the Stytch test project, which the in-process driver does not.
- **Stytch dashboard** (project `TabletopWar`, test) — two pieces of project state this code depends on and does not create, plus a note on managing the second:
  - `TABLETOP_WEB_URL/reset-password` must be a registered redirect URL for password resets; the two redirect URLs registered on 08-24 for OAuth and magic links can be removed.
  - The **session custom claim template** must be set to exactly `{"account_id": "{{ user.external_id }}"}` (dashboard → Custom Claim Templates, the `SESSION` template, for the test environment and again for live). `{{ user.external_id }}` is one of the supported variables, and the rendered object "will be used as the initial set of custom claims for all of that user's sessions", so Stytch stamps the claim on every mint with no call from us. The JSON key is ours to choose and must stay `account_id`, because both `StytchAuthentication.AccountClaim` and the game server's `StytchJwtVerifier.AccountClaim` read that name. **If it is missing or misspelled nothing errors:** every JWT mints without `account_id`, `CurrentAccount.Id` throws `UnauthorizedAccessException` on every management request, and the game server rejects every WebSocket connection — a total, silent outage with no Stytch error anywhere. Task 5's `AuthTests` catch a code regression but not a missing template, so the manual check in Task 5 Step 7 (read the JWT at jwt.io) is what proves the template exists. Limits, from the same page: total claims under 4 kB, no registered claim names (`iss`, `sub`, `aud`, `exp`, `nbf`, `iat`, `jti`, or anything under `https://stytch.com/*` — `account_id` is none of these), and template changes reach existing sessions only the next time a JWT is minted.
  - The template is also readable and writable over the Programmatic Workspace Actions API, verified 09-06 against Stytch's OpenAPI: `GET` and `PUT https://management.stytch.com/pwa/v3/projects/{project_slug}/environments/{environment_slug}/jwt_templates/SESSION`, Basic auth with a *workspace management* key (not the project secret), `PUT` body `{"template_content": …, "custom_audience": …}` with **both fields required** — send `custom_audience` back exactly as `GET` returned it, because a non-empty value replaces `aud` and both verifiers require `aud = project_id`. Nothing in this plan calls it; it is worth knowing only as a way to read the template back in CI, or to set it on a live project without the dashboard.

## Questions for the project owner

~~1. **Is a request with no `Tabletop-Client-Version` header accepted?**~~ Struck 09-06: the specs answer it. The management API's version check exists *"because the Godot client is a downloaded desktop application and a user can be months behind"* (management API → `/v1` in the path), and the client shell is the document that says *"the client sends its version on every request"* — the web app is deployed with the API and is not the case the mechanism was built for. A missing header is accepted, which is what `ClientVersion.Decide` does; spec correction 7 records it in the spec.

~~1. **Should the account id go into the JWT by a per-login `session_custom_claims` or by a Stytch dashboard "custom claim template"?**~~ **Ruled 09-06: the template**, `{"account_id": "{{ user.external_id }}"}`. The plan's reason for the code path — visible, testable, no dashboard state — did not survive the cost the whole-design review had already measured: an extra Stytch call on every login *and* on every refresh, and refresh runs once a minute per client. Stytch's custom-claim-templates page, re-read 09-06, lists `{{ user.external_id }}` among the supported variables and says the rendered object "will be used as the initial set of custom claims for all of that user's sessions", so the claim costs nothing at run time. Consequences, all now written into Tasks 4 and 5: `StytchClient.RefreshSession` loses its `customClaims` parameter and `Sessions.Complete` makes no second call on a returning login or a refresh; `external_id = account.id` stops being incidental and becomes the mechanism; the template is recorded under *Interfaces needed from other modules → Stytch dashboard*, with what its absence breaks; and the one case the template cannot cover on its own — the first login, where the session is minted before `external_id` is written — is handled by re-minting the JWT once on that login only, which `AuthTests` asserts happens exactly once there and not at all afterwards.
~~2. **Do joined heroes (`joinToUnit` on a non-combined entry) import as their own unit?**~~ **Ruled 2026-09-06: the hero is a model of the unit it joined.** The assumption had been that the join was lost, because no army table records it and content values dropped `list_entry`. Nothing had to be added to hold the hero: `ModelSlot` already carries `Stats` and `Properties`, and `slot_stat` and `slot_property` are already written and read by `ArmyRepository`, so the hero's quality and defence go on its own slot and its rules go there with them. Consequences, all in Task 9: `Merge` returns a host plus the entries joining it, one `Section` per entry appends that entry's slots with its own base, its own cursor and its own stats, the unit's stats and properties stay the host's, and a `joinToUnit` that resolves to nothing or to another joining entry keeps the old behaviour of a unit of its own. Two facts follow that no code enforces: `Quality` and `Defense` are declared at unit level in the OPR properties document and are now stored at model level too, and a joined hero's `unit`-kind rules sit where `Lookup.UnitHas` cannot see them — both under *Interfaces needed from other modules*.
~~3. **Default base and height when Army Forge gives none.**~~ **Ruled 2026-09-06: the height is scaled from the base.** The base default stands at `Base.Circle(32/25.4)` for an entry with no `bases.round`; the height is `HeightFromBase(base)`, `1.5 ×` the base's long axis, replacing the flat 1.5" every slot used to get. A 60 mm cavalry oval therefore starts at 3.54" and a 120 mm oval at 7.09", where both used to start the height of an infantryman. It stays a guess and is meant to read as one: nothing in either ruleset publishes a height, Army Forge publishes none, and the base is the only physical dimension the export gives, so the scale is arbitrary and the player corrects it per slot through `SlotPatch.Height`. A per-`GameRules` table of heights would be the alternative and no such table exists.
4. **How are partial weapon counts dealt onto models? Ruled 2026-09-09 by the owner, as assumed: an advancing cursor** ([closing the open questions](../specs/2026-09-09-open-questions-design.md)). Import stays deterministic and expands a unit on its own; the per-slot weapon editor and `PATCH` shape that asking the player would need are not written, and the player corrects a slot afterwards. Assumed an advancing cursor (9× Rifle, 1× Flamer → the flamer on the last model). If the user should be asked, import cannot expand deterministically, and the web app needs a per-slot weapon editor plus a `PATCH` shape for weapons — neither designed.
~~5. **Which game systems import?**~~ **Ruled 2026-09-06: Grimdark Future and Age of Fantasy both import**, in the owner's words *"the mechanics are the same. Both future and fantasy should supported."* `GameRules` maps `gf` and `aof` to the one `grimdark-future` rules implementation, which is what the project already records: the scheduler and interpreter plans build one OPR engine, and `architecture.md` → *Rules as data* and the geometry values spec both say Age of Fantasy's terrain rules are word for word identical to Grimdark Future's, which is why it is not a second ruleset. Only the content differs, and that is the consequence recorded against the interpreter plan under *Interfaces needed from other modules*: an Age of Fantasy list can be imported before any content set exists to play it with. The skirmish and regiment variants (`gff`, `aofs`, `aofr`) still answer `422`; they are different mechanics, not different content.
~~6. **Who may `/end` and `DELETE` a game?**~~ **Ruled 2026-09-06: the creator alone.** The assumption had been any participant, on the reading that the setup phase leaves a game "unowned". The creator already starts and ends setup, so ending and destroying the game belong to the same account. `GameRepository.IsCreator` replaces `IsParticipant` on `POST /v1/games/{id}/end` and `DELETE /v1/games/{id}`, and anyone else gets `404`, the same answer this project gives for any other row that is not the caller's (Task 3's owner-only rule). The right stays with the creator after they leave, because `game.created_by` has no `on delete` action and outlives the participant row. A game does not become undeletable when its creator's account goes: Task 13 deletes every game the account created as well as every game it played in, which is why the game set there is a union.
7. **Should `GET /v1/games` be newest-first? Ruled 2026-09-09: yes.** The list filters by status, name and created-by-me, which is a filter set for finding something recent, and ascending by id puts every finished game ahead of the one started this evening. The query is `id < @after order by id desc` and `Page<T>.Next` still holds the last id, so the keyset cursor is unchanged. The original assumption, now overturned: oldest-first ascending by id, the keyset order the spec's cursor mechanism gives. If newest-first, the query becomes `id < @after order by id desc` and `Page<T>.Next` still holds the last id.
8. **Should the texture kind accept `image/webp`? Ruled 2026-09-09: yes.** Godot 4 loads WebP natively and the table texture is downloaded by every client in the game, where the saving is real. `AssetRules.ContentTypes[AssetKind.Texture]` and the `002` check constraint carry it, and the web app's intake follows — the `ContentType` union, `classifyFiles`' extension list and its test. Miniature and terrain are unaffected; GLB remains the only mesh format uploaded.
