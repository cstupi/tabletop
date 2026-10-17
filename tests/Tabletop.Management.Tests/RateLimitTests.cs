using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Tabletop.Management.Identity;

namespace Tabletop.Management.Tests;

/// <summary>Each test builds its own host with the limits it exercises; the shared factory runs with every limit off.</summary>
public class RateLimitTests
{
    /// <summary>TestServer has no peer address; this stands in for the TCP peer, ahead of the app's forwarded-headers step.</summary>
    private sealed class Peer : Microsoft.AspNetCore.Hosting.IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, n) => { ctx.Connection.RemoteIpAddress = IPAddress.Parse(ctx.Request.Headers["X-Test-Peer"].FirstOrDefault() ?? "127.0.0.1"); return n(ctx); });
            next(app);
        };
    }

    static (ManagementFactory Factory, WebApplicationFactory<Program> Host) Host(params (string Key, string Value)[] settings)
    {
        var f = new ManagementFactory { Settings = settings.ToDictionary(s => s.Key, s => (string?)s.Value) };
        return (f, f.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddTransient<Microsoft.AspNetCore.Hosting.IStartupFilter, Peer>())));
    }

    static HttpRequestMessage Otp(string email, string? forwardedFor = null, string? peer = null)
    {
        var r = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/otp") { Content = JsonContent.Create(new { email }) };
        if (forwardedFor is not null) r.Headers.Add("X-Forwarded-For", forwardedFor);
        if (peer is not null) r.Headers.Add("X-Test-Peer", peer);
        return r;
    }

    [Fact]
    public async Task Credential_endpoints_are_limited_per_client_ip_read_from_a_trusted_proxy_only()
    {
        var (f, host) = Host(("RATE_LIMIT_AUTH_PER_IP", "3"));
        using var _ = f; using var c = host.CreateClient();
        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Otp($"a{i}@example.com", "203.0.113.5"))).StatusCode);
        var limited = await c.SendAsync(Otp("a3@example.com", "203.0.113.5"));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Contains("Too many requests", await limited.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Otp("b@example.com", "203.0.113.6"))).StatusCode);                 // another client behind the proxy

        // A peer that is not a known proxy is its own client, whatever it claims in X-Forwarded-For.
        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Otp($"c{i}@example.com", $"192.0.2.{i}", "198.51.100.9"))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await c.SendAsync(Otp("c9@example.com", "192.0.2.99", "198.51.100.9"))).StatusCode);

        // Session refresh is not a credential endpoint: the budget never locks a signed-in client out.
        for (var i = 0; i < 5; i++)
        {
            var refresh = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/session/refresh") { Content = JsonContent.Create(new { sessionJwt = "x" }) };
            refresh.Headers.Add("X-Forwarded-For", "203.0.113.5");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await c.SendAsync(refresh)).StatusCode);
        }
    }

    [Fact]
    public async Task A_trusted_proxy_may_be_named_by_network_as_a_docker_gateway_is()
    {
        var (f, host) = Host(("RATE_LIMIT_AUTH_PER_IP", "1"), ("TRUSTED_PROXIES", "172.31.250.0/24"));
        using var _ = f; using var c = host.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Otp("a@example.com", "203.0.113.5", "172.31.250.1"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Otp("b@example.com", "203.0.113.6", "172.31.250.1"))).StatusCode);   // another client behind it
        Assert.Equal(HttpStatusCode.TooManyRequests, (await c.SendAsync(Otp("c@example.com", "203.0.113.5", "172.31.250.1"))).StatusCode);
    }

    [Fact]
    public async Task Credential_endpoints_are_limited_per_email_across_addresses()
    {
        var (f, host) = Host(("RATE_LIMIT_AUTH_PER_EMAIL", "2"));
        using var _ = f; using var c = host.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Otp("victim@example.com", "203.0.113.1"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/v1/auth/password", new { email = "Victim@Example.com ", password = "guess" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await c.SendAsync(Otp("victim@example.com", "203.0.113.3"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Otp("other@example.com", "203.0.113.3"))).StatusCode);
    }

    [Fact]
    public async Task Every_request_is_limited_per_ip_and_per_account()
    {
        var (f, host) = Host(("RATE_LIMIT_PER_IP", "4"));
        using (f)
        using (var c = host.CreateClient())
        {
            for (var i = 0; i < 4; i++) Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/healthz")).StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, (await c.GetAsync("/healthz")).StatusCode);
        }

        var (g, host2) = Host(("RATE_LIMIT_PER_ACCOUNT", "3"));
        using (g)
        using (var c = host2.CreateClient())
        {
            var (me, other) = (Guid.CreateVersion7(), Guid.CreateVersion7());
            await host2.Services.GetRequiredService<IAccountRepository>().Insert(me, "Me");
            await host2.Services.GetRequiredService<IAccountRepository>().Insert(other, "Other");
            HttpRequestMessage Get(Guid account, string ip)
            {
                var r = new HttpRequestMessage(HttpMethod.Get, "/v1/account");
                r.Headers.Authorization = new("Bearer", TestKeys.Mint(account));
                r.Headers.Add("X-Forwarded-For", ip);
                return r;
            }
            for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Get(me, $"203.0.113.{i}"))).StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, (await c.SendAsync(Get(me, "203.0.113.50"))).StatusCode);   // a new address does not reset an account
            Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Get(other, "203.0.113.50"))).StatusCode);
        }
    }
}
