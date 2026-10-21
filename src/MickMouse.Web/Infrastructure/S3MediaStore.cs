using Amazon.S3;
using Amazon.S3.Model;

namespace MickMouse.Web.Infrastructure;

public sealed class S3MediaStore(IAmazonS3 client, string bucket) : IMediaStore
{
    public async Task<string> SaveAsync(Stream source, string originalName, string contentType, CancellationToken ct = default)
    {
        await using var upload = await MediaValidation.ReadUploadAsync(source, originalName, contentType, ct);
        var key = "uploads/" + Guid.NewGuid().ToString("N") + Path.GetExtension(originalName).ToLowerInvariant();
        await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = upload,
            AutoCloseStream = false,
            ContentType = MediaValidation.NormalizeContentType(contentType),
            ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
        }, ct);
        return key;
    }

    public async Task<string> GetPlaybackUrlAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        StorageKey.Validate(key);
        return await client.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Protocol = Protocol.HTTPS,
            Expires = DateTime.UtcNow.AddMinutes(30),
            ResponseHeaderOverrides = new ResponseHeaderOverrides { ContentDisposition = "inline" }
        });
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        StorageKey.Validate(key);
        if (key.StartsWith("samples/", StringComparison.Ordinal)) return;
        await client.DeleteObjectAsync(new DeleteObjectRequest { BucketName = bucket, Key = key }, ct);
    }
}
