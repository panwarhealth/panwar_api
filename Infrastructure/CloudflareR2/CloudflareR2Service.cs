using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Panwar.Api.Infrastructure.CloudflareR2;

/// <summary>
/// Single-bucket R2 client for placement artwork. The bucket is private —
/// no custom domain attached, no public R2.dev URL. Browsers never fetch
/// artwork directly. The API mints short-lived presigned URLs after verifying
/// the requesting client owns the placement; those URLs go through the raw
/// R2 S3 endpoint and expire in 15 minutes.
/// </summary>
public class CloudflareR2Service : ICloudflareR2Service
{
    private readonly IAmazonS3 _s3Client;
    private readonly ILogger<CloudflareR2Service> _logger;
    private readonly string _bucket;

    public CloudflareR2Service(IConfiguration configuration, ILogger<CloudflareR2Service> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var accountId = configuration["CLOUDFLARE_R2_ACCOUNT_ID"]
            ?? throw new InvalidOperationException("CLOUDFLARE_R2_ACCOUNT_ID not configured");
        var accessKey = configuration["CLOUDFLARE_R2_ACCESS_KEY"]
            ?? throw new InvalidOperationException("CLOUDFLARE_R2_ACCESS_KEY not configured");
        var secretKey = configuration["CLOUDFLARE_R2_SECRET_KEY"]
            ?? throw new InvalidOperationException("CLOUDFLARE_R2_SECRET_KEY not configured");
        _bucket = configuration["CLOUDFLARE_R2_BUCKET"]
            ?? throw new InvalidOperationException("CLOUDFLARE_R2_BUCKET not configured");

        var credentials = new BasicAWSCredentials(accessKey, secretKey);
        var config = new AmazonS3Config
        {
            ServiceURL = $"https://{accountId}.r2.cloudflarestorage.com",
            ForcePathStyle = true
        };

        _s3Client = new AmazonS3Client(credentials, config);
    }

    public Task<(string uploadUrl, string objectKey)> GenerateUploadUrlAsync(string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        var sanitized = SanitizeFileName(fileName);
        var key = $"placements/{Guid.NewGuid()}_{sanitized}";

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = key,
            Verb = HttpVerb.PUT,
            Expires = DateTime.UtcNow.AddMinutes(15),
            ContentType = contentType
        };

        var uploadUrl = _s3Client.GetPreSignedURL(request);
        return Task.FromResult((uploadUrl, key));
    }

    public Task<string> GenerateDownloadUrlAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(15)
        };

        return Task.FromResult(_s3Client.GetPreSignedURL(request));
    }

    public async Task<bool> DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        try
        {
            await _s3Client.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = _bucket,
                Key = objectKey
            }, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete R2 object {Key}", objectKey);
            return false;
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
    }
}
