using System.Security.Claims;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Http;

namespace Tabletop.Management.Content.Shared;

/// <summary>A player's Shared folder, opened by their username (shared-folders spec): any signed-in player browses, previews and copies
/// from it. An unknown username and an account without a Shared folder are the same 404.</summary>
public static class SharedEndpoints
{
    private static readonly TimeSpan DownloadTtl = TimeSpan.FromMinutes(60);

    public static void MapShared(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/shared/{username}").RequireAuthorization();

        g.MapGet("", async (string username, ISharedRepository shared, IBlobStore blobs) =>
        {
            if (Limits.Username(username) is not null || await shared.Browse(username) is not { } view) return Problems.NotFound();
            var assets = new List<SharedAssetDto>(view.Assets.Count);
            foreach (var a in view.Assets) assets.Add(a.ThumbnailKey is null ? a : a with { Thumbnail = await ThumbnailLimits.Url(blobs, a.ThumbnailKey) });
            return Results.Ok(view with { Assets = assets });
        });

        g.MapGet("/assets/{assetId:guid}/download", async (string username, Guid assetId, ISharedRepository shared, IBlobStore blobs) =>
            Limits.Username(username) is null && await shared.BlobKey(username, assetId) is { } key
                ? Results.Ok(new DownloadResponse(await blobs.PresignDownload(key, DownloadTtl), DateTimeOffset.UtcNow.Add(DownloadTtl)))
                : Problems.NotFound());

        g.MapPost("/copy", async (ClaimsPrincipal user, string username, CopySharedRequest r, ISharedRepository shared, IFolderRepository folders, SharedCopyLimits limits) =>
        {
            if ((r.AssetIds is null) == (r.FolderId is null)) return Problems.Unprocessable("Name assetIds or folderId.");
            if (r.AssetIds is not null && Limits.BulkIds(r.AssetIds) is { } bad) return Problems.Unprocessable(bad);
            if (Limits.Username(username) is not null) return Problems.NotFound();
            var caller = CurrentAccount.Id(user);
            SharedCopyResult result;
            try { result = await shared.Copy(caller, username, r.AssetIds, r.FolderId, r.DestinationId, new AssetQuota(UploadLimits.AccountAssets, UploadLimits.AccountBytes), limits); }
            catch (AssetQuotaExceeded e)
            {
                return Problems.Unprocessable(e.Which == AssetQuotaExceeded.Limit.Assets
                    ? $"An account holds at most {UploadLimits.AccountAssets} assets."
                    : $"An account holds at most {UploadLimits.AccountBytes >> 30} GiB of uploads.");
            }
            switch (result.Refusal)
            {
                case SharedCopyRefusal.SharerMissing or SharedCopyRefusal.DestinationMissing or SharedCopyRefusal.FolderMissing: return Problems.NotFound();
                case SharedCopyRefusal.Own: return Problems.Unprocessable("That is your own Shared folder.");
                case SharedCopyRefusal.NothingToCopy: return Problems.Unprocessable("Nothing to copy: the folder holds no uploaded models.");
                case SharedCopyRefusal.TooMany: return Problems.Unprocessable($"A copy takes at most {limits.PerCopy} models. Copy a sub-folder.");
                case SharedCopyRefusal.FolderCap: return Problems.Unprocessable($"An account holds at most {limits.Folders} folders.");
                case SharedCopyRefusal.PendingConflict: return Problems.Conflict("An unfinished upload of the same file is in your library as a different kind. Delete it, then copy again.");
                case SharedCopyRefusal.DailyCap:
                    return Problems.TooManyRequests($"You can copy from Shared folders at most {limits.PerDay} times a day.", Math.Max(1, (int)Math.Ceiling(result.RetryAfter!.Value.TotalSeconds)));
            }
            var mine = (await folders.List(caller)).ToDictionary(f => f.Id);
            return Results.Ok(new CopySharedResponse(result.Assets, result.Folders.Select(id => mine[id]).ToList(), result.NotFound));
        });
    }
}
