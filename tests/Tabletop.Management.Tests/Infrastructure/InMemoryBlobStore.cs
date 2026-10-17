using System.Collections.Concurrent;
using System.Net;
using System.Web;
using Tabletop.Management.Content.Assets;

namespace Tabletop.Management.Tests;

/// <summary>
/// Objects by key. A presigned URL names a host that does not resolve, the verb and, for a PUT, the signed content type;
/// Client() is an HttpClient that serves those URLs from this store, as MinIO serves S3BlobStore's. A PUT with another
/// content type is refused, as SigV4 refuses it. While Down, a server Put hangs until its token is cancelled, as a put to an
/// unreachable MinIO hangs until the SDK times out. Each object records when it was written, by Now.
/// </summary>
public sealed class InMemoryBlobStore : IBlobStore
{
    public const string Host = "blobs.invalid";
    private readonly ConcurrentDictionary<string, byte[]> _objects = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _written = new();

    public bool Down { get; set; }
    /// <summary>The clock a write is stamped with.</summary>
    public Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>Every key, for tests that check what is left.</summary>
    public IReadOnlyList<string> Keys => _objects.Keys.Order().ToList();

    private void Write(string key, byte[] bytes)
    {
        _objects[key] = bytes;
        _written[key] = Now();
    }

    public async Task Put(string key, byte[] bytes, string contentType, CancellationToken ct = default)
    {
        if (Down) await Task.Delay(Timeout.Infinite, ct);
        Write(key, bytes.ToArray());
    }

    public Task<Uri> PresignUpload(string key, string contentType, TimeSpan ttl) =>
        Task.FromResult(new Uri($"http://{Host}/{key}?verb=PUT&content-type={HttpUtility.UrlEncode(contentType)}"));

    public Task<Uri> PresignDownload(string key, TimeSpan ttl) => Task.FromResult(new Uri($"http://{Host}/{key}?verb=GET"));

    public Task<long?> Size(string key) => Task.FromResult(_objects.TryGetValue(key, out var b) ? (long?)b.Length : null);

    public Task<BlobRead?> OpenRead(string key, CancellationToken ct = default) =>
        Task.FromResult(_objects.TryGetValue(key, out var b) ? new BlobRead(new MemoryStream(b, writable: false), b.Length) : null);

    public Task Copy(string sourceKey, string destinationKey)
    {
        Write(destinationKey, _objects.TryGetValue(sourceKey, out var b) ? b : throw new KeyNotFoundException($"no object {sourceKey}"));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListOlderThan(string prefix, DateTimeOffset cutoff) =>
        Task.FromResult<IReadOnlyList<string>>(_written.Where(w => w.Key.StartsWith(prefix, StringComparison.Ordinal) && w.Value < cutoff && _objects.ContainsKey(w.Key)).Select(w => w.Key).ToList());

    /// <summary>As an S3 ranged GET: the range is clamped to the object; a first byte past its end is a 416.</summary>
    public Task<byte[]> ReadRange(string key, long first, long lastInclusive)
    {
        var bytes = _objects.TryGetValue(key, out var b) ? b : throw new KeyNotFoundException($"no object {key}");
        if (first >= bytes.Length) throw new IOException($"416: {key} has {bytes.Length} bytes");
        return Task.FromResult(bytes[(int)first..(int)Math.Min(lastInclusive + 1, bytes.Length)]);
    }

    public Task Delete(IReadOnlyList<string> keys)
    {
        foreach (var k in keys) { _objects.TryRemove(k, out _); _written.TryRemove(k, out _); }
        return Task.CompletedTask;
    }

    public HttpClient Client() => new(new Presigned(this));

    private sealed class Presigned(InMemoryBlobStore store) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            if (uri.Host != Host) throw new HttpRequestException($"{uri.Host} is not the in-memory blob store");
            var key = uri.AbsolutePath.TrimStart('/');
            var query = HttpUtility.ParseQueryString(uri.Query);
            if (query["verb"] != request.Method.Method) return new HttpResponseMessage(HttpStatusCode.Forbidden);
            if (request.Method == HttpMethod.Put)
            {
                if (request.Content?.Headers.ContentType?.MediaType != query["content-type"]) return new HttpResponseMessage(HttpStatusCode.Forbidden);
                store.Write(key, await request.Content!.ReadAsByteArrayAsync(ct));
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return store._objects.TryGetValue(key, out var bytes)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
