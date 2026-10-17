using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Tabletop.Management.Identity;

namespace Tabletop.Management.Http;

/// <summary>Per-minute request budgets, from TABLETOP_RATE_LIMIT_* (0 turns one off). Development defaults every limit off so the
/// simulator and the E2E suite never trip them; every other environment defaults to the production values.</summary>
public sealed record RateLimits(int PerIp, int PerAccount, int AuthPerIp, int AuthPerEmail)
{
    public static readonly RateLimits Production = new(PerIp: 600, PerAccount: 600, AuthPerIp: 20, AuthPerEmail: 5);
    public static readonly RateLimits Off = new(0, 0, 0, 0);

    public static RateLimits From(IConfiguration c, IHostEnvironment env)
    {
        var d = env.IsDevelopment() ? Off : Production;
        int Read(string key, int fallback) => int.TryParse(c[key], out var v) && v >= 0 ? v : fallback;
        return new(Read("RATE_LIMIT_PER_IP", d.PerIp), Read("RATE_LIMIT_PER_ACCOUNT", d.PerAccount),
            Read("RATE_LIMIT_AUTH_PER_IP", d.AuthPerIp), Read("RATE_LIMIT_AUTH_PER_EMAIL", d.AuthPerEmail));
    }
}

public static class RateLimiting
{
    public const string AuthPolicy = "auth";

    /// <summary>The proxies whose X-Forwarded-For/-Proto are believed: TABLETOP_TRUSTED_PROXIES, comma-separated addresses or CIDR networks,
    /// default loopback only (Caddy on the same host). A request from any other peer is partitioned by the peer itself.</summary>
    /// <remarks>Everything reads configuration lazily, through options and factories, so WebApplicationFactory overrides (applied
    /// during Build) reach it.</remarks>
    public static IServiceCollection AddProxyAndRateLimits(this IServiceCollection services)
    {
        services.AddSingleton(sp => RateLimits.From(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<IHostEnvironment>()));
        services.AddSingleton<EmailThrottle>();
        services.AddOptions<ForwardedHeadersOptions>().Configure<IConfiguration>((o, config) =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.ForwardLimit = 1;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
            foreach (var p in (config["TRUSTED_PROXIES"] ?? "127.0.0.1,::1").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (p.Contains('/')) o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(p));
                else o.KnownProxies.Add(IPAddress.Parse(p));
        });
        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<RateLimits>((o, limits) =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = (ctx, _) => new ValueTask(Problems.TooManyRequests("Too many requests. Wait a minute and try again.", RetryAfterSeconds(ctx.Lease)).ExecuteAsync(ctx.HttpContext));
            o.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(ctx => Window("ip:" + Ip(ctx), limits.PerIp)),
                PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                    ctx.User.FindFirstValue(StytchAuthentication.AccountClaim) is { } account ? Window("account:" + account, limits.PerAccount) : RateLimitPartition.GetNoLimiter("anonymous")));
            o.AddPolicy(AuthPolicy, ctx => Window("auth:" + Ip(ctx), limits.AuthPerIp));
        });
        return services;
    }

    public static readonly TimeSpan WindowLength = TimeSpan.FromMinutes(1);

    /// <summary>Whole seconds until the partition's window frees: the lease's own RetryAfter when it has one, else the window length.</summary>
    public static int RetryAfterSeconds(RateLimitLease lease) =>
        lease.TryGetMetadata(MetadataName.RetryAfter, out var after) ? Math.Max(1, (int)Math.Ceiling(after.TotalSeconds)) : (int)WindowLength.TotalSeconds;

    public static string Ip(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static RateLimitPartition<string> Window(string key, int perMinute) => perMinute <= 0
        ? RateLimitPartition.GetNoLimiter(key)
        : RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = WindowLength, QueueLimit = 0 });
}

/// <summary>The per-email budget on the credential endpoints, which the IP limiter cannot see: the email is in the body.</summary>
public sealed class EmailThrottle(RateLimits limits) : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter = PartitionedRateLimiter.Create<string, string>(email => limits.AuthPerEmail <= 0
        ? RateLimitPartition.GetNoLimiter(email)
        : RateLimitPartition.GetFixedWindowLimiter(email, _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.AuthPerEmail, Window = RateLimiting.WindowLength, QueueLimit = 0 }));

    /// <summary>Null when the email is within its budget; otherwise the seconds until its window frees.</summary>
    public int? TryAcquire(string? email)
    {
        using var lease = _limiter.AttemptAcquire((email ?? "").Trim().ToLowerInvariant());
        return lease.IsAcquired ? null : RateLimiting.RetryAfterSeconds(lease);
    }

    public void Dispose() => _limiter.Dispose();
}
