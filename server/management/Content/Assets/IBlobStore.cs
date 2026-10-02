namespace Tabletop.Management.Content.Assets;

/// <summary>Content-addressed bytes, per owner. Presigned URLs both ways; the server itself reads only ranges.</summary>
public interface IBlobStore
{
    static string Key(Guid ownerId, string hash) => $"{ownerId}/{hash}";
    /// <summary>A server-side write, for bytes the server made itself (the starter miniatures); uploads from a user go through PresignUpload.</summary>
    Task Put(string key, byte[] bytes, string contentType);
    Task<Uri> PresignUpload(string key, string contentType, TimeSpan ttl);
    Task<Uri> PresignDownload(string key, TimeSpan ttl);
    Task<long?> Size(string key);
    Task<byte[]> ReadRange(string key, long first, long lastInclusive);
    Task Delete(IReadOnlyList<string> keys);
}
