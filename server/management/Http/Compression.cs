using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;

namespace Tabletop.Management.Http;

/// <summary>
/// Brotli and gzip on every JSON answer. `EnableForHttps` is on because behind Caddy the request is https by the time it gets here
/// (`UseForwardedHeaders` reads X-Forwarded-Proto), so off it would compress nothing in production. BREACH needs a secret repeated
/// across responses next to text the attacker chose: the only secrets management answers with are the auth routes' session tokens
/// (`/v1/auth`, `/v1/dev`), which are never compressed; asset answers' presigned URLs are signed per request.
/// </summary>
public static class Compression
{
    static readonly string[] Uncompressed = ["/v1/auth", "/v1/dev"];

    public static IServiceCollection AddJsonCompression(this IServiceCollection services)
    {
        services.AddResponseCompression(o =>
        {
            o.EnableForHttps = true;
            o.Providers.Add<BrotliCompressionProvider>();
            o.Providers.Add<GzipCompressionProvider>();
            o.MimeTypes = [.. ResponseCompressionDefaults.MimeTypes, "application/problem+json"];
        });
        services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Optimal);
        services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Optimal);
        return services;
    }

    /// <summary>Compression for every route but the session-carrying ones, whose Accept-Encoding is dropped before the middleware reads it.</summary>
    public static IApplicationBuilder UseJsonCompression(this IApplicationBuilder app)
    {
        app.Use((ctx, next) =>
        {
            if (Uncompressed.Any(p => ctx.Request.Path.StartsWithSegments(p))) ctx.Request.Headers.AcceptEncoding = default;
            return next(ctx);
        });
        return app.UseResponseCompression();
    }
}
