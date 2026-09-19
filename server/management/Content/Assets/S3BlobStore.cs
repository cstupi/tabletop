using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Tabletop.Management.Configuration;

namespace Tabletop.Management.Content.Assets;

public sealed class S3BlobStore(ManagementOptions options) : IBlobStore
{
    private readonly AmazonS3Client _s3 = new(new BasicAWSCredentials(options.S3AccessKey, options.S3SecretKey),
        new AmazonS3Config { ServiceURL = options.S3Endpoint, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
    private readonly bool _http = options.S3Endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    public async Task<Uri> PresignUpload(string key, string contentType, long byteSize, TimeSpan ttl) => new(await _s3.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        { BucketName = options.S3Bucket, Key = key, Verb = HttpVerb.PUT, ContentType = contentType, Expires = DateTime.UtcNow.Add(ttl), Protocol = _http ? Amazon.S3.Protocol.HTTP : Amazon.S3.Protocol.HTTPS }));

    public async Task<Uri> PresignDownload(string key, TimeSpan ttl) => new(await _s3.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        { BucketName = options.S3Bucket, Key = key, Verb = HttpVerb.GET, Expires = DateTime.UtcNow.Add(ttl), Protocol = _http ? Amazon.S3.Protocol.HTTP : Amazon.S3.Protocol.HTTPS }));

    public async Task<long?> Size(string key)
    {
        try { return (await _s3.GetObjectMetadataAsync(options.S3Bucket, key)).ContentLength; }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound) { return null; }
    }

    public async Task<byte[]> ReadRange(string key, long first, long lastInclusive)
    {
        using var response = await _s3.GetObjectAsync(new GetObjectRequest { BucketName = options.S3Bucket, Key = key, ByteRange = new ByteRange(first, lastInclusive) });
        using var ms = new MemoryStream();
        await response.ResponseStream.CopyToAsync(ms);
        return ms.ToArray();
    }

    public async Task Delete(IReadOnlyList<string> keys)
    {
        foreach (var chunk in keys.Chunk(1000))
            await _s3.DeleteObjectsAsync(new DeleteObjectsRequest { BucketName = options.S3Bucket, Objects = chunk.Select(k => new KeyVersion { Key = k }).ToList() });
    }
}
