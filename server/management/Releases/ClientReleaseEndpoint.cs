using Tabletop.Management.Content.Assets;
using Tabletop.Management.Http;

namespace Tabletop.Management.Releases;

public sealed record ClientDownload(string Platform, string FileName, long Size, string Sha256, Uri Url);

public sealed record ClientRelease(string Version, DateTimeOffset Date, string Notes, int? ProtocolVersion, IReadOnlyList<ClientDownload> Downloads, DateTimeOffset ExpiresAt);

public static class ClientReleaseEndpoint
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

    /// <summary><c>GET /v1/client/latest</c>: the newest client build, one presigned download per known platform.</summary>
    public static void MapClientReleases(this IEndpointRouteBuilder app) =>
        app.MapGet("/v1/client/latest", async (ClientReleases releases, IBlobStore blobs, CancellationToken ct) =>
        {
            var read = await releases.Latest(ct);
            if (read.Unreadable) return Problems.BadGateway("The published release manifest cannot be read.");
            if (read.Manifest is not { } m) return Problems.NotFound();
            var downloads = new List<ClientDownload>();
            foreach (var platform in ClientReleases.Platforms)
            {
                // Only keys under releases/: a manifest never presigns an asset or a staged upload.
                if (m.Platforms?.GetValueOrDefault(platform) is not { } file || !file.Key.StartsWith(ClientReleases.Prefix, StringComparison.Ordinal) || file.Key.Contains("..")) continue;
                downloads.Add(new(platform, file.Key[(file.Key.LastIndexOf('/') + 1)..], file.Size, file.Sha256, await blobs.PresignDownload(file.Key, Ttl)));
            }
            return Results.Ok(new ClientRelease(m.Version, m.Date, m.Notes ?? "", m.ProtocolVersion, downloads, DateTimeOffset.UtcNow.Add(Ttl)));
        }).RequireAuthorization();
}
