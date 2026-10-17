using System.Security.Claims;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Http;

namespace Tabletop.Management.Games;

public static class ManifestEndpoint
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(60);

    public static void MapManifest(this IEndpointRouteBuilder app) =>
        app.MapGet("/v1/games/{id:guid}/assets", async (ClaimsPrincipal u, Guid id, IGameRepository games, IBlobStore blobs) =>
        {
            if (!await games.IsParticipant(id, CurrentAccount.Id(u))) return Problems.NotFound();
            var entries = new List<ManifestEntry>();
            foreach (var r in await games.ManifestAssets(id))
                entries.Add(new ManifestEntry(r.AssetId, AssetRules.Kind(r.Kind), r.Hash, await blobs.PresignDownload(IBlobStore.Key(r.OwnerId, r.Hash), Ttl), r.Scale, new Tabletop.Geometry.Orientation(r.Pitch, r.Yaw, r.Roll)));
            return Results.Ok(new Manifest(entries, DateTimeOffset.UtcNow.Add(Ttl)));
        }).RequireAuthorization();
}
