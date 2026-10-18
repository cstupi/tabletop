using System.Text.Json;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Http;
using Tabletop.Protocol;

namespace Tabletop.Management.Content.Builtin;

public sealed record ServedBuiltinFile(string Sha256, long Size, Uri Url);

public sealed record ServedBuiltin(string Version, JsonElement? Meshes, JsonElement? Textures, JsonElement? Terrain, IReadOnlyDictionary<string, ServedBuiltinFile> Files, DateTimeOffset ExpiresAt);

public static class BuiltinEndpoint
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

    /// <summary><c>GET /v1/builtin</c>: the published built-in catalog, one presigned download per file.</summary>
    public static void MapBuiltin(this IEndpointRouteBuilder app) =>
        app.MapGet("/v1/builtin", async (BuiltinCatalogs catalogs, IBlobStore blobs, CancellationToken ct) =>
        {
            var read = await catalogs.Latest(ct);
            if (read.Unreadable) return Problems.BadGateway("The published built-in catalog cannot be read.");
            if (read.Catalog is not { } c) return Problems.NotFound();
            var files = new Dictionary<string, ServedBuiltinFile>();
            // Only builtin/{sha256}: a catalog never presigns an asset, a staged upload or a release.
            foreach (var (path, f) in c.Files ?? new Dictionary<string, BuiltinFileRef>())
                if (f.Sha256 is { } sha && BuiltinCatalogs.Sha256().IsMatch(sha))
                    files[path] = new(sha, f.Size, await blobs.PresignDownload(BuiltinCatalogs.Key(sha), Ttl));
            return Results.Ok(new ServedBuiltin(c.Version, c.Meshes, c.Textures, c.Terrain, files, DateTimeOffset.UtcNow.Add(Ttl)));
        }).RequireAuthorization();
}

/// <summary>Names the published built-in catalog's version on every response, so a running client learns of a release
/// from the requests it already makes (builtin content spec → Serving).</summary>
public sealed class BuiltinVersionHeader(RequestDelegate next, BuiltinCatalogs catalogs)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            if (catalogs.KnownVersion() is { } version) context.Response.Headers[ProtocolVersion.BuiltinHeader] = version;
            return Task.CompletedTask;
        });
        return next(context);
    }
}
