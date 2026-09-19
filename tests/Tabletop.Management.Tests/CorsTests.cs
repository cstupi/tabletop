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
