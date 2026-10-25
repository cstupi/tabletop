using System.Security.Claims;
using Tabletop.Management.Http;

namespace Tabletop.Management.Content.Assets;

/// <summary>Other accounts' shared assets: list, preview download, copy into the caller's library (shared-models spec). Sharing one's own
/// is <c>POST /v1/assets/share</c> and <c>/unshare</c>.</summary>
public static class SharedAssetEndpoints
{
    private static readonly TimeSpan DownloadTtl = TimeSpan.FromMinutes(60);

    public static void MapSharedAssets(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/shared-assets").RequireAuthorization();

        g.MapGet("", async (ClaimsPrincipal user, ISharedAssetRepository shared, int? limit, Guid? after, string? kind, string? q) =>
        {
            AssetKind? k = null;
            if (kind is not null)
            {
                if (!Enum.TryParse<AssetKind>(kind, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed)) return Problems.Unprocessable("kind is miniature, terrain or texture.");
                k = parsed;
            }
            q = q?.Trim();
            if (q is { Length: > 0 } && Limits.Text(q, "q", Limits.Name) is { } bad) return Problems.Unprocessable(bad);
            var page = Keyset.Parse(limit, after);
            return Results.Ok(Page<SharedAssetDto>.Of(await shared.List(CurrentAccount.Id(user), page, k, q), page.Limit, a => a.Id));
        });

        g.MapGet("/{id:guid}", async (ClaimsPrincipal user, Guid id, ISharedAssetRepository shared) =>
            await shared.Get(CurrentAccount.Id(user), id) is { } a ? Results.Ok(a) : Problems.NotFound());

        g.MapGet("/{id:guid}/download", async (ClaimsPrincipal user, Guid id, ISharedAssetRepository shared, IBlobStore blobs) =>
            await shared.BlobKey(CurrentAccount.Id(user), id) is { } key
                ? Results.Ok(new DownloadResponse(await blobs.PresignDownload(key, DownloadTtl), DateTimeOffset.UtcNow.Add(DownloadTtl)))
                : Problems.NotFound());

        g.MapPost("/copy", async (ClaimsPrincipal user, CopySharedRequest r, ISharedAssetRepository shared, IAssetRepository assets) =>
        {
            if (Limits.BulkIds(r.AssetIds) is { } bad) return Problems.Unprocessable(bad);
            var caller = CurrentAccount.Id(user);
            CopyResult result;
            try { result = await shared.Copy(caller, r.AssetIds, r.FolderId, new AssetQuota(UploadLimits.AccountAssets, UploadLimits.AccountBytes)); }
            catch (AssetQuotaExceeded e)
            {
                return Problems.Unprocessable(e.Which == AssetQuotaExceeded.Limit.Assets
                    ? $"An account holds at most {UploadLimits.AccountAssets} assets."
                    : $"An account holds at most {UploadLimits.AccountBytes >> 30} GiB of uploads.");
            }
            if (result.FolderMissing) return Problems.NotFound();
            if (result.PendingConflict) return Problems.Conflict("An unfinished upload of the same file is in your library as a different kind. Delete it, then copy again.");
            var copied = new List<AssetDto>();
            foreach (var id in result.Copied) if (await assets.Get(caller, id) is { } a) copied.Add(a);
            return Results.Ok(new CopySharedResponse(copied, result.NotFound));
        });
    }
}
