using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;

namespace Panwar.Api.Services.Edm;

public interface IEdmImageStore
{
    /// <summary>Uploads to the public eDM bucket and returns the URL email clients will load.</summary>
    Task<string> PutAsync(string key, byte[] bytes, string contentType, CancellationToken cancellationToken = default);
}

/// <summary>
/// eDM images have to be publicly fetchable by every inbox, so they live in a separate public R2
/// bucket rather than the private panwar-portals-media one. Credentials default to the shared
/// CLOUDFLARE_R2_* token; set EDM_R2_ACCESS_KEY / EDM_R2_SECRET_KEY if that token isn't scoped to it.
/// </summary>
public class EdmImageStore : IEdmImageStore
{
    private readonly IConfiguration _configuration;
    private AmazonS3Client? _client;

    public EdmImageStore(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<string> PutAsync(string key, byte[] bytes, string contentType, CancellationToken cancellationToken = default)
    {
        var bucket = Require("EDM_R2_BUCKET");
        var publicBase = Require("EDM_R2_PUBLIC_BASE_URL").TrimEnd('/');

        await Client().PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = new MemoryStream(bytes),
            ContentType = contentType,
            Headers = { CacheControl = "public, max-age=31536000, immutable" },
            // R2 doesn't accept the SDK's streaming-signature or trailing-checksum uploads.
            DisablePayloadSigning = true,
        }, cancellationToken);

        return $"{publicBase}/{key}";
    }

    private AmazonS3Client Client()
    {
        if (_client is not null) return _client;
        var accountId = Require("CLOUDFLARE_R2_ACCOUNT_ID");
        var accessKey = _configuration["EDM_R2_ACCESS_KEY"] ?? Require("CLOUDFLARE_R2_ACCESS_KEY");
        var secretKey = _configuration["EDM_R2_SECRET_KEY"] ?? Require("CLOUDFLARE_R2_SECRET_KEY");
        _client = new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey), new AmazonS3Config
        {
            ServiceURL = $"https://{accountId}.r2.cloudflarestorage.com",
            ForcePathStyle = true,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        });
        return _client;
    }

    private string Require(string key) =>
        _configuration[key] is { Length: > 0 } v ? v : throw new EdmValidationException($"Image hosting isn't configured ({key})");
}
