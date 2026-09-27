# Task 1: Project, solution, configuration and the test host

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
// Registered as a factory (not from builder.Configuration here) because WebApplicationFactory
// applies its ConfigureAppConfiguration overrides during builder.Build(), after this point in the
// file runs.
builder.Services.AddSingleton(sp => ManagementOptions.From(sp.GetRequiredService<IConfiguration>()));

var app = builder.Build();
// Resolved eagerly, right after Build() (once ConfigureAppConfiguration overrides have landed), so
// missing required configuration fails startup instead of being silently deferred to first use.
var options = app.Services.GetRequiredService<ManagementOptions>();
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
TABLETOP_STYTCH_PROJECT_ID=project-test-7a0ad7e7-906a-434a-a3d2-bf90668f9f43
# TABLETOP_STYTCH_SECRET is never written here: `cd server/management && dotnet user-secrets set STYTCH_SECRET <secret>`
```

- [ ] **Step 7: Run `dotnet test tests/Tabletop.Management.Tests`; expect 1 passed (the `.env` copied from `.env.example` supplies the DB and S3 keys). Commit.**

```bash
git add Tabletop.sln .env.example server/management tests/Tabletop.Management.Tests
git commit -m "Add the management API project, its configuration and the test host"
```
