using System.Security.Claims;
using System.Security.Cryptography;
using Tabletop.Geometry;
using Tabletop.Management.Http;

namespace Tabletop.Management.Content.Assets;

public static class AssetEndpoints
{
    private static readonly TimeSpan UploadTtl = TimeSpan.FromMinutes(5), DownloadTtl = TimeSpan.FromMinutes(60);

    public static void MapAssets(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/assets").RequireAuthorization();

        // kind binds as a string: the minimal-API enum binder is case-sensitive, and the wire spelling is lowercase ("miniature").
        g.MapGet("", async (ClaimsPrincipal user, IAssetRepository assets, IBlobStore blobs, int? limit, Guid? after, string? kind) =>
        {
            AssetKind? k = null;
            if (kind is not null)
            {
                if (!Enum.TryParse<AssetKind>(kind, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed)) return Problems.Unprocessable("kind is miniature, terrain or texture.");
                k = parsed;
            }
            var page = Keyset.Parse(limit, after);
            var listed = await assets.List(CurrentAccount.Id(user), page, k);
            var items = new List<AssetDto>(listed.Count);
            foreach (var a in listed) items.Add(await WithThumbnail(blobs, a));
            return Results.Ok(Page<AssetDto>.Of(items, page.Limit, a => a.Id));
        });

        g.MapPost("", async (ClaimsPrincipal user, CreateAssetRequest r, IAssetRepository assets, IBlobStore blobs) =>
        {
            if (!Enum.IsDefined(r.Kind)) return Problems.Unprocessable("kind is miniature, terrain or texture.");
            if (Limits.Text(r.Name, "name", Limits.Name) is { } bad) return Problems.Unprocessable(bad);
            if (r.ByteSize <= 0 || !AssetRules.IsSha256(r.Hash)) return Problems.Unprocessable("byteSize and a 64-hex sha256 hash are required.");
            if (!AssetRules.ContentTypes[r.Kind].Contains(r.ContentType)) return Results.Problem(title: "Unsupported media type", detail: $"{r.Kind} accepts {string.Join(", ", AssetRules.ContentTypes[r.Kind])}. STL is not accepted; convert it first.", statusCode: StatusCodes.Status415UnsupportedMediaType);
            if (r.ByteSize > UploadLimits.Bytes(r.Kind)) return Problems.Unprocessable($"A {AssetRules.Kind(r.Kind)} file is at most {UploadLimits.Bytes(r.Kind) >> 20} MiB.");
            if ((r.Kind == AssetKind.Miniature) != (r.ModelledBase is not null)) return Problems.Unprocessable("A miniature carries a modelledBase; terrain and textures do not.");
            if (Placement(r.Scale, r.Orientation, r.ModelledBase) is { } unusable) return Problems.Unprocessable(unusable);
            var owner = CurrentAccount.Id(user);
            AssetDto asset;
            try { asset = await assets.Create(owner, r, new AssetQuota(UploadLimits.AccountAssets, UploadLimits.AccountBytes)); }
            catch (AssetQuotaExceeded e)
            {
                return Problems.Unprocessable(e.Which == AssetQuotaExceeded.Limit.Assets
                    ? $"An account holds at most {UploadLimits.AccountAssets} assets."
                    : $"An account holds at most {UploadLimits.AccountBytes >> 30} GiB of uploads.");
            }
            var url = asset.Uploaded ? null : await blobs.PresignUpload(IBlobStore.StagingKey(owner, asset.Id), r.ContentType, UploadTtl);
            return Results.Created($"/v1/assets/{asset.Id}", new CreateAssetResponse(asset, url));
        });

        g.MapGet("/{id:guid}", async (ClaimsPrincipal user, Guid id, IAssetRepository assets, IBlobStore blobs) =>
            await assets.Get(CurrentAccount.Id(user), id) is { } a ? Results.Ok(await WithThumbnail(blobs, a)) : Problems.NotFound());

        g.MapPut("/{id:guid}/thumbnail", PutThumbnail);

        g.MapPatch("/{id:guid}", async (ClaimsPrincipal user, Guid id, PatchAssetRequest p, IAssetRepository assets) =>
        {
            var owner = CurrentAccount.Id(user);
            var existing = await assets.Get(owner, id);
            if (existing is null) return Problems.NotFound();
            if (p.Name is not null && Limits.Text(p.Name, "name", Limits.Name) is { } bad) return Problems.Unprocessable(bad);
            if (p.ModelledBase is not null && existing.Kind != AssetKind.Miniature) return Problems.Unprocessable("A miniature carries a modelledBase; terrain and textures do not.");
            if (Placement(p.Scale ?? existing.Scale, p.Orientation ?? existing.Orientation, p.ModelledBase) is { } unusable) return Problems.Unprocessable(unusable);
            return await assets.Patch(owner, id, p) is { } a ? Results.Ok(a) : Problems.NotFound();
        });

        g.MapDelete("/{id:guid}", async (ClaimsPrincipal user, Guid id, IAssetRepository assets, IBlobStore blobs) =>
        {
            var owner = CurrentAccount.Id(user);
            if (!await Delete(owner, id, assets, blobs)) return Problems.NotFound();
            return Results.NoContent();
        });

        g.MapPost("/delete", async (ClaimsPrincipal user, BulkAssetsRequest r, IAssetRepository assets, IBlobStore blobs) =>
        {
            if (Limits.BulkIds(r.AssetIds) is { } bad) return Problems.Unprocessable(bad);
            var owner = CurrentAccount.Id(user);
            List<Guid> done = [], notFound = [];
            foreach (var id in r.AssetIds.Distinct())
            {
                if (await Delete(owner, id, assets, blobs)) done.Add(id); else notFound.Add(id);
            }
            return Results.Ok(new BulkAssetsResponse(done, notFound));
        });

        g.MapPost("/{id:guid}/confirm", Confirm);

        g.MapGet("/{id:guid}/download", async (ClaimsPrincipal user, Guid id, IAssetRepository assets, IBlobStore blobs) =>
        {
            var owner = CurrentAccount.Id(user);
            var asset = await assets.Get(owner, id);
            if (asset is null) return Problems.NotFound();
            if (!asset.Uploaded) return Problems.Conflict("Not confirmed yet.");
            if (await assets.BlobKey(owner, id) is not { } key) return Problems.NotFound();
            return Results.Ok(new DownloadResponse(await blobs.PresignDownload(key, DownloadTtl), DateTimeOffset.UtcNow.Add(DownloadTtl)));
        });

        g.MapGet("/{id:guid}/volumes", async (ClaimsPrincipal user, Guid id, IAssetRepository assets) =>
            await assets.Get(CurrentAccount.Id(user), id) is null ? Problems.NotFound() : Results.Ok(await assets.GetVolumes(CurrentAccount.Id(user), id)));

        g.MapPut("/{id:guid}/volumes", async (ClaimsPrincipal user, Guid id, IReadOnlyList<Volume> volumes, IAssetRepository assets) =>
        {
            var asset = await assets.Get(CurrentAccount.Id(user), id);
            if (asset is null) return Problems.NotFound();
            if (asset.Kind != AssetKind.Terrain) return Problems.Conflict("Only terrain carries volumes.");
            if (volumes.Count > UploadLimits.Volumes) return Problems.Unprocessable($"A terrain piece carries at most {UploadLimits.Volumes} volumes.");
            foreach (var v in volumes)
                if (VolumeProblem(v) is { } bad) return Problems.Unprocessable(bad);
            return Results.Ok(await assets.PutVolumes(id, volumes));
        });
    }

    /// <summary>Deletes the asset, its staging object, its file when no blob row names it any more, and its thumbnail when no asset
    /// names that.</summary>
    private static async Task<bool> Delete(Guid owner, Guid id, IAssetRepository assets, IBlobStore blobs)
    {
        var thumbnail = (await assets.Get(owner, id))?.ThumbnailKey;
        var (found, orphaned) = await assets.Delete(owner, id);
        if (!found) return false;
        List<string> keys = [IBlobStore.StagingKey(owner, id)];
        if (orphaned is not null) keys.Add(orphaned);
        if (thumbnail is not null) keys.AddRange(await assets.Unreferenced([thumbnail]));
        await blobs.Delete(keys);
        return true;
    }

    private static async Task<AssetDto> WithThumbnail(IBlobStore blobs, AssetDto a) =>
        a.ThumbnailKey is null ? a : a with { Thumbnail = await ThumbnailLimits.Url(blobs, a.ThumbnailKey) };

    /// <summary>The uploader's browser renders the thumbnail: a PNG or WebP of a mesh asset with its bytes uploaded, checked by its header
    /// and stored at a key of its own; the picture it replaces goes unless a copy names it.</summary>
    private static async Task<IResult> PutThumbnail(ClaimsPrincipal user, Guid id, HttpRequest request, IAssetRepository assets, IBlobStore blobs)
    {
        var type = request.ContentType?.Split(';')[0].Trim().ToLowerInvariant();
        if (type is null || !ThumbnailLimits.ContentTypes.Contains(type))
            return Results.Problem(title: "Unsupported media type", detail: "A thumbnail is image/png or image/webp.", statusCode: StatusCodes.Status415UnsupportedMediaType);
        var owner = CurrentAccount.Id(user);
        var asset = await assets.Get(owner, id);
        if (asset is null) return Problems.NotFound();
        if (!asset.Uploaded) return Problems.Conflict("Not confirmed yet.");
        if (asset.Kind == AssetKind.Texture) return Problems.Conflict("A texture is its own picture.");
        using var buffer = new MemoryStream();
        var chunk = new byte[16384];
        for (int n; (n = await request.Body.ReadAsync(chunk)) > 0;)
        {
            buffer.Write(chunk, 0, n);
            if (buffer.Length > ThumbnailLimits.Bytes) return Problems.Unprocessable($"A thumbnail is at most {ThumbnailLimits.Bytes >> 10} KiB.");
        }
        var bytes = buffer.ToArray();
        var info = await ImageHeader.Read((first, last) => Task.FromResult(bytes[(int)first..(int)Math.Min(last + 1, bytes.Length)]), 0, bytes.Length);
        if (info is null || info.ContentType != type) return Problems.Unprocessable($"The body is not a {type} image.");
        if (info.Width is <= 0 or > ThumbnailLimits.Side || info.Height is <= 0 or > ThumbnailLimits.Side)
            return Problems.Unprocessable($"A thumbnail is at most {ThumbnailLimits.Side} × {ThumbnailLimits.Side} pixels.");
        // A fresh key per picture: a copy names the object it was made with, which stays as it is until no asset names it.
        var key = IBlobStore.ThumbnailKey(id, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        await blobs.Put(key, bytes, type);
        var (found, previous) = await assets.SetThumbnail(owner, id, key);
        if (previous is not null && previous != key) await blobs.Delete(await assets.Unreferenced([previous]));
        if (found) return Results.NoContent();
        await blobs.Delete(await assets.Unreferenced([key]));
        return Problems.NotFound();
    }

    /// <summary>The staged bytes are copied to a key no URL names, hashed and checked there, and only then copied to the content
    /// key: a PUT to the staging URL during confirm cannot change what is checked or what is kept.</summary>
    private static async Task<IResult> Confirm(ClaimsPrincipal user, Guid id, IAssetRepository assets, IBlobStore blobs, ILoggerFactory logs)
    {
        var owner = CurrentAccount.Id(user);
        var asset = await assets.Get(owner, id);
        if (asset is null) return Problems.NotFound();
        var (staging, held, content) = (IBlobStore.StagingKey(owner, id), IBlobStore.HeldKey(owner, id), IBlobStore.Key(owner, asset.Hash));
        var glb = asset.ContentType == "model/gltf-binary";
        if (asset.Uploaded)
        {
            await blobs.Delete([staging]);
            // The blob row's key: a copied blob names the sharer's object, not {owner}/{hash}.
            var key = await assets.BlobKey(owner, id) ?? content;
            return Results.Ok(new ConfirmResponse(asset, glb ? await Triangles(blobs, key, asset.ByteSize) : null, null));
        }

        var size = await blobs.Size(staging);
        if (size is null) return Problems.Conflict("The bytes have not been uploaded yet.");
        if (size != asset.ByteSize || size > UploadLimits.Bytes(asset.Kind))
        {
            await blobs.Delete([staging]);
            return Problems.Unprocessable($"Uploaded {size} bytes, expected {asset.ByteSize}.");
        }
        await blobs.Copy(staging, held);
        await blobs.Delete([staging]);
        long? triangles = null;
        try
        {
            if (await Sha256(blobs, held, asset.ByteSize) != asset.Hash) return Problems.Unprocessable("The uploaded bytes do not match the declared hash.");
            RangeReader read = (first, last) => blobs.ReadRange(held, first, last);
            var check = glb ? await GlbHeader.Check(asset.ByteSize, read) : await ImageHeader.CheckTexture(asset.ContentType, asset.ByteSize, read);
            if (check.Rejection is not null)
            {
                logs.CreateLogger("Tabletop.Management.Assets").LogInformation("Refused upload {AssetId} of {Owner}: {Detail}", id, owner, check.Detail);
                return Problems.Unprocessable(check.Rejection);
            }
            triangles = check.Triangles;
            await blobs.Copy(held, content);
        }
        finally { await blobs.Delete([held]); }
        if (!await assets.MarkUploaded(owner, asset.Hash))
        {
            // The sweep deleted the rows while this confirm ran; the content object goes too unless a copy's blob row names it.
            await blobs.Delete(await assets.Unreferenced([content]));
            return Problems.Conflict("The upload was abandoned before it was confirmed; create the asset again.");
        }
        var warning = triangles > UploadLimits.WarnTriangles ? $"{triangles:N0} triangles is above the {UploadLimits.WarnTriangles:N0} this table renders comfortably; every participant downloads it." : null;
        return Results.Ok(new ConfirmResponse((await assets.Get(owner, id))!, triangles, warning));
    }

    /// <summary>Lowercase hex, or null when the object is not exactly length bytes.</summary>
    private static async Task<string?> Sha256(IBlobStore blobs, string key, long length)
    {
        await using var read = await blobs.OpenRead(key);
        if (read is null || read.Length != length) return null;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        for (int n; (n = await read.Content.ReadAsync(buffer)) > 0;)
        {
            total += n;
            if (total > length) return null;
            hash.AppendData(buffer, 0, n);
        }
        return total == length ? Convert.ToHexStringLower(hash.GetHashAndReset()) : null;
    }

    /// <summary>The triangle count of an already-confirmed GLB, for a deduplicated confirm; null if it cannot be read.</summary>
    private static async Task<long?> Triangles(IBlobStore blobs, string key, long length)
    {
        try { return (await GlbHeader.Check(length, (first, last) => blobs.ReadRange(key, first, last))).Triangles; }
        catch (Exception) { return null; }
    }

    private static string? Placement(double scale, Orientation o, Base? b)
    {
        if (!double.IsFinite(scale) || scale <= 0) return "scale is a finite number above zero.";
        if (!double.IsFinite(o.Pitch) || !double.IsFinite(o.Yaw) || !double.IsFinite(o.Roll)) return "orientation angles are finite numbers.";
        if (b is { } mb && (!Enum.IsDefined(mb.Shape) || !double.IsFinite(mb.A) || !double.IsFinite(mb.B) || mb.A <= 0 || mb.B <= 0)) return "modelledBase is circle, oval or rect with finite sizes above zero.";
        return null;
    }

    private const TerrainDeclarations AllDeclarations = TerrainDeclarations.Impassable | TerrainDeclarations.BlocksLineOfSight | TerrainDeclarations.SeeInAndOut
        | TerrainDeclarations.Cover | TerrainDeclarations.Difficult | TerrainDeclarations.Dangerous | TerrainDeclarations.DangerousOnRushCharge | TerrainDeclarations.Steep;

    private static string? VolumeProblem(Volume? v)
    {
        if (v is null) return "A volume is required.";
        if (!Enum.IsDefined(v.Kind)) return "A volume's kind is solid or area.";
        if ((v.Declarations & ~AllDeclarations) != 0) return "A volume declares only the eight terrain declarations.";
        if (!v.Footprint.IsValid(1)) return "A volume's footprint is a circle or rect with finite sizes above zero.";
        if (!double.IsFinite(v.Offset.X) || !double.IsFinite(v.Offset.Y) || !double.IsFinite(v.Yaw.Degrees) || !double.IsFinite(v.Bottom) || !double.IsFinite(v.Top))
            return "A volume's offset, yaw, bottom and top are finite numbers.";
        return Limits.Text(v.Notes, "notes", Limits.Notes, required: false, multiline: true);
    }
}
