using System.Security.Claims;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Http;

namespace Tabletop.Management.Content.Offers;

/// <summary>Offering one's assets to a named player, who accepts (a copy into their library) or declines (shared-models spec). Every route
/// is the sharer's or the recipient's: anyone else gets 404.</summary>
public static class OfferEndpoints
{
    private static readonly TimeSpan DownloadTtl = TimeSpan.FromMinutes(60);

    public static void MapOffers(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/offers").RequireAuthorization();

        g.MapGet("/players", async (ClaimsPrincipal user, IOfferRepository offers) => Results.Ok(await offers.Players(CurrentAccount.Id(user))));

        g.MapPost("", async (ClaimsPrincipal user, CreateOffersRequest r, IOfferRepository offers, OfferLimits limits) =>
        {
            if (r.RecipientIds is null || r.RecipientIds.Count == 0) return Problems.Unprocessable("recipientIds is required.");
            if (r.RecipientIds.Count > OfferLimits.Recipients) return Problems.Unprocessable($"An offer names at most {OfferLimits.Recipients} players.");
            if ((r.AssetIds is null) == (r.FolderId is null)) return Problems.Unprocessable("Name assetIds or folderId.");
            if (r.AssetIds is not null && Limits.BulkIds(r.AssetIds) is { } bad) return Problems.Unprocessable(bad);
            var caller = CurrentAccount.Id(user);
            var result = await offers.Create(caller, r.RecipientIds, r.AssetIds, r.FolderId, limits);
            switch (result.Refusal)
            {
                case OfferRefusal.RecipientMissing or OfferRefusal.FolderMissing: return Problems.NotFound();
                case OfferRefusal.NothingToOffer: return Problems.Unprocessable("Nothing to offer: only uploaded models in your library can be offered.");
                case OfferRefusal.DailyCap:
                    return Problems.TooManyRequests($"You can make at most {limits.PerDay} offers a day.", Math.Max(1, (int)Math.Ceiling(result.RetryAfter!.Value.TotalSeconds)));
                case OfferRefusal.RecipientFull: return Problems.Unprocessable($"{result.FullRecipient} has too many open offers.");
            }
            var made = new List<OfferDto>();
            foreach (var id in result.OfferIds) if (await offers.Get(caller, id) is { } o) made.Add(o);
            return Results.Ok(new CreateOffersResponse(made, result.NotFound));
        });

        g.MapGet("/sent", async (ClaimsPrincipal user, IOfferRepository offers, int? limit, Guid? after) =>
        {
            var page = Keyset.Parse(limit, after);
            return Results.Ok(Page<OfferDto>.Of(await offers.Sent(CurrentAccount.Id(user), page), page.Limit, o => o.Id));
        });

        g.MapGet("/received", async (ClaimsPrincipal user, IOfferRepository offers, int? limit, Guid? after) =>
        {
            var page = Keyset.Parse(limit, after);
            return Results.Ok(Page<OfferDto>.Of(await offers.Received(CurrentAccount.Id(user), page), page.Limit, o => o.Id));
        });

        g.MapGet("/received/count", async (ClaimsPrincipal user, IOfferRepository offers) => Results.Ok(new OfferCountDto(await offers.OpenCount(CurrentAccount.Id(user)))));

        g.MapGet("/{id:guid}", async (ClaimsPrincipal user, Guid id, IOfferRepository offers) =>
            await offers.Get(CurrentAccount.Id(user), id) is { } o ? Results.Ok(o) : Problems.NotFound());

        g.MapGet("/{id:guid}/assets/{assetId:guid}/download", async (ClaimsPrincipal user, Guid id, Guid assetId, IOfferRepository offers, IBlobStore blobs) =>
            await offers.BlobKey(CurrentAccount.Id(user), id, assetId) is { } key
                ? Results.Ok(new DownloadResponse(await blobs.PresignDownload(key, DownloadTtl), DateTimeOffset.UtcNow.Add(DownloadTtl)))
                : Problems.NotFound());

        g.MapPost("/{id:guid}/accept", async (ClaimsPrincipal user, Guid id, AcceptOfferRequest r, IOfferRepository offers, IAssetRepository assets) =>
        {
            var caller = CurrentAccount.Id(user);
            AcceptResult result;
            try { result = await offers.Accept(caller, id, r.FolderId, new AssetQuota(UploadLimits.AccountAssets, UploadLimits.AccountBytes)); }
            catch (AssetQuotaExceeded e)
            {
                return Problems.Unprocessable(e.Which == AssetQuotaExceeded.Limit.Assets
                    ? $"An account holds at most {UploadLimits.AccountAssets} assets."
                    : $"An account holds at most {UploadLimits.AccountBytes >> 30} GiB of uploads.");
            }
            if (result.OfferMissing || result.FolderMissing) return Problems.NotFound();
            if (result.PendingConflict) return Problems.Conflict("An unfinished upload of the same file is in your library as a different kind. Delete it, then accept again.");
            var copied = new List<AssetDto>();
            foreach (var a in result.Copied) if (await assets.Get(caller, a) is { } dto) copied.Add(dto);
            return Results.Ok(new AcceptOfferResponse(copied));
        });

        g.MapPost("/{id:guid}/decline", async (ClaimsPrincipal user, Guid id, DeclineOfferRequest r, IOfferRepository offers) =>
            await offers.Decline(CurrentAccount.Id(user), id, r.Block) ? Results.NoContent() : Problems.NotFound());

        g.MapPost("/{id:guid}/withdraw", async (ClaimsPrincipal user, Guid id, IOfferRepository offers) =>
            await offers.Withdraw(CurrentAccount.Id(user), id) switch
            {
                WithdrawResult.Withdrawn => Results.NoContent(),
                WithdrawResult.NotOpen => Problems.Conflict("The offer was already accepted, declined or withdrawn."),
                _ => Problems.NotFound(),
            });

        g.MapGet("/blocked", async (ClaimsPrincipal user, IOfferRepository offers) => Results.Ok(await offers.Blocked(CurrentAccount.Id(user))));

        g.MapDelete("/blocked/{accountId:guid}", async (ClaimsPrincipal user, Guid accountId, IOfferRepository offers) =>
            await offers.Unblock(CurrentAccount.Id(user), accountId) ? Results.NoContent() : Problems.NotFound());
    }
}
