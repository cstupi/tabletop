using System.Security.Cryptography;

namespace Tabletop.Client.Core;

public sealed class HashMismatchException(string expected, string actual) : Exception($"Downloaded bytes hash to {actual}, expected {expected}.")
{
    public string Expected { get; } = expected;
    public string Actual { get; } = actual;
}

public sealed class AssetTooLargeException(long cap) : Exception($"Download exceeds the {cap}-byte cap.")
{
    public long Cap { get; } = cap;
}

/// <summary>Blobs by SHA-256 on disk. Fetched once per machine; the same mesh in two games is one download.</summary>
public sealed class AssetCache(string root, HttpClient http)
{
    readonly SemaphoreSlim _gate = new(4, 4);

    public string PathFor(string hash) => Path.Combine(root, $"{hash}.bin");
    public bool Has(string hash) => File.Exists(PathFor(hash));

    public const long MeshCap = 64L << 20, TextureCap = 16L << 20;

    /// <summary>The download cap for an asset kind; an unknown kind gets the mesh cap.</summary>
    public static long CapFor(AssetKind? kind) => kind == AssetKind.Texture ? TextureCap : MeshCap;

    public Task<string> FetchAsync(string hash, Uri url, CancellationToken ct) => FetchAsync(hash, url, MeshCap, ct);

    /// <summary>Downloads unless cached. Over <paramref name="maxBytes"/>, by Content-Length or by the bytes actually streamed,
    /// it throws <see cref="AssetTooLargeException"/> and keeps nothing.</summary>
    public async Task<string> FetchAsync(string hash, Uri url, long maxBytes, CancellationToken ct)
    {
        var path = PathFor(hash);
        if (File.Exists(path)) return path;
        Directory.CreateDirectory(root);
        var part = Path.Combine(root, $"{hash}.{Guid.CreateVersion7():N}.part");
        await _gate.WaitAsync(ct);
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > maxBytes) throw new AssetTooLargeException(maxBytes);
            using var sha = SHA256.Create();
            await using (var file = File.Create(part))
            await using (var hashing = new CryptoStream(file, sha, CryptoStreamMode.Write))
            await using (var body = await response.Content.ReadAsStreamAsync(ct))
            {
                var buffer = new byte[81920];
                long total = 0;
                for (int n; (n = await body.ReadAsync(buffer, ct)) > 0;)
                {
                    total += n;
                    if (total > maxBytes) throw new AssetTooLargeException(maxBytes);   // a missing or lying Content-Length
                    await hashing.WriteAsync(buffer.AsMemory(0, n), ct);
                }
            }
            var actual = Convert.ToHexStringLower(sha.Hash!);
            if (actual != hash) { File.Delete(part); throw new HashMismatchException(hash, actual); }
            try { if (!File.Exists(path)) File.Move(part, path); }
            catch (IOException) when (File.Exists(path)) { }   // another call for this hash won the move; its bytes hashed to the same value
            finally { if (File.Exists(part)) File.Delete(part); }
            return path;
        }
        catch (Exception) when (File.Exists(part)) { File.Delete(part); throw; }
        finally { _gate.Release(); }
    }

    public static string Sha256Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
