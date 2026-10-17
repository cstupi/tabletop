namespace Tabletop.Management.Content.Assets;

/// <summary>Bytes per owner. Content keys ({owner}/{hash}) are written only by the server; a user's presigned PUT names a staging
/// key (staging/{owner}/{assetId}), which confirm verifies and copies to the content key.</summary>
public interface IBlobStore
{
    const string StagingPrefix = "staging/";
    static string Key(Guid ownerId, string hash) => $"{ownerId}/{hash}";
    /// <summary>Where a user's presigned PUT for this asset lands.</summary>
    static string StagingKey(Guid ownerId, Guid assetId) => $"{StagingPrefix}{ownerId}/{assetId}";
    /// <summary>The server's own copy of a staged upload while confirm verifies it; no presigned URL names it.</summary>
    static string HeldKey(Guid ownerId, Guid assetId) => StagingKey(ownerId, assetId) + ".held";

    /// <summary>A server-side write, for bytes the server made itself (the starter miniatures).</summary>
    Task Put(string key, byte[] bytes, string contentType, CancellationToken ct = default);
    Task<Uri> PresignUpload(string key, string contentType, TimeSpan ttl);
    Task<Uri> PresignDownload(string key, TimeSpan ttl);
    Task<long?> Size(string key);
    /// <summary>The object as a stream with its length; null when absent.</summary>
    Task<BlobRead?> OpenRead(string key, CancellationToken ct = default);
    Task<byte[]> ReadRange(string key, long first, long lastInclusive);
    /// <summary>A server-side copy; the bytes do not pass through this process.</summary>
    Task Copy(string sourceKey, string destinationKey);
    /// <summary>Keys under the prefix last written before the cutoff.</summary>
    Task<IReadOnlyList<string>> ListOlderThan(string prefix, DateTimeOffset cutoff);
    Task Delete(IReadOnlyList<string> keys);
}

/// <summary>An open object: its bytes and length. Disposing closes the underlying response.</summary>
public sealed class BlobRead(Stream content, long length, IDisposable? owner = null) : IAsyncDisposable
{
    public Stream Content { get; } = content;
    public long Length { get; } = length;

    public async ValueTask DisposeAsync()
    {
        await Content.DisposeAsync();
        owner?.Dispose();
    }
}
