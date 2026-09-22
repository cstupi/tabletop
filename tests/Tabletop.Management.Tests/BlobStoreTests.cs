using Microsoft.Extensions.Configuration;
using Tabletop.Management.Configuration;
using Tabletop.Management.Content.Assets;

namespace Tabletop.Management.Tests;

public class BlobStoreTests : IAsyncLifetime
{
    private readonly S3BlobStore _store;
    private readonly string _key = IBlobStore.Key(Guid.CreateVersion7(), "sha256-test");
    public BlobStoreTests() { DotEnv.Load(); _store = new S3BlobStore(ManagementOptions.From(new ConfigurationBuilder().AddEnvironmentVariables("TABLETOP_").AddInMemoryCollection(new Dictionary<string, string?> { ["STYTCH_PROJECT_ID"] = "p", ["STYTCH_SECRET"] = "s" }).Build())); }

    [Fact]
    public async Task Presigned_put_then_ranged_read_then_presigned_get()
    {
        var glb = TestGlb.Build((36, null, true));
        Assert.Null(await _store.Size(_key));
        using var http = new HttpClient();
        var put = await http.PutAsync(await _store.PresignUpload(_key, "model/gltf-binary", TimeSpan.FromMinutes(5)), new ByteArrayContent(glb) { Headers = { ContentType = new("model/gltf-binary") } });
        Assert.True(put.IsSuccessStatusCode, await put.Content.ReadAsStringAsync());
        Assert.Equal(glb.Length, await _store.Size(_key));
        var head = await _store.ReadRange(_key, 0, GlbHeader.HeaderBytes - 1);
        Assert.Equal(GlbHeader.HeaderBytes, head.Length);
        Assert.True(GlbHeader.IsGlb(head.AsSpan(0, 12)));
        var got = await http.GetByteArrayAsync(await _store.PresignDownload(_key, TimeSpan.FromMinutes(5)));
        Assert.Equal(glb, got);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => _store.Delete([_key]);
}
