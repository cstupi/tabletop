using System.Security.Claims;
using Tabletop.Geometry;
using Tabletop.Management.Http;

namespace Tabletop.Management.Content.Assets;

public static class AssetEndpoints
{
    private static readonly TimeSpan UploadTtl = TimeSpan.FromMinutes(15), DownloadTtl = TimeSpan.FromMinutes(60);

    public static void MapAssets(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/assets").RequireAuthorization();

        g.MapGet("", async (ClaimsPrincipal user, AssetRepository assets, int? limit, Guid? after, AssetKind? kind) =>
        {
            var page = Keyset.Parse(limit, after);
            return Results.Ok(Page<AssetDto>.Of(await assets.List(CurrentAccount.Id(user), page, kind), page.Limit, a => a.Id));
        });

        g.MapPost("", async (ClaimsPrincipal user, CreateAssetRequest r, AssetRepository assets, IBlobStore blobs) =>
        {
            if (string.IsNullOrWhiteSpace(r.Name) || r.ByteSize <= 0 || !AssetRules.IsSha256(r.Hash)) return Problems.Unprocessable("name, byteSize and a 64-hex sha256 hash are required.");
            if (!AssetRules.ContentTypes[r.Kind].Contains(r.ContentType)) return Results.Problem(title: "Unsupported media type", detail: $"{r.Kind} accepts {string.Join(", ", AssetRules.ContentTypes[r.Kind])}. STL is not accepted; convert it first.", statusCode: StatusCodes.Status415UnsupportedMediaType);
            if ((r.Kind == AssetKind.Miniature) != (r.ModelledBase is not null)) return Problems.Unprocessable("A miniature carries a modelledBase; terrain and textures do not.");
            var owner = CurrentAccount.Id(user);
            var asset = await assets.Create(owner, r);
            var url = asset.Uploaded ? null : await blobs.PresignUpload(IBlobStore.Key(owner, r.Hash), r.ContentType, r.ByteSize, UploadTtl);
            return Results.Created($"/v1/assets/{asset.Id}", new CreateAssetResponse(asset, url));
        });

        g.MapGet("/{id:guid}", async (ClaimsPrincipal user, Guid id, AssetRepository assets) =>
            await assets.Get(CurrentAccount.Id(user), id) is { } a ? Results.Ok(a) : Problems.NotFound());

        g.MapPatch("/{id:guid}", async (ClaimsPrincipal user, Guid id, PatchAssetRequest p, AssetRepository assets) =>
            await assets.Patch(CurrentAccount.Id(user), id, p) is { } a ? Results.Ok(a) : Problems.NotFound());

        g.MapDelete("/{id:guid}", async (ClaimsPrincipal user, Guid id, AssetRepository assets, IBlobStore blobs) =>
        {
            var (found, orphaned) = await assets.Delete(CurrentAccount.Id(user), id);
            if (!found) return Problems.NotFound();
            if (orphaned is not null) await blobs.Delete([orphaned]);
            return Results.NoContent();
        });

        g.MapPost("/{id:guid}/confirm", async (ClaimsPrincipal user, Guid id, AssetRepository assets, IBlobStore blobs) =>
        {
            var owner = CurrentAccount.Id(user);
            var asset = await assets.Get(owner, id);
            if (asset is null) return Problems.NotFound();
            var key = IBlobStore.Key(owner, asset.Hash);
            var size = await blobs.Size(key);
            if (size is null) return Problems.Conflict("The bytes have not been uploaded yet.");
            if (size != asset.ByteSize) return Problems.Unprocessable($"Uploaded {size} bytes, expected {asset.ByteSize}.");
            long? triangles = null; string? warning = null;
            if (asset.ContentType == "model/gltf-binary")
            {
                var head = await blobs.ReadRange(key, 0, GlbHeader.HeaderBytes - 1);
                if (!GlbHeader.IsGlb(head)) { await blobs.Delete([key]); return Problems.Unprocessable("Not a GLB. STL and .gltf-with-external-buffers are not accepted."); }
                var len = GlbHeader.JsonChunkLength(head);
                triangles = GlbHeader.CountTriangles(await blobs.ReadRange(key, GlbHeader.HeaderBytes, GlbHeader.HeaderBytes + len - 1));
                if (triangles > GlbHeader.WarnAbove) warning = $"{triangles:N0} triangles is above the 100,000 this table renders comfortably; every participant downloads it.";
            }
            await assets.MarkUploaded(owner, asset.Hash);
            return Results.Ok(new ConfirmResponse((await assets.Get(owner, id))!, triangles, warning));
        });

        g.MapGet("/{id:guid}/download", async (ClaimsPrincipal user, Guid id, AssetRepository assets, IBlobStore blobs) =>
        {
            var owner = CurrentAccount.Id(user);
            var asset = await assets.Get(owner, id);
            if (asset is null) return Problems.NotFound();
            if (!asset.Uploaded) return Problems.Conflict("Not confirmed yet.");
            return Results.Ok(new DownloadResponse(await blobs.PresignDownload(IBlobStore.Key(owner, asset.Hash), DownloadTtl), DateTimeOffset.UtcNow.Add(DownloadTtl)));
        });

        g.MapGet("/{id:guid}/volumes", async (ClaimsPrincipal user, Guid id, AssetRepository assets) =>
            await assets.Get(CurrentAccount.Id(user), id) is null ? Problems.NotFound() : Results.Ok(await assets.GetVolumes(CurrentAccount.Id(user), id)));

        g.MapPut("/{id:guid}/volumes", async (ClaimsPrincipal user, Guid id, IReadOnlyList<Volume> volumes, AssetRepository assets) =>
        {
            var asset = await assets.Get(CurrentAccount.Id(user), id);
            if (asset is null) return Problems.NotFound();
            if (asset.Kind != AssetKind.Terrain) return Problems.Conflict("Only terrain carries volumes.");
            return Results.Ok(await assets.PutVolumes(id, volumes));
        });
    }
}
