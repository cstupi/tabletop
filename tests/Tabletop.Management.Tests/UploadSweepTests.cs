using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tabletop.Geometry;
using Tabletop.Management.Content.Assets;

namespace Tabletop.Management.Tests;

public class UploadSweepTests
{
    static CreateAssetRequest Mini(string name) =>
        new(name, AssetKind.Miniature, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray())), 10, "model/gltf-binary", 1, new Orientation(0, 0, 0), Base.Circle(1));

    [Fact]
    public async Task Deletes_staging_objects_and_unconfirmed_rows_older_than_an_hour()
    {
        var rows = new InMemoryRows();
        var assets = new InMemoryAssetRepository(rows);
        var blobs = new InMemoryBlobStore();
        var owner = Guid.CreateVersion7();
        rows.Accounts[owner] = new InMemoryRows.AccountRow { Name = "A" };
        var now = DateTimeOffset.UtcNow;

        var abandoned = await assets.Create(owner, Mini("abandoned"));
        rows.Blobs[(owner, abandoned.Hash)].CreatedAt = now.AddHours(-2);
        var recent = await assets.Create(owner, Mini("recent"));
        var confirmed = await assets.Create(owner, Mini("confirmed"));
        await assets.MarkUploaded(owner, confirmed.Hash);
        rows.Blobs[(owner, confirmed.Hash)].CreatedAt = now.AddHours(-2);

        blobs.Now = () => now.AddHours(-2);
        await blobs.Put(IBlobStore.StagingKey(owner, abandoned.Id), [1], "model/gltf-binary");
        await blobs.Put(IBlobStore.HeldKey(owner, abandoned.Id), [1], "model/gltf-binary");
        await blobs.Put(IBlobStore.Key(owner, confirmed.Hash), [1], "model/gltf-binary");
        blobs.Now = () => now.AddMinutes(-59);
        await blobs.Put(IBlobStore.StagingKey(owner, recent.Id), [1], "model/gltf-binary");

        var swept = await new UploadSweep(assets, blobs).Run(now);

        Assert.Equal(new UploadSweep.Result(2, 1), swept);
        Assert.Equal([IBlobStore.Key(owner, confirmed.Hash), IBlobStore.StagingKey(owner, recent.Id)], blobs.Keys);
        Assert.Null(await assets.Get(owner, abandoned.Id));
        Assert.NotNull(await assets.Get(owner, recent.Id));
        Assert.NotNull(await assets.Get(owner, confirmed.Id));
    }

    [Fact]
    public void The_sweeper_is_a_hosted_service_every_ten_minutes()
    {
        using var f = new ManagementFactory();
        Assert.Contains(f.Services.GetServices<IHostedService>(), s => s is UploadSweeper);
        Assert.Equal(TimeSpan.FromMinutes(10), UploadSweeper.Interval);
        Assert.Equal(TimeSpan.FromHours(1), UploadSweep.Age);
    }
}
