# Task 3: HTTP plumbing — the version check, keyset paging, problems, JSON

**Files:**
- Create: `server/management/Http/ClientVersionMiddleware.cs`, `server/management/Http/Keyset.cs`, `server/management/Http/Problems.cs`
- Modify: `server/management/Program.cs`
- Create: `tests/Tabletop.Management.Tests/ClientVersionTests.cs`, `tests/Tabletop.Management.Tests/KeysetTests.cs`, `tests/Tabletop.Management.Tests/CorsTests.cs`

**Interfaces:**
- Consumes: `ProtocolVersion.Header`, `ProtocolVersion.UpgradeRequiredStatusCode`, `ProtocolVersion.Current`; `ContentJson.Configure(JsonSerializerOptions)`; `ManagementOptions.WebUrl` (Task 1).
- Produces: `ClientVersion.Decide(string? header, int minimum) : ClientVersionVerdict { Accept, UpgradeRequired, Malformed }`; `ClientVersionMiddleware`; `Keyset.Parse(int? limit, Guid? after) : Keyset(int Limit, Guid? After)` (default 50, max 200); `Page<T>(IReadOnlyList<T> Items, Guid? Next)` with `static Page<T>.Of(IReadOnlyList<T> items, int limit, Func<T, Guid> id)`; `Problems.NotFound()`, `Problems.Conflict(string detail)`, `Problems.Unprocessable(string detail)` returning `IResult`s with `application/problem+json`.
- Every response body is serialised with `ContentJson.Configure` (camelCase, snake_case enums, nulls omitted), so a `TableState` or `GameInfo` on HTTP is byte-identical to the same value on the socket.
- A missing header is accepted: the web app is served from the same repo and tracks the API directly (management API → `/v1` in the path; client shell → Home: "the client sends its version on every request"). The spec states it (management API → The version check).
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
        // ASP.NET Core's CORS middleware answers a preflight with the policy's whole configured header
        // list, not the intersection with Access-Control-Request-Headers (verified against
        // Microsoft.AspNetCore.Cors.Infrastructure.CorsService directly) — a superset is spec-legal, so
        // this checks the two requested headers are allowed and nothing outside the fixed allowlist leaks in.
        var allowedHeaders = response.Headers.GetValues("Access-Control-Allow-Headers").Single().Split(',').Select(s => s.Trim().ToLowerInvariant()).ToHashSet();
        Assert.Superset(new HashSet<string> { "authorization", "content-type" }, allowedHeaders);
        Assert.Subset(new HashSet<string> { "authorization", "content-type", "tabletop-client-version" }, allowedHeaders);
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
    public static IResult TooManyRequests(string detail) => Results.Problem(title: "Too many requests", detail: detail, statusCode: StatusCodes.Status429TooManyRequests);
}
```

- [ ] **Step 4: Wire `Program.cs`** — before `Build()`:

```csharp
builder.Services.ConfigureHttpJsonOptions(o => Tabletop.Content.ContentJson.Configure(o.SerializerOptions));
builder.Services.AddProblemDetails();
builder.Services.AddCors();
```

and after `Build()`, in this order: the exception handler, the policy itself — built here because it reads `options`, which is resolved after `Build()` — then the version middleware:

```csharp
app.UseCors(p => p
    .WithOrigins(options.WebUrl.TrimEnd('/'))
    .WithHeaders("Authorization", "Content-Type", Tabletop.Protocol.ProtocolVersion.Header)
    .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE"));
app.UseMiddleware<Tabletop.Management.Http.ClientVersionMiddleware>();
```

CORS runs before the version check so a preflight (which carries no `Tabletop-Client-Version`) is answered whatever the client's version. `Keyset.Parse` throwing `ArgumentOutOfRangeException` is turned into a 400 by an exception handler added here: `app.UseExceptionHandler(h => h.Run(ctx => Results.Problem(statusCode: ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error is ArgumentOutOfRangeException ? 400 : 500).ExecuteAsync(ctx)));` placed before both.

- [ ] **Step 5: Run `dotnet test tests/Tabletop.Management.Tests`; expect 16 passed. Commit: `Add the version check, keyset paging, problem responses and CORS for the web app`.**
