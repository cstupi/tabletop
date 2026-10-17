using Microsoft.Extensions.Configuration;
using Tabletop.Management.Configuration;
using Tabletop.Management.Content.Assets;

namespace Tabletop.Management.Tests;

/// <summary>IBlobStore's contract, run against InMemoryBlobStore (unit) and S3BlobStore on MinIO (E2E).</summary>
public abstract class BlobStoreContract : IAsyncLifetime
{
    protected abstract IBlobStore Store { get; }
    /// <summary>What reaches the presigned URLs.</summary>
    protected abstract HttpClient Http { get; }
    private readonly string _key = IBlobStore.Key(Guid.CreateVersion7(), "sha256-test");

    [Fact]
    public async Task Presigned_put_then_ranged_read_then_presigned_get()
    {
        var glb = TestGlb.Build((36, null, true));
        Assert.Null(await Store.Size(_key));
        var put = await Http.PutAsync(await Store.PresignUpload(_key, "model/gltf-binary", TimeSpan.FromMinutes(5)), new ByteArrayContent(glb) { Headers = { ContentType = new("model/gltf-binary") } });
        Assert.True(put.IsSuccessStatusCode, await put.Content.ReadAsStringAsync());
        Assert.Equal(glb.Length, await Store.Size(_key));
        var head = await Store.ReadRange(_key, 0, GlbHeader.HeaderBytes - 1);
        Assert.Equal(GlbHeader.HeaderBytes, head.Length);
        Assert.True(GlbHeader.IsGlb(head.AsSpan(0, 12)));
        Assert.Equal(glb[^4..], await Store.ReadRange(_key, glb.Length - 4, glb.Length + 100));
        Assert.Equal(glb, await Http.GetByteArrayAsync(await Store.PresignDownload(_key, TimeSpan.FromMinutes(5))));
    }

    [Fact]
    public async Task A_put_is_bound_to_its_signed_content_type()
    {
        var url = await Store.PresignUpload(_key, "model/gltf-binary", TimeSpan.FromMinutes(5));
        var put = await Http.PutAsync(url, new ByteArrayContent([1, 2, 3]) { Headers = { ContentType = new("image/png") } });
        Assert.False(put.IsSuccessStatusCode);
        Assert.Null(await Store.Size(_key));
    }

    [Fact]
    public async Task A_server_put_is_read_back_and_delete_removes_it()
    {
        await Store.Put(_key, [1, 2, 3, 4], "model/gltf-binary");
        Assert.Equal(4, await Store.Size(_key));
        Assert.Equal([2, 3], await Store.ReadRange(_key, 1, 2));
        await Store.Delete([_key]);
        Assert.Null(await Store.Size(_key));
        await Store.Delete([_key]);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Store.Delete([_key]);
}

public sealed class InMemoryBlobStoreTests : BlobStoreContract
{
    private readonly InMemoryBlobStore _store = new();
    protected override IBlobStore Store => _store;
    protected override HttpClient Http => _store.Client();
}

[Trait("Category", "E2E")]
public sealed class S3BlobStoreTests : BlobStoreContract
{
    private static readonly HttpClient Shared = new();
    private readonly S3BlobStore _store;
    public S3BlobStoreTests() { DotEnv.Load(TestDatabase.RepoRoot); _store = new S3BlobStore(ManagementOptions.From(new ConfigurationBuilder().AddEnvironmentVariables("TABLETOP_").AddInMemoryCollection(new Dictionary<string, string?> { ["STYTCH_PROJECT_ID"] = "p", ["STYTCH_SECRET"] = "s" }).Build())); }
    protected override IBlobStore Store => _store;
    protected override HttpClient Http => Shared;
}

/// <summary>Presigning is a local computation: no MinIO.</summary>
public sealed class S3PresignTests
{
    [Fact]
    public async Task Presigned_urls_name_the_public_endpoint_when_one_is_set()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DB_CONNECTION"] = "x", ["S3_ENDPOINT"] = "http://minio:9000", ["S3_PUBLIC_ENDPOINT"] = "http://127.0.0.1:9000", ["S3_BUCKET"] = "b",
            ["S3_ACCESS_KEY"] = "k", ["S3_SECRET_KEY"] = "s", ["STYTCH_PROJECT_ID"] = "p", ["STYTCH_SECRET"] = "s",
        }).Build();
        var url = await new S3BlobStore(ManagementOptions.From(config)).PresignDownload("k", TimeSpan.FromMinutes(1));
        Assert.Equal("127.0.0.1", url.Host);
    }
}
